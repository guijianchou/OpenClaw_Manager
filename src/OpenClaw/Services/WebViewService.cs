// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace OpenClaw.Services;

/// <summary>
/// Manages WebView2 lifecycle, navigation, and connection state monitoring.
/// </summary>
public partial class WebViewService : IDisposable
{
    private WebView2? _webView;
    private CoreWebView2? _coreWebView;
    private bool _isInitialized;
    private string? _lastNavigatedUrl;
    private int _retryCount;
    private NavigationCancellationScope? _navigationCancellation;
    private CancellationTokenSource? _navigationStartWatchdogCts;
    private CancellationTokenSource? _navigationCompletionWatchdogCts;
    private string? _lastLifecycleLogKey;
    private const int MaxRetries = 3;
    private const int PageTokenCaptureRetryAttempts = 3;
    private const ulong NoCurrentNavigationId = ulong.MaxValue;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan NavigationStartTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan NavigationCompletionTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PageTokenCaptureRetryDelay = TimeSpan.FromMilliseconds(250);
    private readonly IAppLogger _logger;
    private readonly UiTaskDispatcher _uiDispatcher;
    private readonly WebViewGenerationTracker _generations;
    private readonly WebViewStatusInspector _statusInspector;
    private readonly WebViewMessageOwnership _messageOwnership;
    private readonly object _navigationStartWatchdogGate = new();
    private readonly object _navigationCompletionWatchdogGate = new();
    private TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs>? _navigationStartingHandler;
    private TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs>? _navigationCompletedHandler;
    private TypedEventHandler<CoreWebView2, CoreWebView2ProcessFailedEventArgs>? _processFailedHandler;
    private TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs>? _webMessageReceivedHandler;
    private bool _isDisposed;
    private ulong _currentNavigationId = NoCurrentNavigationId;
    private ulong _activeNavigationCompletionWatchdogId = NoCurrentNavigationId;
    private int _hostGeneration;

    internal WebViewService(
        IAppLogger logger,
        WebViewMessageOwnership messageOwnership,
        Func<Action, bool> dispatchToUi)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _messageOwnership = messageOwnership ?? throw new ArgumentNullException(nameof(messageOwnership));
        _uiDispatcher = new UiTaskDispatcher(dispatchToUi);
        _generations = new WebViewGenerationTracker();
        _statusInspector = new WebViewStatusInspector(GetCoreWebView, _uiDispatcher, _generations, _messageOwnership, _logger);
        _heartbeatRuntime = new HeartbeatRuntime(_logger);
        _heartbeatTransport = new GatewayHeartbeatTransport();
        _hostedSessionHeartbeatPolicy = new HostedSessionHeartbeatPolicy();
        _statusInspector.SnapshotUpdated += snapshot => ApplyControlUiSnapshot(snapshot, raiseIssueEvent: false);
    }

    /// <summary>
    /// Raised when the connection/loading state changes.
    /// </summary>
    public event Action<ConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// Raised when a navigation error occurs.
    /// </summary>
    public event Action<string>? NavigationErrorOccurred;

    /// <summary>
    /// Raised when navigation completed successfully.
    /// </summary>
    public event Action<string?>? NavigationCompleted;

    /// <summary>
    /// Raised when CoreWebView2 accepts a navigation request but never reports a navigation event.
    /// </summary>
    public event Action<string>? NavigationStartTimedOut;

    /// <summary>
    /// Raised when WebView2 starts navigation but never reports a completion event.
    /// </summary>
    public event Action<string>? NavigationCompletionTimedOut;

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    public ConnectionState CurrentState { get; private set; } = ConnectionState.Offline;

    /// <summary>
    /// Gets whether the WebView2 control is initialized and ready.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the environment profile currently backing the active WebView2 instance.
    /// </summary>
    public string? CurrentEnvironmentName { get; private set; }

    /// <summary>
    /// Initializes the WebView2 control with a custom user data folder.
    /// </summary>
    public async Task InitializeAsync(WebView2 webView, string environmentName, CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            return;
        }

        DetachCurrentWebView();
        _webView = webView;
        _coreWebView = null;
        var initializationGeneration = _generations.Next();
        _messageOwnership.ResetForNewWebView();
        CurrentEnvironmentName = environmentName;
        _isInitialized = false;

        try
        {
            var userDataFolder = GetUserDataFolderForEnvironment(environmentName);
            Directory.CreateDirectory(userDataFolder);

            // In WinUI 3, set user data folder via environment variable before initialization.
            // This avoids API signature differences between WinUI 3 and Win32 WebView2.
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", userDataFolder);

            await webView.EnsureCoreWebView2Async();
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrentInitialization(webView, initializationGeneration))
            {
                return;
            }

            var coreWebView = TryGetCoreWebView2(webView);
            if (coreWebView is null)
            {
                throw new InvalidOperationException("CoreWebView2 became unavailable immediately after initialization.");
            }

            if (!IsCurrentInitialization(webView, initializationGeneration))
            {
                return;
            }

            _coreWebView = coreWebView;

            // Make WebView2 follow system Light/Dark theme preferred scheme
            coreWebView.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;

            // Set default background to transparent (blends with Mica)
            webView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;

            var hostGeneration = _hostGeneration;
            _navigationStartingHandler = CreateNavigationStartingHandler(hostGeneration);
            _navigationCompletedHandler = CreateNavigationCompletedHandler(hostGeneration);
            _processFailedHandler = CreateProcessFailedHandler(hostGeneration);
            _webMessageReceivedHandler = CreateWebMessageReceivedHandler(hostGeneration);

            coreWebView.NavigationStarting += _navigationStartingHandler;
            coreWebView.NavigationCompleted += _navigationCompletedHandler;
            coreWebView.ProcessFailed += _processFailedHandler;
            coreWebView.WebMessageReceived += _webMessageReceivedHandler;

            // Allow file input dialog
            coreWebView.Settings.AreDefaultContextMenusEnabled = true;
            coreWebView.Settings.IsStatusBarEnabled = false;
            coreWebView.Settings.AreDevToolsEnabled = true;

            coreWebView.Settings.IsGeneralAutofillEnabled = true;

            _isInitialized = true;
            _logger.Info("WebView2 initialized successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Info("WebView2 initialization cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Error($"WebView2 initialization failed: {ex}");
            SetState(ConnectionState.Error);
            NavigationErrorOccurred?.Invoke($"WebView2 initialization failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Navigates the WebView2 to the specified URL.
    /// </summary>
    public void Navigate(string url)
    {
        var coreWebView = GetCoreWebView();
        if (!_isInitialized || coreWebView is null)
        {
            _logger.Warning("Cannot navigate: WebView2 not initialized.");
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            _logger.Warning($"Invalid URL: {url}");
            NavigationErrorOccurred?.Invoke($"Invalid URL: {url}");
            return;
        }

        _logger.Info($"Navigating to: {url}");
        _lastNavigatedUrl = url;
        _retryCount = 0;
        var navigationGeneration = PrepareNavigationStart();
        SetState(ConnectionState.Loading);
        LogLifecycleEventOnce("navigation.start", new { url });
        if (!TryNavigateCoreWebView(coreWebView, url, "Navigate"))
        {
            SetState(ConnectionState.Error);
            NavigationErrorOccurred?.Invoke("Navigation failed before WebView2 was ready.");
            return;
        }

        ObserveNavigationStartTimeout(navigationGeneration, url);
    }

    /// <summary>
    /// Reloads the current page.
    /// </summary>
    public bool Reload()
    {
        var coreWebView = GetCoreWebView();
        if (!_isInitialized || coreWebView is null)
        {
            _logger.Warning("Cannot reload: WebView2 not initialized.");
            SetState(ConnectionState.Error);
            NavigationErrorOccurred?.Invoke("Cannot reload: WebView2 not initialized.");
            return false;
        }

        _logger.Info("Reloading page.");
        var navigationGeneration = PrepareNavigationStart();
        SetState(ConnectionState.Loading);
        if (TryReloadCoreWebView(coreWebView))
        {
            ObserveNavigationStartTimeout(navigationGeneration, coreWebView.Source);
            return true;
        }

        SetState(ConnectionState.Error);
        NavigationErrorOccurred?.Invoke("Reload failed before WebView2 was ready.");
        return false;
    }

    /// <summary>
    /// Clears all browsing data (cookies, cache, local storage) from the WebView2 profile.
    /// </summary>
    public async Task ClearBrowsingDataAsync()
    {
        var coreWebView = GetCoreWebView();
        if (coreWebView is null)
        {
            return;
        }

        try
        {
            _logger.Info("Clearing browsing data.");
            await coreWebView.Profile.ClearBrowsingDataAsync();
            _logger.Info("Browsing data cleared.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to clear browsing data: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the WebView2 DevTools window.
    /// </summary>
    public void OpenDevTools()
    {
        var coreWebView = GetCoreWebView();
        if (coreWebView is null)
        {
            return;
        }

        try
        {
            coreWebView.OpenDevToolsWindow();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.Warning($"OpenDevTools skipped because CoreWebView2 became unavailable: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the session for a specific environment profile.
    /// </summary>
    public async Task ClearEnvironmentSessionAsync(string environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return;
        }

        var coreWebView = GetCoreWebView();
        if (coreWebView is not null &&
            _isInitialized &&
            string.Equals(CurrentEnvironmentName, environmentName, StringComparison.Ordinal))
        {
            _logger.Info($"Clearing active browsing data for environment '{environmentName}'.");
            await coreWebView.Profile.ClearBrowsingDataAsync();
            return;
        }

        await Task.Run(() => DeleteUserDataFolderForEnvironment(environmentName, _logger));
    }

    /// <summary>
    /// Retries the last navigation if within retry limits.
    /// Returns true if a retry was initiated.
    /// </summary>
    public bool RetryNavigation()
    {
        var coreWebView = GetCoreWebView();
        if (string.IsNullOrEmpty(_lastNavigatedUrl) || !_isInitialized || coreWebView is null)
        {
            return false;
        }

        _retryCount = 0; // manual retry resets counter
        _logger.Info($"Manual retry navigation to: {_lastNavigatedUrl}");
        var navigationGeneration = PrepareNavigationStart();
        SetState(ConnectionState.Loading);
        if (TryNavigateCoreWebView(coreWebView, _lastNavigatedUrl, "Manual retry"))
        {
            ObserveNavigationStartTimeout(navigationGeneration, _lastNavigatedUrl);
            return true;
        }

        SetState(ConnectionState.Error);
        NavigationErrorOccurred?.Invoke("Retry failed before WebView2 was ready.");
        return false;
    }

    /// <summary>
    /// Gets the current source URL of the WebView2.
    /// </summary>
    public string? GetCurrentUrl()
    {
        return GetCoreWebView()?.Source;
    }

    /// <summary>
    /// Gets whether the active WebView2 instance already uses the requested environment profile.
    /// </summary>
    public bool IsUsingEnvironmentProfile(string? environmentName)
    {
        return _isInitialized &&
            !string.IsNullOrWhiteSpace(environmentName) &&
            string.Equals(CurrentEnvironmentName, environmentName, StringComparison.Ordinal);
    }

    private TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs> CreateNavigationStartingHandler(int hostGeneration)
    {
        return (sender, args) => OnNavigationStarting(sender, args, hostGeneration);
    }

    private TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> CreateNavigationCompletedHandler(int hostGeneration)
    {
        return (sender, args) => OnNavigationCompleted(sender, args, hostGeneration);
    }

    private TypedEventHandler<CoreWebView2, CoreWebView2ProcessFailedEventArgs> CreateProcessFailedHandler(int hostGeneration)
    {
        return (sender, args) => OnProcessFailed(sender, args, hostGeneration);
    }

    private TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs> CreateWebMessageReceivedHandler(int hostGeneration)
    {
        return (sender, args) => OnWebMessageReceived(sender, args, hostGeneration);
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args, int hostGeneration)
    {
        try
        {
            if (!IsCurrentHost(hostGeneration))
            {
                return;
            }

            var message = args.WebMessageAsJson;
            using var document = System.Text.Json.JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!_messageOwnership.TryCaptureCurrentVersion(args, root, out var pageVersion))
            {
                return;
            }

            if (!_statusInspector.TryApplyHostMessage(message, pageVersion, out var snapshot))
            {
                return;
            }

            if (!_messageOwnership.IsCurrentAcceptedPageVersion(pageVersion))
            {
                return;
            }

            ApplyControlUiSnapshot(snapshot, raiseIssueEvent: true);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to process Control UI status message: {ex.Message}");
        }
    }

    // --- Event handlers ---

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args, int hostGeneration)
    {
        var coreWebView = _coreWebView;
        if (coreWebView is null || !IsCurrentHost(hostGeneration))
        {
            return;
        }

        CancelNavigationStartWatchdog();
        var navigationGeneration = PrepareNavigationStart();
        _currentNavigationId = args.NavigationId;
        _statusInspector.SetLoadingSnapshot(args.Uri);
        SetState(ConnectionState.Loading);
        ObserveNavigationCompletionTimeout(args.NavigationId, navigationGeneration, args.Uri);
        LogLifecycleEventOnce("navigation.starting", new { uri = args.Uri });
    }

    private async void OnNavigationCompleted(
        CoreWebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args,
        int hostGeneration)
    {
        try
        {
            await HandleNavigationCompletedAsync(sender, args, hostGeneration);
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
        }
        catch (ObjectDisposedException) when (_isDisposed)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                _logger.Warning($"Navigation completion handling failed: {ex.Message}");
            }
        }
    }

    private async Task HandleNavigationCompletedAsync(
        CoreWebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args,
        int hostGeneration)
    {
        if (!IsCurrentHost(hostGeneration) || _coreWebView is null)
        {
            return;
        }

        if (!TryClaimNavigationCompleted(sender, args, hostGeneration))
        {
            return;
        }

        CancelNavigationStartWatchdog();
        CancelNavigationCompletionWatchdog();
        if (args.IsSuccess)
        {
            _retryCount = 0;
            var completionGeneration = _generations.Current;
            var navigationCancellation = _navigationCancellation;
            if (navigationCancellation is null)
            {
                return;
            }

            var navigationLease = navigationCancellation.TryAcquire();
            if (navigationLease is null)
            {
                return;
            }

            try
            {
                var pageTokenAccepted = await CaptureCurrentPageTokenAsync(
                    sender,
                    args.NavigationId,
                    completionGeneration,
                    hostGeneration,
                    navigationLease.Token);
                if (!IsCurrentNavigation(args.NavigationId, completionGeneration, hostGeneration))
                {
                    return;
                }

                if (!pageTokenAccepted)
                {
                    ObservePageTokenCaptureRetry(sender, args.NavigationId, completionGeneration, hostGeneration, navigationCancellation);
                }
                else
                {
                    ObserveSessionReadyReportRequest(sender, args.NavigationId, completionGeneration, hostGeneration, navigationCancellation);
                }
            }
            finally
            {
                navigationLease.Dispose();
            }

            _statusInspector.SetPageLoadedSnapshot(sender.Source);
            StartStatusProbeLoop();
            LogLifecycleEventOnce("navigation.completed", new { uri = sender.Source });
            NavigationCompleted?.Invoke(sender.Source);
        }
        else
        {
            CancelStatusProbeLoop();
            _logger.Warning($"Navigation failed: {args.WebErrorStatus}");

            var isConnectionError = args.WebErrorStatus is
                CoreWebView2WebErrorStatus.ConnectionAborted or
                CoreWebView2WebErrorStatus.ConnectionReset or
                CoreWebView2WebErrorStatus.Disconnected or
                CoreWebView2WebErrorStatus.Timeout or
                CoreWebView2WebErrorStatus.ServerUnreachable or
                CoreWebView2WebErrorStatus.HostNameNotResolved;

            if (isConnectionError)
            {
                SetState(ConnectionState.Reconnecting);

                var autoRetryOutcome = await TryAutoRetryAfterConnectionErrorAsync(sender, args, hostGeneration);
                if (autoRetryOutcome == AutoRetryOutcome.Started)
                {
                    return; // don't fire error event for auto-retries
                }

                if (autoRetryOutcome == AutoRetryOutcome.Stale)
                {
                    return;
                }

                if (autoRetryOutcome == AutoRetryOutcome.Failed)
                {
                    SetState(ConnectionState.Error);
                    NavigationErrorOccurred?.Invoke("Auto-retry failed before WebView2 was ready.");
                    return;
                }
            }

            SetState(args.WebErrorStatus switch
            {
                CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or
                CoreWebView2WebErrorStatus.CertificateExpired or
                CoreWebView2WebErrorStatus.CertificateRevoked or
                CoreWebView2WebErrorStatus.CertificateIsInvalid => ConnectionState.AuthFailed,
                _ when isConnectionError => ConnectionState.Error,
                _ => ConnectionState.Error,
            });
            NavigationErrorOccurred?.Invoke($"Navigation error: {args.WebErrorStatus}");
        }
    }

    private bool TryClaimNavigationCompleted(
        CoreWebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args,
        int hostGeneration)
    {
        if (!IsCurrentHost(hostGeneration) || _coreWebView is null)
        {
            return false;
        }

        if (args.NavigationId == _currentNavigationId)
        {
            return true;
        }

        if (_currentNavigationId == NoCurrentNavigationId && HasActiveNavigationStartWatchdog())
        {
            CancelNavigationStartWatchdog();
            _currentNavigationId = args.NavigationId;
            LogLifecycleEventOnce(
                "navigation.starting.recovered_from_completion",
                new
                {
                    uri = sender.Source,
                    args.NavigationId
                });
            return true;
        }

        return false;
    }

    private async Task<AutoRetryOutcome> TryAutoRetryAfterConnectionErrorAsync(
        CoreWebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args,
        int hostGeneration)
    {
        if (_retryCount >= MaxRetries || string.IsNullOrEmpty(_lastNavigatedUrl))
        {
            return AutoRetryOutcome.NotAttempted;
        }

        _retryCount++;
        var retryGeneration = _generations.Current;
        var retryNavigationId = args.NavigationId;
        var navigationCancellation = _navigationCancellation;
        using var navigationLease = navigationCancellation?.TryAcquire();
        if (navigationLease is null)
        {
            return AutoRetryOutcome.Stale;
        }

        var token = navigationLease.Token;
        _logger.Info($"Auto-retry {_retryCount}/{MaxRetries} in {RetryDelay.TotalSeconds}s...");

        try
        {
            await Task.Delay(RetryDelay, token);
        }
        catch (TaskCanceledException)
        {
            _logger.Info("Auto-retry cancelled (new navigation started).");
            return AutoRetryOutcome.Stale;
        }

        var coreWebView = GetCoreWebView();
        if (_isDisposed ||
            !_generations.IsCurrent(retryGeneration) ||
            retryNavigationId != _currentNavigationId ||
            !IsCurrentHost(hostGeneration) ||
            coreWebView is null ||
            string.IsNullOrEmpty(_lastNavigatedUrl))
        {
            return AutoRetryOutcome.Stale;
        }

        var navigationGeneration = PrepareNavigationStart();
        if (!TryNavigateCoreWebView(coreWebView, _lastNavigatedUrl, "Auto-retry"))
        {
            return AutoRetryOutcome.Failed;
        }

        ObserveNavigationStartTimeout(navigationGeneration, _lastNavigatedUrl);
        return AutoRetryOutcome.Started;
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args, int hostGeneration)
    {
        if (!IsCurrentHost(hostGeneration) || _coreWebView is null)
        {
            return;
        }

        CancelStatusProbeLoop();
        CancelNavigationStartWatchdog();
        CancelNavigationCompletionWatchdog();
        CancelNavigationCancellation();
        InvalidateControlUiInspectionCache();
        _generations.Next();
        _messageOwnership.BeginNavigation();
        _statusInspector.SetUnavailableSnapshot("Browser process failed.");
        _logger.Error($"WebView2 process failed: {args.Reason} ({args.ProcessFailedKind})");
        SetState(ConnectionState.Error);
        NavigationErrorOccurred?.Invoke($"Browser process failed: {args.Reason}");
    }

    public void DetachCurrentWebViewHost()
    {
        if (_isDisposed)
        {
            return;
        }

        DetachCurrentWebView();
    }

    private void DetachCurrentWebView()
    {
        CancelStatusProbeLoop();
        StopHeartbeat();
        CancelNavigationStartWatchdog();
        CancelNavigationCompletionWatchdog();
        _hostGeneration++;
        _generations.Next();
        _messageOwnership.BeginNavigation();
        InvalidateControlUiInspectionCache();
        CancelNavigationCancellation();

        var coreWebView = GetCoreWebView();
        if (coreWebView is not null)
        {
            if (_navigationStartingHandler is not null)
            {
                coreWebView.NavigationStarting -= _navigationStartingHandler;
            }

            if (_navigationCompletedHandler is not null)
            {
                coreWebView.NavigationCompleted -= _navigationCompletedHandler;
            }

            if (_processFailedHandler is not null)
            {
                coreWebView.ProcessFailed -= _processFailedHandler;
            }

            if (_webMessageReceivedHandler is not null)
            {
                coreWebView.WebMessageReceived -= _webMessageReceivedHandler;
            }
        }

        _navigationStartingHandler = null;
        _navigationCompletedHandler = null;
        _processFailedHandler = null;
        _webMessageReceivedHandler = null;
        _webView = null;
        _coreWebView = null;
        _isInitialized = false;
        _statusInspector.SetUnknownSnapshot();
        _lastPublishedControlUiSnapshot = ControlUiProbeSnapshot.Unknown;
        _lastLifecycleLogKey = null;
    }

    public void Dispose()
    {
        _isDisposed = true;
        DetachCurrentWebView();
        CancelNavigationStartWatchdog();
        CancelNavigationCompletionWatchdog();
        CancelNavigationCancellation();
        _statusInspector.Dispose();
        _heartbeatRuntime.Dispose();
    }

    private static CoreWebView2? TryGetCoreWebView2(WebView2? webView)
    {
        if (webView is null)
        {
            return null;
        }

        try
        {
            return webView.CoreWebView2;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private CoreWebView2? GetCoreWebView()
    {
        if (_coreWebView is not null)
        {
            return _coreWebView;
        }

        _coreWebView = TryGetCoreWebView2(_webView);
        return _coreWebView;
    }

    private bool IsCurrentInitialization(WebView2 webView, int generation)
    {
        return !_isDisposed &&
            _generations.IsCurrent(generation) &&
            ReferenceEquals(webView, _webView);
    }

    private bool IsCurrentHost(int hostGeneration)
    {
        return !_isDisposed &&
            _coreWebView is not null &&
            _hostGeneration == hostGeneration;
    }

    private bool IsCurrentNavigation(ulong navigationId, int generation, int hostGeneration)
    {
        return !_isDisposed &&
            navigationId == _currentNavigationId &&
            _generations.IsCurrent(generation) &&
            IsCurrentHost(hostGeneration);
    }

    private int PrepareNavigationStart()
    {
        CancelNavigationStartWatchdog();
        CancelNavigationCompletionWatchdog();
        CancelStatusProbeLoop();
        InvalidateControlUiInspectionCache();
        var generation = _generations.Next();
        _currentNavigationId = NoCurrentNavigationId;
        _messageOwnership.BeginNavigation();
        ReplaceNavigationCancellation();
        _lastReportedIssueKey = null;
        _heartbeatConnectingCount = 0;
        _lastHeartbeatObservationKey = null;
        return generation;
    }

    private void CancelActiveNavigation()
    {
        CancelNavigationStartWatchdog();
        CancelNavigationCompletionWatchdog();
        CancelStatusProbeLoop();
        InvalidateControlUiInspectionCache();
        _generations.Next();
        _currentNavigationId = NoCurrentNavigationId;
        _messageOwnership.BeginNavigation();
        CancelNavigationCancellation();
        _lastReportedIssueKey = null;
    }

    private void ObserveNavigationStartTimeout(int navigationGeneration, string? url)
    {
        var cancellation = new CancellationTokenSource();
        lock (_navigationStartWatchdogGate)
        {
            _navigationStartWatchdogCts = cancellation;
        }

        _ = ObserveNavigationStartTimeoutAsync(navigationGeneration, url, cancellation);
    }

    private async Task ObserveNavigationStartTimeoutAsync(
        int navigationGeneration,
        string? url,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(NavigationStartTimeout, cancellation.Token).ConfigureAwait(false);
            await _uiDispatcher.RunAsync(
                new Action(() => HandleNavigationStartTimeout(navigationGeneration, url)),
                cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when WebView2 raises a navigation event, another navigation starts, or the app closes.
        }
        catch (ObjectDisposedException)
        {
            // Expected when WebView2 is torn down during shutdown or host recreation.
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                _logger.Warning($"Navigation start watchdog failed: {ex.Message}");
            }
        }
        finally
        {
            lock (_navigationStartWatchdogGate)
            {
                if (ReferenceEquals(_navigationStartWatchdogCts, cancellation))
                {
                    _navigationStartWatchdogCts = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void HandleNavigationStartTimeout(int navigationGeneration, string? url)
    {
        if (_isDisposed ||
            _coreWebView is null ||
            !_generations.IsCurrent(navigationGeneration) ||
            _currentNavigationId != NoCurrentNavigationId)
        {
            return;
        }

        var message = $"Navigation did not start within {NavigationStartTimeout.TotalSeconds:0.#}s.";
        _logger.Warning("navigation.start.timeout", new
        {
            url,
            timeoutSeconds = NavigationStartTimeout.TotalSeconds
        });
        _statusInspector.SetUnavailableSnapshot(message);
        CancelNavigationCancellation();
        SetState(ConnectionState.Reconnecting);
        NavigationStartTimedOut?.Invoke(message);
    }

    private void ObserveNavigationCompletionTimeout(
        ulong navigationId,
        int navigationGeneration,
        string? url)
    {
        var cancellation = new CancellationTokenSource();
        lock (_navigationCompletionWatchdogGate)
        {
            _navigationCompletionWatchdogCts = cancellation;
            _activeNavigationCompletionWatchdogId = navigationId;
        }

        _ = ObserveNavigationCompletionTimeoutAsync(navigationId, navigationGeneration, url, cancellation);
    }

    private async Task ObserveNavigationCompletionTimeoutAsync(
        ulong navigationId,
        int navigationGeneration,
        string? url,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(NavigationCompletionTimeout, cancellation.Token).ConfigureAwait(false);
            await _uiDispatcher.RunAsync(
                new Action(() => HandleNavigationCompletionTimeout(navigationId, navigationGeneration, url)),
                cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when WebView2 completes navigation, another navigation starts, or the app closes.
        }
        catch (ObjectDisposedException)
        {
            // Expected when WebView2 is torn down during shutdown or host recreation.
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                _logger.Warning($"Navigation completion watchdog failed: {ex.Message}");
            }
        }
        finally
        {
            lock (_navigationCompletionWatchdogGate)
            {
                if (ReferenceEquals(_navigationCompletionWatchdogCts, cancellation))
                {
                    _navigationCompletionWatchdogCts = null;
                    _activeNavigationCompletionWatchdogId = NoCurrentNavigationId;
                }
            }

            cancellation.Dispose();
        }
    }

    private void HandleNavigationCompletionTimeout(
        ulong navigationId,
        int navigationGeneration,
        string? url)
    {
        if (_isDisposed ||
            _coreWebView is null ||
            !_generations.IsCurrent(navigationGeneration) ||
            navigationId != _activeNavigationCompletionWatchdogId ||
            navigationId != _currentNavigationId)
        {
            return;
        }

        var message = $"Navigation did not complete within {NavigationCompletionTimeout.TotalSeconds:0.#}s.";
        _logger.Warning("navigation.completion.timeout", new
        {
            url,
            navigationId,
            timeoutSeconds = NavigationCompletionTimeout.TotalSeconds
        });
        _statusInspector.SetUnavailableSnapshot(message);
        CancelNavigationCancellation();
        SetState(ConnectionState.Reconnecting);
        NavigationCompletionTimedOut?.Invoke(message);
    }

    private void CancelNavigationStartWatchdog()
    {
        CancellationTokenSource? cancellation;

        lock (_navigationStartWatchdogGate)
        {
            cancellation = _navigationStartWatchdogCts;
            _navigationStartWatchdogCts = null;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The watchdog owns CTS disposal and may have completed while cancellation was requested.
        }
    }

    private bool HasActiveNavigationStartWatchdog()
    {
        lock (_navigationStartWatchdogGate)
        {
            return _navigationStartWatchdogCts is not null;
        }
    }

    private void CancelNavigationCompletionWatchdog()
    {
        CancellationTokenSource? cancellation;

        lock (_navigationCompletionWatchdogGate)
        {
            cancellation = _navigationCompletionWatchdogCts;
            _navigationCompletionWatchdogCts = null;
            _activeNavigationCompletionWatchdogId = NoCurrentNavigationId;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The watchdog owns CTS disposal and may have completed while cancellation was requested.
        }
    }

    private void ReplaceNavigationCancellation()
    {
        var previous = _navigationCancellation;
        _navigationCancellation = new NavigationCancellationScope();
        previous?.CancelAndRetire();
    }

    private void CancelNavigationCancellation()
    {
        var cancellation = _navigationCancellation;
        _navigationCancellation = null;
        cancellation?.CancelAndRetire();
    }

    private bool TryNavigateCoreWebView(CoreWebView2 coreWebView, string url, string context)
    {
        try
        {
            coreWebView.Navigate(url);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.Warning($"{context} skipped because CoreWebView2 became unavailable: {ex.Message}");
            return false;
        }
    }

    private bool TryReloadCoreWebView(CoreWebView2 coreWebView)
    {
        try
        {
            coreWebView.Reload();
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.Warning($"Reload skipped because CoreWebView2 became unavailable: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> CaptureCurrentPageTokenAsync(
        CoreWebView2 coreWebView,
        ulong navigationId,
        int generation,
        int hostGeneration,
        CancellationToken cancellationToken = default,
        bool logUnavailable = true)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linkedCancellation = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken)
                : null;
            var commandToken = linkedCancellation?.Token ?? timeout.Token;
            var raw = await coreWebView.ExecuteScriptAsync("window.__openClawHostBridge?.pageToken || ''")
                .AsTask(commandToken);
            if (_isDisposed ||
                !IsCurrentNavigation(navigationId, generation, hostGeneration))
            {
                return false;
            }

            var pageToken = System.Text.Json.JsonSerializer.Deserialize<string>(raw);
            if (!_messageOwnership.AcceptPageToken(coreWebView.Source, pageToken))
            {
                if (logUnavailable)
                {
                    _logger.Warning("WebView page token was unavailable after navigation.");
                }

                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when navigation changes, the WebView is detached, or the app is closing.
        }
        catch (OperationCanceledException)
        {
            if (logUnavailable)
            {
                _logger.Warning("Timed out while capturing WebView page token.");
            }
        }
        catch (ObjectDisposedException ex)
        {
            if (IsCurrentNavigation(navigationId, generation, hostGeneration))
            {
                _logger.Warning($"WebView page token capture was interrupted by disposed resource: {ex.ObjectName ?? ex.Message}");
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (logUnavailable)
            {
                _logger.Warning($"Failed to capture WebView page token: {ex.Message}");
            }
        }

        return false;
    }

    private void ObservePageTokenCaptureRetry(
        CoreWebView2 coreWebView,
        ulong navigationId,
        int generation,
        int hostGeneration,
        NavigationCancellationScope navigationCancellation)
    {
        var navigationLease = navigationCancellation.TryAcquire();
        if (navigationLease is null)
        {
            return;
        }

        _ = RetryPageTokenCaptureAsync(coreWebView, navigationId, generation, hostGeneration, navigationLease);
    }

    private async Task RetryPageTokenCaptureAsync(
        CoreWebView2 coreWebView,
        ulong navigationId,
        int generation,
        int hostGeneration,
        NavigationCancellationScope.Lease navigationLease)
    {
        try
        {
            var cancellationToken = navigationLease.Token;
            for (var attempt = 1; attempt <= PageTokenCaptureRetryAttempts; attempt++)
            {
                await Task.Delay(PageTokenCaptureRetryDelay, cancellationToken);
                if (!IsCurrentNavigation(navigationId, generation, hostGeneration))
                {
                    return;
                }

                if (await CaptureCurrentPageTokenAsync(
                    coreWebView,
                    navigationId,
                    generation,
                    hostGeneration,
                    cancellationToken,
                    logUnavailable: false))
                {
                    await RequestSessionReadyReportAsync(coreWebView, navigationId, generation, hostGeneration, cancellationToken);
                    return;
                }
            }

            if (IsCurrentNavigation(navigationId, generation, hostGeneration))
            {
                _logger.Warning("WebView page token remained unavailable after retry.");
                _statusInspector.TrySetUnavailableSnapshot(
                    "Hosted bridge page token was not accepted after navigation.",
                    generation);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when navigation changes, the WebView is detached, or the app is closing.
        }
        catch (ObjectDisposedException ex)
        {
            if (IsCurrentNavigation(navigationId, generation, hostGeneration))
            {
                _logger.Warning($"WebView page token retry was interrupted by disposed resource: {ex.ObjectName ?? ex.Message}");
            }
        }
        catch (Exception ex)
        {
            if (IsCurrentNavigation(navigationId, generation, hostGeneration))
            {
                _logger.Warning($"WebView page token retry failed: {ex.Message}");
            }
        }
        finally
        {
            navigationLease.Dispose();
        }
    }

    private void ObserveSessionReadyReportRequest(
        CoreWebView2 coreWebView,
        ulong navigationId,
        int generation,
        int hostGeneration,
        NavigationCancellationScope navigationCancellation)
    {
        var navigationLease = navigationCancellation.TryAcquire();
        if (navigationLease is null)
        {
            return;
        }

        _ = RequestSessionReadyReportAsync(coreWebView, navigationId, generation, hostGeneration, navigationLease);
    }

    private async Task RequestSessionReadyReportAsync(
        CoreWebView2 coreWebView,
        ulong navigationId,
        int generation,
        int hostGeneration,
        NavigationCancellationScope.Lease navigationLease)
    {
        try
        {
            await RequestSessionReadyReportAsync(coreWebView, navigationId, generation, hostGeneration, navigationLease.Token);
        }
        finally
        {
            navigationLease.Dispose();
        }
    }

    private async Task RequestSessionReadyReportAsync(
        CoreWebView2 coreWebView,
        ulong navigationId,
        int generation,
        int hostGeneration,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!IsCurrentNavigation(navigationId, generation, hostGeneration))
            {
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linkedCancellation = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken)
                : null;
            var commandToken = linkedCancellation?.Token ?? timeout.Token;
            await coreWebView.ExecuteScriptAsync("window.__openClawHostBridge?.reportSessionReady?.() ?? false")
                .AsTask(commandToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when navigation changes, the WebView is detached, or the app is closing.
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentNavigation(navigationId, generation, hostGeneration))
            {
                _logger.Warning("Timed out while requesting hosted session-ready report.");
            }
        }
        catch (ObjectDisposedException ex)
        {
            if (IsCurrentNavigation(navigationId, generation, hostGeneration))
            {
                _logger.Warning($"Hosted session-ready report request was interrupted by disposed resource: {ex.ObjectName ?? ex.Message}");
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            if (IsCurrentNavigation(navigationId, generation, hostGeneration))
            {
                _logger.Warning($"Failed to request hosted session-ready report: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            if (IsCurrentNavigation(navigationId, generation, hostGeneration))
            {
                _logger.Warning($"Hosted session-ready report request failed: {ex.Message}");
            }
        }
    }

    private void LogLifecycleEventOnce(string eventName, object? context = null)
    {
        var logKey = context is null
            ? eventName
            : $"{eventName}:{System.Text.Json.JsonSerializer.Serialize(context)}";

        if (string.Equals(_lastLifecycleLogKey, logKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastLifecycleLogKey = logKey;
        _logger.Info(eventName, context);
    }

    private void SetState(ConnectionState newState)
    {
        if (CurrentState != newState)
        {
            CurrentState = newState;
            ConnectionStateChanged?.Invoke(newState);
        }
    }

    private enum AutoRetryOutcome
    {
        NotAttempted,
        Started,
        Stale,
        Failed,
    }

}

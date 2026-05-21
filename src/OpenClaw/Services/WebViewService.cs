// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

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
    private CancellationTokenSource? _retryCts;
    private int _webViewGeneration;
    private string? _lastLifecycleLogKey;
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

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
    public async Task InitializeAsync(WebView2 webView, string environmentName)
    {
        DetachCurrentWebView();
        _webView = webView;
        _coreWebView = null;
        NextWebViewGeneration();
        CurrentEnvironmentName = environmentName;
        _isInitialized = false;

        try
        {
            var userDataFolder = GetUserDataFolderForEnvironment(environmentName);
            Directory.CreateDirectory(userDataFolder);

            // In WinUI 3, set user data folder via environment variable before initialization.
            // This avoids API signature differences between WinUI 3 and Win32 WebView2.
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", userDataFolder);

            await _webView.EnsureCoreWebView2Async();
            var coreWebView = TryGetCoreWebView2(_webView);
            if (coreWebView is null)
            {
                throw new InvalidOperationException("CoreWebView2 became unavailable immediately after initialization.");
            }

            _coreWebView = coreWebView;

            // Make WebView2 follow system Light/Dark theme preferred scheme
            coreWebView.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;

            // Set default background to transparent (blends with Mica)
            _webView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;

            // Wire up events
            coreWebView.NavigationStarting += OnNavigationStarting;
            coreWebView.NavigationCompleted += OnNavigationCompleted;
            coreWebView.ProcessFailed += OnProcessFailed;
            coreWebView.WebMessageReceived += OnWebMessageReceived;

            // Allow file input dialog
            coreWebView.Settings.AreDefaultContextMenusEnabled = true;
            coreWebView.Settings.IsStatusBarEnabled = false;
            coreWebView.Settings.AreDevToolsEnabled = true;

            coreWebView.Settings.IsGeneralAutofillEnabled = true;

            _isInitialized = true;
            App.Logger.Info("WebView2 initialized successfully.");
        }
        catch (Exception ex)
        {
            App.Logger.Error($"WebView2 initialization failed: {ex}");
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
            App.Logger.Warning("Cannot navigate: WebView2 not initialized.");
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            App.Logger.Warning($"Invalid URL: {url}");
            NavigationErrorOccurred?.Invoke($"Invalid URL: {url}");
            return;
        }

        App.Logger.Info($"Navigating to: {url}");
        _lastNavigatedUrl = url;
        _retryCount = 0;
        CancelStatusProbeLoop();
        NextWebViewGeneration();
        _retryCts?.Cancel();
        _retryCts = new CancellationTokenSource();
        SetState(ConnectionState.Loading);
        LogLifecycleEventOnce("navigation.start", new { url });
        try
        {
            coreWebView.Navigate(url);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            App.Logger.Warning($"Navigate skipped because CoreWebView2 became unavailable: {ex.Message}");
            SetState(ConnectionState.Error);
            NavigationErrorOccurred?.Invoke($"Navigation failed before WebView2 was ready: {ex.Message}");
        }
    }

    /// <summary>
    /// Reloads the current page.
    /// </summary>
    public void Reload()
    {
        var coreWebView = GetCoreWebView();
        if (coreWebView is null)
        {
            return;
        }

        App.Logger.Info("Reloading page.");
        SetState(ConnectionState.Loading);
        try
        {
            coreWebView.Reload();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            App.Logger.Warning($"Reload skipped because CoreWebView2 became unavailable: {ex.Message}");
        }
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
            App.Logger.Info("Clearing browsing data.");
            await coreWebView.Profile.ClearBrowsingDataAsync();
            App.Logger.Info("Browsing data cleared.");
        }
        catch (Exception ex)
        {
            App.Logger.Error($"Failed to clear browsing data: {ex.Message}");
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
            App.Logger.Warning($"OpenDevTools skipped because CoreWebView2 became unavailable: {ex.Message}");
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
            App.Logger.Info($"Clearing active browsing data for environment '{environmentName}'.");
            await coreWebView.Profile.ClearBrowsingDataAsync();
            return;
        }

        DeleteUserDataFolderForEnvironment(environmentName);
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
        App.Logger.Info($"Manual retry navigation to: {_lastNavigatedUrl}");
        SetState(ConnectionState.Loading);
        try
        {
            coreWebView.Navigate(_lastNavigatedUrl);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            App.Logger.Warning($"Manual retry skipped because CoreWebView2 became unavailable: {ex.Message}");
            return false;
        }
        return true;
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

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var snapshot = ParseControlUiSnapshot(args.WebMessageAsJson);
            if (snapshot.Phase == ControlUiPhase.Unknown)
            {
                return;
            }

            ApplyControlUiSnapshot(snapshot, raiseIssueEvent: true);
        }
        catch (Exception ex)
        {
            App.Logger.Warning($"Failed to process Control UI status message: {ex.Message}");
        }
    }

    // --- Event handlers ---

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        CancelStatusProbeLoop();
        NextWebViewGeneration();
        _lastReportedIssueKey = null;
        _heartbeatConnectingCount = 0;
        _lastHeartbeatObservationKey = null;
        _latestControlUiSnapshot = ControlUiProbeSnapshot.Loading(args.Uri);
        SetState(ConnectionState.Loading);
        LogLifecycleEventOnce("navigation.starting", new { uri = args.Uri });
    }

    private async void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess)
        {
            _retryCount = 0;
            ApplyControlUiSnapshot(ControlUiProbeSnapshot.PageLoaded(sender.Source), raiseIssueEvent: false);
            StartStatusProbeLoop();
            LogLifecycleEventOnce("navigation.completed", new { uri = sender.Source });
            NavigationCompleted?.Invoke(sender.Source);
        }
        else
        {
            CancelStatusProbeLoop();
            App.Logger.Warning($"Navigation failed: {args.WebErrorStatus}");

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

                // Auto-retry for connection errors
                if (_retryCount < MaxRetries && !string.IsNullOrEmpty(_lastNavigatedUrl))
                {
                    _retryCount++;
                    var token = _retryCts?.Token ?? CancellationToken.None;
                    App.Logger.Info($"Auto-retry {_retryCount}/{MaxRetries} in {RetryDelay.TotalSeconds}s...");
                    try
                    {
                        await Task.Delay(RetryDelay, token);
                    }
                    catch (TaskCanceledException)
                    {
                        App.Logger.Info("Auto-retry cancelled (new navigation started).");
                        return;
                    }
                    var coreWebView = GetCoreWebView();
                    if (coreWebView is not null && !string.IsNullOrEmpty(_lastNavigatedUrl))
                    {
                        try
                        {
                            coreWebView.Navigate(_lastNavigatedUrl);
                        }
                        catch (Exception ex) when (ex is COMException or InvalidOperationException)
                        {
                            App.Logger.Warning($"Auto-retry skipped because CoreWebView2 became unavailable: {ex.Message}");
                        }
                        return; // don't fire error event for auto-retries
                    }
                }
            }

            SetState(args.WebErrorStatus switch
            {
                CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or
                CoreWebView2WebErrorStatus.CertificateExpired or
                CoreWebView2WebErrorStatus.CertificateRevoked or
                CoreWebView2WebErrorStatus.CertificateIsInvalid => ConnectionState.AuthFailed,
                _ when isConnectionError => ConnectionState.Reconnecting,
                _ => ConnectionState.Error,
            });
            NavigationErrorOccurred?.Invoke($"Navigation error: {args.WebErrorStatus}");
        }
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        CancelStatusProbeLoop();
        NextWebViewGeneration();
        _latestControlUiSnapshot = ControlUiProbeSnapshot.Unavailable("Browser process failed.");
        App.Logger.Error($"WebView2 process failed: {args.Reason} ({args.ProcessFailedKind})");
        SetState(ConnectionState.Error);
        NavigationErrorOccurred?.Invoke($"Browser process failed: {args.Reason}");
    }

    private void DetachCurrentWebView()
    {
        CancelStatusProbeLoop();
        StopHeartbeat();
        NextWebViewGeneration();
        _retryCts?.Cancel();

        var coreWebView = GetCoreWebView();
        if (coreWebView is not null)
        {
            coreWebView.NavigationStarting -= OnNavigationStarting;
            coreWebView.NavigationCompleted -= OnNavigationCompleted;
            coreWebView.ProcessFailed -= OnProcessFailed;
            coreWebView.WebMessageReceived -= OnWebMessageReceived;
        }

        _webView = null;
        _coreWebView = null;
        _isInitialized = false;
        _lastControlUiInspectionAt = DateTimeOffset.MinValue;
        _latestControlUiSnapshot = ControlUiProbeSnapshot.Unknown;
        _lastLifecycleLogKey = null;
    }

    public void Dispose()
    {
        DetachCurrentWebView();
        _retryCts?.Dispose();
        _retryCts = null;
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

    private int NextWebViewGeneration()
    {
        return Interlocked.Increment(ref _webViewGeneration);
    }

    private bool IsCurrentGeneration(int generation)
    {
        return Volatile.Read(ref _webViewGeneration) == generation;
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
        App.Logger.Info(eventName, context);
    }

    private void SetState(ConnectionState newState)
    {
        if (CurrentState != newState)
        {
            CurrentState = newState;
            ConnectionStateChanged?.Invoke(newState);
        }
    }

}

/// <summary>
/// Represents the connection/loading state of the WebView2 session.
/// </summary>

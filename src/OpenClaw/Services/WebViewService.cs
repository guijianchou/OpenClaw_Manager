// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using OpenClaw.Helpers;
using Windows.Foundation;

namespace OpenClaw.Services;

/// <summary>
/// Manages WebView2 lifecycle, navigation, and connection state monitoring.
/// </summary>
public partial class WebViewService : IDiagnosticWebViewSession, IDisposable
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
    private static readonly TimeSpan NavigationStartRecoveryWindow = NavigationCompletionTimeout - NavigationStartTimeout;
    private static readonly TimeSpan PageTokenCaptureRetryDelay = TimeSpan.FromMilliseconds(250);
    private readonly IAppLogger _logger;
    private readonly UiTaskDispatcher _uiDispatcher;
    private readonly WebViewGenerationTracker _generations;
    private readonly WebViewStatusInspector _statusInspector;
    private readonly WebViewMessageOwnership _messageOwnership;
    private readonly Func<bool> _shouldEnableDevTools;
    private readonly object _navigationStartWatchdogGate = new();
    private readonly object _navigationCompletionWatchdogGate = new();
    private TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs>? _navigationStartingHandler;
    private TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs>? _navigationCompletedHandler;
    private TypedEventHandler<CoreWebView2, CoreWebView2ProcessFailedEventArgs>? _processFailedHandler;
    private TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs>? _webMessageReceivedHandler;
    private bool _isDisposed;
    private ulong _currentNavigationId = NoCurrentNavigationId;
    private ulong _activeNavigationCompletionWatchdogId = NoCurrentNavigationId;
    private bool _hasActiveNavigationStartWatchdogOwnership;
    private int _activeNavigationStartWatchdogGeneration;
    private string? _activeNavigationStartWatchdogUrl;
    private string? _activeNavigationStartWatchdogPreviousSource;
    private int _hostGeneration;

    internal WebViewService(
        IAppLogger logger,
        WebViewMessageOwnership messageOwnership,
        Func<Action, bool> dispatchToUi,
        Func<bool>? shouldEnableDevTools = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _messageOwnership = messageOwnership ?? throw new ArgumentNullException(nameof(messageOwnership));
        _shouldEnableDevTools = shouldEnableDevTools ?? (() => false);
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
    /// Raised when a navigation timeout recovery request became unnecessary before the WebView host was replaced.
    /// </summary>
    public event Action? NavigationTimeoutRecovered;

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
    /// Gets the Gateway URL identity currently backing the active WebView2 profile.
    /// </summary>
    public string? CurrentEnvironmentGatewayUrl { get; private set; }

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
            NavigationErrorOccurred?.Invoke(string.Format(StringResources.WebViewInvalidUrlFormat, url));
            return;
        }

        _logger.Info($"Navigating to: {url}");
        _lastNavigatedUrl = url;
        _retryCount = 0;
        var navigationGeneration = PrepareNavigationStart();
        var previousSource = coreWebView.Source;
        SetState(ConnectionState.Loading);
        LogLifecycleEventOnce("navigation.start", new { url });
        ObserveNavigationStartTimeout(navigationGeneration, url, previousSource);
        if (!TryNavigateCoreWebView(coreWebView, url, "Navigate"))
        {
            CancelNavigationStartWatchdog();
            CancelNavigationCancellation();
            SetState(ConnectionState.Error);
            NavigationErrorOccurred?.Invoke(StringResources.WebViewNavigationNotReady);
            return;
        }
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
            NavigationErrorOccurred?.Invoke(StringResources.WebViewReloadNotInitialized);
            return false;
        }

        _logger.Info("Reloading page.");
        var navigationGeneration = PrepareNavigationStart();
        var currentSource = coreWebView.Source;
        SetState(ConnectionState.Loading);
        ObserveNavigationStartTimeout(navigationGeneration, currentSource, currentSource);
        if (TryReloadCoreWebView(coreWebView))
        {
            return true;
        }

        CancelNavigationStartWatchdog();
        CancelNavigationCancellation();
        SetState(ConnectionState.Error);
        NavigationErrorOccurred?.Invoke(StringResources.WebViewReloadNotReady);
        return false;
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
        var previousSource = coreWebView.Source;
        SetState(ConnectionState.Loading);
        ObserveNavigationStartTimeout(navigationGeneration, _lastNavigatedUrl, previousSource);
        if (TryNavigateCoreWebView(coreWebView, _lastNavigatedUrl, "Manual retry"))
        {
            return true;
        }

        CancelNavigationStartWatchdog();
        CancelNavigationCancellation();
        SetState(ConnectionState.Error);
        NavigationErrorOccurred?.Invoke(StringResources.WebViewRetryNotReady);
        return false;
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

}

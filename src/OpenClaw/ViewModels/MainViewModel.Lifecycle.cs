// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml.Controls;
using OpenClaw.Services;

namespace OpenClaw.ViewModels;

public partial class MainViewModel
{
    private void SubscribeToServiceEvents()
    {
        _webViewService.ConnectionStateChanged += OnConnectionStateChanged;
        _webViewService.NavigationErrorOccurred += OnNavigationError;
        _webViewService.NavigationStartTimedOut += OnNavigationStartTimedOut;
        _webViewService.NavigationCompletionTimedOut += OnNavigationCompletionTimedOut;
        _webViewService.ControlUiSnapshotUpdated += OnControlUiSnapshotUpdated;
        _webViewService.HeartbeatObserved += OnHeartbeatObserved;
        _latencyService.LatencyUpdated += OnLatencyUpdated;
    }

    private void UnsubscribeFromServiceEvents()
    {
        _webViewService.ConnectionStateChanged -= OnConnectionStateChanged;
        _webViewService.NavigationErrorOccurred -= OnNavigationError;
        _webViewService.NavigationStartTimedOut -= OnNavigationStartTimedOut;
        _webViewService.NavigationCompletionTimedOut -= OnNavigationCompletionTimedOut;
        _webViewService.ControlUiSnapshotUpdated -= OnControlUiSnapshotUpdated;
        _webViewService.HeartbeatObserved -= OnHeartbeatObserved;
        _latencyService.LatencyUpdated -= OnLatencyUpdated;
    }

    private void InitializeCoordinator()
    {
        _coordinator = new ShellSessionCoordinator();
        _coordinator.RecoveryStateChanged += OnRecoveryStateChanged;
        _coordinator.TelemetryUpdated += OnTelemetryUpdated;
    }

    /// <summary>
    /// Initializes the WebView2 control. Called from the view after the control is loaded.
    /// </summary>
    public async Task InitializeWebViewAsync(WebView2 webView)
    {
        if (_isDisposed || _selectedEnvironment is null || _coordinator is null)
        {
            RefreshResourceScheduling();
            return;
        }

        var cancellationToken = _lifetimeCts.Token;
        _runtime.Logger.Info("Initializing WebView2 host.", new { environment = _selectedEnvironment.Name });

        await _webViewService.InitializeAsync(webView, _selectedEnvironment.Name, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_isDisposed || !_webViewService.IsInitialized)
        {
            return;
        }

        _runtime.Logger.Info("WebView2 host initialized.", new { environment = _selectedEnvironment.Name });

        await _hostedUiBridge.InitializeAsync(webView, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_isDisposed || !_hostedUiBridge.IsInitialized)
        {
            return;
        }

        _runtime.Logger.Info("Hosted UI bridge initialized for WebView2.", new { environment = _selectedEnvironment.Name });

        await _coordinator.AttachAsync(
            _webViewService,
            _hostedUiBridge,
            _runtime.Configuration.Settings.RecoveryPolicy,
            _runtime.Configuration.Settings.Heartbeat,
            _runtime.Logger,
            _dispatchToUi);
        cancellationToken.ThrowIfCancellationRequested();
        if (_isDisposed)
        {
            return;
        }

        _coordinator.SetEnvironment(_selectedEnvironment.Name, _selectedEnvironment.GatewayUrl);
        UpdateStatusPresentation();
        RefreshResourceScheduling();
        _runtime.Logger.Info("Shell session coordinator attached.", new { environment = _selectedEnvironment.Name });

        if (_webViewService.IsInitialized)
        {
            _runtime.Logger.Info("Navigating WebView2 to selected environment.", new { environment = _selectedEnvironment.Name, gatewayUrl = _selectedEnvironment.GatewayUrl });
            _webViewService.Navigate(_selectedEnvironment.GatewayUrl);
        }
    }

    /// <summary>
    /// Detaches services from the current WebView host before the view closes or replaces it.
    /// </summary>
    public void DetachWebViewHost()
    {
        if (_isDisposed)
        {
            return;
        }

        _coordinator?.DetachServices();
        _hostedUiBridge.DetachCurrentWebView();
        _webViewService.DetachCurrentWebViewHost();
        RefreshResourceScheduling();
    }

    public void Dispose()
    {
        _isDisposed = true;
        _lifetimeCts.Cancel();
        UnsubscribeFromServiceEvents();

        if (_coordinator is not null)
        {
            _coordinator.RecoveryStateChanged -= OnRecoveryStateChanged;
            _coordinator.TelemetryUpdated -= OnTelemetryUpdated;
            _coordinator.Dispose();
            _coordinator = null;
        }

        _latencyService.Dispose();
        _hostedUiBridge.Dispose();
        _webViewService.Dispose();
        _lifetimeCts.Dispose();
    }

    /// <summary>
    /// Reloads environments from configuration (e.g. after settings dialog closes).
    /// </summary>
    public void RefreshEnvironments()
    {
        LoadEnvironments();
    }

    /// <summary>
    /// Notifies the coordinator that the host window went to background.
    /// </summary>
    public void NotifyHostHidden()
    {
        if (_isDisposed)
        {
            return;
        }

        _isHostVisible = false;
        RefreshResourceScheduling();
        _coordinator?.OnHostHidden();
    }

    /// <summary>
    /// Notifies the coordinator that the host window returned to foreground.
    /// </summary>
    public async Task NotifyHostVisibleAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isHostVisible = true;

        if (_coordinator is not null)
        {
            await _coordinator.OnHostVisibleAsync(_lifetimeCts.Token);
        }

        RefreshResourceScheduling();
    }

    private void RefreshResourceScheduling()
    {
        if (!_isHostVisible || _selectedEnvironment is null || !_webViewService.IsInitialized)
        {
            _latencyService.Stop();
            _webViewService.StopHeartbeat();
            return;
        }

        _latencyService.Start(_selectedEnvironment.GatewayUrl);

        if (ShouldRunHeartbeatForCurrentState())
        {
            EnsureHeartbeatUiPrimed();
            StartHeartbeatForSelectedEnvironment();
            return;
        }

        _webViewService.StopHeartbeat();
    }

    private bool ShouldRunHeartbeatForCurrentState()
    {
        var snapshot = _webViewService.LatestControlUiSnapshot;
        return (_webViewService.CurrentState == ConnectionState.Connected &&
                snapshot.Phase == ControlUiPhase.Connected) ||
            (_webViewService.CurrentState == ConnectionState.Reconnecting &&
                snapshot.Phase == ControlUiPhase.Unavailable);
    }
}

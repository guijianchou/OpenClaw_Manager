// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.UI.Xaml.Controls;
using OpenClaw.Models;
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
        _webViewService.NavigationTimeoutRecovered += OnNavigationTimeoutRecovered;
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
        _webViewService.NavigationTimeoutRecovered -= OnNavigationTimeoutRecovered;
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

        if (_selectedEnvironment.IsPlaceholder)
        {
            ApplyPlaceholderEnvironmentState();
            return;
        }

        var cancellationToken = _lifetimeCts.Token;
        var environmentName = _selectedEnvironment.Name;
        var gatewayUrl = _selectedEnvironment.GatewayUrl;
        _runtime.Logger.Info("Initializing WebView2 host.", new { environment = environmentName });

        await _webViewService.InitializeAsync(webView, environmentName, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentSelectedEnvironment(environmentName, gatewayUrl) ||
            _isDisposed ||
            !_webViewService.IsInitialized)
        {
            return;
        }

        _runtime.Logger.Info("WebView2 host initialized.", new { environment = environmentName });

        await _hostedUiBridge.InitializeAsync(webView, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentSelectedEnvironment(environmentName, gatewayUrl) ||
            _isDisposed ||
            !_hostedUiBridge.IsInitialized)
        {
            return;
        }

        _runtime.Logger.Info("Hosted UI bridge initialized for WebView2.", new { environment = environmentName });

        await _coordinator.AttachAsync(
            _webViewService,
            _hostedUiBridge,
            _runtime.Configuration.Settings.RecoveryPolicy,
            _runtime.Configuration.Settings.Heartbeat,
            _runtime.Logger,
            _dispatchToUi);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentSelectedEnvironment(environmentName, gatewayUrl) || _isDisposed)
        {
            return;
        }

        _coordinator.SetEnvironment(environmentName, gatewayUrl);
        UpdateStatusPresentation();
        RefreshResourceScheduling();
        _runtime.Logger.Info("Shell session coordinator attached.", new { environment = environmentName });

        if (_webViewService.IsInitialized && IsCurrentSelectedEnvironment(environmentName, gatewayUrl))
        {
            _runtime.Logger.Info("Navigating WebView2 to selected environment.", new { environment = environmentName, gatewayUrl });
            _webViewService.Navigate(gatewayUrl);
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
        if (_selectedEnvironment?.IsPlaceholder == true)
        {
            ApplyPlaceholderEnvironmentState();
        }
        else
        {
            ApplyWebViewHostDetachedState();
        }

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
            ResetResourceProbeProjection();
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
        ResetHeartbeatProjection();
    }

    private bool ShouldRunHeartbeatForCurrentState()
    {
        var snapshot = _webViewService.LatestControlUiSnapshot;
        return (_webViewService.CurrentState == ConnectionState.Connected &&
                snapshot.Phase == ControlUiPhase.Connected) ||
            (_webViewService.CurrentState == ConnectionState.Reconnecting &&
                snapshot.Phase == ControlUiPhase.Unavailable);
    }

    private bool IsCurrentSelectedEnvironment(string environmentName, string gatewayUrl)
    {
        return _selectedEnvironment is not null &&
            !_selectedEnvironment.IsPlaceholder &&
            string.Equals(_selectedEnvironment.Name, environmentName, StringComparison.Ordinal) &&
            string.Equals(_selectedEnvironment.GatewayUrl, gatewayUrl, StringComparison.Ordinal);
    }

    private void ResetResourceProbeProjection()
    {
        ResetHeartbeatProjection();
        ResetLatencyProjection();
    }

    private void ApplyWebViewHostDetachedState()
    {
        ApplyConnectionState(ConnectionState.Loading);
        ResetTelemetry();
        ApplyRecoveryState(RecoveryState.Connecting);
        ResetResourceProbeProjection();
        IsErrorVisible = false;
        ShowRetryButton = false;
    }
}

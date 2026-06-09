// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Helpers;
using OpenClaw.Models;

namespace OpenClaw.ViewModels;

public partial class MainViewModel
{
    private void LoadEnvironments()
    {
        Environments.Clear();

        foreach (var environment in _runtime.Configuration.Settings.Environments)
        {
            Environments.Add(environment);
        }

        UpdateEnvironmentSelection(_runtime.Configuration.GetSelectedEnvironment(), persistSelection: false);
    }

    private void UpdateEnvironmentSelection(EnvironmentConfig? environment, bool persistSelection)
    {
        _selectedEnvironment = environment;
        OnPropertyChanged(nameof(SelectedEnvironment));
        OnPropertyChanged(nameof(CurrentUrl));
        OnPropertyChanged(nameof(SelectedEnvironmentName));
        OnPropertyChanged(nameof(IsPlaceholderEnvironment));

        if (environment is null)
        {
            RefreshResourceScheduling();
            ResetTelemetry();
            return;
        }

        if (environment.IsPlaceholder)
        {
            var shouldClearWebViewHost = _webViewService.IsInitialized;
            ApplyPlaceholderEnvironmentState();
            if (persistSelection)
            {
                _runtime.Configuration.Settings.SelectedEnvironmentName = environment.Name;
                _runtime.Configuration.SaveDeferred();
            }

            if (shouldClearWebViewHost)
            {
                WebViewRecreationRequested?.Invoke("environment_placeholder_selected");
            }

            return;
        }

        var shouldCreateWebViewHost = !_webViewService.IsInitialized;

        ResetTelemetry();
        _coordinator?.Reset();
        _coordinator?.SetEnvironment(environment.Name, environment.GatewayUrl);
        UpdateStatusPresentation();
        RefreshResourceScheduling();

        if (persistSelection)
        {
            _runtime.Configuration.Settings.SelectedEnvironmentName = environment.Name;
            _runtime.Configuration.SaveDeferred();
        }

        if (shouldCreateWebViewHost)
        {
            WebViewRecreationRequested?.Invoke("environment_placeholder_replaced");
            return;
        }

        if (_webViewService.IsUsingEnvironmentProfile(environment.Name, environment.GatewayUrl))
        {
            _webViewService.Navigate(environment.GatewayUrl);
        }
        else
        {
            WebViewRecreationRequested?.Invoke("environment_profile_changed");
        }
    }

    private void ApplyPlaceholderEnvironmentState()
    {
        ResetTelemetry();
        _coordinator?.Reset();
        _webViewService.StopHeartbeat();
        _latencyService.Stop();
        ResetResourceProbeProjection();
        ApplyConnectionState(OpenClaw.Services.ConnectionState.Offline);
        ApplyRecoveryState(RecoveryState.Healthy);
        StatusMessage = StringResources.StatusConfigureGateway;
        StatusIndicatorBrush = WarningBrush;
        IsErrorVisible = false;
        ShowRetryButton = false;
    }

    public Task ClearSessionForEnvironmentAsync(EnvironmentConfig environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return ClearSessionForEnvironmentAsync(environment.Name, environment.GatewayUrl);
    }

    public Task ClearSessionForEnvironmentAsync(string environmentName) =>
        ClearSessionForEnvironmentAsync(
            environmentName,
            _runtime.Configuration.Settings.Environments
                .FirstOrDefault(env => string.Equals(env.Name, environmentName, StringComparison.Ordinal))
                ?.GatewayUrl);

    public async Task ClearSessionForEnvironmentAsync(string environmentName, string? gatewayUrl)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return;
        }

        await _webViewService.ClearEnvironmentSessionAsync(environmentName, gatewayUrl);

        if (string.Equals(_selectedEnvironment?.Name, environmentName, StringComparison.Ordinal) &&
            string.Equals(_selectedEnvironment?.GatewayUrl, gatewayUrl, StringComparison.Ordinal))
        {
            DismissError();
            DismissDiagnostics();
            WebViewRecreationRequested?.Invoke("active_session_cleared");
        }
    }
}

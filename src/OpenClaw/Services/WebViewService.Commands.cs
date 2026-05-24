// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Runtime.InteropServices;

namespace OpenClaw.Services;

public partial class WebViewService
{
    /// <summary>
    /// Sends the in-app stop command when possible, falling back to stopping navigation.
    /// </summary>
    public async Task StopAsync()
    {
        var coreWebView = GetCoreWebView();
        if (coreWebView is null)
        {
            return;
        }

        var aborted = await TryAbortActiveRunAsync();
        if (aborted)
        {
            _logger.Info("Triggered the hosted UI stop action.");
            return;
        }

        var injected = await InjectStopCommandAsync();
        if (injected)
        {
            _logger.Info("Injected /stop command into the web UI.");
            return;
        }

        _logger.Info("Stop command injection unavailable, stopping navigation instead.");
        try
        {
            coreWebView.Stop();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.Warning($"Stop skipped because CoreWebView2 became unavailable: {ex.Message}");
            return;
        }

        if (CurrentState == ConnectionState.Loading)
        {
            SetState(ConnectionState.Offline);
        }
    }

    /// <summary>
    /// Attempts to inject "/stop" into the active chat input and submit it.
    /// </summary>
    public async Task<bool> InjectStopCommandAsync()
    {
        var coreWebView = GetCoreWebView();
        if (coreWebView is null)
        {
            return false;
        }

        try
        {
            var result = await coreWebView.ExecuteScriptAsync(WebViewCommandScripts.StopInjection);
            return string.Equals(result?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.Warning($"Stop skipped because CoreWebView2 became unavailable: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to inject /stop command: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Attempts to use the hosted UI's built-in stop/abort affordance before falling back to command injection.
    /// </summary>
    public async Task<bool> TryAbortActiveRunAsync()
    {
        var coreWebView = GetCoreWebView();
        if (coreWebView is null)
        {
            return false;
        }

        try
        {
            var result = await coreWebView.ExecuteScriptAsync(WebViewCommandScripts.AbortRun);
            return string.Equals(result?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.Warning($"Abort skipped because CoreWebView2 became unavailable: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to trigger hosted UI stop action: {ex.Message}");
            return false;
        }
    }
}

// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

public partial class WebViewService
{
    private static readonly TimeSpan CommandScriptTimeout = TimeSpan.FromSeconds(3);

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

        var pageVersion = _messageOwnership.CaptureAcceptedPageVersion();
        if (pageVersion == 0)
        {
            StopCurrentNavigation(coreWebView);
            return;
        }

        var aborted = await TryAbortActiveRunAsync(coreWebView, pageVersion);
        if (aborted)
        {
            _logger.Info("Triggered the hosted UI stop action.");
            return;
        }

        if (!IsStillCurrentWebViewCommandTarget(coreWebView, pageVersion))
        {
            return;
        }

        var injected = await InjectStopCommandAsync(coreWebView, pageVersion);
        if (injected)
        {
            _logger.Info("Injected /stop command into the web UI.");
            return;
        }

        if (!IsStillCurrentWebViewCommandTarget(coreWebView, pageVersion))
        {
            return;
        }

        _logger.Info("Stop command injection unavailable, stopping navigation instead.");
        StopCurrentNavigation(coreWebView);
    }

    private void StopCurrentNavigation(CoreWebView2 coreWebView)
    {
        if (_isDisposed || !ReferenceEquals(coreWebView, _coreWebView))
        {
            return;
        }

        try
        {
            coreWebView.Stop();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.Warning($"Stop skipped because CoreWebView2 became unavailable: {ex.Message}");
            return;
        }

        CancelActiveNavigation();
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

        var pageVersion = _messageOwnership.CaptureAcceptedPageVersion();
        if (pageVersion == 0)
        {
            return false;
        }

        return await InjectStopCommandAsync(coreWebView, pageVersion);
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

        var pageVersion = _messageOwnership.CaptureAcceptedPageVersion();
        if (pageVersion == 0)
        {
            return false;
        }

        return await TryAbortActiveRunAsync(coreWebView, pageVersion);
    }

    private async Task<bool> InjectStopCommandAsync(CoreWebView2 coreWebView, int pageVersion)
    {
        try
        {
            var result = await ExecuteCommandScriptAsync(coreWebView, WebViewCommandScripts.StopInjection);
            if (!IsStillCurrentWebViewCommandTarget(coreWebView, pageVersion))
            {
                return false;
            }

            return string.Equals(result?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"Stop command injection timed out after {CommandScriptTimeout.TotalSeconds:0.#}s.");
            return false;
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

    private async Task<bool> TryAbortActiveRunAsync(CoreWebView2 coreWebView, int pageVersion)
    {
        try
        {
            var result = await ExecuteCommandScriptAsync(coreWebView, WebViewCommandScripts.AbortRun);
            if (!IsStillCurrentWebViewCommandTarget(coreWebView, pageVersion))
            {
                return false;
            }

            return string.Equals(result?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"Hosted UI stop action timed out after {CommandScriptTimeout.TotalSeconds:0.#}s.");
            return false;
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

    private static async Task<string> ExecuteCommandScriptAsync(CoreWebView2 coreWebView, string script)
    {
        using var timeout = new CancellationTokenSource(CommandScriptTimeout);
        return await coreWebView.ExecuteScriptAsync(script)
            .AsTask(timeout.Token);
    }

    private bool IsStillCurrentWebViewCommandTarget(CoreWebView2 coreWebView, int pageVersion)
    {
        return !_isDisposed &&
            ReferenceEquals(coreWebView, _coreWebView) &&
            _messageOwnership.IsCurrentAcceptedPageVersion(pageVersion);
    }
}

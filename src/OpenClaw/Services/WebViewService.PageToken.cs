// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

public partial class WebViewService
{
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
}

// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

public partial class WebViewService
{
    private void ObserveNavigationStartTimeout(
        int navigationGeneration,
        string? url,
        string? previousSource)
    {
        var cancellation = new CancellationTokenSource();
        lock (_navigationStartWatchdogGate)
        {
            _hasActiveNavigationStartWatchdogOwnership = true;
            _activeNavigationStartWatchdogGeneration = navigationGeneration;
            _activeNavigationStartWatchdogUrl = url;
            _activeNavigationStartWatchdogPreviousSource = previousSource;
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
            await Task.Delay(NavigationStartRecoveryWindow, cancellation.Token).ConfigureAwait(false);
            await _uiDispatcher.RunAsync(
                new Action(() => ClearExpiredNavigationStartWatchdogOwnership(navigationGeneration)),
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
            ClearNavigationStartWatchdogOwnershipLocked();
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

    private bool TryGetActiveNavigationStartWatchdog(
        out int navigationGeneration,
        out string? url,
        out string? previousSource)
    {
        lock (_navigationStartWatchdogGate)
        {
            navigationGeneration = _activeNavigationStartWatchdogGeneration;
            url = _activeNavigationStartWatchdogUrl;
            previousSource = _activeNavigationStartWatchdogPreviousSource;
            return _hasActiveNavigationStartWatchdogOwnership;
        }
    }

    private void ClearNavigationStartWatchdogOwnershipLocked()
    {
        _hasActiveNavigationStartWatchdogOwnership = false;
        _activeNavigationStartWatchdogGeneration = 0;
        _activeNavigationStartWatchdogUrl = null;
        _activeNavigationStartWatchdogPreviousSource = null;
    }

    private void ClearExpiredNavigationStartWatchdogOwnership(int navigationGeneration)
    {
        lock (_navigationStartWatchdogGate)
        {
            if (!_hasActiveNavigationStartWatchdogOwnership ||
                _activeNavigationStartWatchdogGeneration != navigationGeneration)
            {
                return;
            }

            ClearNavigationStartWatchdogOwnershipLocked();
            _navigationStartWatchdogCts = null;
        }

        _logger.Warning("navigation.start.recovery_window_expired", new
        {
            navigationGeneration,
            timeoutSeconds = NavigationStartTimeout.TotalSeconds,
            recoveryWindowSeconds = NavigationStartRecoveryWindow.TotalSeconds
        });
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
}

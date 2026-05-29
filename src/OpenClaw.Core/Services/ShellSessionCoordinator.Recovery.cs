// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Models;

namespace OpenClaw.Services;

public sealed partial class ShellSessionCoordinator
{
    /// <summary>
    /// Requests a reconnection to the gateway.
    /// </summary>
    public async Task RequestReconnectAsync(string reason, CancellationToken cancellationToken = default)
    {
        var webViewService = _webViewService;
        if (webViewService is null)
        {
            return;
        }

        var operation = TryStartRecoveryOperation(RecoveryOperationKind.Reconnect, reason, cancellationToken);
        if (operation is null)
        {
            return;
        }

        var bridge = _bridge;

        try
        {
            var delay = CalculateReconnectDelay();
            await Task.Delay(delay, operation.CancellationToken);
            ThrowIfRecoveryCancelled(operation);

            var handledInPage = false;
            if (bridge is not null)
            {
                handledInPage = await bridge.NotifyReconnectIntentAsync(operation.CancellationToken);
                handledInPage = await bridge.RequestSessionRefreshAsync(operation.CancellationToken) || handledInPage;
                ThrowIfRecoveryCancelled(operation);
            }

            if (handledInPage)
            {
                await Task.Delay(750, operation.CancellationToken);
                var snapshot = await webViewService.InspectControlUiStateAsync(operation.CancellationToken);
                ThrowIfRecoveryCancelled(operation);
                if (TryResolveReloadFallback(snapshot, reason, "post_in_page_reconnect"))
                {
                    return;
                }
            }

            var preReloadSnapshot = await webViewService.InspectControlUiStateAsync(operation.CancellationToken);
            ThrowIfRecoveryCancelled(operation);
            if (TryResolveReloadFallback(preReloadSnapshot, reason, "pre_reload"))
            {
                return;
            }

            var reloadStarted = await webViewService.ReloadAsync(operation.CancellationToken);
            if (!reloadStarted)
            {
                MarkRecoveryFailed("WebView reload could not be started.");
                return;
            }

            _lastTransportActivityAt = DateTimeOffset.Now;
            _logger.Info("recovery.reconnect.reload", new { attempt = operation.Attempt, handledInPage });
            MarkRecoveryConnecting();
        }
        catch (OperationCanceledException)
        {
            _logger.Info("recovery.cancelled", new { reason });
        }
        catch (Exception ex)
        {
            _logger.Error("recovery.reconnect.fail", new { ex.Message });
            MarkRecoveryDegraded(ex.Message);
        }
        finally
        {
            CompleteRecoveryOperation(operation);
        }
    }

    /// <summary>
    /// Requests a soft resync (state reconciliation without full reload).
    /// </summary>
    public async Task RequestSoftResyncAsync(string reason, CancellationToken cancellationToken = default)
    {
        var bridge = _bridge;
        if (bridge is null)
        {
            return;
        }

        var operation = TryStartRecoveryOperation(RecoveryOperationKind.SoftResync, reason, cancellationToken);
        if (operation is null)
        {
            return;
        }

        try
        {
            var handled = await bridge.RequestLightweightSyncAsync(operation.CancellationToken);
            handled = await bridge.RequestRecentMessagesAsync(operation.CancellationToken) || handled;
            ThrowIfRecoveryCancelled(operation);

            if (!handled)
            {
                _logger.Warning("recovery.soft_resync.unsupported", new { attempt = operation.Attempt });
                MarkRecoveryDegraded();
                return;
            }

            await Task.Delay(1500, operation.CancellationToken);

            var webViewService = _webViewService;
            if (webViewService is not null)
            {
                var snapshot = await webViewService.InspectControlUiStateAsync(operation.CancellationToken);
                ThrowIfRecoveryCancelled(operation);
                if (IsSessionAlive(snapshot) && !snapshot.IsBusyStale)
                {
                    _streamHealth = HealthStatus.Healthy;
                }
                else if (snapshot.IsBusyStale)
                {
                    _streamHealth = HealthStatus.Degraded;
                }
            }

            if (_streamHealth == HealthStatus.Healthy)
            {
                _logger.Info("recovery.soft_resync.ok", new { attempt = operation.Attempt });
                MarkRecoveryReady();
            }
            else
            {
                _logger.Warning("recovery.soft_resync.fail", new { attempt = operation.Attempt, streamHealth = _streamHealth });
                MarkRecoveryDegraded();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("recovery.cancelled", new { reason });
        }
        catch (Exception ex)
        {
            _logger.Error("recovery.soft_resync.fail", new { ex.Message });
            MarkRecoveryDegraded(ex.Message);
        }
        finally
        {
            CompleteRecoveryOperation(operation);
        }
    }

    /// <summary>
    /// Requests a hard refresh (full page reload).
    /// </summary>
    public async Task RequestHardRefreshAsync(string reason, CancellationToken cancellationToken = default)
    {
        var webViewService = _webViewService;
        if (webViewService is null)
        {
            return;
        }

        var operation = TryStartRecoveryOperation(RecoveryOperationKind.HardRefresh, reason, cancellationToken);
        if (operation is null)
        {
            return;
        }

        try
        {
            var snapshot = await webViewService.InspectControlUiStateAsync(operation.CancellationToken);
            ThrowIfRecoveryCancelled(operation);
            if (TryResolveReloadFallback(snapshot, reason, "hard_refresh"))
            {
                return;
            }

            var reloadStarted = await webViewService.ReloadAsync(operation.CancellationToken);
            if (!reloadStarted)
            {
                MarkRecoveryFailed("WebView reload could not be started.");
                return;
            }

            await Task.Delay(1000, operation.CancellationToken);
            MarkRecoveryConnecting();
        }
        catch (OperationCanceledException)
        {
            _logger.Info("recovery.cancelled", new { reason });
        }
        catch (Exception ex)
        {
            _logger.Error("recovery.hard_refresh.fail", new { ex.Message });
            MarkRecoveryFailed(ex.Message);
        }
        finally
        {
            CompleteRecoveryOperation(operation);
        }
    }
}

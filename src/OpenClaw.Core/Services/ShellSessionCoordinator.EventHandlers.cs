// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Models;

namespace OpenClaw.Services;

public sealed partial class ShellSessionCoordinator
{
    private void HandleNavigationCompleted(string? uri)
    {
        _lastTransportActivityAt = DateTimeOffset.Now;
        _transportHealth = HealthStatus.Healthy;
        _hostedUiHealth = HealthStatus.Degraded;

        _logger.Info("webview.navigation.ok", new { uri });
    }

    private void HandleHostedUiStateUpdated(ControlUiProbeSnapshot snapshot)
    {
        UpdateSnapshotActivity();
        ApplySnapshotHealth(snapshot);
        LogHostedUiState(snapshot);
        ApplyHostedUiRecoveryState(snapshot);
    }

    private void HandleSessionReady(SessionReadyEventArgs args)
    {
        _sessionHealth = HealthStatus.Healthy;
        _hostedUiHealth = HealthStatus.Healthy;
        _streamHealth = HealthStatus.Healthy;
        ResetEscalationCounters();

        if (_recoveryState is RecoveryState.Connecting or RecoveryState.Reconnecting)
        {
            MarkRecoveryReady();
            _logger.Info("session.ready", new
            {
                args.Model,
                args.Uri,
                modelSource = string.IsNullOrWhiteSpace(args.ModelSource) ? null : args.ModelSource
            });
        }
        else
        {
            MarkRecoveryHealthy();
        }

        PublishTelemetry();
    }

    private async Task HandleEventGapDetectedAsync(EventGapEventArgs args, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_isInBackground)
        {
            LogIgnoredGap(args);
            return;
        }

        ApplyDetectedGap(args);
        await ExecuteGapRecoveryAsync(args, cancellationToken);
        PublishTelemetry();
    }

    private async Task ExecuteGapRecoveryAsync(EventGapEventArgs args, CancellationToken cancellationToken)
    {
        var preferredGapRecovery = await GetPreferredGapRecoveryAsync(cancellationToken);
        if (preferredGapRecovery == GapRecoveryAction.None)
        {
            return;
        }

        await RequestGapRecoveryAsync(args, preferredGapRecovery, cancellationToken);
    }

    private async Task RequestGapRecoveryAsync(
        EventGapEventArgs args,
        GapRecoveryAction preferredGapRecovery,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (preferredGapRecovery == GapRecoveryAction.SoftResync &&
            _softResyncAttempts < _recoveryOptions.MaxSoftResyncAttempts)
        {
            await RequestSoftResyncAsync(
                $"Event gap detected while session remained alive (seq {args.ExpectedSeq} -> {args.GotSeq})",
                cancellationToken);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_reconnectAttempts < _recoveryOptions.MaxReconnectAttempts)
        {
            await RequestReconnectAsync(
                $"Event gap detected (seq {args.ExpectedSeq} -> {args.GotSeq})",
                cancellationToken);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_softResyncAttempts < _recoveryOptions.MaxSoftResyncAttempts)
        {
            await RequestSoftResyncAsync(
                $"Event gap persists after {_reconnectAttempts} reconnects",
                cancellationToken);
            return;
        }

        await RequestHardRefreshAsync("Event gap persists after soft resync attempts", cancellationToken);
    }

    private void HandleConnectionStateChanged(ConnectionState state)
    {
        ApplyConnectionHealth(state);
        _logger.Info($"connection.state.{state.ToString().ToLower()}", new { state });
    }

    private void HandleNavigationError(string message)
    {
        _logger.Error("navigation.error", new { message });
    }

    private void HandleHeartbeatObserved(HeartbeatProbeResult result)
    {
        _lastHeartbeatAt = DateTimeOffset.Now;
        ApplyHeartbeatHealth(result);
    }

    private async Task HandleHeartbeatFailedAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_isInBackground)
        {
            _logger.Info("heartbeat.recovery.deferred", new { message, reason = "background" });
            return;
        }

        _logger.Warning("heartbeat.recovery.requested", new { message });
        cancellationToken.ThrowIfCancellationRequested();
        await RequestReconnectAsync($"Heartbeat recovery requested: {message}", cancellationToken);
    }
}

// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Models;

namespace OpenClaw.Services;

public sealed partial class ShellSessionCoordinator
{
    private void UpdateSnapshotActivity()
    {
        _lastTransportActivityAt = DateTimeOffset.Now;
        _lastEventAt = _lastTransportActivityAt;
    }

    private void ApplySnapshotHealth(ControlUiProbeSnapshot snapshot)
    {
        _hostedUiHealth = MapHostedUiHealth(snapshot);
        _sessionHealth = MapSessionHealth(snapshot);
        _streamHealth = MapStreamHealth(snapshot);
    }

    private void LogHostedUiState(ControlUiProbeSnapshot snapshot)
    {
        _logger.Info("hosted_ui.state", new
        {
            phase = snapshot.Phase,
            summary = snapshot.Summary,
            shellDetected = snapshot.ShellDetected,
            currentModel = string.IsNullOrWhiteSpace(snapshot.CurrentModel) ? null : snapshot.CurrentModel,
            modelSource = string.IsNullOrWhiteSpace(snapshot.ModelSource) ? null : snapshot.ModelSource
        });
    }

    private void ApplyHostedUiRecoveryState(ControlUiProbeSnapshot snapshot)
    {
        if (TryRecoverStaleBusySession(snapshot))
        {
            return;
        }

        switch (snapshot.Phase)
        {
            case ControlUiPhase.Connected when _recoveryState is not RecoveryState.Ready and not RecoveryState.Healthy:
                MarkRecoveryReady();
                ResetEscalationCounters();
                break;
            case ControlUiPhase.AuthRequired:
            case ControlUiPhase.PairingRequired:
            case ControlUiPhase.OriginRejected:
                MarkRecoveryAuthIssue(snapshot.DetailOrSummary);
                break;
            case ControlUiPhase.GatewayError:
            case ControlUiPhase.Unavailable:
                MarkRecoveryDegraded(snapshot.DetailOrSummary);
                break;
        }
    }

    private bool TryRecoverStaleBusySession(ControlUiProbeSnapshot snapshot)
    {
        if (!snapshot.IsBusyStale || _isInBackground)
        {
            return false;
        }

        _logger.Warning("stream.busy_stale.detected", new
        {
            snapshot.BusyStaleSeconds,
            snapshot.WorkState,
            softResyncAttempts = _softResyncAttempts,
            activity = string.IsNullOrWhiteSpace(snapshot.ActivitySignature) ? null : snapshot.ActivitySignature
        });

        if (_softResyncAttempts >= _recoveryOptions.MaxSoftResyncAttempts)
        {
            SafeFireAndForget(
                async token =>
                {
                    token.ThrowIfCancellationRequested();
                    await RequestHardRefreshAsync(
                        $"Hosted Control UI busy for {snapshot.BusyStaleSeconds}s without chat progress after {_softResyncAttempts} soft resync attempt(s).",
                        token);
                },
                "stream.busy_stale.hard_refresh");
            return true;
        }

        SafeFireAndForget(
            async token =>
            {
                token.ThrowIfCancellationRequested();
                await RequestSoftResyncAsync(
                    $"Hosted Control UI busy for {snapshot.BusyStaleSeconds}s without chat progress.",
                    token);
            },
            "stream.busy_stale.soft_resync");
        return true;
    }

    private void LogIgnoredGap(EventGapEventArgs args)
    {
        _logger.Info("stream.gap.ignored", new
        {
            reason = "background",
            expectedSeq = args.ExpectedSeq,
            gotSeq = args.GotSeq
        });
    }

    private void ApplyDetectedGap(EventGapEventArgs args)
    {
        _recentGapCount++;
        _lastEventSeq = args.GotSeq;
        _lastStateVersion = args.CurrentStateVersion;
        _streamHealth = HealthStatus.Degraded;

        _logger.Warning("stream.gap.detected", new
        {
            expectedSeq = args.ExpectedSeq,
            gotSeq = args.GotSeq,
            gapSize = args.GotSeq - args.ExpectedSeq
        });
    }
}

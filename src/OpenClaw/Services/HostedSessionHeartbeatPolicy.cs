// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed class HostedSessionHeartbeatPolicy
{
    public HeartbeatProbeResult? Map(ControlUiProbeSnapshot snapshot)
    {
        return snapshot.Phase switch
        {
            ControlUiPhase.Connected =>
                HeartbeatProbeResult.Healthy("Hosted Control UI reports an active Gateway session."),
            ControlUiPhase.AuthRequired or ControlUiPhase.PairingRequired or ControlUiPhase.OriginRejected =>
                HeartbeatProbeResult.SessionBlocked(snapshot.DetailOrSummary),
            ControlUiPhase.PageLoaded or ControlUiPhase.GatewayConnecting =>
                HeartbeatProbeResult.Connecting("Hosted Control UI is still reconnecting to the Gateway."),
            ControlUiPhase.GatewayError =>
                HeartbeatProbeResult.Failure(snapshot.DetailOrSummary),
            ControlUiPhase.Unavailable =>
                HeartbeatProbeResult.Failure(snapshot.DetailOrSummary),
            _ => null,
        };
    }
}

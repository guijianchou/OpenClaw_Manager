// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Helpers;

namespace OpenClaw.Services;

internal sealed class HostedSessionHeartbeatPolicy
{
    public HeartbeatProbeResult? Map(ControlUiProbeSnapshot snapshot)
    {
        return snapshot.Phase switch
        {
            ControlUiPhase.Connected =>
                HeartbeatProbeResult.Healthy(StringResources.HeartbeatHostedSessionActive),
            ControlUiPhase.AuthRequired or ControlUiPhase.PairingRequired or ControlUiPhase.OriginRejected =>
                HeartbeatProbeResult.SessionBlocked(snapshot.DetailOrSummary),
            ControlUiPhase.PageLoaded or ControlUiPhase.GatewayConnecting =>
                HeartbeatProbeResult.Connecting(StringResources.HeartbeatHostedSessionReconnecting),
            ControlUiPhase.GatewayError =>
                HeartbeatProbeResult.Failure(snapshot.DetailOrSummary),
            ControlUiPhase.Unavailable =>
                HeartbeatProbeResult.Failure(snapshot.DetailOrSummary),
            _ => null,
        };
    }
}

// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

public static class GatewayHeartbeatProbeMapper
{
    public static HeartbeatProbeResult Map(GatewayHttpStatusClassification classification)
    {
        return classification.Kind switch
        {
            GatewayHttpStatusKind.Reachable => HeartbeatProbeResult.Healthy(classification.Detail),
            GatewayHttpStatusKind.AccessRequired or
            GatewayHttpStatusKind.GatewayWaitingApproval or
            GatewayHttpStatusKind.AuthRateLimited => HeartbeatProbeResult.SessionBlocked(classification.Detail),
            _ => HeartbeatProbeResult.Failure(classification.Detail),
        };
    }
}

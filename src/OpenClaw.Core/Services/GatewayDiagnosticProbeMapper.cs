// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

public enum GatewayDiagnosticProbeSeverity
{
    Pass,
    Warning,
    Failure,
}

public static class GatewayDiagnosticProbeMapper
{
    public static GatewayDiagnosticProbeSeverity Map(GatewayHttpStatusKind kind)
    {
        return kind switch
        {
            GatewayHttpStatusKind.Reachable => GatewayDiagnosticProbeSeverity.Pass,
            GatewayHttpStatusKind.Redirected or
            GatewayHttpStatusKind.MethodRejected or
            GatewayHttpStatusKind.MissingPath or
            GatewayHttpStatusKind.CloudflareTunnelUnavailable or
            GatewayHttpStatusKind.ServerOrProxyError => GatewayDiagnosticProbeSeverity.Failure,
            _ => GatewayDiagnosticProbeSeverity.Warning,
        };
    }
}

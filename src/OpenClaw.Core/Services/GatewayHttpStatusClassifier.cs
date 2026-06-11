// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Net;

namespace OpenClaw.Services;

public enum GatewayHttpStatusKind
{
    Reachable,
    Redirected,
    AccessRequired,
    GatewayWaitingApproval,
    AuthRateLimited,
    MissingPath,
    MethodRejected,
    CloudflareTunnelUnavailable,
    ServerOrProxyError,
    Unexpected,
}

public readonly record struct GatewayHttpStatusClassification(
    GatewayHttpStatusKind Kind,
    int StatusCode,
    string Detail,
    bool IsReachable);

public static class GatewayHttpStatusClassifier
{
    public static GatewayHttpStatusClassification Classify(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        bool viaCloudflare)
    {
        var numericStatusCode = (int)statusCode;
        var proxyHint = viaCloudflare ? " via Cloudflare" : string.Empty;
        var reason = string.IsNullOrWhiteSpace(reasonPhrase)
            ? string.Empty
            : $" {reasonPhrase.Trim()}";

        return numericStatusCode switch
        {
            >= 200 and < 300 => new(
                GatewayHttpStatusKind.Reachable,
                numericStatusCode,
                $"Gateway Control UI HTTP path reachable{proxyHint} ({numericStatusCode}).",
                true),
            301 or 302 or 303 or 307 or 308 => new(
                GatewayHttpStatusKind.Redirected,
                numericStatusCode,
                $"Gateway Control UI HTTP path redirected{proxyHint} ({numericStatusCode}).",
                true),
            401 or 403 => new(
                GatewayHttpStatusKind.AccessRequired,
                numericStatusCode,
                $"Gateway Control UI HTTP path is reachable{proxyHint} but requires authentication or origin approval ({numericStatusCode}).",
                true),
            409 => new(
                GatewayHttpStatusKind.GatewayWaitingApproval,
                numericStatusCode,
                $"Gateway Control UI HTTP path is reachable{proxyHint} but is waiting for device approval ({numericStatusCode}{reason}).",
                true),
            429 => new(
                GatewayHttpStatusKind.AuthRateLimited,
                numericStatusCode,
                $"Gateway Control UI HTTP path is reachable{proxyHint} but authentication is rate-limited ({numericStatusCode}{reason}).",
                true),
            404 => new(
                GatewayHttpStatusKind.MissingPath,
                numericStatusCode,
                $"Gateway Control UI HTTP path was not found{proxyHint} ({numericStatusCode}{reason}).",
                false),
            405 => new(
                GatewayHttpStatusKind.MethodRejected,
                numericStatusCode,
                $"Gateway rejected the HTTP probe{proxyHint} ({numericStatusCode}{reason}).",
                false),
            // Cloudflare reports a disconnected tunnel as HTTP 530 (body error 1033);
            // 1033 itself is never an HTTP status code.
            530 when viaCloudflare => new(
                GatewayHttpStatusKind.CloudflareTunnelUnavailable,
                numericStatusCode,
                $"Cloudflare Tunnel is not connected to the Gateway origin ({numericStatusCode}{reason}).",
                false),
            >= 500 => new(
                GatewayHttpStatusKind.ServerOrProxyError,
                numericStatusCode,
                $"Gateway or reverse proxy returned an error{proxyHint} ({numericStatusCode}{reason}).",
                false),
            _ => new(
                GatewayHttpStatusKind.Unexpected,
                numericStatusCode,
                $"Gateway returned an unexpected HTTP response{proxyHint} ({numericStatusCode}{reason}).",
                false),
        };
    }
}

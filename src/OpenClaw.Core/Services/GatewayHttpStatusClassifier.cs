// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

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
    private const int CloudflareTunnelUnavailableErrorCode = 1033;
    private const int MaxCloudflareErrorSnippetBytes = 8192;
    private static readonly TimeSpan CloudflareErrorSnippetReadTimeout = TimeSpan.FromSeconds(1);
    private static readonly string[] CloudflareErrorHeaderNames = ["cf-error-type", "cf-error-code"];

    public static GatewayHttpStatusClassification Classify(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        bool viaCloudflare,
        int? cloudflareErrorCode = null)
    {
        var numericStatusCode = (int)statusCode;
        var proxyHint = viaCloudflare ? " via Cloudflare" : string.Empty;
        var reason = string.IsNullOrWhiteSpace(reasonPhrase)
            ? string.Empty
            : $" {reasonPhrase.Trim()}";

        if (cloudflareErrorCode == CloudflareTunnelUnavailableErrorCode)
        {
            return new(
                GatewayHttpStatusKind.CloudflareTunnelUnavailable,
                numericStatusCode,
                $"Cloudflare error 1033 indicates no connected tunnel could reach the Gateway origin ({numericStatusCode}{reason}).",
                false);
        }

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
                false),
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
            1033 => new(
                GatewayHttpStatusKind.CloudflareTunnelUnavailable,
                numericStatusCode,
                $"Cloudflare error 1033 indicates no connected tunnel could reach the Gateway origin ({numericStatusCode}{reason}).",
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

    public static async Task<GatewayHttpStatusClassification> ClassifyResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var viaCloudflare = response.Headers.TryGetValues("cf-ray", out _);
        var cloudflareErrorHeaderValues = TryGetCloudflareErrorHeaderValues(response);
        var bodySnippet = ShouldInspectCloudflareErrorBody(response.StatusCode, viaCloudflare, cloudflareErrorHeaderValues)
            ? await ReadBodySnippetWithTimeoutAsync(response.Content, cancellationToken).ConfigureAwait(false)
            : null;
        var cloudflareErrorCode = TryDetectCloudflareErrorCode(cloudflareErrorHeaderValues, bodySnippet);

        return Classify(response.StatusCode, response.ReasonPhrase, viaCloudflare, cloudflareErrorCode);
    }

    public static int? TryDetectCloudflareErrorCode(
        IEnumerable<string>? cloudflareErrorHeaderValues,
        string? bodySnippet)
    {
        if (cloudflareErrorHeaderValues is not null)
        {
            foreach (var value in cloudflareErrorHeaderValues)
            {
                if (TryDetectCloudflare1033HeaderValue(value))
                {
                    return CloudflareTunnelUnavailableErrorCode;
                }
            }
        }

        return TryDetectCloudflare1033Body(bodySnippet)
            ? CloudflareTunnelUnavailableErrorCode
            : null;
    }

    private static bool ShouldInspectCloudflareErrorBody(
        HttpStatusCode statusCode,
        bool viaCloudflare,
        IEnumerable<string>? cloudflareErrorHeaderValues)
    {
        if (cloudflareErrorHeaderValues is not null && cloudflareErrorHeaderValues.Any())
        {
            return true;
        }

        var numericStatusCode = (int)statusCode;
        return viaCloudflare && numericStatusCode >= 400;
    }

    private static IEnumerable<string>? TryGetCloudflareErrorHeaderValues(HttpResponseMessage response)
    {
        List<string>? values = null;

        foreach (var headerName in CloudflareErrorHeaderNames)
        {
            var headerValues = TryGetHeaderValues(response, headerName);
            if (headerValues is null)
            {
                continue;
            }

            values ??= [];
            values.AddRange(headerValues);
        }

        return values;
    }

    private static IEnumerable<string>? TryGetHeaderValues(HttpResponseMessage response, string headerName)
    {
        List<string>? values = null;

        if (response.Headers.TryGetValues(headerName, out var responseValues))
        {
            values = [.. responseValues];
        }

        if (response.Content?.Headers.TryGetValues(headerName, out var contentValues) == true)
        {
            values ??= [];
            values.AddRange(contentValues);
        }

        return values;
    }

    private static async Task<string?> ReadBodySnippetWithTimeoutAsync(
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CloudflareErrorSnippetReadTimeout);

        try
        {
            return await ReadBodySnippetAsync(content, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<string?> ReadBodySnippetAsync(HttpContent? content, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return null;
        }

        try
        {
            await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[MaxCloudflareErrorSnippetBytes];
            var offset = 0;

            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(offset, buffer.Length - offset),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            return offset == 0 ? null : Encoding.UTF8.GetString(buffer, 0, offset);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryDetectCloudflare1033HeaderValue(string? value) =>
        string.Equals(value?.Trim(), "1033", StringComparison.OrdinalIgnoreCase);

    private static bool TryDetectCloudflare1033Body(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.Contains("1033", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Regex.IsMatch(value, @"\berror\s+(?:code\s*:?\s*)?1033\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(value, @"\bcf-error-code\b.{0,80}\b1033\b", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }
}

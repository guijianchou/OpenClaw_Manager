// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed class GatewayHeartbeatTransport
{
    private static readonly HttpClient HeartbeatHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<HeartbeatProbeResult> ProbeAsync(string url, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache, no-store, max-age=0");
            request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

            using var response = await HeartbeatHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var statusCode = (int)response.StatusCode;
            var proxyHint = response.Headers.TryGetValues("cf-ray", out _) ? " via Cloudflare" : string.Empty;

            return statusCode switch
            {
                >= 200 and < 300 => HeartbeatProbeResult.Healthy($"Gateway reachable over HTTP{proxyHint} ({statusCode})."),
                301 or 302 or 303 or 307 or 308 => HeartbeatProbeResult.Healthy(
                    $"Gateway reachable over HTTP{proxyHint} but redirected ({statusCode})."),
                401 or 403 => HeartbeatProbeResult.Healthy(
                    $"Gateway reachable over HTTP{proxyHint} but requires authentication or origin approval ({statusCode})."),
                404 => HeartbeatProbeResult.Failure(
                    $"Gateway Control UI path was not found over HTTP{proxyHint} ({statusCode} {response.ReasonPhrase})."),
                405 => HeartbeatProbeResult.Failure(
                    $"Gateway rejected the heartbeat probe over HTTP{proxyHint} ({statusCode} {response.ReasonPhrase})."),
                >= 500 => HeartbeatProbeResult.Failure(
                    $"Gateway returned a server/proxy error over HTTP{proxyHint} ({statusCode} {response.ReasonPhrase})."),
                _ => HeartbeatProbeResult.Failure(
                    $"Gateway returned an unexpected HTTP response{proxyHint} ({statusCode} {response.ReasonPhrase}).")
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HeartbeatProbeResult.Failure($"Gateway heartbeat request failed: {ex.Message}");
        }
    }
}

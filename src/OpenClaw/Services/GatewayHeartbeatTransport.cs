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
            var classification = GatewayHttpStatusClassifier.Classify(
                response.StatusCode,
                response.ReasonPhrase,
                response.Headers.TryGetValues("cf-ray", out _));

            return classification.Kind switch
            {
                GatewayHttpStatusKind.Reachable or
                GatewayHttpStatusKind.Redirected or
                GatewayHttpStatusKind.AccessRequired => HeartbeatProbeResult.Healthy(classification.Detail),
                GatewayHttpStatusKind.GatewayWaitingApproval or
                GatewayHttpStatusKind.AuthRateLimited => HeartbeatProbeResult.SessionBlocked(classification.Detail),
                _ => HeartbeatProbeResult.Failure(classification.Detail),
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

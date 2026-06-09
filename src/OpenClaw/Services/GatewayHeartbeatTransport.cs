// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Helpers;

namespace OpenClaw.Services;

internal sealed class GatewayHeartbeatTransport
{
    private static readonly HttpClient HeartbeatHttpClient = CreateHeartbeatHttpClient();

    public async Task<HeartbeatProbeResult> ProbeAsync(string url, CancellationToken token)
    {
        try
        {
            var probeUri = ControlUiProbeUriFactory.TryCreateConfigUri(url);
            if (probeUri is null)
            {
                return HeartbeatProbeResult.Failure(StringResources.HeartbeatInvalidControlUiUrl);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, probeUri);
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache, no-store, max-age=0");
            request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
            request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");

            using var response = await HeartbeatHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var classification = await GatewayHttpStatusClassifier.ClassifyResponseAsync(response, token);
            return GatewayHeartbeatProbeMapper.Map(classification);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HeartbeatProbeResult.Failure(
                string.Format(StringResources.HeartbeatRequestFailedFormat, ex.Message));
        }
    }

    private static HttpClient CreateHeartbeatHttpClient()
    {
        return new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }
}

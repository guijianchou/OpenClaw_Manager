// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Net.Http;

namespace OpenClaw.Services;

public enum GatewayDiagnosticProbeErrorKind
{
    None,
    InvalidUrl,
    Timeout,
    Unreachable,
    Unexpected,
}

public sealed record GatewayDiagnosticProbeResult(
    GatewayDiagnosticProbeSeverity Severity,
    GatewayHttpStatusKind Kind,
    int? StatusCode,
    string Detail,
    bool IsReachable,
    bool IsNonLocalHttp,
    GatewayDiagnosticProbeErrorKind ErrorKind,
    string? ReasonPhrase = null)
{
    public static GatewayDiagnosticProbeResult Failure(
        GatewayDiagnosticProbeErrorKind errorKind,
        string detail,
        bool isNonLocalHttp = false) =>
        new(
            GatewayDiagnosticProbeSeverity.Failure,
            GatewayHttpStatusKind.Unexpected,
            null,
            detail,
            false,
            isNonLocalHttp,
            errorKind);
}

public sealed class GatewayDiagnosticProbe : IDisposable
{
    private static readonly string[] LocalLoopbackHosts = ["127.0.0.1", "localhost", "::1"];
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;

    public GatewayDiagnosticProbe()
        : this(CreateHttpClient(), disposeHttpClient: true)
    {
    }

    public GatewayDiagnosticProbe(HttpMessageHandler messageHandler)
        : this(new HttpClient(messageHandler) { Timeout = TimeSpan.FromSeconds(10) }, disposeHttpClient: true)
    {
    }

    public GatewayDiagnosticProbe(HttpClient httpClient, bool disposeHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _disposeHttpClient = disposeHttpClient;
    }

    public async Task<GatewayDiagnosticProbeResult> ProbeAsync(
        string? gatewayUrl,
        CancellationToken cancellationToken = default)
    {
        var isNonLocalHttp = IsNonLocalHttp(gatewayUrl);
        var probeUri = ControlUiProbeUriFactory.TryCreateConfigUri(gatewayUrl);
        if (probeUri is null)
        {
            return GatewayDiagnosticProbeResult.Failure(
                GatewayDiagnosticProbeErrorKind.InvalidUrl,
                "Invalid Control UI URL.",
                isNonLocalHttp);
        }

        try
        {
            using var response = await _httpClient.GetAsync(
                probeUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var classification = await GatewayHttpStatusClassifier.ClassifyResponseAsync(
                response,
                cancellationToken).ConfigureAwait(false);
            var severity = ResolveSeverity(classification.Kind, isNonLocalHttp);

            return new GatewayDiagnosticProbeResult(
                severity,
                classification.Kind,
                classification.StatusCode,
                classification.Detail,
                classification.IsReachable,
                isNonLocalHttp,
                GatewayDiagnosticProbeErrorKind.None,
                response.ReasonPhrase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            return GatewayDiagnosticProbeResult.Failure(
                GatewayDiagnosticProbeErrorKind.Timeout,
                ex.Message,
                isNonLocalHttp);
        }
        catch (HttpRequestException ex)
        {
            return GatewayDiagnosticProbeResult.Failure(
                GatewayDiagnosticProbeErrorKind.Unreachable,
                ex.Message,
                isNonLocalHttp);
        }
        catch (Exception ex)
        {
            return GatewayDiagnosticProbeResult.Failure(
                GatewayDiagnosticProbeErrorKind.Unexpected,
                ex.Message,
                isNonLocalHttp);
        }
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static GatewayDiagnosticProbeSeverity ResolveSeverity(
        GatewayHttpStatusKind kind,
        bool isNonLocalHttp)
    {
        var severity = GatewayDiagnosticProbeMapper.Map(kind);
        return severity == GatewayDiagnosticProbeSeverity.Pass && isNonLocalHttp
            ? GatewayDiagnosticProbeSeverity.Warning
            : severity;
    }

    private static bool IsNonLocalHttp(string? gatewayUrl)
    {
        if (string.IsNullOrWhiteSpace(gatewayUrl) ||
            !Uri.TryCreate(gatewayUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IsLoopbackLike(uri);
    }

    private static bool IsLoopbackLike(Uri uri)
    {
        return uri.IsLoopback ||
            LocalLoopbackHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    private static HttpClient CreateHttpClient()
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

// Copyright (c) Lanstack @openclaw. All rights reserved.

using Microsoft.Web.WebView2.Core;
using OpenClaw.Helpers;

namespace OpenClaw.Services;

/// <summary>
/// Performs startup diagnostics: WebView2 runtime detection, network probes,
/// and auth/session invalidation detection.
/// </summary>
public class DiagnosticService
{
    private static readonly GatewayDiagnosticProbe SharedGatewayDiagnosticProbe = new();

    /// <summary>
    /// Checks whether the WebView2 runtime is installed and available.
    /// </summary>
    public static DiagnosticResult CheckWebView2Runtime(IAppLogger logger)
    {
        var version = GetWebView2RuntimeVersion(logger);
        if (string.IsNullOrEmpty(version))
        {
            return DiagnosticResult.Fail(
                StringResources.DiagnosticWebViewRuntimeNotFound,
                StringResources.DiagnosticWebViewRuntimeNotFoundDetail);
        }

        return DiagnosticResult.Pass($"{StringResources.DiagnosticWebView2RuntimeLabel} v{version}");
    }

    public static string? GetWebView2RuntimeVersion(IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception ex)
        {
            logger.Warning("diagnostics.webview2.runtime_version.failed", new { ex.Message });
            return null;
        }
    }

    /// <summary>
    /// Probes network connectivity to the given gateway URL.
    /// </summary>
    public static async Task<DiagnosticResult> ProbeNetworkAsync(
        string? gatewayUrl,
        ControlUiProbeSnapshot? snapshot = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(gatewayUrl))
        {
            return DiagnosticResult.Skip(StringResources.DiagnosticNoGatewayUrlConfigured);
        }

        var probeResult = await SharedGatewayDiagnosticProbe.ProbeAsync(gatewayUrl, cancellationToken);
        var nonLocalHttpDetail = GetNonLocalHttpWarningDetail(probeResult.IsNonLocalHttp);
        var statusCode = probeResult.StatusCode;

        if (probeResult.ErrorKind != GatewayDiagnosticProbeErrorKind.None)
        {
            return CreateNetworkErrorResult(probeResult, nonLocalHttpDetail);
        }

        return probeResult.Kind switch
        {
            GatewayHttpStatusKind.Reachable =>
                CreateNetworkDiagnosticResult(
                    probeResult.Severity,
                    string.Format(StringResources.DiagnosticHttpReachableFormat, statusCode),
                    StringResources.DiagnosticHttpReachableDetail,
                    snapshot,
                    nonLocalHttpDetail),
            GatewayHttpStatusKind.AccessRequired =>
                CreateNetworkDiagnosticResult(
                    probeResult.Severity,
                    string.Format(StringResources.DiagnosticAccessRejectedFormat, statusCode),
                    StringResources.DiagnosticAccessRejectedDetail,
                    snapshot,
                    nonLocalHttpDetail),
            GatewayHttpStatusKind.GatewayWaitingApproval =>
                CreateNetworkDiagnosticResult(
                    probeResult.Severity,
                    StringResources.DiagnosticGatewayWaitingApproval,
                    StringResources.DiagnosticGatewayWaitingApprovalDetail,
                    snapshot,
                    nonLocalHttpDetail),
            GatewayHttpStatusKind.AuthRateLimited =>
                CreateNetworkDiagnosticResult(
                    probeResult.Severity,
                    StringResources.DiagnosticAuthRateLimited,
                    StringResources.DiagnosticAuthRateLimitedDetail,
                    snapshot,
                    nonLocalHttpDetail),
            GatewayHttpStatusKind.MethodRejected =>
                CreateNetworkDiagnosticResult(
                    probeResult.Severity,
                    StringResources.DiagnosticMethodRejected,
                    StringResources.DiagnosticMethodRejectedDetail,
                    snapshot,
                    nonLocalHttpDetail),
            GatewayHttpStatusKind.Redirected =>
                CreateNetworkDiagnosticResult(
                    probeResult.Severity,
                    string.Format(StringResources.DiagnosticRedirectedFormat, statusCode),
                    StringResources.DiagnosticRedirectedDetail,
                    snapshot,
                    nonLocalHttpDetail),
            GatewayHttpStatusKind.MissingPath =>
                CreateNetworkDiagnosticResult(
                    probeResult.Severity,
                    StringResources.DiagnosticPathNotFound,
                    StringResources.DiagnosticPathNotFoundDetail,
                    snapshot,
                    nonLocalHttpDetail),
            GatewayHttpStatusKind.CloudflareTunnelUnavailable =>
                CreateNetworkDiagnosticResult(
                    probeResult.Severity,
                    string.Format(StringResources.DiagnosticGatewayReturnedFormat, statusCode, probeResult.ReasonPhrase),
                    StringResources.DiagnosticCloudflareTunnelUnavailableDetail,
                    snapshot,
                    nonLocalHttpDetail),
            GatewayHttpStatusKind.ServerOrProxyError =>
                CreateNetworkDiagnosticResult(
                    probeResult.Severity,
                    string.Format(StringResources.DiagnosticGatewayReturnedFormat, statusCode, probeResult.ReasonPhrase),
                    StringResources.DiagnosticGatewayReturnedServerFailureDetail,
                    snapshot,
                    nonLocalHttpDetail),
            _ =>
                CreateNetworkDiagnosticResult(
                    probeResult.Severity,
                    string.Format(StringResources.DiagnosticGatewayReturnedFormat, statusCode, probeResult.ReasonPhrase),
                    StringResources.DiagnosticGatewayReturnedDetail,
                    snapshot,
                    nonLocalHttpDetail)
        };
    }

    private static DiagnosticResult CreateNetworkDiagnosticResult(
        GatewayDiagnosticProbeSeverity severity,
        string message,
        string detail,
        ControlUiProbeSnapshot? snapshot = null,
        string? warningDetail = null)
    {
        var resolvedDetail = AppendHostedControlUiStateDetail(
            AppendDiagnosticDetail(detail, warningDetail),
            snapshot);
        return severity switch
        {
            GatewayDiagnosticProbeSeverity.Pass => new DiagnosticResult(DiagnosticStatus.Pass, message, resolvedDetail),
            GatewayDiagnosticProbeSeverity.Failure => DiagnosticResult.Fail(message, resolvedDetail),
            _ => DiagnosticResult.Warn(message, resolvedDetail),
        };
    }

    private static DiagnosticResult CreateNetworkErrorResult(
        GatewayDiagnosticProbeResult probeResult,
        string? warningDetail)
    {
        return probeResult.ErrorKind switch
        {
            GatewayDiagnosticProbeErrorKind.InvalidUrl => DiagnosticResult.Fail(
                StringResources.DiagnosticNetworkProbeFailed,
                AppendDiagnosticDetail(StringResources.DiagnosticInvalidControlUiUrlDetail, warningDetail)),
            GatewayDiagnosticProbeErrorKind.Timeout => DiagnosticResult.Fail(
                StringResources.DiagnosticGatewayTimeout,
                AppendDiagnosticDetail(StringResources.DiagnosticGatewayTimeoutDetail, warningDetail)),
            GatewayDiagnosticProbeErrorKind.Unreachable => DiagnosticResult.Fail(
                StringResources.DiagnosticGatewayUnreachable,
                AppendDiagnosticDetail(probeResult.Detail, warningDetail)),
            _ => DiagnosticResult.Fail(
                StringResources.DiagnosticNetworkProbeFailed,
                AppendDiagnosticDetail(probeResult.Detail, warningDetail)),
        };
    }

    private static string? GetNonLocalHttpWarningDetail(bool isNonLocalHttp)
    {
        if (!isNonLocalHttp)
        {
            return null;
        }

        return AppendDiagnosticDetail(
            StringResources.DiagnosticNonLocalHttp,
            StringResources.DiagnosticNonLocalHttpDetail);
    }

    private static string AppendDiagnosticDetail(string detail, string? additionalDetail)
    {
        return string.IsNullOrWhiteSpace(additionalDetail)
            ? detail
            : string.Join('\n', detail, additionalDetail);
    }

    private static string AppendHostedControlUiStateDetail(string detail, ControlUiProbeSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Phase is ControlUiPhase.Unknown or ControlUiPhase.Unavailable)
        {
            return detail;
        }

        var state = string.IsNullOrWhiteSpace(snapshot.DetailOrSummary)
            ? snapshot.Phase.ToString()
            : $"{snapshot.Phase}: {snapshot.DetailOrSummary}";
        var hostedStateDetail = string.Format(StringResources.DiagnosticHostedStateDetailFormat, state);
        return string.IsNullOrWhiteSpace(detail)
            ? hostedStateDetail
            : string.Join('\n', detail, hostedStateDetail);
    }

    /// <summary>
    /// Checks if common session indicators are present in the WebView2.
    /// Returns a hint about whether the session may be expired/invalid.
    /// </summary>
    public static async Task<DiagnosticResult> CheckSessionAsync(
        IDiagnosticWebViewSession webViewSession,
        ControlUiProbeSnapshot? snapshot = null,
        CancellationToken cancellationToken = default)
    {
        if (!webViewSession.IsInitialized)
        {
            return DiagnosticResult.Skip(StringResources.DiagnosticWebViewNotInitialized);
        }

        snapshot ??= await webViewSession.InspectControlUiStateAsync(cancellationToken);
        if (snapshot.Phase == ControlUiPhase.Unavailable)
        {
            return DiagnosticResult.Skip(
                StringResources.DiagnosticControlUiStateUnavailable,
                string.IsNullOrWhiteSpace(snapshot.Detail) ? null : snapshot.Detail);
        }

        return snapshot.Phase switch
        {
            ControlUiPhase.Connected => DiagnosticResult.Pass(StringResources.DiagnosticGatewaySessionAppearsActive),
            ControlUiPhase.PageLoaded or ControlUiPhase.GatewayConnecting => DiagnosticResult.Warn(
                snapshot.Summary,
                string.IsNullOrWhiteSpace(snapshot.Detail) ? StringResources.DiagnosticPageLoadedButEstablishing : snapshot.Detail),
            ControlUiPhase.AuthRequired => DiagnosticResult.Warn(snapshot.Summary, snapshot.DetailOrSummary),
            ControlUiPhase.PairingRequired => DiagnosticResult.Warn(
                snapshot.Summary,
                string.Format(StringResources.DiagnosticCurrentDeviceApprovalDetailFormat, snapshot.DetailOrSummary)),
            ControlUiPhase.OriginRejected => DiagnosticResult.Fail(
                snapshot.Summary,
                string.Format(StringResources.DiagnosticOriginRejectedFailDetailFormat, snapshot.DetailOrSummary)),
            ControlUiPhase.GatewayError => DiagnosticResult.Fail(snapshot.Summary, snapshot.DetailOrSummary),
            _ => DiagnosticResult.Skip(StringResources.DiagnosticNoPageLoaded)
        };
    }

    /// <summary>
    /// Runs all startup diagnostics and returns a summary.
    /// </summary>
    public static async Task<DiagnosticReport> RunAllAsync(
        string? gatewayUrl,
        IDiagnosticWebViewSession? webViewSession,
        IAppLogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var report = new DiagnosticReport();
        ControlUiProbeSnapshot? snapshot = null;

        report.Items.Add((StringResources.DiagnosticWebView2RuntimeLabel, CheckWebView2Runtime(logger)));

        if (webViewSession is not null)
        {
            snapshot = await webViewSession.InspectControlUiStateAsync(cancellationToken);
        }

        report.Items.Add((StringResources.DiagnosticNetworkConnectivityLabel, await ProbeNetworkAsync(gatewayUrl, snapshot, cancellationToken)));

        if (webViewSession is not null)
        {
            report.Items.Add((StringResources.DiagnosticSessionStatusLabel, await CheckSessionAsync(webViewSession, snapshot, cancellationToken)));
            report.Items.Add((StringResources.DiagnosticInstrumentationLabel, DescribeInstrumentation(webViewSession)));
        }

        return report;
    }

    public static DiagnosticResult DescribeInstrumentation(IDiagnosticWebViewSession webViewSession)
    {
        var snapshot = webViewSession.LatestControlUiSnapshot;
        var summary =
            $"Inspect req={webViewSession.TotalControlUiInspectionRequests}, " +
            $"cache={webViewSession.CachedControlUiInspectionRequests}, " +
            $"coalesced={webViewSession.CoalescedControlUiInspectionRequests}, " +
            $"hb reload={webViewSession.HeartbeatRecoveryRequests}, " +
            $"phase={snapshot.Phase}, " +
            $"busy={snapshot.IsBusy}, " +
            $"stale={snapshot.IsBusyStale}/{snapshot.BusyStaleSeconds}s, " +
            $"inputText={snapshot.FocusedInputHasText}.";

        return DiagnosticResult.Pass(summary);
    }

}

/// <summary>
/// Result of a single diagnostic check.
/// </summary>
public record DiagnosticResult(DiagnosticStatus Status, string Message, string? Detail = null)
{
    public static DiagnosticResult Pass(string message) => new(DiagnosticStatus.Pass, message);
    public static DiagnosticResult Warn(string message, string? detail = null) => new(DiagnosticStatus.Warning, message, detail);
    public static DiagnosticResult Fail(string message, string? detail = null) => new(DiagnosticStatus.Fail, message, detail);
    public static DiagnosticResult Skip(string message, string? detail = null) => new(DiagnosticStatus.Skipped, message, detail);
}

public enum DiagnosticStatus
{
    Pass,
    Warning,
    Fail,
    Skipped,
}

/// <summary>
/// Aggregated diagnostic report.
/// </summary>
public class DiagnosticReport
{
    public List<(string Name, DiagnosticResult Result)> Items { get; } = [];

    public bool HasFailures => Items.Any(i => i.Result.Status == DiagnosticStatus.Fail);
    public bool HasWarnings => Items.Any(i => i.Result.Status == DiagnosticStatus.Warning);

    public string ToSummary()
    {
        var lines = new System.Text.StringBuilder();
        foreach (var (name, result) in Items)
        {
            var icon = result.Status switch
            {
                DiagnosticStatus.Pass => StringResources.DiagnosticStatusPass,
                DiagnosticStatus.Warning => StringResources.DiagnosticStatusWarning,
                DiagnosticStatus.Fail => StringResources.DiagnosticStatusFail,
                _ => StringResources.DiagnosticStatusSkipped,
            };

            lines.AppendLine($"{icon} {name}: {result.Message}");
            if (!string.IsNullOrEmpty(result.Detail))
            {
                lines.AppendLine($"   {result.Detail}");
            }
        }

        return lines.ToString();
    }
}

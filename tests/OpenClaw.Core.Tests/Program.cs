// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.IO.Compression;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

namespace OpenClaw.Core.Tests;

/// <summary>
/// Lets standard VSTest-based workflows execute the same regression harness used by dotnet run.
/// </summary>
[TestClass]
public sealed class CoreRegressionHarnessTests
{
    [TestMethod]
    public async Task CoreRegressionHarnessPasses()
    {
        Assert.AreEqual(0, await Program.Main());
    }
}

internal static class Program
{
    public static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Test)[]
        {
            ("Cloudflare error 1033 body is detected under an HTTP 5xx response", () => Run(Cloudflare1033BodyIsDetected)),
            ("Cloudflare error 1033 response body is detected through production classifier entry point", Cloudflare1033ResponseBodyIsDetectedThroughProductionEntryPointAsync),
            ("Cloudflare error 1033 header is detected under an HTTP 5xx response", () => Run(Cloudflare1033HeaderIsDetected)),
            ("Cloudflare error 1033 header is detected through production classifier entry point", Cloudflare1033HeaderIsDetectedThroughProductionEntryPointAsync),
            ("Cloudflare error 1033 code header is detected through production classifier entry point", Cloudflare1033CodeHeaderIsDetectedThroughProductionEntryPointAsync),
            ("Plain 5xx response remains a server or proxy error", () => Run(Plain5xxRemainsServerOrProxyError)),
            ("Cloudflare branded 5xx body with unrelated 1033 remains a server or proxy error", CloudflareBrandedBodyWithUnrelated1033RemainsServerOrProxyErrorAsync),
            ("Cloudflare error body snippet read timeout falls back to status classification", CloudflareBodySnippetReadTimeoutFallsBackToStatusClassificationAsync),
            ("Control UI probe URI preserves base path and appends config endpoint", () => Run(ProbeUriPreservesBasePath)),
            ("Control UI probe URI appends config endpoint at root", () => Run(ProbeUriAppendsAtRoot)),
            ("Control UI probe URI is idempotent for an already configured endpoint", () => Run(ProbeUriIsIdempotent)),
            ("Control UI probe URI normalizes already configured endpoint without trailing slash", () => Run(ProbeUriNormalizesConfiguredEndpointWithoutTrailingSlash)),
            ("Control UI probe key distinguishes base paths and ports", () => Run(ProbeKeyDistinguishesBasePathsAndPorts)),
            ("Control UI probe URI strips userinfo before publishing probe URL or key", () => Run(ProbeUriStripsUserInfo)),
            ("Control UI probe URI rejects non-http schemes and relative URLs", () => Run(ProbeUriRejectsInvalidUrls)),
            ("Settings window bounds use the dedicated persisted width floor", () => Run(SettingsWindowBoundsUseDedicatedPersistedWidthFloor)),
            ("Settings window bounds reject unsafe narrow persisted widths", () => Run(SettingsWindowBoundsRejectUnsafeNarrowPersistedWidths)),
            ("Tray menu strings expose localized status and tooltip formats", () => Run(TrayMenuStringsExposeLocalizedStatusAndTooltipFormats)),
            ("Latency history clear removes stale samples", () => Run(LatencyHistoryClearRemovesStaleSamples)),
            ("Gateway classifier treats missing and method-rejected probe paths as failures", () => Run(ClassifierMarksMissingAndMethodRejectedPathsAsFailures)),
            ("Gateway classifier treats auth rate-limit as reachable user action", () => Run(ClassifierMarksAuthRateLimitAsReachableUserAction)),
            ("Gateway classifier treats reverse-proxy 5xx as unreachable", () => Run(ClassifierMarksServerOrProxyErrorsAsUnreachable)),
            ("Heartbeat maps access-required HTTP states to session-blocked", () => Run(HeartbeatMapsAccessRequiredToSessionBlocked)),
            ("Heartbeat maps auth rate-limit HTTP states to session-blocked", () => Run(HeartbeatMapsAuthRateLimitToSessionBlocked)),
            ("Heartbeat maps redirects to failure", () => Run(HeartbeatMapsRedirectsToFailure)),
            ("Heartbeat maps missing Control UI path to failure", () => Run(HeartbeatMapsMissingPathToFailure)),
            ("Latency service publishes redirect responses as failure", LatencyServicePublishesRedirectsAsFailureAsync),
            ("Latency service does not publish auth-required responses as success", LatencyServiceDoesNotPublishAuthRequiredAsSuccessAsync),
            ("Latency service publishes 2xx responses as success", LatencyServicePublishesSuccessAsync),
            ("Diagnostics mapper marks path and proxy failures as failures", () => Run(DiagnosticsMapperMarksPathAndProxyFailuresAsFailures)),
            ("Diagnostics mapper distinguishes pass, warning, and failure states", () => Run(DiagnosticsMapperDistinguishesPassWarningAndFailureStates)),
            ("Diagnostics mapper marks redirects as failures", () => Run(DiagnosticsMapperMarksRedirectsAsFailures)),
            ("Diagnostic bundle redacts copied log files", DiagnosticBundleRedactsCopiedLogFilesAsync),
            ("Diagnostic bundle uses unique paths for repeated exports", DiagnosticBundleUsesUniquePathsForRepeatedExportsAsync),
            ("Diagnostic network probe downgrades reachable non-local HTTP to warning", DiagnosticProbeDowngradesReachableNonLocalHttpToWarningAsync),
            ("Heartbeat resolver preserves hosted connecting state when transport fails", () => Run(HeartbeatResolverPreservesHostedConnectingStateWhenTransportFails)),
            ("Heartbeat resolver maps transport session-blocked to user action while preserving hosted detail", () => Run(HeartbeatResolverMapsTransportSessionBlockedToUserAction)),
            ("Session ready clears terminal recovery states", SessionReadyClearsTerminalRecoveryStatesAsync),
            ("Stale session ready does not clear current environment recovery state", StaleSessionReadyDoesNotClearCurrentEnvironmentRecoveryStateAsync),
            ("Hard refresh cooldown starts only after reload succeeds", HardRefreshCooldownStartsOnlyAfterReloadSucceedsAsync),
            ("Successful soft resync resets consecutive recovery attempts", SuccessfulSoftResyncResetsConsecutiveAttemptsAsync),
            ("Configuration normalizes invalid recovery policy values", ConfigurationNormalizesInvalidRecoveryPolicyValuesAsync),
            ("Configuration normalizes invalid environment entries", ConfigurationNormalizesInvalidEnvironmentEntriesAsync),
            ("Diagnostic bundle limits oversized log entries", DiagnosticBundleLimitsOversizedLogEntriesAsync),
            ("Diagnostic bundle limits total log payload and redacts headers", DiagnosticBundleLimitsTotalLogPayloadAndRedactsHeadersAsync),
        };

        var failures = new List<string>();
        foreach (var (name, test) in tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                var failure = $"{name}: {ex.Message}";
                failures.Add(failure);
                Console.Error.WriteLine($"FAIL {failure}");
            }
        }

        if (failures.Count == 0)
        {
            return 0;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"{failures.Count} test(s) failed:");
        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return 1;
    }

    private static Task Run(Action test)
    {
        test();
        return Task.CompletedTask;
    }

    private static void Cloudflare1033BodyIsDetected()
    {
        var detectedCode = GatewayHttpStatusClassifier.TryDetectCloudflareErrorCode(
            cloudflareErrorHeaderValues: null,
            bodySnippet: "<html><title>Error 1033</title><span class=\"cf-error-code\">1033</span></html>");

        var classification = GatewayHttpStatusClassifier.Classify(
            HttpStatusCode.ServiceUnavailable,
            "Service Unavailable",
            viaCloudflare: true,
            cloudflareErrorCode: detectedCode);

        AssertEqual(1033, detectedCode, "detectedCode");
        AssertEqual(GatewayHttpStatusKind.CloudflareTunnelUnavailable, classification.Kind, "classification.Kind");
        AssertEqual(503, classification.StatusCode, "classification.StatusCode");
        AssertFalse(classification.IsReachable, "classification.IsReachable");
    }

    private static void Cloudflare1033HeaderIsDetected()
    {
        var detectedCode = GatewayHttpStatusClassifier.TryDetectCloudflareErrorCode(["1033"], bodySnippet: null);
        var classification = GatewayHttpStatusClassifier.Classify(
            HttpStatusCode.BadGateway,
            "Bad Gateway",
            viaCloudflare: true,
            cloudflareErrorCode: detectedCode);

        AssertEqual(1033, detectedCode, "detectedCode");
        AssertEqual(GatewayHttpStatusKind.CloudflareTunnelUnavailable, classification.Kind, "classification.Kind");
        AssertEqual(502, classification.StatusCode, "classification.StatusCode");
    }

    private static async Task Cloudflare1033ResponseBodyIsDetectedThroughProductionEntryPointAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            ReasonPhrase = "Service Unavailable",
            Content = new StringContent("<html><title>Error 1033</title><span class=\"cf-error-code\">1033</span></html>"),
        };
        response.Headers.TryAddWithoutValidation("cf-ray", "7b6f-example-LAX");

        var classification = await GatewayHttpStatusClassifier.ClassifyResponseAsync(response);

        AssertEqual(GatewayHttpStatusKind.CloudflareTunnelUnavailable, classification.Kind, "classification.Kind");
        AssertEqual(503, classification.StatusCode, "classification.StatusCode");
        AssertFalse(classification.IsReachable, "classification.IsReachable");
    }

    private static async Task Cloudflare1033HeaderIsDetectedThroughProductionEntryPointAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            ReasonPhrase = "Bad Gateway",
            Content = new StringContent("generic edge body"),
        };
        response.Headers.TryAddWithoutValidation("cf-error-type", "1033");

        var classification = await GatewayHttpStatusClassifier.ClassifyResponseAsync(response);

        AssertEqual(GatewayHttpStatusKind.CloudflareTunnelUnavailable, classification.Kind, "classification.Kind");
        AssertEqual(502, classification.StatusCode, "classification.StatusCode");
        AssertFalse(classification.IsReachable, "classification.IsReachable");
    }

    private static async Task Cloudflare1033CodeHeaderIsDetectedThroughProductionEntryPointAsync()
    {
        using var response = new HttpResponseMessage((HttpStatusCode)530)
        {
            ReasonPhrase = "Cloudflare Error",
            Content = new StringContent("body read is not required for this test"),
        };
        response.Headers.TryAddWithoutValidation("cf-error-code", "1033");

        var classification = await GatewayHttpStatusClassifier.ClassifyResponseAsync(response);

        AssertEqual(GatewayHttpStatusKind.CloudflareTunnelUnavailable, classification.Kind, "classification.Kind");
        AssertEqual(530, classification.StatusCode, "classification.StatusCode");
        AssertFalse(classification.IsReachable, "classification.IsReachable");
    }

    private static void Plain5xxRemainsServerOrProxyError()
    {
        var detectedCode = GatewayHttpStatusClassifier.TryDetectCloudflareErrorCode(
            cloudflareErrorHeaderValues: null,
            bodySnippet: "Build 1033 failed in a backend log line.");

        var classification = GatewayHttpStatusClassifier.Classify(
            HttpStatusCode.InternalServerError,
            "Internal Server Error",
            viaCloudflare: true,
            cloudflareErrorCode: detectedCode);

        AssertNull(detectedCode, "detectedCode");
        AssertEqual(GatewayHttpStatusKind.ServerOrProxyError, classification.Kind, "classification.Kind");
    }

    private static async Task CloudflareBrandedBodyWithUnrelated1033RemainsServerOrProxyErrorAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            ReasonPhrase = "Service Unavailable",
            Content = new StringContent("<html><body>Cloudflare edge request id 1033. Retry later.</body></html>"),
        };
        response.Headers.TryAddWithoutValidation("cf-ray", "7b6f-example-LAX");

        var classification = await GatewayHttpStatusClassifier.ClassifyResponseAsync(response);

        AssertEqual(GatewayHttpStatusKind.ServerOrProxyError, classification.Kind, "classification.Kind");
        AssertEqual(503, classification.StatusCode, "classification.StatusCode");
        AssertFalse(classification.IsReachable, "classification.IsReachable");
    }

    private static async Task CloudflareBodySnippetReadTimeoutFallsBackToStatusClassificationAsync()
    {
        using var body = new BlockingReadStream("<html><title>Error 1033</title></html>");
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            ReasonPhrase = "Service Unavailable",
            Content = new StreamContent(body),
        };
        response.Headers.TryAddWithoutValidation("cf-ray", "7b6f-example-LAX");

        var classificationTask = GatewayHttpStatusClassifier.ClassifyResponseAsync(response);
        var completed = await Task.WhenAny(classificationTask, Task.Delay(TimeSpan.FromMilliseconds(2500)));
        body.Unblock();

        AssertEqual(classificationTask, completed, "classificationTask");
        var classification = await classificationTask;

        AssertEqual(GatewayHttpStatusKind.ServerOrProxyError, classification.Kind, "classification.Kind");
        AssertEqual(503, classification.StatusCode, "classification.StatusCode");
    }

    private static void ProbeUriPreservesBasePath()
    {
        var probeUri = ControlUiProbeUriFactory.TryCreateConfigUri("https://gateway.example.com/manager?token=redacted#top");

        AssertNotNull(probeUri, "probeUri");
        AssertEqual("https://gateway.example.com/manager/__openclaw__/a2ui/", probeUri!.ToString(), "probeUri");
    }

    private static void ProbeUriIsIdempotent()
    {
        var probeUri = ControlUiProbeUriFactory.TryCreateConfigUri("https://gateway.example.com/manager/__openclaw__/a2ui/");

        AssertNotNull(probeUri, "probeUri");
        AssertEqual("https://gateway.example.com/manager/__openclaw__/a2ui/", probeUri!.ToString(), "probeUri");
    }

    private static void ProbeUriAppendsAtRoot()
    {
        var probeUri = ControlUiProbeUriFactory.TryCreateConfigUri("https://gateway.example.com/?token=redacted#top");

        AssertNotNull(probeUri, "probeUri");
        AssertEqual("https://gateway.example.com/__openclaw__/a2ui/", probeUri!.ToString(), "probeUri");
    }

    private static void ProbeUriNormalizesConfiguredEndpointWithoutTrailingSlash()
    {
        var probeUri = ControlUiProbeUriFactory.TryCreateConfigUri("https://gateway.example.com/manager/__openclaw__/a2ui?token=redacted");

        AssertNotNull(probeUri, "probeUri");
        AssertEqual("https://gateway.example.com/manager/__openclaw__/a2ui/", probeUri!.ToString(), "probeUri");
    }

    private static void ProbeKeyDistinguishesBasePathsAndPorts()
    {
        var method = typeof(ControlUiProbeUriFactory).GetMethod(
            "TryCreateProbeKey",
            [typeof(string)]);
        AssertNotNull(method, "TryCreateProbeKey");

        var rootKey = (string?)method!.Invoke(null, ["https://gateway.example.com"]);
        var appKey = (string?)method.Invoke(null, ["https://gateway.example.com/app"]);
        var alternatePortKey = (string?)method.Invoke(null, ["https://gateway.example.com:8443/app"]);

        AssertEqual("https://gateway.example.com/__openclaw__/a2ui/", rootKey, "rootKey");
        AssertEqual("https://gateway.example.com/app/__openclaw__/a2ui/", appKey, "appKey");
        AssertEqual("https://gateway.example.com:8443/app/__openclaw__/a2ui/", alternatePortKey, "alternatePortKey");
        AssertNotEqual(rootKey, appKey, "rootKey/appKey");
        AssertNotEqual(appKey, alternatePortKey, "appKey/alternatePortKey");
    }

    private static void ProbeUriStripsUserInfo()
    {
        var probeUri = ControlUiProbeUriFactory.TryCreateConfigUri("https://user:pass@gateway.example.com/manager?token=redacted");
        var probeKey = ControlUiProbeUriFactory.TryCreateProbeKey("https://user:pass@gateway.example.com/manager?token=redacted");

        AssertNotNull(probeUri, "probeUri");
        AssertEqual("https://gateway.example.com/manager/__openclaw__/a2ui/", probeUri!.ToString(), "probeUri");
        AssertEqual("https://gateway.example.com/manager/__openclaw__/a2ui/", probeKey, "probeKey");
        AssertNotContains("user", probeUri.ToString(), "probeUri");
        AssertNotContains("pass", probeKey!, "probeKey");
    }

    private static void ProbeUriRejectsInvalidUrls()
    {
        AssertNull(ControlUiProbeUriFactory.TryCreateConfigUri("wss://gateway.example.com/manager"), "webSocketProbeUri");
        AssertNull(ControlUiProbeUriFactory.TryCreateConfigUri("/manager"), "relativeProbeUri");
        AssertNull(ControlUiProbeUriFactory.TryCreateConfigUri("file:///tmp/control-ui.html"), "fileProbeUri");
    }

    private static void SettingsWindowBoundsUseDedicatedPersistedWidthFloor()
    {
        AssertTrue(
            WindowBoundsUtilities.CanPersistWindowBounds(
                left: 100,
                top: 100,
                width: WindowBoundsUtilities.MinimumPersistedSettingsWindowWidth,
                height: WindowBoundsUtilities.MinimumPersistedSettingsWindowHeight,
                minimumWidth: WindowBoundsUtilities.MinimumPersistedSettingsWindowWidth,
                minimumHeight: WindowBoundsUtilities.MinimumPersistedSettingsWindowHeight),
            "minimumSettingsWidthAccepted");
        AssertFalse(
            WindowBoundsUtilities.CanPersistWindowBounds(
                left: 100,
                top: 100,
                width: WindowBoundsUtilities.MinimumPersistedSettingsWindowWidth - 1,
                height: WindowBoundsUtilities.MinimumPersistedSettingsWindowHeight,
                minimumWidth: WindowBoundsUtilities.MinimumPersistedSettingsWindowWidth,
                minimumHeight: WindowBoundsUtilities.MinimumPersistedSettingsWindowHeight),
            "belowMinimumSettingsWidthRejected");
    }

    private static void SettingsWindowBoundsRejectUnsafeNarrowPersistedWidths()
    {
        AssertTrue(
            WindowBoundsUtilities.MinimumPersistedSettingsWindowWidth >= 600,
            "minimumSettingsWidthAtLeastSafeLayoutWidth");
        AssertFalse(
            WindowBoundsUtilities.CanPersistWindowBounds(
                left: 100,
                top: 100,
                width: 599,
                height: WindowBoundsUtilities.MinimumPersistedSettingsWindowHeight,
                minimumWidth: WindowBoundsUtilities.MinimumPersistedSettingsWindowWidth,
                minimumHeight: WindowBoundsUtilities.MinimumPersistedSettingsWindowHeight),
            "narrowSettingsWidthRejected");
    }

    private static void TrayMenuStringsExposeLocalizedStatusAndTooltipFormats()
    {
        var statusHeaderProperty = typeof(TrayMenuStrings).GetProperty("StatusHeaderFormat");
        var tooltipProperty = typeof(TrayMenuStrings).GetProperty("TooltipFormat");

        AssertNotNull(statusHeaderProperty, "StatusHeaderFormat");
        AssertNotNull(tooltipProperty, "TooltipFormat");
        AssertEqual("Status: {0}", statusHeaderProperty!.GetValue(TrayMenuStrings.Default), "StatusHeaderFormat");
        AssertEqual("OpenClaw - {0}", tooltipProperty!.GetValue(TrayMenuStrings.Default), "TooltipFormat");
    }

    private static void LatencyHistoryClearRemovesStaleSamples()
    {
        var history = new LatencyHistory(3);
        history.Record(ControlUiLatencySnapshot.Success("gateway.example.com", 42));

        AssertEqual(1, history.CreateSummary().SampleCount, "sampleCountBeforeClear");

        history.Clear();

        AssertEqual(0, history.CreateSummary().SampleCount, "sampleCountAfterClear");
    }

    private static void ClassifierMarksMissingAndMethodRejectedPathsAsFailures()
    {
        var missingPath = GatewayHttpStatusClassifier.Classify(HttpStatusCode.NotFound, "Not Found", viaCloudflare: true);
        var methodRejected = GatewayHttpStatusClassifier.Classify(HttpStatusCode.MethodNotAllowed, "Method Not Allowed", viaCloudflare: true);

        AssertEqual(GatewayHttpStatusKind.MissingPath, missingPath.Kind, "missingPath.Kind");
        AssertFalse(missingPath.IsReachable, "missingPath.IsReachable");
        AssertEqual(GatewayHttpStatusKind.MethodRejected, methodRejected.Kind, "methodRejected.Kind");
        AssertFalse(methodRejected.IsReachable, "methodRejected.IsReachable");
    }

    private static void ClassifierMarksAuthRateLimitAsReachableUserAction()
    {
        var classification = GatewayHttpStatusClassifier.Classify((HttpStatusCode)429, "Too Many Requests", viaCloudflare: true);

        AssertEqual(GatewayHttpStatusKind.AuthRateLimited, classification.Kind, "classification.Kind");
        AssertTrue(classification.IsReachable, "classification.IsReachable");
    }

    private static void ClassifierMarksServerOrProxyErrorsAsUnreachable()
    {
        var classification = GatewayHttpStatusClassifier.Classify(HttpStatusCode.BadGateway, "Bad Gateway", viaCloudflare: true);

        AssertEqual(GatewayHttpStatusKind.ServerOrProxyError, classification.Kind, "classification.Kind");
        AssertFalse(classification.IsReachable, "classification.IsReachable");
    }

    private static void HeartbeatMapsAccessRequiredToSessionBlocked()
    {
        var classification = GatewayHttpStatusClassifier.Classify(
            HttpStatusCode.Forbidden,
            "Forbidden",
            viaCloudflare: true);

        var result = GatewayHeartbeatProbeMapper.Map(classification);

        AssertEqual(HeartbeatProbeStatus.SessionBlocked, result.Status, "result.Status");
    }

    private static void HeartbeatMapsAuthRateLimitToSessionBlocked()
    {
        var classification = GatewayHttpStatusClassifier.Classify(
            (HttpStatusCode)429,
            "Too Many Requests",
            viaCloudflare: true);

        var result = GatewayHeartbeatProbeMapper.Map(classification);

        AssertEqual(HeartbeatProbeStatus.SessionBlocked, result.Status, "result.Status");
    }

    private static void HeartbeatMapsRedirectsToFailure()
    {
        var classification = GatewayHttpStatusClassifier.Classify(
            HttpStatusCode.Redirect,
            "Found",
            viaCloudflare: true);

        var result = GatewayHeartbeatProbeMapper.Map(classification);

        AssertEqual(HeartbeatProbeStatus.Failure, result.Status, "result.Status");
        AssertFalse(classification.IsReachable, "classification.IsReachable");
    }

    private static void HeartbeatMapsMissingPathToFailure()
    {
        var classification = GatewayHttpStatusClassifier.Classify(
            HttpStatusCode.NotFound,
            "Not Found",
            viaCloudflare: true);

        var result = GatewayHeartbeatProbeMapper.Map(classification);

        AssertEqual(HeartbeatProbeStatus.Failure, result.Status, "result.Status");
    }

    private static async Task LatencyServicePublishesRedirectsAsFailureAsync()
    {
        using var service = new ControlUiLatencyService(
            new StaticResponseHandler(() => new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                ReasonPhrase = "Found",
            }),
            TimeSpan.FromMinutes(30));

        var snapshot = await CaptureLatencySnapshotAsync(service, "https://gateway.example.com/manager");

        AssertEqual(ControlUiLatencyState.Failure, snapshot.State, "snapshot.State");
        AssertFalse(snapshot.IsSuccess, "snapshot.IsSuccess");
        AssertEqual("gateway.example.com", snapshot.Host, "snapshot.Host");
    }

    private static async Task LatencyServiceDoesNotPublishAuthRequiredAsSuccessAsync()
    {
        using var service = new ControlUiLatencyService(
            new StaticResponseHandler(() => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = "Unauthorized",
            }),
            TimeSpan.FromSeconds(30));

        var snapshot = await CaptureLatencySnapshotAsync(service, "https://gateway.example.com/manager");

        AssertEqual(ControlUiLatencyState.Failure, snapshot.State, "snapshot.State");
        AssertFalse(snapshot.IsSuccess, "snapshot.IsSuccess");
        AssertContains("requires authentication", snapshot.Detail ?? string.Empty, "snapshot.Detail");
    }

    private static async Task LatencyServicePublishesSuccessAsync()
    {
        using var service = new ControlUiLatencyService(
            new StaticResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                ReasonPhrase = "OK",
            }),
            TimeSpan.FromMinutes(30));

        var snapshot = await CaptureLatencySnapshotAsync(service, "https://gateway.example.com/manager");

        AssertEqual(ControlUiLatencyState.Success, snapshot.State, "snapshot.State");
        AssertTrue(snapshot.IsSuccess, "snapshot.IsSuccess");
        AssertEqual("gateway.example.com", snapshot.Host, "snapshot.Host");
    }

    private static async Task<ControlUiLatencySnapshot> CaptureLatencySnapshotAsync(
        ControlUiLatencyService service,
        string controlUiUrl)
    {
        var completion = new TaskCompletionSource<ControlUiLatencySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.LatencyUpdated += snapshot => completion.TrySetResult(snapshot);
        service.Start(controlUiUrl);

        var completed = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        if (completed != completion.Task)
        {
            throw new InvalidOperationException("Timed out waiting for latency snapshot.");
        }

        return await completion.Task;
    }

    private static void DiagnosticsMapperMarksPathAndProxyFailuresAsFailures()
    {
        AssertEqual(GatewayDiagnosticProbeSeverity.Failure, GatewayDiagnosticProbeMapper.Map(GatewayHttpStatusKind.MissingPath), "missingPath");
        AssertEqual(GatewayDiagnosticProbeSeverity.Failure, GatewayDiagnosticProbeMapper.Map(GatewayHttpStatusKind.MethodRejected), "methodRejected");
        AssertEqual(GatewayDiagnosticProbeSeverity.Failure, GatewayDiagnosticProbeMapper.Map(GatewayHttpStatusKind.CloudflareTunnelUnavailable), "cloudflareTunnelUnavailable");
        AssertEqual(GatewayDiagnosticProbeSeverity.Failure, GatewayDiagnosticProbeMapper.Map(GatewayHttpStatusKind.ServerOrProxyError), "serverOrProxyError");
    }

    private static void DiagnosticsMapperDistinguishesPassWarningAndFailureStates()
    {
        AssertEqual(GatewayDiagnosticProbeSeverity.Pass, GatewayDiagnosticProbeMapper.Map(GatewayHttpStatusKind.Reachable), "reachable");
        AssertEqual(GatewayDiagnosticProbeSeverity.Warning, GatewayDiagnosticProbeMapper.Map(GatewayHttpStatusKind.AccessRequired), "accessRequired");
        AssertEqual(GatewayDiagnosticProbeSeverity.Warning, GatewayDiagnosticProbeMapper.Map(GatewayHttpStatusKind.GatewayWaitingApproval), "gatewayWaitingApproval");
        AssertEqual(GatewayDiagnosticProbeSeverity.Warning, GatewayDiagnosticProbeMapper.Map(GatewayHttpStatusKind.AuthRateLimited), "authRateLimited");
        AssertEqual(GatewayDiagnosticProbeSeverity.Warning, GatewayDiagnosticProbeMapper.Map(GatewayHttpStatusKind.Unexpected), "unexpected");
    }

    private static void DiagnosticsMapperMarksRedirectsAsFailures()
    {
        AssertEqual(GatewayDiagnosticProbeSeverity.Failure, GatewayDiagnosticProbeMapper.Map(GatewayHttpStatusKind.Redirected), "redirected");
    }

    private static async Task DiagnosticBundleRedactsCopiedLogFilesAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"openclaw-tests-{Guid.NewGuid():N}");
        var logsDirectory = Path.Combine(tempRoot, "logs");
        var outputDirectory = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(logsDirectory);

        try
        {
            var logPath = Path.Combine(logsDirectory, "openclaw-20260608.log");
            await File.WriteAllTextAsync(
                logPath,
                "Navigating to: https://tenant.example.com/private/control?token=abc123 " +
                "\"refreshSecret\":\"super-secret\"");

            var outputPath = await DiagnosticBundleService.ExportBundleAsync(
                "{\"gatewayUrl\":\"https://tenant.example.com/private/control?token=abc123\"}",
                logsDirectory,
                "Summary mentions https://tenant.example.com/private/control?token=abc123",
                outputDirectory,
                CreateRuntimeInfo());

            var logEntry = await ReadZipEntryAsync(outputPath, "logs/openclaw-20260608.log");
            var summaryEntry = await ReadZipEntryAsync(outputPath, "diagnostic-summary.txt");

            AssertNotContains("tenant.example.com", logEntry, "logEntry");
            AssertNotContains("/private/control", logEntry, "logEntry");
            AssertNotContains("abc123", logEntry, "logEntry");
            AssertNotContains("super-secret", logEntry, "logEntry");
            AssertContains("https://<host>/<path>", logEntry, "logEntry");
            AssertNotContains("tenant.example.com", summaryEntry, "summaryEntry");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task DiagnosticBundleUsesUniquePathsForRepeatedExportsAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"openclaw-tests-{Guid.NewGuid():N}");
        var logsDirectory = Path.Combine(tempRoot, "logs");
        var outputDirectory = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(logsDirectory);

        try
        {
            var first = await DiagnosticBundleService.ExportBundleAsync(
                "{}",
                logsDirectory,
                "first",
                outputDirectory,
                CreateRuntimeInfo());
            var second = await DiagnosticBundleService.ExportBundleAsync(
                "{}",
                logsDirectory,
                "second",
                outputDirectory,
                CreateRuntimeInfo());

            AssertNotEqual(first, second, "bundlePath");
            AssertTrue(File.Exists(first), "firstBundleExists");
            AssertTrue(File.Exists(second), "secondBundleExists");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task DiagnosticProbeDowngradesReachableNonLocalHttpToWarningAsync()
    {
        using var probe = new GatewayDiagnosticProbe(
            new StaticResponseHandler(
                () => new HttpResponseMessage(HttpStatusCode.OK) { ReasonPhrase = "OK" },
                "http://gateway.example.com/manager/__openclaw__/a2ui/"));

        var result = await probe.ProbeAsync("http://gateway.example.com/manager");

        AssertEqual(GatewayDiagnosticProbeSeverity.Warning, result.Severity, "result.Severity");
        AssertEqual(GatewayHttpStatusKind.Reachable, result.Kind, "result.Kind");
        AssertTrue(result.IsNonLocalHttp, "result.IsNonLocalHttp");
    }

    private static void HeartbeatResolverPreservesHostedConnectingStateWhenTransportFails()
    {
        var result = HeartbeatProbeResolver.Resolve(
            HeartbeatProbeResult.Connecting("hosted connecting"),
            HeartbeatProbeResult.Failure("transport failed"));

        AssertEqual(HeartbeatProbeStatus.Connecting, result.Status, "result.Status");
        AssertContains("hosted connecting", result.Message, "result.Message");
        AssertContains("transport failed", result.Message, "result.Message");
    }

    private static void HeartbeatResolverMapsTransportSessionBlockedToUserAction()
    {
        var result = HeartbeatProbeResolver.Resolve(
            HeartbeatProbeResult.Connecting("hosted connecting"),
            HeartbeatProbeResult.SessionBlocked("transport auth required"));

        AssertEqual(HeartbeatProbeStatus.SessionBlocked, result.Status, "result.Status");
        AssertContains("hosted connecting", result.Message, "result.Message");
        AssertContains("transport auth required", result.Message, "result.Message");
    }

    private static async Task SessionReadyClearsTerminalRecoveryStatesAsync()
    {
        var webView = new FakeShellSessionWebView();
        var bridge = new FakeShellSessionBridge();
        using var coordinator = await CreateAttachedCoordinatorAsync(webView, bridge);

        webView.RaiseControlUiSnapshot(new ControlUiProbeSnapshot(
            ControlUiPhase.AuthRequired,
            "auth",
            "auth required",
            "https://gateway.example.com",
            true,
            false,
            false,
            "idle",
            string.Empty));

        AssertEqual(RecoveryState.AuthIssue, coordinator.CurrentRecoveryState, "authStateBeforeReady");
        bridge.RaiseSessionReady();
        AssertEqual(RecoveryState.Ready, coordinator.CurrentRecoveryState, "authStateAfterReady");

        webView.RaiseControlUiSnapshot(ControlUiProbeSnapshot.Unavailable("proxy unavailable"));
        AssertEqual(RecoveryState.Degraded, coordinator.CurrentRecoveryState, "degradedStateBeforeReady");
        bridge.RaiseSessionReady();
        AssertEqual(RecoveryState.Ready, coordinator.CurrentRecoveryState, "degradedStateAfterReady");

        webView.InspectDelay = TimeSpan.FromSeconds(5);
        var hardRefreshTask = coordinator.RequestHardRefreshAsync("test hard refresh");
        await EventuallyAsync(
            () => coordinator.CurrentRecoveryState == RecoveryState.Refreshing,
            "hard refresh state should become Refreshing");

        bridge.RaiseSessionReady();
        AssertEqual(RecoveryState.Ready, coordinator.CurrentRecoveryState, "refreshingStateAfterReady");
        webView.CancelInspection();
        await hardRefreshTask;
    }

    private static async Task StaleSessionReadyDoesNotClearCurrentEnvironmentRecoveryStateAsync()
    {
        var webView = new FakeShellSessionWebView();
        var bridge = new FakeShellSessionBridge();
        using var coordinator = await CreateAttachedCoordinatorAsync(webView, bridge);

        webView.RaiseControlUiSnapshot(new ControlUiProbeSnapshot(
            ControlUiPhase.AuthRequired,
            "auth",
            "auth required",
            "https://gateway.example.com",
            true,
            false,
            false,
            "idle",
            string.Empty));

        AssertEqual(RecoveryState.AuthIssue, coordinator.CurrentRecoveryState, "stateBeforeStaleReady");
        bridge.RaiseSessionReady("https://old.example.com");
        AssertEqual(RecoveryState.AuthIssue, coordinator.CurrentRecoveryState, "stateAfterStaleReady");

        bridge.RaiseSessionReady("https://gateway.example.com");
        AssertEqual(RecoveryState.Ready, coordinator.CurrentRecoveryState, "stateAfterCurrentReady");
    }

    private static async Task HardRefreshCooldownStartsOnlyAfterReloadSucceedsAsync()
    {
        var webView = new FakeShellSessionWebView
        {
            Snapshot = new ControlUiProbeSnapshot(
                ControlUiPhase.AuthRequired,
                "auth",
                "auth required",
                "https://gateway.example.com",
                true,
                false,
                false,
                "idle",
                string.Empty),
        };
        var bridge = new FakeShellSessionBridge();
        using var coordinator = await CreateAttachedCoordinatorAsync(
            webView,
            bridge,
            new RecoveryPolicyOptions
            {
                HardRefreshCooldownSeconds = 300,
            });

        await coordinator.RequestHardRefreshAsync("auth fallback");
        AssertEqual(0, webView.ReloadRequests, "reloadRequestsAfterFallback");

        webView.Snapshot = ControlUiProbeSnapshot.Unavailable("proxy unavailable");
        await coordinator.RequestHardRefreshAsync("actual hard refresh");
        AssertEqual(1, webView.ReloadRequests, "reloadRequestsAfterActualRefresh");
    }

    private static async Task SuccessfulSoftResyncResetsConsecutiveAttemptsAsync()
    {
        var webView = new FakeShellSessionWebView
        {
            Snapshot = ConnectedSnapshot(),
        };
        var bridge = new FakeShellSessionBridge();
        using var coordinator = await CreateAttachedCoordinatorAsync(
            webView,
            bridge,
            new RecoveryPolicyOptions
            {
                MaxSoftResyncAttempts = 2,
                MaxReconnectAttempts = 2,
            });

        await coordinator.RequestSoftResyncAsync("first");
        await coordinator.RequestSoftResyncAsync("second");

        webView.RaiseControlUiSnapshot(ConnectedSnapshot() with
        {
            IsBusyStale = true,
            BusyStaleSeconds = 180,
        });

        await EventuallyAsync(
            () => bridge.LightweightSyncRequests >= 3 || webView.ReloadRequests > 0,
            "busy-stale recovery should schedule a follow-up action");

        AssertEqual(0, webView.ReloadRequests, "reloadRequests");
        AssertEqual(3, bridge.LightweightSyncRequests, "lightweightSyncRequests");
    }

    private static async Task ConfigurationNormalizesInvalidRecoveryPolicyValuesAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"openclaw-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempRoot, "settings.json"),
                """
                {
                    "environments": [
                        {
                            "name": "Default",
                            "gatewayUrl": "https://gateway.example.com",
                            "isDefault": true
                        }
                    ],
                    "selectedEnvironmentName": "Default",
                    "recoveryPolicy": {
                        "backgroundResumeThresholdSeconds": -5,
                        "maxReconnectAttempts": 0,
                        "maxSoftResyncAttempts": 0,
                        "eventIdleSuspicionSeconds": -1,
                        "transportIdleSuspicionSeconds": -2,
                        "reconnectDelayMs": -10,
                        "reconnectBackoffMultiplier": 0,
                        "maxReconnectDelayMs": -1,
                        "hardRefreshCooldownSeconds": -30
                    }
                }
                """);

            var configuration = new ConfigurationService(tempRoot);
            configuration.Load();
            var recovery = configuration.Settings.RecoveryPolicy;

            AssertTrue(recovery.BackgroundResumeThresholdSeconds >= 0, "BackgroundResumeThresholdSeconds");
            AssertTrue(recovery.MaxReconnectAttempts >= 1, "MaxReconnectAttempts");
            AssertTrue(recovery.MaxSoftResyncAttempts >= 1, "MaxSoftResyncAttempts");
            AssertTrue(recovery.EventIdleSuspicionSeconds >= 0, "EventIdleSuspicionSeconds");
            AssertTrue(recovery.TransportIdleSuspicionSeconds >= 0, "TransportIdleSuspicionSeconds");
            AssertTrue(recovery.ReconnectDelayMs >= 0, "ReconnectDelayMs");
            AssertTrue(recovery.ReconnectBackoffMultiplier >= 1, "ReconnectBackoffMultiplier");
            AssertTrue(recovery.MaxReconnectDelayMs >= recovery.ReconnectDelayMs, "MaxReconnectDelayMs");
            AssertTrue(recovery.HardRefreshCooldownSeconds >= 0, "HardRefreshCooldownSeconds");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task ConfigurationNormalizesInvalidEnvironmentEntriesAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"openclaw-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempRoot, "settings.json"),
                """
                {
                    "environments": [
                        {
                            "name": "   ",
                            "gatewayUrl": "https://blank-name.example.com",
                            "isDefault": true
                        },
                        {
                            "name": " Prod ",
                            "gatewayUrl": " https://prod.example.com/app ",
                            "isDefault": true
                        },
                        {
                            "name": "Prod",
                            "gatewayUrl": "https://prod-alt.example.com/app",
                            "isDefault": true
                        },
                        {
                            "name": "Broken",
                            "gatewayUrl": "   ",
                            "isDefault": false
                        }
                    ],
                    "selectedEnvironmentName": " Missing "
                }
                """);

            var configuration = new ConfigurationService(tempRoot);
            configuration.Load();

            AssertTrue(configuration.Settings.Environments.Count >= 2, "environmentCount");
            AssertFalse(configuration.Settings.Environments.Any(env => string.IsNullOrWhiteSpace(env.Name)), "blankNamesRemoved");
            AssertFalse(configuration.Settings.Environments.Any(env => string.IsNullOrWhiteSpace(env.GatewayUrl)), "blankUrlsRemoved");
            AssertEqual(
                configuration.Settings.Environments.Count,
                configuration.Settings.Environments.Select(env => env.Name).Distinct(StringComparer.Ordinal).Count(),
                "distinctNames");
            AssertEqual(1, configuration.Settings.Environments.Count(env => env.IsDefault), "defaultCount");
            AssertTrue(
                configuration.Settings.Environments.Any(env => string.Equals(env.Name, configuration.Settings.SelectedEnvironmentName, StringComparison.Ordinal)),
                "selectedEnvironmentExists");
            AssertTrue(
                configuration.Settings.Environments.All(env => env.Name == env.Name.Trim() && env.GatewayUrl == env.GatewayUrl.Trim()),
                "namesAndUrlsTrimmed");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task DiagnosticBundleLimitsOversizedLogEntriesAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"openclaw-tests-{Guid.NewGuid():N}");
        var logsDirectory = Path.Combine(tempRoot, "logs");
        var outputDirectory = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(logsDirectory);

        try
        {
            var logPath = Path.Combine(logsDirectory, "openclaw-20260608.log");
            await File.WriteAllTextAsync(logPath, new string('x', 6 * 1024 * 1024));

            var outputPath = await DiagnosticBundleService.ExportBundleAsync(
                "{}",
                logsDirectory,
                string.Empty,
                outputDirectory,
                CreateRuntimeInfo());

            using var archive = ZipFile.OpenRead(outputPath);
            var logEntry = archive.GetEntry("logs/openclaw-20260608.log");
            var notesEntry = archive.GetEntry("diagnostic-bundle-notes.txt");

            AssertNull(logEntry, "oversizedLogEntry");
            AssertNotNull(notesEntry, "notesEntry");
            var notes = await ReadZipEntryAsync(outputPath, "diagnostic-bundle-notes.txt");
            AssertContains("openclaw-20260608.log", notes, "notes");
            AssertContains("skipped", notes, "notes");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task DiagnosticBundleLimitsTotalLogPayloadAndRedactsHeadersAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"openclaw-tests-{Guid.NewGuid():N}");
        var logsDirectory = Path.Combine(tempRoot, "logs");
        var outputDirectory = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(logsDirectory);

        try
        {
            for (var i = 0; i < 4; i++)
            {
                var logPath = Path.Combine(logsDirectory, $"openclaw-2026060{i}.log");
                var content = string.Join(Environment.NewLine, new[]
                {
                    $"Authorization: Bearer secret-token-{i}",
                    $"Cookie: session=secret-cookie-{i}; theme=dark",
                    $"Set-Cookie: refresh=secret-refresh-{i}",
                    new string('x', 3 * 1024 * 1024),
                });
                await File.WriteAllTextAsync(logPath, content);
            }

            var outputPath = await DiagnosticBundleService.ExportBundleAsync(
                "{\"authorization\":\"Bearer settings-secret\"}",
                logsDirectory,
                "Authorization: Basic summary-secret",
                outputDirectory,
                CreateRuntimeInfo());

            using var archive = ZipFile.OpenRead(outputPath);
            var copiedLogs = archive.Entries.Count(entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal));
            var notes = await ReadZipEntryAsync(outputPath, "diagnostic-bundle-notes.txt");
            var firstLog = archive.Entries.First(entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal)).FullName;
            var logText = await ReadZipEntryAsync(outputPath, firstLog);
            var summary = await ReadZipEntryAsync(outputPath, "diagnostic-summary.txt");
            var settings = await ReadZipEntryAsync(outputPath, "settings-redacted.json");

            AssertTrue(copiedLogs < 4, "copiedLogs");
            AssertContains("skipped", notes, "notes");
            AssertNotContains("secret-token", logText, "logText");
            AssertNotContains("secret-cookie", logText, "logText");
            AssertNotContains("secret-refresh", logText, "logText");
            AssertNotContains("summary-secret", summary, "summary");
            AssertNotContains("settings-secret", settings, "settings");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static DiagnosticRuntimeInfo CreateRuntimeInfo() =>
        new(
            WebView2RuntimeVersion: "test-webview2",
            OsVersion: "test-os",
            DotNetVersion: "test-dotnet",
            AppVersion: "test-app",
            ProcessArchitecture: "x64",
            ProcessorCount: 8,
            MachineHash: "test-machine");

    private static ControlUiProbeSnapshot ConnectedSnapshot() =>
        new(
            ControlUiPhase.Connected,
            "connected",
            string.Empty,
            "https://gateway.example.com",
            true,
            false,
            false,
            "idle",
            "test-model");

    private static async Task<ShellSessionCoordinator> CreateAttachedCoordinatorAsync(
        FakeShellSessionWebView webView,
        FakeShellSessionBridge bridge,
        RecoveryPolicyOptions? recoveryOptions = null)
    {
        var coordinator = new ShellSessionCoordinator();
        await coordinator.AttachAsync(
            webView,
            bridge,
            recoveryOptions ?? new RecoveryPolicyOptions(),
            new HeartbeatOptions(),
            new TestLogger());
        coordinator.SetEnvironment("Default", "https://gateway.example.com");
        return coordinator;
    }

    private static async Task<string> ReadZipEntryAsync(string zipPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(entryName);
        AssertNotNull(entry, entryName);
        await using var stream = entry!.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'.");
        }
    }

    private static void AssertFalse(bool actual, string name)
    {
        if (actual)
        {
            throw new InvalidOperationException($"{name}: expected false.");
        }
    }

    private static void AssertTrue(bool actual, string name)
    {
        if (!actual)
        {
            throw new InvalidOperationException($"{name}: expected true.");
        }
    }

    private static void AssertNull<T>(T? actual, string name)
    {
        if (actual is not null)
        {
            throw new InvalidOperationException($"{name}: expected null, got '{actual}'.");
        }
    }

    private static void AssertNotNull<T>(T? actual, string name)
    {
        if (actual is null)
        {
            throw new InvalidOperationException($"{name}: expected non-null.");
        }
    }

    private static void AssertNotEqual<T>(T? first, T? second, string name)
    {
        if (EqualityComparer<T?>.Default.Equals(first, second))
        {
            throw new InvalidOperationException($"{name}: expected values to differ, both were '{first}'.");
        }
    }

    private static void AssertContains(string expected, string actual, string name)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name}: expected to contain '{expected}', got '{actual}'.");
        }
    }

    private static void AssertNotContains(string unexpected, string actual, string name)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name}: expected not to contain '{unexpected}', got '{actual}'.");
        }
    }

    private static async Task EventuallyAsync(Func<bool> predicate, string message)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException(message);
    }

    private sealed class StaticResponseHandler(
        Func<HttpResponseMessage> createResponse,
        string expectedRequestUri = "https://gateway.example.com/manager/__openclaw__/a2ui/") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AssertEqual(expectedRequestUri, request.RequestUri?.ToString(), "request.RequestUri");
            return Task.FromResult(createResponse());
        }
    }

    private sealed class FakeShellSessionWebView : IShellSessionWebView
    {
        private CancellationTokenSource? _inspectionCancellation;

        public event Action<ConnectionState>? ConnectionStateChanged;
        public event Action<string>? NavigationErrorOccurred;
        public event Action<string?>? NavigationCompleted;
        public event Action<HeartbeatProbeResult>? HeartbeatObserved;
        public event Action<string>? HeartbeatFailed;
        public event Action<ControlUiProbeSnapshot>? ControlUiSnapshotUpdated;

        public ControlUiProbeSnapshot Snapshot { get; set; } = ConnectedSnapshot();

        public TimeSpan InspectDelay { get; set; }

        public int ReloadRequests { get; private set; }

        public int TotalControlUiInspectionRequests { get; private set; }

        public int CachedControlUiInspectionRequests => 0;

        public int CoalescedControlUiInspectionRequests => 0;

        public int HeartbeatRecoveryRequests => 0;

        public async Task<ControlUiProbeSnapshot> InspectControlUiStateAsync(CancellationToken cancellationToken)
        {
            TotalControlUiInspectionRequests++;
            if (InspectDelay > TimeSpan.Zero)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _inspectionCancellation = linked;
                await Task.Delay(InspectDelay, linked.Token);
            }

            return Snapshot;
        }

        public Task<bool> ReloadAsync(CancellationToken cancellationToken)
        {
            ReloadRequests++;
            return Task.FromResult(true);
        }

        public void RaiseControlUiSnapshot(ControlUiProbeSnapshot snapshot)
        {
            Snapshot = snapshot;
            ControlUiSnapshotUpdated?.Invoke(snapshot);
        }

        public void CancelInspection()
        {
            _inspectionCancellation?.Cancel();
            InspectDelay = TimeSpan.Zero;
        }

        public void RaiseConnectionState(ConnectionState state) => ConnectionStateChanged?.Invoke(state);

        public void RaiseNavigationError(string message) => NavigationErrorOccurred?.Invoke(message);

        public void RaiseNavigationCompleted(string? uri) => NavigationCompleted?.Invoke(uri);

        public void RaiseHeartbeatObserved(HeartbeatProbeResult result) => HeartbeatObserved?.Invoke(result);

        public void RaiseHeartbeatFailed(string message) => HeartbeatFailed?.Invoke(message);
    }

    private sealed class TestLogger : IAppLogger
    {
        public void Info(string message) { }

        public void Warning(string message) { }

        public void Error(string message) { }

        public void Info(string eventKey, object? context = null) { }

        public void Warning(string eventKey, object? context = null) { }

        public void Error(string eventKey, object? context = null) { }
    }

    private sealed class FakeShellSessionBridge : IShellSessionBridge
    {
        public event Action<SessionReadyEventArgs>? SessionReady;
        public event Action<EventGapEventArgs>? EventGapDetected;

        public int LightweightSyncRequests { get; private set; }

        public Task<bool> RequestSessionRefreshAsync(CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> RequestRecentMessagesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> RequestLightweightSyncAsync(CancellationToken cancellationToken)
        {
            LightweightSyncRequests++;
            return Task.FromResult(true);
        }

        public Task<bool> NotifyReconnectIntentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public void RaiseSessionReady(string? uri = null) =>
            SessionReady?.Invoke(new SessionReadyEventArgs(
                DateTimeOffset.UtcNow.ToString("O"),
                "test-model",
                uri ?? "https://gateway.example.com",
                "test"));

        public void RaiseEventGap() =>
            EventGapDetected?.Invoke(new EventGapEventArgs(
                ExpectedSeq: 1,
                GotSeq: 3,
                LastStateVersion: "1",
                CurrentStateVersion: "3",
                DetectedAt: DateTimeOffset.UtcNow.ToString("O")));
    }

    private sealed class BlockingReadStream(string text) : Stream
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(text);
        private readonly TaskCompletionSource _unblocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _offset;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _bytes.Length;

        public override long Position
        {
            get => _offset;
            set => throw new NotSupportedException();
        }

        public void Unblock() => _unblocked.TrySetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _unblocked.Task.WaitAsync(cancellationToken);
            if (_offset >= _bytes.Length)
            {
                return 0;
            }

            var count = Math.Min(buffer.Length, _bytes.Length - _offset);
            _bytes.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush()
        {
        }
    }
}

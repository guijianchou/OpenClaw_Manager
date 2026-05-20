using System.Net;
using System.Reflection;
using OpenClaw.Helpers;
using OpenClaw.Models;
using OpenClaw.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Reset cancels in-flight reconnect", Tests.ResetCancelsInFlightReconnectAsync),
    ("Reconnect falls back to reload when in-page recovery stalls", Tests.ReconnectFallsBackToReloadWhenInPageRecoveryDoesNotReviveSessionAsync),
    ("Input-focused reload defer does not record successful recovery", Tests.InputFocusedReloadDeferDoesNotRecordSuccessAsync),
    ("Input-focused empty editor does not block hard refresh", Tests.InputFocusedEmptyEditorDoesNotBlockHardRefreshAsync),
    ("Stale busy snapshot requests soft resync", Tests.StaleBusySnapshotRequestsSoftResyncAsync),
    ("Stale busy snapshot escalates to hard refresh after soft resync budget", Tests.StaleBusySnapshotEscalatesToHardRefreshAfterSoftResyncBudgetAsync),
    ("Soft resync unsupported marks recovery degraded", Tests.SoftResyncUnsupportedMarksRecoveryDegradedAsync),
    ("Event gap requests soft resync while session is alive", Tests.EventGapRequestsSoftResyncAsync),
    ("Event gap escalates to hard refresh when attempts are exhausted", Tests.EventGapEscalatesToHardRefreshAsync),
    ("Auth issue short-circuits recovery requests", Tests.AuthIssueShortCircuitsRecoveryAsync),
    ("Hard refresh short-circuits on auth issue", Tests.HardRefreshShortCircuitsOnAuthIssueAsync),
    ("Background resume reconnects when session is gone", Tests.BackgroundResumeReconnectsWhenSessionIsGoneAsync),
    ("Background resume skips reconnect when session is still alive", Tests.BackgroundResumeSkipsReconnectWhenSessionAliveAsync),
    ("Hard refresh respects cooldown throttling", Tests.HardRefreshRespectsCooldownAsync),
    ("Reset clears recovery attempt totals", Tests.ResetClearsRecoveryAttemptTotalsAsync),
    ("Deferred save flushes requests queued during write", Tests.DeferredSaveFlushesRequestsQueuedDuringWriteAsync),
    ("Settings load normalizes null option sections", Tests.SettingsLoadNormalizesNullOptionSections),
    ("Latency probe requests Control UI config endpoint", Tests.LatencyProbeRequestsControlUiConfigAsync),
    ("Latency probe cancellation completes background task", Tests.LatencyProbeCancellationCompletesBackgroundTaskAsync),
    ("Latency history tooltip summarizes recent samples", Tests.LatencyHistoryTooltipSummarizesRecentSamples),
    ("Tray close policy hides to tray until exit is requested", Tests.TrayClosePolicyHidesToTrayUntilExitRequested),
    ("Tray close policy respects close-to-tray setting", Tests.TrayClosePolicyRespectsCloseToTraySetting),
    ("Settings load defaults tray options on", Tests.SettingsLoadDefaultsTrayOptionsOn),
    ("Settings load rejects minimized window sentinel bounds", Tests.SettingsLoadRejectsMinimizedWindowSentinelBounds),
    ("Main window skips persisting minimized or hidden bounds", Tests.MainWindowSkipsPersistingMinimizedOrHiddenBounds),
    ("Settings default disables multiple instances", Tests.SettingsDefaultDisablesMultipleInstances),
    ("Settings General exposes multiple instances option", Tests.SettingsGeneralExposesMultipleInstancesOption),
    ("Settings boolean options use PowerToys-style rows", Tests.SettingsBooleanOptionsUsePowerToysStyleRows),
    ("Settings environment edit keeps apply action", Tests.SettingsEnvironmentEditKeepsApplyAction),
    ("Settings always-on-top strings are localized", Tests.SettingsAlwaysOnTopStringsAreLocalized),
    ("Settings switch rows use compact spacing", Tests.SettingsSwitchRowsUseCompactSpacing),
    ("App startup honors multiple instance setting", Tests.AppStartupHonorsMultipleInstanceSetting),
    ("Settings navigation places General after Language", Tests.SettingsNavigationPlacesGeneralAfterLanguage),
    ("Version metadata is 3.3.3", Tests.VersionMetadataIs333),
    ("Repository code style is explicit", Tests.RepositoryCodeStyleIsExplicit),
    ("Directory build enables analyzers and style", Tests.DirectoryBuildEnablesAnalyzersAndStyle),
    ("Executable test harness rejects dotnet test false positives", Tests.ExecutableTestHarnessRejectsDotnetTestFalsePositives),
    ("Documentation includes WinUI format platform", Tests.DocumentationIncludesWinUiFormatPlatform),
    ("About dialog repository link targets OpenClaw Manager repo", Tests.AboutDialogRepositoryLinkTargetsOpenClawManagerRepo),
    ("Settings window uses non-blocking frame refresh", Tests.SettingsWindowUsesNonBlockingFrameRefresh),
    ("Settings window avoids first-frame black flash", Tests.SettingsWindowAvoidsFirstFrameBlackFlash),
    ("Title bar caption button states use opaque theme colors", Tests.TitleBarCaptionButtonStatesUseOpaqueThemeColors),
    ("Top status pill leaves room for long model names", Tests.TopStatusPillLeavesRoomForLongModelNames),
    ("Top status model text matches status bar font size", Tests.TopStatusModelTextMatchesStatusBarFontSize),
    ("Settings window is prewarmed after startup", Tests.SettingsWindowIsPrewarmedAfterStartup),
    ("Settings language selection syncs after load", Tests.SettingsLanguageSelectionSyncsAfterLoad),
    ("Settings language options are code populated", Tests.SettingsLanguageOptionsAreCodePopulated),
    ("Settings prewarm is requeued after close", Tests.SettingsPrewarmIsRequeuedAfterClose),
    ("Tray Win32 Unicode entry points declare Unicode marshalling", Tests.TrayWin32UnicodeEntryPointsDeclareUnicodeMarshalling),
    ("Tray callback reads NOTIFYICON_VERSION_4 event low word", Tests.TrayCallbackReadsNotifyIconVersion4EventLowWord),
    ("Tray context menu uses localized command labels", Tests.TrayContextMenuUsesLocalizedCommandLabels),
    ("Tray context menu uses popup-capable owner window", Tests.TrayContextMenuUsesPopupCapableOwnerWindow),
    ("Tray menu strings are injected and accessible", Tests.TrayMenuStringsAreInjectedAndAccessible),
    ("Tray menu strings default fallback uses English", Tests.TrayMenuStringsDefaultFallbackUsesEnglish),
    ("Tray menu exposes reload and view logs commands", Tests.TrayMenuExposesReloadAndViewLogsCommands),
    ("Tray menu status header reflects work status", Tests.TrayMenuStatusHeaderReflectsWorkStatus),
    ("Hosted UI bridge reads current model from OpenClaw model select", Tests.HostedUiBridgeReadsCurrentModelFromOpenClawModelSelect),
    ("Hosted UI bridge reads current model from OpenClaw app state", Tests.HostedUiBridgeReadsCurrentModelFromOpenClawAppState),
    ("Hosted UI bridge uses structured model source pipeline", Tests.HostedUiBridgeUsesStructuredModelSourcePipeline),
    ("Hosted UI bridge defers app-state defaults", Tests.HostedUiBridgeDefersAppStateDefaults),
    ("Hosted UI bridge keeps null override default semantics", Tests.HostedUiBridgeKeepsNullOverrideDefaultSemantics),
    ("Hosted UI bridge avoids object-shaped model strings", Tests.HostedUiBridgeAvoidsObjectShapedModelStrings),
    ("Hosted UI snapshots carry model source instrumentation", Tests.HostedUiSnapshotsCarryModelSourceInstrumentation),
    ("Hosted UI session ready carries model source", Tests.HostedUiSessionReadyCarriesModelSource),
    ("Hosted UI bridge ignores sidebar-only mutations during status polling", Tests.HostedUiBridgeIgnoresSidebarOnlyMutationsDuringStatusPolling),
    ("Hosted UI bridge ignores settings and cron mutation storms", Tests.HostedUiBridgeIgnoresSettingsAndCronMutationStorms),
    ("Hosted UI bridge reports stale busy and input text state", Tests.HostedUiBridgeReportsStaleBusyAndInputTextState),
    ("Main view model preserves known model on empty snapshots", Tests.MainViewModelPreservesKnownModelOnEmptySnapshots),
    ("HotkeyBinding parses standard modifier+key string", Tests.HotkeyBindingParsesStandardModifierKeyString),
    ("HotkeyBinding round-trips through ToString", Tests.HotkeyBindingRoundTripsThroughToString),
    ("HotkeyBinding parse returns null for empty or invalid input", Tests.HotkeyBindingParseReturnsNullForInvalidInput),
    ("HotkeyBinding parse handles single key without modifier", Tests.HotkeyBindingParseSingleKeyWithoutModifier),
    ("AppSettings defaults hotkey to Ctrl+Alt+Space enabled", Tests.AppSettingsDefaultsHotkeyToCtrlAltSpaceEnabled),
    ("Settings load without hotkey fields uses defaults", Tests.SettingsLoadWithoutHotkeyFieldsUsesDefaults),
    ("Settings Shell exposes global hotkey controls", Tests.SettingsShellExposesGlobalHotkeyControls),
    ("SettingsViewModel persists and validates global hotkey fields", Tests.SettingsViewModelPersistsAndValidatesGlobalHotkeyFields),
    ("Diagnostic instrumentation includes hosted UI stream state", Tests.DiagnosticInstrumentationIncludesHostedUiStreamState),
    ("DiagnosticBundle redacts gateway URL host", Tests.DiagnosticBundleRedactsGatewayUrlHost),
    ("DiagnosticBundle redacts token-like values", Tests.DiagnosticBundleRedactsTokenLikeValues),
    ("DiagnosticBundle includes runtime info", Tests.DiagnosticBundleIncludesRuntimeInfo),
    ("DiagnosticBundle collects recent log files", Tests.DiagnosticBundleCollectsRecentLogFiles),
    ("CloudflareRay parses PoP from standard cf-ray header", Tests.CloudflareRayParsesPopFromStandardHeader),
    ("CloudflareRay returns null for missing or malformed header", Tests.CloudflareRayReturnsNullForMissingOrMalformed),
    ("Latency tooltip includes PoP when available", Tests.LatencyTooltipIncludesPopWhenAvailable),
    ("SingleInstance stop awaits listener task completion", Tests.SingleInstanceStopAwaitsListenerTaskCompletion),
    ("AppSettings defaults AlwaysOnTop to false", Tests.AppSettingsDefaultsAlwaysOnTopToFalse),
    ("Always-on-top applies native topmost fallback", Tests.AlwaysOnTopAppliesNativeTopmostFallback),
    ("Always-on-top pin button uses accent color when active", Tests.AlwaysOnTopPinButtonUsesAccentColorWhenActive),
    ("AppSettings defaults CompactMode to false", Tests.AppSettingsDefaultsCompactModeToFalse),
    ("Compact mode bounds bypass minimum persistable size", Tests.CompactModeBoundsBypassMinimumPersistableSize),
    ("WebView circuit breaker trips after repeated failures", Tests.WebViewCircuitBreakerTripsAfterRepeatedFailures),
    ("WebView circuit breaker resets after cooldown", Tests.WebViewCircuitBreakerResetsAfterCooldown),
    ("Window hide restores minimized placement first", Tests.WindowHideRestoresMinimizedPlacementFirst),
    ("Atomic writer replaces existing content", Tests.AtomicWriterReplacesExistingContent),
    ("Log tail reader returns the final lines", Tests.LogTailReaderReturnsFinalLines),
    ("Log retention removes only expired OpenClaw logs", Tests.LogRetentionRemovesOnlyExpiredOpenClawLogs),
};

var failures = new List<string>();

foreach (var (name, run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"[PASS] {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"[FAIL] {name}");
        Console.WriteLine(ex);
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Failures:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    Environment.ExitCode = 1;
}

internal static class Tests
{
    public static async Task ResetCancelsInFlightReconnectAsync()
    {
        var webView = new FakeShellSessionWebView();
        var bridge = new FakeShellSessionBridge();
        var coordinator = CreateCoordinator(webView, bridge, new RecoveryPolicyOptions
        {
            ReconnectDelayMs = 5_000,
            MaxReconnectDelayMs = 5_000,
            ReconnectBackoffMultiplier = 1
        });

        var recoveryTask = coordinator.RequestReconnectAsync("test-reset-cancel");
        await Task.Delay(50);

        coordinator.Reset();
        await recoveryTask;

        Assert.Equal(RecoveryState.Connecting, coordinator.CurrentRecoveryState, "Reset should restore connecting state.");
        Assert.False(coordinator.IsRecoveryInProgress, "Recovery should not remain in progress after reset.");
        Assert.Equal(0, webView.ReloadCount, "Reconnect should be cancelled before reload.");

        var telemetry = coordinator.GetTelemetrySnapshot();
        Assert.Null(telemetry.LastRecoveryReason, "Reset should clear the last recovery reason.");
        Assert.Null(telemetry.LastRecoveryStartedAt, "Reset should clear the last recovery timestamp.");
    }

    public static async Task ReconnectFallsBackToReloadWhenInPageRecoveryDoesNotReviveSessionAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("post-in-page reconnect still unavailable"));
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("pre-reload still unavailable"));

        var bridge = new FakeShellSessionBridge
        {
            ReconnectIntentResult = true
        };

        var coordinator = CreateCoordinator(webView, bridge, new RecoveryPolicyOptions
        {
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            ReconnectBackoffMultiplier = 1,
            HardRefreshCooldownSeconds = 0
        });

        await coordinator.RequestReconnectAsync("fallback");

        var telemetry = coordinator.GetTelemetrySnapshot();
        Assert.Equal(1, bridge.NotifyReconnectIntentCalls, "Reconnect should notify the hosted UI before falling back.");
        Assert.Equal(1, bridge.RequestSessionRefreshCalls, "Reconnect should still request an in-page refresh before reload.");
        Assert.Equal(2, webView.InspectCount, "Reconnect fallback should inspect both post-in-page and pre-reload snapshots.");
        Assert.Equal(1, webView.ReloadCount, "Unavailable reconnect fallback should reload the page.");
        Assert.Equal(RecoveryState.Connecting, coordinator.CurrentRecoveryState, "Reload fallback should leave the coordinator connecting.");
        Assert.Equal(1, telemetry.TotalReconnectAttempts, "Reconnect fallback should count the reconnect attempt.");
        Assert.Equal("fallback", telemetry.LastRecoveryReason, "Reconnect fallback should preserve the triggering reason.");
    }

    public static async Task InputFocusedReloadDeferDoesNotRecordSuccessAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(new ControlUiProbeSnapshot(
            ControlUiPhase.GatewayError,
            "Gateway error",
            "stream unavailable",
            "https://gateway.example",
            ShellDetected: true,
            IsBusy: false,
            InputFocused: true,
            WorkState: "unknown",
            CurrentModel: "gpt-test")
        {
            FocusedInputHasText = true,
        });
        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge());

        await coordinator.RequestHardRefreshAsync("input-focused");

        var telemetry = coordinator.GetTelemetrySnapshot();
        Assert.Equal(RecoveryState.Degraded, telemetry.CurrentRecoveryState, "Focused input with unsent text should defer reload and mark recovery degraded.");
        Assert.Null(telemetry.LastSuccessfulRecoveryAt, "Deferred reload should not be recorded as a successful recovery.");
        Assert.Equal(0, webView.ReloadCount, "Deferred reload should not reload the page.");
    }

    public static async Task InputFocusedEmptyEditorDoesNotBlockHardRefreshAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(new ControlUiProbeSnapshot(
            ControlUiPhase.GatewayError,
            "Gateway error",
            "stream unavailable",
            "https://gateway.example",
            ShellDetected: true,
            IsBusy: false,
            InputFocused: true,
            WorkState: "unknown",
            CurrentModel: "gpt-test")
        {
            FocusedInputHasText = false,
        });
        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge());

        await coordinator.RequestHardRefreshAsync("input-focused-empty");

        Assert.Equal(1, webView.ReloadCount, "Focused but empty chat input should not block recovery reload.");
        Assert.Equal(RecoveryState.Connecting, coordinator.CurrentRecoveryState, "Hard refresh should leave the coordinator reconnecting.");
    }

    public static async Task StaleBusySnapshotRequestsSoftResyncAsync()
    {
        var webView = new FakeShellSessionWebView();
        var bridge = new FakeShellSessionBridge
        {
            LightweightSyncResult = true,
            RecentMessagesResult = true,
        };
        var coordinator = CreateCoordinator(webView, bridge);

        webView.EnqueueSnapshot(new ControlUiProbeSnapshot(
            ControlUiPhase.Connected,
            "Connected",
            string.Empty,
            "https://gateway.example",
            ShellDetected: true,
            IsBusy: false,
            InputFocused: false,
            WorkState: "idle",
            CurrentModel: "gpt-test"));
        webView.RaiseControlUiSnapshotUpdated(new ControlUiProbeSnapshot(
            ControlUiPhase.Connected,
            "Connected",
            string.Empty,
            "https://gateway.example",
            ShellDetected: true,
            IsBusy: true,
            InputFocused: false,
            WorkState: "busy",
            CurrentModel: "gpt-test")
        {
            IsBusyStale = true,
            BusyStaleSeconds = 35,
            ActivitySignature = "run-123:assistant:42",
        });

        await WaitUntilAsync(() => bridge.RequestLightweightSyncCalls == 1, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => coordinator.CurrentRecoveryState == RecoveryState.Ready, TimeSpan.FromSeconds(3));

        Assert.Equal(1, bridge.RequestRecentMessagesCalls, "Stale busy recovery should also fetch recent messages.");
        Assert.Equal(0, webView.ReloadCount, "Stale busy recovery should attempt soft resync before reload.");
        Assert.Equal(RecoveryState.Ready, coordinator.CurrentRecoveryState, "Successful stale busy soft resync should return the coordinator to ready.");
    }

    public static async Task StaleBusySnapshotEscalatesToHardRefreshAfterSoftResyncBudgetAsync()
    {
        var webView = new FakeShellSessionWebView();
        var bridge = new FakeShellSessionBridge();
        var coordinator = CreateCoordinator(webView, bridge, new RecoveryPolicyOptions
        {
            MaxSoftResyncAttempts = 0,
            HardRefreshCooldownSeconds = 0,
        });
        var staleSnapshot = new ControlUiProbeSnapshot(
            ControlUiPhase.Connected,
            "Connected",
            string.Empty,
            "https://gateway.example",
            ShellDetected: true,
            IsBusy: true,
            InputFocused: false,
            WorkState: "busy",
            CurrentModel: "gpt-test")
        {
            IsBusyStale = true,
            BusyStaleSeconds = 45,
            ActivitySignature = "run-456:assistant:42",
        };

        webView.EnqueueSnapshot(staleSnapshot);
        webView.RaiseControlUiSnapshotUpdated(staleSnapshot);

        await WaitUntilAsync(() => webView.ReloadCount == 1, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => coordinator.CurrentRecoveryState == RecoveryState.Connecting, TimeSpan.FromSeconds(3));

        Assert.Equal(0, bridge.RequestLightweightSyncCalls, "Exhausted stale busy recovery should skip additional soft resync attempts.");
        Assert.Equal(0, bridge.RequestRecentMessagesCalls, "Exhausted stale busy recovery should reload instead of repeatedly fetching recent messages.");
    }

    public static async Task SoftResyncUnsupportedMarksRecoveryDegradedAsync()
    {
        var webView = new FakeShellSessionWebView();
        var bridge = new FakeShellSessionBridge();
        var coordinator = CreateCoordinator(webView, bridge);

        await coordinator.RequestSoftResyncAsync("unsupported");

        var telemetry = coordinator.GetTelemetrySnapshot();
        Assert.Equal(1, bridge.RequestLightweightSyncCalls, "Soft resync should try lightweight sync first.");
        Assert.Equal(1, bridge.RequestRecentMessagesCalls, "Soft resync should also try recent messages before giving up.");
        Assert.Equal(RecoveryState.Degraded, coordinator.CurrentRecoveryState, "Unsupported soft resync should degrade recovery state.");
        Assert.Equal(0, webView.ReloadCount, "Unsupported soft resync should not reload the page.");
        Assert.Equal(1, telemetry.TotalSoftResyncAttempts, "Unsupported soft resync should count the attempted recovery.");
        Assert.Equal("unsupported", telemetry.LastRecoveryReason, "Unsupported soft resync should keep the triggering reason.");
        Assert.Null(telemetry.LastSuccessfulRecoveryAt, "Unsupported soft resync should not record success.");
    }

    public static async Task EventGapRequestsSoftResyncAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.PageLoaded("https://gateway.example"));
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.PageLoaded("https://gateway.example"));

        var bridge = new FakeShellSessionBridge
        {
            LightweightSyncResult = true,
            RecentMessagesResult = true,
        };

        var coordinator = CreateCoordinator(webView, bridge, new RecoveryPolicyOptions
        {
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            ReconnectBackoffMultiplier = 1
        });

        bridge.RaiseEventGap(new EventGapEventArgs(10, 12, "v1", "v2", DateTimeOffset.UtcNow.ToString("O")));
        await Task.Delay(1_800);

        Assert.Equal(1, bridge.RequestLightweightSyncCalls, "Alive session gap should attempt lightweight sync first.");
        Assert.Equal(1, bridge.RequestRecentMessagesCalls, "Alive session gap should also request recent messages.");
        Assert.Equal(0, webView.ReloadCount, "Soft resync path should not reload the page.");
        Assert.Equal(RecoveryState.Ready, coordinator.CurrentRecoveryState, "Successful soft resync should return coordinator to ready.");
    }

    public static async Task EventGapEscalatesToHardRefreshAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("gap-detected"));
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("hard-refresh"));

        var bridge = new FakeShellSessionBridge();
        var coordinator = CreateCoordinator(webView, bridge, new RecoveryPolicyOptions
        {
            MaxReconnectAttempts = 0,
            MaxSoftResyncAttempts = 0,
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            ReconnectBackoffMultiplier = 1
        });

        bridge.RaiseEventGap(new EventGapEventArgs(3, 9, "v1", "v2", DateTimeOffset.UtcNow.ToString("O")));
        await Task.Delay(1_300);

        Assert.Equal(1, webView.ReloadCount, "Exhausted gap recovery should escalate to hard refresh reload.");
        Assert.Equal(RecoveryState.Connecting, coordinator.CurrentRecoveryState, "Hard refresh should leave the coordinator reconnecting.");
    }

    public static async Task AuthIssueShortCircuitsRecoveryAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(CreateAuthRequiredSnapshot());

        var bridge = new FakeShellSessionBridge();
        var coordinator = CreateCoordinator(webView, bridge);

        bridge.RaiseEventGap(new EventGapEventArgs(1, 5, "v1", "v2", DateTimeOffset.UtcNow.ToString("O")));
        await Task.Delay(150);

        Assert.Equal(RecoveryState.AuthIssue, coordinator.CurrentRecoveryState, "Auth issues should short-circuit recovery.");
        Assert.Equal(0, bridge.RequestLightweightSyncCalls, "Auth short-circuit should avoid sync requests.");
        Assert.Equal(0, bridge.NotifyReconnectIntentCalls, "Auth short-circuit should avoid reconnect requests.");
        Assert.Equal(0, webView.ReloadCount, "Auth short-circuit should avoid reloads.");
    }

    public static async Task HardRefreshShortCircuitsOnAuthIssueAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(CreateAuthRequiredSnapshot());

        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge());

        await coordinator.RequestHardRefreshAsync("auth-refresh");

        var telemetry = coordinator.GetTelemetrySnapshot();
        Assert.Equal(RecoveryState.AuthIssue, coordinator.CurrentRecoveryState, "Hard refresh should stop when auth is required.");
        Assert.Equal(0, webView.ReloadCount, "Auth-gated hard refresh should not reload the page.");
        Assert.Equal(1, telemetry.TotalHardRefreshAttempts, "Auth short-circuit should still count the attempted hard refresh.");
        Assert.Equal("auth-refresh", telemetry.LastRecoveryReason, "Auth short-circuit should preserve the triggering reason.");
        Assert.Null(telemetry.LastSuccessfulRecoveryAt, "Auth short-circuit should not record a successful recovery.");
    }

    public static async Task BackgroundResumeReconnectsWhenSessionIsGoneAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("background-resume"));
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("pre-reload"));

        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge(), new RecoveryPolicyOptions
        {
            EnableBackgroundResume = true,
            BackgroundResumeThresholdSeconds = 0,
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            ReconnectBackoffMultiplier = 1,
            HardRefreshCooldownSeconds = 0
        });

        coordinator.OnHostHidden();
        await Task.Delay(20);
        await coordinator.OnHostVisibleAsync();
        await Task.Delay(100);

        Assert.Equal(1, webView.ReloadCount, "Background resume should trigger reconnect reload when the session is gone.");
        Assert.Equal(RecoveryState.Connecting, coordinator.CurrentRecoveryState, "Reconnect path should leave the coordinator connecting.");
        Assert.Equal("Background resume threshold exceeded", coordinator.GetTelemetrySnapshot().LastRecoveryReason, "Reconnect reason should be recorded.");
    }

    public static async Task BackgroundResumeSkipsReconnectWhenSessionAliveAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.PageLoaded("https://gateway.example"));

        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge(), new RecoveryPolicyOptions
        {
            EnableBackgroundResume = true,
            BackgroundResumeThresholdSeconds = 0,
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            ReconnectBackoffMultiplier = 1,
            HardRefreshCooldownSeconds = 0
        });

        coordinator.OnHostHidden();
        await Task.Delay(20);
        await coordinator.OnHostVisibleAsync();

        Assert.Equal(0, webView.ReloadCount, "Healthy background resume should not trigger reconnect reload.");
        Assert.Equal(RecoveryState.Connecting, coordinator.CurrentRecoveryState, "Skipped background resume should leave the existing state untouched.");
        Assert.Null(coordinator.GetTelemetrySnapshot().LastRecoveryReason, "Skipped background resume should not record a new recovery reason.");
    }

    public static async Task HardRefreshRespectsCooldownAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("hard-refresh"));

        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge(), new RecoveryPolicyOptions
        {
            ReconnectDelayMs = 1,
            MaxReconnectDelayMs = 1,
            ReconnectBackoffMultiplier = 1,
            HardRefreshCooldownSeconds = 3600
        });

        await coordinator.RequestHardRefreshAsync("first-refresh");
        await coordinator.RequestHardRefreshAsync("second-refresh");

        var telemetry = coordinator.GetTelemetrySnapshot();
        Assert.Equal(1, webView.ReloadCount, "Hard refresh should reload only once within the cooldown window.");
        Assert.Equal(1, telemetry.TotalHardRefreshAttempts, "Cooldown throttling should prevent a second hard refresh attempt from being counted.");
        Assert.Equal("first-refresh", telemetry.LastRecoveryReason, "Throttled hard refresh should not overwrite the last successful start reason.");
    }

    public static async Task ResetClearsRecoveryAttemptTotalsAsync()
    {
        var webView = new FakeShellSessionWebView();
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("still unavailable"));
        webView.EnqueueSnapshot(ControlUiProbeSnapshot.Unavailable("still unavailable"));
        var coordinator = CreateCoordinator(webView, new FakeShellSessionBridge());

        await coordinator.RequestReconnectAsync("before-reset");
        Assert.Equal(1, coordinator.GetTelemetrySnapshot().TotalReconnectAttempts, "Sanity check should record the reconnect attempt before reset.");

        coordinator.Reset();
        var telemetry = coordinator.GetTelemetrySnapshot();

        Assert.Equal(0, telemetry.TotalReconnectAttempts, "Reset should clear total reconnect attempts.");
        Assert.Equal(0, telemetry.TotalSoftResyncAttempts, "Reset should clear total soft resync attempts.");
        Assert.Equal(0, telemetry.TotalHardRefreshAttempts, "Reset should clear total hard refresh attempts.");
    }

    public static async Task DeferredSaveFlushesRequestsQueuedDuringWriteAsync()
    {
        var directory = CreateTempDirectory();
        try
        {
            var writeCount = 0;
            var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void WriteSettings(string path, string contents)
            {
                var count = Interlocked.Increment(ref writeCount);
                if (count == 1)
                {
                    firstWriteStarted.SetResult();
                    releaseFirstWrite.Task.GetAwaiter().GetResult();
                }

                File.WriteAllText(path, contents);
            }

            var configuration = new ConfigurationService(
                directory,
                new TestLogger(),
                TimeSpan.Zero,
                WriteSettings);

            configuration.Settings.AppLanguage = "en-US";
            configuration.SaveDeferred();

            await firstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            configuration.Settings.AppLanguage = "zh-CN";
            configuration.SaveDeferred();
            releaseFirstWrite.SetResult();

            await WaitUntilAsync(() => Volatile.Read(ref writeCount) >= 2, TimeSpan.FromSeconds(5));

            var json = File.ReadAllText(Path.Combine(directory, "settings.json"));
            Assert.Contains("\"appLanguage\": \"zh-CN\"", json, "Deferred save should flush changes queued while an earlier save is writing.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task SettingsLoadNormalizesNullOptionSections()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "settings.json"),
                """
                {
                  "environments": null,
                  "heartbeat": null,
                  "recoveryPolicy": null,
                  "diagnostics": null
                }
                """);

            var configuration = new ConfigurationService(directory, new TestLogger(), TimeSpan.Zero);

            configuration.Load();

            Assert.NotNull(configuration.Settings.Environments, "NormalizeSettings should repair a null environments array.");
            Assert.NotNull(configuration.Settings.Heartbeat, "NormalizeSettings should repair null heartbeat options.");
            Assert.NotNull(configuration.Settings.RecoveryPolicy, "NormalizeSettings should repair null recovery policy options.");
            Assert.NotNull(configuration.Settings.Diagnostics, "NormalizeSettings should repair null diagnostics options.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static async Task LatencyProbeRequestsControlUiConfigAsync()
    {
        Uri? requestedUri = null;
        using var service = new ControlUiLatencyService(
            new StubHttpMessageHandler((request, _) =>
            {
                requestedUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }),
            TimeSpan.FromMilliseconds(50));

        var snapshotSource = new TaskCompletionSource<ControlUiLatencySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.LatencyUpdated += snapshot => snapshotSource.TrySetResult(snapshot);

        service.Start("https://ai.falsemeet.site/control/");
        var snapshot = await snapshotSource.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("/control/__openclaw/control-ui-config.json", requestedUri?.AbsolutePath, "Latency probe should target the hosted Control UI config endpoint under the configured base path.");
        Assert.True(snapshot.IsSuccess, "401 from the config endpoint should still prove the Gateway is reachable.");
    }

    public static async Task LatencyProbeCancellationCompletesBackgroundTaskAsync()
    {
        using var service = new ControlUiLatencyService(
            new StubHttpMessageHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            TimeSpan.FromSeconds(30));

        service.Start("https://gateway.example/");
        service.Stop();

        var probeTask = GetCurrentProbeTask(service);
        Assert.NotNull(probeTask, "Latency service should keep the active probe task observable for shutdown.");
        await probeTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(probeTask.IsCompletedSuccessfully, "Cancelling the initial latency probe should complete the background task without faulting.");
    }

    public static Task LatencyHistoryTooltipSummarizesRecentSamples()
    {
        var history = new LatencyHistory(capacity: 3);

        history.Record(ControlUiLatencySnapshot.Success("gateway.example", 90));
        history.Record(ControlUiLatencySnapshot.Success("gateway.example", 120));
        history.Record(ControlUiLatencySnapshot.Unknown);
        history.Record(ControlUiLatencySnapshot.Success("gateway.example", 250));
        history.Record(ControlUiLatencySnapshot.Success("gateway.example", 180));

        var summary = history.CreateSummary();

        Assert.Equal(3, summary.SampleCount, "Latency history should keep only the configured number of recent successful samples.");
        Assert.Equal(180L, summary.LatestMs, "Latency history should report the latest successful sample.");
        Assert.Equal(120L, summary.MinMs, "Latency history should report the minimum retained sample.");
        Assert.Equal(183L, summary.AverageMs, "Latency history should round the average retained sample.");
        Assert.Equal(250L, summary.P95Ms, "Latency history should use nearest-rank p95 for the retained samples.");
        Assert.Equal(250L, summary.MaxMs, "Latency history should report the maximum retained sample.");
        Assert.Equal(
            "Latency history (3 samples)\nLatest: 180 ms\nMin: 120 ms\nAvg: 183 ms\nP95: 250 ms\nMax: 250 ms",
            LatencyTooltipFormatter.Format(summary),
            "Latency tooltip should expose the operational min/avg/p95/max values.");
        return Task.CompletedTask;
    }

    public static Task TrayClosePolicyHidesToTrayUntilExitRequested()
    {
        var policy = new TrayClosePolicy();

        Assert.Equal(TrayCloseDisposition.HideToTray, policy.GetCloseDisposition(closeToTray: true), "Normal close should hide the window to tray.");

        policy.RequestExit();

        Assert.Equal(TrayCloseDisposition.Exit, policy.GetCloseDisposition(closeToTray: true), "Explicit quit should allow the app to exit.");
        return Task.CompletedTask;
    }

    public static Task TrayClosePolicyRespectsCloseToTraySetting()
    {
        var policy = new TrayClosePolicy();

        Assert.Equal(TrayCloseDisposition.Exit, policy.GetCloseDisposition(closeToTray: false), "Disabling close-to-tray should let the close button exit.");
        return Task.CompletedTask;
    }

    public static Task SettingsLoadDefaultsTrayOptionsOn()
    {
        var settings = new AppSettings();

        Assert.True(settings.MinimizeToTray, "Minimize-to-tray should default on so the shell behaves like a tray app.");
        Assert.True(settings.CloseToTray, "Close-to-tray should default on so the window close button keeps the tray app alive.");
        return Task.CompletedTask;
    }

    public static Task SettingsLoadRejectsMinimizedWindowSentinelBounds()
    {
        var directory = CreateTempDirectory();
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllText(settingsPath, """
            {
              "windowWidth": 160,
              "windowHeight": 28,
              "windowLeft": -32000,
              "windowTop": -32000
            }
            """);

            var configuration = new ConfigurationService(directory, new TestLogger());
            configuration.Load();

            Assert.Equal(1280d, configuration.Settings.WindowWidth, "Minimized sentinel width should reset to the default window width.");
            Assert.Equal(800d, configuration.Settings.WindowHeight, "Minimized sentinel height should reset to the default window height.");
            Assert.Equal(-1d, configuration.Settings.WindowLeft, "Minimized sentinel left should reset to the unset window position.");
            Assert.Equal(-1d, configuration.Settings.WindowTop, "Minimized sentinel top should reset to the unset window position.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task MainWindowSkipsPersistingMinimizedOrHiddenBounds()
    {
        var lifecyclePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.Lifecycle.cs");
        var source = File.ReadAllText(lifecyclePath);
        var saveIndex = source.IndexOf("private void SaveWindowBounds()", StringComparison.Ordinal);
        var closeIndex = source.IndexOf("private void OnWindowClosed", StringComparison.Ordinal);

        Assert.True(saveIndex >= 0, "SaveWindowBounds should exist.");
        Assert.True(closeIndex > saveIndex, "SaveWindowBounds should appear before OnWindowClosed.");

        var saveMethod = source[saveIndex..closeIndex];
        Assert.Contains("_isWindowHidden", saveMethod, "Hidden-to-tray windows should not overwrite the last visible bounds.");
        Assert.Contains("WindowFrameHelper.IsWindowMinimized(this)", saveMethod, "Minimized windows should not overwrite the last visible bounds.");
        return Task.CompletedTask;
    }

    public static Task SettingsDefaultDisablesMultipleInstances()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Models",
            "AppSettings.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("AllowMultipleInstances", source, "Settings should persist whether multiple OpenClaw windows are allowed.");
        Assert.Contains("AllowMultipleInstances { get; set; } = false", source, "Multiple instances should be disabled by default.");
        return Task.CompletedTask;
    }

    public static Task SettingsGeneralExposesMultipleInstancesOption()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var viewModelPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "ViewModels",
            "SettingsViewModel.cs");
        var enResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "en-us",
            "Resources.resw");

        var xaml = File.ReadAllText(xamlPath);
        var viewModel = File.ReadAllText(viewModelPath);
        var enResources = File.ReadAllText(enResourcesPath);

        Assert.Contains("SettingsAllowMultipleInstances", xaml, "General settings should include a Multiple instances settings row.");
        Assert.Contains("AllowMultipleInstances", viewModel, "SettingsViewModel should expose the multiple instances setting.");
        Assert.Contains("Settings.AllowMultipleInstances = AllowMultipleInstances", viewModel, "Settings save should persist the multiple instances setting.");
        Assert.Contains("<value>Multiple instances</value>", enResources, "English label should be exactly Multiple instances.");
        Assert.Contains("<value>Allow multiple instance of Openclaw for windows</value>", enResources, "English description should match the requested wording.");
        return Task.CompletedTask;
    }

    public static Task SettingsBooleanOptionsUsePowerToysStyleRows()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var projectPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "OpenClaw.csproj");

        var xaml = File.ReadAllText(xamlPath);
        var project = File.ReadAllText(projectPath);

        Assert.Contains("CommunityToolkit.WinUI.Controls", project, "OpenClaw should reference the Toolkit settings controls used by SettingsCard.");
        Assert.Contains("xmlns:controls=\"using:CommunityToolkit.WinUI.Controls\"", xaml, "Settings XAML should import Toolkit controls.");
        Assert.Contains("<controls:SettingsCard", xaml, "Boolean settings should be grouped in SettingsCard rows.");
        Assert.Contains("<ToggleSwitch", xaml, "Boolean settings should use right-aligned ToggleSwitch controls.");
        Assert.DoesNotContain("<CheckBox", xaml, "Settings should not use checkbox controls after adopting the PowerToys settings row style.");

        Assert.Contains("IsOn=\"{x:Bind ViewModel.MinimizeToTray, Mode=TwoWay}\"", xaml, "Minimize-to-tray should bind through ToggleSwitch.IsOn.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.CloseToTray, Mode=TwoWay}\"", xaml, "Close-to-tray should bind through ToggleSwitch.IsOn.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.AllowMultipleInstances, Mode=TwoWay}\"", xaml, "Multiple instances should bind through ToggleSwitch.IsOn.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.EnableGlobalHotkey, Mode=TwoWay}\"", xaml, "Global hotkey enable should bind through ToggleSwitch.IsOn.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.AlwaysOnTop, Mode=TwoWay}\"", xaml, "Always-on-top should bind through ToggleSwitch.IsOn.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.EditIsDefault, Mode=TwoWay}\"", xaml, "Default environment should bind through ToggleSwitch.IsOn.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.EnableDevLog, Mode=TwoWay}\"", xaml, "Developer logging should bind through ToggleSwitch.IsOn.");
        return Task.CompletedTask;
    }

    public static Task SettingsEnvironmentEditKeepsApplyAction()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var actionsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Actions.cs");
        var resourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Helpers",
            "StringResources.cs");

        var xaml = File.ReadAllText(xamlPath);
        var actions = File.ReadAllText(actionsPath);
        var resources = File.ReadAllText(resourcesPath);
        var actionsBarIndex = xaml.IndexOf("x:Name=\"EnvironmentActionsBar\"", StringComparison.Ordinal);
        var defaultIndex = xaml.IndexOf("x:Name=\"DefaultEnvironmentRow\"", StringComparison.Ordinal);
        var applyIndex = xaml.IndexOf("Click=\"OnApplyClick\"", StringComparison.Ordinal);

        Assert.True(actionsBarIndex >= 0, "Environment default toggle and Apply action should share one cohesive action bar.");
        Assert.True(defaultIndex >= 0, "Environment default toggle should live in a named compact row.");
        Assert.True(actionsBarIndex < defaultIndex, "Default toggle should sit inside the environment action bar.");
        Assert.True(applyIndex > defaultIndex, "Environment Apply should sit to the right of the default toggle inside the action bar.");
        Assert.Contains("ColumnSpacing=\"16\"", xaml, "Environment action bar should use compact two-column spacing.");
        Assert.Contains("SettingsApply", xaml, "Environment Apply button should use the localized SettingsApply label.");
        Assert.Contains("private void OnApplyClick", actions, "Settings should expose an explicit Apply handler for environment edits.");
        Assert.Contains("TryApplyEdit()", actions, "Apply handler should commit the current environment draft before switching or saving.");
        Assert.Contains("ValidationInfoBar.IsOpen = false;", actions, "Apply handler should clear stale validation errors after a successful environment edit.");
        Assert.Contains("public static string SettingsApply", resources, "SettingsApply should be exposed through StringResources.");
        return Task.CompletedTask;
    }

    public static Task SettingsAlwaysOnTopStringsAreLocalized()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var resourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Helpers",
            "StringResources.cs");
        var enResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "en-us",
            "Resources.resw");
        var zhResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "zh-cn",
            "Resources.resw");

        var xaml = File.ReadAllText(xamlPath);
        var resources = File.ReadAllText(resourcesPath);
        var enResources = File.ReadAllText(enResourcesPath);
        var zhResources = File.ReadAllText(zhResourcesPath);

        Assert.Contains("helpers:StringResources.SettingsAlwaysOnTop", xaml, "Always-on-top header should use localized resources.");
        Assert.Contains("helpers:StringResources.SettingsAlwaysOnTopDescription", xaml, "Always-on-top description should use localized resources.");
        Assert.DoesNotContain("Header=\"Always on Top\"", xaml, "Always-on-top should not hard-code English in XAML.");
        Assert.Contains("public static string SettingsAlwaysOnTop", resources, "Always-on-top label should be exposed through StringResources.");
        Assert.Contains("SettingsAlwaysOnTop", enResources, "English resources should include the always-on-top label.");
        Assert.Contains("SettingsAlwaysOnTop", zhResources, "Chinese resources should include the always-on-top label.");
        return Task.CompletedTask;
    }

    public static Task SettingsSwitchRowsUseCompactSpacing()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"ShellSwitchRows\"", xaml, "Shell switch cards should be grouped so their spacing is intentional.");
        Assert.Contains("x:Name=\"ShellSwitchRows\" Spacing=\"8\"", xaml, "Shell switch cards should use compact 8px spacing.");
        Assert.Contains("x:Name=\"DevToolsSwitchRows\" Spacing=\"8\"", xaml, "Developer switch cards should use compact 8px spacing.");
        Assert.DoesNotContain("<controls:SettingsCard Header=\"{x:Bind helpers:StringResources.SetAsDefault}\"", xaml, "Set as default should not use the full-height SettingsCard treatment.");
        return Task.CompletedTask;
    }

    public static Task AppStartupHonorsMultipleInstanceSetting()
    {
        var appPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "App.xaml.cs");
        var coordinatorPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "SingleInstanceCoordinator.cs");
        var appSource = File.ReadAllText(appPath);
        var coordinatorSource = File.ReadAllText(coordinatorPath);

        Assert.True(File.Exists(coordinatorPath), "Single-instance startup should live in a dedicated coordinator service.");
        Assert.Contains("public sealed class SingleInstanceCoordinator", coordinatorSource, "Single-instance coordinator should be public so the WinUI app can use the Core service.");
        Assert.Contains("AllowMultipleInstances", appSource, "App startup should read the multiple instances setting.");
        Assert.Contains("SingleInstanceCoordinator", appSource, "App startup should coordinate secondary launches when multiple instances are disabled.");
        Assert.Contains("RequestActivationOfPrimaryInstance", appSource, "Secondary launches should request activation of the primary instance.");
        return Task.CompletedTask;
    }

    public static Task SettingsNavigationPlacesGeneralAfterLanguage()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var enResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "en-us",
            "Resources.resw");
        var zhResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "zh-cn",
            "Resources.resw");

        var xaml = File.ReadAllText(xamlPath);
        var enResources = File.ReadAllText(enResourcesPath);
        var zhResources = File.ReadAllText(zhResourcesPath);
        var languageIndex = xaml.IndexOf("x:Name=\"NavLanguage\"", StringComparison.Ordinal);
        var shellIndex = xaml.IndexOf("x:Name=\"NavShell\"", StringComparison.Ordinal);
        var environmentsIndex = xaml.IndexOf("x:Name=\"NavEnvironments\"", StringComparison.Ordinal);
        var sessionsIndex = xaml.IndexOf("x:Name=\"NavSessions\"", StringComparison.Ordinal);
        var devToolsIndex = xaml.IndexOf("x:Name=\"NavDevTools\"", StringComparison.Ordinal);

        Assert.True(languageIndex >= 0, "Settings navigation should include Language.");
        Assert.True(shellIndex >= 0, "Settings navigation should include the General/Shell behavior entry.");
        Assert.True(environmentsIndex >= 0, "Settings navigation should include Environments.");
        Assert.True(sessionsIndex >= 0, "Settings navigation should include Sessions.");
        Assert.True(devToolsIndex >= 0, "Settings navigation should include Dev Tools.");
        Assert.True(languageIndex < shellIndex, "General should appear immediately after Language.");
        Assert.True(shellIndex < environmentsIndex, "Environment management should appear after General.");
        Assert.True(environmentsIndex < sessionsIndex, "Sessions should appear after Environments.");
        Assert.True(sessionsIndex < devToolsIndex, "Dev Tools should stay last.");
        Assert.Contains("<value>General</value>", enResources, "English Shell navigation label should read General.");
        Assert.Contains("SettingsNavShell", zhResources, "Chinese resources should define the General/Shell label.");
        return Task.CompletedTask;
    }

    public static Task VersionMetadataIs333()
    {
        var projectPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "OpenClaw.csproj");
        var packageManifestPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Package.appxmanifest");
        var appManifestPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "app.manifest");
        var aboutPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "AboutDialog.xaml.cs");

        var project = File.ReadAllText(projectPath);
        var packageManifest = File.ReadAllText(packageManifestPath);
        var appManifest = File.ReadAllText(appManifestPath);
        var about = File.ReadAllText(aboutPath);

        Assert.Contains("<Version>3.3.3</Version>", project, "Project package version should be 3.3.3.");
        Assert.Contains("<AssemblyVersion>3.3.3.0</AssemblyVersion>", project, "Assembly version should be 3.3.3.0.");
        Assert.Contains("<FileVersion>3.3.3.0</FileVersion>", project, "File version should be 3.3.3.0.");
        Assert.Contains("Version=\"3.3.3.0\"", packageManifest, "Package manifest version should be 3.3.3.0.");
        Assert.Contains("version=\"3.3.3.0\"", appManifest, "Application manifest assembly identity should be 3.3.3.0.");
        Assert.Contains("AppMetadata.GetDisplayVersion()", about, "About dialog should display the assembly-backed app version.");
        return Task.CompletedTask;
    }

    public static Task RepositoryCodeStyleIsExplicit()
    {
        var editorConfigPath = Path.Combine(Directory.GetCurrentDirectory(), ".editorconfig");
        Assert.True(File.Exists(editorConfigPath), "Repository should define a root .editorconfig.");

        var editorConfig = File.ReadAllText(editorConfigPath);
        Assert.Contains("root = true", editorConfig, ".editorconfig should be the root style source.");
        Assert.Contains("end_of_line = lf", editorConfig, "Source files should default to Linux-style LF endings.");
        Assert.Contains("insert_final_newline = true", editorConfig, "Files should end with one final newline.");
        Assert.Contains("trim_trailing_whitespace = true", editorConfig, "Trailing whitespace should be rejected by formatter verification.");
        Assert.Contains("indent_size = 4", editorConfig, "C# and XAML indentation should be four spaces.");
        Assert.Contains("csharp_prefer_braces = true", editorConfig, "Control flow should use braces consistently.");
        Assert.Contains("dotnet_diagnostic.IDE0055.severity", editorConfig, "Formatting diagnostics should be explicit.");
        return Task.CompletedTask;
    }

    public static Task DirectoryBuildEnablesAnalyzersAndStyle()
    {
        var propsPath = Path.Combine(Directory.GetCurrentDirectory(), "Directory.Build.props");
        var props = File.ReadAllText(propsPath);

        Assert.Contains("<EnableNETAnalyzers>true</EnableNETAnalyzers>", props, "Shared build props should enable .NET analyzers.");
        Assert.Contains("<AnalysisLevel>latest</AnalysisLevel>", props, "Analyzer level should follow the SDK used by the repo.");
        Assert.Contains("<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>", props, "Builds should honor .editorconfig code-style severities.");
        Assert.Contains("<Nullable>enable</Nullable>", props, "Nullable analysis should be project-wide by default.");
        Assert.Contains("<ImplicitUsings>enable</ImplicitUsings>", props, "Implicit usings should be project-wide by default.");
        return Task.CompletedTask;
    }

    public static Task ExecutableTestHarnessRejectsDotnetTestFalsePositives()
    {
        var projectPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "tests",
            "OpenClaw.Tests",
            "OpenClaw.Tests.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains("FailDotNetTestForExecutableHarness", project, "Executable test harness should not let dotnet test report a false green run.");
        Assert.Contains("dotnet run --project tests\\OpenClaw.Tests\\OpenClaw.Tests.csproj", project, "The dotnet test error should point to the real test command.");
        Assert.Contains("BeforeTargets=\"VSTest\"", project, "The guard should run when dotnet test invokes the VSTest target.");
        return Task.CompletedTask;
    }

    public static Task DocumentationIncludesWinUiFormatPlatform()
    {
        var readme = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "README.md"));
        var readmeZh = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "readme_zh.md"));
        var notes = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "DEVELOPMENT_NOTES.md"));

        Assert.Contains("$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore", readme, "README should document the WinUI platform needed for dotnet format.");
        Assert.Contains("$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore", readmeZh, "Chinese README should document the WinUI platform needed for dotnet format.");
        Assert.Contains("$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore", notes, "Development notes should document the WinUI platform needed for dotnet format.");
        return Task.CompletedTask;
    }

    public static Task AboutDialogRepositoryLinkTargetsOpenClawManagerRepo()
    {
        var aboutPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "AboutDialog.xaml");

        var about = File.ReadAllText(aboutPath);

        Assert.Contains("NavigateUri=\"https://github.com/guijianchou/OpenClaw_Manager\"", about, "About dialog repository link should target the OpenClaw Manager repository.");
        Assert.DoesNotContain("NavigateUri=\"https://github.com/Jutaosay/openclaw_for_windows\"", about, "About dialog should not link to the old repository.");
        return Task.CompletedTask;
    }

    public static Task SettingsWindowUsesNonBlockingFrameRefresh()
    {
        var themePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Theme.cs");
        var source = File.ReadAllText(themePath);

        Assert.DoesNotContain("redrawWindow: true", source, "Settings window should not force synchronous native redraws while opening.");
        Assert.DoesNotContain("repeatRefreshOnDarkTransition: true", source, "Settings window should not repeat dark-transition frame refreshes while opening.");
        Assert.DoesNotContain("rootElement.UpdateLayout()", source, "Settings window should not force a full layout pass during title-bar refresh.");
        return Task.CompletedTask;
    }

    public static Task SettingsWindowAvoidsFirstFrameBlackFlash()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var initializationPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Initialization.cs");

        var xaml = File.ReadAllText(xamlPath);
        var initialization = File.ReadAllText(initializationPath);

        Assert.Contains("x:Name=\"SettingsRoot\"", xaml, "Settings root should be named so the first painted surface is explicit.");
        Assert.Contains("Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\"", xaml, "Settings root should paint an opaque theme background before child content is ready.");
        Assert.DoesNotContain("MicaBackdrop", initialization, "Settings window should not rely on Mica during its first visible frame.");
        return Task.CompletedTask;
    }

    public static Task TitleBarCaptionButtonStatesUseOpaqueThemeColors()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Helpers",
            "WindowFrameHelper.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("FromArgb(96, 255, 255, 255)", source, "Caption button hover colors should be opaque because AppWindow.TitleBar state colors do not alpha-blend reliably.");
        Assert.DoesNotContain("FromArgb(144, 255, 255, 255)", source, "Caption button pressed colors should be opaque because AppWindow.TitleBar state colors do not alpha-blend reliably.");
        Assert.DoesNotContain("FromArgb(20, 0, 0, 0)", source, "Caption button hover colors should be opaque because AppWindow.TitleBar state colors do not alpha-blend reliably.");
        Assert.DoesNotContain("FromArgb(36, 0, 0, 0)", source, "Caption button pressed colors should be opaque because AppWindow.TitleBar state colors do not alpha-blend reliably.");
        Assert.Contains("Windows.UI.Color.FromArgb(255, 55, 55, 55)", source, "Dark caption button hover should use a subtle opaque color.");
        Assert.Contains("Windows.UI.Color.FromArgb(255, 68, 68, 68)", source, "Dark caption button pressed should use a subtle opaque color.");
        Assert.Contains("Windows.UI.Color.FromArgb(255, 229, 229, 229)", source, "Light caption button hover should use a subtle opaque color.");
        Assert.Contains("Windows.UI.Color.FromArgb(255, 217, 217, 217)", source, "Light caption button pressed should use a subtle opaque color.");
        return Task.CompletedTask;
    }

    public static Task TopStatusPillLeavesRoomForLongModelNames()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("MaxWidth=\"780\"", xaml, "Top status pill should be wide enough for provider/model labels.");
        Assert.Contains("MinWidth=\"440\"", xaml, "Top status pill should not collapse the model column before auth/status indicators.");
        Assert.Contains("x:Name=\"ModelStatusSegment\"", xaml, "Model status segment should be named so layout regressions are easy to test.");
        Assert.Contains("MinWidth=\"190\"", xaml, "Model status segment should reserve room for current OpenClaw provider/model names.");
        Assert.Contains("x:Name=\"AccessStatusSegment\"", xaml, "Auth/access segment should be explicit in the status pill layout.");
        Assert.Contains("Margin=\"18,0,0,0\"", xaml, "Auth/access segment should leave a visible gap after long model labels.");
        return Task.CompletedTask;
    }

    public static Task TopStatusModelTextMatchesStatusBarFontSize()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);
        var modelTextIndex = xaml.IndexOf("Text=\"{x:Bind ViewModel.ModelSummaryText, Mode=OneWay}\"", StringComparison.Ordinal);
        var statusTextIndex = xaml.IndexOf("x:Name=\"StatusText\"", StringComparison.Ordinal);

        Assert.True(modelTextIndex >= 0, "Model summary TextBlock should be present in the top status pill.");
        Assert.True(statusTextIndex >= 0, "Status bar text should be present for font-size comparison.");

        var modelTextBlock = xaml.Substring(modelTextIndex, Math.Min(260, xaml.Length - modelTextIndex));
        var statusTextBlock = xaml.Substring(statusTextIndex, Math.Min(260, xaml.Length - statusTextIndex));

        Assert.Contains("FontSize=\"12\"", statusTextBlock, "Status bar text should use 12px as the comparison size.");
        Assert.Contains("FontSize=\"12\"", modelTextBlock, "Top MODEL value should use the same 12px font size as the status bar text.");
        return Task.CompletedTask;
    }

    public static Task SettingsWindowIsPrewarmedAfterStartup()
    {
        var commandsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.Commands.cs");
        var lifecyclePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.Lifecycle.cs");

        var commands = File.ReadAllText(commandsPath);
        var lifecycle = File.ReadAllText(lifecyclePath);

        Assert.Contains("PrewarmSettingsWindow", commands, "MainWindow should pre-create Settings after startup so the first click does not pay XAML construction cost.");
        Assert.Contains("DispatcherQueuePriority.Low", lifecycle, "Settings prewarm should run at low priority after the initial window load.");
        Assert.Contains("PrewarmSettingsWindow()", lifecycle, "MainWindow should schedule Settings prewarm from root load.");
        return Task.CompletedTask;
    }

    public static Task SettingsLanguageSelectionSyncsAfterLoad()
    {
        var initializationPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Initialization.cs");
        var themePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Theme.cs");

        var initialization = File.ReadAllText(initializationPath);
        var theme = File.ReadAllText(themePath);
        var initializeNavigationIndex = initialization.IndexOf("private void InitializeNavigationState()", StringComparison.Ordinal);
        var rootLoadedIndex = theme.IndexOf("private void OnRootLoaded", StringComparison.Ordinal);
        var rootLoadedSelectionIndex = theme.IndexOf("SetLanguageSelection(ViewModel.SelectedLanguage)", StringComparison.Ordinal);
        var syncIndex = theme.IndexOf("public void SyncWithCurrentSettings()", StringComparison.Ordinal);
        var syncSelectionIndex = theme.IndexOf("SetLanguageSelection(ViewModel.SelectedLanguage)", syncIndex, StringComparison.Ordinal);

        Assert.True(initializeNavigationIndex >= 0, "Settings navigation initialization should exist.");
        Assert.DoesNotContain("SetLanguageSelection(ViewModel.SelectedLanguage);", initialization, "Language selection should not be finalized before the Settings window is loaded.");
        Assert.True(rootLoadedIndex >= 0, "Settings root Loaded handler should exist.");
        Assert.True(rootLoadedSelectionIndex > rootLoadedIndex, "Settings root Loaded should re-apply language selection after ComboBox items are loaded.");
        Assert.True(syncIndex >= 0, "Settings should expose SyncWithCurrentSettings.");
        Assert.True(syncSelectionIndex > syncIndex, "Settings activation sync should re-apply language selection before showing the window.");
        return Task.CompletedTask;
    }

    public static Task SettingsPrewarmIsRequeuedAfterClose()
    {
        var commandsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.Commands.cs");
        var commands = File.ReadAllText(commandsPath);
        var closedIndex = commands.IndexOf("private void OnSettingsWindowClosed", StringComparison.Ordinal);
        var requeueIndex = commands.IndexOf("QueueSettingsWindowPrewarm()", closedIndex, StringComparison.Ordinal);

        Assert.Contains("private void QueueSettingsWindowPrewarm()", commands, "Settings prewarm should be centralized in one low-priority queue helper.");
        Assert.True(closedIndex >= 0, "Settings closed handler should exist.");
        Assert.True(requeueIndex > closedIndex, "Closing Settings should queue the next prewarmed Settings instance.");
        return Task.CompletedTask;
    }

    public static Task SettingsLanguageOptionsAreCodePopulated()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var initializationPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Initialization.cs");
        var navigationPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Navigation.cs");
        var sharedPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Shared.cs");

        var xaml = File.ReadAllText(xamlPath);
        var initialization = File.ReadAllText(initializationPath);
        var navigation = File.ReadAllText(navigationPath);
        var shared = File.ReadAllText(sharedPath);

        Assert.DoesNotContain("<ComboBoxItem", xaml, "Language ComboBox items should not rely on x:Bind content in prewarmed windows.");
        Assert.Contains("PopulateLanguageOptions();", initialization, "Settings should populate language options in code before navigation selection.");
        Assert.Contains("LanguageComboBox.Items.Clear()", navigation, "Language option population should rebuild the ComboBox items explicitly.");
        Assert.Contains("new ComboBoxItem", navigation, "Language options should use concrete ComboBoxItem content and tags.");
        Assert.Contains("_isSyncingLanguageSelection", shared, "Language selection sync should guard against prewarm/activation events mutating the ViewModel.");
        Assert.Contains("if (_isSyncingLanguageSelection)", navigation, "Language selection changed handler should ignore programmatic sync changes.");
        return Task.CompletedTask;
    }

    public static Task TrayWin32UnicodeEntryPointsDeclareUnicodeMarshalling()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "TrayIconService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("RegisterClassExW\", CharSet = CharSet.Unicode", source, "RegisterClassExW should use Unicode string marshalling.");
        Assert.Contains("CreateWindowExW\", CharSet = CharSet.Unicode", source, "CreateWindowExW should use Unicode string marshalling so the registered class name can be found.");
        Assert.Contains("UnregisterClassW\", CharSet = CharSet.Unicode", source, "UnregisterClassW should use Unicode string marshalling.");
        Assert.Contains("LoadImageW\", CharSet = CharSet.Unicode", source, "LoadImageW should use Unicode string marshalling for the ico path.");
        Assert.Contains("GetModuleHandleW\", CharSet = CharSet.Unicode", source, "GetModuleHandleW should use Unicode string marshalling.");
        Assert.Contains("AppendMenuW\", CharSet = CharSet.Unicode", source, "AppendMenuW should use Unicode string marshalling for menu text.");
        return Task.CompletedTask;
    }

    public static Task TrayCallbackReadsNotifyIconVersion4EventLowWord()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "TrayIconService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("LowWord(lParam)", source, "NOTIFYICON_VERSION_4 sends the tray event in LOWORD(lParam).");
        Assert.Contains("& 0xFFFF", source, "Tray callback parsing should mask off the high-word icon id.");
        return Task.CompletedTask;
    }

    public static Task TrayContextMenuUsesLocalizedCommandLabels()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "TrayIconService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("_menuStrings.OpenLabel", source, "Tray menu should use the localized open label.");
        Assert.Contains("_menuStrings.ReloadLabel", source, "Tray menu should use the localized reload label.");
        Assert.Contains("_menuStrings.ViewLogsLabel", source, "Tray menu should use the localized view logs label.");
        Assert.Contains("_menuStrings.SettingsLabel", source, "Tray menu should use the localized settings label.");
        Assert.Contains("_menuStrings.ExitLabel", source, "Tray menu should use the localized exit label.");
        Assert.Contains("MenuStatusHeader", source, "Tray menu should include a status header.");
        Assert.DoesNotContain("\"Hide OpenClaw\"", source, "Minimal tray menu should not expose a hide command.");
        Assert.DoesNotContain("\"Show OpenClaw\"", source, "Minimal tray menu should not expose a show command.");
        Assert.DoesNotContain("\"Open Settings\"", source, "Minimal tray menu should use the shorter settings label.");
        Assert.DoesNotContain("\"Quit\"", source, "Minimal tray menu should use exit terminology.");
        return Task.CompletedTask;
    }

    public static Task TrayContextMenuUsesPopupCapableOwnerWindow()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "TrayIconService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("WindowHandles.MessageOnly", source, "Tray menu owner should not be a message-only window because popup menus need a normal owner.");
        Assert.DoesNotContain("new(-3)", source, "Tray icon service should not use HWND_MESSAGE for the popup menu owner.");
        return Task.CompletedTask;
    }

    public static Task WindowHideRestoresMinimizedPlacementFirst()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Helpers",
            "WindowFrameHelper.cs");
        var source = File.ReadAllText(sourcePath);
        var hideIndex = source.IndexOf("public static void HideWindow(Window window)", StringComparison.Ordinal);
        var showIndex = source.IndexOf("public static void ShowAndActivateWindow(Window window)", StringComparison.Ordinal);

        Assert.True(hideIndex >= 0, "HideWindow should exist.");
        Assert.True(showIndex > hideIndex, "ShowAndActivateWindow should follow HideWindow.");

        var hideMethod = source[hideIndex..showIndex];
        var minimizedCheckIndex = hideMethod.IndexOf("IsIconic(hwnd)", StringComparison.Ordinal);
        var restoreIndex = hideMethod.IndexOf("ShowWindow(hwnd, ShowWindowRestore)", StringComparison.Ordinal);
        var hideCallIndex = hideMethod.IndexOf("ShowWindow(hwnd, ShowWindowHide)", StringComparison.Ordinal);

        Assert.True(minimizedCheckIndex >= 0, "HideWindow should check whether the HWND is minimized.");
        Assert.True(restoreIndex >= 0, "HideWindow should restore minimized placement before hiding.");
        Assert.True(hideCallIndex > restoreIndex, "HideWindow should hide only after minimized placement is restored.");
        return Task.CompletedTask;
    }

    public static Task AtomicWriterReplacesExistingContent()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "old");

            AtomicFileWriter.WriteAllText(path, "new");

            Assert.Equal("new", File.ReadAllText(path), "Atomic write should replace the target contents.");
            Assert.Equal(0, Directory.EnumerateFiles(directory, "*.tmp").Count(), "Atomic write should clean temporary files after success.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task LogTailReaderReturnsFinalLines()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "openclaw-2026-05-01.log");
            File.WriteAllLines(path, Enumerable.Range(1, 8).Select(i => $"line-{i}"));

            var tail = LogFileUtilities.ReadLastLines(path, 3);

            Assert.Equal(8, tail.TotalLineCount, "Tail reader should report the full line count.");
            Assert.True(tail.WasTruncated, "Tail reader should indicate when earlier lines were omitted.");
            Assert.Equal("line-6|line-7|line-8", string.Join('|', tail.Lines), "Tail reader should keep only the final requested lines.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task LogRetentionRemovesOnlyExpiredOpenClawLogs()
    {
        var directory = CreateTempDirectory();
        try
        {
            var now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
            var expired = Path.Combine(directory, "openclaw-2026-04-01.log");
            var recent = Path.Combine(directory, "openclaw-2026-04-30.log");
            var unrelated = Path.Combine(directory, "notes.log");
            File.WriteAllText(expired, "old");
            File.WriteAllText(recent, "new");
            File.WriteAllText(unrelated, "keep");
            File.SetLastWriteTimeUtc(expired, now.AddDays(-30).UtcDateTime);
            File.SetLastWriteTimeUtc(recent, now.AddDays(-1).UtcDateTime);
            File.SetLastWriteTimeUtc(unrelated, now.AddDays(-30).UtcDateTime);

            var deleted = LogFileUtilities.DeleteExpiredLogs(directory, TimeSpan.FromDays(14), now);

            Assert.Equal(1, deleted, "Retention should delete only expired OpenClaw log files.");
            Assert.False(File.Exists(expired), "Expired OpenClaw log should be removed.");
            Assert.True(File.Exists(recent), "Recent OpenClaw log should be preserved.");
            Assert.True(File.Exists(unrelated), "Unrelated files should be preserved.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task TrayMenuStringsAreInjectedAndAccessible()
    {
        var strings = new TrayMenuStrings(
            OpenLabel: "打开 OpenClaw",
            ReloadLabel: "重新加载",
            ViewLogsLabel: "查看日志",
            CompactModeLabel: "紧凑模式",
            SettingsLabel: "设置",
            ExitLabel: "退出");

        Assert.Equal("打开 OpenClaw", strings.OpenLabel, "OpenLabel should be the injected Chinese string.");
        Assert.Equal("重新加载", strings.ReloadLabel, "ReloadLabel should be the injected Chinese string.");
        Assert.Equal("查看日志", strings.ViewLogsLabel, "ViewLogsLabel should be the injected Chinese string.");
        Assert.Equal("紧凑模式", strings.CompactModeLabel, "CompactModeLabel should be the injected Chinese string.");
        Assert.Equal("设置", strings.SettingsLabel, "SettingsLabel should be the injected Chinese string.");
        Assert.Equal("退出", strings.ExitLabel, "ExitLabel should be the injected Chinese string.");
        return Task.CompletedTask;
    }

    public static Task TrayMenuStringsDefaultFallbackUsesEnglish()
    {
        var strings = TrayMenuStrings.Default;

        Assert.Equal("Open OpenClaw", strings.OpenLabel, "Default OpenLabel should be English.");
        Assert.Equal("Reload", strings.ReloadLabel, "Default ReloadLabel should be English.");
        Assert.Equal("View Logs", strings.ViewLogsLabel, "Default ViewLogsLabel should be English.");
        Assert.Equal("Compact Mode", strings.CompactModeLabel, "Default CompactModeLabel should be English.");
        Assert.Equal("Settings", strings.SettingsLabel, "Default SettingsLabel should be English.");
        Assert.Equal("Exit", strings.ExitLabel, "Default ExitLabel should be English.");
        return Task.CompletedTask;
    }

    public static Task TrayMenuExposesReloadAndViewLogsCommands()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "TrayIconService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("public event Action? ReloadRequested", source, "TrayIconService should expose a ReloadRequested event.");
        Assert.Contains("public event Action? ViewLogsRequested", source, "TrayIconService should expose a ViewLogsRequested event.");
        Assert.Contains("case MenuReload:", source, "TrayIconService should dispatch the reload menu command.");
        Assert.Contains("ReloadRequested?.Invoke()", source, "TrayIconService should raise ReloadRequested when reload is selected.");
        Assert.Contains("case MenuViewLogs:", source, "TrayIconService should dispatch the view logs menu command.");
        Assert.Contains("ViewLogsRequested?.Invoke()", source, "TrayIconService should raise ViewLogsRequested when view logs is selected.");
        return Task.CompletedTask;
    }

    public static Task TrayMenuStatusHeaderReflectsWorkStatus()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "TrayIconService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("TrayMenuStrings menuStrings", source, "TrayIconService should accept TrayMenuStrings in its constructor.");
        Assert.Contains("private string _statusText", source, "TrayIconService should track the current status text.");
        Assert.Contains("public void UpdateStatus(string statusText)", source, "TrayIconService should expose a status update method.");
        Assert.Contains("$\"Status: {_statusText}\"", source, "TrayIconService should render the current status in the context menu header.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeReadsCurrentModelFromOpenClawModelSelect()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("data-chat-model-select", source, "Bridge should read OpenClaw Web UI's explicit model select before generic heuristics.");
        Assert.Contains("selectedModelOptionValue", source, "Bridge should consider the selected option value when the visible label is localized or default-only.");
        Assert.Contains("selectedModelTitle", source, "Bridge should consider the select title that OpenClaw uses for the displayed model label.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeReadsCurrentModelFromOpenClawAppState()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("readOpenClawAppStateModel", source, "Bridge should read OpenClaw's app state before falling back to visible DOM controls.");
        Assert.Contains("openclaw-app", source, "Bridge should locate the OpenClaw Lit app host.");
        Assert.Contains("chatModelOverrides", source, "Bridge should consider the local model override cache used by OpenClaw Web UI.");
        Assert.Contains("sessionsResult?.defaults", source, "Bridge should resolve the inherited default model when a session has no override.");
        Assert.Contains("modelOverride", source, "Bridge should read OpenClaw session modelOverride fields when no plain model field is present.");
        Assert.Contains("providerOverride", source, "Bridge should read OpenClaw session providerOverride fields when no plain modelProvider field is present.");
        Assert.Contains("searchParams.get('session')", source, "Bridge should fall back to the hosted chat session query string when app.sessionKey is absent.");
        Assert.Contains("overrides instanceof Map", source, "Bridge should support Map-backed chatModelOverrides from Lit app state.");
        Assert.Contains("value == null ? '' : String(value)", source, "Bridge text normalization should not throw when app-state message parts contain object payloads.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeUsesStructuredModelSourcePipeline()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("MODEL_FIELD_KEYS", source, "Model field aliases should be centralized instead of repeated in ad hoc OR chains.");
        Assert.Contains("PROVIDER_FIELD_KEYS", source, "Provider field aliases should be centralized next to model aliases.");
        Assert.Contains("SESSION_KEY_PATHS", source, "Session-key discovery should be data-driven across root and nested app-state variants.");
        Assert.Contains("APP_STATE_PATHS", source, "OpenClaw app-state lookup should cover root and nested state containers without another patch per field move.");
        Assert.Contains("MODEL_SOURCE_READERS", source, "Current model detection should run through an explicit ordered source pipeline.");
        Assert.Contains("readFirstPath", source, "Nested app-state values should be resolved through one path reader instead of repeated optional chains.");
        Assert.Contains("return { value:", source, "Model readers should return both the detected value and its source.");
        Assert.DoesNotContain("entry.model || entry.modelOverride || entry.selectedModel || entry.chatModel || entry.modelId", source, "Model field fallback should no longer be a hand-written OR chain.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeDefersAppStateDefaults()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("let firstDefaultModel = ''", source, "App-state defaults should be retained as a fallback instead of returned before later state candidates are checked.");
        Assert.Contains("if (!firstDefaultModel && defaultsModel)", source, "The first default should be remembered once while overrides and active sessions remain higher priority.");
        Assert.Contains("let nullOverrideDefaultModel = ''", source, "Null override defaults should also be deferred so root app-state cannot mask nested session data.");
        Assert.Contains("return firstDefaultModel", source, "Default model return should happen after every state candidate has been checked.");
        Assert.DoesNotContain("if (defaultsModel) return modelResult(defaultsModel, 'app-state:default');", source, "A root default must not mask a nested state override or active session.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeKeepsNullOverrideDefaultSemantics()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("if (override === null)", source, "A null override means the current app-state candidate should inherit its own default model.");
        Assert.Contains("nullOverrideDefaultModel = defaultsModel;", source, "Null override default should remember the current candidate's default without returning early.");
        Assert.Contains("modelResult(nullOverrideDefaultModel, 'app-state:default')", source, "Null override default should be returned only after later state candidates are checked.");
        Assert.DoesNotContain("if (override === null && defaultsModel) return modelResult(defaultsModel, 'app-state:default');", source, "A null override default must not return before nested session state is inspected.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeAvoidsObjectShapedModelStrings()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("readScalarText", source, "Model field readers should only accept scalar text values.");
        Assert.Contains("readModelLikeValue", source, "Object-shaped model fields should be resolved through explicit id/name/provider fields.");
        Assert.DoesNotContain("const text = compactText(target[key]);", source, "Generic field lookup should not stringify arbitrary objects into '[object Object]'.");
        return Task.CompletedTask;
    }

    public static Task HostedUiSnapshotsCarryModelSourceInstrumentation()
    {
        var modelsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "SessionProbeModels.cs");
        var webViewPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "WebViewService.cs");
        var bridgePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var stateEffectsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "ShellSessionCoordinator.StateEffects.cs");

        var modelsSource = File.ReadAllText(modelsPath);
        var webViewSource = File.ReadAllText(webViewPath);
        var bridgeSource = File.ReadAllText(bridgePath);
        var stateEffectsSource = File.ReadAllText(stateEffectsPath);

        Assert.Contains("string ModelSource", modelsSource, "Snapshots should carry where the MODEL value came from for future diagnostics.");
        Assert.Contains("currentModelSource", bridgeSource, "The injected bridge should emit model-source instrumentation.");
        Assert.Contains("GetString(root, \"currentModelSource\")", webViewSource, "Native parsing should retain the bridge-reported model source.");
        Assert.Contains("modelSource = string.IsNullOrWhiteSpace(snapshot.ModelSource)", stateEffectsSource, "Hosted UI state logs should expose the model source or null.");
        return Task.CompletedTask;
    }

    public static Task HostedUiSessionReadyCarriesModelSource()
    {
        var modelsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "SessionProbeModels.cs");
        var bridgePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var eventHandlersPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "ShellSessionCoordinator.EventHandlers.cs");

        var modelsSource = File.ReadAllText(modelsPath);
        var bridgeSource = File.ReadAllText(bridgePath);
        var eventHandlersSource = File.ReadAllText(eventHandlersPath);

        Assert.Contains("string ModelSource", modelsSource, "Session ready event args should carry the model source.");
        Assert.Contains("modelSource: snapshot.currentModelSource", bridgeSource, "Session ready bridge messages should preserve the detected model source.");
        Assert.Contains("GetString(root, \"modelSource\")", bridgeSource, "Native session-ready parsing should read the bridge-reported model source.");
        Assert.Contains("args.ModelSource", eventHandlersSource, "Session-ready logging should include the model source for diagnostics.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeIgnoresSidebarOnlyMutationsDuringStatusPolling()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("isStatusProbeExcludedElement", source, "Bridge should exclude status-irrelevant content before scanning the DOM.");
        Assert.Contains(".chat-sidebar, .sidebar-panel, .sidebar-content, .chat-tool-card__preview-frame", source, "Bridge should recognize OpenClaw Web UI's right sidebar and hosted canvas frame containers.");
        Assert.Contains("if (mutations.length > 0 && mutations.every(isSidebarOnlyMutation))", source, "Sidebar-only mutation storms should be classified before scheduling status work.");
        Assert.Contains("return;\n    }\n\n    schedule();", source, "Sidebar-only mutations should not schedule any status inspection.");
        Assert.DoesNotContain("scheduleSlow", source, "Sidebar-only changes should be ignored, not converted into periodic expensive DOM scans.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeIgnoresSettingsAndCronMutationStorms()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("readOpenClawAppStateStatus", source, "Bridge should use OpenClaw Lit app state as the connected-page status source before scanning DOM.");
        Assert.Contains("needsDomSignals", source, "Connected OpenClaw pages should not scan rendered settings/cron text for every status probe.");
        Assert.Contains("settings-workspace__body", source, "Settings category bodies such as Communications and Automation should be excluded from status mutation probes.");
        Assert.Contains("config-content", source, "Config form renders should be excluded from status mutation probes.");
        Assert.Contains("cron-workspace", source, "Cron/automation job tables and run logs should be excluded from status mutation probes.");
        Assert.Contains("cron-summary-strip", source, "Cron summary rerenders should not drive high-frequency native status probes.");
        Assert.DoesNotContain("'class']", source, "Bridge should not subscribe to high-volume class attribute mutations from Lit rerenders.");
        Assert.Contains("document.addEventListener('change'", source, "Form and model select changes should use explicit low-cost events instead of observing all class changes.");
        return Task.CompletedTask;
    }

    public static Task HostedUiBridgeReportsStaleBusyAndInputTextState()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "HostedUiBridge.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("focusedInputHasText", source, "Bridge should distinguish focused empty editors from unsent user text.");
        Assert.Contains("activitySignature", source, "Bridge should emit a compact activity signature for stale stream detection.");
        Assert.Contains("isBusyStale", source, "Bridge should flag busy chat sessions that stop making visible or state progress.");
        Assert.Contains("snapshot.phase === 'connected' && snapshot.isBusy ? 4000 : 15000", source, "Busy connected pages should be polled faster than idle connected pages.");
        return Task.CompletedTask;
    }

    public static Task MainViewModelPreservesKnownModelOnEmptySnapshots()
    {
        var statusPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "ViewModels",
            "MainViewModel.Status.cs");
        var fieldsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "ViewModels",
            "MainViewModel.Fields.cs");
        var stateEffectsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "ShellSessionCoordinator.StateEffects.cs");

        var statusSource = File.ReadAllText(statusPath);
        var fieldsSource = File.ReadAllText(fieldsPath);
        var stateEffectsSource = File.ReadAllText(stateEffectsPath);

        Assert.DoesNotContain("ModelSummaryText = FormatModelSummary(snapshot.CurrentModel);", statusSource, "Empty snapshots should not directly clear the last known model.");
        Assert.Contains("_lastKnownModelSummaryText", fieldsSource, "ViewModel should track the last non-empty model summary.");
        Assert.Contains("ApplyModelSummary(snapshot)", statusSource, "Model update behavior should be centralized so empty snapshots preserve the last known model.");
        Assert.Contains("currentModel = string.IsNullOrWhiteSpace(snapshot.CurrentModel)", stateEffectsSource, "Hosted UI state logs should expose whether model detection reached the native layer.");
        return Task.CompletedTask;
    }


    public static Task HotkeyBindingParsesStandardModifierKeyString()
    {
        var binding = HotkeyBinding.Parse("Ctrl+Alt+Space");
        Assert.NotNull(binding, "Parse should succeed for a valid hotkey string.");
        Assert.True(binding!.Ctrl, "Ctrl modifier should be set.");
        Assert.True(binding.Alt, "Alt modifier should be set.");
        Assert.False(binding.Shift, "Shift modifier should not be set.");
        Assert.False(binding.Win, "Win modifier should not be set.");
        Assert.Equal("Space", binding.Key, "Key should be 'Space'.");
        return Task.CompletedTask;
    }

    public static Task HotkeyBindingRoundTripsThroughToString()
    {
        var original = "Ctrl+Shift+F12";
        var binding = HotkeyBinding.Parse(original);
        Assert.NotNull(binding, "Parse should succeed.");
        var serialized = binding!.ToString();
        var reparsed = HotkeyBinding.Parse(serialized);
        Assert.NotNull(reparsed, "Re-parse should succeed.");
        Assert.True(reparsed!.Ctrl, "Ctrl should survive round-trip.");
        Assert.True(reparsed.Shift, "Shift should survive round-trip.");
        Assert.False(reparsed.Alt, "Alt should not be set after round-trip.");
        Assert.Equal("F12", reparsed.Key, "Key should survive round-trip.");
        return Task.CompletedTask;
    }

    public static Task HotkeyBindingParseReturnsNullForInvalidInput()
    {
        Assert.Null(HotkeyBinding.Parse(null), "Null input should return null.");
        Assert.Null(HotkeyBinding.Parse(""), "Empty input should return null.");
        Assert.Null(HotkeyBinding.Parse("   "), "Whitespace input should return null.");
        Assert.Null(HotkeyBinding.Parse("+"), "Lone plus should return null.");
        Assert.Null(HotkeyBinding.Parse("Ctrl+"), "Modifier without key should return null.");
        return Task.CompletedTask;
    }

    public static Task HotkeyBindingParseSingleKeyWithoutModifier()
    {
        var binding = HotkeyBinding.Parse("F5");
        Assert.NotNull(binding, "Single key without modifier should parse.");
        Assert.False(binding!.Ctrl, "No Ctrl.");
        Assert.False(binding.Alt, "No Alt.");
        Assert.False(binding.Shift, "No Shift.");
        Assert.False(binding.Win, "No Win.");
        Assert.Equal("F5", binding.Key, "Key should be F5.");
        return Task.CompletedTask;
    }

    public static Task AppSettingsDefaultsHotkeyToCtrlAltSpaceEnabled()
    {
        var settings = new AppSettings();
        Assert.Equal("Ctrl+Alt+Space", settings.GlobalHotkey, "Default hotkey should be Ctrl+Alt+Space.");
        Assert.True(settings.EnableGlobalHotkey, "Global hotkey should be enabled by default.");
        return Task.CompletedTask;
    }

    public static Task SettingsLoadWithoutHotkeyFieldsUsesDefaults()
    {
        var directory = CreateTempDirectory();
        try
        {
            // Write a minimal settings.json without hotkey fields
            var settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllText(settingsPath, """{"appTheme":"Dark","environments":[]}""");

            var service = new ConfigurationService(directory, new TestLogger());
            service.Load();

            Assert.Equal("Ctrl+Alt+Space", service.Settings.GlobalHotkey, "Missing hotkey field should default to Ctrl+Alt+Space.");
            Assert.True(service.Settings.EnableGlobalHotkey, "Missing enable field should default to true.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task SettingsShellExposesGlobalHotkeyControls()
    {
        var xamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.xaml");
        var actionsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Views",
            "SettingsDialog.Actions.cs");
        var enResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "en-us",
            "Resources.resw");
        var zhResourcesPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Strings",
            "zh-cn",
            "Resources.resw");

        var xaml = File.ReadAllText(xamlPath);
        var actions = File.ReadAllText(actionsPath);
        var enResources = File.ReadAllText(enResourcesPath);
        var zhResources = File.ReadAllText(zhResourcesPath);

        Assert.Contains("SettingsEnableGlobalHotkey", xaml, "Shell settings should expose a global hotkey enable settings row.");
        Assert.Contains("IsOn=\"{x:Bind ViewModel.EnableGlobalHotkey, Mode=TwoWay}\"", xaml, "Global hotkey switch should bind to SettingsViewModel.");
        Assert.Contains("SettingsGlobalHotkey", xaml, "Shell settings should expose a hotkey input label.");
        Assert.Contains("Text=\"{x:Bind ViewModel.GlobalHotkey, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml, "Global hotkey input should bind to SettingsViewModel.");
        Assert.Contains("OnResetHotkeyClick", xaml, "Shell settings should include a reset-to-default hotkey button.");
        Assert.Contains("ResetGlobalHotkey()", actions, "Reset button should reset through SettingsViewModel.");
        Assert.Contains("<value>Global hotkey</value>", enResources, "English hotkey label should be present.");
        Assert.Contains("<value>全局热键</value>", zhResources, "Chinese hotkey label should be present.");
        return Task.CompletedTask;
    }

    public static Task SettingsViewModelPersistsAndValidatesGlobalHotkeyFields()
    {
        var viewModelPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "ViewModels",
            "SettingsViewModel.cs");
        var source = File.ReadAllText(viewModelPath);

        Assert.Contains("public bool EnableGlobalHotkey", source, "SettingsViewModel should expose the global hotkey enabled flag.");
        Assert.Contains("public string GlobalHotkey", source, "SettingsViewModel should expose the global hotkey binding string.");
        Assert.Contains("Settings.EnableGlobalHotkey = EnableGlobalHotkey", source, "Settings save should persist the hotkey enabled flag.");
        Assert.Contains("Settings.GlobalHotkey = GlobalHotkey", source, "Settings save should persist the hotkey binding.");
        Assert.Contains("ResetGlobalHotkey()", source, "SettingsViewModel should expose a reset method for the default hotkey.");
        Assert.Contains("TryValidateHotkey", source, "Settings save should validate the hotkey before persisting.");
        Assert.Contains("HotkeyBinding.Parse(GlobalHotkey)", source, "Hotkey validation should use the shared parser.");
        Assert.Contains("binding.GetVirtualKeyCode() == 0", source, "Hotkey validation should reject keys that cannot be registered.");
        return Task.CompletedTask;
    }

    public static Task DiagnosticInstrumentationIncludesHostedUiStreamState()
    {
        var sourcePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Services",
            "DiagnosticService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("LatestControlUiSnapshot", source, "Diagnostic instrumentation should include the current hosted UI snapshot.");
        Assert.Contains("IsBusyStale", source, "Diagnostic instrumentation should report stale busy state.");
        Assert.Contains("BusyStaleSeconds", source, "Diagnostic instrumentation should report stale busy duration.");
        Assert.Contains("FocusedInputHasText", source, "Diagnostic instrumentation should distinguish focused empty input from unsent user text.");
        return Task.CompletedTask;
    }

    public static Task DiagnosticBundleRedactsGatewayUrlHost()
    {
        var input = """{"environments":[{"name":"prod","gatewayUrl":"https://my-secret-host.example.com/control"}]}""";
        var redacted = DiagnosticBundleService.RedactSettingsJson(input);

        Assert.DoesNotContain("my-secret-host.example.com", redacted, "Host should be redacted from settings JSON.");
        Assert.Contains("<host>", redacted, "Redacted host placeholder should be present.");
        Assert.DoesNotContain("/control", redacted, "Path should be redacted from settings JSON.");
        return Task.CompletedTask;
    }

    public static Task DiagnosticBundleRedactsTokenLikeValues()
    {
        var input = """{"environments":[{"name":"dev","gatewayUrl":"https://x.com"}],"someToken":"abc123secret","apiKey":"sk-live-xyz"}""";
        var redacted = DiagnosticBundleService.RedactSettingsJson(input);

        Assert.DoesNotContain("abc123secret", redacted, "Token value should be redacted.");
        Assert.DoesNotContain("sk-live-xyz", redacted, "API key value should be redacted.");
        Assert.Contains("<redacted>", redacted, "Redacted placeholder should be present for token-like fields.");
        return Task.CompletedTask;
    }

    public static Task DiagnosticBundleIncludesRuntimeInfo()
    {
        var info = DiagnosticBundleService.CollectRuntimeInfo();

        Assert.Contains("OS:", info, "Runtime info should include OS.");
        Assert.Contains(".NET:", info, "Runtime info should include .NET version.");
        Assert.Contains("App:", info, "Runtime info should include app version.");
        return Task.CompletedTask;
    }

    public static Task DiagnosticBundleCollectsRecentLogFiles()
    {
        var directory = CreateTempDirectory();
        try
        {
            var logsDir = Path.Combine(directory, "logs");
            Directory.CreateDirectory(logsDir);

            var today = DateTimeOffset.UtcNow;
            // Create logs: 2 recent, 1 old (>7 days)
            var todayLog = Path.Combine(logsDir, $"openclaw-{today:yyyy-MM-dd}.log");
            var recentLog = Path.Combine(logsDir, $"openclaw-{today.AddDays(-3):yyyy-MM-dd}.log");
            var oldLog = Path.Combine(logsDir, $"openclaw-{today.AddDays(-10):yyyy-MM-dd}.log");
            File.WriteAllText(todayLog, "today's log");
            File.WriteAllText(recentLog, "3 days ago");
            File.WriteAllText(oldLog, "10 days ago");
            File.SetLastWriteTimeUtc(todayLog, today.UtcDateTime);
            File.SetLastWriteTimeUtc(recentLog, today.AddDays(-3).UtcDateTime);
            File.SetLastWriteTimeUtc(oldLog, today.AddDays(-10).UtcDateTime);

            var files = DiagnosticBundleService.CollectRecentLogFiles(logsDir, TimeSpan.FromDays(7));

            Assert.Equal(2, files.Count, "Should collect only logs within 7 days.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static Task CloudflareRayParsesPopFromStandardHeader()
    {
        // Standard cf-ray format: hex-POP
        Assert.Equal("LAX", CloudflareRayParser.ParsePoP("8a1b2c3d4e5f6789-LAX"), "Should extract 3-letter PoP code.");
        Assert.Equal("HKG", CloudflareRayParser.ParsePoP("abcdef1234567890-HKG"), "Should extract HKG.");
        Assert.Equal("NRT", CloudflareRayParser.ParsePoP("0000000000000000-NRT"), "Should extract NRT.");
        // Lowercase should also work
        Assert.Equal("SJC", CloudflareRayParser.ParsePoP("aabbccdd11223344-sjc"), "Should handle lowercase PoP and uppercase it.");
        return Task.CompletedTask;
    }

    public static Task CloudflareRayReturnsNullForMissingOrMalformed()
    {
        Assert.Null(CloudflareRayParser.ParsePoP(null), "Null input should return null.");
        Assert.Null(CloudflareRayParser.ParsePoP(""), "Empty input should return null.");
        Assert.Null(CloudflareRayParser.ParsePoP("8a1b2c3d4e5f6789"), "No dash means no PoP.");
        Assert.Null(CloudflareRayParser.ParsePoP("8a1b2c3d4e5f6789-"), "Trailing dash with no code should return null.");
        Assert.Null(CloudflareRayParser.ParsePoP("8a1b2c3d4e5f6789-AB"), "Two-letter code is not a valid PoP.");
        return Task.CompletedTask;
    }

    public static Task LatencyTooltipIncludesPopWhenAvailable()
    {
        var summary = new LatencyHistorySummary(5, 42, 30, 38, 45, 50);
        var tooltip = LatencyTooltipFormatter.Format(summary, "LAX");

        Assert.Contains("PoP: LAX", tooltip, "Tooltip should include PoP line when available.");
        Assert.Contains("Latest: 42 ms", tooltip, "Tooltip should still include latency data.");
        return Task.CompletedTask;
    }

    public static async Task SingleInstanceStopAwaitsListenerTaskCompletion()
    {
        // Verify that SingleInstanceCoordinator exposes a StopAsync method
        var type = typeof(SingleInstanceCoordinator);
        var stopMethod = type.GetMethod("StopAsync");
        Assert.NotNull(stopMethod, "SingleInstanceCoordinator should expose a StopAsync method.");

        // Verify StopAsync returns Task
        Assert.Equal(typeof(Task), stopMethod!.ReturnType, "StopAsync should return Task.");

        // Functional test: create a primary coordinator, start listening, then stop
        var coordinator = SingleInstanceCoordinator.CreatePrimaryOrSecondary(new TestLogger());
        if (coordinator.IsPrimary)
        {
            coordinator.StartListening();
            await coordinator.StopAsync();
            // After StopAsync, the coordinator should be safe to dispose without race
            coordinator.Dispose();
        }
        else
        {
            // If not primary (unlikely in test), just dispose
            coordinator.Dispose();
        }
    }

    public static Task AppSettingsDefaultsAlwaysOnTopToFalse()
    {
        var settings = new AppSettings();
        Assert.False(settings.AlwaysOnTop, "AlwaysOnTop should default to false.");
        return Task.CompletedTask;
    }

    public static Task AlwaysOnTopAppliesNativeTopmostFallback()
    {
        var alwaysOnTopPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.AlwaysOnTop.cs");
        var frameHelperPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Helpers",
            "WindowFrameHelper.cs");

        var alwaysOnTop = File.ReadAllText(alwaysOnTopPath);
        var frameHelper = File.ReadAllText(frameHelperPath);

        Assert.Contains("WindowFrameHelper.SetTopMost(this, value)", alwaysOnTop, "Always-on-top should use a native topmost fallback, not only AppWindow.Presenter.");
        Assert.Contains("public static bool SetTopMost(Window window, bool value)", frameHelper, "WindowFrameHelper should expose a native SetWindowPos topmost helper.");
        Assert.Contains("new(-1)", frameHelper, "Native topmost helper should use HWND_TOPMOST.");
        Assert.Contains("new(-2)", frameHelper, "Native topmost helper should use HWND_NOTOPMOST.");
        return Task.CompletedTask;
    }

    public static Task AlwaysOnTopPinButtonUsesAccentColorWhenActive()
    {
        var alwaysOnTopPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "MainWindow.AlwaysOnTop.cs");

        var alwaysOnTop = File.ReadAllText(alwaysOnTopPath);

        Assert.Contains("PinButton.Foreground", alwaysOnTop, "Pinned state should be visible through the Pin button foreground.");
        Assert.Contains("AccentTextFillColorPrimaryBrush", alwaysOnTop, "Pinned state should use a theme-aware accent text brush.");
        Assert.Contains("TextFillColorSecondaryBrush", alwaysOnTop, "Unpinned state should use a theme-aware secondary text brush so it remains visible on light backgrounds.");
        return Task.CompletedTask;
    }

    public static Task AppSettingsDefaultsCompactModeToFalse()
    {
        var settings = new AppSettings();
        Assert.False(settings.CompactMode, "CompactMode should default to false.");
        return Task.CompletedTask;
    }

    public static Task CompactModeBoundsBypassMinimumPersistableSize()
    {
        var boundsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "OpenClaw",
            "Helpers",
            "WindowBoundsUtilities.cs");
        var boundsSource = File.ReadAllText(boundsPath);

        Assert.Contains("MinimumPersistedWindowWidth = 640", boundsSource, "Normal bounds persistence should keep a desktop-sized minimum width.");
        Assert.Contains("MinimumPersistedWindowHeight = 480", boundsSource, "Normal bounds persistence should keep a desktop-sized minimum height.");

        var settings = new AppSettings();
        Assert.Equal(-1d, settings.CompactWindowLeft, "CompactWindowLeft should default to -1 (unset).");
        Assert.Equal(-1d, settings.CompactWindowTop, "CompactWindowTop should default to -1 (unset).");
        return Task.CompletedTask;
    }

    public static Task WebViewCircuitBreakerTripsAfterRepeatedFailures()
    {
        var breaker = new WebViewCircuitBreaker(maxAttempts: 5, windowSeconds: 60);

        // 5 attempts should be allowed
        for (var i = 0; i < 5; i++)
        {
            Assert.True(breaker.CanAttempt(), $"Attempt {i + 1} should be allowed.");
            breaker.RecordAttempt();
        }

        // 6th should be blocked
        Assert.False(breaker.CanAttempt(), "6th attempt within window should be blocked.");
        return Task.CompletedTask;
    }

    public static Task WebViewCircuitBreakerResetsAfterCooldown()
    {
        var breaker = new WebViewCircuitBreaker(maxAttempts: 3, windowSeconds: 1);

        for (var i = 0; i < 3; i++)
        {
            breaker.RecordAttempt();
        }

        Assert.False(breaker.CanAttempt(), "Should be tripped after 3 attempts.");

        // Manual reset
        breaker.Reset();
        Assert.True(breaker.CanAttempt(), "Should allow attempts after reset.");
        return Task.CompletedTask;
    }

    private static ControlUiProbeSnapshot CreateAuthRequiredSnapshot()
    {
        return new ControlUiProbeSnapshot(
            ControlUiPhase.AuthRequired,
            "Auth required",
            "Sign in again",
            "https://gateway.example/login",
            ShellDetected: false,
            IsBusy: false,
            InputFocused: false,
            WorkState: "idle",
            CurrentModel: string.Empty);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenClaw.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Timed out waiting for the expected condition.");
    }

    private static Task? GetCurrentProbeTask(ControlUiLatencyService service)
    {
        var field = typeof(ControlUiLatencyService).GetField("_probeTask", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(service) as Task;
    }

    private static ShellSessionCoordinator CreateCoordinator(
        FakeShellSessionWebView webView,
        FakeShellSessionBridge bridge,
        RecoveryPolicyOptions? recoveryOptions = null,
        HeartbeatOptions? heartbeatOptions = null)
    {
        var coordinator = new ShellSessionCoordinator();
        coordinator.AttachAsync(
            webView,
            bridge,
            recoveryOptions ?? new RecoveryPolicyOptions
            {
                ReconnectDelayMs = 1,
                MaxReconnectDelayMs = 1,
                ReconnectBackoffMultiplier = 1,
                HardRefreshCooldownSeconds = 0
            },
            heartbeatOptions ?? new HeartbeatOptions()).GetAwaiter().GetResult();
        return coordinator;
    }
}

internal sealed class TestLogger : IAppLogger
{
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message) { }
    public void Info(string eventKey, object? context = null) { }
    public void Warning(string eventKey, object? context = null) { }
    public void Error(string eventKey, object? context = null) { }
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        _sendAsync = sendAsync;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _sendAsync(request, cancellationToken);
}

internal sealed class FakeShellSessionWebView : IShellSessionWebView
{
    private readonly Queue<ControlUiProbeSnapshot> _snapshots = new();

    public int InspectCount { get; private set; }

    public int ReloadCount { get; private set; }

    public event Action<ConnectionState>? ConnectionStateChanged
    {
        add { }
        remove { }
    }

    public event Action<string>? NavigationErrorOccurred
    {
        add { }
        remove { }
    }

    public event Action<string?>? NavigationCompleted
    {
        add { }
        remove { }
    }

    public event Action<HeartbeatProbeResult>? HeartbeatObserved
    {
        add { }
        remove { }
    }

    public event Action<string>? HeartbeatFailed
    {
        add { }
        remove { }
    }

    public event Action<ControlUiProbeSnapshot>? ControlUiSnapshotUpdated;

    public void EnqueueSnapshot(ControlUiProbeSnapshot snapshot)
    {
        _snapshots.Enqueue(snapshot);
    }

    public void RaiseControlUiSnapshotUpdated(ControlUiProbeSnapshot snapshot)
    {
        ControlUiSnapshotUpdated?.Invoke(snapshot);
    }

    public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync()
    {
        InspectCount++;

        if (_snapshots.Count == 0)
        {
            return Task.FromResult(ControlUiProbeSnapshot.Unknown);
        }

        return Task.FromResult(_snapshots.Dequeue());
    }

    public void Reload()
    {
        ReloadCount++;
    }

    public int TotalControlUiInspectionRequests => InspectCount;

    public int CachedControlUiInspectionRequests => 0;

    public int CoalescedControlUiInspectionRequests => 0;

    public int HeartbeatRecoveryRequests => 0;
}

internal sealed class FakeShellSessionBridge : IShellSessionBridge
{
    public bool SessionRefreshResult { get; set; }
    public bool RecentMessagesResult { get; set; }
    public bool LightweightSyncResult { get; set; }
    public bool ReconnectIntentResult { get; set; }

    public int RequestSessionRefreshCalls { get; private set; }
    public int RequestRecentMessagesCalls { get; private set; }
    public int RequestLightweightSyncCalls { get; private set; }
    public int NotifyReconnectIntentCalls { get; private set; }

    public event Action<SessionReadyEventArgs>? SessionReady
    {
        add { }
        remove { }
    }
    public event Action<EventGapEventArgs>? EventGapDetected;

    public Task<bool> RequestSessionRefreshAsync()
    {
        RequestSessionRefreshCalls++;
        return Task.FromResult(SessionRefreshResult);
    }

    public Task<bool> RequestRecentMessagesAsync()
    {
        RequestRecentMessagesCalls++;
        return Task.FromResult(RecentMessagesResult);
    }

    public Task<bool> RequestLightweightSyncAsync()
    {
        RequestLightweightSyncCalls++;
        return Task.FromResult(LightweightSyncResult);
    }

    public Task<bool> NotifyReconnectIntentAsync()
    {
        NotifyReconnectIntentCalls++;
        return Task.FromResult(ReconnectIntentResult);
    }

    public void RaiseEventGap(EventGapEventArgs args)
    {
        EventGapDetected?.Invoke(args);
    }
}

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message)
    {
        if (condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; Actual: {actual}");
        }
    }

    public static void Null(object? value, string message)
    {
        if (value is not null)
        {
            throw new InvalidOperationException($"{message} Value was: {value}");
        }
    }

    public static void NotNull(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Contains(string expectedSubstring, string actual, string message)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message} Expected substring: {expectedSubstring}; Actual: {actual}");
        }
    }

    public static void DoesNotContain(string unexpectedSubstring, string actual, string message)
    {
        if (actual.Contains(unexpectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message} Unexpected substring: {unexpectedSubstring}");
        }
    }
}

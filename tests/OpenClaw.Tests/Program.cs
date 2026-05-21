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
    ("Version metadata is 3.3.5", Tests.VersionMetadataIs335),
    ("Repository code style is explicit", Tests.RepositoryCodeStyleIsExplicit),
    ("Code style guide documents project conventions", Tests.CodeStyleGuideDocumentsProjectConventions),
    ("Architecture guide preserves current module boundaries", Tests.ArchitectureGuidePreservesCurrentModuleBoundaries),
    ("Core-compatible files are physically owned by Core project", Tests.CoreCompatibleFilesArePhysicallyOwnedByCoreProject),
    ("Directory build enables analyzers and style", Tests.DirectoryBuildEnablesAnalyzersAndStyle),
    ("Executable test harness rejects dotnet test false positives", Tests.ExecutableTestHarnessRejectsDotnetTestFalsePositives),
    ("Test harness is split by domain", Tests.TestHarnessIsSplitByDomain),
    ("Documentation includes WinUI format platform", Tests.DocumentationIncludesWinUiFormatPlatform),
    ("About dialog GitHub link targets Guijianchou profile", Tests.AboutDialogGitHubLinkTargetsGuijianchouProfile),
    ("Settings window uses non-blocking frame refresh", Tests.SettingsWindowUsesNonBlockingFrameRefresh),
    ("Settings window avoids first-frame black flash", Tests.SettingsWindowAvoidsFirstFrameBlackFlash),
    ("Title bar caption button states use opaque theme colors", Tests.TitleBarCaptionButtonStatesUseOpaqueThemeColors),
    ("Top status pill leaves room for long model names", Tests.TopStatusPillLeavesRoomForLongModelNames),
    ("Top status model text matches status bar font size", Tests.TopStatusModelTextMatchesStatusBarFontSize),
    ("Top status typography uses shared resources", Tests.TopStatusTypographyUsesSharedResources),
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
    ("Hosted UI bridge executable script reads current model from select", Tests.HostedUiBridgeExecutableScriptReadsCurrentModelFromSelect),
    ("Hosted UI bridge reads current model from OpenClaw app state", Tests.HostedUiBridgeReadsCurrentModelFromOpenClawAppState),
    ("Hosted UI bridge executable script reads current model from app state", Tests.HostedUiBridgeExecutableScriptReadsCurrentModelFromAppState),
    ("Hosted UI bridge uses structured model source pipeline", Tests.HostedUiBridgeUsesStructuredModelSourcePipeline),
    ("Hosted UI bridge defers app-state defaults", Tests.HostedUiBridgeDefersAppStateDefaults),
    ("Hosted UI bridge keeps null override default semantics", Tests.HostedUiBridgeKeepsNullOverrideDefaultSemantics),
    ("Hosted UI bridge avoids object-shaped model strings", Tests.HostedUiBridgeAvoidsObjectShapedModelStrings),
    ("Hosted UI snapshots carry model source instrumentation", Tests.HostedUiSnapshotsCarryModelSourceInstrumentation),
    ("Hosted UI session ready carries model source", Tests.HostedUiSessionReadyCarriesModelSource),
    ("Hosted UI bridge executable session ready carries model source", Tests.HostedUiBridgeExecutableSessionReadyCarriesModelSource),
    ("Hosted UI bridge executable command dispatch raises host events", Tests.HostedUiBridgeExecutableCommandDispatchRaisesHostEvents),
    ("Hosted UI bridge executable known command returns fallback handled", Tests.HostedUiBridgeExecutableKnownCommandReturnsFallbackHandled),
    ("Hosted UI bridge uses safe host message helper", Tests.HostedUiBridgeUsesSafeHostMessageHelper),
    ("Hosted UI bridge executable session ready noops without WebView host", Tests.HostedUiBridgeExecutableSessionReadyNoopsWithoutWebViewHost),
    ("Hosted UI bridge ignores sidebar-only mutations during status polling", Tests.HostedUiBridgeIgnoresSidebarOnlyMutationsDuringStatusPolling),
    ("Hosted UI bridge executable ignores sidebar-only mutations", Tests.HostedUiBridgeExecutableIgnoresSidebarOnlyMutations),
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
    ("Settings save reapplies live shell options", Tests.SettingsSaveReappliesLiveShellOptions),
    ("AppSettings defaults CompactMode to false", Tests.AppSettingsDefaultsCompactModeToFalse),
    ("Compact mode bounds bypass minimum persistable size", Tests.CompactModeBoundsBypassMinimumPersistableSize),
    ("Compact mode switches top bar to compact layout", Tests.CompactModeSwitchesTopBarToCompactLayout),
    ("WebView status probes ignore stale generations", Tests.WebViewStatusProbesIgnoreStaleGenerations),
    ("WebView inspection cache is generation scoped", Tests.WebViewInspectionCacheIsGenerationScoped),
    ("Heartbeat loop owns timer and task lifetime", Tests.HeartbeatLoopOwnsTimerAndTaskLifetime),
    ("WebView service heartbeat is split by responsibility", Tests.WebViewServiceHeartbeatIsSplitByResponsibility),
    ("WebView service Control UI inspection is split by responsibility", Tests.WebViewServiceControlUiInspectionIsSplitByResponsibility),
    ("Disposable services implement IDisposable", Tests.DisposableServicesImplementIDisposable),
    ("WebView service is split by responsibility", Tests.WebViewServiceIsSplitByResponsibility),
    ("Log viewer loads tail asynchronously", Tests.LogViewerLoadsTailAsynchronously),
    ("Log tail reader reads from file end", Tests.LogTailReaderReadsFromFileEnd),
    ("Hosted bridge script has testable asset seam", Tests.HostedBridgeScriptHasTestableAssetSeam),
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

# Deep Refactor Hardening — Opus Review (v2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the architectural separation that v3.3.x started but left partially realized. Make ownership boundaries machine-enforced, eliminate static coupling patterns, and decompose the two largest runtime assets (WebViewService 1609 LOC across partials, HostedUiBridge.Script.js 921 LOC) into focused, independently testable units.

**Review findings driving this plan (second pass):**

1. `WebViewService` partials total ~1609 LOC. The `.ControlUiInspection.cs` partial (413 LOC) owns inspection gate, probe loop, snapshot cache, generation checks, and JSON parsing — a self-contained responsibility sharing mutable state with navigation/lifecycle.
2. `WebViewService.Heartbeat.cs` (365 LOC) mixes loop lifetime management (`_heartbeatCts`, `_heartbeatTask`, `ObserveHeartbeatShutdownAsync`) with probe logic and threshold decisions.
3. `WebViewService.Commands.cs` (238 LOC) embeds two large inline JS scripts (~160 LOC combined) for stop/abort. These should be embedded assets like the bridge script, not string literals in C#.
4. `HostedUiBridge.Script.js` (921 LOC) mixes host messaging, command dispatch, mutation filtering, status inspection, model resolution, stale-busy detection, session-ready events, and polling timer in one IIFE.
5. `MainWindow.WebView.cs` (218 LOC) owns recreation scheduling, circuit breaker interaction, and instrumentation — orchestration logic that belongs in a service, not a Window partial.
6. `MainWindow.CompactMode.cs` manually patches 8 XAML properties with magic numbers instead of using visual states.
7. `MainViewModel.Status.cs` uses `App.MainWindow?.DispatcherQueue.TryEnqueue()` — static coupling from ViewModel to Window singleton.
8. `MainViewModel.Fields.cs` creates static `Brush` instances (`NeutralBrush`, `SuccessBrush`, etc.) at class level — these are not theme-aware and bypass the resource system.
9. `SettingsViewModel.SaveAll()` directly mutates `App.Configuration.Settings` then calls `App.Configuration.Save()` — no separation between edit state and persistence. The `DidChangeLiveShellOptions()` method recomputes from originals instead of carrying a typed diff.
10. `MainWindow.Commands.cs` `ApplyLiveShellSettings()` re-reads `App.Configuration.Settings` after save — the save result already knows what changed.
11. `DiagnosticBundleService.CollectRuntimeInfo()` uses `Type.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment, ...")` — platform knowledge inside Core via reflection.
12. `ShellSessionCoordinator.Adapters.cs` extension method reads `App.Configuration.Settings.RecoveryPolicy` and `App.Configuration.Settings.Heartbeat` directly — config should be injected by the caller.
13. `LogViewerDialog` loads log tails off the UI thread but has no cancellation. Fast repeated refresh/open/close can race UI updates.
14. README architecture diagram still shows `MainWindow → MainViewModel → ConfigurationService/LoggingService → WebViewService` — does not reflect the real runtime.
15. `DEVELOPMENT_NOTES.md` says "Regression coverage now checks..." in multiple places, but the test harness was removed in v3.3.6.
16. The bridge script polling uses `setTimeout` recursion with variable intervals but no drift correction — can accumulate timing skew over long sessions.
17. `MainViewModel` spans 17 partial files (~1727 LOC total). Status formatting, indicator animation, and heartbeat UI are pure presentation that could be extracted.
18. `WebViewService.Heartbeat.cs` reads `App.Configuration.Settings.Heartbeat` (line 43) and `App.Configuration.Settings.RecoveryPolicy.HardRefreshCooldownSeconds` (line 333) directly — heartbeat settings should be injected at `StartHeartbeat` time, not read from global state mid-loop.
19. `WebViewService` uses `App.Logger` 51 times across its partials with no logger injection — makes the service untestable in isolation and violates the Core pattern where `IAppLogger` is injected.
20. `MainViewModel.Core.Properties.cs` exposes `WebViewService`, `HostedUiBridge`, and `Coordinator` as public properties (lines 88-98) — leaks internal service instances to the view layer, enabling bypasses of ViewModel orchestration.
21. `MainViewModel.Commands.cs` `ShowCircuitBreakerError()` uses a hardcoded English string (line 67) instead of `StringResources` — violates the localization rule.

**Architecture target state:**

```text
MainWindow (WinUI shell — XAML + thin code-behind)
├── MainViewModel (orchestration + bindable properties)
│   ├── StatusPresenter (pure formatting, no field mutation)
│   ├── WebViewService (navigation + lifecycle shell, ~300 LOC)
│   │   ├── WebViewStatusInspector (status probe ownership)
│   │   ├── HeartbeatRuntime (loop lifetime primitive)
│   │   ├── WebViewGenerationTracker (generation counter)
│   │   └── WebViewCommandAssets (embedded JS for stop/abort)
│   ├── HostedUiBridge (JS bridge lifecycle)
│   │   └── embedded JS assets (composition shell + 4 focused modules)
│   ├── WebViewRecreationService (scheduling + circuit breaker)
│   ├── LiveShellSettingsApplier (typed change pipeline)
│   ├── ShellSessionCoordinator (recovery policy — Core)
│   │   └── adapter interfaces (injected, no App.Configuration reads)
│   └── ControlUiLatencyService (Core)
└── OpenClaw.Core (pure .NET — zero WinUI/WebView2 references)
    ├── settings / configuration
    ├── recovery policy / state machine
    ├── diagnostics / log utilities
    └── parser / policy helpers
```

**Ground rules:**

- Keep `src/OpenClaw.Core` as the pure .NET shared source tree.
- Do not restore the removed `tests/` directory.
- Keep Release output folders.
- Do not rewrite historical docs.
- Every task ends with a commit-sized checkpoint and verification.
- New types prefer `internal sealed` unless the public surface is required.
- New async methods carry `CancellationToken` unless the caller provably never cancels.
- Inline JS scripts >30 lines must become embedded resources.

---

## Phase 0: Verification Infrastructure

### Task 0.1 — Repo Structure Guardrail Scripts

**Files:** Create `tools/verify-repo-structure.ps1`, `tools/verify-bridge-model.ps1`. Modify `README.md`, `readme_zh.md`, `DEVELOPMENT_NOTES.md`.

- [ ] **Step 1:** Create `tools/verify-repo-structure.ps1`:
  - No `tests/` directory exists.
  - `OpenClaw.sln` does not reference `OpenClaw.Tests`.
  - All `.cs` under `src/OpenClaw.Core` are free of: `using Microsoft.UI`, `using Microsoft.Web.WebView2`, `using Windows.Graphics`, `using Windows.UI`, `using WinRT`, `App.Configuration`, `App.Logger`, `App.MainWindow`, `Type.GetType("Microsoft.Web.WebView2`.
  - `OpenClaw.csproj` lists all expected embedded JS resources.
  - Post-Phase-1 checks (enabled incrementally): WebViewService.cs must not own inspection internals; Heartbeat.cs must not own CTS/Task; CompactMode.cs must not patch XAML properties; Script.js must be <300 lines.

- [ ] **Step 2:** Create `tools/verify-bridge-model.ps1` — Node.js MODEL resolver test cases (skip gracefully if Node absent).

- [ ] **Step 3:** Document active verification commands in README/DEVELOPMENT_NOTES.

- [ ] **Step 4:** Run verification. Commit: `chore: add active refactor verification scripts`

---

## Phase 1: WebView Service Decomposition

### Task 1.1 — Extract WebViewGenerationTracker + WebViewStatusInspector

**Why (from findings #1):** Inspection is a self-contained responsibility (413 LOC) that should not share mutable state with navigation.

**Files:** Create `WebViewGenerationTracker.cs`, `WebViewStatusInspector.cs`. Modify `WebViewService.cs`, `WebViewService.ControlUiInspection.cs`, `WebViewService.Heartbeat.cs`.

- [ ] **Step 1:** Create `WebViewGenerationTracker` — `Current`, `Next()`, `IsCurrent(int)`.
- [ ] **Step 2:** Create `WebViewStatusInspector` — moves all inspection fields, gate, probe loop, snapshot cache, `ParseControlUiSnapshot`, `ExecuteControlUiInspectionAsync`, `ApplyControlUiSnapshot`, `ProbeControlUiStateAfterNavigationAsync`. Public contract: `InspectAsync`, `StartProbeLoop`, `CancelProbeLoop`, `InvalidateCache`, `SetLoadingSnapshot`, `SetPageLoadedSnapshot`, `SetUnavailableSnapshot`, `TryApplyHostMessage`, `SnapshotUpdated` event.
- [ ] **Step 3:** Reduce `WebViewService.ControlUiInspection.cs` to thin delegation shim.
- [ ] **Step 4:** Replace direct field access in `WebViewService.cs` event handlers with inspector calls.
- [ ] **Step 5:** Update heartbeat to call through wrapper.
- [ ] **Step 6:** Add guardrail. Verify + commit: `refactor: isolate WebView status inspection ownership`

### Task 1.2 — Extract HeartbeatRuntime

**Why (from findings #2):** Loop lifetime is generic and reusable; probe logic and threshold decisions are domain-specific.

**Files:** Create `HeartbeatRuntime.cs`. Modify `WebViewService.Heartbeat.cs`, `WebViewService.cs`.

- [ ] **Step 1:** Create `HeartbeatRuntime` — owns CTS, Task, key-based idempotency, `Start(key, Func<CT, Task>)`, `Stop()`, `IsSameRun(key)`, `Dispose()`.
- [ ] **Step 2:** Replace `_heartbeatCts`/`_heartbeatTask` with `_heartbeatRuntime`.
- [ ] **Step 3:** `StartHeartbeat` computes composite key, delegates to runtime.
- [ ] **Step 4:** `RunSessionAwareHeartbeatLoopAsync` creates/owns its `PeriodicTimer` internally.
- [ ] **Step 5:** Simplify `StopHeartbeat`. Add guardrail. Verify + commit: `refactor: isolate heartbeat loop ownership`

### Task 1.3 — Extract WebView Command Scripts Into Embedded Assets

**Why (from findings #3):** Two inline JS scripts (~160 LOC) in `WebViewService.Commands.cs` violate the project rule that bridge JS belongs in embedded assets.

**Files:** Create `Services/WebViewCommands.StopInjection.js`, `Services/WebViewCommands.AbortRun.js`. Modify `WebViewService.Commands.cs`, `OpenClaw.csproj`.

- [ ] **Step 1:** Extract the `/stop` injection script into `WebViewCommands.StopInjection.js`.
- [ ] **Step 2:** Extract the abort-run script into `WebViewCommands.AbortRun.js`.
- [ ] **Step 3:** Add both as `EmbeddedResource` in csproj.
- [ ] **Step 4:** Load via `Assembly.GetManifestResourceStream` in `WebViewService.Commands.cs` (lazy, like `HostedUiBridgeScript`).
- [ ] **Step 5:** Verify + commit: `refactor: move WebView command scripts to embedded assets`

### Task 1.4 — Extract WebView Recreation Service

**Why (from findings #5):** Recreation scheduling is orchestration logic that belongs in a service, not a Window partial.

**Files:** Create `Services/WebViewRecreationService.cs`. Modify `MainWindow.WebView.cs`, `MainWindow.Shared.cs`.

- [ ] **Step 1:** Create `WebViewRecreationService` — owns scheduling state, merged-count, circuit breaker checks, instrumentation. Exposes `Schedule(reason)`, `OnTimerTick()`, `MarkCompleted()`, `TryConsumeQueued()`.
- [ ] **Step 2:** Move scheduling logic from `MainWindow.WebView.cs` into service.
- [ ] **Step 3:** Keep `MainWindow.WebView.cs` responsible only for: timer wiring, WebView2 control creation/swap (XAML-dependent), and calling `ViewModel.InitializeWebViewAsync`.
- [ ] **Step 4:** Verify + commit: `refactor: extract WebView recreation scheduling`

---

## Phase 2: UI Layer Hardening

### Task 2.1 — Compact Mode Visual States

**Why (from findings #6):** Manual property patching duplicates layout constants and is fragile to style changes.

**Files:** Modify `MainWindow.xaml`, `MainWindow.CompactMode.cs`, `Styles/StatusResources.xaml`.

- [ ] **Step 1:** Add compact-mode resources to `StatusResources.xaml`.
- [ ] **Step 2:** Add `VisualStateGroup` `ShellModeStates` with `FullMode`/`CompactMode` states.
- [ ] **Step 3:** Replace `ApplyCompactTopBarState` with `VisualStateManager.GoToState`.
- [ ] **Step 4:** Remove duplicated constants. Add guardrail. Verify + commit: `refactor: drive compact top bar through visual states`

### Task 2.2 — Live Shell Settings Apply Pipeline

**Why (from findings #9, #10):** Save result already knows what changed — apply path should consume a typed diff, not re-read global state.

**Files:** Create `Core/Models/LiveShellSettings.cs`, `Core/Services/LiveShellSettingsChange.cs`, `Services/LiveShellSettingsApplier.cs`. Modify `SettingsViewModel.cs`, `MainWindow.Commands.cs`.

- [ ] **Step 1:** Create `LiveShellSettings` record in Core.
- [ ] **Step 2:** Create `LiveShellSettingsChange` record with `DidChangeAlwaysOnTop`, `DidChangeGlobalHotkey`, `HasChanges`.
- [ ] **Step 3:** `SettingsSaveResult` carries `LiveShellSettingsChange` computed from before/after snapshots (replaces `DidChangeLiveShellOptions()` bool recomputation).
- [ ] **Step 4:** Create `LiveShellSettingsApplier` in WinUI layer.
- [ ] **Step 5:** Replace `ApplyLiveShellSettings()` with `_applier.Apply(saveResult.Change)`.
- [ ] **Step 6:** Verify + commit: `refactor: centralize live shell settings application`

### Task 2.3 — Log Viewer Cancellation

**Why (from findings #13):** No cancellation means fast repeated refresh/open/close can race UI updates.

**Files:** Modify `Views/LogViewerDialog.xaml.cs`, `Core/Helpers/LogFileUtilities.cs`.

- [ ] **Step 1:** Add `_loadCts` field and `CancelPendingLoad()`.
- [ ] **Step 2:** Subscribe `Closed` to cancel.
- [ ] **Step 3:** Guard `LoadTodayLogAsync` with cancel-before-start + token propagation.
- [ ] **Step 4:** Add `CancellationToken` overload to `LogFileUtilities.ReadLastLines`.
- [ ] **Step 5:** Verify + commit: `refactor: make log viewer tail loading cancellable`

### Task 2.4 — Eliminate Static DispatcherQueue Coupling in MainViewModel

**Why (from findings #7):** `App.MainWindow?.DispatcherQueue.TryEnqueue()` couples ViewModel to Window singleton. If the window is null (during tests or startup race), the callback silently drops.

**Files:** Modify `MainViewModel.Status.cs`, `MainViewModel.cs` or `MainViewModel.Shared.cs`.

- [ ] **Step 1:** Add a `private readonly Action<Action> _dispatchToUi` field initialized from constructor parameter (defaulting to `App.MainWindow?.DispatcherQueue.TryEnqueue` for production).
- [ ] **Step 2:** Replace all `RunOnUiThread(...)` calls with `_dispatchToUi(...)`.
- [ ] **Step 3:** Remove the static `RunOnUiThread` helper.
- [ ] **Step 4:** Verify + commit: `refactor: inject UI dispatcher into MainViewModel`

---

## Phase 3: Bridge Script Decomposition

### Task 3.1 — Split HostedUiBridge.Script.js Into Focused Assets

**Why (from findings #4, #16):** 921 LOC IIFE mixing 7+ concerns. Each should be separately testable.

**Files:** Create `HostedUiBridge.HostMessaging.js`, `HostedUiBridge.MutationFilter.js`, `HostedUiBridge.CommandDispatch.js`, `HostedUiBridge.StatusInspection.js`. Modify `HostedUiBridge.Script.js`, `HostedUiBridge.Script.cs`, `OpenClaw.csproj`.

- [ ] **Step 1:** Extract `HostedUiBridge.HostMessaging.js` — `postHostMessage`, safe `chrome.webview.postMessage` wrapper, `postStatus` deduplication.
- [ ] **Step 2:** Extract `HostedUiBridge.MutationFilter.js` — `STATUS_PROBE_EXCLUDED_SELECTOR`, `isStatusProbeExcludedElement`, `isSidebarOnlyMutation`, `isStatusRelevantMutation`.
- [ ] **Step 3:** Extract `HostedUiBridge.CommandDispatch.js` — `bridgeTargets`, `invokeBridgeMethod`, `dispatchBridgeEvent`, `onCommand` handler factory.
- [ ] **Step 4:** Extract `HostedUiBridge.StatusInspection.js` — `inspectControlUi`, `readOpenClawAppStateStatus`, `detectBusyFromApi`, `collectSignalText`, `collectDomActivitySignature`, `applyBusyStaleness`, `hasVisibleElement`, all phase-detection logic.
- [ ] **Step 5:** Reduce `HostedUiBridge.Script.js` to composition shell (<250 LOC): STRINGS, utility functions (`isVisible`, `textOf`, `labelOf`, `isEditableElement`, `compactText`), module composition, `window.__openClawHostBridge` wiring, MutationObserver setup, history wrapping, event listeners, polling timer.
- [ ] **Step 6:** Update `HostedUiBridge.Script.cs` — add placeholders for each new asset, load in dependency order: HostMessaging → MutationFilter → ModelResolver → StatusInspection → CommandDispatch → main script.
- [ ] **Step 7:** Add all new `.js` as `EmbeddedResource` in csproj.
- [ ] **Step 8:** Extend `tools/verify-bridge-model.ps1` to test command dispatch, host messaging, and mutation filter.
- [ ] **Step 9:** Add line-count guardrail. Verify + commit: `refactor: split hosted bridge browser assets`

### Task 3.2 — Fix Polling Timer Drift

**Why (from findings #16):** `setTimeout` recursion with variable intervals accumulates timing skew over long sessions. Busy sessions poll at 4s, idle at 15s — drift is negligible for idle but compounds during sustained busy periods.

**Files:** Modify `HostedUiBridge.Script.js` (or the new composition shell after Task 3.1).

- [ ] **Step 1:** Replace `setTimeout` recursion with a self-correcting pattern that records `lastTickAt` and adjusts the next delay to compensate for execution time.
- [ ] **Step 2:** Verify + commit: `fix: correct bridge polling timer drift`

---

## Phase 4: Core Boundary Enforcement

### Task 4.1 — Remove WebView2 Reflection From DiagnosticBundleService

**Why (from findings #11):** Platform knowledge inside Core via reflection.

**Files:** Modify `Core/Services/DiagnosticBundleService.cs`, `Services/DiagnosticService.cs`.

- [ ] **Step 1:** Add `string? webView2RuntimeVersion` parameter to `CollectRuntimeInfo` (or create a `DiagnosticRuntimeInfo` input record).
- [ ] **Step 2:** Move WebView2 version collection to WinUI layer using `CoreWebView2Environment.GetAvailableBrowserVersionString()`.
- [ ] **Step 3:** Pass version string into Core as plain text.
- [ ] **Step 4:** Strengthen guardrail. Verify + commit: `refactor: keep WebView2 diagnostics in WinUI layer`

### Task 4.2 — Remove App.Configuration Reads From Coordinator Adapter Extension

**Why (from findings #12):** The `AttachAsync` extension method reads `App.Configuration.Settings.RecoveryPolicy` and `App.Configuration.Settings.Heartbeat` directly. Config should be injected by the caller.

**Files:** Modify `Services/ShellSessionCoordinator.Adapters.cs`, `ViewModels/MainViewModel.Lifecycle.cs`.

- [ ] **Step 1:** Remove default parameter values from the extension method that read `App.Configuration`.
- [ ] **Step 2:** Make `MainViewModel.InitializeWebViewAsync` pass config explicitly:
  ```csharp
  await _coordinator.AttachAsync(_webViewService, _hostedUiBridge,
      App.Configuration.Settings.RecoveryPolicy,
      App.Configuration.Settings.Heartbeat);
  ```
- [ ] **Step 3:** Verify + commit: `refactor: inject config into coordinator adapter`

### Task 4.3 — Inject Logger Into WebViewService

**Why (from findings #19):** `WebViewService` uses `App.Logger` 51 times with no injection. This makes the service untestable and violates the Core pattern where `IAppLogger` is injected via constructor.

**Files:** Modify `Services/WebViewService.cs`, all WebViewService partials.

- [ ] **Step 1:** Add `private readonly IAppLogger _logger;` field and constructor parameter (defaulting to `App.Logger` for backward compat during transition).
- [ ] **Step 2:** Replace all `App.Logger` references in WebViewService partials with `_logger`.
- [ ] **Step 3:** Verify + commit: `refactor: inject logger into WebViewService`

### Task 4.4 — Remove App.Configuration Reads From WebViewService.Heartbeat

**Why (from findings #18):** Heartbeat reads `App.Configuration.Settings.Heartbeat` and `RecoveryPolicy.HardRefreshCooldownSeconds` directly mid-loop. Settings should be captured at `StartHeartbeat` time.

**Files:** Modify `Services/WebViewService.Heartbeat.cs`.

- [ ] **Step 1:** Capture `heartbeatSettings.EnableHeartbeat` check result and `HardRefreshCooldownSeconds` as fields set during `StartHeartbeat`.
- [ ] **Step 2:** Replace `App.Configuration.Settings.Heartbeat` read in `StartHeartbeat` with the parameter already available from the caller.
- [ ] **Step 3:** Replace `App.Configuration.Settings.RecoveryPolicy.HardRefreshCooldownSeconds` in `GetHeartbeatReloadCooldown()` with the captured field.
- [ ] **Step 4:** Verify + commit: `refactor: capture heartbeat config at start time`

### Task 4.5 — Localize Hardcoded English String in Circuit Breaker Error

**Why (from findings #21):** `ShowCircuitBreakerError()` uses a hardcoded English string instead of `StringResources`.

**Files:** Modify `ViewModels/MainViewModel.Commands.cs`, `Strings/en-US/Resources.resw`, `Strings/zh-CN/Resources.resw`.

- [ ] **Step 1:** Add `CircuitBreakerError` key to both `.resw` files.
- [ ] **Step 2:** Replace hardcoded string with `StringResources.CircuitBreakerError`.
- [ ] **Step 3:** Verify + commit: `fix: localize circuit breaker error message`

---

## Phase 5: MainViewModel Responsibility Reduction

### Task 5.1 — Extract StatusPresenter

**Why (from findings #17):** Status formatting and indicator logic is pure presentation (~360 LOC across 4 partials) that could live in a dedicated helper.

**Files:** Create `ViewModels/StatusPresenter.cs`. Modify `MainViewModel.StatusFormatting.cs`, `MainViewModel.Indicators.cs`.

- [ ] **Step 1:** Create `StatusPresenter` — static/instance helper that owns: `FormatModelSummary`, `FormatAccessSummary`, `FormatWorkStatus`, `FormatHeartbeatSummary`, `FormatRecoveryMessage`, `FormatLatencySummary`.
- [ ] **Step 2:** Move pure formatting methods (no field access beyond snapshot inputs) from `MainViewModel.StatusFormatting.cs`.
- [ ] **Step 3:** Keep `MainViewModel.Status.cs` as the event handler that calls `StatusPresenter` and sets bindable properties.
- [ ] **Step 4:** Verify + commit: `refactor: extract status presentation from MainViewModel`

### Task 5.2 — Replace Static Brush Fields With Resource Lookups

**Why (from findings #8):** Static `Brush` instances bypass the theme system. If the app switches themes at runtime, these brushes don't update.

**Files:** Modify `MainViewModel.Fields.cs`, `Styles/StatusResources.xaml`.

- [ ] **Step 1:** Add semantic brush resources to `StatusResources.xaml` with light/dark theme variants:
  ```xml
  <SolidColorBrush x:Key="StatusNeutralBrush" Color="#6B7280" />
  <SolidColorBrush x:Key="StatusSuccessBrush" Color="#22C55E" />
  <SolidColorBrush x:Key="StatusWarningBrush" Color="#F59E0B" />
  <SolidColorBrush x:Key="StatusErrorBrush" Color="#EF4444" />
  ```
- [ ] **Step 2:** Replace static `Brush` fields with resource lookups via `Application.Current.Resources["StatusNeutralBrush"]`.
- [ ] **Step 3:** Verify theme switching still works. Commit: `refactor: use theme-aware status brushes`

### Task 5.3 — Consolidate MainViewModel Field Ownership

**Why:** `MainViewModel.Fields.cs` (64 LOC) and `MainViewModel.Shared.cs` (48 LOC) are catch-all partials.

**Files:** Modify `MainViewModel.Fields.cs`, `MainViewModel.Shared.cs`, other partials.

- [ ] **Step 1:** Move each field to the partial that reads/writes it most.
- [ ] **Step 2:** If `Shared.cs` becomes empty, delete it.
- [ ] **Step 3:** Verify + commit: `refactor: consolidate MainViewModel field ownership`

### Task 5.4 — Narrow Public Service Property Exposure

**Why (from findings #20):** `MainViewModel.Core.Properties.cs` exposes `WebViewService`, `HostedUiBridge`, and `Coordinator` as public properties, letting the view layer bypass ViewModel orchestration.

**Files:** Modify `ViewModels/MainViewModel.Core.Properties.cs`, `MainWindow.WebView.cs` (if it accesses these).

- [ ] **Step 1:** Audit all external consumers of `ViewModel.WebViewService`, `ViewModel.HostedUiBridge`, `ViewModel.Coordinator`.
- [ ] **Step 2:** For each consumer, determine if the access can be replaced with a ViewModel method/command/event.
- [ ] **Step 3:** Change properties to `internal` where possible. If a property is only used by `MainWindow` partials (same assembly), `internal` is sufficient.
- [ ] **Step 4:** Verify + commit: `refactor: narrow MainViewModel service property visibility`

---

## Phase 6: Documentation Alignment

### Task 6.1 — Update README Architecture Diagram

**Why (from findings #14):** Diagram is stale.

- [ ] **Step 1:** Replace with target-state diagram from this plan.
- [ ] **Step 2:** Add "Active Verification" section.
- [ ] **Step 3:** Verify + commit: `docs: align README architecture with refactored runtime`

### Task 6.2 — Reconcile Development Notes

**Why (from findings #15):** "Regression coverage now checks" is misleading post-harness-removal.

- [ ] **Step 1:** Add "Active Verification After Test Harness Removal" section near top.
- [ ] **Step 2:** Add unreleased changelog entry.
- [ ] **Step 3:** Verify + commit: `docs: reconcile development notes with active verification`

---

## Phase 7: Final Integration

### Task 7.1 — Full Machine Verification + VS2026 Debug Checklist

- [ ] **Step 1:** Run full verification suite.
- [ ] **Step 2:** Clean Debug output directories.
- [ ] **Step 3:** VS2026 manual debug checklist:
  ```text
  1. Start app in VS2026 Debug.
  2. Confirm selected environment loads.
  3. Submit a hosted task and observe output stream.
  4. Wait for completion without manual reload.
  5. Confirm MODEL field is non-empty after startup, session switch, page reload.
  6. Open Settings/Cron/heavy pages — watch WebView2 CPU.
  7. Change global hotkey in Settings — works without restart.
  8. Toggle Always on Top — window state changes immediately.
  9. Enter/exit compact mode at 480px — top bar does not clip.
  10. Open log viewer, refresh repeatedly, close during loading — no UI hang.
  11. Use Reload and Stop commands.
  12. Confirm tray show/hide and close-to-tray.
  13. Switch theme (System → Dark → Light) — status brushes update.
  14. Leave app running 30+ min with busy session — no polling drift.
  ```
- [ ] **Step 4:** Final commit: `refactor: complete deep architecture hardening`

---

## Completion Criteria

- `tests/` remains absent.
- `OpenClaw.Core` has zero WinUI/WebView2 references (including reflection).
- `WebViewService` no longer directly owns inspection internals or heartbeat loop lifetime.
- `WebViewService` receives `IAppLogger` via constructor — no `App.Logger` static access.
- `WebViewService.Heartbeat` does not read `App.Configuration` — settings captured at start time.
- `HeartbeatRuntime` is a reusable, testable loop-lifetime primitive.
- `WebViewStatusInspector` is a self-contained inspection service with generation safety.
- WebView command scripts are embedded assets, not inline strings.
- WebView recreation scheduling lives in a service, not a Window partial.
- `HostedUiBridge.Script.js` is a composition shell under 250 lines with 4 focused modules.
- Bridge polling timer has drift correction.
- Compact mode uses XAML visual states.
- Settings live-apply uses a typed change pipeline.
- Log viewer loading is cancellable.
- `MainViewModel` does not use static `App.MainWindow` for dispatching.
- Status brushes are theme-aware resources.
- `MainViewModel` status formatting is extracted to a presenter.
- `MainViewModel` service properties are `internal`, not `public`.
- Coordinator adapter does not read `App.Configuration` directly.
- No hardcoded English user-facing strings outside `StringResources`.
- Documentation matches the active codebase.
- Full verification passes and VS2026 manual debug checklist has no blocking regression.

---

## Dependency Graph

```text
Phase 0 (verification scripts) ─── always first
│
├─► Phase 1 (WebView decomposition)
│    ├─ Task 1.1 (StatusInspector) → Task 1.2 (HeartbeatRuntime) [sequential]
│    ├─ Task 1.3 (Command assets) [independent]
│    └─ Task 1.4 (Recreation service) [independent]
│
├─► Phase 2 (UI hardening) ─── independent of Phase 1
│    ├─ Task 2.1 (compact visual states)
│    ├─ Task 2.2 (settings pipeline)
│    ├─ Task 2.3 (log viewer cancellation)
│    └─ Task 2.4 (dispatcher injection)
│
├─► Phase 3 (bridge split) ─── independent of Phase 1/2
│    ├─ Task 3.1 (JS decomposition)
│    └─ Task 3.2 (polling drift fix) [after 3.1]
│
├─► Phase 4 (Core boundary + DI) ─── independent
│    ├─ Task 4.1 (diagnostics reflection)
│    ├─ Task 4.2 (adapter config injection)
│    ├─ Task 4.3 (logger injection into WebViewService) [after 1.1, 1.2]
│    ├─ Task 4.4 (heartbeat config capture) [after 1.2]
│    └─ Task 4.5 (localize circuit breaker string) [independent]
│
├─► Phase 5 (ViewModel reduction) ─── after Phase 1 + 2.4 + 4.3
│    ├─ Task 5.1 (StatusPresenter)
│    ├─ Task 5.2 (theme-aware brushes)
│    ├─ Task 5.3 (field consolidation) [after 5.1 + 5.2]
│    └─ Task 5.4 (narrow service property exposure) [after 5.3]
│
├─► Phase 6 (docs) ─── after all code phases
│
└─► Phase 7 (integration) ─── always last
```

Phases 1–4 can be worked in parallel by independent agents (with noted sequential dependencies within phases). Phase 5 depends on Phase 1 (StatusInspector changes ViewModel's service surface), Task 2.4 (dispatcher injection), and Task 4.3 (logger injection changes WebViewService constructor). Phase 6 depends on all code phases. Phase 7 is always last.

---

## LOC Budget (target after refactor)

| File | Current | Target | Notes |
|------|---------|--------|-------|
| WebViewService.cs (main) | 508 | ~350 | After removing generation/inspection delegation |
| WebViewService.ControlUiInspection.cs | 413 | ~40 | Thin shim only |
| WebViewService.Heartbeat.cs | 365 | ~250 | Probe logic stays, lifetime moves out |
| WebViewService.Commands.cs | 238 | ~60 | Scripts become embedded assets |
| HostedUiBridge.Script.js | 921 | <250 | Composition shell only |
| MainWindow.WebView.cs | 218 | ~80 | Timer + control swap only |
| MainWindow.CompactMode.cs | 146 | ~60 | Visual state call only |
| MainViewModel (total) | 1727 | ~1400 | After presenter extraction |


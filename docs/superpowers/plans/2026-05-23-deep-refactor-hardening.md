# Deep Refactor Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the v3.3.x architecture cleanup real instead of patch-stacked: stable WebView/bridge ownership, live settings application, predictable compact layout, explicit verification without the removed `tests/` harness, and documentation that matches the active codebase and development notes.

**Architecture:** Keep `src/OpenClaw.Core` as the pure .NET shared source tree and keep the active solution free of `tests/` for now. Move volatile behavior behind small services and contracts inside the existing app/Core split: WinUI owns windows/WebView2/tray/hotkey, Core owns policy/parsing/state decisions, and script behavior is verified by lightweight repo scripts instead of an active C# test project. This file is the authoritative combined plan; `2026-05-23-deep-refactor-hardening_opus.md` remains a review input.

**Tech Stack:** WinUI 3, C#/.NET 10, WebView2, embedded JavaScript assets, PowerShell verification scripts, `dotnet restore/build/format`, manual VS2026 debug checklist.

---

## Review Findings Driving This Plan

### Review Scope

This pass reviewed:

- `README.md` and `readme_zh.md`: current 3.0.1 notes that carry forward the v3.3.6 architecture-cleanup baseline, architecture diagram, feature table, Cloudflare Tunnel/VPS guidance, development workflow.
- `changelog.md`: v3.0.0 through v3.3.6 English and Chinese entries, especially WebView, recovery, MODEL, compact mode, hotkey, diagnostics, and test harness changes.
- `DEVELOPMENT_NOTES.md`: root-cause notes for title bar/DWM sync, tray Win32 integration, window bounds, always-on-top, hosted MODEL/status bridge, WebView CPU spikes, stale chat stream recovery, and active verification.
- `docs/code-style.md`: current architecture boundaries, partial ownership, Core rules, large-file rules, and no-active-harness test policy.
- `src/OpenClaw` and `src/OpenClaw.Core`: current ownership of WebView2 lifecycle, hosted bridge assets, settings save/apply flow, compact layout, diagnostics, logging, tray/hotkey, window bounds, and Core boundary.

1. `README.md` still describes a very small architecture (`MainWindow -> MainViewModel -> WebViewService`), but the real runtime now includes `HostedUiBridge`, `ShellSessionCoordinator`, heartbeat/recovery, app/Core adapters, and embedded script assets. The diagram is no longer useful for maintenance.
2. `DEVELOPMENT_NOTES.md` records real root causes for MODEL blank output, stale busy streams, WebView CPU spikes, always-on-top, compact mode, and settings live application, but those lessons are not consistently enforced by code boundaries or verification.
3. `WebViewService` was split into partial files, but it still owns lifecycle, navigation, status parsing, status cache/generation, heartbeat, retry, profile cleanup, command injection, and event handling. Partial files improved readability but did not fully separate responsibilities.
4. `HostedUiBridge.Script.js` is still a 900+ line browser asset. `HostedUiBridge.ModelResolver.js` was extracted, but session-ready, command dispatch, mutation filtering, status inspection, stale busy detection, and host-message safety are still mixed in one script.
5. `SettingsViewModel` still reads and writes `App.Configuration` directly, while `MainWindow` applies live shell settings by reacting to `SettingsSaveResult`. This fixes the current hotkey/always-on-top symptom, but persistence and runtime application remain split across UI classes.
6. `MainWindow.CompactMode.cs` manually mutates XAML element properties (`TopStatusPill.MinWidth`, margins, visibility) instead of driving a compact visual state. It works as a patch, but the XAML and code-behind now duplicate layout constants.
7. `LogViewerDialog` loads log tails off the UI thread, but cancellation and concurrent refresh ownership are not explicit. Fast repeated refresh/open/close can still race UI updates.
8. `OpenClaw.Core` is mostly WinUI-free. The only suspicious boundary is `DiagnosticBundleService` using reflection with a `Microsoft.Web.WebView2.Core` type name. It is not a direct reference, but it is platform knowledge inside Core.
9. Active regression tests were removed from the solution and repository. Current verification is restore/build/format/diff-check plus manual VS2026 debug. That is acceptable for the current decision, but deep bridge/MODEL refactors need a replacement verification path that does not resurrect `tests/`.
10. Historical plan/progress docs still mention the removed `tests/OpenClaw.Tests` harness. They can remain as history, but current README/changelog/development notes must clearly distinguish historical coverage from active verification.
11. `WebViewService.Commands.cs` still embeds large stop/abort JavaScript strings. These are browser assets and should be embedded resources like the hosted bridge assets, not long C# literals.
12. `MainWindow.WebView.cs` still owns WebView recreation scheduling, circuit-breaker coordination, and instrumentation. The Window partial should keep XAML/control swap work, while scheduling policy belongs in a service.
13. At the start of this plan, `MainViewModel.Status.cs` still reached `App.MainWindow?.DispatcherQueue` directly. That coupled presentation updates to the global Window singleton and could silently drop updates during startup, shutdown, or future window recreation.
14. `ShellSessionCoordinator.Adapters.cs` still reads `App.Configuration.Settings.RecoveryPolicy` and `App.Configuration.Settings.Heartbeat` from inside the adapter extension. Coordinator configuration should be passed explicitly by the caller.
15. `WebViewService` and its partials still use `App.Logger` directly in many places. Logger ownership should be constructor-injected so service code is easier to reason about and does not spread static App dependencies.
16. `WebViewService.Heartbeat.cs` still reads heartbeat/recovery configuration from `App.Configuration` during runtime. A heartbeat run should capture its effective settings at start time.
17. `HostedUiBridge.Script.js` uses recursive `setTimeout` polling without drift correction. Sustained busy sessions can gradually skew poll timing.
18. `MainViewModel` still owns presentation formatting, indicator state, static brushes, and service exposure across many partials. This is workable but continues the patch-stacked pattern that made MODEL/status regressions hard to review.
19. Status brushes are static `Brush` instances in the ViewModel rather than theme-aware resources. Runtime theme switching can bypass those static instances.
20. `MainViewModel.Core.Properties.cs` exposes service instances publicly (`WebViewService`, `HostedUiBridge`, `Coordinator`), which makes it easy for the view layer to bypass orchestration.
21. `ShowCircuitBreakerError()` still has a hardcoded English user-facing string. User-facing strings belong in `StringResources` and `.resw`.
22. `HostedUiBridge.cs` still logs through `App.Logger`. After script extraction, the bridge should follow the same constructor-injected logger pattern as `WebViewService` so service code does not keep spreading static `App` dependencies.

The list above is the baseline review that originally generated Tasks 1-7D. The 2026-05-24 snapshot below supersedes any "still" wording for tasks that have since been completed or are present in the current dirty batch.

### Current Branch Snapshot

This plan was rechecked on 2026-05-24 against the active refactor branch:

```text
branch: codex/deep-refactor-hardening
baseline checkpoint: 443e9a5 chore: establish deep refactor baseline
latest committed checkpoint: 696eb8f refactor: extract status presenter
committed tasks: 1, 2, 3, 3A, 3B, 3C, 4, 5, 6, 6A, 7, 7A, 7B, 7C1
current dirty batch: 7C2, 7C3, 7D, 8, 9, 11, 12, 13, 14, 15, 15A, plus final review follow-up fixes; local verification is rerun after each follow-up, and the VS2026/Gateway manual checklist remains open
current release metadata target: 3.0.1, per the latest finalization request; v3.3.6 remains the architecture cleanup baseline reviewed by this plan
```

The repository already has the approved `tests/` removal and `src/OpenClaw.Core` retention committed. The current working tree contains the second-pass dirty batch and should be kept, verified, and committed as one final checkpoint after the remaining open tasks:

```text
DEVELOPMENT_NOTES.md
README.md
changelog.md
docs/code-style.md
readme_zh.md
src/OpenClaw.Core/Helpers/LogFileUtilities.cs
src/OpenClaw.Core/Services/ConfigurationService.cs
src/OpenClaw.Core/Services/ShellSessionCoordinator.Attach.cs
src/OpenClaw.Core/Services/ShellSessionCoordinator.Dependencies.cs
src/OpenClaw.Core/Services/ShellSessionCoordinator.Events.cs
src/OpenClaw.Core/Services/ShellSessionCoordinator.Recovery.cs
src/OpenClaw.Core/Services/ShellSessionCoordinator.RecoveryLifecycle.cs
src/OpenClaw.Core/Services/ShellSessionCoordinator.StateEffects.cs
src/OpenClaw.Core/Services/ShellSessionCoordinator.cs
src/OpenClaw/Abstractions/AppRuntimeContext.cs
src/OpenClaw/App.xaml.cs
src/OpenClaw/Helpers/StringResources.cs
src/OpenClaw/MainWindow.Commands.cs
src/OpenClaw/MainWindow.CompactMode.cs
src/OpenClaw/MainWindow.Lifecycle.cs
src/OpenClaw/MainWindow.Shared.cs
src/OpenClaw/MainWindow.WebView.cs
src/OpenClaw/MainWindow.xaml.cs
src/OpenClaw/OpenClaw.csproj
src/OpenClaw/Package.appxmanifest
src/OpenClaw/app.manifest
src/OpenClaw/Services/GatewayHeartbeatTransport.cs
src/OpenClaw/Services/HostedUiBridge.ActivityState.js
src/OpenClaw/Services/HostedUiBridge.CommandDispatch.js
src/OpenClaw/Services/HostedUiBridge.DomUtilities.js
src/OpenClaw/Services/HostedUiBridge.HostMessaging.js
src/OpenClaw/Services/HostedUiBridge.ModelDomFallback.js
src/OpenClaw/Services/HostedUiBridge.MutationFilter.js
src/OpenClaw/Services/HostedUiBridge.PhaseClassifier.js
src/OpenClaw/Services/HostedUiBridge.Script.cs
src/OpenClaw/Services/HostedUiBridge.Script.js
src/OpenClaw/Services/HostedUiBridge.StatusInspection.js
src/OpenClaw/Services/HostedSessionHeartbeatPolicy.cs
src/OpenClaw/Services/HostedUiBridge.cs
src/OpenClaw/Services/SettingsPersistenceAdapter.cs
src/OpenClaw/Services/ShellSessionCoordinator.Adapters.cs
src/OpenClaw/Services/UiTaskDispatcher.cs
src/OpenClaw/Services/WebViewService.ControlUiInspection.cs
src/OpenClaw/Services/WebViewService.cs
src/OpenClaw/Services/WebViewService.Heartbeat.cs
src/OpenClaw/Services/WebViewMessageOwnership.cs
src/OpenClaw/Services/WebViewStatusInspector.cs
src/OpenClaw/Services/WebViewStatusInspectionScripts.cs
src/OpenClaw/Services/WebViewStatusInspector.Inspect.js
src/OpenClaw/Strings/en-us/Resources.resw
src/OpenClaw/Strings/zh-cn/Resources.resw
src/OpenClaw/ViewModels/MainViewModel.Commands.cs
src/OpenClaw/ViewModels/MainViewModel.Core.Properties.cs
src/OpenClaw/ViewModels/MainViewModel.Environment.cs
src/OpenClaw/ViewModels/MainViewModel.Fields.cs
src/OpenClaw/ViewModels/MainViewModel.Heartbeat.cs
src/OpenClaw/ViewModels/MainViewModel.Indicators.cs
src/OpenClaw/ViewModels/MainViewModel.Lifecycle.cs
src/OpenClaw/ViewModels/MainViewModel.Status.cs
src/OpenClaw/ViewModels/MainViewModel.cs
src/OpenClaw/ViewModels/SettingsViewModel.cs
src/OpenClaw/Views/LogViewerDialog.xaml.cs
src/OpenClaw/Views/SettingsDialog.Actions.cs
src/OpenClaw/Views/SettingsDialog.Shared.cs
src/OpenClaw/Views/SettingsDialog.Theme.cs
src/OpenClaw/Views/SettingsDialog.xaml
src/OpenClaw/Views/SettingsDialog.xaml.cs
tools/verify-bridge-scripts.ps1
tools/verify-repo-structure.ps1
docs/superpowers/plans/2026-05-23-deep-refactor-hardening.md
```

Current review measurements, excluding generated `bin/` and `obj/` files:

| Surface | Current shape | Review result |
| --- | --- | --- |
| `WebViewService.cs` | 779 lines | Better bounded than the original service because status inspection, heartbeat transport/policy, bridge ownership, and command scripts are split out, but it remains the largest navigation/lifecycle/event shell and should not absorb new behavior. |
| `WebViewStatusInspector.cs` + `.Inspect.js` | 493 C# lines plus a 13-line embedded inspect script | Generation-scoped inspection is centralized, inline browser script ownership is fixed, caller cancellation no longer contaminates a shared in-flight probe, stalled script execution is bounded, navigation-after-load inspection dispatches through the UI dispatcher, and stop paths cancel the active probe before clearing ownership while leaving disposal to the running probe. |
| `WebViewService.Heartbeat.cs` | 275 lines plus `GatewayHeartbeatTransport`, `HostedSessionHeartbeatPolicy`, and `HeartbeatRuntime` | Loop lifetime, transport probing, and hosted-session phase mapping are now separate; hosted-session inspection now carries the heartbeat cancellation token, enters WebView2 inspection through the injected UI dispatcher, schedules the heartbeat loop off the caller thread, and publishes an immediate first observation before waiting for the periodic interval. |
| `HostedUiBridge.Script.js` | 236 lines | Composition shell stays under the 250-line guardrail, and native code can request a connected-shell `session-ready` replay after page-token acceptance. |
| `HostedUiBridge.StatusInspection.js` | 90 lines | Now a composition asset; MODEL DOM fallback, activity/stale-busy, DOM utilities, and phase classification live in focused assets. |
| `MainViewModel*.cs` | about 999 lines across partials | Service properties are internal, status presentation is extracted, and `App.Logger`/`App.Configuration` are replaced by `AppRuntimeContext`; UI dispatch must be injected by the owning window, with no `App.MainWindow` fallback. |
| `SettingsViewModel.cs` | 462 lines | Config persistence and save logging live behind `SettingsPersistenceAdapter`; save diffing now uses current persisted live settings instead of constructor-captured values. |
| `LogViewerDialog.xaml.cs` | 118 lines | Tail loading is cancellable and close/refresh ownership is explicit; stale load completions and failures return before writing UI state. |
| `ShellSessionCoordinator.Adapters.cs` + `UiTaskDispatcher.cs` | 98 adapter lines plus a focused dispatcher helper | App-layer adapters marshal WebView2 and hosted bridge calls back to the UI dispatcher for background heartbeat/recovery triggers, failed dispatch completes the adapter task with an error instead of executing on a background thread, recovery reloads carry the active recovery cancellation token while queued, and recovery bridge commands carry the same token while queued and while waiting on hosted script execution. |
| `WebViewMessageOwnership.cs` | focused owner/page-token validator | Hosted bridge status/session/gap messages carry native owner and page tokens; C# rejects mismatched sender or token before applying current state. |
| README architecture diagram | updated to show `WebViewRecreationService`, `LiveShellSettingsApplier`, and `SettingsPersistenceAdapter` as MainWindow/app-edge ownership | Must stay aligned with the final Task 12/16 outcome before commit. |
| Generated output | Debug folders have been cleaned; Release folders still exist | Preserve Release folders and avoid regenerating Debug outputs before final commit unless a new build is required. |

## 2026-05-24 Review Result

### Done Or Nearly Done

1. The no-`tests/` verification replacement exists: `tools/verify-repo-structure.ps1` and `tools/verify-bridge-scripts.ps1`.
2. WebView inspection generation ownership is now centralized in `WebViewStatusInspector`.
3. Heartbeat task/cancellation ownership moved into `HeartbeatRuntime`.
4. WebView stop/abort command scripts moved into embedded JS assets.
5. WebView recreation scheduling moved out of `MainWindow.WebView.cs` into `WebViewRecreationService`.
6. Runtime settings for hotkey and always-on-top now flow through typed `LiveShellSettingsChange` and `LiveShellSettingsApplier`.
7. Compact mode top-bar layout uses `RootLayout` visual states instead of code-behind property patching.
8. Hosted bridge assets were split into host messaging, mutation filtering, MODEL resolution, status inspection, command dispatch, and composition shell.
9. Bridge polling now has drift-aware scheduling.
10. WebView2 runtime lookup moved out of Core diagnostics.
11. ShellSessionCoordinator adapter configuration is explicit at the call site.
12. MainViewModel status dispatch uses an injected UI dispatcher.
13. Status formatting and theme-aware brushes are mostly moved into `StatusPresenter` and resources.
14. The current dirty batch narrows MainViewModel service properties, localizes circuit-breaker error text, and injects `IAppLogger` into `HostedUiBridge`.
15. Log Viewer tail loading is cancellable and close/refresh races are handled by one dialog-owned cancellation source.
16. README, Chinese README, changelog, development notes, and code-style docs now describe the active no-`tests/` verification surface and current runtime graph.
17. `WebViewStatusInspector` now loads its browser inspection code from an embedded `.js` asset.
18. Heartbeat HTTP probing and hosted-session phase mapping are split into `GatewayHeartbeatTransport` and `HostedSessionHeartbeatPolicy`.
19. `SettingsViewModel` no longer reads or writes `App.Configuration` directly; configuration persistence is isolated behind `SettingsPersistenceAdapter`.
20. `MainViewModel` uses `AppRuntimeContext` for logger/configuration access.
21. `HostedUiBridge.StatusInspection.js` is split into focused DOM, MODEL DOM fallback, activity/stale-busy, and phase-classification assets, with Node verifier coverage for each new asset.
22. `SettingsViewModel` no longer uses `App.Logger`; save logging is behind `SettingsPersistenceAdapter`.
23. Prewarmed `SettingsDialog` instances reload a fresh `SettingsViewModel` from current persisted settings before activation, preventing stale hotkey/always-on-top values from overwriting newer live settings.
24. Compact-mode shutdown now saves compact position separately and skips normal window bounds persistence, preventing `480x120` compact dimensions from becoming the full-mode restore size.
25. Hosted bridge scripts no longer cache localized string payloads statically; each WebView initialization composes the bridge script from cached assets plus current `StringResources`.
26. Coalesced WebView status inspections now honor caller cancellation, and heartbeat hosted-session probes pass the heartbeat token through to inspection.
27. Settings diagnostic bundle action text is localized, and guardrails cover the settings persistence boundary, compact bounds save, localized bridge-script caching, and Settings hardcoded text regression.
28. WebView status script execution is bounded, so a stalled `ExecuteScriptAsync` call cannot keep the shared in-flight inspection task alive indefinitely.
29. ShellSessionCoordinator app adapters now marshal WebView2 and hosted bridge calls through the UI dispatcher, covering heartbeat-triggered recovery work that starts off the UI thread.
30. Log Viewer stale load failures no longer write over newer refreshes or closing dialogs.
31. Current app/package/documentation version metadata targets `3.0.1` per the latest finalization request, while the older changelog `v3.0.1 (2026-04-21)` and `v3.0.0 (2026-04-21)` entries remain historical.
32. UI dispatcher enqueue failure no longer falls back to executing WebView2 or WinUI work inline on a background thread; repository guardrails cover this contract.
33. Hosted bridge messages now carry native owner/page tokens, and both WebViewService and HostedUiBridge reject stale sender/token combinations before writing state.
34. WebView recreation, WebViewService initialization, and HostedUiBridge initialization honor window/ViewModel lifetime cancellation before subscribing events or setting initialized state.
35. ShellSessionCoordinator recovery event handlers and stale-busy recovery tasks are observed through a SafeFireAndForget helper instead of async-void/unobserved task paths.
36. Native page-token acceptance now requests a connected-shell `session-ready` replay so early ready messages rejected by ownership filtering are not lost.
37. Status probe stop paths cancel the active CTS before clearing ownership and still leave disposal to the running probe, closing the remaining cancel/dispose race.
38. HostedUiBridge tracks and removes its document-created script id during detach, preventing repeated initialization from accumulating old bridge observers and poll timers.
39. Hosted bridge command dispatch and WebView stop/abort command scripts now have bounded timeouts, preventing stalled page promises from blocking native recovery or user stop handling indefinitely.
40. Heartbeat loops publish one immediate first observation before waiting for the first periodic interval, reducing stale waiting-state time after foreground resume or session recovery.
41. HeartbeatRuntime schedules loops asynchronously so the immediate first observation cannot run inline on the caller/UI thread.
42. Heartbeat recovery requests now stop only the current heartbeat run before publishing the recovery event, so an old loop cannot stop or recover a newly started run.
43. `DiagnosticService` receives logger ownership from callers instead of reading `App.Logger`, and WebView2 runtime lookup failures use a stable structured log key.
44. ShellSessionCoordinator reconnect and soft-resync bridge commands now carry the active recovery operation cancellation token through the Core bridge contract, app-layer UI dispatcher adapter, and hosted bridge script execution path.
45. ShellSessionCoordinator reconnect and hard-refresh reloads now carry the active recovery operation cancellation token through the Core WebView contract and app-layer UI dispatcher adapter.
46. Final responsiveness follow-ups now keep long-running async commands from reentering, run diagnostic bundle log enumeration/zip work off the UI thread, and delete inactive WebView2 profile folders from a background thread.

### Still Required Before Final Commit

1. Run the VS2026 manual debug checklist against real WebView2/Gateway/Cloudflare Tunnel behavior, including reverse-proxy 4xx/5xx/auth/origin pages, `cf-ray` PoP parsing, DWM title-bar edges, and single-instance handoff.
2. Make one final commit only after the manual checklist has no blocking regression. Earlier tasks already have commits; do not create more per-task commits unless the user asks.

### Recommended Next Refactor Wave

These are not all needed to stabilize the current dirty batch, but they are the next places where patch stacking can reappear:

1. Consider a later WebView lifecycle/navigation extraction if `WebViewService.cs` grows again; keep the current branch focused on the bridge/status/settings hardening already scoped here.
2. If a C# harness is reintroduced later, prioritize executable coverage for settings save/apply, hosted MODEL resolution, stale-busy recovery, and WebView generation gating.

## Traceability Matrix

| Source commitment or lesson | Current code reality | Plan coverage |
| --- | --- | --- |
| Current 3.0.1 README/changelog carry forward the v3.3.6 bridge/WebView hardening and generation-scoped inspection baseline. | Generation-scoped inspection is implemented in `WebViewStatusInspector`, and its browser probe is now an embedded `.js` asset. | Tasks 2 and 11 are done; Task 16 protects the current fix. |
| Current 3.0.1 README/changelog carry forward the v3.3.6 local regression harness removal baseline. | `tests/` is absent and the solution has no `OpenClaw.Tests` reference. Active checks are PowerShell guardrails plus Node-backed bridge script checks. | Tasks 1 and 9 are done; Task 16 reruns the active verification surface. |
| README/changelog call out hosted MODEL app-state resolution. | `HostedUiBridge.ModelResolver.js` owns app-state resolution, and `HostedUiBridge.ModelDomFallback.js` owns DOM fallback selectors. | Tasks 6 and 12 are done. |
| Development notes say MODEL blank output comes from app-state/DOM timing, not XAML only. | MODEL app-state and DOM fallback paths are separate assets with bridge verifier coverage. | Task 12 is done without restoring `tests/`. |
| Development notes say stale busy output should soft-resync before hard refresh and should not be blocked by an empty focused editor. | Stale-busy detection lives in `HostedUiBridge.ActivityState.js`; recovery coordination remains in Core, heartbeat, and bridge command dispatch. | Tasks 3, 6, 6A, and 12 are done. |
| Development notes say WebView/CoreWebView2 async work must carry generation ownership after awaits. | `WebViewStatusInspector` checks generation before and after `ExecuteScriptAsync`, while the probe body lives in `WebViewStatusInspector.Inspect.js`. | Tasks 2 and 11 are done. |
| Heartbeat and recovery can start from background work but WebView2 remains UI-thread-owned. | `ShellSessionCoordinator` app adapters dispatch WebView2 and hosted bridge calls through `UiTaskDispatcher`; failed enqueue returns a failed task instead of running on a background thread. | Task 16 follow-up stabilization is done. |
| Gateway transport heartbeat should distinguish Cloudflare/reverse-proxy error pages from healthy transport. | `GatewayHeartbeatTransport` keeps 2xx, redirects, and auth-required responses as reachability signals, but treats missing Control UI 404, rejected heartbeat-probe 405, 5xx, and unexpected 4xx responses as failures. | Task 16 follow-up stabilization is done. |
| Old WebView documents must not write status/session state after navigation or recreation. | `WebViewMessageOwnership` validates native owner/page tokens and current sender; programmatic navigation/reload/retry now invalidates accepted page tokens and clears the accepted navigation id before CoreWebView2 starts navigating, re-checks generation after page-token capture awaits, treats stale auto-retry continuations as no-op, and logs failures through an observed navigation-completed async boundary. | Task 16 follow-up stabilization is done. |
| Reload/retry failures should not strand the shell in a transient state. | Reload, manual retry, and auto-retry now surface exhausted retries and CoreWebView2 command-start failures as error state/navigation errors instead of leaving Loading/Reconnecting visible; reload returns whether CoreWebView2 accepted the command, recovery treats a no-op reload as failed recovery, recovery-owned reloads carry the active operation cancellation token through the UI dispatcher, and auto-retry delay continuations return stale when another navigation has taken over. | Task 16 follow-up stabilization is done; the eighteenth final-review follow-up closes the queued recovery reload cancellation gap. |
| WebView recreation and shutdown must not resurrect disposed WebView2 instances. | MainWindow/ViewModel lifetime cancellation is checked after WebView2 and bridge initialization awaits; recreation now detaches the coordinator, bridge, and service before closing old controls, and recreation/foreground-resume async work is observed during shutdown. | Task 16 follow-up stabilization is done. |
| Recovery async event handlers should not hide unobserved failures. | ShellSessionCoordinator uses SafeFireAndForget for event-gap, heartbeat, and stale-busy recovery tasks, with cancellation/disposal/error logging; abort/replacement paths cancel recovery CTS and leave disposal to the running operation. | Task 16 follow-up stabilization is done. |
| A slow or stuck status script should not block every coalesced status probe. | `WebViewStatusInspector` wraps status `ExecuteScriptAsync` with `InspectionTimeout`, returns an unavailable snapshot on timeout, owns the status probe task/cancellation source so stop paths cancel without disposing a token still in use, and dispatches the probe loop's WebView2 inspection work through the UI dispatcher. | Task 16 follow-up stabilization is done. |
| Hosted page command promises should not block native recovery or stop handling indefinitely. | `HostedUiBridge.SendCommandAsync` and WebView stop/abort command scripts now wrap `ExecuteScriptAsync` with bounded timeouts, recovery-owned bridge commands also link the active recovery cancellation token through UI dispatch and script execution, and repository guardrails require those timeout/cancellation boundaries to stay in place. | Task 16 follow-up stabilization is done; the seventeenth final-review follow-up closes the remaining bridge-command cancellation gap. |
| Early hosted `session-ready` messages can race native page-token capture. | After page-token acceptance, WebViewService calls the bridge's `reportSessionReady()` path, and the JS only emits when the shell is connected, letting native recover from an ownership-filtered early message without accepting stale documents. | Task 16 follow-up stabilization is done. |
| Changelog v3.3.4 says heartbeat loop ownership is explicit. | `HeartbeatRuntime` owns CTS/task lifetime; heartbeat transport probing and hosted-session phase mapping are split out. | Tasks 3 and 13 are done. |
| Changelog v3.3.4 says settings hotkey/always-on-top apply immediately. | Live shell settings use `LiveShellSettingsChange`; persisted configuration writes and save logging go through `SettingsPersistenceAdapter`. | Tasks 4, 14, and 15A are done. |
| WinUI async UI actions should not throw from event handlers or allow rapid duplicate operations. | Log/About ContentDialog entry points guard reentry and log failures; Settings session reset disables the clicked button while running and reports localized failures. | Task 16 follow-up stabilization is done. |
| Background/coalesced work must have explicit lifetime ownership before shutdown. | `ConfigurationService` stores the deferred-save worker task and cancellation source, serializes coalesced save versions under the worker lifetime gate, and `FlushDeferredSave()` cancels/drains/observes the worker before doing the final synchronous settings save. | Task 16 follow-up stabilization is done. |
| Changelog v3.3.4 says compact top bar was tightened for 480px. | Compact mode uses `RootLayout` visual states and guardrails reject code-behind layout patching. | Task 5 done; Task 16 manual checklist keeps the 480px path in final verification. |
| Development notes say log viewer must not block or race the UI thread. | Tail read runs off the UI thread, accepts cancellation, and close/refresh ownership lives in `LogViewerDialog`. | Task 8 is done. |
| Development notes define Core as WinUI/WebView2-free. | Core guardrail rejects WinUI/WebView2 direct/reflection references; WebView2 runtime lookup moved to the WinUI layer. | Task 7 done; Task 16 reruns guardrails. |
| Development notes cover title-bar/DWM, tray Win32, single-instance, and window bounds lessons. | The current refactor did not intentionally change those platform paths. | Task 16 manual checklist keeps them in final verification. |
| Code-style notes say large browser scripts should live as assets and remain verifiable. | Command scripts, hosted bridge scripts, and WebView status inspection scripts are embedded assets. `StatusInspection.js` is composition-only. | Tasks 3B, 11, and 12 are done. |
| Development notes say WebView recovery should be deliberate and observable. | Recreation scheduling is in `WebViewRecreationService`; `MainWindow.WebView.cs` still records instrumentation and performs control swap. | Task 3C done; 7C2 dirty batch narrows `Coordinator` access through `UpdateShellInstrumentation`. |
| Code-style notes say static App coupling should not spread into services/ViewModels. | Service hotspots, `MainViewModel`, and `SettingsViewModel` logging/configuration paths are behind explicit app-edge facades/adapters. | Tasks 3A, 7A, 7B, 7D, 14, 15, and 15A are done. |
| README/changelog emphasize long-running hosted sessions and stale busy recovery. | Polling drift is fixed, and stale-busy/activity state is isolated in `HostedUiBridge.ActivityState.js`. | Tasks 6A and 12 are done. |
| Code-style notes call for centralized resources and localizable user strings. | Status brushes use resources; the circuit-breaker string is localized in the dirty batch. | Task 7C1 done; 7C3 dirty batch must be verified. |
| The goal of this refactor is to stop patch stacking. | Original patch layers are separated across explicit services/assets; remaining work is the VS2026 manual Gateway/Cloudflare checklist and one final commit. | Task 16 defines the remaining cleanup path. |

## Current Ground Rules

- Keep `src/OpenClaw.Core`.
- Do not restore the removed `tests/` directory in this refactor.
- Keep Release output folders unless a later explicit cleanup request says otherwise.
- Do not rewrite historical `docs/superpowers/plans/*` and `docs/superpowers/progress/*` records except to add a short archival note if needed.
- Every task still needs a verification command list, but after the 2026-05-24 review do not create more per-task commits. The user asked for one final commit after the remaining work is finished.
- Start implementation from the current `codex/deep-refactor-hardening` checkpoint. The baseline cleanup and Tasks 1 through 7C1 are already committed; keep the current dirty 7C2/7C3/7D batch and verify it before final commit.
- Do not bump the app version inside this plan unless the user explicitly asks for the implementation branch to become a release.
- New service types default to `internal sealed` unless a public contract is required.
- New async methods accept `CancellationToken` when there is a realistic owner that can cancel the work.
- Inline JavaScript longer than 30 lines must move to an embedded `.js` resource with a verifier or guardrail.
- Do not add new direct reads of `App.Configuration`, `App.Logger`, or `App.MainWindow` inside service/view-model internals.
- User-visible English/Chinese text must go through `StringResources` and `.resw`; diagnostic protocol tokens and log event names may stay literal.

---

## Target Runtime Shape

```text
OpenClaw Manager
|- MainWindow (WinUI shell: XAML, WebView2 control swap, tray/window integration)
|  |- WebViewRecreationService
|  |- LiveShellSettingsApplier
|  |- SettingsDialog / SettingsPersistenceAdapter
|  `- MainViewModel (orchestration and bindable state)
|     |- StatusPresenter
|     |- WebViewService
|     |  |- WebViewStatusInspector
|     |  |- HeartbeatRuntime
|     |  |- WebViewGenerationTracker
|     |  `- WebView command JS assets
|     |- HostedUiBridge
|     |  `- embedded bridge JS modules
|     |- ShellSessionCoordinator adapters
|     `- ControlUiLatencyService
`- OpenClaw.Core
   |- settings/configuration models
   |- recovery policy/state machine
   |- diagnostics/log utilities
   `- parser/protocol helpers
```

## Target File Budgets

These are review targets, not hard build failures unless the guardrail script explicitly enforces them.

| File | Current issue | Target |
| --- | --- | --- |
| `WebViewService.cs` | still about 511 lines of lifecycle, navigation, generation, and event wiring | around 350 lines after future lifecycle/navigation delegation |
| `WebViewStatusInspector.cs` | script asset is extracted; inspector still owns JSON parsing and snapshot application | keep generation/parser ownership explicit; do not move browser code back inline |
| `WebViewService.ControlUiInspection.cs` | thin wrapper after Task 2 | keep thin; guardrail should continue to reject inspection internals here |
| `WebViewService.Heartbeat.cs` | loop lifetime, HTTP transport, and hosted-session phase mapping are split; partial still owns heartbeat orchestration | keep orchestration focused and do not reintroduce CTS/task or transport ownership |
| `WebViewService.Commands.cs` | command scripts are now embedded assets | keep command orchestration plus asset loading only |
| `HostedUiBridge.Script.js` | composition shell under 250 lines | keep under guardrail |
| `HostedUiBridge.StatusInspection.js` | 90-line composition asset | keep composition-only; route future MODEL fallback, stale-busy, and phase-matching changes to focused assets |
| `MainWindow.WebView.cs` | control swap plus instrumentation | keep WebView2 control swap and timer wiring only |
| `MainWindow.CompactMode.cs` | visual-state transition only | keep state transition call only |
| `SettingsViewModel.cs` | validation and edit state remain; config persistence and save logging are behind an adapter | keep `App.Configuration` and `App.Logger` out of the ViewModel |
| `MainViewModel` partials | presentation and app-runtime context are improved; UI dispatch is injected by `MainWindow` and guardrails reject `App.MainWindow` fallback | keep presentation helpers and do not reintroduce direct `App.Logger`, `App.Configuration`, or `App.MainWindow` access |

### Task 0: Preflight And Checkpoint

**Files:**
- Inspect only unless committing the pending Task 1 verification scripts.

- [x] **Step 1: Confirm working tree state**

Run:

```powershell
git status --short --branch -uall
```

Expected current state before continuing implementation is the dedicated refactor branch with this amended plan plus Task 1 verification scripts uncommitted:

```text
## codex/deep-refactor-hardening
 M docs/superpowers/plans/2026-05-23-deep-refactor-hardening.md
?? tools/verify-bridge-scripts.ps1
?? tools/verify-repo-structure.ps1
```

- [x] **Step 2: Decide checkpoint handling**

The baseline cleanup is already committed as:

```text
443e9a5 chore: establish deep refactor baseline
```

Do not recommit the removed harness. Finish Task 1 by updating docs, running verification, and committing only the verification scripts plus related current documentation.

- [x] **Step 3: Confirm Release output policy**

Run:

```powershell
Get-ChildItem -Path src -Directory -Recurse -Force -Filter Release | Select-Object -ExpandProperty FullName
```

Expected: Release directories may exist and must not be deleted by this plan. Debug directories produced by verification can be deleted after each task.

---

### Task 1: Add A No-Tests Verification Surface

**Files:**
- Create: `tools/verify-bridge-scripts.ps1`
- Create: `tools/verify-repo-structure.ps1`
- Modify: `README.md`
- Modify: `readme_zh.md`
- Modify: `DEVELOPMENT_NOTES.md`
- Modify: `docs/code-style.md`

- [x] **Step 1: Create `tools/verify-bridge-scripts.ps1`**

Add a PowerShell script that executes the embedded MODEL resolver with Node.js. It must not create `tests/`, and after final hardening it must fail closed unless `OPENCLAW_ALLOW_NODE_SKIP=1` is set explicitly.

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$resolverPath = Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.ModelResolver.js'

function Resolve-NodeCommand {
    if (-not [string]::IsNullOrWhiteSpace($env:OPENCLAW_NODE)) {
        return $env:OPENCLAW_NODE
    }

    $command = Get-Command node -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

$nodeCommand = Resolve-NodeCommand
if (-not $nodeCommand) {
    if ($env:OPENCLAW_ALLOW_NODE_SKIP -eq '1') {
        Write-Host 'SKIP: node is not available and OPENCLAW_ALLOW_NODE_SKIP=1; bridge script verification skipped.'
        exit 0
    }

    throw 'Node.js is required for bridge script verification. Set OPENCLAW_NODE to a Node executable, or set OPENCLAW_ALLOW_NODE_SKIP=1 only for an explicit local skip.'
}

try {
    & $nodeCommand --version | Out-Null
} catch {
    if ($env:OPENCLAW_ALLOW_NODE_SKIP -eq '1') {
        Write-Host "SKIP: node is not executable and OPENCLAW_ALLOW_NODE_SKIP=1; bridge script verification skipped. $($_.Exception.Message)"
        exit 0
    }

    throw "Node.js is required for bridge script verification, but '$nodeCommand' is not executable. Set OPENCLAW_NODE to a working Node executable, or set OPENCLAW_ALLOW_NODE_SKIP=1 only for an explicit local skip. $($_.Exception.Message)"
}

$resolver = Get-Content -LiteralPath $resolverPath -Raw
$cases = @'
[
  {
    "name": "default model",
    "states": [{"sessionsResult":{"defaults":{"model":"gpt-5.5","modelProvider":"openai"},"sessions":[]}}],
    "sessionKey": "s1",
    "expected": "openai/gpt-5.5"
  },
  {
    "name": "null override falls back after session lookup",
    "states": [{"sessionKey":"s1","chatModelOverrides":{"s1":null},"sessionsResult":{"defaults":{"model":"gpt-5.4","modelProvider":"openai"},"sessions":[{"key":"s1","model":"claude-sonnet-4.5","modelProvider":"anthropic"}]}}],
    "sessionKey": "s1",
    "expected": "anthropic/claude-sonnet-4.5"
  },
  {
    "name": "object override",
    "states": [{"chatModelOverrides":{"s1":{"model":{"id":"qwen3-coder"},"provider":{"id":"dashscope"}}},"sessionsResult":{"defaults":{"model":"gpt-5.4","modelProvider":"openai"},"sessions":[]}}],
    "sessionKey": "s1",
    "expected": "dashscope/qwen3-coder"
  }
]
'@

$runner = @"
$resolver
const cases = $cases;
let failed = 0;
for (const item of cases) {
  const result = resolveOpenClawAppStateModel(item.states, item.sessionKey);
  if (!result || result.value !== item.expected) {
    console.error(`FAIL: ${item.name}: expected ${item.expected}, got ${result && result.value}`);
    failed += 1;
  } else {
    console.log(`PASS: ${item.name}`);
  }
}
process.exit(failed === 0 ? 0 : 1);
"@

$tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ('openclaw-model-resolver-' + [System.Guid]::NewGuid() + '.js')
try {
    Set-Content -LiteralPath $tempFile -Value $runner -Encoding UTF8
    & $nodeCommand $tempFile
} finally {
    Remove-Item -LiteralPath $tempFile -ErrorAction SilentlyContinue
}
```

- [x] **Step 2: Create `tools/verify-repo-structure.ps1`**

Add guardrails for the current architecture decision: no active `tests/`, Core stays WinUI-free, embedded script resources exist, and historical docs are not treated as active verification.

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

$testsPath = Join-Path $repoRoot 'tests'
if (Test-Path -LiteralPath $testsPath) {
    throw 'Active tests/ directory exists, but current checkpoint intentionally keeps tests out of the solution.'
}

$solution = Get-Content -LiteralPath (Join-Path $repoRoot 'OpenClaw.sln') -Raw
if ($solution -match 'OpenClaw\.Tests|tests\\OpenClaw\.Tests') {
    throw 'OpenClaw.sln still references the removed test harness.'
}

$coreFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core') -Recurse -File -Include *.cs
$forbiddenCorePattern = 'using Microsoft\.UI|using Microsoft\.Web\.WebView2|using Windows\.Graphics|using Windows\.UI|using WinRT|App\.Configuration|App\.Logger|App\.MainWindow'
foreach ($file in $coreFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match $forbiddenCorePattern) {
        throw "Core boundary violation: $($file.FullName)"
    }
}

$project = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/OpenClaw.csproj') -Raw
foreach ($resource in @('HostedUiBridge.Script.js', 'HostedUiBridge.ModelResolver.js')) {
    if ($project -notmatch [regex]::Escape($resource)) {
        throw "Missing embedded bridge resource entry: $resource"
    }
}

Write-Host 'PASS: repository structure guardrails'
```

- [x] **Step 3: Document the new verification commands**

Update current docs to include:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
$env:OPENCLAW_NODE='C:\Users\Zen\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
powershell -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
git diff --check
```

In `DEVELOPMENT_NOTES.md`, replace current active wording that implies executable regression coverage with a clear statement:

```markdown
Historical notes may mention regression coverage from removed harness versions. The active post-harness-removal verification surface is solution restore/build/format, repo-structure guardrails, bridge MODEL script checks, whitespace checks, and VS2026 manual debug.
```

- [x] **Step 4: Run verification**

Run:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
$env:OPENCLAW_NODE='C:\Users\Zen\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
powershell -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
git diff --check
```

Expected:

```text
Build succeeded.
PASS: repository structure guardrails
PASS: default model
PASS: null override falls back after session lookup
PASS: object override
```

- [x] **Step 5: Commit**

```powershell
git add README.md readme_zh.md DEVELOPMENT_NOTES.md docs/code-style.md tools/verify-bridge-scripts.ps1 tools/verify-repo-structure.ps1
git commit -m "chore: add active refactor verification scripts"
```

---

### Task 2: Split WebView Runtime Ownership From Status Inspection

**Files:**
- Create: `src/OpenClaw/Services/WebViewGenerationTracker.cs`
- Create: `src/OpenClaw/Services/WebViewStatusInspector.cs`
- Modify: `src/OpenClaw/Services/WebViewService.cs`
- Modify: `src/OpenClaw/Services/WebViewService.ControlUiInspection.cs`
- Modify: `src/OpenClaw/Services/WebViewService.Heartbeat.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Fields.cs`
- Modify: `src/OpenClaw/MainWindow.Shared.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Create `WebViewGenerationTracker`**

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed class WebViewGenerationTracker
{
    private int _generation;

    public int Current => Volatile.Read(ref _generation);

    public int Next()
    {
        return Interlocked.Increment(ref _generation);
    }

    public bool IsCurrent(int generation)
    {
        return Current == generation;
    }
}
```

- [x] **Step 2: Create `WebViewStatusInspector`**

Move these fields and methods out of the `WebViewService` partial into the new class:

- `_inspectionGate`
- `_statusProbeCts`
- `_inFlightInspectionTask`
- `_inFlightInspectionGeneration`
- `_latestControlUiSnapshot`
- `_latestControlUiSnapshotGeneration`
- `_lastControlUiInspectionAt`
- inspection counters
- `InspectControlUiStateAsync`
- `StartStatusProbeLoop`
- `CancelStatusProbeLoop`
- `ApplyControlUiSnapshot`
- `ExecuteControlUiInspectionAsync`
- `ParseControlUiSnapshot`

Keep the public contract narrow:

```csharp
internal sealed class WebViewStatusInspector : IDisposable
{
    public WebViewStatusInspector(
        Func<CoreWebView2?> getCoreWebView,
        WebViewGenerationTracker generations,
        IAppLogger logger);

    public event Action<ControlUiProbeSnapshot>? SnapshotUpdated;

    public ControlUiProbeSnapshot LatestSnapshot { get; }
    public int TotalRequests { get; }
    public int CachedRequests { get; }
    public int CoalescedRequests { get; }

    public Task<ControlUiProbeSnapshot> InspectAsync(CancellationToken cancellationToken = default);
    public void StartProbeLoop();
    public void CancelProbeLoop();
    public void InvalidateCache();
    public void SetLoadingSnapshot(string? uri);
    public void SetPageLoadedSnapshot(string? uri);
    public void SetUnavailableSnapshot(string summary);
    public bool TryApplyHostMessage(string json);
    public void Dispose();
}
```

- [x] **Step 3: Add explicit `WebViewService` logger/generation wiring**

Add constructor-owned dependencies to `WebViewService`. Do not use a nullable logger fallback inside the service.

```csharp
private readonly IAppLogger _logger;
private readonly WebViewGenerationTracker _generations;
private readonly WebViewStatusInspector _statusInspector;

public WebViewService(IAppLogger logger)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _generations = new WebViewGenerationTracker();
    _statusInspector = new WebViewStatusInspector(GetCoreWebView, _generations, _logger);
    _statusInspector.SnapshotUpdated += snapshot => ControlUiSnapshotUpdated?.Invoke(snapshot);
}
```

Move `_webViewService` construction out of the field initializer:

```csharp
private readonly WebViewService _webViewService;
```

Update `MainViewModel` constructor:

```csharp
public MainViewModel(AppRuntimeContext runtime, Func<Action, bool> dispatchToUi)
{
    _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    _dispatchToUi = dispatchToUi ?? throw new ArgumentNullException(nameof(dispatchToUi));
    _webViewService = new WebViewService(runtime.Logger, _messageOwnership, _dispatchToUi);
    InitializeCommands();
    SubscribeToServiceEvents();
    InitializeCoordinator();
    LoadEnvironments();
    UpdateStatusPresentation();
}
```

Update `MainWindow.xaml.cs`:

```csharp
ViewModel = new MainViewModel(new AppRuntimeContext(App.Logger, App.Configuration), TryDispatchToUi);
```

This keeps the App-bound logger/configuration at the application edge while removing them from `WebViewService` and requiring explicit UI dispatcher injection.

- [x] **Step 4: Keep generation checks inside inspector**

Every await in `WebViewStatusInspector` that can apply a snapshot must check:

```csharp
cancellationToken.ThrowIfCancellationRequested();
if (!_generations.IsCurrent(generation))
{
    return ControlUiProbeSnapshot.Unknown;
}
```

No caller should be able to apply an inspection result without both a generation and the currently owned cancellation token. Fire-and-forget probe loops must pass their probe-loop token into `InspectAsync`.

- [x] **Step 5: Reduce `WebViewService.ControlUiInspection.cs` to a compatibility wrapper**

After the move, this partial should only expose the existing public members and delegate to `_statusInspector`:

```csharp
public ControlUiProbeSnapshot LatestControlUiSnapshot => _statusInspector.LatestSnapshot;
public int TotalControlUiInspectionRequests => _statusInspector.TotalRequests;
public int CachedControlUiInspectionRequests => _statusInspector.CachedRequests;
public int CoalescedControlUiInspectionRequests => _statusInspector.CoalescedRequests;

public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync(CancellationToken cancellationToken = default)
{
    return _statusInspector.InspectAsync(cancellationToken);
}
```

- [x] **Step 6: Update `WebViewService` lifecycle calls**

Replace direct field manipulation with explicit inspector calls:

```csharp
_statusInspector.CancelProbeLoop();
_statusInspector.InvalidateCache();
_generations.Next();
_statusInspector.SetLoadingSnapshot(args.Uri);
_statusInspector.SetPageLoadedSnapshot(sender.Source);
_statusInspector.StartProbeLoop();
```

- [x] **Step 7: Update heartbeat hosted-session probe**

In `WebViewService.Heartbeat.cs`, replace direct inspection call with the wrapper:

```csharp
var snapshot = await InspectControlUiStateAsync();
```

Keep this line only if it now delegates to `WebViewStatusInspector`. Do not let heartbeat own inspection state.

- [x] **Step 8: Add structure guardrails**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$webViewService = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.cs') -Raw
if ($webViewService -match 'ParseControlUiSnapshot|ExecuteControlUiInspectionAsync|_latestControlUiSnapshot') {
    throw 'WebViewService.cs must not own Control UI inspection internals.'
}
```

- [x] **Step 9: Run verification and commit**

Run the full command set from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/WebViewGenerationTracker.cs src/OpenClaw/Services/WebViewStatusInspector.cs src/OpenClaw/Services/WebViewService.cs src/OpenClaw/Services/WebViewService.ControlUiInspection.cs src/OpenClaw/Services/WebViewService.Heartbeat.cs src/OpenClaw/ViewModels/MainViewModel.cs src/OpenClaw/ViewModels/MainViewModel.Fields.cs src/OpenClaw/MainWindow.Shared.cs tools/verify-repo-structure.ps1
git commit -m "refactor: isolate WebView status inspection ownership"
```

---

### Task 3: Split Heartbeat Runtime Into An Owned Loop

**Files:**
- Create: `src/OpenClaw/Services/HeartbeatRuntime.cs`
- Modify: `src/OpenClaw/Services/WebViewService.Heartbeat.cs`
- Modify: `src/OpenClaw/Services/WebViewService.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Create `HeartbeatRuntime`**

Move timer/task/cancellation ownership out of `WebViewService.Heartbeat.cs`.

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed class HeartbeatRuntime : IDisposable
{
    private readonly IAppLogger _logger;
    private CancellationTokenSource? _cancellation;
    private Task? _task;
    private string? _key;

    public HeartbeatRuntime(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsRunning => _task is { IsCompleted: false };

    public bool IsSameRun(string key)
    {
        return IsRunning && string.Equals(_key, key, StringComparison.Ordinal);
    }

    public void Start(string key, Func<CancellationToken, Task> loop)
    {
        Stop();
        _key = key;
        _cancellation = new CancellationTokenSource();
        _task = RunObservedAsync(key, loop, _cancellation);
    }

    public void Stop()
    {
        var cancellation = _cancellation;
        var task = _task;
        _cancellation = null;
        _task = null;
        _key = null;

        cancellation?.Cancel();
        if (task is null)
        {
            cancellation?.Dispose();
            return;
        }

        _ = ObserveStopAsync(task, cancellation);
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task RunObservedAsync(
        string key,
        Func<CancellationToken, Task> loop,
        CancellationTokenSource cancellation)
    {
        try
        {
            await loop(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Expected during Stop().
        }
        catch (Exception ex)
        {
            _logger.Error($"Heartbeat loop error for run '{key}': {ex.Message}");
        }
    }

    private async Task ObserveStopAsync(Task task, CancellationTokenSource? cancellation)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during Stop().
        }
        catch (Exception ex)
        {
            _logger.Error($"Heartbeat loop shutdown error: {ex.Message}");
        }
        finally
        {
            cancellation?.Dispose();
        }
    }
}
```

- [x] **Step 2: Remove direct `_heartbeatCts` and `_heartbeatTask` fields**

In `WebViewService.Heartbeat.cs`, replace:

```csharp
private CancellationTokenSource? _heartbeatCts;
private Task? _heartbeatTask;
```

with:

```csharp
private readonly HeartbeatRuntime _heartbeatRuntime;
```

Initialize it in the `WebViewService(IAppLogger logger)` constructor created in Task 2:

```csharp
public WebViewService(IAppLogger logger)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _generations = new WebViewGenerationTracker();
    _statusInspector = new WebViewStatusInspector(GetCoreWebView, _generations, _logger);
    _heartbeatRuntime = new HeartbeatRuntime(_logger);
    _statusInspector.SnapshotUpdated += snapshot => ControlUiSnapshotUpdated?.Invoke(snapshot);
}
```

- [x] **Step 3: Change start/stop logic**

`StartHeartbeat` should compute a complete key including URL, interval, thresholds, and enabled state:

```csharp
var heartbeatKey = $"{gatewayUrl}|{intervalSeconds}|{_heartbeatFailureThreshold}|{_heartbeatConnectingThreshold}|enabled";
if (_heartbeatRuntime.IsSameRun(heartbeatKey))
{
    return;
}

_heartbeatRuntime.Start(heartbeatKey, token => RunSessionAwareHeartbeatLoopAsync(gatewayUrl, TimeSpan.FromSeconds(intervalSeconds), token));
```

Change `RunSessionAwareHeartbeatLoopAsync` signature:

```csharp
private async Task RunSessionAwareHeartbeatLoopAsync(string gatewayUrl, TimeSpan interval, CancellationToken token)
```

Create and dispose `PeriodicTimer` inside that method:

```csharp
using var timer = new PeriodicTimer(interval);
while (await timer.WaitForNextTickAsync(token))
{
    // Move the existing per-tick heartbeat body here unchanged:
    // ProbeGatewayHealthAsync, LogHeartbeatObservation, status-specific counter updates,
    // TryScheduleHeartbeatReload, and the existing OperationCanceledException handling.
}
```

- [x] **Step 4: Simplify `StopHeartbeat`**

```csharp
public void StopHeartbeat()
{
    _heartbeatRuntime.Stop();
    _heartbeatFailureCount = 0;
    _heartbeatConnectingCount = 0;
    _lastHeartbeatObservationKey = null;
    _heartbeatGatewayUrl = null;
    _heartbeatIntervalSeconds = 0;
    _heartbeatFailureThreshold = DefaultHeartbeatFailureThreshold;
    _heartbeatConnectingThreshold = DefaultHeartbeatConnectingThreshold;
    _lastStartHeartbeatKey = null;
}
```

- [x] **Step 5: Add structure guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$heartbeat = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Heartbeat.cs') -Raw
if ($heartbeat -match 'CancellationTokenSource\? _heartbeatCts|Task\? _heartbeatTask|ObserveHeartbeatShutdownAsync') {
    throw 'Heartbeat loop ownership must live in HeartbeatRuntime.'
}
```

- [x] **Step 6: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/HeartbeatRuntime.cs src/OpenClaw/Services/WebViewService.Heartbeat.cs src/OpenClaw/Services/WebViewService.cs tools/verify-repo-structure.ps1
git commit -m "refactor: isolate heartbeat loop ownership"
```

---

### Task 3A: Replace Static WebView Logging And Capture Heartbeat Configuration

**Files:**
- Modify: `src/OpenClaw/Services/WebViewService.cs`
- Modify: `src/OpenClaw/Services/WebViewService.ControlUiInspection.cs`
- Modify: `src/OpenClaw/Services/WebViewService.Commands.cs`
- Modify: `src/OpenClaw/Services/WebViewService.Heartbeat.cs`
- Modify: `src/OpenClaw/Services/WebViewService.Profile.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Replace static logger reads inside `WebViewService` partials**

In these files, replace every `App.Logger.Info`, `App.Logger.Warning`, and `App.Logger.Error` call with `_logger.Info`, `_logger.Warning`, and `_logger.Error`:

```text
src/OpenClaw/Services/WebViewService.cs
src/OpenClaw/Services/WebViewService.ControlUiInspection.cs
src/OpenClaw/Services/WebViewService.Commands.cs
src/OpenClaw/Services/WebViewService.Heartbeat.cs
src/OpenClaw/Services/WebViewService.Profile.cs
```

Do not change logger usage in `MainWindow`, `MainViewModel`, `DiagnosticService`, or `HostedUiBridge` during this task.

- [x] **Step 2: Capture heartbeat runtime options at start time**

Add fields in `WebViewService.Heartbeat.cs`:

```csharp
private int _heartbeatHardRefreshCooldownSeconds;
private bool _heartbeatEnabledForRun;
```

Change `StartHeartbeat` so it captures values from the `HeartbeatOptions` and `RecoveryPolicyOptions` supplied by the caller. If `StartHeartbeat` currently has no recovery-policy parameter, add one:

```csharp
public void StartHeartbeat(
    string? gatewayUrl,
    HeartbeatOptions heartbeatOptions,
    RecoveryPolicyOptions recoveryPolicyOptions)
{
    _heartbeatEnabledForRun = heartbeatOptions.EnableHeartbeat;
    _heartbeatHardRefreshCooldownSeconds = recoveryPolicyOptions.HardRefreshCooldownSeconds;
    // keep existing interval/threshold setup
}
```

Then replace the mid-loop `App.Configuration.Settings.RecoveryPolicy.HardRefreshCooldownSeconds` read with:

```csharp
var seconds = _heartbeatHardRefreshCooldownSeconds;
```

- [x] **Step 3: Update callers**

Update callers that start heartbeat work to pass both settings explicitly:

```csharp
webViewService.StartHeartbeat(
    environment.GatewayUrl,
    App.Configuration.Settings.Heartbeat,
    App.Configuration.Settings.RecoveryPolicy);
```

If the call flows through `ShellSessionCoordinatorAdapters`, Task 7A will remove the adapter's fallback global reads and keep this explicit configuration flow.

- [x] **Step 4: Add guardrails**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$webViewServiceFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services') -File -Filter 'WebViewService*.cs'
foreach ($file in $webViewServiceFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match 'App\.Logger') {
        throw "WebViewService partial must use injected logger, not App.Logger: $($file.Name)"
    }
}

$heartbeat = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Heartbeat.cs') -Raw
if ($heartbeat -match 'App\.Configuration\.Settings\.Heartbeat|App\.Configuration\.Settings\.RecoveryPolicy') {
    throw 'Heartbeat must capture settings at start time instead of reading App.Configuration mid-loop.'
}
```

- [x] **Step 5: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/WebViewService*.cs tools/verify-repo-structure.ps1
git commit -m "refactor: inject WebViewService logger and capture heartbeat config"
```

---

### Task 3B: Move WebView Command Scripts To Embedded Assets

**Files:**
- Create: `src/OpenClaw/Services/WebViewCommands.StopInjection.js`
- Create: `src/OpenClaw/Services/WebViewCommands.AbortRun.js`
- Modify: `src/OpenClaw/Services/WebViewService.Commands.cs`
- Modify: `src/OpenClaw/OpenClaw.csproj`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Extract `/stop` injection script**

Move the JavaScript currently embedded inside `TryInjectStopCommandAsync()` to `WebViewCommands.StopInjection.js` without changing selectors, input handling, event dispatch, or `/stop` text.

The C# method should become:

```csharp
private async Task<bool> TryInjectStopCommandAsync()
{
    try
    {
        var result = await ExecuteScriptAsync(WebViewCommandScripts.StopInjection);
        return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
    }
    catch (COMException ex) when (IsCoreWebViewUnavailable(ex))
    {
        _logger.Warning($"Stop skipped because CoreWebView2 became unavailable: {ex.Message}");
        return false;
    }
    catch (Exception ex)
    {
        _logger.Warning($"Failed to inject /stop command: {ex.Message}");
        return false;
    }
}
```

- [x] **Step 2: Extract abort-run script**

Move the JavaScript currently embedded inside `TryAbortActiveRunAsync()` to `WebViewCommands.AbortRun.js` without changing stop/abort button detection or hosted API targets.

The C# method should become:

```csharp
private async Task<bool> TryAbortActiveRunAsync()
{
    try
    {
        var result = await ExecuteScriptAsync(WebViewCommandScripts.AbortRun);
        return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
    }
    catch (COMException ex) when (IsCoreWebViewUnavailable(ex))
    {
        _logger.Warning($"Abort skipped because CoreWebView2 became unavailable: {ex.Message}");
        return false;
    }
    catch (Exception ex)
    {
        _logger.Warning($"Failed to trigger hosted UI stop action: {ex.Message}");
        return false;
    }
}
```

- [x] **Step 3: Add command script loader**

Add a small internal helper in `WebViewService.Commands.cs` or a new `WebViewCommandScripts.cs`:

```csharp
internal static class WebViewCommandScripts
{
    private const string StopInjectionResourceName = "OpenClaw.Services.WebViewCommands.StopInjection.js";
    private const string AbortRunResourceName = "OpenClaw.Services.WebViewCommands.AbortRun.js";

    public static string StopInjection => _stopInjection.Value;
    public static string AbortRun => _abortRun.Value;

    private static readonly Lazy<string> _stopInjection = new(() => Load(StopInjectionResourceName));
    private static readonly Lazy<string> _abortRun = new(() => Load(AbortRunResourceName));

    private static string Load(string resourceName)
    {
        var assembly = typeof(WebViewCommandScripts).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded WebView command script: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [x] **Step 4: Add embedded resources**

Add to `OpenClaw.csproj`:

```xml
<EmbeddedResource Include="Services\WebViewCommands.StopInjection.js" LogicalName="OpenClaw.Services.WebViewCommands.StopInjection.js" />
<EmbeddedResource Include="Services\WebViewCommands.AbortRun.js" LogicalName="OpenClaw.Services.WebViewCommands.AbortRun.js" />
```

- [x] **Step 5: Add inline JavaScript guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$commandFile = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Commands.cs') -Raw
if ($commandFile -match 'ExecuteScriptAsync\(@"|ExecuteScriptAsync\(\$@"') {
    throw 'WebViewService.Commands.cs must load browser scripts from embedded JS assets, not large inline strings.'
}
foreach ($asset in @('WebViewCommands.StopInjection.js', 'WebViewCommands.AbortRun.js')) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "src/OpenClaw/Services/$asset"))) {
        throw "Missing WebView command script asset: $asset"
    }
}
```

- [x] **Step 6: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/WebViewService.Commands.cs src/OpenClaw/Services/WebViewCommandScripts.cs src/OpenClaw/Services/WebViewCommands.StopInjection.js src/OpenClaw/Services/WebViewCommands.AbortRun.js src/OpenClaw/OpenClaw.csproj tools/verify-repo-structure.ps1
git commit -m "refactor: move WebView command scripts to embedded assets"
```

---

### Task 3C: Extract WebView Recreation Scheduling

**Files:**
- Create: `src/OpenClaw/Services/WebViewRecreationService.cs`
- Modify: `src/OpenClaw/MainWindow.WebView.cs`
- Modify: `src/OpenClaw/MainWindow.Shared.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Create scheduling service**

Create `WebViewRecreationService` to own merged recreation requests, cooldown/circuit-breaker checks, and scheduling telemetry:

```csharp
internal sealed class WebViewRecreationService
{
    private readonly IAppLogger _logger;
    private bool _isRecreating;
    private bool _hasQueuedRequest;
    private string? _queuedReason;
    private int _mergedRequests;

    public WebViewRecreationService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsRecreating => _isRecreating;
    public int MergedRequests => _mergedRequests;

    public WebViewRecreationDecision Schedule(string reason, bool circuitBreakerAllowsRecreation)
    {
        if (!circuitBreakerAllowsRecreation)
        {
            return WebViewRecreationDecision.Suppressed(reason);
        }

        if (_isRecreating)
        {
            _hasQueuedRequest = true;
            _queuedReason = reason;
            _mergedRequests++;
            _logger.Info("webview.recreate.merged", new { reason, merged = _mergedRequests });
            return WebViewRecreationDecision.Merged(reason);
        }

        _isRecreating = true;
        return WebViewRecreationDecision.Start(reason);
    }

    public string? CompleteAndConsumeQueuedReason()
    {
        _isRecreating = false;
        if (!_hasQueuedRequest)
        {
            return null;
        }

        _hasQueuedRequest = false;
        var reason = _queuedReason;
        _queuedReason = null;
        return reason;
    }
}

internal readonly record struct WebViewRecreationDecision(
    WebViewRecreationDecisionKind Kind,
    string Reason)
{
    public static WebViewRecreationDecision Start(string reason) => new(WebViewRecreationDecisionKind.Start, reason);
    public static WebViewRecreationDecision Merged(string reason) => new(WebViewRecreationDecisionKind.Merged, reason);
    public static WebViewRecreationDecision Suppressed(string reason) => new(WebViewRecreationDecisionKind.Suppressed, reason);
}

internal enum WebViewRecreationDecisionKind
{
    Start,
    Merged,
    Suppressed,
}
```

- [x] **Step 2: Move policy out of `MainWindow.WebView.cs`**

Keep `MainWindow.WebView.cs` responsible for:

```text
- owning the actual WebView2 control instance
- starting/stopping the existing UI timer if one exists
- swapping the control in XAML
- calling ViewModel.InitializeWebViewAsync
```

Move these responsibilities into `WebViewRecreationService`:

```text
- whether a request starts now or merges into an in-flight request
- merged request count
- queued reason
- circuit-breaker suppression decision shape
- recreation instrumentation messages
```

- [x] **Step 3: Wire service into `MainWindow`**

Add a field in `MainWindow.Shared.cs`:

```csharp
private readonly WebViewRecreationService _webViewRecreationService = new(App.Logger);
```

Use service decisions in the existing scheduling method:

```csharp
var decision = _webViewRecreationService.Schedule(reason, circuitBreakerAllowsRecreation);
if (decision.Kind == WebViewRecreationDecisionKind.Suppressed)
{
    ViewModel.ShowCircuitBreakerError();
    return;
}

if (decision.Kind == WebViewRecreationDecisionKind.Merged)
{
    return;
}

StartWebViewRecreationTimer(decision.Reason);
```

- [x] **Step 4: Preserve existing platform behavior**

Do not move or rewrite:

```text
- WebView2 control construction
- WebView2 environment/user-data-folder selection
- title bar/DWM code
- tray show/hide code
- window bounds persistence
```

Those are historical high-risk areas documented in `DEVELOPMENT_NOTES.md`.

- [x] **Step 5: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$mainWindowWebView = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.WebView.cs') -Raw
if ($mainWindowWebView -match '_mergedRecreationRequests|_queuedRecreationReason|_isRecreatingWebView') {
    throw 'WebView recreation scheduling state must live in WebViewRecreationService.'
}
```

- [x] **Step 6: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/WebViewRecreationService.cs src/OpenClaw/MainWindow.WebView.cs src/OpenClaw/MainWindow.Shared.cs tools/verify-repo-structure.ps1
git commit -m "refactor: extract WebView recreation scheduling"
```

---

### Task 4: Convert Runtime Settings To An Apply Pipeline

**Files:**
- Create: `src/OpenClaw.Core/Models/LiveShellSettings.cs`
- Create: `src/OpenClaw.Core/Models/LiveShellSettingsChange.cs`
- Create: `src/OpenClaw.Core/Models/SettingsSaveResult.cs`
- Create: `src/OpenClaw/Services/LiveShellSettingsApplier.cs`
- Modify: `src/OpenClaw/ViewModels/SettingsViewModel.cs`
- Modify: `src/OpenClaw/Views/SettingsDialog.Shared.cs`
- Modify: `src/OpenClaw/MainWindow.Commands.cs`
- Modify: `src/OpenClaw/MainWindow.Shared.cs`
- Modify: `src/OpenClaw/MainWindow.xaml.cs`
- Modify: `src/OpenClaw/MainWindow.Hotkey.cs`
- Modify: `src/OpenClaw/MainWindow.AlwaysOnTop.cs`

- [x] **Step 1: Create Core settings snapshot**

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Models;

public sealed record LiveShellSettings(
    bool AlwaysOnTop,
    bool EnableGlobalHotkey,
    string GlobalHotkey)
{
    public static LiveShellSettings From(AppSettings settings)
    {
        return new LiveShellSettings(
            settings.AlwaysOnTop,
            settings.EnableGlobalHotkey,
            settings.GlobalHotkey.Trim());
    }
}
```

- [x] **Step 2: Create Core change descriptor**

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Models;

public sealed record LiveShellSettingsChange(
    LiveShellSettings Before,
    LiveShellSettings After)
{
    public bool DidChangeAlwaysOnTop => Before.AlwaysOnTop != After.AlwaysOnTop;
    public bool DidChangeGlobalHotkey =>
        Before.EnableGlobalHotkey != After.EnableGlobalHotkey ||
        !string.Equals(Before.GlobalHotkey, After.GlobalHotkey, StringComparison.Ordinal);

    public bool HasChanges => DidChangeAlwaysOnTop || DidChangeGlobalHotkey;
}
```

- [x] **Step 3: Move `SettingsSaveResult` out of the view layer**

Create `src/OpenClaw.Core/Models/SettingsSaveResult.cs`:

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Models;

public readonly record struct SettingsSaveResult(
    bool DidChangeEnvironmentState,
    bool DidChangeSessionTopology,
    bool DidChangeLanguage,
    LiveShellSettingsChange LiveShellSettingsChange)
{
    public bool DidChangeLiveShellOptions => LiveShellSettingsChange.HasChanges;
}
```

Remove this record from `src/OpenClaw/Views/SettingsDialog.Shared.cs`:

```csharp
public readonly record struct SettingsSaveResult(
    bool DidChangeEnvironmentState,
    bool DidChangeSessionTopology,
    bool DidChangeLanguage,
    bool DidChangeLiveShellOptions);
```

Add this using to `src/OpenClaw/Views/SettingsDialog.Shared.cs`:

```csharp
using OpenClaw.Models;
```

Keep the `SettingsSaved` event signature unchanged:

```csharp
public event Action<SettingsSaveResult>? SettingsSaved;
```

- [x] **Step 4: Make `SettingsSaveResult` carry the live-shell change**

Change result construction in `SettingsViewModel.TrySave`:

```csharp
var beforeLiveSettings = new LiveShellSettings(
    _originalAlwaysOnTop,
    _originalEnableGlobalHotkey,
    _originalGlobalHotkey.Trim());
```

Place that snapshot before any `App.Configuration.Settings` mutation in `SaveAll`. After all settings have been written and before returning `true`, construct the result with:

```csharp
var afterLiveSettings = LiveShellSettings.From(App.Configuration.Settings);
result = new SettingsSaveResult(
    DidChangeEnvironmentState,
    DidChangeSessionTopology,
    !string.Equals(_originalLanguage, SelectedLanguage, StringComparison.Ordinal),
    new LiveShellSettingsChange(beforeLiveSettings, afterLiveSettings));
```

Update `SettingsSaveResult` so `DidChangeLiveShellOptions` is computed from `LiveShellSettingsChange.HasChanges`.

- [x] **Step 5: Create `LiveShellSettingsApplier`**

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Models;

namespace OpenClaw.Services;

internal sealed class LiveShellSettingsApplier
{
    private readonly Action<bool> _setAlwaysOnTop;
    private readonly Action _reapplyGlobalHotkey;

    public LiveShellSettingsApplier(Action<bool> setAlwaysOnTop, Action reapplyGlobalHotkey)
    {
        _setAlwaysOnTop = setAlwaysOnTop;
        _reapplyGlobalHotkey = reapplyGlobalHotkey;
    }

    public void Apply(LiveShellSettingsChange change)
    {
        if (change.DidChangeAlwaysOnTop)
        {
            _setAlwaysOnTop(change.After.AlwaysOnTop);
        }

        if (change.DidChangeGlobalHotkey)
        {
            _reapplyGlobalHotkey();
        }
    }
}
```

- [x] **Step 6: Wire the applier into `MainWindow`**

Add a field to `src/OpenClaw/MainWindow.Shared.cs`:

```csharp
private readonly LiveShellSettingsApplier _liveShellSettingsApplier;
```

Initialize it in `src/OpenClaw/MainWindow.xaml.cs` after `InitializeComponent()` and before settings windows can be opened:

```csharp
_liveShellSettingsApplier = new LiveShellSettingsApplier(SetAlwaysOnTop, ReapplyGlobalHotkey);
```

- [x] **Step 7: Update `MainWindow` settings handler**

Replace `ApplyLiveShellSettings()` with:

```csharp
if (saveResult.LiveShellSettingsChange.HasChanges)
{
    _liveShellSettingsApplier.Apply(saveResult.LiveShellSettingsChange);
}
```

This removes direct re-reading from `App.Configuration.Settings` when the save result already knows what changed.

- [x] **Step 8: Add structure guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$settingsViewModel = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/SettingsViewModel.cs') -Raw
if ($settingsViewModel -match 'using OpenClaw\.Views;') {
    throw 'SettingsViewModel must not depend on the Views namespace for SettingsSaveResult.'
}

$settingsDialogShared = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Views/SettingsDialog.Shared.cs') -Raw
if ($settingsDialogShared -match 'record struct SettingsSaveResult') {
    throw 'SettingsSaveResult must live in OpenClaw.Core models, not SettingsDialog.Shared.cs.'
}
```

- [x] **Step 9: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw.Core/Models/LiveShellSettings.cs src/OpenClaw.Core/Models/LiveShellSettingsChange.cs src/OpenClaw.Core/Models/SettingsSaveResult.cs src/OpenClaw/Services/LiveShellSettingsApplier.cs src/OpenClaw/ViewModels/SettingsViewModel.cs src/OpenClaw/Views/SettingsDialog.Shared.cs src/OpenClaw/MainWindow.Commands.cs src/OpenClaw/MainWindow.Shared.cs src/OpenClaw/MainWindow.xaml.cs src/OpenClaw/MainWindow.Hotkey.cs src/OpenClaw/MainWindow.AlwaysOnTop.cs tools/verify-repo-structure.ps1
git commit -m "refactor: centralize live shell settings application"
```

---

### Task 5: Replace Compact Mode Property Patching With Visual States

**Files:**
- Modify: `src/OpenClaw/MainWindow.xaml`
- Modify: `src/OpenClaw/MainWindow.CompactMode.cs`
- Modify: `src/OpenClaw/Styles/StatusResources.xaml`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Add compact resources**

In `StatusResources.xaml`, add:

```xml
<x:Double x:Key="CompactTopStatusPillMinWidth">0</x:Double>
<Thickness x:Key="CompactTopStatusPillPadding">8,5</Thickness>
<Thickness x:Key="CompactTopStatusPillMargin">0,0,8,0</Thickness>
<x:Double x:Key="CompactTopStatusModelSegmentMinWidth">0</x:Double>
<Thickness x:Key="CompactTopStatusModelSegmentMargin">4,0,0,0</Thickness>
<Thickness x:Key="CompactTopBarPadding">8,6</Thickness>
```

- [x] **Step 2: Add visual states to `RootLayout`**

`MainWindow` derives from `Window`, not `Control`, so avoid the Window-as-Control visual-state pattern. Wrap the existing shell layout in a root `UserControl x:Name="RootLayout"` with an inner layout grid, attach the visual-state group to `RootLayout`, and switch it with `VisualStateManager.GoToState(RootLayout, stateName, useTransitions: false)`.

Add this block as the first child inside `RootLayout`:

```xml
<VisualStateManager.VisualStateGroups>
    <VisualStateGroup x:Name="ShellModeStates">
        <VisualState x:Name="FullMode" />
        <VisualState x:Name="CompactMode">
            <VisualState.Setters>
                <Setter Target="CommandBarSurface.Padding" Value="{StaticResource CompactTopBarPadding}" />
                <Setter Target="EnvironmentSummaryGroup.Visibility" Value="Collapsed" />
                <Setter Target="LatencyBadge.Visibility" Value="Collapsed" />
                <Setter Target="TopStatusPill.MinWidth" Value="{StaticResource CompactTopStatusPillMinWidth}" />
                <Setter Target="TopStatusPill.Margin" Value="{StaticResource CompactTopStatusPillMargin}" />
                <Setter Target="TopStatusPill.Padding" Value="{StaticResource CompactTopStatusPillPadding}" />
                <Setter Target="ModelStatusSegment.MinWidth" Value="{StaticResource CompactTopStatusModelSegmentMinWidth}" />
                <Setter Target="ModelStatusSegment.Margin" Value="{StaticResource CompactTopStatusModelSegmentMargin}" />
            </VisualState.Setters>
        </VisualState>
    </VisualStateGroup>
</VisualStateManager.VisualStateGroups>
```

- [x] **Step 3: Simplify code-behind**

Replace `ApplyCompactTopBarState(bool isCompact)` body:

```csharp
private void ApplyCompactTopBarState(bool isCompact)
{
    VisualStateManager.GoToState(RootLayout, isCompact ? "CompactMode" : "FullMode", useTransitions: false);
}
```

Add `using Microsoft.UI.Xaml;` if it is not already available in `MainWindow.CompactMode.cs`.

Remove duplicated constants:

```csharp
private const double FullTopStatusPillMinWidth = 440;
private const double FullModelStatusSegmentMinWidth = 190;
```

- [x] **Step 4: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$compact = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.CompactMode.cs') -Raw
if ($compact -match 'TopStatusPill\.MinWidth|ModelStatusSegment\.MinWidth|EnvironmentSummaryGroup\.Visibility|LatencyBadge\.Visibility') {
    throw 'Compact top-bar layout should be driven by XAML visual states, not code-behind property patching.'
}
$windowStatePattern = 'VisualStateManager\.GoToState\(\s*this'
if ($compact -match $windowStatePattern) {
    throw 'MainWindow compact mode must switch RootLayout, not the Window instance.'
}
if ($compact -notmatch 'VisualStateManager\.GoToState\(\s*RootLayout') {
    throw 'MainWindow compact mode must switch the RootLayout visual state owner.'
}

$mainWindowXaml = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.xaml') -Raw
if ($mainWindowXaml -notmatch 'x:Name="RootLayout"[\s\S]*VisualStateManager\.VisualStateGroups') {
    throw 'Compact visual states must be attached to RootLayout.'
}
```

- [x] **Step 5: Run automated verification and queue manual UI check**

Run full verification from Task 1.

Manual VS2026 debug checklist to carry into Task 16 final integration:

```text
1. Launch app.
2. Enter compact mode from tray or title command.
3. Confirm 480px compact width shows status pill without environment selector or latency badge.
4. Confirm MODEL text ellipsizes instead of overlapping buttons.
5. Exit compact mode and confirm full top bar restores.
```

- [x] **Step 6: Commit**

```powershell
git add src/OpenClaw/MainWindow.xaml src/OpenClaw/MainWindow.CompactMode.cs src/OpenClaw/Styles/StatusResources.xaml tools/verify-repo-structure.ps1
git commit -m "refactor: drive compact top bar through visual states"
```

---

### Task 6: Split Hosted Bridge Browser Script Into Focused Assets

**Files:**
- Create: `src/OpenClaw/Services/HostedUiBridge.StatusInspection.js`
- Create: `src/OpenClaw/Services/HostedUiBridge.CommandDispatch.js`
- Create: `src/OpenClaw/Services/HostedUiBridge.MutationFilter.js`
- Create: `src/OpenClaw/Services/HostedUiBridge.HostMessaging.js`
- Modify: `src/OpenClaw/Services/HostedUiBridge.Script.js`
- Modify: `src/OpenClaw/Services/HostedUiBridge.Script.cs`
- Modify: `src/OpenClaw/OpenClaw.csproj`
- Modify: `tools/verify-bridge-scripts.ps1`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Extract host messaging**

Move `postHostMessage`, host message kind constants, and safe `chrome.webview.postMessage` handling into `HostedUiBridge.HostMessaging.js`.

Export one object:

```javascript
const openClawHostMessaging = (() => {
  const postHostMessage = (message) => {
    try {
      if (!window.chrome?.webview?.postMessage) return false;
      window.chrome.webview.postMessage(message);
      return true;
    } catch {
      return false;
    }
  };

  return { postHostMessage };
})();
```

- [x] **Step 2: Extract command dispatch**

Move `invokeBridgeMethod`, `dispatchBridgeEvent`, and `onCommand` into `HostedUiBridge.CommandDispatch.js`.

Expose:

```javascript
const openClawCommandDispatch = (() => {
  const createCommandHandler = ({ inspectControlUi, postStatus, checkSessionReady }) => {
    return async (message) => {
      const command = message?.command || '';
      const payload = message?.payload;

      switch (command) {
        case 'refresh_session':
          return await runCommand(
            command,
            payload,
            ['refreshSession', 'reloadSession', 'reconnect', 'connect', 'resume'],
            { inspectControlUi, postStatus, checkSessionReady });
        case 'fetch_recent_messages':
          return await runCommand(
            command,
            payload,
            ['fetchRecentMessages', 'loadRecentMessages', 'syncMessages', 'sync'],
            { inspectControlUi, postStatus });
        case 'lightweight_sync':
          return await runCommand(
            command,
            payload,
            ['sync', 'refresh', 'refreshSession', 'fetchRecentMessages', 'loadRecentMessages'],
            { inspectControlUi, postStatus, checkSessionReady });
        case 'reconnect_intent':
          return await runCommand(
            command,
            payload,
            ['reconnect', 'connect', 'resume', 'refreshSession'],
            { inspectControlUi, postStatus });
        default:
          dispatchBridgeEvent(command, payload);
          return false;
      }
    };
  };

  return { createCommandHandler, dispatchBridgeEvent, invokeBridgeMethod };
})();
```

The helper `runCommand` should call `invokeBridgeMethod`, dispatch CustomEvent fallback only when no hosted method handled the command, post a fresh status snapshot and session-ready check when provided, then return only the hosted-method `handled` result. Dispatching a CustomEvent without a hosted method is observable fallback, but it is not enough to report a soft-resync command as handled.

- [x] **Step 3: Extract mutation filtering**

Move excluded selectors and mutation relevance checks into `HostedUiBridge.MutationFilter.js`.

Expose:

```javascript
const openClawMutationFilter = (() => {
  const STATUS_PROBE_EXCLUDED_SELECTOR = '.chat-sidebar, .sidebar-panel, .sidebar-content, .chat-tool-card__preview-frame, .settings-workspace__body, .config-content, .config-form, .config-section-card, .cron-summary-strip, .cron-workspace';
  const isStatusProbeExcludedElement = (el) => Boolean(el?.closest?.(STATUS_PROBE_EXCLUDED_SELECTOR));
  const isStatusRelevantMutation = (mutation) => {
    const target = asElement(mutation.target);
    if (!target || isStatusProbeExcludedElement(target)) return false;
    if (mutation.type === 'childList') return true;
    if (mutation.type !== 'attributes') return false;
    return ['aria-busy', 'data-busy', 'data-running', 'data-state', 'data-status', 'aria-label', 'title']
      .includes(mutation.attributeName || '');
  };

  const isStatusRelevantEventTarget = (target) => {
    const element = asElement(target);
    return Boolean(element) && !isStatusProbeExcludedElement(element);
  };

  return { isStatusProbeExcludedElement, isStatusRelevantMutation, isStatusRelevantEventTarget };
})();
```

- [x] **Step 4: Extract status inspection**

Move `inspectControlUi`, app-state status reading, DOM fallback status reading, stale busy activity signature, and current model reader wiring into `HostedUiBridge.StatusInspection.js`.

Expose:

```javascript
const openClawStatusInspection = (() => {
  const createInspector = ({ strings, mutationFilter, modelResolver }) => {
    const inspectControlUi = () => {
      // Move the existing inspectControlUi body here and replace:
      // STRINGS -> strings
      // isStatusProbeExcludedElement -> mutationFilter.isStatusProbeExcludedElement
      // readCurrentModel -> a local reader backed by modelResolver.resolveOpenClawAppStateModel
    };

    return { inspectControlUi, isEditableElement, compactText };
  };

  return { createInspector };
})();
```

Keep the current stale-busy behavior together with status inspection: `readChatActivitySignature`, `collectDomActivitySignature`, `applyBusyStaleness`, `BUSY_STALE_THRESHOLD_MS`, `inputFocused`, and `focusedInputHasText` move into this asset.

- [x] **Step 5: Make `HostedUiBridge.Script.js` a composition file**

After extraction, `HostedUiBridge.Script.js` should only:

- define `STRINGS`
- compose imported asset snippets
- create `inspectControlUi`
- wire `window.__openClawHostBridge`
- schedule mutation/change/load events

Target size: under 250 lines.

- [x] **Step 6: Update script builder**

In `HostedUiBridge.Script.cs`, add placeholders:

```csharp
private const string HostMessagingPlaceholder = "__OPENCLAW_HOST_MESSAGING_SCRIPT__";
private const string StatusInspectionPlaceholder = "__OPENCLAW_STATUS_INSPECTION_SCRIPT__";
private const string CommandDispatchPlaceholder = "__OPENCLAW_COMMAND_DISPATCH_SCRIPT__";
private const string MutationFilterPlaceholder = "__OPENCLAW_MUTATION_FILTER_SCRIPT__";
```

Load and replace each embedded resource in stable dependency order:

1. Host messaging
2. Mutation filter
3. Model resolver
4. Status inspection
5. Command dispatch
6. Main script

- [x] **Step 7: Update project embedded resources**

Add to `OpenClaw.csproj`:

```xml
<EmbeddedResource Include="Services\HostedUiBridge.StatusInspection.js" LogicalName="OpenClaw.Services.HostedUiBridge.StatusInspection.js" />
<EmbeddedResource Include="Services\HostedUiBridge.CommandDispatch.js" LogicalName="OpenClaw.Services.HostedUiBridge.CommandDispatch.js" />
<EmbeddedResource Include="Services\HostedUiBridge.MutationFilter.js" LogicalName="OpenClaw.Services.HostedUiBridge.MutationFilter.js" />
<EmbeddedResource Include="Services\HostedUiBridge.HostMessaging.js" LogicalName="OpenClaw.Services.HostedUiBridge.HostMessaging.js" />
```

- [x] **Step 8: Extend bridge script verification**

Extend the existing `tools/verify-bridge-scripts.ps1` from Task 1 with checks for:

```text
PASS: MODEL default model
PASS: MODEL null override session precedence
PASS: MODEL object override
PASS: command dispatch returns handled true when bridge method exists
PASS: command dispatch falls back to CustomEvent when method missing
PASS: host messaging returns false without chrome.webview
PASS: mutation filter ignores settings/config/cron/sidebar mutations
```

Do this with Node.js. The verifier must fail closed when Node is missing or not executable, unless `OPENCLAW_ALLOW_NODE_SKIP=1` is set explicitly for a local skip.

- [x] **Step 9: Add structure guardrails**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$mainBridgeScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.Script.js') -Raw
$mainBridgeLines = ($mainBridgeScript -split "`n").Count
if ($mainBridgeLines -gt 250) {
    throw "HostedUiBridge.Script.js should be a composition file under 250 lines; found $mainBridgeLines."
}
foreach ($asset in @(
    'HostedUiBridge.StatusInspection.js',
    'HostedUiBridge.CommandDispatch.js',
    'HostedUiBridge.MutationFilter.js',
    'HostedUiBridge.HostMessaging.js'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "src/OpenClaw/Services/$asset"))) {
        throw "Missing focused bridge asset: $asset"
    }
}
```

- [x] **Step 10: Run verification and commit**

Run full verification from Task 1, using the renamed `tools\verify-bridge-scripts.ps1` if renamed.

Commit:

```powershell
git add src/OpenClaw/Services/HostedUiBridge*.js src/OpenClaw/Services/HostedUiBridge.Script.cs src/OpenClaw/OpenClaw.csproj tools/verify-bridge-scripts.ps1 tools/verify-repo-structure.ps1
git commit -m "refactor: split hosted bridge browser assets"
```

---

### Task 6A: Correct Hosted Bridge Polling Timer Drift

**Files:**
- Modify: `src/OpenClaw/Services/HostedUiBridge.Script.js`
- Modify: `tools/verify-bridge-scripts.ps1`

- [x] **Step 1: Replace recursive timeout scheduling with drift-aware scheduling**

After Task 6 has reduced `HostedUiBridge.Script.js` to a composition shell, keep the polling owner in that shell but make it self-correcting.

Replace the current polling state:

```javascript
let pollInterval = 8000;
let pollTimer = 0;
```

with:

```javascript
let pollInterval = 8000;
let pollTimer = 0;
let nextPollAt = 0;

const getPollInterval = (snapshot) => {
  if (snapshot.phase === 'connected' && snapshot.isBusy) return 4000;
  if (snapshot.phase === 'gateway_connecting' || snapshot.phase === 'page_loaded') return 4000;
  return 15000;
};

const scheduleNextPoll = (interval, now = Date.now()) => {
  pollInterval = interval;
  nextPollAt = nextPollAt > now ? nextPollAt + interval : now + interval;
  const delay = Math.max(0, nextPollAt - Date.now());
  pollTimer = window.setTimeout(tick, delay);
};
```

Then make `tick` compute the next interval from the snapshot and call `scheduleNextPoll(nextInterval)` after the current inspection work completes. Do not schedule the next tick before the current status snapshot is posted.

- [x] **Step 2: Preserve restart semantics**

Keep the existing restart entry point but reset `nextPollAt` when an external event requests immediate polling:

```javascript
const restartPolling = (interval = pollInterval) => {
  if (pollTimer) {
    window.clearTimeout(pollTimer);
  }

  nextPollAt = 0;
  scheduleNextPoll(interval);
};
```

- [x] **Step 3: Add bridge script verification for drift helper**

Extend `tools/verify-bridge-scripts.ps1` to load the composition shell and assert these strings exist after Task 6A:

```text
PASS: polling uses nextPollAt drift correction
PASS: polling interval is snapshot-driven
```

The script check can be structural:

```powershell
$bridgeScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.Script.js') -Raw
if ($bridgeScript -notmatch 'nextPollAt' -or $bridgeScript -notmatch 'scheduleNextPoll') {
    throw 'Bridge polling must use drift-aware scheduling.'
}
```

- [x] **Step 4: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/HostedUiBridge.Script.js tools/verify-bridge-scripts.ps1
git commit -m "fix: correct hosted bridge polling drift"
```

---

### Task 7: Move Diagnostic WebView Runtime Knowledge Out Of Core

**Files:**
- Modify: `src/OpenClaw.Core/Services/DiagnosticBundleService.cs`
- Modify: `src/OpenClaw/Services/DiagnosticService.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Commands.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Remove WebView2 type-name reflection from Core**

In `DiagnosticBundleService`, remove this Core-layer WebView2 lookup:

```csharp
Type.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment, Microsoft.Web.WebView2.Core")
```

Add a pure Core runtime record near `DiagnosticBundleService`:

```csharp
public sealed record DiagnosticRuntimeInfo(
    string? WebView2RuntimeVersion,
    string OsVersion,
    string DotNetVersion,
    string AppVersion,
    string ProcessArchitecture,
    int ProcessorCount,
    string MachineHash);
```

Replace `CollectRuntimeInfo()` with overloads that accept the plain record and no longer know the WebView2 assembly name:

```csharp
public static DiagnosticRuntimeInfo CollectRuntimeInfo(string? webView2RuntimeVersion)
{
    return new DiagnosticRuntimeInfo(
        WebView2RuntimeVersion: webView2RuntimeVersion,
        OsVersion: Environment.OSVersion.ToString(),
        DotNetVersion: Environment.Version.ToString(),
        AppVersion: GetAppVersion(),
        ProcessArchitecture: System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        ProcessorCount: Environment.ProcessorCount,
        MachineHash: HashMachineName(Environment.MachineName));
}

public static string FormatRuntimeInfo(DiagnosticRuntimeInfo runtimeInfo)
{
    var webView2 = string.IsNullOrWhiteSpace(runtimeInfo.WebView2RuntimeVersion)
        ? "unavailable"
        : runtimeInfo.WebView2RuntimeVersion;

    return string.Join(Environment.NewLine, new[]
    {
        $"OS: {runtimeInfo.OsVersion}",
        $".NET: {runtimeInfo.DotNetVersion}",
        $"App: {runtimeInfo.AppVersion}",
        $"Architecture: {runtimeInfo.ProcessArchitecture}",
        $"Machine: {runtimeInfo.MachineHash}",
        $"Processors: {runtimeInfo.ProcessorCount}",
        $"WebView2: {webView2}",
    });
}
```

Change `ExportBundleAsync` to receive runtime info instead of collecting it internally:

```csharp
public static async Task<string> ExportBundleAsync(
    string settingsJson,
    string logsDirectory,
    string diagnosticSummary,
    string outputDirectory,
    DiagnosticRuntimeInfo runtimeInfo)
```

Inside `ExportBundleAsync`, replace:

```csharp
var runtimeInfo = CollectRuntimeInfo();
await AddTextEntryAsync(archive, "runtime-info.txt", runtimeInfo);
```

with:

```csharp
await AddTextEntryAsync(archive, "runtime-info.txt", FormatRuntimeInfo(runtimeInfo));
```

- [x] **Step 2: Collect WebView2 runtime version in WinUI layer**

In `DiagnosticService`, use direct WebView2 API:

```csharp
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
```

Keep `CheckWebView2Runtime(IAppLogger logger)` on the existing behavior path, but call the helper so there is one WinUI-owned runtime lookup and no service-internal `App.Logger` read:

```csharp
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
```

- [x] **Step 3: Pass runtime info from export command**

In `MainViewModel.Commands.cs`, update `OnExportDiagnosticBundleAsync()` before calling Core:

```csharp
var runtimeInfo = DiagnosticBundleService.CollectRuntimeInfo(
    DiagnosticService.GetWebView2RuntimeVersion(_runtime.Logger));
```

Then pass the record to the export call:

```csharp
var outputPath = await DiagnosticBundleService.ExportBundleAsync(
    settingsJson,
    logsDirectory,
    diagnosticSummary,
    outputDirectory,
    runtimeInfo);
```

- [x] **Step 4: Strengthen Core guardrail**

Update `tools/verify-repo-structure.ps1` to reject:

```powershell
$forbiddenCorePattern = 'using Microsoft\.UI|using Microsoft\.Web\.WebView2|using Windows\.Graphics|using Windows\.UI|using WinRT|Microsoft\.Web\.WebView2|Type\.GetType\("Microsoft\.Web\.WebView2'
```

inside `src/OpenClaw.Core`.

- [x] **Step 5: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw.Core/Services/DiagnosticBundleService.cs src/OpenClaw/Services/DiagnosticService.cs src/OpenClaw/ViewModels/MainViewModel.Commands.cs tools/verify-repo-structure.ps1
git commit -m "refactor: keep WebView2 diagnostics in WinUI layer"
```

---

### Task 7A: Inject Coordinator Adapter Configuration

**Files:**
- Modify: `src/OpenClaw/Services/ShellSessionCoordinator.Adapters.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Lifecycle.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Remove global fallback reads from adapter extension**

Change `ShellSessionCoordinatorAdapters.AttachAsync` so callers must supply recovery and heartbeat options:

```csharp
public static Task AttachAsync(
    this ShellSessionCoordinator coordinator,
    WebViewService webViewService,
    HostedUiBridge bridge,
    RecoveryPolicyOptions recoveryOptions,
    HeartbeatOptions heartbeatOptions,
    IAppLogger logger)
{
    return coordinator.AttachAsync(
        new ShellSessionWebViewAdapter(webViewService),
        new ShellSessionBridgeAdapter(bridge),
        recoveryOptions,
        heartbeatOptions,
        logger);
}
```

Do not keep nullable defaults that read `App.Configuration` or `App.Logger` inside the adapter.

- [x] **Step 2: Pass options at the call site**

In `MainViewModel.Lifecycle.cs`, replace:

```csharp
await _coordinator.AttachAsync(_webViewService, _hostedUiBridge);
```

with:

```csharp
await _coordinator.AttachAsync(
    _webViewService,
    _hostedUiBridge,
    App.Configuration.Settings.RecoveryPolicy,
    App.Configuration.Settings.Heartbeat,
    App.Logger);
```

This keeps App-bound configuration at the application orchestration edge instead of inside the adapter.

- [x] **Step 3: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$adapter = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/ShellSessionCoordinator.Adapters.cs') -Raw
if ($adapter -match 'App\.Configuration|App\.Logger') {
    throw 'ShellSessionCoordinator adapter must receive configuration and logger explicitly.'
}
```

- [x] **Step 4: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/ShellSessionCoordinator.Adapters.cs src/OpenClaw/ViewModels/MainViewModel.Lifecycle.cs tools/verify-repo-structure.ps1
git commit -m "refactor: inject coordinator adapter configuration"
```

---

### Task 7B: Inject UI Dispatcher Into MainViewModel

**Files:**
- Modify: `src/OpenClaw/ViewModels/MainViewModel.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Fields.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Status.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Heartbeat.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Indicators.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Add dispatcher abstraction**

Add a private field:

```csharp
private readonly Func<Action, bool> _dispatchToUi;
```

Update the constructor without dropping the `IAppLogger` dependency introduced in Task 2:

```csharp
public MainViewModel(IAppLogger logger, Func<Action, bool> dispatchToUi)
{
    _webViewService = new WebViewService(logger);
    _dispatchToUi = dispatchToUi ?? throw new ArgumentNullException(nameof(dispatchToUi));
    InitializeCommands();
    SubscribeToServiceEvents();
    InitializeCoordinator();
    LoadEnvironments();
    UpdateStatusPresentation();
}
```

Do not run fallback synchronous execution when dispatch fails. Failed dispatch should log/drop or return a failed task at the app edge; it must not run WinUI/WebView2 work on the originating background thread.

- [x] **Step 2: Remove static helper from status partial**

Replace `RunOnUiThread(() => ...)` in `MainViewModel.Status.cs`, `MainViewModel.Heartbeat.cs`, and `MainViewModel.Indicators.cs` with:

```csharp
_dispatchToUi(() => ApplyConnectionState(state));
```

Use the matching existing lambda body for each call.

- [x] **Step 3: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$viewModelStatus = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Status.cs') -Raw
if ($viewModelStatus -match 'App\.MainWindow|RunOnUiThread') {
    throw 'MainViewModel status updates must use injected UI dispatcher.'
}
```

- [x] **Step 4: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/ViewModels/MainViewModel.cs src/OpenClaw/ViewModels/MainViewModel.Fields.cs src/OpenClaw/ViewModels/MainViewModel.Status.cs src/OpenClaw/ViewModels/MainViewModel.Heartbeat.cs src/OpenClaw/ViewModels/MainViewModel.Indicators.cs tools/verify-repo-structure.ps1
git commit -m "refactor: inject MainViewModel UI dispatcher"
```

---

### Task 7C1: Extract StatusPresenter And Theme-Aware Status Brushes

**Files:**
- Create: `src/OpenClaw/ViewModels/StatusPresenter.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Fields.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Formatting.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.StatusFormatting.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Heartbeat.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Indicators.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.RunIndicators.cs`
- Modify: `src/OpenClaw/Styles/StatusResources.xaml`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Use semantic status brush resources**

Use existing semantic resources from `Colors.xaml` instead of constructing status brushes in the ViewModel:

```xml
<SolidColorBrush x:Key="StatusOfflineBrush" Color="#FF6B7280" />
<SolidColorBrush x:Key="SuccessBrush" Color="#FF22C55E" />
<SolidColorBrush x:Key="StatusReconnectingBrush" Color="#FFF59E0B" />
<SolidColorBrush x:Key="StatusErrorBrush" Color="#FFEF4444" />
```

Document the chosen key names in `docs/code-style.md` during Task 9.

- [x] **Step 2: Add resource lookup helpers**

Replace static brush creation in `MainViewModel.Fields.cs`:

```csharp
private static readonly Brush NeutralBrush = CreateBrush(107, 114, 128);
private static readonly Brush SuccessBrush = CreateBrush(34, 197, 94);
private static readonly Brush WarningBrush = CreateBrush(245, 158, 11);
private static readonly Brush ErrorBrush = CreateBrush(239, 68, 68);
```

with resource-backed properties:

```csharp
private static Brush NeutralBrush => GetStatusBrush("StatusOfflineBrush");
private static Brush SuccessBrush => GetStatusBrush("SuccessBrush");
private static Brush WarningBrush => GetStatusBrush("StatusReconnectingBrush");
private static Brush ErrorBrush => GetStatusBrush("StatusErrorBrush");

private static Brush GetStatusBrush(string key)
{
    return Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
        ? brush
        : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}
```

This removes static brush instances while preserving the existing formatting call sites.

- [x] **Step 3: Extract pure formatting into `StatusPresenter`**

Create `StatusPresenter` for formatting methods that do not need to mutate ViewModel fields. Keep brush lookup in `MainViewModel` and pass the current brushes into the presenter so `StatusPresenter` does not need to reach into private ViewModel members.

```csharp
internal readonly record struct StatusBrushes(
    Brush Neutral,
    Brush Success,
    Brush Warning,
    Brush Error);

internal readonly record struct StatusPresentation(string Text, Brush Brush);

internal sealed class StatusPresenter
{
    public StatusPresentation FormatConnectionState(ConnectionState state, StatusBrushes brushes)
    {
        return state switch
        {
            ConnectionState.Connected => new StatusPresentation(StringResources.StatusConnected, brushes.Success),
            ConnectionState.Loading => new StatusPresentation(StringResources.StatusLoading, brushes.Warning),
            ConnectionState.GatewayConnecting => new StatusPresentation(StringResources.StatusGatewayConnecting, brushes.Warning),
            ConnectionState.Reconnecting => new StatusPresentation(StringResources.StatusReconnecting, brushes.Warning),
            ConnectionState.AuthFailed => new StatusPresentation(StringResources.StatusAuthFailed, brushes.Error),
            ConnectionState.Error => new StatusPresentation(StringResources.StatusError, brushes.Error),
            _ => new StatusPresentation(StringResources.StatusOffline, brushes.Neutral),
        };
    }
}
```

In `MainViewModel`, provide the brushes at the call site:

```csharp
private StatusBrushes CurrentStatusBrushes =>
    new(NeutralBrush, SuccessBrush, WarningBrush, ErrorBrush);
```

During implementation, move the existing pure methods from `MainViewModel.Formatting.cs` and `MainViewModel.StatusFormatting.cs` into `StatusPresenter`. Keep methods that set bindable properties in `MainViewModel`. Remove the private nested `StatusPresentation` record from `MainViewModel.StatusFormatting.cs` after the new shared record compiles.

- [x] **Step 4: Keep mutation in MainViewModel**

`MainViewModel.Status.cs` should continue to own:

```text
- event handlers
- setting bindable properties
- telemetry logging
- dispatching to the UI thread
```

`StatusPresenter` should own:

```text
- text/brush/mode calculation from input snapshots and states
- no field mutation
- no App.Configuration reads
- no App.MainWindow reads
```

- [x] **Step 5: Add guardrails**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$fields = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Fields.cs') -Raw
if ($fields -match 'CreateBrush\(|new SolidColorBrush\(Color\.FromArgb') {
    throw 'MainViewModel must use theme-aware status brush resources.'
}

$presenter = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/StatusPresenter.cs') -Raw
if ($presenter -match 'App\.Configuration|App\.MainWindow|SetProperty\(') {
    throw 'StatusPresenter must stay pure presentation logic.'
}
```

- [x] **Step 6: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/ViewModels/StatusPresenter.cs src/OpenClaw/ViewModels/MainViewModel.Fields.cs src/OpenClaw/ViewModels/MainViewModel.Formatting.cs src/OpenClaw/ViewModels/MainViewModel.StatusFormatting.cs src/OpenClaw/ViewModels/MainViewModel.Heartbeat.cs src/OpenClaw/ViewModels/MainViewModel.Indicators.cs src/OpenClaw/ViewModels/MainViewModel.RunIndicators.cs src/OpenClaw/Styles/StatusResources.xaml tools/verify-repo-structure.ps1
git commit -m "refactor: extract status presentation and resource brushes"
```

---

### Task 7C2: Narrow MainViewModel Service Surface

**Files:**
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Core.Properties.cs`
- Modify: `src/OpenClaw/MainWindow.WebView.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Audit current external consumers**

Current external consumers are expected to be `MainWindow` partials only:

```text
ViewModel.Coordinator
ViewModel.WebViewService
ViewModel.HostedUiBridge
```

Search before editing:

```powershell
rg -n "ViewModel\.(WebViewService|HostedUiBridge|Coordinator)" src/OpenClaw -g "*.cs" -g "*.xaml"
```

- [x] **Step 2: Replace service property access with narrow methods where simple**

For instrumentation updates currently using `ViewModel.Coordinator`, add a ViewModel method:

```csharp
public void UpdateShellInstrumentation(
    string lastInstrumentationEvent,
    int? totalWebViewRecreations = null,
    int? mergedWebViewRecreationRequests = null)
{
    _coordinator?.UpdateInstrumentation(
        totalWebViewRecreations: totalWebViewRecreations,
        mergedWebViewRecreationRequests: mergedWebViewRecreationRequests,
        totalControlUiInspectionRequests: _webViewService.TotalControlUiInspectionRequests,
        cachedControlUiInspectionRequests: _webViewService.CachedControlUiInspectionRequests,
        coalescedControlUiInspectionRequests: _webViewService.CoalescedControlUiInspectionRequests,
        deferredSaveRequests: App.Configuration.DeferredSaveRequests,
        deferredSaveCoalescedRequests: App.Configuration.DeferredSaveCoalescedRequests,
        heartbeatRecoveryRequests: _webViewService.HeartbeatRecoveryRequests,
        lastInstrumentationEvent: lastInstrumentationEvent);
}
```

Use this method from `MainWindow.WebView.cs` instead of reaching into `Coordinator`.

- [x] **Step 3: Narrow remaining service properties**

If `MainWindow` still needs a service for control binding or initialization, change public properties to `internal`:

```csharp
internal WebViewService WebViewService => _webViewService;
internal HostedUiBridge HostedUiBridge => _hostedUiBridge;
internal ShellSessionCoordinator? Coordinator => _coordinator;
```

Do not make them public unless a XAML binding requires it. Current `MainWindow.xaml` does not bind these service properties.

- [x] **Step 4: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$coreProperties = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Core.Properties.cs') -Raw
if ($coreProperties -match 'public WebViewService|public HostedUiBridge|public ShellSessionCoordinator') {
    throw 'MainViewModel service properties must not be public.'
}
```

- [x] **Step 5: Run verification**

Run full verification from Task 1.

Final commit is deferred to Task 16 per the 2026-05-24 execution note. When committing at the end, include these files:

```powershell
git add src/OpenClaw/ViewModels/MainViewModel.Core.Properties.cs src/OpenClaw/MainWindow.WebView.cs tools/verify-repo-structure.ps1
```

---

### Task 7C3: Localize Circuit Breaker Error Text

**Files:**
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Commands.cs`
- Modify: `src/OpenClaw/Strings/en-us/Resources.resw`
- Modify: `src/OpenClaw/Strings/zh-cn/Resources.resw`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Add resource keys**

Add to `Resources.resw` English:

```xml
<data name="CircuitBreakerRecreationSuppressed" xml:space="preserve">
  <value>WebView recovery is temporarily paused after repeated failures. Please wait a moment before retrying.</value>
</data>
```

Add to `Resources.resw` Chinese:

```xml
<data name="CircuitBreakerRecreationSuppressed" xml:space="preserve">
  <value>WebView 连续恢复失败后已暂时暂停自动恢复。请稍后再重试。</value>
</data>
```

Current implementation uses the same key with the Chinese value `WebView 连续恢复失败后已暂时暂停自动恢复。请稍后再重试。`.

- [x] **Step 2: Replace hardcoded message**

In `ShowCircuitBreakerError()`, replace the hardcoded English message with:

```csharp
ErrorMessage = StringResources.CircuitBreakerRecreationSuppressed;
```

Keep existing visibility/retry-button behavior unchanged.

- [x] **Step 3: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$commands = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Commands.cs') -Raw
if ($commands -match 'WebView recovery is temporarily paused') {
    throw 'Circuit breaker user-facing text must come from StringResources.'
}

foreach ($resourceFile in @(
    'src/OpenClaw/Strings/en-us/Resources.resw',
    'src/OpenClaw/Strings/zh-cn/Resources.resw'
)) {
    $resources = Get-Content -LiteralPath (Join-Path $repoRoot $resourceFile) -Raw
    if ($resources -notmatch 'name="CircuitBreakerRecreationSuppressed"') {
        throw "Missing localized circuit breaker resource: $resourceFile"
    }
}
```

- [x] **Step 4: Run verification**

Run full verification from Task 1.

Final commit is deferred to Task 16 per the 2026-05-24 execution note. When committing at the end, include these files:

```powershell
git add src/OpenClaw/ViewModels/MainViewModel.Commands.cs src/OpenClaw/Strings/en-us/Resources.resw src/OpenClaw/Strings/zh-cn/Resources.resw tools/verify-repo-structure.ps1
```

---

### Task 7D: Inject Logger Into HostedUiBridge

**Files:**
- Modify: `src/OpenClaw/Services/HostedUiBridge.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Fields.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Add constructor logger dependency**

In `HostedUiBridge.cs`, add a constructor and logger field:

```csharp
private readonly IAppLogger _logger;

public HostedUiBridge(IAppLogger logger)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
}
```

Replace every `App.Logger.Info`, `App.Logger.Warning`, and `App.Logger.Error` inside `HostedUiBridge.cs` with `_logger.Info`, `_logger.Warning`, and `_logger.Error`.

- [x] **Step 2: Construct the bridge from the ViewModel logger**

In `MainViewModel.Fields.cs`, replace the field initializer:

```csharp
private readonly HostedUiBridge _hostedUiBridge = new();
```

with:

```csharp
private readonly HostedUiBridge _hostedUiBridge;
```

Then initialize it in the `MainViewModel(AppRuntimeContext runtime, Func<Action, bool> dispatchToUi)` constructor that Task 7B owns:

```csharp
_hostedUiBridge = new HostedUiBridge(runtime.Logger, _messageOwnership);
```

- [x] **Step 3: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$hostedBridge = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.cs') -Raw
if ($hostedBridge -match 'App\.Logger') {
    throw 'HostedUiBridge must use injected IAppLogger, not App.Logger.'
}
```

- [x] **Step 4: Run verification**

Run full verification from Task 1.

Final commit is deferred to Task 16 per the 2026-05-24 execution note. When committing at the end, include these files:

```powershell
git add src/OpenClaw/Services/HostedUiBridge.cs src/OpenClaw/ViewModels/MainViewModel.Fields.cs tools/verify-repo-structure.ps1
```

---

### Task 8: Make Log Viewer Loading Cancellable

**Files:**
- Modify: `src/OpenClaw/Views/LogViewerDialog.xaml.cs`
- Modify: `src/OpenClaw.Core/Helpers/LogFileUtilities.cs`

- [x] **Step 1: Add dialog cancellation**

Add fields to `LogViewerDialog`:

```csharp
private CancellationTokenSource? _loadCts;
```

Add cleanup:

```csharp
private void CancelPendingLoad()
{
    _loadCts?.Cancel();
}
```

Subscribe `Closed` and cancel:

```csharp
Closed += (_, _) => CancelPendingLoad();
```

- [x] **Step 2: Guard refresh ownership**

Change `LoadTodayLogAsync` to create one owner token per refresh and to ignore cancellation without showing an error:

```csharp
private async Task LoadTodayLogAsync()
{
    CancelPendingLoad();
    _loadCts = new CancellationTokenSource();
    var token = _loadCts.Token;

    try
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var logFile = Path.Combine(_logDirectory, $"openclaw-{today}.log");
        LogFileLabel.Text = string.Format(StringResources.LogFileLabelFormat, $"openclaw-{today}.log");

        if (!File.Exists(logFile))
        {
            token.ThrowIfCancellationRequested();
            LogContent.Text = StringResources.LogNotFoundToday;
            return;
        }

        var tail = await Task.Run(
            () => LogFileUtilities.ReadLastLines(logFile, LogFileUtilities.DefaultTailLineCount, token),
            token);

        token.ThrowIfCancellationRequested();

        var content = string.Join(Environment.NewLine, tail.Lines);
        LogContent.Text = tail.WasTruncated
            ? string.Format(StringResources.LogShowingLastLinesFormat, tail.TotalLineCount) + Environment.NewLine + Environment.NewLine + content
            : content;
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
        LogContent.Text = string.Format(StringResources.LogReadFailedFormat, ex.Message);
    }
}
```

- [x] **Step 3: Add cancellation to log utility**

Add overload:

```csharp
public static LogTailResult ReadLastLines(string path, int maxLines, CancellationToken cancellationToken)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentOutOfRangeException.ThrowIfNegative(maxLines);
    cancellationToken.ThrowIfCancellationRequested();

    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096);
    if (stream.Length == 0)
    {
        return new LogTailResult([], 0);
    }

    stream.Seek(0, SeekOrigin.End);
    var lastByte = ReadByteAt(stream, stream.Length - 1);
    var buffer = new byte[4096];
    var tailBytes = new List<byte>();
    var position = stream.Length;
    var totalNewLines = 0;
    var capturedNewLines = 0;
    var shouldCaptureTail = maxLines > 0;

    while (position > 0)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var readSize = (int)Math.Min(buffer.Length, position);
        position -= readSize;
        stream.Seek(position, SeekOrigin.Begin);
        var bytesRead = stream.Read(buffer, 0, readSize);

        for (var i = bytesRead - 1; i >= 0; i--)
        {
            var value = buffer[i];
            if (value == (byte)'\n')
            {
                totalNewLines++;
                if (shouldCaptureTail)
                {
                    capturedNewLines++;
                    if (capturedNewLines > maxLines)
                    {
                        shouldCaptureTail = false;
                        continue;
                    }
                }
            }

            if (shouldCaptureTail)
            {
                tailBytes.Add(value);
            }
        }
    }

    var totalLineCount = totalNewLines + (lastByte == (byte)'\n' ? 0 : 1);
    if (maxLines == 0)
    {
        return new LogTailResult([], totalLineCount);
    }

    tailBytes.Reverse();
    var text = Encoding.UTF8.GetString(tailBytes.ToArray()).Replace("\r\n", "\n");
    var lines = text.Split('\n').ToList();
    if (lines.Count > 0 && lines[^1].Length == 0)
    {
        lines.RemoveAt(lines.Count - 1);
    }

    if (lines.Count > maxLines)
    {
        lines = lines.Skip(lines.Count - maxLines).ToList();
    }

    return new LogTailResult(lines, totalLineCount);
}
```

Keep the existing overload:

```csharp
public static LogTailResult ReadLastLines(string path, int maxLines)
{
    return ReadLastLines(path, maxLines, CancellationToken.None);
}
```

- [x] **Step 4: Run verification**

Run full verification from Task 1.

Final commit is deferred to Task 16 per the 2026-05-24 execution note. When committing at the end, include these files:

```powershell
git add src/OpenClaw/Views/LogViewerDialog.xaml.cs src/OpenClaw.Core/Helpers/LogFileUtilities.cs
```

---

### Task 9: Update README Architecture And Current Limitations

**Files:**
- Modify: `README.md`
- Modify: `readme_zh.md`
- Modify: `DEVELOPMENT_NOTES.md`
- Modify: `changelog.md`
- Modify: `docs/code-style.md`

- [x] **Step 1: Replace the outdated README architecture diagram**

Use this English diagram:

```text
OpenClaw Manager
|- MainWindow (WinUI shell: XAML, WebView2 control swap, tray/window integration)
|  |- WebViewRecreationService
|  |- LiveShellSettingsApplier
|  |- SettingsDialog / SettingsPersistenceAdapter
|  `- MainViewModel (orchestration and bindable state)
|     |- StatusPresenter
|     |- WebViewService
|     |  |- WebViewStatusInspector
|     |  |- HeartbeatRuntime
|     |  |- WebViewGenerationTracker
|     |  `- WebView command JS assets
|     |- HostedUiBridge
|     |  `- embedded bridge JS assets
|     |- ShellSessionCoordinator adapters
|     `- ControlUiLatencyService
`- OpenClaw.Core
   |- settings/configuration models
   |- recovery policy/state machine
   |- diagnostics/log utilities
   `- parser/policy helpers
```

Use the equivalent Chinese diagram in `readme_zh.md`.

- [x] **Step 2: Add active verification section**

State that `tests/` is intentionally absent at this checkpoint and list the active verification commands from Task 1.

- [x] **Step 3: Reconcile development notes**

Add a short section near the top of `DEVELOPMENT_NOTES.md`:

```markdown
## Active Verification After Test Harness Removal

Older notes mention regression tests that existed in previous checkpoints. Current active verification is:

- solution restore/build/format
- repository structure guardrails
- bridge script behavior checks
- whitespace diff checks
- VS2026 manual debug on real WebView2/Gateway behavior

When a note says "regression coverage now checks", read it as historical context unless the current verification section lists an active command for it.
```

- [x] **Step 4: Update changelog**

Add a new unreleased entry:

```markdown
### Unreleased

- Planned a second-pass architecture hardening pass after reviewing the v3.3.6 cleanup against README and development-note commitments.
- Documented the no-`tests/` verification replacement: repo-structure guardrails and bridge script checks.
```

Also document current limitations in README/development notes: no active C# harness, bridge script behavior covered by `tools\verify-bridge-scripts.ps1`, and VS2026 manual debug coverage still required for real Gateway/Cloudflare Tunnel behavior.

- [x] **Step 5: Run verification**

Run full verification from Task 1.

Final commit is deferred to Task 16 per the 2026-05-24 execution note. When committing at the end, include these files:

```powershell
git add README.md readme_zh.md DEVELOPMENT_NOTES.md changelog.md docs/code-style.md
```

---

### Task 11: Move WebView Status Inspection Script To An Embedded Asset

**Files:**
- Create: `src/OpenClaw/Services/WebViewStatusInspector.Inspect.js`
- Create: `src/OpenClaw/Services/WebViewStatusInspectionScripts.cs`
- Modify: `src/OpenClaw/Services/WebViewStatusInspector.cs`
- Modify: `src/OpenClaw/OpenClaw.csproj`
- Modify: `tools/verify-repo-structure.ps1`

This task originally belonged to the next refactor wave, but it was pulled into the current dirty batch before final Task 16 completion so status inspection could follow the same embedded-asset and guardrail pattern as the hosted bridge scripts.

- [x] **Step 1: Create the inspect script asset**

Move the current inline `const string script = """..."""` body from `WebViewStatusInspector.ExecuteControlUiInspectionAsync` into `src/OpenClaw/Services/WebViewStatusInspector.Inspect.js`:

```javascript
(() => {
  if (!window.__openClawHostBridge || typeof window.__openClawHostBridge.inspect !== 'function') {
    return JSON.stringify({
      kind: 'openclaw-control-ui-status',
      phase: 'unavailable',
      summary: 'Control UI bridge unavailable.',
      detail: '',
      url: window.location ? window.location.href : '',
      shellDetected: false
    });
  }

  return JSON.stringify(window.__openClawHostBridge.inspect());
})()
```

- [x] **Step 2: Add a resource loader**

Create `WebViewStatusInspectionScripts.cs`:

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal static class WebViewStatusInspectionScripts
{
    private const string InspectResourceName = "OpenClaw.Services.WebViewStatusInspector.Inspect.js";
    private static readonly Lazy<string> InspectScript = new(() => Load(InspectResourceName));

    public static string Inspect => InspectScript.Value;

    private static string Load(string resourceName)
    {
        var assembly = typeof(WebViewStatusInspectionScripts).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded WebView status inspection script: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [x] **Step 3: Replace the inline script**

In `WebViewStatusInspector.ExecuteControlUiInspectionAsync`, remove the local raw string and call:

```csharp
var rawResult = await coreWebView.ExecuteScriptAsync(WebViewStatusInspectionScripts.Inspect);
```

Keep the existing cancellation and generation checks before and after `ExecuteScriptAsync`.

- [x] **Step 4: Register the embedded resource**

Add to `src/OpenClaw/OpenClaw.csproj`:

```xml
<EmbeddedResource Include="Services\WebViewStatusInspector.Inspect.js" LogicalName="OpenClaw.Services.WebViewStatusInspector.Inspect.js" />
```

- [x] **Step 5: Add guardrails and verify**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$statusInspector = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewStatusInspector.cs') -Raw
if ($statusInspector -match 'const string script = """|ExecuteScriptAsync\(@"|ExecuteScriptAsync\(\$@"') {
    throw 'WebViewStatusInspector must load browser scripts from embedded JS assets.'
}

if ($project -notmatch [regex]::Escape('WebViewStatusInspector.Inspect.js')) {
    throw 'Missing embedded WebView status inspection script resource.'
}
```

Run the full verification command set from Task 16.

---

### Task 12: Split HostedUiBridge Status Inspection Into Focused Assets

**Files:**
- Create: `src/OpenClaw/Services/HostedUiBridge.DomUtilities.js`
- Create: `src/OpenClaw/Services/HostedUiBridge.ModelDomFallback.js`
- Create: `src/OpenClaw/Services/HostedUiBridge.ActivityState.js`
- Create: `src/OpenClaw/Services/HostedUiBridge.PhaseClassifier.js`
- Modify: `src/OpenClaw/Services/HostedUiBridge.StatusInspection.js`
- Modify: `src/OpenClaw/Services/HostedUiBridge.Script.cs`
- Modify: `src/OpenClaw/OpenClaw.csproj`
- Modify: `tools/verify-bridge-scripts.ps1`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Extract DOM utilities**

Move these functions from `HostedUiBridge.StatusInspection.js` to `HostedUiBridge.DomUtilities.js`: `isVisible`, `textOf`, `labelOf`, `isEditableElement`, `compactText`, and simple scalar/path helpers. Expose:

```javascript
const openClawDomUtilities = (() => {
  return { isVisible, textOf, labelOf, isEditableElement, compactText, readPath, readScalarText };
})();
```

- [x] **Step 2: Extract MODEL DOM fallback**

Move DOM MODEL fallback logic to `HostedUiBridge.ModelDomFallback.js`, keeping app-state resolution in `HostedUiBridge.ModelResolver.js`:

```javascript
const openClawModelDomFallback = (() => {
  const createReader = ({ dom, mutationFilter }) => {
    const readOpenClawModelSelect = () => emptyModelResult();
    const readModelFromDomCandidates = () => emptyModelResult();
    return { readOpenClawModelSelect, readModelFromDomCandidates };
  };

  return { createReader };
})();
```

During implementation, replace the stub bodies above with the existing moved code; do not change selector behavior in the same commit.

- [x] **Step 3: Extract activity/stale-busy state**

Move `readChatActivitySignature`, `collectDomActivitySignature`, stale threshold state, focused-input state, and `applyBusyStaleness` into `HostedUiBridge.ActivityState.js`:

```javascript
const openClawActivityState = (() => {
  const createActivityTracker = ({ dom, mutationFilter }) => {
    return { applyBusyStaleness };
  };

  return { createActivityTracker };
})();
```

- [x] **Step 4: Extract Gateway/auth phase classification**

Move text matching and final phase selection into `HostedUiBridge.PhaseClassifier.js`:

```javascript
const openClawPhaseClassifier = (() => {
  const classify = ({ text, appStateStatus, strings }) => {
    return {
      phase: appStateStatus.phase,
      summary: appStateStatus.summary,
      detail: appStateStatus.detail
    };
  };

  return { classify };
})();
```

Replace the placeholder return with the existing phase-classification branches during implementation.

- [x] **Step 5: Keep `StatusInspection.js` as the composition owner**

After extraction, `HostedUiBridge.StatusInspection.js` should create the inspector by composing the new modules and stay under 300 lines:

```javascript
const openClawStatusInspection = (() => {
  const createInspector = ({ strings, mutationFilter, modelResolver, statusKind }) => {
    const dom = openClawDomUtilities;
    const modelFallback = openClawModelDomFallback.createReader({ dom, mutationFilter });
    const activity = openClawActivityState.createActivityTracker({ dom, mutationFilter });

    const inspectControlUi = () => {
      // Compose app-state status, model fallback, activity state, and phase classification.
    };

    return { inspectControlUi, isEditableElement: dom.isEditableElement, compactText: dom.compactText };
  };

  return { createInspector };
})();
```

- [x] **Step 6: Register and verify assets**

Update `HostedUiBridge.Script.cs` to load the new assets before `HostedUiBridge.StatusInspection.js`, add them to `OpenClaw.csproj`, extend `tools/verify-bridge-scripts.ps1` with at least one behavior check per new asset, and add a guardrail that rejects `HostedUiBridge.StatusInspection.js` above 300 lines.

Verification run for this task:

```powershell
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:OPENCLAW_NODE='C:\Users\Zen\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'; powershell -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
```

---

### Task 13: Split Heartbeat Policy From HTTP Transport Probing

**Files:**
- Create: `src/OpenClaw/Services/GatewayHeartbeatTransport.cs`
- Create: `src/OpenClaw/Services/HostedSessionHeartbeatPolicy.cs`
- Modify: `src/OpenClaw/Services/WebViewService.Heartbeat.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Extract HTTP transport probing**

Create `GatewayHeartbeatTransport` and move `HeartbeatHttpClient` plus `ProbeGatewayTransportAsync` into it:

```csharp
internal sealed class GatewayHeartbeatTransport
{
    private static readonly HttpClient HeartbeatHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<HeartbeatProbeResult> ProbeAsync(string url, CancellationToken token)
    {
        // Move the current ProbeGatewayTransportAsync body here unchanged.
    }
}
```

- [x] **Step 2: Extract hosted-session policy**

Create `HostedSessionHeartbeatPolicy` for mapping `ControlUiProbeSnapshot` to `HeartbeatProbeResult`:

```csharp
internal sealed class HostedSessionHeartbeatPolicy
{
    public HeartbeatProbeResult? Map(ControlUiProbeSnapshot snapshot)
    {
        return snapshot.Phase switch
        {
            ControlUiPhase.Connected => HeartbeatProbeResult.Healthy("Hosted Control UI reports an active Gateway session."),
            ControlUiPhase.AuthRequired or ControlUiPhase.PairingRequired or ControlUiPhase.OriginRejected =>
                HeartbeatProbeResult.SessionBlocked(snapshot.DetailOrSummary),
            ControlUiPhase.PageLoaded or ControlUiPhase.GatewayConnecting =>
                HeartbeatProbeResult.Connecting("Hosted Control UI is still reconnecting to the Gateway."),
            ControlUiPhase.GatewayError => HeartbeatProbeResult.Failure(snapshot.DetailOrSummary),
            _ => null,
        };
    }
}
```

- [x] **Step 3: Wire into `WebViewService.Heartbeat.cs`**

Add readonly fields in `WebViewService`:

```csharp
private readonly GatewayHeartbeatTransport _heartbeatTransport;
private readonly HostedSessionHeartbeatPolicy _hostedSessionHeartbeatPolicy;
```

Initialize them in the constructor and reduce `WebViewService.Heartbeat.cs` to loop orchestration plus threshold state.

- [x] **Step 4: Add guardrails and verify**

Reject `HttpClient` and direct phase-mapping strings in `WebViewService.Heartbeat.cs`:

```powershell
$heartbeat = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Heartbeat.cs') -Raw
if ($heartbeat -match 'new\(\) \{ Timeout = TimeSpan\.FromSeconds\(10\) \}|HttpClient') {
    throw 'Heartbeat HTTP transport must live in GatewayHeartbeatTransport.'
}
```

Run the full verification command set from Task 16.

---

### Task 14: Extract Settings Persistence From SettingsViewModel

**Files:**
- Create: `src/OpenClaw/Services/SettingsPersistenceAdapter.cs`
- Modify: `src/OpenClaw/ViewModels/SettingsViewModel.cs`
- Modify: `src/OpenClaw/Views/SettingsDialog.xaml.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Create an app-layer persistence adapter**

Create:

```csharp
internal sealed class SettingsPersistenceAdapter
{
    public AppSettings Current => App.Configuration.Settings;

    public void Save()
    {
        App.Configuration.Save();
    }

    public EnvironmentConfig? GetSelectedEnvironment()
    {
        return App.Configuration.GetSelectedEnvironment();
    }
}
```

This is an app-layer adapter, not a Core type, because it intentionally owns the `App.Configuration` boundary.

- [x] **Step 2: Inject the adapter into `SettingsViewModel`**

Change the constructor to accept the adapter:

```csharp
private readonly SettingsPersistenceAdapter _settingsPersistence;

public SettingsViewModel(SettingsPersistenceAdapter settingsPersistence)
{
    _settingsPersistence = settingsPersistence;
    LoadFromSettings(_settingsPersistence.Current);
}
```

Move field initializers that currently read `App.Configuration.Settings` into `LoadFromSettings(AppSettings settings)` so construction has one read boundary.

- [x] **Step 3: Replace direct config mutation**

Inside `SaveAll`, write to:

```csharp
var settings = _settingsPersistence.Current;
settings.Environments = [.. Environments];
settings.SelectedEnvironmentName = persistedSelection;
settings.AppLanguage = SelectedLanguage;
settings.MinimizeToTray = MinimizeToTray;
settings.CloseToTray = CloseToTray;
settings.AllowMultipleInstances = AllowMultipleInstances;
settings.EnableGlobalHotkey = EnableGlobalHotkey;
settings.GlobalHotkey = GlobalHotkey.Trim();
settings.AlwaysOnTop = AlwaysOnTop;
settings.Diagnostics.EnableVerboseRecoveryLogging = EnableDevLog;
_settingsPersistence.Save();
```

- [x] **Step 4: Wire dialog construction and guardrails**

Construct `SettingsViewModel` with `new SettingsPersistenceAdapter()` from the Settings dialog. Add a guardrail that rejects `App.Configuration` in `SettingsViewModel.cs` after the adapter extraction.

Run the full verification command set from Task 16.

---

### Task 15: Add A Narrow App Runtime Facade For MainViewModel

**Files:**
- Create: `src/OpenClaw/Abstractions/AppRuntimeContext.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Commands.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Lifecycle.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Status.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Define the facade**

Create:

```csharp
internal sealed class AppRuntimeContext
{
    public AppRuntimeContext(IAppLogger logger, ConfigurationService configuration)
    {
        Logger = logger;
        Configuration = configuration;
    }

    public IAppLogger Logger { get; }
    public ConfigurationService Configuration { get; }
}
```

- [x] **Step 2: Pass it into `MainViewModel`**

Change `MainViewModel` construction:

```csharp
public MainViewModel(AppRuntimeContext runtime, Func<Action, bool> dispatchToUi)
{
    _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    _dispatchToUi = dispatchToUi ?? throw new ArgumentNullException(nameof(dispatchToUi));
    _webViewService = new WebViewService(runtime.Logger, _messageOwnership, _dispatchToUi);
    _hostedUiBridge = new HostedUiBridge(runtime.Logger, _messageOwnership);
    InitializeCommands();
    SubscribeToServiceEvents();
    InitializeCoordinator();
    LoadEnvironments();
    UpdateStatusPresentation();
}
```

The app edge must pass the owning window's dispatcher explicitly.

- [x] **Step 3: Replace direct App reads in MainViewModel partials**

Replace `App.Logger` with `_runtime.Logger` and `App.Configuration` with `_runtime.Configuration` in `MainViewModel.Commands.cs`, `MainViewModel.Lifecycle.cs`, `MainViewModel.Environment.cs`, `MainViewModel.Heartbeat.cs`, and `MainViewModel.Status.cs`. `MainViewModel` must not read `App.MainWindow`; the owning window injects UI dispatch.

- [x] **Step 4: Add guardrails and verify**

Add a guardrail that rejects `App.Logger`, `App.Configuration`, and `App.MainWindow` in `MainViewModel*.cs` after this task:

```powershell
$mainViewModelFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels') -File -Filter 'MainViewModel*.cs'
foreach ($file in $mainViewModelFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match 'App\.Logger|App\.Configuration') {
        throw "MainViewModel must use AppRuntimeContext instead of App globals: $($file.Name)"
    }
    if ($content -match 'App\.MainWindow') {
        throw "MainViewModel must use the injected UI dispatcher instead of App.MainWindow: $($file.Name)"
    }
}
```

Run the full verification command set from Task 16.

---

### Task 15A: Remove The Remaining SettingsViewModel App Logger Edge

**Files:**
- Modify: `src/OpenClaw/Services/SettingsPersistenceAdapter.cs`
- Modify: `src/OpenClaw/ViewModels/SettingsViewModel.cs`
- Modify: `tools/verify-repo-structure.ps1`

This is a small follow-up discovered during the 2026-05-24 review. Task 14 removed direct `App.Configuration` access from `SettingsViewModel`, but one `App.Logger.Info("Settings saved.")` call remained in the ViewModel. This task is now complete; the save log lives behind `SettingsPersistenceAdapter`.

- [x] **Step 1: Move save logging into the adapter boundary**

Inject the logger into `SettingsPersistenceAdapter` and keep the default app-edge constructor:

```csharp
internal sealed class SettingsPersistenceAdapter
{
    private readonly IAppLogger _logger;

    public SettingsPersistenceAdapter()
        : this(App.Logger)
    {
    }

    public SettingsPersistenceAdapter(IAppLogger logger)
    {
        _logger = logger;
    }

    public AppSettings Current => App.Configuration.Settings;

    public void Save()
    {
        App.Configuration.Save();
        _logger.Info("Settings saved.");
    }

    public EnvironmentConfig? GetSelectedEnvironment()
    {
        return App.Configuration.GetSelectedEnvironment();
    }
}
```

- [x] **Step 2: Remove direct logging from `SettingsViewModel`**

Delete:

```csharp
App.Logger.Info("Settings saved.");
```

The ViewModel should call only `_settingsPersistence.Save()` for persistence side effects.

- [x] **Step 3: Add a guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
if ($settingsViewModel -match 'App\.Logger') {
    throw 'SettingsViewModel must use SettingsPersistenceAdapter instead of App.Logger.'
}
```

- [x] **Step 4: Verify**

Verification run for this task:

```powershell
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
```

---

### Task 16: Final Integration And VS2026 Debug Checklist

**Files:**
- Modify only files needed for version/changelog if user asks for a version bump after implementation.

- [x] **Step 1: Re-run full machine verification after final review fixes**

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
$env:OPENCLAW_NODE='C:\Users\Zen\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
powershell -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
git diff --check
```

Verification was rerun again after the native owner/page-token message validation, programmatic-navigation page-token invalidation, post-await navigation generation re-check, observed navigation-completed async handling, shell-dialog/session-reset async UI reentry guards, configuration deferred-save worker ownership, WebView recreation detach-before-close path, strict UI-dispatch contract, cancellable WebView recreation/initialization, observed ShellSessionCoordinator fire-and-forget recovery tasks, recovery/status-probe CTS disposal ownership, and the `3.0.0` version metadata update. Restore, x64 Debug build, `dotnet format --verify-no-changes`, repository guardrails, bridge script checks with the Codex runtime Node path, and `git diff --check` all completed successfully.

Verification was rerun again after the status-probe cancel/dispose race fix, native-triggered `session-ready` replay after page-token acceptance, HostedUiBridge document-created script-id removal, and README/development-note/guardrail alignment. Restore, x64 Debug build, `dotnet format --verify-no-changes`, repository guardrails, bridge script checks with the Codex runtime Node path, `git diff --check`, local launch verification, and Debug-output cleanup all completed successfully. The remaining checklist items still require VS2026/manual Gateway validation.

Verification was rerun again after the composed bridge verifier isolation, Cloudflare/Gateway manual checklist expansion, programmatic-navigation id invalidation, hosted command timeout guardrails, and WebView stop/abort command timeout guardrails. Restore, x64 Debug build, `dotnet format --verify-no-changes`, repository guardrails, bridge script checks with the Codex runtime Node path, `git diff --check`, local launch verification, and Debug-output cleanup all completed successfully. Release output folders were preserved. The remaining checklist items still require VS2026/manual Gateway/Cloudflare validation.

Follow-up hardening added post-await current-target checks to hosted bridge command dispatch and WebView stop/abort command scripts. This extends the existing timeout guardrail by rejecting command results that return after the active WebView target has been replaced.

Follow-up stability review found that `ControlUiLatencyService` still had the old background-loop ownership shape: `Stop()` cancelled and disposed timer/CTS resources while the probe task could still be running. The service now mirrors the heartbeat ownership rule: `Stop()` clears current ownership and cancels, the observed probe task logs unexpected failures, and the probe loop disposes its own timer/CTS resources in `finally`. `tools\verify-repo-structure.ps1` now guards this contract.

Follow-up shutdown review found that `SingleInstanceCoordinator.StopAsync()` existed but app shutdown still disposed the coordinator directly. `App.OnMainWindowClosed` now waits for `StopAsync()` before disposing the coordinator, and final logger disposal moved to the app-level close path after the listener shutdown so pipe-listener failures remain observable. `tools\verify-repo-structure.ps1` now guards this contract.

Additional local verification after the single-instance shutdown fix started the current branch Debug executable successfully, launched a second instance, and confirmed the secondary process exited after handing activation to the primary instance. A close-path verification then temporarily backed up `%LOCALAPPDATA%\OpenClaw\settings.json`, set `closeToTray=false`, started the current branch executable, called `CloseMainWindow()`, confirmed the process exited within 10 seconds, and restored the original settings file. This verifies local startup, single-instance handoff, and non-tray shutdown sequencing, but it still does not replace the VS2026 real Gateway/Cloudflare checklist.

Final review follow-up addressed the remaining P2 stale-result risks reported during the 2026-05-24 full review. Hosted bridge commands and WebView stop/abort command scripts now capture the accepted page-ownership version before executing page script and reject results if same-WebView navigation invalidates that page before the await returns. Heartbeat loops now carry a run id so old loops cannot publish observations or trigger recovery after stop/restart. Control UI latency probes now carry a run id, publish under that run guard, and MainViewModel drops latency snapshots whose host no longer matches the selected environment. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, and code-style docs now guard or document these contracts.

Second final-review follow-up closed three remaining gaps found by parallel review. `WebViewStatusInspector` now requires an accepted page version before direct script inspection and before publishing, and cancelled coalesced callers cannot leave a late script result to update current UI state. `WebViewService.StopAsync()` keeps its fallback `CoreWebView2.Stop()` call bound to the original WebView/page target so stale abort or `/stop` script rejection cannot stop a newer page. Compact mode now collapses nonessential fixed-width top-bar segments and nonessential title actions at 480px instead of only shrinking the outer status pill. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, and code-style docs were updated to guard or document these contracts.

Third final-review follow-up tightened WebView auto-retry ownership. Failed navigation completions now delegate retry delay and retry command-start handling to a typed outcome path: started retries suppress the old error publish, stale continuations exit when navigation/generation/WebView ownership changed, and exhausted retries or CoreWebView2 retry command-start failures publish Error instead of leaving Reconnecting visible. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, and code-style docs now guard or document this contract.

Fourth final-review follow-up closed the adjacent manual Retry no-op path. `MainViewModel.OnRetry()` no longer hides the visible error before `WebViewService.RetryNavigation()` confirms that a retry navigation started. If no retryable WebView navigation exists because no URL was loaded or WebView2 is unavailable, the InfoBar remains visible with a localized recovery hint to Reload or switch environments. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, and code-style docs now guard or document this contract.

Additional local verification after the manual Retry no-op follow-up passed restore, x64 Debug build, `dotnet format --verify-no-changes`, repository guardrails, bridge script checks with the Codex runtime Node path, and `git diff --check`. Local launch verification started the current branch Debug executable successfully:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 11929462
Responding = True
```

The process was stopped after verification so Debug outputs could be cleaned again. This proves startup/window creation for the local unpackaged path, but it still does not replace the VS2026 real Gateway/Cloudflare checklist.

Fifth final-review follow-up closed the adjacent no-op Reload path. `WebViewService.Reload()` now returns whether CoreWebView2 accepted the reload command and publishes Error when WebView2 is unavailable. `IShellSessionWebView.ReloadAsync()` carries that result through the UI-dispatched adapter, and `ShellSessionCoordinator` marks recovery failed if a reconnect or hard refresh reload could not actually start. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, and code-style docs now guard or document this contract.

Additional local verification after the no-op Reload follow-up passed restore, x64 Debug build, `dotnet format --verify-no-changes`, repository guardrails, bridge script checks with the Codex runtime Node path, and `git diff --check`. Local launch verification started the current branch Debug executable successfully:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 21563234
Responding = True
```

The process was stopped after verification so Debug outputs could be cleaned again. This proves startup/window creation for the local unpackaged path, but it still does not replace the VS2026 real Gateway/Cloudflare checklist.

Sixth final-review follow-up closed the last review drift between code, guardrails, and documentation. Hosted-session heartbeat now treats `Unavailable` inspection as failure, WebView status inspection timeout/failure snapshots publish only while the generation and accepted page version are still current, hosted command CustomEvent fallback dispatches events but returns unhandled without a real hosted method, connected `session-ready` can emit again when an initially empty MODEL later becomes non-empty, stale-busy recovery is limited to chat/output activity, rapid single-instance relaunch can take over when the old pipe is already stopped, live multiple-instance settings apply to the running coordinator, Settings View Logs closes Settings before opening the log dialog, and compact position restore validates current display work areas. README, Chinese README, changelog, development notes, code-style docs, `tools\verify-repo-structure.ps1`, and `tools\verify-bridge-scripts.ps1` document or guard these contracts. The bridge verifier now requires Node by default and skips only when `OPENCLAW_ALLOW_NODE_SKIP=1` is set explicitly.

Additional local verification after the sixth final-review follow-up passed restore, x64 Debug build, `dotnet format --verify-no-changes`, repository guardrails, bridge script checks with the Codex runtime Node path, and `git diff --check`. Local launch verification started the current branch Debug executable successfully:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 14289262
Responding = True
```

The process was stopped after verification so Debug outputs could be cleaned again. This proves startup/window creation for the local unpackaged path, but it still does not replace the VS2026 real Gateway/Cloudflare checklist.

Seventh final-review follow-up removed the remaining `MainViewModel` fallback to `App.MainWindow`. `MainWindow` now owns dispatcher injection, `MainViewModel` requires a `Func<Action, bool>` dispatcher in its constructor, and repository guardrails reject `App.MainWindow`, nullable dispatchers, and `DispatchThroughMainWindow` in ViewModel code. Development notes, code-style docs, changelog, and this plan now document the stricter UI-dispatch boundary.

Eighth final-review follow-up tightened heartbeat responsiveness. `HeartbeatRuntime.Start()` now schedules the observed loop asynchronously, and `WebViewService.Heartbeat.cs` processes one heartbeat tick immediately when a run starts before entering the `PeriodicTimer` loop. This keeps Gateway/Cloudflare/session state from remaining in a stale waiting state for a full heartbeat interval after foreground resume or recovery without running the first tick inline on the caller/UI thread. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, code-style docs, and this plan now guard or document the contract.

Ninth final-review follow-up tightened the live multiple-instance settings path. Settings save now queues an observed async single-instance preference update instead of synchronously waiting for `SingleInstanceCoordinator.StopAsync()` on the UI save path, while app shutdown still serializes with that path and waits for listener stop before disposal. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, code-style docs, and this plan now guard or document the contract.

Tenth final-review follow-up closed a single-instance lock ownership gap introduced by the observed async live-settings path. `SingleInstanceCoordinator` now uses a named semaphore instead of a named mutex, so shutdown and live multiple-instance stop/start paths can release the cross-process lock from any thread after awaits. The coordinator still keeps the named pipe activation handoff stable, treats legacy same-name lock conflicts as secondary launches, and retries takeover after activation failure. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, code-style docs, and this plan now guard or document the contract.

Additional local verification after the tenth final-review follow-up used a temporary console project that referenced `OpenClaw.Core`, acquired a unique named single-instance semaphore, confirmed a second coordinator could not become primary while the first was alive, disposed the primary from another thread, and confirmed a new coordinator could reacquire the same lock. The temporary project was removed after the check. Fresh restore, x64 Debug build, `dotnet format --verify-no-changes`, repository guardrails, bridge script checks with the Codex runtime Node path, `git diff --check`, and local launch verification also completed successfully. Local launch evidence after the named semaphore change:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 4720278
Responding = True
```

Eleventh final-review follow-up closed the last local review findings from the parallel code review pass. Transient `Unavailable` or `Unknown` status inspections no longer clear the last non-empty native MODEL summary for the same accepted page, and `Unavailable` snapshots now downgrade stale `Connected` shell state to Reconnecting. `SingleInstanceCoordinator.StopAsync()` now drains the named-pipe listener instead of using a two-second best-effort timeout: shutdown cancels the listener, disposes the active pipe server to unblock `WaitForConnectionAsync`, and waits for the listener task before the semaphore is released; `Dispose()` also defensively drains before releasing ownership. Settings View Logs now closes Settings before dispatching the main-window Log Viewer dialog, and the Pin tooltip uses localized resources. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, code-style docs, and this plan now guard or document these contracts.

Additional local verification after the eleventh final-review follow-up passed restore, x64 Debug build, `dotnet format --verify-no-changes`, repository guardrails, bridge script checks with the Codex runtime Node path, `git diff --check`, a focused temporary console check for single-instance listener drain and lock reacquire, and local WinUI launch verification. The temporary console project was removed after the check. Local launch evidence after the follow-up:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 1836746
Responding = True
```

Twelfth final-review follow-up closed the latest read-only review drift. `GatewayHeartbeatTransport` now treats missing Control UI paths (`404`) and rejected heartbeat probes (`405`) as failures rather than healthy transport, while still treating explicit auth/origin approval responses as reachable. `HostedUiBridge.CommandDispatch.js` now returns unhandled for unknown CustomEvent fallback commands, and the bridge verifier includes an unknown-command regression check. Compact-mode documentation now describes the current reduced control/status layout instead of claiming the window shows only a status bar.

Additional local verification after the twelfth final-review follow-up passed restore, x64 Debug build, `dotnet format --verify-no-changes`, repository guardrails, bridge script checks with the Codex runtime Node path, `git diff --check`, and local WinUI launch verification. The first restore attempt was blocked by sandbox network permissions while fetching NuGet repository signatures, then passed after rerunning with approved network access. Local launch verification started the current branch Debug executable successfully:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 2229592
Responding = True
```

The process was stopped after verification so Debug outputs could be cleaned again. No `Debug` directories remained afterward, and Release folders were preserved. This proves startup/window creation for the local unpackaged path, but it still does not replace the VS2026 real Gateway/Cloudflare checklist.

Thirteenth final-review follow-up closed two remaining code-style/runtime ownership findings. `DiagnosticService` no longer reads `App.Logger`; diagnostics runtime version lookup receives `IAppLogger` from `MainViewModel` and logs failures through a stable event key with context. Heartbeat recovery now stops only the current run under the heartbeat run-id gate before publishing `HeartbeatFailed`, preventing an old heartbeat loop from stopping or recovering a newly started run. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, code-style docs, and this plan now guard or document these contracts.

Fourteenth final-review follow-up closed the remaining heartbeat/page-token/recovery cancellation findings. Heartbeat hosted-session inspections now call `InspectControlUiStateAsync(token, publishSnapshot: false)`, so heartbeat failure accounting and recovery own the transition instead of the inspection directly publishing `Unavailable`. `MainViewModel` resource scheduling keeps heartbeat alive for owned `ConnectionState.Reconnecting` plus `ControlUiPhase.Unavailable` hosted-session states. Exhausted native page-token capture now publishes an owned `Unavailable` snapshot instead of leaving the shell around `PageLoaded` / `GatewayConnecting`, and ShellSessionCoordinator recovery inspections pass the active operation cancellation token before deciding reload fallback or completion. Repository guardrails were extended for these contracts and passed after the production fix.

Fifteenth final-review follow-up tightened the remaining ShellSessionCoordinator observed-recovery lifetime. Event-gap, heartbeat-triggered, stale-busy, and foreground-resume recovery work now owns cancellable operation CTS instances. Attach, detach, reset, and dispose paths cancel those pending observed operations before replacing WebView/bridge services. Foreground resume links the ViewModel lifetime token with the coordinator observed-operation token, and recovery inspection helpers pass their active token into `InspectControlUiStateAsync` while rethrowing `OperationCanceledException` instead of turning cancellation into reconnect fallback. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, code-style docs, and this plan now guard or document these contracts.

Sixteenth final-review follow-up tightened the remaining single-instance startup responsiveness issue. Secondary-launch activation requests now use async named-pipe connect/write, activation-failure takeover retries use cancellable async delay under one shared takeover deadline instead of `Thread.Sleep`, and `App.OnLaunched` delegates to a logged async launch boundary before creating the main window. Shutdown still waits for `SingleInstanceCoordinator.StopAsync()` before releasing semaphore ownership. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, code-style docs, and this plan now guard or document these contracts.

Seventeenth final-review follow-up tightened the remaining recovery bridge-command cancellation path. `IShellSessionBridge` now requires the active recovery operation cancellation token for reconnect and soft-resync commands, `ShellSessionCoordinator.Recovery` passes that token from the current operation, the WinUI adapter cancels queued UI-dispatch work with the same token, and `HostedUiBridge.SendCommandAsync` links that token with its bounded command timeout before awaiting WebView2 script execution. This prevents detach/reset/service replacement from leaving old in-page bridge commands queued or running against a newer hosted session. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, code-style docs, and this plan now guard or document the contract.

Eighteenth final-review follow-up tightened the matching recovery reload cancellation path. `IShellSessionWebView.ReloadAsync` now requires the active recovery operation cancellation token, `ShellSessionCoordinator.Recovery` passes that token for reconnect and hard-refresh reloads, and the WinUI adapter uses the same token while dispatching reload to the UI thread. This prevents detach/reset/service replacement from leaving an old recovery reload queued and later running against a newer hosted session. `tools\verify-repo-structure.ps1`, README, Chinese README, changelog, development notes, code-style docs, and this plan now guard or document the contract.

Nineteenth final-review follow-up tightened the recovery reload dispatch implementation. `UiTaskDispatcher` now has a cancellable synchronous `RunAsync<T>(Func<T>, CancellationToken)` overload, and `ShellSessionWebViewAdapter.ReloadAsync` dispatches `WebViewService.Reload` directly through that overload instead of wrapping synchronous WebView2 work in `Task.FromResult` to reach the async cancellation path. `tools\verify-repo-structure.ps1`, changelog, development notes, code-style docs, and this plan now guard or document the contract.

Twentieth final-review follow-up closed the remaining review drift found after the broad refactor pass. Settings save now dirty-merges only fields edited in the open dialog so stale snapshots cannot overwrite live Pin, hotkey, multiple-instance, or environment changes, and same-value two-way binding writes return before marking settings fields dirty. ShellSessionCoordinator public recovery requests link the caller cancellation token into the active recovery operation CTS before queueing inspections, bridge commands, or reloads. Transient `Loading`, `Unavailable`, and `Unknown` status snapshots preserve the last non-empty MODEL for the same accepted page. Compact-mode loading UI is derived from compact state plus the current loading state, and XAML owns the compact-state collapse so loading changes cannot re-show the ring at 480px. README, Chinese README, changelog, development notes, code-style docs, and repository guardrails now document or protect these contracts.

Additional local verification after the twentieth final-review follow-up passed locked restore after rerunning restore with approved NuGet network access, repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build with 0 warnings and 0 errors, and `git diff --check`. Debug output directories generated by the build were removed again, no repository `Debug` directories remained, and Release folders were preserved under `src/OpenClaw/bin/x64/Release`, `src/OpenClaw/obj/x64/Release`, `src/OpenClaw.Core/bin/Release`, and `src/OpenClaw.Core/obj/Release`. This still does not replace the VS2026 real Gateway/Cloudflare checklist.

Twenty-first final-review follow-up tightened the Settings dirty-merge contract after another code review pass. Settings shell/language setters now ignore same-value writes before marking a field dirty, so `x:Bind` two-way initialization or control synchronization cannot turn an unchanged stale dialog snapshot into a dirty field. `SettingsDialog.xaml` also had its sidebar TextBlock indentation normalized and an old non-ASCII separator comment replaced with a plain ASCII comment. Repository guardrails, README, Chinese README, changelog, development notes, code-style docs, and this plan now document or protect the same-value dirty-flag rule.

Additional local verification after the twenty-first final-review follow-up passed locked restore, repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build with 0 warnings and 0 errors, `git diff --check`, and a local launch check of the current branch Debug executable. Launch evidence: `MainWindowTitle = OpenClaw`, `MainWindowHandle = 3540276`, and `Responding = True`. The verification process was stopped afterward, Debug output directories generated by the build were removed again, no repository `Debug` directories remained, and Release folders were preserved under `src/OpenClaw/bin/x64/Release`, `src/OpenClaw/obj/x64/Release`, `src/OpenClaw.Core/bin/Release`, and `src/OpenClaw.Core/obj/Release`. This still does not replace the VS2026 real Gateway/Cloudflare checklist.

Twenty-second final-review follow-up tightened the WebView page-token/session-ready replay cancellation edge. Navigation completion now carries a lease-owned navigation cancellation scope into native page-token capture, page-token retry, and native-triggered `session-ready` replay. Those paths link the leased token with their bounded WebView2 script timeouts, cancel retry delay with the same token, and let reload, detach, or newer navigation cancel and retire old work without disposing tokens still held by bounded retry/replay operations. Repository guardrails, README, Chinese README, changelog, development notes, code-style docs, and this plan now document or protect the current-navigation cancellation contract.

Additional local verification after the twenty-second final-review follow-up passed locked restore with approved NuGet network access, repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build with 0 warnings and 0 errors, `git diff --check`, and a local launch check of the current branch Debug executable. Launch evidence: `MainWindowTitle = OpenClaw`, `MainWindowHandle = 2230432`, and `Responding = True`. The verification process was stopped afterward, Debug output directories generated by the build were removed again, no repository `Debug` directories remained, and Release folders were preserved under `src/OpenClaw/bin/x64/Release`, `src/OpenClaw/obj/x64/Release`, `src/OpenClaw.Core/bin/Release`, and `src/OpenClaw.Core/obj/Release`. This still does not replace the VS2026 real Gateway/Cloudflare checklist.

Twenty-third final-review follow-up moved the navigation cancellation contract from a raw `_retryCts` field to a focused `NavigationCancellationScope`. Page-token retry, native-triggered `session-ready` replay, and auto-retry now acquire leases before using a navigation token. Navigation start, detach, and dispose cancel and retire the old scope, while actual token disposal waits until outstanding bounded retry/replay leases are released. This keeps the previous stale-work cancellation behavior but removes the remaining cancel/dispose race on tokens held by background WebView2 script work. Repository guardrails, README, Chinese README, changelog, development notes, code-style docs, and this plan now protect the lease-owned cancellation rule.

Additional local verification after the twenty-third final-review follow-up passed locked restore with approved NuGet network access, repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build with 0 warnings and 0 errors, `git diff --check`, and a local launch check of the current branch Debug executable. Launch evidence: `MainWindowTitle = OpenClaw`, `MainWindowHandle = 3344640`, and `Responding = True`. The verification process was stopped afterward, Debug output directories generated by the build were removed again, no repository `Debug` directories remained, and Release folders were preserved under `src/OpenClaw/bin/x64/Release`, `src/OpenClaw/obj/x64/Release`, `src/OpenClaw.Core/bin/Release`, and `src/OpenClaw.Core/obj/Release`. This still does not replace the VS2026 real Gateway/Cloudflare checklist.

Twenty-fourth final-review follow-up tightened `NavigationCancellationScope.CancelAndRetire()` itself. Scope retirement now releases the internal cancellation lease in `finally` even when cancellation callbacks throw `AggregateException`, so a callback failure cannot leave a retired navigation scope undisposed or interrupt reload/detach/shutdown cleanup. Repository guardrails, changelog, development notes, code-style docs, and this plan now protect the cancellation-callback failure boundary.

Additional local verification after the twenty-fourth final-review follow-up passed locked restore with approved NuGet network access, repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build with 0 warnings and 0 errors, `git diff --check`, and a local launch check of the current branch Debug executable. Launch evidence: `MainWindowTitle = OpenClaw`, `MainWindowHandle = 2885534`, and `Responding = True`. The verification process was stopped afterward, Debug output directories generated by the build were removed again, no repository `Debug` directories remained, and Release folders were preserved under `src/OpenClaw/bin/x64/Release`, `src/OpenClaw/obj/x64/Release`, `src/OpenClaw.Core/bin/Release`, and `src/OpenClaw.Core/obj/Release`. This still does not replace the VS2026 real Gateway/Cloudflare checklist.

Additional local verification after the eighteenth final-review follow-up passed locked restore after rerunning restore with approved NuGet network access, repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, and x64 Debug build with 0 warnings and 0 errors. Debug output directories generated by the build were removed again, and Release folders were preserved. This still does not replace the VS2026 real Gateway/Cloudflare checklist.

Additional local verification after the seventeenth final-review follow-up passed repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build with 0 warnings and 0 errors, `git diff --check`, and locked restore after rerunning restore with approved NuGet network access. Debug output directories generated by the build were removed again, and Release folders were preserved. This still does not replace the VS2026 real Gateway/Cloudflare checklist.

Additional local verification after the sixteenth final-review follow-up passed repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build, `git diff --check`, and locked restore after rerunning restore with approved NuGet network access. A first local launch/handoff verifier started the current branch Debug executable, observed `MainWindowTitle = OpenClaw`, `MainWindowHandle = 6883308`, `Responding = True`, and confirmed the secondary launch exited after handoff, but the checker then failed on a PowerShell single-object `.Count` mistake. A corrected rerun did not proceed because an existing user process was running from `C:\Users\Zen\Repo\Projects\OpenClaw\OpenClaw.exe`; the process was left running. This leaves VS2026/manual single-instance handoff validation open.

Follow-up review then tightened the same startup path further: the activation-failure takeover now shares one timeout deadline across semaphore creation/opening and ownership acquisition, and retry delays are capped to the remaining budget. This prevents the documented three-second takeover window from becoming two sequential full waits. Repository guardrails, `dotnet format --verify-no-changes`, `git diff --check`, and x64 Debug build passed after the change. One build attempt failed because `dotnet format` and `dotnet build` were started in parallel and both touched the XAML compiler `obj` input file; rerunning build serially passed with 0 warnings and 0 errors.

Additional local verification after the fifteenth final-review follow-up passed locked restore with network access, x64 Debug build, `dotnet format --verify-no-changes`, repository guardrails, bridge script checks with the Codex runtime Node path, and `git diff --check`. Local launch verification started the current branch Debug executable successfully:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 1180988
Responding = True
```

The process was stopped after verification so Debug outputs could be cleaned again. This proves startup/window creation for the local unpackaged path, but it still does not replace the VS2026 real Gateway/Cloudflare checklist.

- [x] **Step 2: Clean Debug outputs generated by verification**

Find Debug directories:

```powershell
Get-ChildItem -Path . -Directory -Recurse -Force -Filter Debug | Where-Object { $_.FullName -notmatch '\\.git(\\|$)' } | Select-Object -ExpandProperty FullName
```

Delete only verified `Debug` output directories. Keep Release folders.

After final review verification, Debug outputs were generated again and cleaned. No `Debug` directories remain under the repository. The remaining Release folders were verified under `src/OpenClaw/bin/x64/Release`, `src/OpenClaw/obj/x64/Release`, `src/OpenClaw.Core/bin/Release`, and `src/OpenClaw.Core/obj/Release`.

- [ ] **Step 3: Run VS2026 manual debug checklist**

Current note: an existing `OpenClaw` instance was detected at `C:\Users\Zen\Repo\Projects\OpenClaw\OpenClaw.exe`. After user approval, that old-path instance was stopped and the current branch Debug build was launched from:

```text
C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
```

Startup evidence: process path matched the current branch build, window title was `OpenClaw`, `MainWindowHandle` was non-zero, and the process was responding. The remaining checklist items still require VS2026/manual Gateway validation.

Additional local launch verification after the `3.0.0` version metadata update started the current branch executable:

```text
C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
```

The launched process reported `MainWindowTitle = OpenClaw`, `MainWindowHandle = 7538506`, `Responding = True`, and a process path matching the current branch Debug output. This proves startup/window creation for the local unpackaged path, but it does not replace the VS2026 Gateway/Cloudflare Tunnel checklist below.

Additional local launch verification after the native owner/page-token and lifetime-cancellation fixes initially hit the single-instance handoff because an old-path process was already running at `C:\Users\Zen\Repo\Projects\OpenClaw\OpenClaw.exe`. That old-path process was stopped for verification, and the current branch Debug executable was launched successfully:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 2032922
Responding = True
```

The process was stopped after verification so Debug outputs could be cleaned.

Additional local launch verification after the programmatic-navigation ownership and WebView detach-before-close follow-up again hit the old-path single-instance blocker at `C:\Users\Zen\Repo\Projects\OpenClaw\OpenClaw.exe`. That old-path process was stopped for verification, and the current branch Debug executable launched successfully:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 1836786
Responding = True
```

The process was stopped after verification so Debug outputs could be cleaned again. Release folders remain under `src/OpenClaw/bin/x64/Release`, `src/OpenClaw/obj/x64/Release`, `src/OpenClaw.Core/bin/Release`, and `src/OpenClaw.Core/obj/Release`.

Additional local launch verification after the observed navigation-completed async boundary started the current branch Debug executable successfully:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 10160880
Responding = True
```

The process was stopped after verification. Debug outputs generated by this verification pass were cleaned again; no repository `Debug` directories remain, and Release folders remain preserved.

Additional local launch verification after the shell-dialog/session-reset async UI reentry guards started the current branch Debug executable successfully:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 9832290
Responding = True
```

The process was stopped after verification so Debug outputs could be cleaned again.

Additional local launch verification after the configuration deferred-save worker ownership fix started the current branch Debug executable successfully:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 2359550
Responding = True
```

The process was stopped after verification so Debug outputs could be cleaned again.

Additional local launch verification after the ViewModel UI-dispatch exception boundary and WebView process-failure navigation-cancellation cleanup started the current branch Debug executable successfully:

```text
Path = C:\Users\Zen\Repo\Codes\Claw_winui3\src\OpenClaw\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenClaw.exe
MainWindowTitle = OpenClaw
MainWindowHandle = 11667740
Responding = True
```

The process was stopped after verification so Debug outputs could be cleaned again. No repository `Debug` directories remain, and Release folders remain preserved.

Twenty-fifth final-review follow-up tightened UI responsiveness paths that were still easy to regress during normal use. `AsyncCommand` now rejects repeated execution while an async command is running, resets command availability in `finally`, observes command failures, and defensively treats a null task result as completed. Diagnostic bundle export now runs log enumeration and zip compression off the UI thread, and inactive WebView2 profile folder deletion runs on a background thread instead of synchronously from Settings.

Additional local verification after the twenty-fifth final-review follow-up passed locked restore after rerunning restore with approved NuGet network access, repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build with 0 warnings and 0 errors, `git diff --check`, and a local launch check of the current branch Debug executable. Launch evidence: `MainWindowTitle = OpenClaw`, `MainWindowHandle = 3606700`, and `Responding = True`. The verification process was stopped afterward, Debug output directories generated by the build were removed again, no repository `Debug` directories remained, and Release folders were preserved under `src/OpenClaw/bin/x64/Release`, `src/OpenClaw/obj/x64/Release`, `src/OpenClaw.Core/bin/Release`, and `src/OpenClaw.Core/obj/Release`. This still does not replace the VS2026 real Gateway/Cloudflare checklist.

Twenty-sixth finalization updated the active refactor-validation branch metadata from `3.0.0` to `3.0.1` in the app project, assembly/file versions, package manifest, application manifest, README, Chinese README, changelog, and this plan. The changelog and README explicitly keep the older `v3.0.1 (2026-04-21)` and `v3.0.0 (2026-04-21)` entries as historical release context rather than rewriting the release sequence.

Additional local verification after the `3.0.1` metadata update passed locked restore, repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build with 0 warnings and 0 errors, `git diff --check`, and a local launch check of the current branch Debug executable. Launch evidence: `MainWindowTitle = OpenClaw`, `MainWindowHandle = 7735362`, and `Responding = True`. The verification process was stopped afterward, Debug output directories generated by the build were removed again, no repository `Debug` directories remained, and Release folders were preserved under `src/OpenClaw/bin/x64/Release`, `src/OpenClaw/obj/x64/Release`, `src/OpenClaw.Core/bin/Release`, and `src/OpenClaw.Core/obj/Release`. This still does not replace the VS2026 real Gateway/Cloudflare checklist.

Twenty-seventh final-review follow-up added a release-metadata guardrail to `tools/verify-repo-structure.ps1`. The guardrail now checks that `OpenClaw.csproj`, assembly/file versions, package manifest, application manifest, README, Chinese README, and changelog all stay aligned at the active `3.0.1` refactor-validation metadata. This makes the final version bump machine-verifiable instead of relying only on manual grep during handoff.

Additional local verification after the release-metadata guardrail passed locked restore after rerunning with approved NuGet network access, repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build with 0 warnings and 0 errors, and `git diff --check`. Debug output directories generated by the build were removed again, no repository `Debug` directories remained, and Release folders were preserved. A direct current-branch launch check was not repeated in this pass because an older OpenClaw instance from `C:\Users\Zen\Repo\Projects\OpenClaw\OpenClaw.exe` was already running and would interfere with single-instance startup verification; it was not stopped automatically.

Twenty-eighth final-review follow-up tightened Settings UI string ownership. The Control UI URL placeholder now comes from `StringResources` and both English and Chinese resource files instead of a hard-coded XAML literal, the English multiple-instances description was corrected, and `tools/verify-repo-structure.ps1` now rejects reintroducing that hard-coded placeholder or omitting the localized placeholder resource.

Additional local verification after the Settings string ownership follow-up passed locked restore after rerunning with approved NuGet network access, repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build with 0 warnings and 0 errors, and `git diff --check`. Debug output directories generated by the build were removed again, no repository `Debug` directories remained, and the older OpenClaw process from `C:\Users\Zen\Repo\Projects\OpenClaw\OpenClaw.exe` was still left untouched.

Twenty-ninth final-review follow-up added a localization resource-key guardrail. `tools/verify-repo-structure.ps1` now parses the English and Chinese `.resw` files as XML and fails when a `data name` exists in only one locale, so new user-visible strings cannot silently fall back in one language after future Settings, diagnostics, or bridge text changes.

Additional local verification after the localization resource-key guardrail passed locked restore after rerunning with approved NuGet network access, repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build with 0 warnings and 0 errors, and `git diff --check`. Debug output directories generated by the build were removed again, no repository `Debug` directories remained, and Release folders were preserved.

Thirtieth final-review follow-up closed the remaining tray-menu localization gap. `TrayMenuCompactMode` now exists in both English and Chinese resources, `StringResources` exposes typed tray-menu properties, `MainWindow.Tray.cs` no longer uses raw `StringResources.Get(...)` fallback logic for tray menu labels, and `tools/verify-repo-structure.ps1` now rejects missing tray menu resource keys or literal tray fallback strings.

Additional local verification after the thirtieth final-review follow-up passed locked restore after rerunning with approved NuGet network access, repository guardrails, bridge script checks with the Codex runtime Node path, `dotnet format --verify-no-changes`, x64 Debug build with 0 warnings and 0 errors, and `git diff --check`. The tray-menu raw fallback grep returned no matches, TODO/FIXME/HACK/XXX scan returned no matches, Debug output directories generated by the build were removed again, no repository `Debug` directories remained, and Release folders were preserved under `src/OpenClaw/bin/x64/Release`, `src/OpenClaw/obj/x64/Release`, `src/OpenClaw.Core/bin/Release`, and `src/OpenClaw.Core/obj/Release`. This still does not replace the VS2026 real Gateway/Cloudflare checklist.

Thirty-first final-review follow-up targeted the VS2026 startup spinner and `Navigation did not start within 12s` report. `WebViewService` no longer requires `NavigationStarting` before accepting a current `NavigationCompleted`; when the start watchdog is still active and no navigation id has been claimed, completion claims the navigation and logs `navigation.starting.recovered_from_completion`. Completion timeouts now also require an active completion-watchdog id, so a timeout callback already queued before cancellation cannot publish recovery after successful completion. `HostedUiBridge` message handling now uses host generation plus owner/page-token validation instead of CoreWebView2 wrapper reference identity, and `MainViewModel` no longer keeps the central loading overlay visible during `GatewayConnecting`. Repository guardrails, README, Chinese README, changelog, development notes, and code-style docs now document or protect these contracts.

Thirty-second final-review follow-up closed two adjacent stale-navigation windows found by parallel review. Page-token retry exhaustion now publishes `Unavailable` through the captured navigation generation instead of the tracker's current generation, so an old retry cannot downgrade a newer page. `WebViewService.StopAsync()` fallback now cancels navigation watchdogs, status probes, accepted page ownership, and navigation retry/replay cancellation after `CoreWebView2.Stop()`, so a user-stopped load cannot later trigger stale `navigation.start.timeout` or `navigation.completion.timeout` recovery. Repository guardrails, README, Chinese README, changelog, development notes, code-style docs, and this plan now guard or document these contracts.

Thirty-third final-review follow-up tightened the remaining user-visible recovery gaps found by parallel review. Terminal `Unavailable` snapshots now show the error InfoBar while the shell is reconnecting instead of silently changing status text, and `GatewayError` / `Unavailable` snapshots move `ShellSessionCoordinator` out of stale Ready/Healthy recovery projection. If completion-timeout recovery cancelled navigation cancellation ownership but a late successful `NavigationCompleted` is still current, the navigation path recreates cancellation ownership and continues the normal page-token/status-probe flow. WebView recreation exceptions now publish a localized actionable error with Retry instead of only logging after timeout recovery hid the first-hop InfoBar. Repository guardrails, README, Chinese README, changelog, development notes, code-style docs, and this plan now guard or document these contracts.

Thirty-fourth final-review follow-up tightened the stale visible-status and active timeout-recreation edge. Resource stop paths now reset visible heartbeat and latency projection; WebView detach/recreation resets MODEL, access, work, recovery, heartbeat, and latency before a replacement session reports fresh state. `WebViewStatusInspector.SetUnknownSnapshot()` now notifies listeners so detach cannot leave old hosted-session projection visible. Child WebView layout timeouts count against the recreation circuit breaker, and late successful navigation recovery can cancel pending, deferred, or already-active timeout-only WebView recreation before it detaches the recovered host. Unexpected navigation-completion handler failures now publish `Unavailable` plus Error instead of only logging after the completion watchdog has been cancelled. Repository guardrails, bridge script behavior checks, README, Chinese README, changelog, development notes, code-style docs, and this plan now guard or document these contracts.

Thirty-fifth final-review follow-up tightened first-run placeholder diagnostics after direct VS2026 startup verification. `WebViewRecreationService` now exposes whether pending, deferred, or active recreation work exists, and placeholder layout-resume handling clears/logs only when there is real recreation work or a stale WebView2 child. Latest launch evidence showed one `webview.recreation.skipped_placeholder_environment` event for `initial_load` and no `deferred_resume_placeholder`, WebView2 initialization, `example.com` navigation, latency probe, heartbeat, or navigation timeout events in the latest launch window.

Thirty-sixth final-review follow-up completed the next `WebViewStatusInspector` responsibility split. The root inspector partial now keeps shared state, public entry points, and snapshot publication only; direct inspection/coalescing moved to `WebViewStatusInspector.Inspection.cs`, and the post-navigation probe loop moved to `WebViewStatusInspector.Probe.cs`. Repository guardrails and docs now reject moving direct inspection, probe, parsing, or bounded script execution back into the root inspector partial.

Additional verification after the thirty-sixth final-review follow-up passed repository guardrails, bridge script checks with the Codex runtime Node path, x64 Debug build with 0 warnings and 0 errors, `dotnet format --verify-no-changes`, solution configuration validation for Debug/Release across x64/x86/ARM64, and `git diff --check`. A current Debug executable launch also passed the placeholder startup log assertion: one `webview.recreation.skipped_placeholder_environment` event for `initial_load` and no WebView2 initialization, `example.com` navigation, latency probe, heartbeat, `deferred_resume_placeholder`, or navigation-timeout events.

Thirty-seventh final-review follow-up made app-global access boundaries machine-verifiable. `tools/verify-repo-structure.ps1` now allows direct `App.Logger`, `App.Configuration`, and `App.MainWindow` access only at the WinUI app edge (`App.xaml.cs`, `MainWindow` partials, and dialog glue). Runtime services, ViewModels, Core-compatible code, and adapters must continue to use injected or typed dependencies.

Additional verification after the thirty-seventh final-review follow-up passed repository guardrails, bridge script checks with the Codex runtime Node path, x64 Debug build with 0 warnings and 0 errors, `dotnet format --verify-no-changes`, and `git diff --check`.

Thirty-eighth final-review follow-up tightened the Settings/WebView boundary after the VS2026 solution-load check. `tools/verify-repo-structure.ps1` now rejects new `SettingsViewModel` calls into `WebViewService` runtime/session APIs, while preserving the existing static `TryMoveUserDataFolderToRenamedEnvironment` profile-rename migration helper. Code-style and development notes now document that Settings remains a draft/persistence ViewModel and WebView navigation, heartbeat, session, and recreation work stays behind MainViewModel, MainWindow, and focused services.

```text
1. Start app in VS2026 Debug.
2. Confirm selected environment loads.
3. Submit a hosted OpenClaw task and observe output stream.
4. Wait for completion without manual reload.
5. Confirm MODEL field is non-empty after startup, session switch, page reload, and native-triggered `session-ready` replay.
6. Open Settings/Cron/heavy pages and watch WebView2 CPU behavior.
7. Change global hotkey in Settings and confirm it works without restart.
8. Toggle Always on Top in Settings and confirm current window state changes immediately.
9. Enter and exit compact mode at 480px and confirm top bar does not clip.
10. Open log viewer, refresh repeatedly, close during loading, and confirm no UI hang.
11. Use Reload and Stop commands against the hosted UI.
12. Confirm tray show/hide and close-to-tray still work.
13. Confirm tray Compact Mode and Reload menu entries still dispatch to the current window.
14. Relaunch while an instance is already running and confirm single-instance handoff restores the current window instead of starting a second process.
15. Open Settings after toggling Pin/global hotkey outside the dialog and confirm stale prewarmed settings do not overwrite current values.
16. Close the app while compact mode is active, relaunch, exit compact mode, and confirm full-mode window bounds are not restored as 480x120.
17. Simulate or visit Cloudflare/reverse-proxy 5xx pages and unexpected 4xx pages, then confirm heartbeat treats them as failures and recovery runs after the upstream becomes healthy again.
18. Visit auth/approval/origin-rejected Gateway pages and confirm status text maps to auth/origin states instead of connected.
19. Hover the latency tooltip on a real Cloudflare Tunnel response and confirm `cf-ray` / PoP parsing still works.
20. Switch light/dark/system themes and confirm the title-bar/DWM border, including the top 1px edge, stays visually aligned.
```

- [x] **Step 4: Final status check**

```powershell
git status --short --branch -uall
```

- [ ] **Step 5: Commit final integration**

```powershell
git add .
git commit -m "refactor: complete second-pass architecture hardening"
```

---

## Completion Criteria

- `tests/` remains absent unless the user explicitly changes that decision.
- `OpenClaw.Core` remains in the solution and has no WinUI/WebView2 direct dependency.
- `WebViewService` no longer owns status inspection internals or heartbeat loop lifetime directly.
- `HostedUiBridge.Script.js` becomes a composition shell under 250 lines, with focused embedded JS assets for MODEL, status inspection, command dispatch, mutation filtering, and host messaging.
- `WebViewService` and `HostedUiBridge` use injected `IAppLogger` instead of static `App.Logger`.
- Settings live behavior is applied from a typed change pipeline instead of re-reading global config in multiple places.
- Compact mode uses XAML visual states for top-bar layout.
- Documentation clearly distinguishes historical removed harness coverage from active verification.
- `HostedUiBridge.StatusInspection.js` is split below the planned guardrail and remains composition-only.
- `SettingsViewModel` has no direct `App.Configuration` or `App.Logger` access.
- `SingleInstanceCoordinator` uses a named semaphore for cross-process ownership and still waits for named-pipe listener shutdown before disposal.
- Full verification passes and VS2026 manual debug checklist has no blocking regression.

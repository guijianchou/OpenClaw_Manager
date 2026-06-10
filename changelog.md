# Changelog

**Language:** English | [简体中文](#简体中文)

Full release notes for OpenClaw Manager. See [README.md](README.md) / [readme_zh.md](readme_zh.md) for project overview.

---

## English

### v5.0.1 (2026-06-01)

- Updated app, assembly, file, package manifest, application manifest, README, Chinese README, and changelog metadata to `5.0.1`.
- Unified Gateway HTTP status classification across heartbeat, diagnostics, and latency probes for Cloudflare Tunnel / reverse-proxy deployments. Proxy, path, server, and Cloudflare Tunnel 1033 failures now report as failures instead of healthy transport or latency samples.
- Moved Control UI latency probing to the documented `__openclaw__/a2ui/` hosted Control UI path and stopped recording 404/405/5xx/1033 responses in latency history.
- Surfaced Settings persistence failures back to the Settings dialog so file-lock, permission, disk, or atomic-write failures no longer close the dialog as if settings were saved.
- Added repository guardrails that prevent reverting latency probes to the stale `control-ui-config.json` path, prevent unclassified HTTP success publishing, and require Settings write failures to flow through the persistence adapter.

### v5.0.0 (2026-05-29)

- Hardened terminal hosted-session failure projection: `Unavailable` snapshots now show a visible InfoBar while reconnecting, `GatewayError`/`Unavailable` move ShellSessionCoordinator out of stale Ready/Healthy recovery state, and WebView recreation exceptions surface a localized actionable error instead of only logging after timeout recovery hid the InfoBar.
- Hardened late completion-timeout recovery: a still-current successful `NavigationCompleted` that arrives after completion-timeout recovery recreates navigation cancellation ownership and runs the normal page-token/status-probe path instead of returning before cleanup notifications.
- Hardened stale visible status cleanup: WebView detach/recreation and stopped resource probes now reset heartbeat, latency, MODEL, access, work, and shell projections before the replacement session reports state, and unexpected navigation-completion handler failures publish `Unavailable` plus Error instead of leaving Loading stale.
- Hardened WebView recreation retry ownership: child layout timeouts now count against the circuit breaker, and a late recovered navigation can cancel pending, deferred, or already-active timeout-only recreation before it detaches a recovered WebView.
- Treats the default `https://example.com` environment as a first-run placeholder instead of a navigable Control UI target, so startup and placeholder reselection skip WebView2 host creation, stop probes, clear stale WebView host state, and show the localized configure-Gateway status instead of navigating to `example.com` or leaving the loading ring active.
- Hardened the WebView startup stall path: `NavigationCompleted` can now claim a pending navigation only when it matches the target recorded by the start watchdog, start-timeout recovery keeps that target for a bounded late-completion window, late completion cancels any still-pending or deferred timeout-driven WebView recreation, timeout-driven recreation requests cannot overwrite higher-priority settings/initial/session/topology recreation reasons, completion watchdogs require an active watchdog id before publishing timeout recovery, HostedUiBridge message handling uses host-generation plus owner/page-token ownership instead of CoreWebView2 wrapper identity, and the full-window loading ring clears after browser navigation instead of staying visible throughout `GatewayConnecting`.
- Closed two adjacent stale-navigation windows: exhausted page-token retry now publishes `Unavailable` only for the original navigation generation, and Stop fallback cancels navigation watchdogs, probes, page ownership, and navigation cancellation so a stopped load cannot later fire stale startup-timeout recovery.
- Closed the post-navigation half-connected state by publishing an owned terminal `Unavailable` snapshot when the bounded status probe loop never reaches a terminal Control UI phase, and stopping further post-navigation page-script probing after recovery takes ownership.
- Made Control UI issue snapshots surface the visible error InfoBar, so auth, pairing, origin rejection, and Gateway error states do not only change status text while looking like a silent stall.
- Reload now clears stale visible errors only after WebViewService confirms reload navigation started, matching the guarded manual Retry behavior.
- Tightened dynamic WebView startup gating so recreated WebView2 controls must be loaded, visible, and non-zero sized inside a visible non-compact/non-minimized host before initialization and navigation, child layout timeouts are requeued through the normal recreation timer/circuit-breaker path instead of waiting forever for another window event, deferred recreation ownership stays in `WebViewRecreationService`, and layout diagnostics record compact, hidden, minimized, and child-control dimensions.
- Fixed VS2026 solution loading by keeping `OpenClaw.Core` as a platform-independent SDK class library and mapping the solution's `x64`/`x86`/`ARM64` platforms to the Core project's `AnyCPU` configuration.
- Tightened the solution guardrail so `OpenClaw.Core` cannot be converted back to architecture-specific project platforms or mapped to undeclared Core configurations.
- Added a repository guardrail against new synchronous waits in runtime/UI code, while explicitly preserving the existing shutdown drains for settings flush, logger flush, and single-instance listener ownership release.
- Applied saved language preferences through the Windows App SDK `Microsoft.Windows.Globalization.ApplicationLanguages` API so startup no longer logs the previous WinRT language-override warning.
- Split WebView navigation internals into focused partials for event/completion flow, host-message handling, shared navigation ownership/cancellation state, watchdog ownership, CoreWebView2 command wrappers, page-token/session-ready retry, and process-failure/auto-retry recovery.
- Split WebView lifecycle/session operations out of the main service file so `WebViewService.cs` keeps shared state, construction, events, and public navigation commands while lifecycle/current-target and profile/session operations live in dedicated partials.
- Split `WebViewStatusInspector` direct inspection/coalescing, post-navigation probes, parsing, and bounded script execution into focused partials so the main inspector keeps shared state, public entry points, and snapshot publication only.
- Documented the second-pass architecture hardening plan that follows the v3.3.6 cleanup baseline.
- Clarified the active no-`tests/` verification replacement: restore/build/format, repository guardrails, bridge script checks, whitespace checks, and VS2026 manual debug.
- Aligned documentation with the current runtime split: `WebViewStatusInspector`, `HeartbeatRuntime`, `WebViewRecreationService`, split embedded `HostedUiBridge` assets, compact-mode visual states, the live shell settings apply pipeline, and `StatusPresenter`.
- Refreshed the second-pass plan to reflect the completed WebView status inspection asset, heartbeat transport/policy split, settings persistence adapter, and `AppRuntimeContext` work.
- Split `HostedUiBridge.StatusInspection.js` into focused DOM utilities, MODEL DOM fallback, activity/stale-busy, and phase-classification assets, with bridge script verifier coverage.
- Moved the last Settings save log out of `SettingsViewModel` and behind the settings persistence adapter boundary.
- Hardened final stabilization paths: prewarmed Settings dialogs reload current persisted state before activation, compact mode no longer persists 480x120 as normal window bounds, heartbeat hosted-session probes honor cancellation, coalesced WebView status inspections do not let caller cancellation contaminate shared in-flight results, status inspection timeout/failure snapshots are published under current page ownership, localized bridge scripts are composed per WebView initialization, and string resources are invalidated after language override attempts.
- Localized the Settings diagnostic bundle action and added guardrails for settings persistence boundaries, compact bounds saves, and localized bridge-script caching.
- Added a bounded timeout around WebView status script inspection so stalled `ExecuteScriptAsync` calls cannot hold coalesced probes indefinitely, and made status probe task/cancellation ownership explicit.
- Added bounded timeouts and post-await current-target checks around hosted bridge command dispatch and WebView stop/abort command scripts so stalled page promises cannot block native recovery or apply stale command results after WebView replacement.
- Extended hosted bridge and WebView stop/abort command ownership checks to include accepted page versions, so same-WebView navigation cannot let an old document promise report success for the current page.
- Routed ShellSessionCoordinator WebView2 and hosted bridge adapter calls through the UI dispatcher, covering heartbeat-triggered recovery paths that start on background threads.
- Routed the public Control UI inspection entry, WebViewService heartbeat hosted-session inspection, and navigation-after-load status probe loop through the injected UI dispatcher before touching WebView2.
- Classified gateway transport 5xx, missing Control UI 404, rejected heartbeat-probe 405, and unexpected 4xx responses as failures so Cloudflare/reverse-proxy error pages can trigger recovery instead of being treated as healthy transport.
- Tightened the UI-dispatch contract so failed dispatcher enqueue no longer runs WebView2 or WinUI work inline on a background thread.
- Added native owner/page-token validation for hosted bridge messages so stale WebView documents cannot write session/status messages back into the current page state.
- Closed the remaining WebView ownership window by invalidating page tokens and clearing the accepted navigation id before programmatic navigation/reload/retry, re-checking generation after page-token capture awaits, and retrying page-token capture without blocking status probes.
- Requested a native-triggered `session-ready` replay after page-token acceptance so early hosted ready messages rejected by ownership filtering can be delivered without a manual reload.
- Linked page-token capture retry and native-triggered `session-ready` replay to a lease-owned navigation cancellation scope, so reload, detach, or a new navigation cancels stale replay work without disposing tokens still held by bounded retry/replay operations; cancellation callback failures can no longer block scope retirement.
- Removed the status probe cancellation/disposal race by cancelling the active probe CTS before clearing ownership, while preserving probe-owned disposal.
- Tracked and removed the hosted bridge document-created script id on WebView detach so repeated initialization does not accumulate old bridge observers and timers.
- Reload and retry paths now publish an error state when CoreWebView2 becomes unavailable before the command starts, instead of leaving the shell stuck in Loading/Reconnecting.
- Reload now returns whether CoreWebView2 actually accepted the command, and recovery paths treat a no-op reload as failed recovery instead of advancing to Connecting without a real refresh.
- Manual Retry now only hides the visible error after retry navigation actually starts; if no retryable WebView navigation exists, the localized error remains visible with a Reload/environment-switch recovery hint.
- Auto-retry continuations now re-check navigation generation and accepted target before retrying, treat changed navigation as stale, and publish Error when retries are exhausted or the retry command cannot start instead of leaving the shell in Reconnecting or letting old navigation-completed work overwrite current state.
- Added an observed exception boundary around WebView navigation-completed async handling so failed probe startup or recovery notifications are logged instead of escaping an `async void` event handler.
- Detached the coordinator, bridge, and WebView service before closing outgoing WebView2 controls during recreation, and tracked recreation/foreground-resume async work so shutdown does not write through disposed shell state.
- Guarded shell ContentDialog entry points against rapid reentry, logged dialog failures, and made Settings session-reset disable its button while running with localized failure reporting.
- Made configuration deferred-save ownership explicit by storing the worker task/cancellation source, serializing coalesced save versions under one lifetime gate, and cancelling, draining, or observing it before shutdown flushes settings.
- Made WebView recreation and WebView/bridge initialization cancellable during shutdown, replaced ShellSessionCoordinator async event handlers with observed fire-and-forget recovery tasks, and kept recovery/probe cancellation disposal owned by the running operation.
- Hardened Log Viewer cancellation so stale load failures cannot write over a newer refresh or a closing dialog.
- Hardened Control UI latency probe lifetime so stop/restart only cancels the active probe while the observed probe task owns timer/CTS disposal and logs unexpected failures.
- Hardened stale run filtering for heartbeat and latency probes: old heartbeat loops cannot publish observations or trigger recovery after stop/restart, new heartbeat loops run off the caller thread and publish an immediate first observation before waiting for the first periodic interval, and old latency probes are rejected by run id plus selected environment host before updating the UI.
- Removed the remaining `MainViewModel` fallback to `App.MainWindow`; view-model UI dispatch now must be injected by the owning window, and repository guardrails reject reintroducing the global window dependency.
- Hardened the final review findings: WebView status inspections now require an accepted page version before script execution and before publishing, cancelled coalesced inspection callers cannot write UI state after they leave, Stop fallback remains tied to the original WebView/page target, bridge CustomEvent fallback no longer reports soft-resync commands as handled without a real hosted method, non-chat settings/Cron busy state no longer triggers stale-chat recovery, and compact mode collapses nonessential fixed-width top-bar segments at 480px.
- Hardened single-instance shutdown and relaunch behavior so the app waits for the named-pipe listener stop before disposing the coordinator, uses a named semaphore so async live settings and shutdown paths can release the single-instance lock from any thread, applies multiple-instance setting changes to the running coordinator, and observes listener stop/start changes asynchronously so Settings save is not blocked by named-pipe shutdown.
- Hardened the final review follow-ups: transient `Unavailable`/`Unknown` status inspections no longer clear the last non-empty MODEL for the same accepted page, `Unavailable` downgrades stale `Connected` shell state, single-instance shutdown drains the named-pipe listener instead of using a best-effort timeout, Settings View Logs closes Settings before opening the log dialog, and the Pin tooltip uses localized resources.
- Hardened the latest review follow-ups: heartbeat no longer treats missing Control UI paths or rejected heartbeat probes as healthy transport, unknown hosted bridge CustomEvent fallbacks return unhandled, and compact-mode documentation now reflects the current reduced control/status layout.
- Hardened heartbeat/recovery interaction after final review: hosted-session heartbeat inspections no longer publish UI snapshots before heartbeat failure accounting, resource scheduling keeps heartbeat alive for owned `Reconnecting`/`Unavailable` hosted-session states, and stale heartbeat loops cannot stop or recover a newly started run.
- Hardened page-token and recovery cancellation paths: exhausted native page-token capture now publishes an owned `Unavailable` snapshot, and ShellSessionCoordinator recovery inspections carry the active operation cancellation token before deciding reload fallback or recovery completion.
- Hardened ShellSessionCoordinator observed recovery lifetime: event-gap, heartbeat-triggered, stale-busy, and foreground-resume recovery work now owns cancellable operation CTS instances, attach/detach/reset/dispose cancels pending inspections before service replacement, and cancellation is rethrown instead of becoming reconnect fallback.
- Hardened ShellSessionCoordinator bridge-command cancellation: reconnect and soft-resync commands now pass the active recovery operation token through the Core bridge contract, the WinUI UI-dispatch adapter, and `HostedUiBridge` script execution so cancelled or replaced recovery operations do not keep old in-page commands queued or running against a newer session.
- Hardened ShellSessionCoordinator recovery reload cancellation: reconnect and hard-refresh reloads now pass the active recovery operation token through the Core WebView contract and WinUI UI-dispatch adapter so cancelled or replaced recovery operations cannot start a queued reload against a newer session.
- Tightened UI dispatch for recovery reloads by adding a cancellable synchronous dispatcher overload and removing the `Task.FromResult` wrapper around synchronous WebView2 reload work.
- Hardened final-review cleanup: transient `Loading`, `Unavailable`, and `Unknown` snapshots preserve the last non-empty MODEL for the same accepted page; Settings save dirty-merges only edited fields and ignores same-value two-way binding writes so stale dialog snapshots cannot overwrite live Pin/hotkey/environment changes; public recovery requests link caller cancellation into the active operation CTS; and compact-mode loading-ring visibility is derived from compact state plus loading state so it cannot reappear in compact mode.
- Hardened single-instance relaunch responsiveness: secondary-launch activation requests now use async named-pipe connect/write, activation-failure takeover retries use cancellable async delay with one shared takeover deadline instead of `Thread.Sleep`, and the shutdown path still drains the named-pipe listener before releasing the semaphore.
- Hardened final UI/lifecycle cleanup: injected ViewModel UI updates now catch and log callback failures instead of letting dispatcher callbacks escape as unhandled UI-thread exceptions, and WebView process-failure handling retires navigation retry/replay cancellation before publishing the unavailable snapshot.
- Hardened final responsiveness cleanup: long-running async commands now reject repeated execution while running, tolerate null task results defensively, export diagnostic bundles without enumerating logs or compressing zip files on the UI thread, and delete inactive WebView2 profile folders from a background thread.
- Set current app, assembly, file, package manifest, application manifest, README, and changelog metadata to `5.0.0` for this refactor validation branch. The older `v3.0.5 (2026-05-01)`, `v3.0.1 (2026-04-21)`, and `v3.0.0 (2026-04-21)` entries below remain historical release context.
- Added repository guardrails that keep `5.0.0` project, assembly/file, package manifest, application manifest, README, Chinese README, and changelog metadata aligned during finalization.
- Moved the Settings Control UI URL placeholder into localized resources and corrected the multiple-instances description text.
- Added a repository guardrail that requires English and Chinese `.resw` resource keys to stay aligned.
- Moved tray Open, Compact Mode, and Exit labels behind typed `StringResources` properties, added the missing Chinese Compact Mode label, and added guardrails against raw tray-menu resource fallbacks.
- Recorded the remaining finalization target: VS2026 manual Gateway/Cloudflare checklist and final commit. Automated local verification has run, Debug outputs were cleaned after verification, and Release folders are preserved.
- Noted that earlier changelog references to executable regression coverage are historical after the v3.3.6 harness removal.
- Hardened `tools\verify-bridge-scripts.ps1` so Node runner failures fail PowerShell, Node is required by default unless `OPENCLAW_ALLOW_NODE_SKIP=1` is set explicitly, and an isolated full composed-bridge behavior check covers native-triggered `session-ready` replay.
- Expanded the VS2026 manual checklist to include Cloudflare/reverse-proxy 4xx/5xx/auth/origin pages, `cf-ray` PoP parsing, DWM title-bar edges, and single-instance relaunch handoff.

### v3.3.6 (2026-05-21)

- Promoted the v3.3.5 architecture cleanup after VS2026 debug validation.
- Kept the bridge/WebView hardening as the current baseline: embedded hosted bridge assets, event/command path hardening, safe host messaging, and generation-scoped WebView inspection cache reuse.
- Removed the local regression harness from the active solution and repository while keeping `OpenClaw.Core` as the app's shared pure .NET source tree.
- Synced app, assembly, file, package manifest, application manifest, README, and changelog metadata to `3.3.6`.

### v3.3.5 (2026-05-20)

- Added `docs/code-style.md` as the canonical project code-style and architecture guide.
- Centralized top status and status-bar typography, spacing, and layout constants into focused WinUI resource dictionaries under `src/OpenClaw/Styles`.
- Split the executable test harness into focused `Tests.*.cs` domain files and added coverage for code-style documentation, architecture boundaries, and shared top-status XAML resources.
- Split `WebViewService` command-injection, heartbeat, Control UI inspection, and profile-folder helpers into focused partial files.
- Moved all Core-compatible source files into the physical `src/OpenClaw.Core` tree, including the window-bounds policy that was previously linked from the WinUI project.
- Moved the main hosted bridge browser script into embedded JS assets, leaving C# responsible only for resource loading and localized string/model resolver injection.
- Extracted hosted MODEL app-state resolution into an embedded JS asset and added executable regression coverage for defaults, `null` overrides, Map-backed overrides, and object-shaped payloads.
- Added executable hosted bridge event coverage for session-ready metadata, command-dispatch return values, sidebar mutation filtering, safe WebView2 host messaging, and generation-scoped WebView inspection cache reuse.
- Synced app, assembly, file, package manifest, application manifest, README, and regression-test version metadata to `3.3.5`.

### v3.3.4 (2026-05-20)

- Updated the About dialog GitHub profile links and labels to `https://github.com/Guijianchou`.
- Applied Always-on-top and global hotkey changes immediately after Settings save.
- Tightened compact mode top-bar layout so nonessential status segments collapse cleanly at 480px.
- Guarded WebView2 status probes with WebView/navigation generation ownership so stale async script results cannot overwrite current state.
- Made heartbeat loop ownership and log viewer loading explicit: heartbeat owns its timer/task, and log tailing runs off the UI thread.
- Synced app, assembly, file, package manifest, application manifest, README, and regression-test version metadata to `3.3.4`.

### v3.3.3 (2026-05-19)

- Fixed the top MODEL value typography so it uses the same 12px text size as the native status bar.
- Hardened hosted OpenClaw model detection for app-state variants, URL session keys, Map-backed model overrides, and non-string payload normalization.
- Deferred app-state default MODEL fallbacks, including `null` session overrides, so root defaults do not mask later nested active-session models.
- Synced app, assembly, file, package manifest, application manifest, README, and regression-test version metadata to `3.3.3`.

### v3.3.2 (2026-05-19)

- Added stale busy-stream detection for hosted chat sessions: the bridge now tracks chat activity signatures and polls busy connected pages more frequently.
- Added stale-stream recovery escalation: OpenClaw Manager first soft-resyncs lightweight state and recent messages, then performs a hard refresh after the soft-resync budget is exhausted.
- Narrowed the input-focus reload guard so an empty focused editor no longer blocks recovery refreshes, while unsent user text still defers automatic reload.
- Expanded diagnostics with the latest hosted UI phase, busy state, stale duration, and focused-input text state.
- Synced app, assembly, file, package manifest, application manifest, README, and regression-test version metadata to `3.3.2`.

### v3.3.1 (2026-05-17)

- Fixed the status-bar MODEL field so it reads the current model from OpenClaw Web UI's explicit model selector.
- Hardened MODEL detection to read OpenClaw app state when DOM controls are not ready, and preserve the last non-empty model across transient empty snapshots.
- Reduced WebView2 CPU spikes while long right-sidebar content loads by ignoring status-irrelevant sidebar DOM changes and hosted preview frames.
- Expanded the top status pill so long provider/model names retain more context before AUTH/Status indicators, and moved connected OpenClaw settings/cron pages onto an app-state status fast path to avoid DOM mutation storms.
- Documented the current mitigation status after manual testing: MODEL display and WebView2 CPU spikes are improved enough for current use, while very long model names and especially heavy settings/Cron pages remain areas to monitor.
- Synced app, assembly, file, package manifest, application manifest, README, and regression-test version metadata to `3.3.1`.

### v3.3.0 (2026-05-12)

- Refined Settings with PowerToys-style settings rows, compact ToggleSwitch spacing, and localized always-on-top text.
- Reorganized Settings navigation to Language, General, Environments, Sessions, and Dev Tools.
- Polished the Environment editor by grouping Set as default and Apply into a single compact action bar.
- Removed the manual GitHub update-check UI and service from the About dialog.
- Synced app, assembly, file, package manifest, application manifest, About dialog, README, and regression-test version metadata to `3.3.0`.

### v3.2.1 (2026-05-09)

- Removed the toast notification feature because Windows toast activation is not a good fit for the current unpackaged WebView2 shell.
- Removed notification settings, notifier lifecycle wiring, and related regression coverage.
- Kept the v3.2 native features intact: global hotkey, tray commands, diagnostic export, Cloudflare PoP tooltip, always-on-top, compact mode, and WebView2 circuit breaker.
- Synced app, assembly, file, package manifest, application manifest, About dialog, and regression-test version metadata to `3.2.1`.

### v3.2.0 (2026-05-09)

- Added localized tray context menu with Reload, View Logs, status header, and full Chinese support.
- Added configurable global hotkey (default Ctrl+Alt+Space) to show/hide the main window from anywhere, including Settings UI controls, validation, and reset-to-default support.
- Added diagnostic bundle export: one-click zip of redacted settings, recent logs, runtime info, and diagnostic summary.
- Added Cloudflare PoP (Point of Presence) display in the latency tooltip by parsing the `cf-ray` response header.
- Added `StopAsync` to `SingleInstanceCoordinator` for clean listener shutdown without pipe races.
- Added always-on-top pin button in the title bar with persistent setting, native `HWND_TOPMOST` fallback, and theme-aware active/inactive colors so the Pin state remains visible in light and dark themes.
- Added compact mode: reduced window (480x120) showing only status bars, with independent position persistence.
- Added task-complete toast notification when work status transitions from LIVE to IDLE (debounced, only when window is hidden).
- Added WebView2 recreation circuit breaker: stops runaway recreation after 5 attempts per minute and shows actionable error.
- Added `AppSettings` fields for global hotkey, always-on-top, compact mode, and notification preferences.
- Synced app, assembly, file, package manifest, application manifest, and About dialog version metadata to `3.2.0`.

### v3.1.3 (2026-05-08)

- Fixed taskbar/system restore after minimizing to tray by restoring the minimized HWND placement before hiding the window.
- Covered the remaining dedicated-GPU direct mode restore path where Windows could otherwise keep the main window at `160x28` and `-32000,-32000` after taskbar activation.
- Added regression coverage to ensure tray hiding restores minimized placement before calling `SW_HIDE`.
- Synced app, assembly, file, package manifest, application manifest, and About dialog version metadata to `3.1.3`.

### v3.1.2 (2026-05-08)

- Fixed main-window restoration after GPU/display topology changes such as switching to dedicated-GPU direct mode.
- Sanitized persisted minimized-window sentinel bounds like `160x28` at `-32000,-32000` so startup falls back to a visible default window.
- Stopped saving window bounds while the main window is hidden to tray or minimized, preventing invisible-window state from being persisted again.
- Recentered previously saved bounds onto the current display when the saved rectangle no longer intersects any available work area.
- Synced app, assembly, file, package manifest, application manifest, and About dialog version metadata to `3.1.2`.

### v3.1.1 (2026-05-02)

- Renamed the Settings More section to Advanced.
- Synced app, assembly, file, package manifest, application manifest, and About dialog version metadata to `3.1.1`.

### v3.1.0 (2026-05-02)

- Added a system tray icon with status tooltip, minimize/close-to-tray support, and right-click Open OpenClaw, Settings, and Exit actions.
- Fixed tray initialization by declaring Unicode marshalling for Win32 `*W` entry points, including window class registration, icon loading, and menu text.
- Fixed tray right-click handling for the `NOTIFYICON_VERSION_4` callback format by reading the event from `LOWORD(lParam)`.
- Fixed tray menu popup behavior by using a hidden normal owner window instead of a message-only `HWND_MESSAGE` window.
- Added More settings for minimize-to-tray, close-to-tray, and optional multiple-instance behavior.
- Disabled multiple instances by default; secondary launches now restore the existing OpenClaw window when the setting is off.
- Renamed the Shell settings section to More and moved it to the bottom of the settings navigation.
- Changed window minimize and close behavior to hide OpenClaw to the tray when enabled and the tray icon is available.
- Added latency badge hover details for the most recent probe samples, including latest, min, average, p95, and max round-trip time.
- Synced app, assembly, file, manifest, and About dialog version metadata to `3.1.0`.

### v3.0.6 (2026-05-02)

- Fixed deferred settings saves so updates queued while a previous write is flushing are persisted by a follow-up save.
- Hardened settings loading against explicit `null` JSON sections for environments, heartbeat, recovery, and diagnostics options.
- Moved log retention cleanup off the `LoggingService` constructor path and into the background writer task.
- Switched latency probing to `GET __openclaw/control-ui-config.json` under the configured Control UI base path, with clean cancellation for the initial probe task.
- Split pure .NET recovery/config/logging code into `OpenClaw.Core` so tests can reference real shared code instead of compiling a growing mix of linked files and stubs.
- Pinned NuGet package versions, enabled package lock files, and removed the obsolete `RestorePackagesConfig` restore switch.
- Synced app, assembly, file, manifest, and About dialog version metadata to `3.0.6`.

### v3.0.5 (2026-05-01)

- Hardened settings persistence with atomic writes so interrupted saves no longer risk leaving a truncated `settings.json`.
- Improved Cloudflare Tunnel behavior by replacing ICMP latency checks with HTTP HEAD RTT probes and honoring the configured hard-refresh cooldown in heartbeat recovery.
- Reduced local resource buildup by closing replaced WebView2 instances explicitly, tail-reading the log viewer, and applying 14-day log retention.
- Trimmed UI churn by de-duplicating heartbeat/run indicator property changes and converting the Stop command path to awaitable async execution.
- Synced app, assembly, file, manifest, and About dialog version metadata to `3.0.5`.

### v3.0.4 (2026-04-29)

- Fixed the main window top-edge artifact by removing the XAML edge cover workaround and explicitly syncing the WinUI title bar, DWM caption, and DWM border colors.
- Updated theme-change handling so `ActualThemeChanged` uses the full native frame refresh path instead of only repainting managed title-bar content.
- Synced app, assembly, file, manifest, and About dialog version metadata to `3.0.4`.

### v3.0.3 (2026-04-22)

- Kept the shell lightweight by narrowing Hosted UI DOM scanning to auth/origin/pairing/connectivity signals and avoiding broader page-text sweeps.
- Tuned the default heartbeat, reconnect, and hard-refresh cadence for the Cloudflare Tunnel remote-gateway path so the shell is less aggressive during transient tunnel jitter.
- Reduced startup and debug-session noise by removing eager string-resource warm-up, caching `CoreWebView2` handles, and de-duplicating high-frequency WebView lifecycle logs.

### v3.0.2 (2026-04-21)

- Fixed Visual Studio solution configuration mappings so the test project now maps cleanly across `x64`, `x86`, and `ARM64` solution platforms without showing unknown project configuration warnings.
- Reduced startup and background overhead by deferring non-critical warm-up work, pausing hidden-window activity, and tightening WebView recreation scheduling into a single debounced path.
- Added lightweight runtime observability for WebView recreation, Control UI inspect reuse/coalescing, deferred settings saves, and heartbeat-triggered recovery so diagnostics now expose the recent optimization paths more clearly.

### v3.0.1 (2026-04-21)

- Continued the refactor by splitting `MainWindow` and `SettingsDialog` startup logic into smaller initialization, action, navigation, and theme files without changing existing behavior.
- Consolidated duplicated window theme and title-bar refresh logic into shared helpers so the main window and settings window now follow the same theme-application pipeline.
- Fixed an initialization-order null reference in `ShellSessionCoordinator` by making logger and recovery-option dependencies available before `AttachAsync()` runs.
- Fixed the window-shell split so the new partial entry files compile cleanly and the main window, settings window, and About version display stay in sync at `3.0.1`.

### v3.0.0 (2026-04-21)

- Refactored shared window theme and native frame refresh logic into reusable helpers to reduce duplicate patch-style fixes across the main window and settings window.
- Split reusable command, indicator, and app metadata types out of large view model files to make responsibilities clearer and future maintenance safer.
- Consolidated main window environment selection and UI-thread update flows so behavior stays the same while the code path is easier to reason about.

### v2.1.4 (2026-04-20)

- Added a top-right latency badge for the active Control UI endpoint.
- Increased latency refresh cadence from 3 seconds to 1 second.
- Reduced transient blank latency readings by retaining the most recent successful ping value when a probe briefly misses.

### v2.1.3 (2026-04-20)

- Fixed the Settings window so reopening it immediately resyncs the current app theme before the window is shown again.
- Replaced the title bar refresh resize hack with a non-geometry non-client refresh path based on native frame invalidation, redraw, and DWM flush.

### v2.1.2 (2026-04-19)

- Unified heartbeat settings so runtime behavior now respects the configured enable flag and reconnect thresholds.
- Added settings normalization so legacy `heartbeatIntervalSeconds` values migrate cleanly into the newer heartbeat settings object.
- Added explicit disposal for `WebViewService`, `HostedUiBridge`, `ShellSessionCoordinator`, and main-window event subscriptions during shutdown.
- Improved diagnostics and settings guidance for Cloudflare Tunnel and reverse-proxy deployments, especially around `gateway.controlUi.allowedOrigins`.
- Clarified that environment URLs must use the public hosted Control UI page origin rather than the raw Gateway WebSocket endpoint.

### v2.0.9 (2026-03-31)

- Refined the recovery architecture so heartbeat, event-gap handling, and background resume all prefer in-page reconnect or soft resync before falling back to a hard reload.
- Added input-focus-aware recovery guards to reduce unexpected refreshes while typing.
- Removed the last dead duplicate bridge constant from `WebViewService`, leaving `HostedUiBridge` as the single injected page bridge.
- Polished the top status strip layout so heartbeat summary and indicators each occupy their own centered lane.
- Fixed the top heartbeat badge staying gray by preventing duplicate heartbeat restarts from resetting the timer before the first probe completed.
- Tightened the top status strip spacing so `HB`, `MODEL`, `AUTH`, and `Status` read more evenly without over-compressing the model label.

### v2.0.6 (2026-03-30)

- Consolidated hosted UI snapshot ownership under `WebViewService` and reduced duplicate status pipelines.
- Hardened WebView recreation and bridge reattachment behavior to avoid stale subscriptions.
- Localized heartbeat summary text in both English and Chinese.

---

## 简体中文

### v5.0.1 (2026-06-01)

- 将 app、assembly、file、package manifest、application manifest、README、中文 README 和 changelog 元数据同步到 `5.0.1`。
- 为 Cloudflare Tunnel / 反向代理部署统一 Gateway HTTP 状态分类，heartbeat、diagnostics 和 latency probe 共用同一套语义；代理、路径、服务端和 Cloudflare Tunnel 1033 故障现在会报告为失败，而不是健康 transport 或健康延迟样本。
- 将 Control UI latency probe 改到文档中的 `__openclaw__/a2ui/` 托管 Control UI 路径，并停止把 404/405/5xx/1033 响应写入 latency history。
- Settings 持久化失败现在会回传到 Settings 对话框；文件锁定、权限、磁盘或原子写入失败不会再让对话框像保存成功一样关闭。
- 新增仓库 guardrail，防止 latency probe 回退到旧 `control-ui-config.json` 路径、防止未分类 HTTP 状态被发布为成功，并要求 Settings 写入失败经由 persistence adapter 传回 UI。

### v5.0.0 (2026-05-29)

- 继续加固 terminal hosted-session failure 投影：`Unavailable` 会在 Reconnecting 时显示可见 InfoBar，`GatewayError` / `Unavailable` 会把 ShellSessionCoordinator 从旧的 Ready/Healthy recovery state 移出，WebView 重建异常会显示本地化可操作错误，而不是在 timeout recovery 隐藏 InfoBar 后只写日志。
- 加固 completion-timeout 后的迟到成功 completion：仍属于当前 navigation 的成功 `NavigationCompleted` 会重新建立 navigation cancellation ownership，并继续正常 page-token/status-probe 路径，而不是在清理通知前提前返回。
- 默认 `https://example.com` 环境现在被当作首次运行占位符，而不是可导航的 Control UI 目标；启动或重新选择占位环境时会跳过 WebView2 host 创建、停止 probe、清理旧 WebView host 状态，并显示本地化的配置 Gateway 状态，不再导航到 `example.com` 或让 loading ring 持续转圈。
- 加固 WebView 启动卡住路径：当 WebView2 没有送达 `NavigationStarting` 时，只有匹配 start watchdog 记录目标的 `NavigationCompleted` 可以接管 pending navigation；start timeout recovery 会在有界窗口内继续保留该目标，迟到 completion 恢复页面时会取消仍在排队或因 compact/隐藏/最小化延后的 timeout 触发型 WebView 重建，timeout 触发的重建请求不会覆盖 settings、initial、session 或 topology 等更高优先级的重建原因；completion watchdog 需要匹配 active watchdog id 才能发布 timeout recovery；HostedUiBridge 消息入口改用 host generation 加 owner/page-token 归属，而不是 CoreWebView2 wrapper identity；全窗口 loading ring 也会在浏览器 navigation 结束后清除，不再贯穿整个 `GatewayConnecting` 阶段。
- 关闭两个相邻的旧 navigation 窗口：page-token retry 耗尽时只会对原 navigation generation 发布 `Unavailable`，Stop fallback 会取消 navigation watchdog、probe、page ownership 和 navigation cancellation，避免已停止的加载稍后触发过期启动 timeout recovery。
- 关闭导航后的半连接状态：有界 status probe loop 如果始终没有进入终态 Control UI phase，会发布归属明确且会终止 probe 的 `Unavailable` 快照，后续交给 recovery 接管而不是继续执行页面脚本探测。
- Control UI issue 快照现在会显示可见错误 InfoBar；auth、pairing、origin rejection 和 Gateway error 不再只改变状态文字而看起来像静默卡住。
- Reload 现在只有在 WebViewService 确认 reload navigation 已启动后才清除旧错误提示，与手动 Retry 的防误清逻辑保持一致。
- 收紧动态 WebView 启动门槛：重建出来的 WebView2 子控件必须已 loaded、可见、尺寸非零，并且外层 host 处于非 compact、非隐藏、非最小化状态后才会初始化和导航；WebView2 子控件 layout timeout 会通过正常 recreation timer/circuit-breaker 路径重新排队，避免一直等下一个窗口事件；延后重建归属保留在 `WebViewRecreationService`，layout 诊断日志会记录 compact、hidden、minimized 和子控件尺寸。
- 修复 VS2026 打开 solution 时的项目配置错误：`OpenClaw.Core` 保持平台无关 SDK class library，solution 的 `x64`/`x86`/`ARM64` platform 映射到 Core project 的 `AnyCPU` 配置。
- 收紧 solution guardrail：`OpenClaw.Core` 不能再被改回架构专用 project platform，也不能映射到 Core 未声明的配置。
- 增加仓库 guardrail，禁止在 runtime/UI 路径新增同步等待；只保留 settings flush、logger flush 和 single-instance listener ownership release 这些明确的 shutdown drain 例外。
- 保存的语言偏好现在通过 Windows App SDK 的 `Microsoft.Windows.Globalization.ApplicationLanguages` API 应用，启动时不再记录之前 WinRT language-override warning。
- 将 WebView navigation internals 继续拆成聚焦 partial：event/completion flow、host-message handling、shared navigation ownership/cancellation state、watchdog ownership、CoreWebView2 command wrapper、page-token/session-ready retry、process-failure/auto-retry recovery 分别归属独立文件。
- 将 WebView lifecycle/session 操作从主服务文件拆出；`WebViewService.cs` 保留共享状态、构造、事件和 public navigation command，lifecycle/current-target 与 profile/session 操作分别归属 dedicated partial。
- 将 `WebViewStatusInspector` 的 direct inspection/coalescing、post-navigation probe、parsing 和有界 script execution 拆到聚焦 partial，让主 inspector 只保留共享状态、公共入口和 snapshot publication。
- 记录 v3.3.6 架构清理基线之后的第二轮 architecture hardening 计划。
- 明确当前 no-`tests/` 验证替代方案：restore/build/format、仓库 guardrail、bridge script checks、空白差异检查和 VS2026 manual debug。
- 将文档同步到当前 runtime 拆分：`WebViewStatusInspector`、`HeartbeatRuntime`、`WebViewRecreationService`、拆分后的 embedded `HostedUiBridge` assets、compact mode visual states、live shell settings apply pipeline 和 `StatusPresenter`。
- 根据当前代码刷新第二轮计划，纳入已完成的 WebView status inspection asset、heartbeat transport/policy 拆分、settings persistence adapter 和 `AppRuntimeContext`。
- 将 `HostedUiBridge.StatusInspection.js` 拆分为 DOM utilities、MODEL DOM fallback、activity/stale-busy 和 phase-classification assets，并补充 bridge script verifier 覆盖。
- 将最后一个 Settings 保存日志从 `SettingsViewModel` 移到 settings persistence adapter 边界后面。
- 加固最终收尾路径：预热的 Settings 对话框在激活前重新加载当前持久化状态，compact mode 不再把 480x120 写成普通窗口尺寸，heartbeat hosted-session probe 支持取消，合并的 WebView status inspection 不再让调用方取消污染共享 in-flight 结果，status inspection timeout/失败会在当前 page ownership 下发布 `Unavailable` 快照，localized bridge script 改为每次 WebView 初始化时组合，并在 language override 尝试后刷新字符串资源缓存。
- 本地化 Settings 诊断包操作，并增加 settings persistence 边界、compact bounds 保存和 localized bridge-script 缓存 guardrail。
- 为 WebView 状态脚本检查增加有界 timeout，避免 `ExecuteScriptAsync` 卡住后把后续 coalesced probe 一起拖住，并显式归属 status probe task/cancellation 生命周期。
- 为 hosted bridge command dispatch 和 WebView stop/abort command scripts 增加有界 timeout 和 await 后 current-target 校验，避免页面 promise 卡住时无限阻塞 native recovery，或在 WebView 替换后写入过期 command 结果。
- 将 hosted bridge 和 WebView stop/abort command 的归属检查扩展到已接受的 page version，避免同一个 WebView 内导航后旧 document promise 把成功结果写到当前页面。
- 将 ShellSessionCoordinator 的 WebView2 和 hosted bridge adapter 调用切回 UI dispatcher，覆盖从后台 heartbeat 发起的 recovery 路径。
- 将公共 Control UI inspection 入口、WebViewService heartbeat hosted-session inspection 和导航后的 status probe loop 都切回注入的 UI dispatcher 后再触碰 WebView2。
- Gateway transport heartbeat 现在会把 5xx、缺失 Control UI 路径的 404、heartbeat probe 被拒绝的 405 和未明确允许的 4xx 识别为失败，避免 Cloudflare/反代错误页被误判为健康传输。
- 收紧 UI dispatcher 契约，dispatcher enqueue 失败时不再把 WebView2 或 WinUI 工作退回后台线程 inline 执行。
- 增加 hosted bridge message 的 native owner/page-token 校验，避免旧 WebView document 把 session/status 消息写回当前页面状态。
- 关闭剩余 WebView ownership 窗口：程序主动 navigation/reload/retry 会先失效 page token 并清空已接受的 navigation id，page-token 捕获 await 之后重新校验 generation，并在不阻塞 status probe 的前提下重试 page-token 捕获。
- page-token 被 native 接受后会主动请求一次 `session-ready` 重放，避免过早发出的 hosted ready 消息被 ownership filter 拒收后必须手动刷新。
- page-token 捕获重试和 native-triggered `session-ready` replay 现在绑定 lease-owned navigation cancellation scope，避免 reload、detach 或新 navigation 后旧 replay 工作继续排队，同时不会 dispose 仍被有界 retry/replay 持有的 token；cancellation callback 失败也不会阻塞 scope retirement。
- 调整 status probe 取消路径，在清空所有权前先取消 active probe CTS，同时继续由正在运行的 probe 负责释放，移除 cancel/dispose 竞态。
- Hosted bridge 现在跟踪并移除 document-created script id，避免同一 WebView 重复初始化后累积旧 observer 和 timer。
- Reload 和 retry 路径在 CoreWebView2 调用开始前失效时会发布 error 状态，避免外壳停在 Loading/Reconnecting。
- Reload 现在会返回 CoreWebView2 是否真正接受了刷新命令，recovery 路径会把 no-op reload 视为恢复失败，而不是在没有真实刷新的情况下推进到 Connecting。
- 手动 Retry 现在只有在 retry navigation 真正启动后才隐藏当前错误；如果没有可重试的 WebView navigation，会继续显示本地化错误，并提示使用刷新或切换环境恢复。
- Auto-retry continuation 现在会在重试前重新校验 navigation generation 和目标归属，把已经切换的 navigation 当作 stale 退出；如果重试耗尽或 retry 命令无法启动，会发布 Error，避免外壳停在 Reconnecting，也避免旧 navigation-completed 工作覆盖当前状态。
- 为 WebView navigation-completed async 处理增加可观察的异常边界，probe 启动或 recovery 通知失败时会记录日志，而不是从 `async void` event handler 逃逸。
- WebView recreation 在关闭旧 WebView2 控件前先 detach coordinator、bridge 和 WebView service，并跟踪 recreation/foreground-resume async work，避免 shutdown 后写入已释放的 shell 状态。
- Shell ContentDialog 入口现在防止快速重复触发，dialog 失败会写日志；Settings session-reset 运行期间会禁用按钮，并用本地化错误提示报告失败。
- 配置延迟保存现在显式保存 worker task/cancellation source，用同一个生命周期 gate 串行化合并保存版本，并会在 shutdown flush settings 前取消、等待或观察旧 worker。
- WebView recreation 与 WebView/bridge 初始化在 shutdown 期间支持取消，并将 ShellSessionCoordinator async event handler 改为有观察和日志的 fire-and-forget recovery task，recovery/probe cancellation 的释放由正在运行的 operation 统一负责。
- 加固 Log Viewer 取消路径，避免过期 load 的失败结果覆盖新一次刷新或正在关闭的对话框。
- 加固 Control UI latency probe 生命周期，stop/restart 只取消 active probe，由已观察的 probe task 统一释放 timer/CTS 并记录异常。
- 加固 heartbeat 和 latency probe 的旧 run 过滤：旧 heartbeat loop 在 stop/restart 后不能再发布观察或触发 recovery，新 heartbeat loop 会离开调用线程运行，并在等待第一个周期 interval 前先发布一次即时观察，旧 latency probe 也会通过 run id 和当前选中环境 host 校验后才更新 UI。
- 移除 `MainViewModel` 最后的 `App.MainWindow` fallback；ViewModel 的 UI dispatch 必须由拥有它的窗口注入，仓库 guardrail 会拒绝重新引入全局窗口依赖。
- 加固最终 review 发现的问题：WebView status inspection 在执行脚本和发布状态前都要求 accepted page version，已取消的合并 inspection caller 离开后不能再写 UI 状态，Stop fallback 会绑定最初的 WebView/page target，bridge CustomEvent fallback 没有真实 hosted method 时不再报告 soft-resync 已处理，非 chat Settings/Cron busy 状态不再触发 stale-chat recovery，compact mode 在 480px 下会折叠非必要固定宽度顶栏段。
- 加固 single-instance 关闭和 relaunch 行为，App 会先等待 named-pipe listener 停止再释放 coordinator，并改用 named semaphore，让异步 live settings 和 shutdown 路径可以从任意线程释放 single-instance lock；同时会把多实例设置变更应用到当前运行的 coordinator，并通过被观察的异步路径处理 listener stop/start，避免 Settings 保存被 named-pipe shutdown 阻塞。
- 加固 single-instance 重新启动流畅度：二次启动请求 primary 激活时改用异步 named-pipe connect/write，激活失败后的接管重试改用可取消的异步 delay，并共用一个接管 deadline，不再用同步 `Thread.Sleep` 卡启动线程；关闭路径仍会等待 named-pipe listener 排空后再释放 semaphore。
- 加固最终 review follow-up：短暂 `Unavailable`/`Unknown` status inspection 不再清空同一 accepted page 内最近一次非空 MODEL，`Unavailable` 会降级旧 `Connected` shell 状态，single-instance shutdown 不再使用 best-effort timeout 而是等待 named-pipe listener 排空，Settings View Logs 会先关闭 Settings 再打开 log dialog，Pin tooltip 也改为本地化资源。
- 加固最新 review follow-up：heartbeat 不再把缺失 Control UI 路径或被拒绝的 heartbeat probe 当作健康 transport，未知 hosted bridge CustomEvent fallback 会返回未处理，compact-mode 文档也同步为当前的缩小控制/状态窗口。
- 加固 heartbeat/recovery 交互：hosted-session heartbeat inspection 不再在 heartbeat 失败计数前发布 UI 快照，resource scheduling 会在自己接管的 `Reconnecting`/`Unavailable` hosted-session 状态下继续跑 heartbeat，旧 heartbeat loop 也不能停掉或 recovery 新开始的 run。
- 加固 page-token 和 recovery cancellation 路径：native page-token 捕获耗尽后会发布归属明确的 `Unavailable` 快照，ShellSessionCoordinator recovery inspection 在判断 reload fallback 或 recovery 完成前会携带当前 operation cancellation token。
- 加固 ShellSessionCoordinator observed recovery 生命周期：event-gap、heartbeat-triggered、stale-busy 和 foreground-resume recovery work 都有可取消的 operation CTS，attach/detach/reset/dispose 会在服务替换前取消 pending inspection，取消异常会继续抛出而不是变成 reconnect fallback。
- 加固 ShellSessionCoordinator bridge command cancellation：reconnect 和 soft-resync command 现在会把当前 recovery operation token 传过 Core bridge contract、WinUI UI-dispatch adapter 和 `HostedUiBridge` 脚本执行链路，避免已取消或已替换的 recovery operation 继续排队或运行旧页面命令。
- 加固 ShellSessionCoordinator recovery reload cancellation：reconnect 和 hard-refresh reload 现在会把当前 recovery operation token 传过 Core WebView contract 和 WinUI UI-dispatch adapter，避免已取消或已替换的 recovery operation 对新 session 启动排队中的旧 reload。
- 收紧 recovery reload 的 UI dispatch：新增可取消的同步 dispatcher overload，并移除同步 WebView2 reload 外层的 `Task.FromResult` 包装。
- 加固最终 review 收尾：短暂 `Loading`、`Unavailable` 和 `Unknown` 快照会在同一 accepted page 内保留最近一次非空 MODEL；Settings 保存只 dirty-merge 实际编辑过的字段，并忽略 two-way binding 初始化时的同值写回，避免 stale dialog snapshot 覆盖外部 Pin、hotkey 或 environment 变更；public recovery request 会把调用方 cancellation 链接进当前 operation CTS；compact-mode loading-ring visibility 由 compact 状态和 loading 状态共同派生，避免 compact 下重新显示。
- 加固最终 UI/lifecycle 收尾：注入式 ViewModel UI 更新现在会捕获并记录回调异常，避免 dispatcher 回调变成 UI 线程未处理异常；WebView process-failure 处理会先退休 navigation retry/replay cancellation，再发布 unavailable snapshot。
- 加固最终流畅度收尾：长耗时 async command 运行期间会拒绝重复执行，并防御性处理空 task 结果；诊断包导出不会在 UI 线程枚举日志或压缩 zip；非当前环境的 WebView2 profile 文件夹删除改到后台线程执行。
- 将当前重构验证分支的 app、assembly、file、package manifest、application manifest、README 和 changelog 元数据同步到 `5.0.0`。下方较早的 `v3.0.5 (2026-05-01)`、`v3.0.1 (2026-04-21)` 和 `v3.0.0 (2026-04-21)` 条目保留为历史发布记录。
- 新增仓库 guardrail，确保 `5.0.0` project、assembly/file、package manifest、application manifest、README、中文 README 和 changelog 元数据在收尾阶段保持一致。
- 将 Settings 的 Control UI URL placeholder 移入本地化资源，并修正 multiple-instances 说明文案。
- 新增仓库 guardrail，要求英文和中文 `.resw` resource key 保持一致。
- 将托盘 Open、Compact Mode、Exit 菜单文案统一到 typed `StringResources` 属性，补齐中文 Compact Mode 菜单文案，并新增 guardrail 防止回退到 raw tray-menu resource fallback。
- 记录剩余收尾目标：VS2026 manual Gateway/Cloudflare checklist 和 final commit。完整本地验证已运行，验证后 Debug 输出已清理，并保留 Release 文件夹。
- 说明早期 changelog 中关于 executable regression coverage 的描述在 v3.3.6 移除 harness 后属于历史记录。
- 加固 `tools\verify-bridge-scripts.ps1`：Node runner 失败现在会让 PowerShell 失败，默认要求 Node，除非明确设置 `OPENCLAW_ALLOW_NODE_SKIP=1`，并新增独立的完整 composed bridge 行为检查，覆盖 native-triggered `session-ready` replay。
- 扩展 VS2026 manual checklist，纳入 Cloudflare/反代 4xx/5xx/auth/origin 页面、`cf-ray` PoP 解析、DWM title-bar 边缘和 single-instance relaunch handoff。

### v3.3.6 (2026-05-21)

- VS2026 debug 验证通过后，将 v3.3.5 架构清理作为当前发布基线。
- 保留 bridge/WebView 加固作为当前基线：嵌入式 hosted bridge asset、事件/命令路径加固、安全 host messaging，以及按 generation 隔离的 WebView inspection cache 复用。
- 从当前 solution 和仓库中移除本地回归测试 harness，同时保留 `OpenClaw.Core` 作为 app 使用的纯 .NET 共享源码树。
- 同步 app、assembly、file、package manifest、application manifest、README 和 changelog 元数据到 `3.3.6`。

### v3.3.5 (2026-05-20)

- 新增 `docs/code-style.md`，作为项目代码规范和架构边界的统一入口。
- 将顶部状态栏和底部状态栏的字号、间距、布局常量集中到 `src/OpenClaw/Styles` 下的 WinUI 资源字典。
- 将可执行测试 harness 拆分为按领域组织的 `Tests.*.cs` 文件，并新增代码规范文档、架构边界和顶部状态栏 XAML 共享资源的回归测试。
- 将 `WebViewService` 的命令注入、heartbeat、Control UI inspection 和 profile 文件夹帮助器拆分到独立 partial 文件。
- 将所有 Core-compatible 源文件迁移到 `src/OpenClaw.Core` 物理源码树，包括此前从 WinUI 项目链接的窗口边界策略。
- 将主托管 bridge 浏览器脚本迁移为嵌入式 JS asset，C# 侧只负责资源加载、本地化字符串注入和 MODEL resolver 注入。
- 将托管 MODEL 的 app-state 解析抽到嵌入式 JS asset，并用可执行回归测试覆盖默认值、`null` override、Map-backed override 和对象形 payload。
- 新增可执行 hosted bridge 事件覆盖：session-ready 元数据、命令分发返回值、侧边栏 mutation filtering、安全 WebView2 host messaging，以及按 generation 隔离的 WebView inspection cache 复用。
- 同步 app、assembly、file、package manifest、application manifest、README 和回归测试版本元数据到 `3.3.5`。

### v3.3.4 (2026-05-20)

- About 对话框里的 GitHub 主页链接和文案已更新为 `https://github.com/Guijianchou`。
- Settings 保存后会立即应用 Always-on-top 和全局热键变更，不再需要重启。
- 收紧紧凑模式顶栏布局，在 480px 宽度下折叠非必要状态段并保留模型/状态可读性。
- WebView2 状态探测现在带有 WebView/导航 generation 归属，避免过期异步脚本结果覆盖当前状态。
- 明确 heartbeat loop 和日志查看器的生命周期：heartbeat 独立持有 timer/task，日志 tail 在 UI 线程之外加载。
- 同步 app、assembly、file、package manifest、application manifest、README 和回归测试版本元数据到 `3.3.4`。

### v3.3.3 (2026-05-19)

- 修复顶部 MODEL 值的字号，使其与原生状态栏的 12px 文本字号一致。
- 加固托管 OpenClaw 模型检测，支持 app-state 变体、URL session key、Map 形式模型 override，以及非字符串 payload 归一化。
- 延后 app-state 默认 MODEL fallback，包括 session 的 `null` override，避免根节点默认模型盖掉后续嵌套的当前会话模型。
- 同步 app、assembly、file、package manifest、application manifest、README 和回归测试版本元数据到 `3.3.3`。

### v3.3.2 (2026-05-19)

- 增加托管聊天会话的 stale busy-stream 检测：bridge 会跟踪聊天活动签名，并更频繁轮询 busy 的已连接页面。
- 增加 stale-stream 恢复升级链路：OpenClaw Manager 会先 soft-resync lightweight state 和 recent messages，soft-resync 预算耗尽后再执行 hard refresh。
- 收窄输入焦点 reload 保护：空的聚焦编辑器不再阻止恢复刷新，但存在未发送文本时仍会延迟自动 reload。
- 扩展诊断信息，加入最近一次 hosted UI phase、busy 状态、stale 持续时间和聚焦输入框文本状态。
- 同步 app、assembly、file、package manifest、application manifest、README 和回归测试版本元数据到 `3.3.2`。

### v3.3.1 (2026-05-17)

- 修复状态栏 MODEL 字段不显示当前模型的问题，现在会读取 OpenClaw Web UI 明确的模型选择器。
- 加固 MODEL 检测：DOM 控件尚未就绪时会读取 OpenClaw app state，并在瞬时空快照期间保留最近一次非空模型。
- 降低右侧栏长内容加载时的 WebView2 CPU 飙升风险，忽略与状态栏无关的 sidebar DOM 变化和内嵌 preview frame。
- 拉宽顶部状态 pill，让较长 provider/model 名称在 AUTH/Status 指示器前保留更多上下文；已连接的 OpenClaw settings/cron 页面改走 app-state 状态快路径，避免 DOM mutation storm。
- 记录手动验证后的当前缓解状态：MODEL 显示和 WebView2 CPU 飙升已有可用级别改善，但超长模型名和特别重的 settings/Cron 页面仍需要继续观察。
- 同步 app、assembly、file、package manifest、application manifest、README 和回归测试版本元数据到 `3.3.1`。

### v3.3.0 (2026-05-12)

- 优化 Settings，采用 PowerToys 风格设置行、紧凑 ToggleSwitch 间距，并补齐窗口置顶文案本地化。
- 将 Settings 导航整理为 Language、General、Environments、Sessions 和 Dev Tools。
- 优化 Environment 编辑区域，将 Set as default 和 Apply 收进同一个紧凑动作栏。
- 从 About 对话框移除手动 GitHub 更新检查 UI 和服务。
- 同步 app、assembly、file、package manifest、application manifest、About dialog、README 和回归测试版本元数据到 `3.3.0`。

### v3.2.1 (2026-05-09)

- 移除 toast 通知功能；当前应用是 unpackaged WebView2 shell，Windows toast activation 不适合这个分发和启动模型。
- 移除通知设置、notifier 生命周期接线和相关回归测试。
- 保留 v3.2 的纯原生能力：全局热键、托盘命令、诊断导出、Cloudflare PoP tooltip、always-on-top、compact mode 和 WebView2 circuit breaker。
- 同步 app、assembly、file、package manifest、application manifest、About dialog 和回归测试版本元数据到 `3.2.1`。

### v3.2.0 (2026-05-09)

- 添加本地化托盘右键菜单，包含 Reload、View Logs、状态标题和完整中文支持。
- 添加可配置全局热键（默认 Ctrl+Alt+Space）用于随时显示/隐藏主窗口，并在 Settings 中提供输入、校验和恢复默认值。
- 添加诊断包导出：一键打包脱敏设置、近期日志、运行时信息和诊断摘要。
- 在延迟 tooltip 中解析 `cf-ray` 响应头并显示 Cloudflare PoP。
- 为 `SingleInstanceCoordinator` 添加 `StopAsync`，关闭时等待 listener task，避免 pipe dispose 竞态。
- 添加标题栏 Always-on-top Pin 按钮，支持持久化设置、原生 `HWND_TOPMOST` fallback，并使用主题感知的启用/未启用颜色，浅色和深色主题下都能区分当前状态。
- 添加 Compact Mode：缩小为仅显示状态栏的窗口，并独立持久化 compact 位置。
- 添加任务完成 toast：工作状态从 LIVE 切换到 IDLE 且窗口不可见时发送通知，并带 debounce。
- 添加 WebView2 recreation circuit breaker：一分钟内超过 5 次重建后停止 runaway recreation，并显示可操作错误。
- 添加 global hotkey、always-on-top、compact mode 和通知偏好的 `AppSettings` 字段。
- 同步 app、assembly、file、package manifest、application manifest 和 About dialog 版本元数据到 `3.2.0`。

### v3.1.3 (2026-05-08)

- 修复最小化到托盘后通过任务栏或系统 restore 恢复时的窗口异常，隐藏窗口前会先恢复 minimized HWND placement。
- 覆盖独显直连模式下剩余的 restore 路径，避免 Windows 在任务栏激活后仍把主窗口保持在 `160x28` 和 `-32000,-32000`。
- 增加回归测试，确保托盘隐藏逻辑在调用 `SW_HIDE` 前先恢复 minimized placement。
- 同步 app、assembly、file、package manifest、application manifest 和 About dialog 版本元数据到 `3.1.3`。

### v3.1.2 (2026-05-08)

- 修复 GPU/显示拓扑变化后主窗口恢复问题，例如切换到独显直连模式。
- 清理持久化的最小化窗口哨兵 bounds，例如 `160x28` 和 `-32000,-32000`，启动时回退到可见默认窗口。
- 主窗口隐藏到托盘或最小化时停止保存窗口 bounds，避免再次持久化不可见窗口状态。
- 当已保存窗口矩形不再与任何可用工作区相交时，将窗口重新居中到当前显示器。
- 同步 app、assembly、file、package manifest、application manifest 和 About dialog 版本元数据到 `3.1.2`。

### v3.1.1 (2026-05-02)

- 将 Settings 的 More 区域重命名为 Advanced。
- 同步 app、assembly、file、package manifest、application manifest 和 About dialog 版本元数据到 `3.1.1`。

### v3.1.0 (2026-05-02)

- 添加系统托盘图标、状态 tooltip、最小化/关闭到托盘支持，以及右键 Open OpenClaw、Settings、Exit 操作。
- 通过为 Win32 `*W` 入口声明 Unicode marshalling 修复托盘初始化，包括窗口类注册、图标加载和菜单文本。
- 通过从 `LOWORD(lParam)` 读取事件修复 `NOTIFYICON_VERSION_4` 回调格式下的托盘右键处理。
- 通过使用隐藏的普通 owner window 而非 message-only `HWND_MESSAGE` 修复托盘菜单弹出行为。
- 添加 More 设置，用于最小化到托盘、关闭到托盘和可选多实例行为。
- 默认禁用多实例；关闭该设置时，二次启动会恢复已有 OpenClaw 窗口。
- 将 Shell 设置区域重命名为 More 并移动到设置导航底部。
- 在启用且托盘图标可用时，将窗口最小化和关闭行为改为隐藏到托盘。
- 为延迟徽标添加悬停详情，展示最近探测样本的 latest、min、average、p95 和 max 往返时间。
- 同步 app、assembly、file、manifest 和 About dialog 版本元数据到 `3.1.0`。

### v3.0.6 (2026-05-02)

- 修复 deferred settings save，使前一次写入 flush 时排队的更新会由后续 save 持久化。
- 加固 settings load，处理 environments、heartbeat、recovery 和 diagnostics options 显式为 `null` 的 JSON。
- 将日志保留清理从 `LoggingService` 构造路径移到后台 writer task。
- 将延迟探测切换为在配置的 Control UI base path 下请求 `GET __openclaw/control-ui-config.json`，并干净取消初始探测任务。
- 将纯 .NET recovery/config/logging 代码拆入 `OpenClaw.Core`，让测试可以引用真实共享代码。
- 固定 NuGet 包版本、启用 package lock files，并移除过时的 `RestorePackagesConfig` restore 开关。
- 同步 app、assembly、file、manifest 和 About dialog 版本元数据到 `3.0.6`。

### v3.0.5 (2026-05-01)

- 使用原子写入加固 settings persistence，避免中断保存留下截断的 `settings.json`。
- 用 HTTP HEAD RTT 探测替代 ICMP 延迟检查，并在 heartbeat recovery 中遵守 hard-refresh cooldown，改善 Cloudflare Tunnel 行为。
- 通过显式关闭被替换的 WebView2 实例、日志查看器 tail-read 和 14 天日志保留，减少本地资源堆积。
- 通过去重 heartbeat/run indicator 属性变更，并将 Stop 命令路径改为可 await 的异步执行，减少 UI 抖动。
- 同步 app、assembly、file、manifest 和 About dialog 版本元数据到 `3.0.5`。

### v3.0.4 (2026-04-29)

- 移除 XAML edge cover workaround，并显式同步 WinUI title bar、DWM caption 和 DWM border 颜色，修复主窗口顶部边缘伪影。
- 更新主题切换处理，使 `ActualThemeChanged` 走完整 native frame refresh 路径，而不是只重绘 managed title-bar content。
- 同步 app、assembly、file、manifest 和 About dialog 版本元数据到 `3.0.4`。

### v3.0.3 (2026-04-22)

- 将 Hosted UI DOM 扫描收窄到 auth/origin/pairing/connectivity 信号，避免更宽泛的页面文本扫描，保持外壳轻量。
- 调整默认 heartbeat、reconnect 和 hard-refresh 节奏，使 Cloudflare Tunnel 远程 Gateway 路径在瞬时 tunnel 抖动时不那么激进。
- 移除 eager string-resource warm-up、缓存 `CoreWebView2` 句柄，并去重高频 WebView 生命周期日志，减少启动和调试噪音。

### v3.0.2 (2026-04-21)

- 修复 Visual Studio 解决方案配置映射，使测试项目在 `x64`、`x86` 和 `ARM64` 平台下不再显示 unknown project configuration 警告。
- 通过延迟非关键 warm-up、暂停 hidden-window activity，并将 WebView recreation scheduling 收敛为单一 debounce 路径，降低启动和后台开销。
- 为 WebView recreation、Control UI inspect reuse/coalescing、deferred settings save 和 heartbeat-triggered recovery 增加轻量运行时可观测性。

### v3.0.1 (2026-04-21)

- 继续重构，将 `MainWindow` 和 `SettingsDialog` 启动逻辑拆为更小的 initialization、action、navigation 和 theme 文件，不改变现有行为。
- 将重复的窗口主题和 title-bar refresh 逻辑合并到共享 helpers，使主窗口和设置窗口使用同一主题应用 pipeline。
- 通过让 logger 和 recovery-option dependencies 在 `AttachAsync()` 之前可用，修复 `ShellSessionCoordinator` 初始化顺序空引用。
- 修复 window-shell 拆分后的编译问题，确保主窗口、设置窗口和 About version display 在 `3.0.1` 保持同步。

### v3.0.0 (2026-04-21)

- 将共享窗口主题和 native frame refresh 逻辑重构为可复用 helpers，减少主窗口和设置窗口间重复的 patch-style 修复。
- 拆出可复用 command、indicator 和 app metadata 类型，让职责更清晰，后续维护更安全。
- 合并主窗口环境选择和 UI-thread update 流程，在行为不变的前提下让代码路径更易推理。

### v2.1.4 (2026-04-20)

- 为当前 Control UI 端点添加右上角延迟徽标。
- 将延迟刷新频率从 3 秒提高到 1 秒。
- 探测短暂失败时保留最近一次成功 ping 值，减少临时空白延迟读数。

### v2.1.3 (2026-04-20)

- 修复 Settings 窗口，使重新打开时会在显示前立即同步当前 app theme。
- 用基于 native frame invalidation、redraw 和 DWM flush 的非几何 non-client refresh 路径替代 title bar refresh resize hack。

### v2.1.2 (2026-04-19)

- 统一 heartbeat settings，使运行时行为遵守配置的 enable flag 和 reconnect thresholds。
- 添加 settings normalization，使旧的 `heartbeatIntervalSeconds` 值能干净迁移到新的 heartbeat settings object。
- 在关闭时显式释放 `WebViewService`、`HostedUiBridge`、`ShellSessionCoordinator` 和主窗口事件订阅。
- 改进 Cloudflare Tunnel 和反向代理部署的诊断与设置说明，尤其是 `gateway.controlUi.allowedOrigins`。
- 明确环境 URL 必须使用公共托管 Control UI 页面 origin，而不是原始 Gateway WebSocket endpoint。

### v2.0.9 (2026-03-31)

- 调整 recovery 架构，使 heartbeat、event-gap handling 和 background resume 都优先尝试 in-page reconnect 或 soft resync，再回退到 hard reload。
- 添加 input-focus-aware recovery guards，减少输入时的意外刷新。
- 移除 `WebViewService` 中最后一个无用重复 bridge constant，让 `HostedUiBridge` 成为唯一注入页面 bridge。
- 打磨顶部状态条布局，让 heartbeat summary 和 indicators 各自占据居中的独立 lane。
- 修复顶部 heartbeat badge 一直为灰色的问题，避免重复 heartbeat restart 在第一次探测完成前重置 timer。
- 收紧顶部状态条间距，让 `HB`、`MODEL`、`AUTH` 和 `Status` 更均衡，同时不过度压缩 model label。

### v2.0.6 (2026-03-30)

- 将 hosted UI snapshot ownership 合并到 `WebViewService`，减少重复状态 pipeline。
- 加固 WebView recreation 和 bridge reattachment 行为，避免 stale subscriptions。
- 本地化英文和中文 heartbeat summary 文本。

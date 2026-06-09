# OpenClaw Development Notes

## v5.1.1 Stability Review Notes

- `5.1.1` keeps the current x64-only application architecture and tightens production behavior around environment identity, recovery ownership, diagnostics, and developer-tool feedback.
- Hosted `session-ready` events must be scoped to the current environment probe identity before they clear recovery state; stale events from a previous endpoint are ignored and logged.
- Hard-refresh cooldowns start only after WebView2 accepts a reload. A failed or unavailable reload must not consume the cooldown window or advance recovery as if a hard refresh started.
- Control UI latency success is reserved for `GatewayHttpStatusKind.Reachable`. Auth-required, pairing, rate-limit, redirects, Cloudflare/proxy failures, and other user-action/error states must publish failure or stale snapshots, not healthy latency.
- Environment configuration normalization trims persisted names and URLs, drops unusable blank entries, de-duplicates display names, guarantees exactly one default, and repairs invalid selected-environment references.
- WebView2 profile identity and session clearing use both environment name and Gateway URL. Legacy profile migration is intentionally marker-gated; unmarked pre-5.1.1 legacy folders are left in place rather than auto-migrated across possible endpoint boundaries.
- Diagnostic bundles must keep total copied log payload bounded, cap diagnostic text entries, redact authorization/cookie/API-key headers, and write notes for skipped or truncated content.
- Release DevTools enablement is injected from diagnostics settings. `WebViewService` must not read `App.Configuration`; the Settings and command surfaces should show localized unavailable, disabled, or failure feedback when DevTools cannot open.

## v5.0.2 Stability Review Notes

- Gateway/Cloudflare HTTP classification must stay strict: header-based Cloudflare 1033 detection accepts exact `1033`, body-based detection requires explicit `Error 1033`, `error code 1033`, or nearby `cf-error-code` evidence, and body snippet reads have a local timeout so diagnostics, heartbeat, and latency probes cannot hang after headers arrive.
- Healthy Gateway network diagnostics must remain `Pass`, while access-required, approval, rate-limit, redirect, and unexpected states remain warnings and path/proxy/tunnel/server failures remain failures.
- When the hosted Control UI reports `Connecting` or failure and the HTTP transport probe also returns a non-healthy result, heartbeat recovery should keep the transport failure detail instead of hiding it behind hosted-session state.
- Latency tooltip text is user-visible and automation-visible, so formatting belongs in the WinUI layer with localized resources; environment changes, placeholder selection, and WebView host detach must clear stale latency samples and Cloudflare PoP state.
- Follow-up review items left intentionally outside this narrow release: theme toggle visuals should be owned by XAML checked-state styling, invalid persisted `AppTheme` values should normalize to `System`, and the approved single-instance shutdown synchronous waits should eventually become a nonblocking bounded drain.

## Project Code Standards

Canonical checklist: [docs/code-style.md](docs/code-style.md).

This project uses C# and WinUI conventions, but follows the Linux engineering bias toward small, explicit, boring code:

- Keep control flow readable. Use braces on every `if`, loop, and branch even for one-line bodies.
- Keep files focused. New service/view-model code should prefer small partials or helper types over growing `WebViewService`, `HostedUiBridge`, or bridge/status inspection assets further.
- Own background work explicitly. A background loop should have a stored `Task`, a stored cancellation source, and one logging boundary for exceptions.
- Long-running UI commands must guard repeated execution while their async work is still running, reset command state in `finally`, and observe/log command failures.
- Expensive filesystem work triggered from UI commands or Settings must run off the UI thread. Do not synchronously enumerate large logs, build diagnostic zip bundles, or delete inactive WebView2 profile folders from dialog or command handlers.
- Deferred or coalesced background work must serialize queue state with its worker lifetime gate, then be cancellable, drained, or observed during shutdown before synchronous flush paths dispose shared services.
- Long-lived OS listeners such as single-instance named pipes must expose a stop path and app shutdown must wait for that stop before disposing listener resources or releasing cross-process ownership.
- Secondary-launch activation and activation-failure takeover waits must stay async and share one bounded takeover deadline. Do not use synchronous named-pipe `Connect` or `Thread.Sleep` in the startup handoff path.
- Runtime settings changes that start or stop long-lived listeners must use an observed async apply path, not a synchronous wait on the Settings/UI save path.
- Settings saves must merge only fields edited in the open Settings window; unchanged stale dialog snapshots must not overwrite live shell changes such as the Pin button, hotkey, multiple-instance mode, or selected environment. Same-value binding writes during Settings initialization must return before marking a field dirty.
- WebView/CoreWebView2 async work must carry a generation or equivalent ownership token across awaits before applying results back to app state.
- Programmatic WebView navigation, reload, and retry must invalidate accepted page ownership and clear the accepted navigation id before calling CoreWebView2, because `NavigationStarting` can arrive after old document messages or completion events are already queued.
- WebView startup recovery must not require `NavigationStarting` to arrive before `NavigationCompleted`. If a start watchdog is still active and the navigation id has not been claimed, only a completion whose current source matches the pending target recorded before `CoreWebView2.Navigate`/`Reload` may claim the navigation and cancel the start watchdog.
- If completion-timeout recovery cancelled navigation cancellation ownership but a successful `NavigationCompleted` is still current, the completion path must recreate navigation cancellation ownership and continue normal page-token/status-probe recovery instead of returning before cleanup notifications.
- Navigation completion watchdogs must have independent active-watchdog ownership, because a queued timeout callback can otherwise pass generation/navigation-id checks after the successful completion path has cancelled the watchdog.
- Hosted bridge WebView message entry points must use host generation plus owner/page-token validation. Do not reject bridge messages by `CoreWebView2` wrapper reference identity.
- Page-token retry exhaustion must publish `Unavailable` against the captured navigation generation, not the tracker's current generation, so an old retry cannot downgrade a newer page.
- Stop fallback is a navigation cancellation path. After `CoreWebView2.Stop()`, cancel navigation watchdogs, status probes, page ownership, and navigation cancellation before updating shell state.
- Reload must return whether CoreWebView2 actually accepted the reload request. Recovery paths must not advance to Connecting or count a hard refresh as started when reload was a no-op because WebView2 was unavailable.
- Manual Retry and Reload must not hide the current error until navigation actually starts. If no retryable WebView navigation exists, keep the localized error visible and point the user to Reload or environment switch recovery.
- Auto-retry continuations are still navigation-completed work. After their retry delay, they must treat changed generation/navigation/WebView targets as stale and must surface exhausted retries or CoreWebView2 command-start failures as Error instead of letting the old completion path publish Reconnecting state with no pending retry.
- WebView/CoreWebView2 async event handlers must route awaited work through a logged exception boundary before publishing app state or notifying observers.
- Unexpected navigation-completion handler failures must publish an owned `Unavailable`/Error projection. Logging only can leave the shell in stale `Loading` after the completion watchdog has already been cancelled.
- WebView status probe loops must store their task and cancellation source, stop by cancelling only, and let the running probe dispose its cancellation source.
- WebView status probe loops must publish an owned `Unavailable` snapshot when all bounded post-navigation probes are exhausted without a terminal Control UI phase. Do not leave the shell indefinitely in `PageLoaded` or `GatewayConnecting`.
- `ControlUiPhase.Unavailable` is a terminal post-navigation probe result; once it is published, recovery owns the next step and the probe loop should stop issuing page scripts.
- Control UI latency probe loops must store and observe their task, stop by cancelling only, let the running probe dispose timer/cancellation resources, and reject stale run results before publishing UI state.
- WebView host recreation must detach native services from the outgoing WebView2 before closing old controls.
- WebView host recreation must not initialize or navigate a hidden, compact, minimized, unloaded, or zero-sized WebView2 child. Defer recreation until the shell host is visible, but requeue child layout timeouts through the normal recreation timer/circuit-breaker path so a transient child `Loaded`/size miss does not wait forever for another window event.
- WebView host recreation layout timeouts count as recreation attempts. A hidden or zero-sized child must not retry forever without tripping the circuit breaker.
- A late successful navigation recovery can cancel pending, deferred, or already-active timeout-only WebView recreation. Before detaching a host for timeout recovery, recheck whether navigation recovery made that replacement unnecessary.
- WebView recreation exceptions must surface a localized actionable InfoBar error with Retry. Logging alone is not enough after timeout recovery has intentionally hidden the first-hop InfoBar.
- Cancellation sources shared with a running async operation should be cancelled by external owners but disposed by the operation that owns the token lifetime.
- Hosted bridge command dispatch and WebView stop/abort command scripts must have bounded timeouts and must reject results if the WebView target or accepted page ownership changes after an await; native recovery and user stop handling should not wait forever on a hosted page promise or consume stale command results.
- Hosted bridge command CustomEvent fallback is not a handled native command by itself. Return handled only when a hosted bridge method accepts the command; otherwise native soft-resync must remain free to escalate.
- WebView status inspections must capture an accepted page version before running page script and again before publishing results. If all coalesced callers cancel, the eventual script result may satisfy the old task but must not publish UI state.
- WebView status inspection timeout or script failure should publish an owned `Unavailable` snapshot when generation/page ownership is still current, downgrade stale connected/busy shell state, and preserve the last non-empty MODEL for the same accepted page.
- Control UI issue snapshots must update the visible error InfoBar as well as status text. Auth, pairing, origin rejection, Gateway error, and terminal `Unavailable` while reconnecting should not look like a silent hang.
- `WebViewStatusInspector.cs` should stay focused on shared state, public entry points, and snapshot publication. Direct inspection/coalescing belongs in `WebViewStatusInspector.Inspection.cs`, post-navigation probing belongs in `WebViewStatusInspector.Probe.cs`, parsing belongs in `WebViewStatusInspector.Parsing.cs`, and bounded `ExecuteScriptAsync` handling belongs in `WebViewStatusInspector.ScriptExecution.cs`.
- WebView Stop fallback must stay tied to the WebView/page target captured at the start of the Stop command. If an abort or `/stop` script returns after navigation or recreation invalidates that target, the fallback must not stop the newer page.
- WinUI async event handlers that open dialogs or mutate environment/session state must guard reentry, catch/log failures, and show localized user-facing failures where applicable.
- WebView/CoreWebView2 work triggered by heartbeat, recovery, or navigation-after-load status probes must enter through an app-layer UI dispatcher before touching WebView2 or hosted bridge objects. This includes both ShellSessionCoordinator adapters and WebViewService's internal heartbeat/probe paths.
- ViewModels should receive UI dispatch from the owning window/app edge. Do not hide missing dispatch ownership by falling back to `App.MainWindow`.
- Heartbeat observations and recovery requests must carry a run id so stop/restart cannot let an old loop publish current state or trigger recovery for a new environment.
- Heartbeat loops must publish one immediate observation before waiting for the first periodic interval so the shell does not sit in a stale waiting state after foreground resume or session recovery.
- Heartbeat runtime must schedule the loop asynchronously; the immediate first observation must not run inline on the caller/UI thread.
- Hosted-session heartbeat must treat Control UI `Unavailable` as failure. A broken bridge/status inspection path must not fall through to a healthy HTTP transport result.
- Stopping heartbeat or latency probes must reset the visible HB/Ping projection. Detaching a WebView host must reset visible MODEL, access, work, recovery, heartbeat, and latency projection before the replacement session reports fresh state.
- Status inspection script execution must be bounded; a stalled WebView2 script task should not keep the shared in-flight inspection alive indefinitely.
- Page-token ownership can reject very early hosted messages; after token acceptance, native code must request a connected-shell `session-ready` replay rather than assuming the first WebView2 post was accepted.
- Page-token capture retry and native-triggered `session-ready` replay must use a lease-owned navigation cancellation scope. Reload, detach, or a newer navigation should cancel and retire the old scope, but token disposal must wait until bounded retry/replay operations release their leases; cancellation callback failures must not block scope retirement.
- A first `session-ready` event with an empty MODEL does not close the ready path permanently; later connected snapshots with a non-empty model may emit ready again so the native MODEL field can recover without reload.
- Transient `Loading`, `Unavailable`, or `Unknown` inspection snapshots must not clear the last non-empty native MODEL summary. Clear MODEL only on explicit auth/origin/Gateway issue phases or a deliberate session/environment identity reset.
- Stale-busy recovery is for chat/output activity only. Settings, Cron, config, and other non-chat shell busy states may show busy UI, but they must not trigger stale-chat recovery reloads.
- Hosted bridge document-created script ids must be removed during WebView detach so observers and poll timers do not accumulate across repeated initialization.
- Hosted bridge JavaScript belongs behind dedicated script-builder and asset seams; keep native WebView orchestration in `HostedUiBridge`, script assembly in `HostedUiBridge.Script.cs`, and executable pure JS logic in focused assets with behavior tests.
- Pure settings, diagnostics, parser, policy, telemetry, recovery, and window-bounds code should live physically under `src/OpenClaw.Core`; there are no current linked Core source exceptions.
- `OpenClaw.Core` is a pure SDK class library and must stay platform-independent. The solution exposes only x64 for the WinUI app, while Core and Core tests map that x64 solution platform to their `Any CPU` project configurations.
- Repository guardrails must fail if `OpenClaw.Core` declares architecture-specific platforms, if the solution reintroduces non-x64 platforms, or if Core/test solution mappings target anything other than `Any CPU`.
- Runtime and UI code should not introduce new synchronous waits. The current approved exceptions are shutdown drains that preserve settings/log durability and single-instance ownership release; `tools\verify-repo-structure.ps1` must fail any new `.Wait()`, `.GetAwaiter().GetResult()`, `Task.Result`, or `Thread.Sleep()` outside those approved lines.
- Unpackaged WinUI 3 language override belongs to Windows App SDK's `Microsoft.Windows.Globalization.ApplicationLanguages`; using the system `Windows.Globalization.ApplicationLanguages` API logs `Language override failed` at startup on this app.
- Diagnostics should depend on narrow session interfaces such as `IDiagnosticWebViewSession`, not concrete navigation/lifecycle services. This keeps diagnostic reporting from becoming another consumer of `WebViewService` internals.
- Direct `App.Logger`, `App.Configuration`, and `App.MainWindow` access is an app-edge allowance for `App.xaml.cs`, `MainWindow` partials, and dialog glue only. Runtime services, ViewModels, Core-compatible code, and adapters must use injected or typed dependencies.
- `SettingsViewModel` is a draft/persistence ViewModel, not a WebView runtime coordinator. The only allowed `WebViewService` call there is the existing static profile-rename helper; navigation, heartbeat, session, and recreation work stays behind MainViewModel/MainWindow/service boundaries.
- `WebViewService.cs` should stay a root partial for shared state, construction, events, state publishing, and public navigation commands. Lifecycle/current-target operations belong in `WebViewService.Lifecycle.cs`; session/profile operations belong in `WebViewService.Session.cs`.
- WebView navigation partials should stay role-focused: `WebViewService.Navigation.cs` for event/completion flow, `WebViewService.HostMessages.cs` for native host-message handling, `WebViewService.NavigationState.cs` for shared navigation ownership/cancellation helpers, `WebViewService.NavigationWatchdogs.cs` for timeout ownership, `WebViewService.NavigationCommands.cs` for CoreWebView2 command wrappers, `WebViewService.PageToken.cs` for page-token/session-ready retry, and `WebViewService.NavigationRecovery.cs` for process-failure/auto-retry recovery.
- Settings that affect live shell behavior must map to a current-process apply path, not only persisted configuration.
- Live shell settings that touch OS listener lifetime, such as multiple-instance mode, must serialize with shutdown and avoid blocking the Settings window on listener stop.
- Compact mode is a 480px layout, not just a smaller window. The visual state must collapse nonessential fixed-width top-bar segments and nonessential title actions; otherwise the remaining MinWidth values will still clip even if the outer pill minimum is reduced.
- Compact-mode loading UI must derive from both compact state and current loading state. Do not bind `LoadingRing.Visibility` directly to loading state in XAML or one-time-collapse it from code-behind.
- The full-window loading ring represents browser navigation only. `GatewayConnecting` belongs in status text and heartbeat/recovery state; it must not keep the central overlay spinner visible after the page has loaded.
- Saved compact-mode positions must be validated against current display work areas before restore; stale off-screen positions should center on the current display rather than reusing old topology.
- Prefer structured logs with stable event keys and context objects. Avoid interpolated operational logs for state transitions.
- Keep user-visible text in `StringResources` unless the string is diagnostic-only or a protocol/status token.
- Keep `.editorconfig` as the source of formatting truth. Do not rely on local IDE defaults.

Canonical local verification:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet run --no-restore --project tests\OpenClaw.Core.Tests\OpenClaw.Core.Tests.csproj
dotnet test OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
powershell -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
git diff --check
```

The Core regression harness is part of the active solution. `dotnet run` is the targeted executable harness path, and `dotnet test` is the supported VSTest workflow that invokes the same harness. The harness covers pure Core status/probe helpers plus heartbeat, latency, diagnostics, configuration normalization, recovery ownership, and diagnostic-bundle semantics. Validate with locked restore, Core harness, VSTest, x64 build, format, repository guardrails, bridge script checks, whitespace checks, and VS2026 manual debug for real WebView2/Gateway behavior. `tools\verify-repo-structure.ps1` is the active architecture and release-metadata guardrail, and `tools\verify-bridge-scripts.ps1` is the active behavior check for embedded bridge scripts. The bridge verifier requires Node.js by default, honors `OPENCLAW_NODE` when a specific Node executable is required, and skips only when `OPENCLAW_ALLOW_NODE_SKIP=1` is set explicitly.

## Active Verification Scope

Older notes mention regression tests that existed in previous checkpoints. Current active verification is:

- Core regression harness through `dotnet run --no-restore --project tests\OpenClaw.Core.Tests\OpenClaw.Core.Tests.csproj`
- solution restore/build/format
- repository structure guardrails through `tools\verify-repo-structure.ps1`
- bridge script behavior checks through `tools\verify-bridge-scripts.ps1`
- whitespace diff checks
- VS2026 manual debug on real WebView2/Gateway behavior

When a note says "Regression coverage now checks", read it as Core-harness coverage only if the behavior is pure .NET and listed in the current harness; otherwise treat it as historical context or manual-debug coverage.

Manual VS2026 debug must cover the runtime edges that local scripts cannot prove:

- hosted Gateway load, task submission, output streaming, and completion without manual reload
- MODEL display after startup, session switch, page reload, and native-triggered `session-ready` replay
- Cloudflare Tunnel or reverse-proxy 5xx pages, unexpected 4xx pages, auth/approval pages, origin rejection, and recovery after the upstream becomes healthy again
- latency tooltip `cf-ray` / Cloudflare PoP parsing against a real tunnel response
- tray show/hide, close-to-tray, reload, compact-mode menu entry, and single-instance relaunch handoff
- global hotkey and always-on-top changes saved from Settings without restarting
- compact-mode entry/exit at 480px and full-mode window-bounds restore after relaunch
- title-bar/DWM border color in light/dark/theme-switch paths, including the top 1px edge
- Log Viewer repeated refresh and close-while-loading behavior

## WinUI 3 Window Chrome And Theme Sync

This note records the debugging lesson from the v3.0.4 top-edge artifact fix.

### Symptom

The main window showed a 1px line at the very top of the custom title bar. On first launch in light mode the line appeared lighter than the title-bar surface. After switching between dark and light themes, the same edge could become black.

### Root Cause

The artifact was not a normal XAML border. It came from mixing three different ownership layers for the same visual edge:

- XAML title-bar surface (`AppTitleBar`)
- WinUI `AppWindow.TitleBar`
- native DWM caption and border attributes

The earlier `TopEdgeCover` workaround made the problem harder to reason about because it painted another 1px layer over the window. Removing that cover alone was not enough, because DWM still owned the real non-client border.

The stable fix was to make every layer use the same concrete color:

- `AppTitleBar.Background`
- `AppWindow.TitleBar.BackgroundColor`
- `AppWindow.TitleBar.InactiveBackgroundColor`
- `DWMWA_CAPTION_COLOR`
- `DWMWA_BORDER_COLOR`

Avoid relying on `Colors.Transparent` for title-bar surfaces that are not caption buttons. Avoid `DWMWA_BORDER_COLOR = COLOR_NONE` when the visual goal is a seamless colored top edge; set an explicit border color instead.

### Debugging Rules

- Treat custom title-bar artifacts as a multi-layer problem first, not as a XAML layout problem.
- Sample pixels from screenshots before changing code. A 1px white, black, or mismatched line usually reveals whether XAML, Mica, or DWM owns the visible edge.
- Do not cover native frame bugs with an extra XAML strip unless the native layer has already been proven impossible to control.
- Theme changes must go through the full native frame refresh path. Updating only managed XAML colors can leave DWM using stale light/dark state.
- Keep the main window and settings window on the same `WindowFrameHelper` contract so fixes do not diverge.

### Implementation Checklist

When changing window chrome or theme behavior:

1. Update the XAML title-bar surface.
2. Update `AppWindow.TitleBar` foreground, background, inactive, hover, and pressed colors.
3. Update DWM immersive dark mode, caption color, text color, and border color.
4. Refresh the non-client frame after theme changes.
5. Verify both startup theme and runtime dark/light switching.

Commands used for baseline verification:

```powershell
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
```

## System Tray Win32 Integration

This note records the v3.1.0 tray icon and right-click menu debugging path.

### Symptoms

The tray icon initially failed to appear after minimizing to tray. After the icon appeared, right-clicking the status-bar tray icon did not open the context menu and produced no visible error.

### Root Causes

The first failure came from mixed Win32 string marshalling. The service called explicit `*W` entry points such as `RegisterClassExW`, `CreateWindowExW`, `LoadImageW`, and `AppendMenuW`, but the `DllImport` declarations did not all specify `CharSet.Unicode`. That allowed the registered class name and created window class name to diverge, producing `CreateWindowExW` error `1407`.

The right-click failure had two separate causes:

- `NOTIFYICON_VERSION_4` reports the mouse event in `LOWORD(lParam)` and the icon id in the high word. Comparing the whole `lParam` against `WM_CONTEXTMENU` or `WM_RBUTTONUP` ignores the right-click event.
- `TrackPopupMenu` needs an owner window that can participate in foreground activation. A message-only `HWND_MESSAGE` window is useful for receiving messages, but it is not a reliable owner for a visible popup menu. Use a hidden normal top-level window instead.

### Implementation Rules

- Every explicit Win32 `*W` import that accepts strings must declare `CharSet = CharSet.Unicode`.
- Keep the tray callback window alive for the entire tray icon lifetime and destroy it only during `TrayIconService.Dispose()`.
- When using `NOTIFYICON_VERSION_4`, decode the callback event with a low-word helper before dispatching mouse actions.
- Use a hidden normal owner window for `TrackPopupMenu`; do not pass `HWND_MESSAGE` as the menu owner.
- Keep right-click tray commands intentional and bounded: Open OpenClaw, Reload, View Logs, Compact Mode, Settings, and Exit. Left-click can remain the quick show/hide toggle.

Historical harness coverage checked the Unicode imports, `LOWORD(lParam)` callback parsing, the bounded tray command set, and the hidden normal owner-window requirement. The current Core harness does not cover tray behavior, so keep these behaviors in the VS2026 manual debug checklist or add guardrails before changing tray code.

## Single Instance Launch Coordination

v3.1.1 keeps `AllowMultipleInstances` off by default because the common Windows workflow is one remote OpenClaw client parked in the tray. In the current Settings UI, the setting lives under Settings > Shell as "Multiple instances"; when enabled, launches keep the existing behavior and create another app window.

When multiple instances are disabled, startup acquires a named single-instance semaphore and the primary instance listens on a named pipe for activation requests. A secondary launch loads settings first, detects the lock owner, sends an activation request to the primary instance with async named-pipe connect/write, and exits. If activation fails because the primary is already exiting, the takeover retry uses cancellable async delay with one shared deadline rather than `Thread.Sleep` so relaunch handoff does not block the startup thread or spend separate full timeouts opening and acquiring the semaphore. The primary dispatches accepted activation requests back to the UI thread and calls `MainWindow.ActivateFromExternalLaunch()`, which restores a tray-hidden window instead of creating another tray icon.

Shutdown must wait for `SingleInstanceCoordinator.StopAsync()` before disposing the coordinator. The named-pipe listener can otherwise still be waiting on `WaitForConnectionAsync` while the single-instance lock and cancellation resources are released, which increases rapid-restart and relaunch handoff races. Keep final logger disposal in the app-level close path after the listener has stopped so shutdown failures remain observable. Use a named `Semaphore` rather than a named `Mutex` for the cross-process lock: live settings and shutdown paths are asynchronous, and a mutex can only be released by the thread that acquired it. `StopAsync()` is a drain, not a best-effort timeout: it cancels the listener, disposes the active pipe server to unblock `WaitForConnectionAsync`, and waits for the listener task before ownership is released.

## Window Bounds Persistence

This note records the v3.1.2 fix for a main-window visibility failure after switching the machine to dedicated-GPU direct mode.

### Symptom

The process started normally, WebView2 initialized, and the taskbar/tray entry remained present, but the main window was not visible. Debug output only showed first-chance WinRT and cancellation exceptions; the application exited cleanly when closed.

### Root Cause

The persisted window bounds in `%LOCALAPPDATA%\OpenClaw\settings.json` had been overwritten while the window was minimized or hidden:

```json
{
  "windowWidth": 160,
  "windowHeight": 28,
  "windowLeft": -32000,
  "windowTop": -32000
}
```

Those coordinates are Windows minimized-window sentinel values, not a user-visible placement. After a display topology change such as dedicated-GPU direct mode, restoring them can leave the WinUI window activated but effectively off-screen or collapsed.

### Implementation Rules

- Never persist bounds while the main window is hidden to tray or minimized.
- Treat `-32000`-style coordinates as invalid persisted state and reset them to the default visible bounds.
- Reject very small persisted sizes that match minimized caption-only dimensions.
- Before moving to saved coordinates, verify that the restored rectangle intersects one of the current `DisplayArea` work areas.
- If saved coordinates no longer intersect any display, center the window on the current display instead of trusting stale topology.

Historical harness coverage checked both sides of the fix: settings load sanitizes minimized sentinel bounds, and `SaveWindowBounds()` skips hidden/minimized windows. The current Core harness does not run WinUI windowing behavior, so preserve this through review, guardrails, or manual debug when window-bounds code changes.

### Settings Dialog Width Floor

The Settings dialog uses a fixed 160px sidebar plus 24px content padding on each side. Several right-pane controls have a 220px minimum width, so the persisted Settings-window width floor must stay at or above 428px. This protects reopened Settings windows from restoring into a layout narrower than the sidebar plus the minimum usable form surface.

Keep `WindowBoundsUtilities.MinimumPersistedSettingsWindowWidth` aligned with the repository guardrail in `tools/verify-repo-structure.ps1` before changing Settings navigation width, content padding, or right-pane control minimums.

## Always-On-Top And Pin State

This note records the v3.2.0 always-on-top follow-up after testing on a machine using dedicated-GPU direct mode.

### Symptoms

The WinUI `OverlappedPresenter.IsAlwaysOnTop` state could appear enabled in app code while the native window did not reliably stay above other applications on that display path. After adding the title-bar Pin affordance, the inactive Pin state also became too faint on light title-bar backgrounds when its foreground fell back to the default subtle button styling.

### Implementation Rules

- Apply always-on-top through both `OverlappedPresenter.IsAlwaysOnTop` and a native `SetWindowPos` fallback using `HWND_TOPMOST` / `HWND_NOTOPMOST`.
- Keep the Pin button state theme-aware. Use `AccentTextFillColorPrimaryBrush` for the active pinned state and `TextFillColorSecondaryBrush` for the inactive state instead of clearing the foreground to `null`.
- Update both the `Button.Foreground` and the nested `FontIcon.Foreground`; the icon is the visible state indicator.
- Persist only the user preference in settings. Reapply the native topmost state from that preference when the main window is initialized.
- Preserve the integration with guardrails or manual verification that assert the native fallback path and the theme-aware Pin colors are present.

## Hosted OpenClaw UI Status Bridge

This note records the v3.3.1 follow-up for status-bar model display and WebView2 CPU spikes when heavy OpenClaw Control UI surfaces are open.

### Symptoms

The top status MODEL field could stay empty even though the hosted OpenClaw chat page had a selected model. After the model value was detected, the old top status pill still truncated long provider/model labels too aggressively before the AUTH indicator.

Opening heavy hosted UI areas such as Communications and Automation/Cron also caused WebView2 CPU spikes. The right-sidebar fix helped sidebar content, but settings/config/Cron pages could still trigger repeated native status probes while Lit rerendered large DOM regions.

### Root Cause

MODEL was not just a XAML binding issue. During startup and session switches, the visible DOM controls can be absent or not yet ready while the OpenClaw Lit root already has the real session state. The bridge must read `openclaw-app` state first, including `sessionKey`, `chatModelOverrides`, `sessionsResult.defaults`, `sessionsResult.sessions`, and `chatModelCatalog`.

The CPU issue came from treating most DOM mutations as status-relevant. Communications, settings/config, and Cron pages render many controls, `details` sections, status chips, markdown/JSON blocks, and run/job lists. Those mutations do not change the native shell status, but the bridge was still scheduling page-level inspection from them.

### Implementation Rules

- Prefer OpenClaw app state for connected-page status before scanning DOM text.
- Read model state from the OpenClaw Lit root before falling back to visible selectors.
- Preserve the last non-empty native model summary across transient connected, unavailable, or unknown snapshots for the same accepted page.
- Exclude status-irrelevant heavy regions from status mutation probes: sidebar, hosted preview frames, settings workspace body, config content/forms, and Cron workspace/summary.
- Do not observe high-volume `class` attribute churn from Lit rerenders for native status updates.
- Use explicit low-cost events, such as `change`, for user selection changes that can affect status.
- Keep bridge status work split by responsibility: `ModelResolver` for app-state MODEL resolution, `ModelDomFallback` for visible MODEL controls, `ActivityState` for stale-busy/activity signatures, `PhaseClassifier` for auth/Gateway text matching, and `StatusInspection` for composition only.
- Keep the status pill wide enough for common provider/model labels, but continue using ellipsis for extreme names or narrow windows.

### Remaining Caveat

The v3.3.1 behavior is a usable mitigation, not proof that every future OpenClaw Control UI page will remain cheap. If upstream class names or page structure changes, re-check the excluded selectors against the current OpenClaw `app-render`, `config`, `channels`, and `cron` views before tuning WebView2 or WinUI code.

## Hosted Chat Stale-Stream Recovery

This note records the v3.3.2 follow-up for hosted chat sessions that keep showing a busy output state even though a manual reload reveals the completed Gateway result.

### Symptom

After submitting a task in the hosted Control UI, output can appear stuck until the user clicks Reload. The result then appears immediately after refresh, which indicates the remote Gateway run often completed and persisted state, but the current WebView session missed or stopped applying chat events.

### Root Cause

The Manager shell only owned the hosted WebView session, not the upstream Gateway WebSocket event stream. It could detect page-level connected/auth/error states, and it had an optional `reportSeq` gap path, but it did not have a fallback signal for the common half-broken case where the page still reports `connected` and `busy` while chat activity stops advancing.

### Implementation Rules

- Treat a connected busy chat session as suspicious when its app-state or visible activity signature does not change for the stale threshold.
- Prefer soft recovery first: lightweight sync and recent-message fetch before full reload.
- Escalate stale busy recovery to hard refresh once the soft-resync budget is exhausted; do not treat a stale connected snapshot as a successful reload fallback.
- Keep reload protection for focused inputs only when the focused editor contains unsent text. An empty focused editor should not block recovery.
- Include phase, busy, stale duration, and focused-input text state in diagnostics so tunnel/proxy and app-state failures can be separated later.

## Heartbeat And Page-Token Recovery Ownership

This note records the final v3.0.0 refactor-validation hardening for hosted sessions that move from a connected page into an unavailable or tokenless state.

### Symptoms

Two related review findings remained after the main WebView and bridge split:

- A hosted-session heartbeat inspection could publish `Unavailable` directly, which moved the shell into `Reconnecting` and caused resource scheduling to stop heartbeat before the heartbeat failure counter and recovery path could complete.
- If native page-token capture never succeeded after navigation, the page could remain stuck around `PageLoaded` / `GatewayConnecting` without a concrete failure snapshot for recovery.

### Root Cause

The problem was split ownership. Heartbeat was both asking for hosted-session state and indirectly letting that inspection publish UI state. At the same time, resource scheduling only kept heartbeat alive for `Connected` / `Connected`, so a heartbeat-owned `Unavailable` transition could stop the very loop that needed to recover it.

Page-token capture had the opposite problem: ownership validation rejected stale or missing tokens correctly, but exhausting retries only logged the condition. The status/recovery pipeline needed a current-generation `Unavailable` snapshot to make progress.

### Implementation Rules

- Heartbeat hosted-session inspections use `publishSnapshot: false`; heartbeat failure accounting decides when recovery is requested.
- Resource scheduling keeps heartbeat alive for owned `ConnectionState.Reconnecting` plus `ControlUiPhase.Unavailable` states.
- Heartbeat recovery stops only the current run under the run-id gate before raising `HeartbeatFailed`.
- Exhausted page-token capture publishes an owned `Unavailable` snapshot with a stable message.
- ShellSessionCoordinator recovery inspections carry the active recovery operation cancellation token before deciding reload fallback or recovery completion.
- ShellSessionCoordinator must not keep stale Ready/Healthy recovery projections after terminal hosted-session failures. `GatewayError` and `Unavailable` snapshots should move recovery state into degraded/failure handling.

## ShellSessionCoordinator Observed Recovery Cancellation

This note records the follow-up hardening for recovery tasks that start from event callbacks or foreground resume.

### Root Cause

The explicit reconnect / soft-resync / hard-refresh operations carried their operation cancellation token into WebView inspection, but the pre-recovery decisions for event gaps and background resume still used uncancellable inspection calls. That left a window where detach, reset, or service replacement could cancel the main recovery operation while an older foreground-resume or event-gap inspection continued waiting on WebView2 and later made a recovery decision against a replaced hosted session.

### Implementation Rules

- Event-gap, heartbeat-triggered, stale-busy, and foreground-resume recovery work owns a cancellable observed-operation CTS.
- Attach, detach, reset, and dispose paths cancel observed recovery operations before replacing WebView/bridge services.
- Foreground resume links the caller lifetime token with the coordinator observed-operation token.
- Recovery inspection helpers pass the active token into `InspectControlUiStateAsync`.
- Public reconnect, soft-resync, and hard-refresh requests link the caller cancellation token into the actual recovery operation CTS before queueing inspections, bridge commands, or reloads.
- Reconnect and hard-refresh reloads pass the same active recovery operation token through `IShellSessionWebView` and the WinUI UI-dispatch adapter.
- Reconnect and soft-resync bridge commands pass the same active recovery operation token through `IShellSessionBridge`, the WinUI UI-dispatch adapter, and `HostedUiBridge.SendCommandAsync`.
- UI-dispatched reloads and bridge commands must be cancellable while queued; bridge commands must also be cancellable while waiting on `ExecuteScriptAsync`; detach/reset/service replacement must not leave old reloads or in-page commands running against a new hosted session.
- Synchronous UI-dispatched WebView2 operations use cancellable sync dispatcher overloads. Do not wrap synchronous WebView2 work in `Task.FromResult` only to reach the async cancellation path.
- `OperationCanceledException` must be rethrown from recovery inspection helpers; cancellation is not a failed inspection and must not become reconnect fallback.

## Final UI And Process-Failure Cleanup

This note records the final v3.0.0 review cleanup for two narrow lifecycle edges found after local verification.

### Root Cause

Most service-to-ViewModel status updates were already marshalled through the injected UI dispatcher, but the dispatched callback itself did not have a local exception boundary. A projection bug in status, heartbeat, or latency UI code could therefore escape on the UI thread after the enqueue succeeded.

WebView process failure already invalidated status inspection generation and page ownership, but it did not actively retire the navigation retry/session-ready replay cancellation scope. The generation checks prevented stale state writes, but old bounded retry/replay work could still wait until its timeout instead of being cancelled immediately.

### Implementation Rules

- ViewModel UI dispatcher callbacks are wrapped in a catch/log boundary; failed projections are logged and do not escape as unhandled UI-thread exceptions.
- WebView process-failure handling cancels and retires navigation retry/replay ownership before publishing the unavailable/error state.
- Repository guardrails protect both contracts.

## Navigation Start Timeout Late Completion Recovery

This note records the follow-up for the VS2026 debug symptom where the shell could report `Navigation did not start within 12s` even though the hosted page later completed navigation.

### Root Cause

The start watchdog treated its cancellation source as both the timer lifetime and the pending navigation target ownership. When the 12s timer fired, the watchdog task cleared that ownership while the shell requested WebView recreation. A late `NavigationCompleted` for the same target could no longer claim the pending navigation, so the shell could stay in a degraded loading/reconnecting path until a manual refresh forced a new status pass.

### Implementation Rules

- Keep pending start-watchdog target ownership separate from the watchdog CTS.
- After the 12s start timeout fires, retain the pending target for a bounded recovery window so a target-matching late `NavigationCompleted` can still claim the navigation.
- Expire that pending target after the bounded window if no late completion arrives.
- Do not cancel navigation retry/replay cancellation on the first start-timeout notification; a late completion still needs the current navigation lease to capture page token and request `session-ready` replay.
- If a late completion recovers the page before the timeout-triggered WebView recreation runs, cancel only the queued or deferred `navigation_start_timeout` / `navigation_completion_timeout` recreation request. Do not cancel settings, initial-load, or topology-change recreation.
- WebView recreation reason merging must preserve the higher-priority reason. Timeout-driven recovery is lower priority than settings, environment, initial-load, session, or topology recreation, and a deferred recreation that resumes after visible layout returns must schedule the original reason rather than replacing it with a generic layout-ready reason.
- `WebViewRecreationService` owns deferred recreation reason state as well as pending recreation state. `MainWindow` may decide whether the WinUI host is visible and swap controls, but it should not carry a separate deferred-reason field or duplicate timeout-recovery cancellation rules.
- If the shell host is visible but the newly-created WebView2 child does not become loaded and non-zero sized before the short layout wait expires, requeue the original recreation reason through `WebViewRecreationService.Schedule(...)` so the existing timer and circuit breaker own the retry cadence.
- Treat the default `https://example.com` environment as a first-run placeholder, not as a navigable Gateway. While selected, skip WebView2 host creation, clear any old host, stop heartbeat/latency probes, and show the localized configure-Gateway status so first-run startup cannot look like a stuck navigation.
- Placeholder selection must also cancel an active WebView recreation loop, and WebView initialization must capture environment name/URL before awaits and re-check that selected-environment identity before bridge attach or navigation.
- Placeholder layout-resume events should clear the WebView host only when there is pending, deferred, or active recreation work or a stale WebView2 child to remove; empty layout events should not spam `deferred_resume_placeholder` skip logs and obscure startup diagnostics.
- Repository guardrails must cover the bounded late-completion window and the timeout-recreation cancellation path.

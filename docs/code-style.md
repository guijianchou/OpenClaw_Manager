# OpenClaw Code Style

This guide is the project-specific layer on top of `.editorconfig`, `.gitattributes`, .NET analyzers, and WinUI conventions. Keep code small, explicit, and boring.

## Formatting

- `.editorconfig` and `.gitattributes` are the source of truth for formatting.
- Use UTF-8, LF line endings, one final newline, and no trailing whitespace.
- Use 4-space indentation for C#, XAML, project files, resources, manifests, and JSON.
- Prefer file-scoped namespaces.
- Keep braces on every `if`, loop, and branch.
- Keep nullable analysis enabled and address warnings before commit.
- Run `dotnet format OpenClaw.sln --verify-no-changes --no-restore` with `Platform=x64` before handoff.

## C# Rules

- Prefer clear control flow over clever expressions.
- Use `var` only for built-in types or when the right-hand side makes the type obvious.
- Keep comments sparse and useful. Explain ownership, lifecycle, or non-obvious platform behavior.
- Keep user-visible strings in `StringResources` typed properties and `.resw` files. Diagnostic-only text and protocol/status tokens may stay close to the code that emits them.
- Keep English and Chinese `.resw` resource keys aligned; `tools\verify-repo-structure.ps1` must fail if a resource key exists in only one locale.
- WinUI language overrides must use `Microsoft.Windows.Globalization.ApplicationLanguages` from Windows App SDK. Do not use `Windows.Globalization.ApplicationLanguages` in this unpackaged WinUI app.
- Prefer structured logs with stable event keys and context objects for state transitions.
- Background work owns its lifetime explicitly: store the `Task`, store cancellation state, and observe shutdown exceptions.
- Do not add synchronous waits in runtime/UI paths. The only approved `.Wait()` / `.GetAwaiter().GetResult()` calls are documented shutdown drains for settings flush, logger flush, and single-instance listener ownership release.
- Long-running UI commands must use `AsyncCommand` or an equivalent reentry guard so repeated clicks cannot start concurrent command work; reset command state in `finally` and observe/log failures.
- Large filesystem work started by UI commands or Settings must run off the UI thread. Log enumeration, diagnostic bundle compression, and inactive WebView2 profile deletion are not allowed to run synchronously in dialog or command handlers.
- Deferred/coalesced background workers must serialize queue state with their lifetime gate, then be cancelled, drained, or observed before shutdown flush paths dispose shared logger/configuration services.
- Long-lived OS listeners such as single-instance named pipes must be stopped through an explicit wait path before their coordinator is disposed or cross-process ownership is released.
- Secondary-launch activation and activation-failure takeover waits must stay async and share one bounded takeover deadline; avoid synchronous pipe connect and `Thread.Sleep` in startup handoff paths.
- Runtime settings changes that start or stop long-lived listeners must use observed async apply paths instead of synchronously waiting on the Settings/UI save path.
- Settings saves must merge only fields edited in the currently open Settings window; unchanged stale snapshots must not overwrite live shell changes made elsewhere while the dialog is open. Same-value setter writes from two-way binding initialization must return before marking settings fields dirty.
- Cross-process single-instance ownership must use a named `Semaphore`, not a named `Mutex`, because live settings and shutdown paths are asynchronous and may release ownership from a different thread.
- WebView/CoreWebView2 async work must carry cancellation and generation ownership before applying state after an `await`.
- Programmatic WebView navigation, reload, and retry must invalidate accepted page ownership and clear the accepted navigation id before the CoreWebView2 call, not only in `NavigationStarting`.
- `NavigationCompleted` must tolerate a missing `NavigationStarting` when a start watchdog is still active and the navigation id has not been claimed; this is a WebView2 event-delivery edge, not a network failure.
- If a successful `NavigationCompleted` arrives after completion-timeout recovery cancelled navigation cancellation ownership, and the navigation generation/id/host are still current, recreate navigation cancellation ownership and run the normal page-token/status-probe path rather than returning before recovery cleanup can complete.
- Navigation completion timeouts must validate an active completion-watchdog id, not only the current page generation and navigation id, so queued timeout callbacks cannot publish recovery after successful completion.
- Reload must report whether CoreWebView2 actually accepted the command; recovery code must treat a no-op reload as failed recovery instead of advancing to Connecting.
- Manual Retry and Reload must not clear visible errors until WebViewService reports that navigation actually started; when no retryable WebView navigation exists, keep a localized actionable error visible.
- Auto-retry continuations must re-check generation/navigation/WebView ownership after their delay, return stale when another navigation has taken over, and publish Error if retries are exhausted or the retry command cannot start.
- WebView/CoreWebView2 async event handlers must delegate awaited work to observed helpers and log failures instead of letting exceptions escape `async void` handlers. Navigation-completion handler failures must also publish `Unavailable` plus Error rather than leaving stale Loading state.
- WebView status probe loops must store their task and cancellation source; stop paths cancel only, while the running probe task disposes its cancellation source in `finally`.
- WebView status probe loops must publish an owned `Unavailable` snapshot when all bounded post-navigation probes are exhausted without a terminal Control UI phase.
- `ControlUiPhase.Unavailable` is terminal for post-navigation status probes; recovery owns follow-up after that state, so probes should not keep executing page scripts.
- Control UI latency probe loops follow the same ownership rule: keep the task observed, make stop paths cancel only, let the running probe dispose timer/cancellation resources, and reject stale run results before publishing UI state.
- WebView host recreation must detach the coordinator, hosted bridge, and WebView service from the outgoing control before closing old WebView2 instances.
- WebView host recreation must wait for a visible non-compact/non-minimized host and a loaded, visible, non-zero-sized WebView2 child before initialization or navigation. If the shell host is unavailable, defer recreation instead of navigating into a non-presented control. If the child layout times out, requeue through the normal recreation timer and count it as a circuit-breaker attempt.
- A late successful navigation recovery must be able to cancel pending, deferred, or already-active timeout-only WebView recreation before that recreation detaches the recovered host.
- WebView host recreation exceptions must surface a localized actionable InfoBar error with Retry. Do not only log after timeout recovery hid the previous InfoBar.
- Cancellation sources handed to running async operations should have one disposal owner; external abort paths cancel them and let the owning operation dispose them.
- WinUI async event handlers that show dialogs or clear environment/session state must guard reentry, catch/log failures, and use localized user-facing error text.
- WebView2 and hosted bridge operations that originate from background recovery, heartbeat, or status-probe paths must marshal through the app-layer UI dispatcher, including WebViewService's own heartbeat and navigation-after-load inspection loops.
- Failed UI dispatcher enqueue attempts must fail or drop the work with logging; they must not run WebView2 or WinUI work inline on the originating background thread.
- Injected ViewModel UI updates must wrap dispatched callbacks in a catch/log boundary so a bad status or heartbeat projection cannot escape as an unhandled UI-thread exception.
- Synchronous WebView2 UI actions that need queue-time cancellation should use cancellable sync dispatcher overloads instead of wrapping the action in `Task.FromResult`.
- ViewModels must receive UI dispatch through constructor injection from the owning window or app edge; do not read `App.MainWindow` as a fallback.
- Hosted bridge messages must carry native owner/page tokens and C# must reject mismatched owner/page/generation values before applying status, session-ready, or event-gap state. Do not reject hosted bridge messages by `CoreWebView2` wrapper reference identity.
- After native page-token acceptance, the host must request a connected-shell `session-ready` replay so early messages rejected by ownership filtering are not lost.
- Page-token capture retry and native-triggered `session-ready` replay must use a lease-owned navigation cancellation scope. Reload, detach, or newer navigation cancels and retires the old scope, token disposal waits for bounded retry/replay leases to release, and cancellation callback failures must not block scope retirement.
- Page-token retry exhaustion must publish `Unavailable` through the captured navigation generation; do not use the current generation after the final retry loop because a newer navigation may have started.
- Stop fallback is part of navigation ownership. After `CoreWebView2.Stop()`, cancel navigation watchdogs, status probes, page ownership, and navigation cancellation so stopped loads cannot publish stale recovery later.
- WebView process-failure handling must cancel and retire navigation retry/replay ownership before publishing unavailable/error state.
- Transient `Loading`, `Unavailable`, or `Unknown` status snapshots must not clear the last non-empty MODEL. Clear it only when explicit auth/origin/Gateway issue phases or deliberate session/environment identity reset means the current page/session identity is no longer trustworthy.
- Hosted bridge document-created scripts must store their WebView2 script id and remove it on detach; repeated initialization must not accumulate old observers or timers.
- WebView recreation and WebView/bridge initialization must honor window/ViewModel lifetime cancellation before subscribing events or writing initialized state.
- Fire-and-forget recovery work must be observed through a helper that catches cancellation, disposal, and unexpected exceptions.
- ShellSessionCoordinator event-gap, heartbeat-triggered, stale-busy, and foreground-resume recovery work must own cancellable observed-operation CTS instances. Attach, detach, reset, and dispose paths cancel those operations before replacing WebView/bridge services.
- ShellSessionCoordinator public reconnect, soft-resync, and hard-refresh requests must link the caller cancellation token into the actual recovery operation CTS before queueing inspections, bridge commands, or reloads.
- ShellSessionCoordinator must not leave `RecoveryState` in a stale Ready/Healthy projection after terminal hosted-session failures; `GatewayError` and `Unavailable` should move recovery state to degraded/failure handling.
- Recovery inspection helpers must pass their active cancellation token into `InspectControlUiStateAsync` and must rethrow `OperationCanceledException`; cancellation must not be converted into reconnect fallback.
- ShellSessionCoordinator reconnect and hard-refresh reloads must pass the active recovery operation cancellation token through the Core WebView contract and app-layer UI dispatcher adapter.
- ShellSessionCoordinator reconnect and soft-resync bridge commands must pass the active recovery operation cancellation token through the Core bridge contract, app-layer UI dispatcher adapter, and hosted bridge command execution path.
- WebView2 script probes and hosted command dispatch must have bounded timeouts and post-await current-target checks so coalesced callers, recovery operations, and user stop handling cannot inherit an indefinitely stuck page script or consume stale results from a replaced WebView.
- WebView status inspections must capture an accepted page version before executing page script, require that version before publishing, and suppress publication when all coalesced callers have cancelled.
- WebView status inspection timeout or script failure must publish an owned `Unavailable` snapshot when generation/page ownership is still current, downgrade stale `Connected` shell state, and preserve the last non-empty MODEL for the same accepted page.
- Control UI issue snapshots such as auth required, pairing required, origin rejected, and Gateway error must make the user-facing error InfoBar visible; terminal `Unavailable` snapshots must also show a visible InfoBar while the shell is reconnecting. Do not only change status text.
- Hosted bridge commands and WebView stop/abort command results must also validate the accepted page-ownership version after awaits, because same-WebView navigation can invalidate the page without replacing the `CoreWebView2` object.
- Hosted bridge CustomEvent fallback must not be reported as a handled command unless a hosted bridge method actually accepted the command.
- WebView Stop fallback must remain bound to the original WebView/page target captured at command start; a stale hosted command rejection must not call `Stop()` on a newer page.
- Heartbeat observations and recovery requests must carry a run id so stop/restart cannot let an old heartbeat loop publish current state or trigger recovery for a new environment.
- Heartbeat-triggered recovery must stop only the current heartbeat run before publishing `HeartbeatFailed`; stale runs must return without stopping or recovering a newer run.
- Hosted-session heartbeat inspections must not publish Control UI snapshots directly. Heartbeat owns failure accounting first, then recovery/status transitions publish the user-visible state.
- Resource scheduling must keep heartbeat active for owned `ConnectionState.Reconnecting` plus `ControlUiPhase.Unavailable` states so the recovery loop can continue observing a broken hosted session.
- Heartbeat loops must publish one immediate observation before waiting for the first periodic interval.
- Heartbeat runtime must schedule loops asynchronously instead of running the immediate first tick inline on the caller thread.
- Hosted-session heartbeat maps Control UI `Unavailable` to failure; broken bridge/status inspection must not fall through to healthy transport.
- Gateway heartbeat transport treats missing Control UI paths, rejected heartbeat probes, 5xx, and unexpected 4xx responses as failures.
- Exhausted native page-token capture after navigation must publish an owned `Unavailable` snapshot instead of leaving the shell in `PageLoaded` or `GatewayConnecting` indefinitely.
- ShellSessionCoordinator recovery inspections must carry the active recovery operation cancellation token before making reload fallback decisions.
- MainViewModel latency updates must reject snapshots whose host no longer matches the selected environment.
- Stopping heartbeat or latency probes must reset the visible HB/Ping projection, and WebView detach/recreation must reset MODEL, access, work, recovery, heartbeat, and latency projections before a replacement session reports fresh state.

## XAML Rules

- Shared visual constants live in focused dictionaries under `src/OpenClaw/Styles/` and are merged from `App.xaml`.
- Repeated status-bar typography, sizing, and spacing use semantic resources or styles, not repeated literals.
- Prefer `ThemeResource`, WinUI system brushes, and app resources over hard-coded theme colors.
- Settings boolean rows use CommunityToolkit `SettingsCard` plus right-aligned `ToggleSwitch`.
- Keep XAML responsible for layout and binding. Put state transitions and behavior in the view model or service layer.
- Compact-mode visual states must collapse nonessential fixed-width top-bar segments and nonessential title actions at 480px; reducing only the outer pill minimum is not enough.
- Compact-mode loading UI must be derived from compact state plus `ViewModel.IsLoading`; `LoadingRing.Visibility` must not be bound directly to loading state where compact mode can re-show it.
- Full-window loading UI represents active browser navigation only. `GatewayConnecting` should stay visible in status text and recovery state without keeping the central loading overlay active.
- Compact-mode saved positions must be validated against current display work areas and centered on the current display when stale.

## Architecture Boundaries

- The WinUI layer owns XAML, windows, WebView2 controls, title-bar behavior, tray/hotkey integration, and app-only adapters.
- The Core physical source tree (`src/OpenClaw.Core`) owns pure settings, diagnostics formatting, parser, policy, telemetry, and recovery logic.
- Define "Core" as WinUI-free. Core-compatible files must not reference `Microsoft.UI`, `Microsoft.Web.WebView2`, XAML types, Windows App SDK packages, or `App`.
- Core-compatible files physically live under `src/OpenClaw.Core`; do not add linked Core source files unless a migration plan explicitly scopes a short-lived transition.
- `OpenClaw.Core` must remain a platform-independent SDK class library. The solution may expose `x86`, `x64`, and `ARM64` for the WinUI app, but Core mappings must target `Debug|AnyCPU` or `Release|AnyCPU` because VS2026 validates project configurations before build.
- There are no current linked Core source exceptions. WinUI adapters should convert platform objects into plain Core types at the app boundary.
- `WebViewStatusInspector` owns generation-scoped and accepted-page-version-scoped Control UI inspection and must not let stale async script results update current state.
- `WebViewStatusInspector.cs` owns shared state, public entry points, and snapshot publication. Keep direct inspection/coalescing in `WebViewStatusInspector.Inspection.cs`, post-navigation probing in `WebViewStatusInspector.Probe.cs`, Control UI snapshot parsing in `WebViewStatusInspector.Parsing.cs`, and bounded script execution in `WebViewStatusInspector.ScriptExecution.cs`.
- `HeartbeatRuntime` owns heartbeat task/cancellation lifetime; heartbeat policy code should not recreate CTS/task ownership, and hosted-session inspection must use the injected UI dispatcher before touching WebView2.
- `GatewayHeartbeatTransport` owns HTTP probing, and `HostedSessionHeartbeatPolicy` owns hosted Control UI phase-to-heartbeat mapping.
- `HostedSessionHeartbeatPolicy` must treat `Unavailable` as failure so a broken page bridge does not appear healthy because transport still responds.
- `WebViewRecreationService` owns recreation scheduling, deferred/pending reason state, merge accounting, and circuit-breaker decisions; `MainWindow` should keep the actual WebView2 control swap.
- The default `https://example.com` environment is a first-run placeholder. While it is selected, `MainWindow` must not create, initialize, or navigate a WebView2 host; it should clear any old WebView host and leave status projection on the localized configure-Gateway state.
- WebView initialization must capture selected environment name and URL before awaits and re-check that identity before bridge attach and navigation. A late environment switch to the placeholder must cancel active recreation, not just clear queued recreation.
- Placeholder WebView cleanup should be signal-bearing. Layout resume events in placeholder mode should log a skip only when pending/deferred/active recreation work exists or a stale WebView2 child is removed.
- `LiveShellSettingsApplier` owns current-process application of live shell settings such as always-on-top and global hotkey changes.
- Live multiple-instance setting changes must serialize listener ownership with app shutdown and must not block Settings save while waiting for the named-pipe listener to stop.
- `SingleInstanceCoordinator` owns the named semaphore and named pipe. Keep the pipe name stable for activation handoff, keep secondary-launch activation/takeover waits asynchronous under one shared deadline, and treat legacy same-name lock conflicts as secondary launches instead of crashing startup.
- Single-instance shutdown is a deliberate drain: app close waits for listener stop before releasing ownership, but ordinary settings/startup/recovery code must stay async and must not add new synchronous waits.
- `SettingsPersistenceAdapter` owns the `App.Configuration` boundary for settings save/load from the settings UI.
- `SettingsViewModel` must stay a settings draft/persistence ViewModel. It may use `WebViewService.TryMoveUserDataFolderToRenamedEnvironment` for the existing profile-rename migration only; runtime WebView navigation, heartbeat, session, and recreation operations stay behind MainViewModel, MainWindow, and focused services.
- `AppRuntimeContext` owns logger/configuration access for `MainViewModel`; do not reintroduce `App.Logger` or `App.Configuration` inside ViewModel partials.
- Direct `App.Logger`, `App.Configuration`, and `App.MainWindow` access is restricted to WinUI app-edge files such as `App.xaml.cs`, `MainWindow` partials, and dialog glue. Services, ViewModels, Core-compatible code, and adapters must receive logger/configuration/window state through typed dependencies.
- `StatusPresenter` owns pure status text/brush/mode formatting and should not mutate bindable ViewModel state.
- `DiagnosticService` depends on `IDiagnosticWebViewSession`; do not couple diagnostic checks directly to concrete `WebViewService` lifecycle/navigation internals.
- `WebViewService.cs` should remain the small root partial for shared state, construction, events, state publishing, and public navigation commands. Keep WebView2 initialization/detach/dispose/current-target checks in `WebViewService.Lifecycle.cs`, and session/profile operations in `WebViewService.Session.cs`.
- `WebViewService.Navigation.cs` owns WebView2 navigation event entry and completion flow only. Keep host-message handling in `WebViewService.HostMessages.cs`, shared navigation ownership/cancellation helpers in `WebViewService.NavigationState.cs`, watchdogs in `WebViewService.NavigationWatchdogs.cs`, CoreWebView2 command wrappers in `WebViewService.NavigationCommands.cs`, page-token/session-ready retry in `WebViewService.PageToken.cs`, and recovery-specific helpers in `WebViewService.NavigationRecovery.cs`.
- New protocol or parser code starts in Core-compatible files unless it directly needs WinUI/WebView2 APIs.
- For guardrail tests, keep this contract explicit: new protocol or parser code starts in Core-compatible files.

## Partial Ownership

- `MainWindow` partial files are split by responsibility: lifecycle, initialization, commands, WebView host recreation, tray, hotkey, compact mode, always-on-top, and theme.
- `MainViewModel` partial files are split by responsibility: fields, bindable properties, commands, environment selection, lifecycle, status formatting, heartbeat, indicators, and telemetry.
- `ShellSessionCoordinator` partial files are split by responsibility: dependency interfaces, attach/dispose, event routing, recovery, recovery inspection, recovery state transitions, state effects, host visibility, helpers, and telemetry.
- `WebViewService` partial files are split by responsibility: root state/events/public commands, lifecycle/current-target checks, session/profile operations, navigation event/completion flow, host-message handling, navigation state/cancellation ownership, navigation watchdogs, CoreWebView2 command wrappers, page-token/session-ready retry, process-failure/auto-retry recovery, heartbeat, Control UI inspection, hosted command injection, and profile-folder helpers.
- `WebViewStatusInspector` partial files are split by responsibility: root state/public entry points/snapshot publication, direct inspection/coalescing, post-navigation probes, JSON parsing, and bounded script execution.
- `HostedUiBridge` keeps native WebView2 bridge lifecycle in C#, while focused embedded JS assets own browser-side host messaging, mutation filtering, model resolution, MODEL DOM fallback, activity/stale-busy state, phase classification, status inspection composition, and command dispatch.
- New partial files are acceptable only when partial files are split by responsibility. Do not create catch-all "misc" or one-feature dumping grounds.

## Large File Rules

- Do not grow `WebViewService` with unrelated responsibilities. Existing command and profile helpers live in focused partials; add new focused partials for future WebView lifecycle, inspection, or recovery behavior.
- Keep native bridge orchestration in `HostedUiBridge`, localized-string/resource assembly in `HostedUiBridge.Script.cs`, and browser implementation in embedded JS assets.
- Treat `HostedUiBridge.Script.js`, `HostedUiBridge.ModelResolver.js`, `HostedUiBridge.ModelDomFallback.js`, `HostedUiBridge.ActivityState.js`, `HostedUiBridge.PhaseClassifier.js`, `HostedUiBridge.CommandDispatch.js`, `HostedUiBridge.MutationFilter.js`, `HostedUiBridge.DomUtilities.js`, `HostedUiBridge.HostMessaging.js`, `HostedUiBridge.StatusInspection.js`, `WebViewStatusInspector.Inspect.js`, and the script builders as testable script surfaces. Add executable JS behavior checks before changing model detection, mutation filtering, session-ready events, host messaging, status inspection, or command handling.
- Keep `HostedUiBridge.StatusInspection.js` as composition only. Add MODEL DOM fallback behavior to `HostedUiBridge.ModelDomFallback.js`, stale-busy behavior to `HostedUiBridge.ActivityState.js`, and auth/Gateway phase matching to `HostedUiBridge.PhaseClassifier.js`.
- Keep stale-busy recovery limited to chat/output activity. Non-chat Settings/Cron/config busy state must not become a stale-chat reload signal.
- Inline browser JavaScript longer than 30 lines should move into an embedded `.js` resource with a verifier or guardrail.
- Avoid large source moves unless the move itself is the purpose of the change and the test plan proves no project-file duplication changed.

## Tests

- There is no active `.NET tests/` regression harness at this checkpoint; embedded bridge behavior is still covered by the script verifier below.
- When tests are reintroduced, prefer behavior tests against Core services and fakes.
- Use source-text assertions only for contracts a harness cannot execute, such as XAML resource usage, project metadata, and platform integration declarations.
- `tools\verify-bridge-scripts.ps1` is the active behavior check for embedded JS assets. Future bridge, MODEL, mutation-filter, status-inspection, and command-dispatch changes must update that script rather than restoring `tests/`.
- Every bug fix or behavior change should have a regression path documented in the PR, manual checklist, or future test harness.

## Version And Documentation

- Version bumps update `OpenClaw.csproj`, package manifest, application manifest, README, Chinese README, and changelog.
- Keep `tools\verify-repo-structure.ps1` aligned with the active release metadata so project/package/documentation version drift fails local verification.
- README files should summarize current behavior and link to deeper docs instead of duplicating implementation notes.
- `DEVELOPMENT_NOTES.md` records debugging history and project lessons. This guide is the canonical checklist for new changes.

## Verification

Run these commands before handing off code:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
$env:OPENCLAW_NODE='C:\Users\Zen\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
powershell -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
git diff --check
```

`tools\verify-repo-structure.ps1` is the active guardrail for the no-`tests/` checkpoint, solution references, Core boundary rules, embedded bridge assets, and release metadata alignment.

`tools\verify-bridge-scripts.ps1` executes focused browser-script behavior checks for embedded JS assets and requires Node.js by default. If the default `node` command is missing or blocked, set `OPENCLAW_NODE` to a specific executable; the path above is the known Codex runtime Node path on this workstation. The script skips only when `OPENCLAW_ALLOW_NODE_SKIP=1` is set explicitly for a local, intentional skip.

# OpenClaw Manager

**Language:** English | [简体中文](readme_zh.md)

**Current version:** 5.0.1

Lightweight Windows-native OpenClaw remote management shell built with WinUI 3 and WebView2.

OpenClaw Manager is a thin desktop shell for the hosted OpenClaw Control UI. It is designed for remote Gateway deployments running on a VPS and exposed through Cloudflare Tunnel, reverse proxy, or another public HTTPS origin.

---

## Overview

This project keeps the existing OpenClaw web experience, but wraps it in a native WinUI 3 window with:

- environment switching
- per-environment WebView2 session isolation
- connection recovery and heartbeat monitoring
- diagnostics and structured logs
- native theme and window integration

It is best suited for users who:

- run OpenClaw Gateway remotely
- access it through Cloudflare Tunnel or a reverse proxy
- want a lightweight Windows-native client instead of keeping a browser tab open

## Current 5.0.1 Notes

- `5.0.1` updates the Gateway/Cloudflare status model for VPS deployments behind Cloudflare Tunnel: heartbeat, diagnostics, and latency probes now share one HTTP status classifier, the latency probe targets the documented `__openclaw__/a2ui/` Control UI path, and proxy/path failures such as 404, 405, 5xx, and Cloudflare Tunnel 1033 no longer publish as healthy latency.
- Settings persistence failures now flow back to the Settings dialog instead of being logged while the dialog closes as if the save succeeded.
- `5.0.0` remains the previous refactor-validation baseline. The v3.3.6 cleanup remains the reviewed baseline; this is not intended to rewrite the historical `v3.0.5` / `v3.0.1` / `v3.0.0` release entries in [changelog.md](changelog.md).
- [docs/code-style.md](docs/code-style.md) is the canonical code-style and architecture guide for this branch.
- The solution maps `OpenClaw.Core` from the active `x64`/`x86`/`ARM64` solution platform to the Core project's platform-independent `AnyCPU` configuration, so VS2026 can load Debug/Release x64 without configuration-manager repair.
- The default `https://example.com` environment is treated as a first-run placeholder, not as a real Control UI. While it is selected, MainWindow skips WebView2 host creation, stops heartbeat/latency probes, clears any previous WebView host, and shows the localized "Configure a Gateway URL in Settings" status instead of navigating to `example.com` or leaving the loading ring active.
- Saved language preference now uses the Windows App SDK `Microsoft.Windows.Globalization.ApplicationLanguages` API, so startup applies the configured language without the previous WinRT `Language override failed` warning.
- Runtime ownership is split across focused services: `WebViewStatusInspector`, `HeartbeatRuntime`, `GatewayHeartbeatTransport`, `HostedSessionHeartbeatPolicy`, `WebViewRecreationService`, `SettingsPersistenceAdapter`, `LiveShellSettingsApplier`, and `StatusPresenter`.
- `WebViewService.cs` now keeps shared fields, construction, events, and public navigation commands; WebView2 initialization/detach/dispose/current-target checks live in `WebViewService.Lifecycle.cs`, and profile/session operations live in `WebViewService.Session.cs`.
- `WebViewService` navigation code is split by responsibility: event/completion flow in `WebViewService.Navigation.cs`, host-message handling in `WebViewService.HostMessages.cs`, shared navigation ownership/cancellation helpers in `WebViewService.NavigationState.cs`, watchdog ownership in `WebViewService.NavigationWatchdogs.cs`, CoreWebView2 command wrappers in `WebViewService.NavigationCommands.cs`, page-token/session-ready retry in `WebViewService.PageToken.cs`, and process-failure/auto-retry recovery in `WebViewService.NavigationRecovery.cs`.
- Hosted bridge logic is split into embedded JavaScript assets for host messaging, mutation filtering, MODEL resolution, DOM fallback, activity/stale-busy tracking, phase classification, command dispatch, and status inspection.
- `WebViewStatusInspector` keeps shared state, public entry points, and snapshot publication in the main partial, with direct inspection/coalescing, post-navigation probes, Control UI snapshot parsing, and bounded script execution split into focused partials.
- WebView status probes are generation-scoped, accepted-page-version scoped, timeout-bounded, UI-dispatched before touching WebView2, and protected by owner/page-token validation so stale or cancelled inspections cannot overwrite current state; timeout, script failure, or exhausted post-navigation probes publish an owned terminal `Unavailable` snapshot, downgrade stale `Connected` shell state, move coordinator recovery state out of stale Ready/Healthy projections, show a visible InfoBar while reconnecting, stop post-navigation page-script probing, and preserve the last non-empty MODEL value through loading, unavailable, and unknown snapshots for the same accepted page.
- Hosted bridge commands and WebView stop/abort scripts have bounded execution timeouts and reject results after the WebView target or accepted page ownership changes; recovery-owned bridge commands link the active recovery cancellation token through UI dispatch and script execution, CustomEvent command fallback is no longer reported as handled unless a hosted bridge method actually accepts the command, and Stop fallback stays bound to the original page target so a stale command rejection cannot stop a newer page.
- WebView navigation, reload, manual retry, and auto-retry invalidate page ownership before issuing CoreWebView2 commands; manual Retry keeps a localized actionable error visible when no retryable navigation exists, recovery-owned reloads carry the active recovery cancellation token and check whether reload actually started before advancing, stale auto-retry continuations exit without publishing old navigation state, page-token retry plus native-triggered `session-ready` replay use a lease-owned navigation cancellation scope so reload/detach/new navigation cancels old work without disposing tokens still in use, exhausted or failed retries surface as Error instead of leaving the shell in Reconnecting, exhausted page-token capture publishes `Unavailable` only against the original navigation generation, and Stop fallback cancels navigation watchdogs/probes/page ownership so a user stop cannot later trigger stale startup timeout recovery.
- WebView startup recovery tolerates a missing `NavigationStarting` callback by letting only a target-matching `NavigationCompleted` claim the pending navigation, records the previous source for stale-completion rejection, preserves the pending target for a bounded post-timeout recovery window, recreates navigation cancellation ownership for a still-current late successful completion after completion-timeout recovery, cancels queued, deferred, or active timeout-driven WebView recreation when a late completion recovers the page, projects unexpected completion-handler failures as `Unavailable` plus Error instead of leaving Loading stale, preserves higher-priority settings/initial/session/topology recreation reasons when timeout recovery requests merge into the queue, guards completion timeouts with an active watchdog id, and keeps the full-window loading ring tied only to browser navigation `Loading` rather than the longer `GatewayConnecting` status phase.
- Dynamic WebView recreation now waits for the window shell, host panel, and new WebView2 child control to be visible and non-zero sized before initialization/navigation; compact, hidden-to-tray, or minimized startup states defer recreation without losing the original higher-priority reason, child layout timeouts are requeued through the normal recreation timer/circuit-breaker path and counted by the circuit breaker instead of retrying forever or waiting for another window event, unexpected recreation exceptions surface a localized actionable error with Retry instead of only logging after timeout recovery hid the InfoBar, and deferred recreation ownership now lives in `WebViewRecreationService` rather than the window partial.
- WebView detach/recreation and resource-stop paths now reset visible heartbeat, latency, MODEL, access, work, and shell projections before the replacement session reports state, so a stopped probe or failed recreation cannot leave stale `HB OK`, ping, `AUTH OK`, `LIVE`/`IDLE`, or previous MODEL values visible as if the old session were still current.
- Hosted bridge session/status messages use host-generation and owner/page-token ownership checks instead of CoreWebView2 wrapper reference identity, so valid `session-ready` and status posts are not dropped because WebView2 exposes a different COM wrapper object.
- WebView process failures retire navigation retry/replay cancellation before publishing the unavailable state, and injected ViewModel UI updates catch/log callback failures so dispatcher callbacks do not escape as unhandled UI-thread exceptions.
- Gateway heartbeat, diagnostics, and latency probes now share Gateway HTTP status classification for Cloudflare Tunnel and reverse-proxy deployments. Heartbeat treats proxy/path/server failures as failures, device approval or rate-limit responses as session-blocked user-action states, and hosted-session inspection `Unavailable` as a failure instead of falling through to healthy HTTP transport; latency probes use the documented `__openclaw__/a2ui/` Control UI path and do not record 404/405/5xx/1033 responses as healthy samples.
- ShellSessionCoordinator recovery work is cancellable across event gaps, stale-busy recovery, heartbeat-triggered recovery, foreground resume, in-page bridge commands, and UI-dispatched reloads; public recovery requests link caller cancellation into the active operation before queueing inspections, bridge commands, or reloads, and attach/detach/reset/dispose cancels pending work before replacing WebView or bridge services so old recovery decisions cannot run against a new hosted session.
- Settings changes for global hotkey, always-on-top, and multiple-instance behavior apply to the running process; Settings save merges only fields edited in the open dialog so stale snapshots do not overwrite live Pin/hotkey/environment changes, same-value two-way binding writes do not mark settings fields dirty, multiple-instance listener changes are observed asynchronously so Settings save does not wait on named-pipe shutdown, secondary-launch activation/takeover waits are async and share one bounded takeover deadline so relaunch handoff does not block startup, and the cross-process single-instance lock uses a named semaphore while shutdown waits for the named-pipe listener to stop before releasing ownership; compact mode uses visual states that collapse nonessential fixed-width top-bar segments at 480px, keeps the loading ring collapsed while compact, and validates saved compact positions against current display work areas; Log Viewer plus latency-probe refresh/stop races are guarded with run-id and selected-host checks; long-running async commands reject repeat activation while running, diagnostic bundle export moves log enumeration/zip work off the UI thread, and inactive WebView2 profile deletion runs on a background thread.
- The local C# regression harness is intentionally absent in this checkpoint; active local verification is restore/build/format, repository guardrails, bridge script checks, whitespace checks, and VS2026 manual debug.
- Detailed implementation history is kept in [changelog.md](changelog.md) and [docs/superpowers/plans/2026-05-23-deep-refactor-hardening.md](docs/superpowers/plans/2026-05-23-deep-refactor-hardening.md).

### This project is

- A WinUI 3 + WebView2 remote management shell
- A Windows-native entry point for hosted OpenClaw Control UI sessions
- A thin client that enhances the existing web UI with native UX

### This project is not

- A local Gateway or node host
- A full native rewrite of the OpenClaw frontend
- An offline-capable standalone application

---

## Features

| Feature | Description |
|---|---|
| WebView2 Shell | Hosts the remote OpenClaw UI inside a native window |
| Environment Switching | Manage multiple hosted Control UI endpoints |
| Connection Status | Status bar, error InfoBar, retry support |
| Auto-Reconnect | Retries failed navigation automatically |
| Heartbeat | Periodic Control UI and transport probe with configurable reconnect thresholds |
| System Tray | Configurable minimize/close-to-tray behavior with Open OpenClaw, Reload, View Logs, Compact Mode, Settings, and Exit actions |
| Global Hotkey | Configurable system-wide hotkey (default Ctrl+Alt+Space) to show/hide the window, with Settings UI validation and reset |
| Instance Control | Optional multiple-instance mode; off by default so app relaunches restore the existing tray-hidden window |
| Session Isolation | Separate WebView2 profile data per configured environment |
| Latency Tooltip | Hover the latency badge for recent min/avg/p95/max round-trip stats and Cloudflare PoP |
| Always on Top | Pin button to keep the window above other applications, with native topmost fallback and distinct active/inactive colors |
| Compact Mode | Reduced control/status window for screen-corner placement |
| Diagnostic Export | One-click export of redacted settings, logs, and runtime info as a zip bundle |
| Theme | Top-bar segmented switcher for System, Light, and Dark |
| Language | English, Simplified Chinese, System |
| Diagnostics | Runtime, network, and session checks |
| Log Viewer | View today's log and open the log folder |
| DevTools | Open WebView2 developer tools |

---

## Tech Stack

| Component | Version |
|---|---|
| .NET | 10.0 |
| Windows App SDK | 1.8.x |
| WebView2 | Bundled via WinAppSDK |
| MVVM | CommunityToolkit.Mvvm 8.x |
| UI Language | English + Simplified Chinese |
| Packaging | Unpackaged, self-contained |

---

## Architecture

```text
OpenClaw Manager
|- MainWindow (WinUI shell: XAML, WebView2 control swap, tray/window integration)
|  |- WebViewRecreationService
|  |- LiveShellSettingsApplier
|  |- SettingsDialog / SettingsPersistenceAdapter
|  `- MainViewModel (orchestration and bindable state)
|     |- StatusPresenter
|     |- UiTaskDispatcher
|     |- WebViewMessageOwnership
|     |- WebViewService
|     |  |- WebViewStatusInspector / WebViewStatusInspectionScripts
|     |  |- HeartbeatRuntime
|     |  |- GatewayHeartbeatTransport / HostedSessionHeartbeatPolicy
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

Design principle: remote-first thin shell. The actual OpenClaw runtime lives on the VPS; this app is a native control surface for the hosted Control UI.

---

## Project Structure

```text
Claw_winui3/
|-- NuGet.config
|-- OpenClaw.sln
|-- DEVELOPMENT_NOTES.md
|-- README.md
|-- readme_zh.md
|-- src/
|   |-- OpenClaw/
|   |   |-- OpenClaw.csproj
|   |   |-- Package.appxmanifest
|   |   |-- app.manifest
|   |   |-- App.xaml
|   |   |-- App.xaml.cs
|   |   |-- MainWindow.xaml
|   |   |-- MainWindow.xaml.cs
|   |   |-- Assets/
|   |   |-- Abstractions/
|   |   |-- Helpers/
|   |   |-- Services/
|   |   |-- Strings/
|   |   |-- Styles/
|   |   |-- ViewModels/
|   |   `-- Views/
|   `-- OpenClaw.Core/
|       |-- OpenClaw.Core.csproj
|       |-- Helpers/
|       |-- Models/
|       `-- Services/
```

### Key folders

- `Services/`: configuration, logging, diagnostics, WebView2 lifecycle, recovery helpers
- `OpenClaw.Core/`: physical source tree for pure .NET shared code used by the WinUI app
- `Styles/`: shared WinUI typography, spacing, and status resources
- `ViewModels/`: shell state, commands, settings editing
- `Views/`: settings, about, and log viewer dialogs
- `Strings/`: localized UI resources

---

## Development Prerequisites

- Windows 10 1809+ or Windows 11
- Visual Studio 2026
- .NET Desktop Development workload
- Windows App SDK C# templates
- .NET 10 SDK

### Dependency Restore Notes

- The solution uses SDK-style projects with `PackageReference`.
- Repository-local `packages/` folders are not required and can be deleted safely.
- A solution-level [Directory.Build.props](Directory.Build.props) enables NuGet lock files and static graph restore for repeatable SDK-style restores.
- The expected workflow after clearing local caches is:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
```

### Code Style

- `.editorconfig` is the root style contract: LF endings, final newline, trimmed trailing whitespace, four-space C#/XAML indentation, required braces, and SDK-enforced formatting diagnostics.
- See [docs/code-style.md](docs/code-style.md) for project-specific architecture boundaries, partial-class ownership, XAML resource rules, Core physical-source rules, and verification commands.
- Run `$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore` before committing style-sensitive changes.

### Active Verification

The local `tests/` harness is intentionally absent at this checkpoint. Current automated verification is restore, x64 build, format, repository guardrails, bridge script checks, and whitespace checks:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
$env:OPENCLAW_NODE='C:\Users\Zen\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
powershell -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
git diff --check
```

`tools\verify-bridge-scripts.ps1` requires Node.js by default because it is the active behavior check for embedded bridge assets. Set `OPENCLAW_NODE` to a specific Node executable when the default `node` on `PATH` is blocked or unavailable; set `OPENCLAW_ALLOW_NODE_SKIP=1` only for an explicit local skip.

Bridge script verification is the active behavior check for the split hosted bridge JS assets. The C# regression-test harness is intentionally not present in this checkpoint.

VS2026 manual debug remains required for real WebView2, Gateway, Cloudflare Tunnel, tray, hotkey, and compact-mode behavior.

Manual debug should explicitly cover:

- real hosted Gateway load, task submission, streaming output, and completion without manual reload
- MODEL display after startup, session switch, page reload, and native-triggered `session-ready` replay
- Cloudflare Tunnel or reverse-proxy 5xx pages, unexpected 4xx pages, auth/approval pages, origin rejection, and recovery after the upstream becomes healthy again
- latency tooltip `cf-ray` / Cloudflare PoP parsing on a real tunnel response
- tray show/hide, close-to-tray, reload, compact-mode menu entry, and single-instance relaunch handoff
- global hotkey and always-on-top changes saved from Settings without restarting
- compact-mode entry/exit at 480px and full-mode window-bounds restore after relaunch
- title-bar/DWM border color in light/dark/theme-switch paths, including the top 1px edge
- Log Viewer repeated refresh and close-while-loading behavior

### Current Limitations

- There is no active in-repo C# test harness; verification depends on restore/build/format, guardrail scripts, bridge script checks, and VS2026 manual debug.
- Bridge script behavior is covered by `tools\verify-bridge-scripts.ps1`, but browser-runtime behavior still needs WebView2/VS2026 debug because the C# harness is intentionally absent.
- `WebViewService` is split into focused partials; new lifecycle, navigation, inspection, heartbeat, command, and profile/session behavior should stay in the matching partial instead of the root file.
- Real Gateway, Cloudflare Tunnel, reverse-proxy error pages, tray, hotkey, single-instance, DWM title-bar, and compact-mode behavior still need VS2026 manual debug because the local C# harness is intentionally absent.

### Development Notes

See [DEVELOPMENT_NOTES.md](DEVELOPMENT_NOTES.md) for lessons learned from native window chrome, theme synchronization, and other maintenance-sensitive areas.

---

## Getting Started

### Visual Studio

1. Open [OpenClaw.sln](OpenClaw.sln) in Visual Studio 2026.
2. Set solution platform to `x64`.
3. Press `F5` to run.

### CLI

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
```

### First Launch

1. The app starts with a placeholder environment and does not navigate WebView2 until a real Control UI URL is configured.
2. Open Settings from the top bar.
3. Add your public OpenClaw Control UI URL, for example `https://your-gateway.example.com`.
4. Save settings and the embedded WebView2 shell will load the remote UI.

### Cloudflare Tunnel / VPS Notes

If your OpenClaw Gateway runs on a VPS behind Cloudflare Tunnel:

- use the public HTTPS Control UI URL in OpenClaw Manager
- do not use the raw Gateway WebSocket URL
- make sure the same public origin is listed in `gateway.controlUi.allowedOrigins`
- if you use `gateway.auth.mode: "trusted-proxy"`, make sure identity headers are forwarded on both HTTP requests and WebSocket upgrades
- do not mix trusted-proxy mode with shared token auth unless upstream explicitly documents that combination as supported
- avoid same-host loopback reverse proxies for trusted-proxy mode; use token/password auth there instead
- make sure the tunnel or reverse proxy preserves the original host and scheme

If the page loads but OpenClaw reports origin rejection, check the exact public origin string and proxy forwarding rules first.

---

## Settings

The Settings window is organized into five sections:

| Section | Content |
|---|---|
| Language | Display language |
| General | Window, tray, global hotkey, always-on-top, and multiple-instance behavior |
| Environments | Add, edit, remove, and choose default hosted Control UI endpoints |
| Sessions | Clear WebView2 session data for a specific environment |
| Developer Tools | Diagnostics, logs, DevTools |

### Environment URL Rules

- Use the hosted Control UI page URL with `http://` or `https://`
- Do not use the raw Gateway WebSocket URL with `ws://` or `wss://`
- For Cloudflare Tunnel or reverse-proxy deployments, always use the exact public browser-facing origin

---

## Data Storage

All local data is stored under `%LOCALAPPDATA%\OpenClaw\`.

| Path | Content |
|---|---|
| `settings.json` | Environment configs, theme, language, tray and instance behavior, heartbeat settings, window state |
| `logs/` | Daily log files |
| `WebView2Data/` | WebView2 profile data, cookies, cache |

---

## Runtime Requirements

The compiled application requires:

| Dependency | Download |
|---|---|
| .NET 10 Desktop Runtime | [Download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| WebView2 Runtime | [Download](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) |

Windows 11 usually already includes WebView2 Runtime. Windows 10 users may need to install it manually.

---

## Changelog

See [changelog.md](changelog.md) for the full release history.

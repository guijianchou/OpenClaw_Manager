# OpenClaw Manager

**Language:** English | [Simplified Chinese](readme_zh.md)

**Current version:** 5.1.1

OpenClaw Manager is a lightweight Windows-native shell for the hosted OpenClaw Control UI, built with WinUI 3 and WebView2.

It is intended for remote Gateway deployments running on a VPS and exposed through Cloudflare Tunnel, a reverse proxy, or another public HTTPS endpoint. The desktop app does not host the Gateway itself; it provides a native Windows control surface around the existing web UI.

---

## Current 5.1.1 Notes

- `5.1.1` is a stability, maintainability, and production-adaptation release for the current x64-only WinUI app.
- Hosted `session-ready` recovery state is scoped to the active environment probe key, so stale events from a previous endpoint cannot clear the current environment's degraded state.
- Hard-refresh cooldown starts only after WebView2 accepts a reload, and latency success is published only for classified reachable Gateway responses, not auth, pairing, rate-limit, redirect, or proxy/error states.
- Settings normalization trims environment names and URLs, removes blank entries, de-duplicates names, enforces one default environment, and repairs invalid selected-environment values.
- WebView2 session identity uses both environment name and Gateway URL. Inactive session clearing targets the exact environment object, and legacy profile migration runs only when the stored URL identity marker matches the current endpoint.
- Diagnostic bundles now cap total copied log payload, cap individual diagnostic text entries, redact auth/cookie/API-key headers, and record skipped or truncated content in bundle notes.
- Settings diagnostics, diagnostic bundle export, and DevTools actions keep the Settings window open and report progress or failures inline. Release DevTools enablement is injected from diagnostics settings instead of reading global configuration inside `WebViewService`.
- The Core regression harness and repository guardrails cover the 5.1.1 contracts, while real WebView2, Gateway, tunnel, tray, hotkey, and compact-mode behavior still require manual Windows debug validation.

---

## Scope

### This Project Is

- A WinUI 3 + WebView2 remote management shell.
- A Windows-native entry point for hosted OpenClaw Control UI sessions.
- A thin client that improves the existing web UI with native windowing, tray, hotkey, diagnostics, and session-management behavior.

### This Project Is Not

- A local Gateway, worker, or node host.
- A full native rewrite of the OpenClaw frontend.
- An offline-capable standalone client.
- A multi-architecture Windows build. The current supported app platform is x64 only.

---

## Feature Overview

| Area | Behavior |
|---|---|
| WebView2 shell | Hosts the remote OpenClaw Control UI in a native WinUI window. |
| Environments | Stores multiple hosted Control UI endpoints and switches between them. |
| Session isolation | Uses separate WebView2 profile data per environment name plus Gateway URL. |
| Recovery | Tracks hosted session state, heartbeat failures, navigation stalls, event gaps, and recovery escalation. |
| Diagnostics | Runs runtime, network, session, and WebView checks with localized status labels. |
| Diagnostic bundle | Exports redacted settings, runtime info, and bounded log samples to a zip bundle. |
| Tray integration | Supports open, reload, logs, compact mode, settings, and exit from the tray menu. |
| Global hotkey | Shows or hides the window with a configurable system-wide hotkey. |
| Compact mode | Provides a reduced control/status layout for small screen-corner placement. |
| Theme | Supports System, Light, and Dark modes with native title-bar synchronization. |
| Localization | Supports English, Simplified Chinese, and System language selection. |
| Log viewer | Opens today's log with refresh, horizontal scrolling, and visible folder/open failures. |

---

## Architecture

```text
OpenClaw Manager
|- MainWindow
|  |- WinUI shell, title bar, tray, hotkey, compact mode, and WebView host swap
|  |- SettingsDialog, LogViewerDialog, AboutDialog
|  `- WebViewRecreationService
|- MainViewModel
|  |- bindable shell state, commands, selected environment, and diagnostics orchestration
|  |- StatusPresenter
|  |- LiveShellSettingsApplier
|  `- ShellSessionCoordinator adapters
|- WebViewService
|  |- Lifecycle: initialize/detach/dispose WebView2 and explicit profile environment
|  |- Profile: environment+URL user-data folders and legacy profile migration
|  |- Session: browsing-data clear, environment session clear, DevTools, current URL
|  |- Navigation: navigate/reload/retry, watchdogs, page-token ownership, process failure
|  |- Heartbeat: Gateway transport and hosted Control UI session observation
|  `- Status inspection: bounded scripts, coalescing, parsing, and snapshot ownership
|- HostedUiBridge
|  `- embedded browser-side JS assets for host messaging, status, MODEL, activity, and commands
`- OpenClaw.Core
   |- pure settings and configuration normalization
   |- recovery models and ShellSessionCoordinator state machine
   |- Gateway/Control UI probe classification and mapping
   |- diagnostic bundle, logging, window-bounds, latency, and tray string helpers
   `- executable Core regression harness under tests/OpenClaw.Core.Tests
```

Key ownership rules:

- `src/OpenClaw.Core` must stay WinUI-free and WebView2-free.
- WinUI files own platform integration, dialogs, controls, WebView2 objects, tray, hotkey, and app-edge adapters.
- `WebViewService.cs` remains the root partial for shared state and public navigation commands; lifecycle, session/profile, heartbeat, navigation, and inspection behavior stay in focused partials.
- User-visible strings belong in typed `StringResources` properties and matching `.resw` entries for both locales.
- Runtime/UI paths must not add new synchronous waits. Large filesystem work triggered from UI commands must run through asynchronous or background paths.

---

## Tech Stack

| Component | Current |
|---|---|
| .NET | 10.0 |
| Windows target SDK | 10.0.26100.0 |
| Minimum Windows version | Windows 10 1809 |
| Supported app platform | x64 / win-x64 |
| UI framework | WinUI 3, Windows App SDK 1.8.x |
| Web runtime | WebView2 |
| MVVM | CommunityToolkit.Mvvm 8.x |
| Packaging | Unpackaged, Windows App SDK self-contained, .NET runtime-dependent |

Runtime installs still need the .NET 10 Desktop Runtime and WebView2 Runtime on the target machine.

---

## Repository Structure

```text
Claw_winui3/
|-- .github/
|   `-- workflows/
|       `-- ci.yml
|-- OpenClaw.sln
|-- Directory.Build.props
|-- NuGet.config
|-- README.md
|-- readme_zh.md
|-- changelog.md
|-- docs/
|   `-- code-style.md
|-- src/
|   |-- OpenClaw/
|   |   |-- OpenClaw.csproj
|   |   |-- Package.appxmanifest
|   |   |-- app.manifest
|   |   |-- App.xaml / App.xaml.cs
|   |   |-- MainWindow.xaml / MainWindow*.cs
|   |   |-- Helpers/
|   |   |-- Services/
|   |   |-- Strings/
|   |   |-- Styles/
|   |   |-- ViewModels/
|   |   `-- Views/
|   `-- OpenClaw.Core/
|       |-- Helpers/
|       |-- Models/
|       `-- Services/
|-- tests/
|   `-- OpenClaw.Core.Tests/
`-- tools/
    |-- verify-repo-structure.ps1
    `-- verify-bridge-scripts.ps1
```

---

## Development Prerequisites

- Windows 10 1809+ or Windows 11, x64 only
- Visual Studio 2026 with .NET Desktop Development workload
- Windows App SDK C# templates
- .NET 10 SDK
- Node.js for `tools\verify-bridge-scripts.ps1`, unless `OPENCLAW_ALLOW_NODE_SKIP=1` is explicitly set for a local skip

The solution uses SDK-style projects, `PackageReference`, package lock files, and static graph restore.

---

## Build And Verification

Use the x64 solution platform for local builds:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
```

Run the full local verification set before handoff:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet run --no-restore --project tests\OpenClaw.Core.Tests\OpenClaw.Core.Tests.csproj
dotnet test OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
git diff --check
```

`tests\OpenClaw.Core.Tests` remains an executable Core harness, so `dotnet run` is the fastest targeted signal for pure Core behavior. The project is also discoverable by `dotnet test`, and both commands are part of the supported verification workflow.

## Continuous Integration

GitHub Actions runs the supported non-interactive verification set on Windows:

- locked NuGet restore
- Core executable harness
- VSTest solution workflow
- Debug x64 WinUI build
- formatting verification
- repository guardrails
- embedded bridge script checks with Node.js
- whitespace checks

The CI workflow is intentionally x64-only and does not claim real app launch coverage. WinUI startup, WebView2 runtime behavior, Gateway access, tray, hotkey, and display/windowing behavior still require local Windows debug validation.

Manual VS2026 debug remains required for:

- real hosted Gateway load, task submission, streaming output, and completion
- WebView2 profile switching, session clearing, DevTools, and profile migration
- Cloudflare Tunnel or reverse-proxy 4xx/5xx/origin-rejection behavior
- tray show/hide, reload, compact mode, close-to-tray, and single-instance relaunch
- global hotkey registration and show/hide behavior
- title-bar, theme, compact-mode, and window-bounds behavior across real displays

---

## Getting Started

### Visual Studio

1. Open [OpenClaw.sln](OpenClaw.sln) in Visual Studio 2026.
2. Set the solution platform to `x64`.
3. Press `F5`.

### First Launch

1. The app starts with a placeholder environment and does not navigate WebView2 until a real Control UI URL is configured.
2. Open Settings from the top bar.
3. Add the public hosted OpenClaw Control UI URL, for example `https://your-gateway.example.com`.
4. Save settings. The embedded WebView2 shell will initialize with that environment's profile and load the remote UI.

---

## Gateway And Proxy Notes

For VPS, Cloudflare Tunnel, or reverse-proxy deployments:

- Use the public browser-facing `http://` or `https://` hosted Control UI page URL.
- Do not use raw `ws://` or `wss://` Gateway WebSocket URLs as environment URLs.
- Include the configured `gateway.controlUi.basePath` when the hosted Control UI is not served from `/`.
- Keep `gateway.controlUi.allowedOrigins` to origin only: scheme, host, and optional port.
- Route both the hosted Control UI path and `<basePath>/__openclaw__/a2ui/` to the Gateway service.
- Preserve the original host and scheme through the tunnel or proxy.
- Forward identity headers on both HTTP requests and WebSocket upgrades when using trusted-proxy auth.
- Avoid mixing trusted-proxy mode and shared token auth unless the upstream Gateway explicitly supports that combination.

If the hosted page loads but the app reports origin rejection, verify the exact public origin and proxy forwarding rules before debugging the desktop shell.

---

## Settings

| Section | Content |
|---|---|
| Language | Display language. |
| General | Window, tray, hotkey, always-on-top, and multiple-instance behavior. |
| Environments | Add, edit, remove, and choose default hosted Control UI endpoints. |
| Sessions | Clear WebView2 session data for a configured environment. |
| Developer Tools | Diagnostics, diagnostic bundle export, logs, and DevTools. |

---

## Local Data

All local data is stored under `%LOCALAPPDATA%\OpenClaw\`.

| Path | Content |
|---|---|
| `settings.json` | Environment configs, theme, language, tray, hotkey, heartbeat, recovery, and window state. |
| `logs/` | Daily application logs. |
| `WebView2Data/` | WebView2 profile data, cookies, cache, and local storage. |

---

## Further Documentation

- [docs/code-style.md](docs/code-style.md): architecture boundaries, style rules, and verification contracts.
- [DEVELOPMENT_NOTES.md](DEVELOPMENT_NOTES.md): historical debugging notes and maintenance lessons.
- [changelog.md](changelog.md): release history.

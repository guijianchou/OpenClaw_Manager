# OpenClaw Manager

**Language:** English | [简体中文](readme_zh.md)

**Current version:** 3.3.6

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

## Current 3.3.6 Notes

- Added [docs/code-style.md](docs/code-style.md) as the canonical project code-style and architecture guide.
- Centralized top status and status-bar typography, spacing, and layout constants into focused WinUI resource dictionaries under `src/OpenClaw/Styles`.
- Retained the Core/app architecture cleanup while removing the local regression harness from the active solution.
- Split `WebViewService` command-injection, heartbeat, Control UI inspection, and profile-folder helpers into focused partial files.
- Moved all Core-compatible source files, including window-bounds policy, into the physical `src/OpenClaw.Core` tree.
- Moved the main hosted bridge browser script into embedded JS assets, with C# limited to resource loading and localized string/model resolver injection.
- Kept hosted MODEL app-state resolution in an embedded JS asset for defaults, null overrides, Map overrides, and object-shaped payloads.
- Kept bridge hardening for session-ready metadata, command-dispatch return values, mutation filtering, safe host messaging, and generation-scoped WebView inspection cache reuse.
- Synced release metadata to `3.3.6` after VS2026 debug validation of the architecture cleanup branch.

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
| System Tray | Configurable minimize/close-to-tray behavior with Open OpenClaw, Reload, View Logs, Settings, and Exit actions |
| Global Hotkey | Configurable system-wide hotkey (default Ctrl+Alt+Space) to show/hide the window, with Settings UI validation and reset |
| Instance Control | Optional multiple-instance mode; off by default so app relaunches restore the existing tray-hidden window |
| Session Isolation | Separate WebView2 profile data per configured environment |
| Latency Tooltip | Hover the latency badge for recent min/avg/p95/max round-trip stats and Cloudflare PoP |
| Always on Top | Pin button to keep the window above other applications, with native topmost fallback and distinct active/inactive colors |
| Compact Mode | Reduced window showing only status bar for screen-corner placement |
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
MainWindow
|- MainViewModel
|  |- ConfigurationService
|  `- LoggingService
`- WebViewService
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
|   |   |-- Models/
|   |   |-- Services/
|   |   |-- Strings/
|   |   |-- ViewModels/
|   |   `-- Views/
|   `-- OpenClaw.Core/
|       `-- OpenClaw.Core.csproj
```

### Key folders

- `Services/`: configuration, logging, diagnostics, WebView2 lifecycle, recovery helpers
- `OpenClaw.Core/`: physical source tree for pure .NET shared code used by the WinUI app
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
dotnet restore src\OpenClaw\OpenClaw.csproj
dotnet build src\OpenClaw\OpenClaw.csproj -r win-x64
```

### First Launch

1. The app starts with a placeholder environment.
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

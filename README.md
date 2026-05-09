# OpenClaw Manager

**Language:** [English](#openclaw-manager) | [简体中文](#openclaw-manager-简体中文)

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

### This project is

- A WinUI 3 + WebView2 remote management shell
- A Windows-native entry point for hosted OpenClaw Control UI sessions
- A thin client that enhances the existing web UI with native UX

### This project is not

- A local Gateway or node host
- A full native rewrite of the OpenClaw frontend
- An offline-capable standalone application

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

## Project Structure

```text
Claw_winui3/
|-- NuGet.config
|-- OpenClaw.sln
|-- DEVELOPMENT_NOTES.md
|-- README.md
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
`-- tests/OpenClaw.Tests/
    |-- OpenClaw.Tests.csproj
    `-- Program.cs
```

### Key folders

- `Services/`: configuration, logging, diagnostics, WebView2 lifecycle, recovery helpers
- `OpenClaw.Core/`: pure .NET shared code linked from the WinUI app for regression coverage
- `tests/OpenClaw.Tests/`: lightweight regression harness for recovery, settings, tray, metadata, and persistence behavior
- `ViewModels/`: shell state, commands, settings editing
- `Views/`: settings, about, and log viewer dialogs
- `Strings/`: localized UI resources

---

## Runtime Requirements

The compiled application requires:

| Dependency | Download |
|---|---|
| .NET 10 Desktop Runtime | [Download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| WebView2 Runtime | [Download](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) |

Windows 11 usually already includes WebView2 Runtime. Windows 10 users may need to install it manually.

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
- A solution-level [Directory.Build.props](Directory.Build.props) enables `RestorePackagesConfig=true` so full Visual Studio / MSBuild builds still auto-restore if a future `packages.config` project is added.
- The expected workflow after clearing local caches is simply:

```powershell
dotnet restore OpenClaw.sln
dotnet build OpenClaw.sln
```

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

## Features

| Feature | Description |
|---|---|
| WebView2 Shell | Hosts the remote OpenClaw UI inside a native window |
| Environment Switching | Manage multiple hosted Control UI endpoints |
| Connection Status | Status bar, error InfoBar, retry support |
| Auto-Reconnect | Retries failed navigation automatically |
| Heartbeat | Periodic Control UI and transport probe with configurable reconnect thresholds |
| System Tray | Configurable minimize/close-to-tray behavior with Open OpenClaw, Settings, and Exit actions |
| Instance Control | Optional multiple-instance mode; off by default so app relaunches restore the existing tray-hidden window |
| Session Isolation | Separate WebView2 profile data per configured environment |
| Latency Tooltip | Hover the latency badge for recent min/avg/p95/max round-trip stats |
| Theme | Top-bar segmented switcher for System, Light, and Dark |
| Language | English, Simplified Chinese, System |
| Diagnostics | Runtime, network, and session checks |
| Log Viewer | View today's log and open the log folder |
| DevTools | Open WebView2 developer tools |

---

## Settings

The Settings window is organized into five sections:

| Section | Content |
|---|---|
| Language | Display language |
| Environments | Add, edit, remove, and choose default hosted Control UI endpoints |
| Sessions | Clear WebView2 session data for a specific environment |
| Developer Tools | Diagnostics, logs, DevTools |
| Advanced | Minimize-to-tray, close-to-tray, and multiple-instance behavior |

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

## Recent Changes

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

# OpenClaw Manager 简体中文

**语言：** [English](#openclaw-manager) | [简体中文](#openclaw-manager-简体中文)

OpenClaw Manager 是一个轻量的 Windows 原生 OpenClaw 远程管理外壳，基于 WinUI 3 和 WebView2 构建。

OpenClaw Manager 是托管版 OpenClaw Control UI 的薄桌面外壳。它面向运行在 VPS 上、并通过 Cloudflare Tunnel、反向代理或其他公共 HTTPS 源暴露的远程 Gateway 部署。

---

## 概览

本项目保留现有 OpenClaw Web 体验，同时把它包进一个原生 WinUI 3 窗口，并提供：

- 环境切换
- 每个环境独立的 WebView2 会话隔离
- 连接恢复和心跳监控
- 诊断和结构化日志
- 原生主题和窗口集成

它适合以下用户：

- 在远程机器上运行 OpenClaw Gateway
- 通过 Cloudflare Tunnel 或反向代理访问它
- 想用轻量 Windows 原生客户端，而不是一直开着浏览器标签页

### 本项目是

- WinUI 3 + WebView2 远程管理外壳
- 托管 OpenClaw Control UI 会话的 Windows 原生入口
- 在现有 Web UI 之上增强原生 UX 的薄客户端

### 本项目不是

- 本地 Gateway 或节点宿主
- OpenClaw 前端的完整原生重写
- 可离线使用的独立应用

---

## 技术栈

| 组件 | 版本 |
|---|---|
| .NET | 10.0 |
| Windows App SDK | 1.8.x |
| WebView2 | 通过 WinAppSDK 捆绑 |
| MVVM | CommunityToolkit.Mvvm 8.x |
| UI 语言 | 英文 + 简体中文 |
| 打包方式 | Unpackaged, self-contained |

---

## 项目结构

```text
Claw_winui3/
|-- NuGet.config
|-- OpenClaw.sln
|-- DEVELOPMENT_NOTES.md
|-- README.md
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
`-- tests/OpenClaw.Tests/
    |-- OpenClaw.Tests.csproj
    `-- Program.cs
```

### 关键目录

- `Services/`：配置、日志、诊断、WebView2 生命周期和恢复辅助逻辑
- `OpenClaw.Core/`：可被测试项目复用的纯 .NET 共享代码
- `tests/OpenClaw.Tests/`：覆盖恢复、设置、托盘、版本元数据和持久化行为的轻量回归测试
- `ViewModels/`：外壳状态、命令和设置编辑
- `Views/`：设置、关于和日志查看对话框
- `Strings/`：本地化 UI 资源

---

## 运行时要求

编译后的应用需要：

| 依赖 | 下载 |
|---|---|
| .NET 10 Desktop Runtime | [Download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| WebView2 Runtime | [Download](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) |

Windows 11 通常已经内置 WebView2 Runtime。Windows 10 用户可能需要手动安装。

---

## 开发前置条件

- Windows 10 1809+ 或 Windows 11
- Visual Studio 2026
- .NET Desktop Development workload
- Windows App SDK C# templates
- .NET 10 SDK

### 依赖恢复说明

- 解决方案使用 SDK-style 项目和 `PackageReference`。
- 仓库本地的 `packages/` 文件夹不是必需的，可以安全删除。
- 解决方案级 [Directory.Build.props](Directory.Build.props) 启用 `RestorePackagesConfig=true`，因此如果未来加入 `packages.config` 项目，完整 Visual Studio / MSBuild 构建仍会自动恢复依赖。
- 清理本地缓存后的预期流程是：

```powershell
dotnet restore OpenClaw.sln
dotnet build OpenClaw.sln
```

### 开发日志

参见 [DEVELOPMENT_NOTES.md](DEVELOPMENT_NOTES.md)，了解原生窗口 chrome、主题同步和其他维护敏感区域的经验记录。

---

## 快速开始

### Visual Studio

1. 用 Visual Studio 2026 打开 [OpenClaw.sln](OpenClaw.sln)。
2. 将解决方案平台设为 `x64`。
3. 按 `F5` 运行。

### CLI

```powershell
dotnet restore src\OpenClaw\OpenClaw.csproj
dotnet build src\OpenClaw\OpenClaw.csproj -r win-x64
```

### 首次启动

1. 应用会使用一个占位环境启动。
2. 从顶部栏打开 Settings。
3. 添加你的公共 OpenClaw Control UI URL，例如 `https://your-gateway.example.com`。
4. 保存设置后，内嵌 WebView2 外壳会加载远程 UI。

### Cloudflare Tunnel / VPS 说明

如果你的 OpenClaw Gateway 运行在 VPS 上并位于 Cloudflare Tunnel 后面：

- 在 OpenClaw Manager 中使用公共 HTTPS Control UI URL
- 不要使用原始 Gateway WebSocket URL
- 确保同一个公共 origin 已列入 `gateway.controlUi.allowedOrigins`
- 如果使用 `gateway.auth.mode: "trusted-proxy"`，确保 HTTP 请求和 WebSocket upgrade 都转发身份头
- 除非上游明确说明支持，不要混用 trusted-proxy 模式和 shared token auth
- trusted-proxy 模式下避免同主机 loopback 反向代理；这种场景请改用 token/password auth
- 确保 tunnel 或反向代理保留原始 host 和 scheme

如果页面可以加载但 OpenClaw 报 origin rejection，先检查精确的公共 origin 字符串和代理转发规则。

---

## 功能

| 功能 | 说明 |
|---|---|
| WebView2 Shell | 在原生窗口内托管远程 OpenClaw UI |
| Environment Switching | 管理多个托管 Control UI 端点 |
| Connection Status | 状态栏、错误 InfoBar 和重试支持 |
| Auto-Reconnect | 导航失败后自动重试 |
| Heartbeat | 周期性 Control UI 和 transport 探测，支持可配置重连阈值 |
| System Tray | 可配置的最小化/关闭到托盘行为，提供 Open OpenClaw、Settings 和 Exit 操作 |
| Instance Control | 可选多实例模式；默认关闭，重新启动会恢复已有托盘隐藏窗口 |
| Session Isolation | 每个配置环境使用独立 WebView2 profile 数据 |
| Latency Tooltip | 悬停延迟徽标查看最新、最小、平均、p95 和最大往返时间 |
| Theme | 顶部栏 System、Light、Dark 分段切换 |
| Language | English、Simplified Chinese、System |
| Diagnostics | 运行时、网络和会话检查 |
| Log Viewer | 查看当天日志并打开日志目录 |
| DevTools | 打开 WebView2 developer tools |

---

## 设置

Settings 窗口包含五个部分：

| 部分 | 内容 |
|---|---|
| Language | 显示语言 |
| Environments | 添加、编辑、删除和选择默认托管 Control UI 端点 |
| Sessions | 清理指定环境的 WebView2 会话数据 |
| Developer Tools | 诊断、日志、DevTools |
| Advanced | 最小化到托盘、关闭到托盘和多实例行为 |

### 环境 URL 规则

- 使用带 `http://` 或 `https://` 的托管 Control UI 页面 URL
- 不要使用 `ws://` 或 `wss://` 的原始 Gateway WebSocket URL
- 对 Cloudflare Tunnel 或反向代理部署，始终使用浏览器可访问的精确公共 origin

---

## 数据存储

所有本地数据都存储在 `%LOCALAPPDATA%\OpenClaw\` 下。

| 路径 | 内容 |
|---|---|
| `settings.json` | 环境配置、主题、语言、托盘和实例行为、心跳设置、窗口状态 |
| `logs/` | 每日日志文件 |
| `WebView2Data/` | WebView2 profile 数据、cookies、缓存 |

---

## 架构

```text
MainWindow
|- MainViewModel
|  |- ConfigurationService
|  `- LoggingService
`- WebViewService
```

设计原则：remote-first thin shell。真正的 OpenClaw runtime 位于 VPS 上；本应用是托管 Control UI 的原生控制面。

---

## 最近更新

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

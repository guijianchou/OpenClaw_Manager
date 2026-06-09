# OpenClaw Manager 简体中文

**语言：** [English](README.md) | 简体中文

**当前版本：** 5.1.1

OpenClaw Manager 是一个轻量的 Windows 原生外壳，用 WinUI 3 和 WebView2 承载托管版 OpenClaw Control UI。

它面向运行在 VPS 上，并通过 Cloudflare Tunnel、反向代理或其他公共 HTTPS 入口暴露的远程 Gateway 部署。本桌面应用不会托管 Gateway 本身，而是在现有 Web UI 外提供原生 Windows 控制面。

---

## 当前 5.1.1 注意事项

- `5.1.1` 是当前 x64-only WinUI 应用的稳定性、可维护性和生产环境适配版本。
- 托管端 `session-ready` 的 recovery state 现在绑定到当前环境 probe key，旧端点的滞后事件不会再清理当前环境的 degraded state。
- hard refresh cooldown 只会在 WebView2 接受 reload 后开始计时；latency success 只对分类为 reachable 的 Gateway 响应发布，不再把 auth、pairing、rate-limit、redirect 或 proxy/error 状态当作成功。
- Settings normalization 会裁剪环境名称和 URL、移除空条目、去重同名环境、保证只有一个默认环境，并修复无效的 selected environment。
- WebView2 session 身份同时使用环境名称和 Gateway URL。非活动 session 清理会针对精确环境对象，legacy profile migration 仅在保存的 URL identity marker 与当前端点一致时执行。
- Diagnostic bundle 现在限制总日志 payload、限制单个诊断文本条目、脱敏 auth/cookie/API-key headers，并在 bundle notes 中记录被跳过或截断的内容。
- Settings 中的 diagnostics、diagnostic bundle export 和 DevTools 操作会保持窗口打开并在页面内反馈结果。Release DevTools enablement 由 diagnostics settings 注入，不再由 `WebViewService` 直接读取全局配置。
- Core regression harness 和 repository guardrails 已覆盖 5.1.1 契约；真实 WebView2、Gateway、tunnel、tray、hotkey 和 compact mode 行为仍需要 Windows 手工调试验证。

---

## 项目边界

### 本项目是

- WinUI 3 + WebView2 远程管理外壳。
- 托管 OpenClaw Control UI session 的 Windows 原生入口。
- 在现有 Web UI 之上补充原生窗口、托盘、热键、诊断和 session 管理体验的 thin client。

### 本项目不是

- 本地 Gateway、worker 或 node host。
- OpenClaw 前端的完整原生重写。
- 可离线使用的独立客户端。
- 多架构 Windows build。当前 app 只支持 x64。

---

## 功能概览

| 范围 | 行为 |
|---|---|
| WebView2 shell | 在 WinUI 原生窗口中承载远程 OpenClaw Control UI。 |
| Environments | 保存多个托管 Control UI endpoint，并在它们之间切换。 |
| Session isolation | 按环境名称和 Gateway URL 分离 WebView2 profile 数据。 |
| Recovery | 跟踪托管 session 状态、heartbeat failure、navigation stall、event gap 和 recovery escalation。 |
| Diagnostics | 执行 runtime、network、session 和 WebView 检查，并使用本地化状态标签。 |
| Diagnostic bundle | 导出脱敏 settings、runtime info 和有界日志样本到 zip bundle。 |
| Tray integration | 支持从托盘菜单 open、reload、logs、compact mode、settings 和 exit。 |
| Global hotkey | 使用可配置的系统级热键显示或隐藏窗口。 |
| Compact mode | 为屏幕角落放置提供缩减后的控制和状态布局。 |
| Theme | 支持 System、Light 和 Dark，并同步原生 title bar。 |
| Localization | 支持 English、Simplified Chinese 和 System 语言选择。 |
| Log viewer | 查看当天日志，支持刷新、横向滚动，并显示目录缺失或打开失败。 |

---

## 架构

```text
OpenClaw Manager
|- MainWindow
|  |- WinUI shell、title bar、tray、hotkey、compact mode、WebView host swap
|  |- SettingsDialog、LogViewerDialog、AboutDialog
|  `- WebViewRecreationService
|- MainViewModel
|  |- 可绑定 shell 状态、commands、selected environment、diagnostics orchestration
|  |- StatusPresenter
|  |- LiveShellSettingsApplier
|  `- ShellSessionCoordinator adapters
|- WebViewService
|  |- Lifecycle: 初始化、detach、dispose WebView2 和显式 profile environment
|  |- Profile: environment+URL user-data folders 和 legacy profile migration
|  |- Session: browsing-data clear、environment session clear、DevTools、current URL
|  |- Navigation: navigate/reload/retry、watchdogs、page-token ownership、process failure
|  |- Heartbeat: Gateway transport 和 hosted Control UI session observation
|  `- Status inspection: bounded scripts、coalescing、parsing、snapshot ownership
|- HostedUiBridge
|  `- browser-side embedded JS assets，处理 host messaging、status、MODEL、activity 和 commands
`- OpenClaw.Core
   |- 纯 settings 和 configuration normalization
   |- recovery models 和 ShellSessionCoordinator state machine
   |- Gateway/Control UI probe classification 和 mapping
   |- diagnostic bundle、logging、window-bounds、latency、tray string helpers
   `- tests/OpenClaw.Core.Tests 下的 executable Core regression harness
```

关键职责规则：

- `src/OpenClaw.Core` 必须保持 WinUI-free 和 WebView2-free。
- WinUI 层负责平台集成、dialogs、controls、WebView2 objects、tray、hotkey 和 app-edge adapters。
- `WebViewService.cs` 只保留 shared state 和 public navigation commands；lifecycle、session/profile、heartbeat、navigation 和 inspection 行为放在对应 focused partial 中。
- 用户可见字符串必须通过 typed `StringResources` properties 和两个 locale 的 `.resw` 条目维护。
- Runtime/UI 路径不能新增同步等待。由 UI command 触发的大文件系统工作必须走异步或后台路径。

---

## 技术栈

| 组件 | 当前 |
|---|---|
| .NET | 10.0 |
| Windows target SDK | 10.0.26100.0 |
| Minimum Windows version | Windows 10 1809 |
| Supported app platform | x64 / win-x64 |
| UI framework | WinUI 3, Windows App SDK 1.8.x |
| Web runtime | WebView2 |
| MVVM | CommunityToolkit.Mvvm 8.x |
| Packaging | Unpackaged, Windows App SDK self-contained, .NET runtime-dependent |

目标机器仍需要安装 .NET 10 Desktop Runtime 和 WebView2 Runtime。

---

## 仓库结构

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

## 开发前置条件

- Windows 10 1809+ 或 Windows 11，仅支持 x64
- Visual Studio 2026，并安装 .NET Desktop Development workload
- Windows App SDK C# templates
- .NET 10 SDK
- Node.js，用于 `tools\verify-bridge-scripts.ps1`；只有明确设置 `OPENCLAW_ALLOW_NODE_SKIP=1` 时才允许本地跳过

解决方案使用 SDK-style projects、`PackageReference`、package lock files 和 static graph restore。

---

## 构建与验证

本地 build 使用 x64 solution platform：

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
```

交付前运行完整本地验证：

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

`tests\OpenClaw.Core.Tests` 仍是 executable Core harness，因此 `dotnet run` 是纯 Core 行为最快的定向信号。该项目也支持 `dotnet test` discoverability，两条命令都是当前支持的验证流程。

## Continuous Integration

GitHub Actions 会在 Windows 上运行当前支持的非交互式验证集合：

- locked NuGet restore
- Core executable harness
- VSTest solution workflow
- Debug x64 WinUI build
- formatting verification
- repository guardrails
- embedded bridge script checks with Node.js
- whitespace checks

CI workflow 明确保持 x64-only，并且不声明覆盖真实应用启动。WinUI startup、WebView2 runtime 行为、Gateway access、tray、hotkey 以及 display/windowing 行为仍需要本地 Windows debug 验证。

仍需 VS2026 手工调试覆盖：

- 真实 hosted Gateway 加载、任务提交、流式输出和完成
- WebView2 profile 切换、session 清理、DevTools 和 profile migration
- Cloudflare Tunnel 或反代 4xx、5xx、origin rejection 行为
- tray show/hide、reload、compact mode、close-to-tray 和 single-instance relaunch
- global hotkey 注册和 show/hide 行为
- title bar、theme、compact mode 和 window bounds 在真实显示器上的表现

---

## 快速开始

### Visual Studio

1. 用 Visual Studio 2026 打开 [OpenClaw.sln](OpenClaw.sln)。
2. 将 solution platform 设置为 `x64`。
3. 按 `F5` 运行。

### 首次启动

1. 应用会以 placeholder environment 启动，在配置真实 Control UI URL 前不会导航 WebView2。
2. 从顶部栏打开 Settings。
3. 添加公共托管 OpenClaw Control UI URL，例如 `https://your-gateway.example.com`。
4. 保存设置后，内嵌 WebView2 shell 会使用该环境的 profile 初始化并加载远程 UI。

---

## Gateway 与代理说明

对于 VPS、Cloudflare Tunnel 或反向代理部署：

- 使用浏览器可访问的公共 `http://` 或 `https://` 托管 Control UI 页面 URL。
- 不要把原始 `ws://` 或 `wss://` Gateway WebSocket URL 配成环境 URL。
- 如果托管 Control UI 不是从 `/` 提供，请包含配置的 `gateway.controlUi.basePath`。
- `gateway.controlUi.allowedOrigins` 只保留 origin：scheme、host 和可选 port。
- 将托管 Control UI path 和 `<basePath>/__openclaw__/a2ui/` 都路由到 Gateway service。
- tunnel 或 proxy 必须保留原始 host 和 scheme。
- trusted-proxy auth 模式下，HTTP request 和 WebSocket upgrade 都必须转发身份 headers。
- 除非上游 Gateway 明确支持，不要混用 trusted-proxy mode 和 shared token auth。

如果页面能加载但应用报告 origin rejection，请先核对精确 public origin 和 proxy forwarding rules，再排查桌面 shell。

---

## Settings

| Section | 内容 |
|---|---|
| Language | 显示语言。 |
| General | 窗口、托盘、热键、always-on-top 和多实例行为。 |
| Environments | 添加、编辑、删除和选择默认托管 Control UI endpoint。 |
| Sessions | 清理指定环境的 WebView2 session data。 |
| Developer Tools | Diagnostics、diagnostic bundle export、logs 和 DevTools。 |

---

## 本地数据

所有本地数据都存储在 `%LOCALAPPDATA%\OpenClaw\`。

| 路径 | 内容 |
|---|---|
| `settings.json` | 环境配置、theme、language、tray、hotkey、heartbeat、recovery 和 window state。 |
| `logs/` | 每日应用日志。 |
| `WebView2Data/` | WebView2 profile data、cookies、cache 和 local storage。 |

---

## 更多文档

- [docs/code-style.md](docs/code-style.md)：架构边界、代码规则和验证契约。
- [DEVELOPMENT_NOTES.md](DEVELOPMENT_NOTES.md)：历史调试记录和维护经验。
- [changelog.md](changelog.md)：完整发布历史。

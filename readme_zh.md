# OpenClaw Manager 简体中文

**语言：** [English](README.md) | 简体中文

**当前版本：** 5.0.1

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

## 当前 5.0.1 注意事项

- `5.0.1` 针对 VPS + Cloudflare Tunnel 部署更新 Gateway/Cloudflare 状态模型：heartbeat、diagnostics 和 latency probe 现在共用同一套 HTTP 状态分类，latency probe 改为探测文档中的 `__openclaw__/a2ui/` Control UI 路径，404、405、5xx、Cloudflare Tunnel 1033 等代理或路径故障不会再显示成健康延迟。
- Settings 持久化失败现在会回传到 Settings 对话框，不再只写日志却让对话框像保存成功一样关闭。
- 本轮收尾继续加固 timeout / `Unavailable` 恢复路径：terminal `Unavailable` 会在 `Reconnecting` 时显示可见 InfoBar，`GatewayError` / `Unavailable` 不会让 ShellSessionCoordinator 停留在旧的 Ready/Healthy 投影，completion timeout 后迟到但仍属当前导航的成功 completion 会重新建立 navigation cancellation ownership 并继续正常 page-token/probe 路径。
- 动态 WebView 重建如果抛出异常，会显示本地化的可操作错误和 Retry，而不是在 timeout recovery 先隐藏 InfoBar 后只写日志。
- 迟到但已恢复的导航现在也能取消已经排队、延后或正在执行的 timeout-only WebView 重建，避免重建流程拆掉已经恢复的 WebView。
- WebView2 子控件 layout timeout 会计入 recreation circuit breaker，不再无限重试。
- WebView detach / recreation 和资源 probe stop 会同步清理顶部可见状态，避免旧 `HB OK`、ping、`AUTH OK`、`LIVE` / `IDLE` 或 MODEL 看起来仍属于当前会话。

- `5.0.0` 保留为上一轮重构验证基线。v3.3.6 架构清理仍是本分支 review baseline；这不表示要改写 [changelog.md](changelog.md) 中历史 `v3.0.5` / `v3.0.1` / `v3.0.0` 发布条目。
- [docs/code-style.md](docs/code-style.md) 是本分支统一的代码规范和架构边界入口。
- solution 会把当前 `x64`/`x86`/`ARM64` solution platform 下的 `OpenClaw.Core` 映射到 Core project 自身的平台无关 `AnyCPU` 配置，避免 VS2026 打开 Debug/Release x64 时要求手动修 Configuration Manager。
- 默认的 `https://example.com` 环境会被当作首次运行占位符，而不是真实 Control UI。选中该环境时，MainWindow 会跳过 WebView2 host 创建、停止 heartbeat/latency probe、清理旧 WebView host，并显示本地化的“请在设置中配置 Gateway URL”状态，不会继续导航到 `example.com` 或让 loading ring 保持转圈。
- 已保存的语言偏好现在使用 Windows App SDK 的 `Microsoft.Windows.Globalization.ApplicationLanguages` API，启动时会应用配置的语言，不再记录之前 WinRT API 触发的 `Language override failed` warning。
- 运行时职责拆到聚焦服务：`WebViewStatusInspector`、`HeartbeatRuntime`、`GatewayHeartbeatTransport`、`HostedSessionHeartbeatPolicy`、`WebViewRecreationService`、`SettingsPersistenceAdapter`、`LiveShellSettingsApplier` 和 `StatusPresenter`。
- `WebViewService.cs` 现在只保留共享字段、构造、事件和 public navigation command；WebView2 初始化、detach/dispose 和 current-target 检查放在 `WebViewService.Lifecycle.cs`，profile/session 操作放在 `WebViewService.Session.cs`。
- `WebViewService` navigation 代码继续按职责拆分：event/completion flow 在 `WebViewService.Navigation.cs`，host-message handling 在 `WebViewService.HostMessages.cs`，shared navigation ownership/cancellation helpers 在 `WebViewService.NavigationState.cs`，watchdog ownership 在 `WebViewService.NavigationWatchdogs.cs`，CoreWebView2 command wrapper 在 `WebViewService.NavigationCommands.cs`，page-token/session-ready retry 在 `WebViewService.PageToken.cs`，process-failure/auto-retry recovery 在 `WebViewService.NavigationRecovery.cs`。
- Hosted bridge 逻辑拆成嵌入式 JavaScript assets，分别负责 host messaging、mutation filtering、MODEL resolution、DOM fallback、activity/stale-busy、phase classification、command dispatch 和 status inspection。
- `WebViewStatusInspector` 主 partial 保留共享状态、公共入口和 snapshot publication，direct inspection/coalescing、post-navigation probe、Control UI snapshot parsing 和有界 script execution 已拆到聚焦 partial。
- WebView status probe 现在按 generation 和 accepted page version 归属、带 timeout、触碰 WebView2 前回到 UI dispatcher，并通过 owner/page-token 校验避免旧 document 或已取消 inspection 覆盖当前状态；timeout、脚本失败或导航后 probe 耗尽都会发布归属明确且会终止 probe 的 `Unavailable` 快照、降级旧的 `Connected` shell 状态，并在同一个 accepted page 内让最近一次非空 MODEL 贯穿 loading、unavailable 和 unknown 快照。
- Hosted bridge command 和 WebView stop/abort 脚本都有有界执行 timeout，并在 WebView 目标或已接受 page ownership 切换后拒绝旧结果；由 recovery 拥有的 bridge command 会把当前 recovery cancellation token 串到 UI dispatch 和脚本执行中；CustomEvent command fallback 不再被当成已处理命令，除非 hosted bridge method 真正接受了命令；Stop fallback 会绑定最初的页面目标，避免旧 command 被拒后误停新页面。
- WebView navigation、reload、手动 retry 和 auto-retry 会在调用 CoreWebView2 前失效 page ownership；手动 Retry 在没有可重试 navigation 时会保留本地化可操作错误提示，由 recovery 拥有的 reload 会携带当前 recovery cancellation token，并确认 reload 确实启动后才推进状态；过期 auto-retry continuation 会直接退出，不再发布旧 navigation 状态，page-token retry 和 native-triggered `session-ready` replay 使用 lease-owned navigation cancellation scope，确保 reload、detach 或新 navigation 会取消旧工作，同时不会 dispose 仍被使用的 token；重试耗尽或失败也会进入 Error，而不是让外壳卡在 Reconnecting；page-token 捕获耗尽只会对原 navigation generation 发布 `Unavailable`，Stop fallback 也会取消 navigation watchdog、probe 和 page ownership，避免用户 Stop 后旧启动 timeout 回头触发 recovery。
- WebView 启动恢复现在只允许匹配 pending 目标的 `NavigationCompleted` 在缺失 `NavigationStarting` 回调时接管 navigation，并记录 previous source 用于拒绝 stale completion；start timeout 后会在有界恢复窗口内保留 pending target，迟到的 completion 恢复页面时会取消尚未执行或因 compact/隐藏/最小化而延后的 timeout 触发型 WebView 重建；timeout recovery 请求与其他请求合并时会保留 settings、initial、session 和 topology 等更高优先级的重建原因；completion timeout 需要匹配 active watchdog id 才能生效，并且全窗口 loading ring 只绑定浏览器 navigation 的 `Loading`，不再覆盖较长的 `GatewayConnecting` 状态阶段。
- 动态 WebView 重建现在会等待 window shell、host panel 和新 WebView2 子控件都可见且尺寸非零后才初始化/导航；compact、隐藏到托盘或最小化启动状态会延后重建且不丢失原始的高优先级原因，WebView2 子控件 layout timeout 会通过正常 recreation timer/circuit-breaker 路径重新排队，避免一直等下一个窗口事件；延后重建归属也由 `WebViewRecreationService` 持有，不再对不可呈现的 WebView 发起导航并在 12 秒后触发 start watchdog。
- Hosted bridge 的 session/status 消息使用 host generation 和 owner/page-token 归属校验，不再依赖 CoreWebView2 wrapper reference identity，避免 WebView2 暴露不同 COM wrapper 对象时吞掉有效的 `session-ready` 或 status post。
- WebView process failure 会先退休 navigation retry/replay cancellation，再发布 unavailable 状态；注入式 ViewModel UI 更新会捕获并记录回调异常，避免 dispatcher 回调变成 UI 线程未处理异常。
- Gateway heartbeat、diagnostics 和 latency probe 现在为 Cloudflare Tunnel / 反向代理部署共用 Gateway HTTP 状态分类。Heartbeat 会把代理、路径和服务端故障识别为失败，把设备 approval 或限流响应识别为需要用户动作的 session-blocked 状态，并把 hosted-session inspection `Unavailable` 当作失败而不是回落到健康 HTTP transport；latency probe 使用文档中的 `__openclaw__/a2ui/` Control UI 路径，且不会把 404/405/5xx/1033 响应记录为健康延迟样本。
- ShellSessionCoordinator 的 recovery work 现在覆盖 event gap、stale-busy recovery、heartbeat-triggered recovery、foreground resume、in-page bridge command 和 UI-dispatched reload 的取消链；public recovery request 会先把调用方 cancellation token 链接到当前 operation，再排队 inspection、bridge command 或 reload；attach/detach/reset/dispose 会在替换 WebView 或 bridge 服务前取消 pending work，避免旧 recovery 决策写到新的 hosted session。
- Settings 中的全局热键、Always on Top 和多实例行为保存后会影响当前进程；Settings 保存只合并当前打开对话框中实际编辑过的字段，避免 stale snapshot 覆盖外部 Pin、hotkey 或 environment 变更，并且 two-way binding 初始化时的同值写回不会把字段误标为 dirty；多实例 listener 变更会走被观察的异步路径，Settings 保存不会同步等待 named-pipe shutdown，二次启动的 primary 激活和失败接管等待也走异步路径，并共用一个有界接管 deadline，避免重新启动接力卡住启动线程；跨进程 single-instance lock 使用 named semaphore，并且 shutdown 会等待 named-pipe listener 停止后再释放所有权；compact mode 使用 visual states 并在 480px 折叠非必要固定宽度顶栏段，compact 下 loading ring 会保持折叠，同时会按当前显示器 work area 校验已保存的 compact 位置；Log Viewer 和 latency probe 的 refresh/stop 竞态也已通过 run-id 和 selected-host 校验加固；长耗时 async command 运行中会拒绝重复触发，诊断包导出的日志枚举/zip 压缩移出 UI 线程，非当前环境的 WebView2 profile 删除也改到后台线程执行。
- 当前 checkpoint 有意不保留本地 C# 回归 harness；本地自动验证由 restore/build/format、仓库 guardrail、bridge script checks、空白差异检查和 VS2026 manual debug 组成。
- 详细实现历史保留在 [changelog.md](changelog.md) 和 [docs/superpowers/plans/2026-05-23-deep-refactor-hardening.md](docs/superpowers/plans/2026-05-23-deep-refactor-hardening.md)。

### 本项目是

- WinUI 3 + WebView2 远程管理外壳
- 托管 OpenClaw Control UI 会话的 Windows 原生入口
- 在现有 Web UI 之上增强原生 UX 的薄客户端

### 本项目不是

- 本地 Gateway 或节点宿主
- OpenClaw 前端的完整原生重写
- 可离线使用的独立应用

---

## 功能

| 功能 | 说明 |
|---|---|
| WebView2 Shell | 在原生窗口内托管远程 OpenClaw UI |
| Environment Switching | 管理多个托管 Control UI 端点 |
| Connection Status | 状态栏、错误 InfoBar 和重试支持 |
| Auto-Reconnect | 导航失败后自动重试 |
| Heartbeat | 周期性 Control UI 和 transport 探测，支持可配置重连阈值 |
| System Tray | 可配置的最小化/关闭到托盘行为，提供打开、重新加载、查看日志、紧凑模式、设置和退出操作（支持中文菜单） |
| Global Hotkey | 可配置的全局热键（默认 Ctrl+Alt+Space）随时显示/隐藏窗口，并支持设置界面校验和重置 |
| Instance Control | 可选多实例模式；默认关闭，重新启动会恢复已有托盘隐藏窗口 |
| Session Isolation | 每个配置环境使用独立 WebView2 profile 数据 |
| Latency Tooltip | 悬停延迟徽标查看最新、最小、平均、p95、最大往返时间和 Cloudflare PoP |
| Always on Top | 标题栏 Pin 按钮让窗口始终置顶，带原生 topmost fallback 和清晰的启用/未启用颜色 |
| Compact Mode | 缩小后的控制/状态窗口，适合屏幕角落放置 |
| Diagnostic Export | 一键导出脱敏设置、日志和运行时信息为 zip 包 |
| Theme | 顶部栏 System、Light、Dark 分段切换 |
| Language | English、Simplified Chinese、System |
| Diagnostics | 运行时、网络和会话检查 |
| Log Viewer | 查看当天日志并打开日志目录 |
| DevTools | 打开 WebView2 developer tools |

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

## 架构

```text
OpenClaw Manager
|- MainWindow (WinUI shell: XAML、WebView2 control swap、tray/window integration)
|  |- WebViewRecreationService
|  |- LiveShellSettingsApplier
|  |- SettingsDialog / SettingsPersistenceAdapter
|  `- MainViewModel (orchestration 和 bindable state)
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

设计原则：remote-first thin shell。真正的 OpenClaw runtime 位于 VPS 上；本应用是托管 Control UI 的原生控制面。

---

## 项目结构

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

### 关键目录

- `Services/`：配置、日志、诊断、WebView2 生命周期和恢复辅助逻辑
- `OpenClaw.Core/`：纯 .NET 共享代码的物理源码树，供 WinUI app 使用
- `Styles/`：共享 WinUI 字体、间距和状态资源
- `ViewModels/`：外壳状态、命令和设置编辑
- `Views/`：设置、关于和日志查看对话框
- `Strings/`：本地化 UI 资源

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
- 解决方案级 [Directory.Build.props](Directory.Build.props) 启用 NuGet lock file 和 static graph restore，用于可重复的 SDK-style 依赖恢复。
- 清理本地缓存后的预期流程是：

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
```

### 代码规范

- `.editorconfig` 是仓库根级代码风格契约：LF 换行、文件末尾换行、清理行尾空白、C#/XAML 四空格缩进、控制流必须加花括号，并通过 SDK 执行格式诊断。
- 参见 [docs/code-style.md](docs/code-style.md)，了解项目架构边界、partial class 职责、XAML 资源规则、Core 物理源码规则和验证命令。
- 提交涉及代码风格的改动前运行 `$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore`。

### 当前验证方式

当前 checkpoint 有意不保留本地 `tests/` harness。现行自动验证由 restore、x64 build、format、仓库结构 guardrail、bridge 脚本检查和空白差异检查组成：

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
$env:OPENCLAW_NODE='C:\Users\Zen\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
powershell -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
git diff --check
```

`tools\verify-bridge-scripts.ps1` 默认要求 Node.js，因为它是当前 embedded bridge assets 的行为验证入口。默认 `PATH` 上的 `node` 被阻止或不可用时，可以用 `OPENCLAW_NODE` 指定 Node 可执行文件；只有明确设置 `OPENCLAW_ALLOW_NODE_SKIP=1` 时才会跳过。

Bridge 脚本验证是当前用于拆分后的 hosted bridge JS assets 的行为检查。当前 checkpoint 有意不保留 C# 回归测试 harness。

真实 WebView2、Gateway、Cloudflare Tunnel、tray、hotkey 和 compact mode 行为仍需要 VS2026 manual debug。

Manual debug 需要明确覆盖：

- 真实 hosted Gateway 加载、任务提交、输出流式更新，并在不手动刷新时完成
- MODEL 在启动、session 切换、页面 reload 和 native-triggered `session-ready` replay 后都非空
- Cloudflare Tunnel 或反代 5xx 页面、未预期 4xx 页面、认证/approval 页面、origin rejection，以及上游恢复健康后的 recovery
- 真实 tunnel 响应中的 latency tooltip `cf-ray` / Cloudflare PoP 解析
- 托盘 show/hide、close-to-tray、reload、compact-mode 菜单入口和 single-instance 重新启动接管
- Settings 保存后的全局热键和 Always on Top 无需重启即可生效
- 480px compact mode 进入/退出，以及 relaunch 后 full-mode window bounds 不会恢复成 compact 尺寸
- light/dark/theme-switch 路径下 title-bar/DWM border color，包含顶部 1px 边缘
- Log Viewer 重复 refresh 和加载中关闭行为

### 当前限制

- 仓库内没有 active C# test harness；当前验证依赖 restore/build/format、guardrail scripts、bridge script checks 和 VS2026 manual debug。
- Bridge 脚本行为由 `tools\verify-bridge-scripts.ps1` 覆盖，但浏览器运行时行为仍需要 WebView2/VS2026 debug；当前 checkpoint 有意不保留 C# harness。
- `WebViewService` 已拆成聚焦 partial；新的 lifecycle、navigation、inspection、heartbeat、command 和 profile/session 行为应放到对应 partial，而不是继续塞进 root 文件。
- 真实 Gateway、Cloudflare Tunnel、反代错误页、tray、hotkey、single-instance、DWM title-bar 和 compact mode 行为仍需要 VS2026 manual debug；当前 checkpoint 有意不保留本地 C# harness。

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
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
```

### 首次启动

1. 应用会使用一个占位环境启动，在配置真实 Control UI URL 前不会导航 WebView2。
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

## 设置

Settings 窗口包含五个部分：

| 部分 | 内容 |
|---|---|
| Language | 显示语言 |
| General | 窗口、托盘、全局热键、窗口置顶和多实例行为 |
| Environments | 添加、编辑、删除和选择默认托管 Control UI 端点 |
| Sessions | 清理指定环境的 WebView2 会话数据 |
| Developer Tools | 诊断、日志、DevTools |

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

## 运行时要求

编译后的应用需要：

| 依赖 | 下载 |
|---|---|
| .NET 10 Desktop Runtime | [Download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| WebView2 Runtime | [Download](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) |

Windows 11 通常已经内置 WebView2 Runtime。Windows 10 用户可能需要手动安装。

---

## 更新日志

完整发布历史请见 [changelog.md](changelog.md)。

# OpenClaw Manager 简体中文

**语言：** [English](README.md) | 简体中文

**当前版本：** 3.3.6

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

## 当前 3.3.6 注意事项

- 新增 [docs/code-style.md](docs/code-style.md)，作为项目代码规范和架构边界的统一入口。
- 将顶部状态栏和底部状态栏的字号、间距、布局常量集中到 `src/OpenClaw/Styles` 下的 WinUI 资源字典。
- 保留 Core/app 架构清理结果，同时从当前 solution 中移除本地回归测试 harness。
- 将 `WebViewService` 的命令注入、heartbeat、Control UI inspection 和 profile 文件夹帮助器拆分到独立 partial 文件。
- 将所有 Core-compatible 源文件迁移到 `src/OpenClaw.Core` 物理源码树，包括窗口边界策略。
- 将主托管 bridge 浏览器脚本迁移为嵌入式 JS asset，C# 侧只负责资源加载、本地化字符串注入和 MODEL resolver 注入。
- 保留托管 MODEL 的 app-state 解析嵌入式 JS asset，覆盖默认值、`null` override、Map override 和对象形 payload 的运行路径。
- 保留 bridge 加固：session-ready 元数据、命令分发返回值、mutation filtering、安全 host messaging，以及按 generation 隔离的 WebView inspection cache 复用。
- VS2026 debug 验证架构清理分支后，将发布元数据同步到 `3.3.6`。

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
| System Tray | 可配置的最小化/关闭到托盘行为，提供打开、重新加载、查看日志、设置和退出操作（支持中文菜单） |
| Global Hotkey | 可配置的全局热键（默认 Ctrl+Alt+Space）随时显示/隐藏窗口，并支持设置界面校验和重置 |
| Instance Control | 可选多实例模式；默认关闭，重新启动会恢复已有托盘隐藏窗口 |
| Session Isolation | 每个配置环境使用独立 WebView2 profile 数据 |
| Latency Tooltip | 悬停延迟徽标查看最新、最小、平均、p95、最大往返时间和 Cloudflare PoP |
| Always on Top | 标题栏 Pin 按钮让窗口始终置顶，带原生 topmost fallback 和清晰的启用/未启用颜色 |
| Compact Mode | 缩小窗口（仅显示状态栏）适合屏幕角落放置 |
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
MainWindow
|- MainViewModel
|  |- ConfigurationService
|  `- LoggingService
`- WebViewService
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
|   |   |-- Models/
|   |   |-- Services/
|   |   |-- Strings/
|   |   |-- ViewModels/
|   |   `-- Views/
|   `-- OpenClaw.Core/
|       `-- OpenClaw.Core.csproj
```

### 关键目录

- `Services/`：配置、日志、诊断、WebView2 生命周期和恢复辅助逻辑
- `OpenClaw.Core/`：纯 .NET 共享代码的物理源码树，供 WinUI app 使用
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

当前 checkpoint 有意不保留本地 `tests/` harness。现行验证由 restore、x64 build、format、仓库结构 guardrail、bridge 脚本检查和空白差异检查组成：

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
powershell -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
git diff --check
```

`tools\verify-bridge-scripts.ps1` 会在可用时使用 Node.js；如果当前环境没有可执行的 Node，会干净跳过。默认 `PATH` 上的 `node` 被阻止或不可用时，可以用 `OPENCLAW_NODE` 指定 Node 可执行文件。

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

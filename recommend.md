# OpenClaw Manager 源码审计与后续开发可行性规划

**基线版本**：v3.1.3
**审计日期**：2026-05-08
**审计范围**：`src/`、`tests/`、README、开发日志、历史规划文档

## 0. 结论

OpenClaw Manager 当前已经不是 MVP。它是一个可用的 WinUI 3 + WebView2 远程管理壳，核心价值在于远程 Control UI 的原生承载、恢复、托盘、状态、诊断和本地持久化。

下一阶段不建议优先做大型分发链路或重写 Web UI。更可行的路线是先补齐原生壳相对浏览器的确定性差异：

1. **v3.2：原生召回与支撑能力**
   托盘菜单本地化、全局热键、诊断包导出、Cloudflare PoP tooltip、单实例 listener 关闭修正。
2. **v3.3：长任务工作流**
   Always-on-top、compact mode、LIVE -> IDLE toast、通知与窗口设置。
3. **v3.4：发布链路**
   在明确外部分发需求后再做 Velopack、签名、CI artifact、MSIX/Winget。

这条路线风险低，能持续提升真实使用价值，也不会过早把时间消耗在 release infrastructure 上。

---

## 1. 项目事实基线

### 1.1 定位

OpenClaw Manager 是远程 OpenClaw Control UI 的 Windows 原生薄壳：

- 不在本机运行 Gateway。
- 不重写 Control UI。
- 使用 WebView2 托管远程页面。
- 用 WinUI 3 提供窗口、托盘、主题、状态、恢复、诊断、设置和本地日志体验。
- 主要面向 VPS + Cloudflare Tunnel / 反向代理 / 公共 HTTPS origin 的部署方式。

后续功能应围绕一个判断标准排序：**它是否提供浏览器 tab 无法自然提供的系统级体验**。

### 1.2 技术栈

| 项 | 当前事实 |
|---|---|
| App Target | `net10.0-windows10.0.26100.0` |
| Core Target | `net10.0` |
| Windows App SDK | `1.8.260416003` |
| Windows SDK Build Tools | `10.0.28000.1839` |
| MVVM | `CommunityToolkit.Mvvm 8.4.2` |
| Packaging | Unpackaged, self-contained |
| 配置存储 | `%LOCALAPPDATA%\OpenClaw\settings.json` |
| 日志存储 | `%LOCALAPPDATA%\OpenClaw\logs\` |
| WebView2 数据 | `%LOCALAPPDATA%\OpenClaw\WebView2Data\` |
| 本地化 | `Strings/en-us/Resources.resw` + `Strings/zh-cn/Resources.resw` |

### 1.3 代码规模

| 范围 | 当前事实 |
|---|---:|
| C# 文件（生产） | 79 |
| 生产 C# | 约 10,796 行 |
| XAML（生产） | 约 874 行（App 49 + MainWindow 394 + SettingsDialog 334 + About 53 + LogViewer 44） |
| 测试 C# | 约 1,341 行 |
| 合计 C# | 约 12,137 行 |
| 测试清单 | 42 项 |
| 本地化资源键 | 193 个（en-us 和 zh-cn 完全一致） |
| StringResources 静态属性 | 约 200 个（覆盖全部 UI 文案） |

旧文档中关于“60+ 测试”的说法已经过期。当前测试清单是 42 项，覆盖恢复、设置、托盘、日志、延迟、版本和窗口 bounds 的关键路径。

---

## 2. 当前功能审计

### 2.1 已经可用的功能

| 功能 | 代码入口 | 状态 |
|---|---|---|
| WebView2 远程壳 | `WebViewService`, `MainWindow.WebView.cs` | 可用 |
| 多环境配置 | `ConfigurationService`, `SettingsViewModel` | 可用 |
| 每环境 profile 隔离 | `WebViewService.GetUserDataFolderForEnvironment()` | 可用 |
| 连接恢复状态机 | `ShellSessionCoordinator.*`, `HostedUiBridge` | 可用，复杂度高 |
| 心跳探测 | `WebViewService.StartHeartbeat()` | 可用 |
| 延迟徽标和 tooltip | `ControlUiLatencyService`, `LatencyHistory` | 可用 |
| 系统托盘 | `TrayIconService`, `MainWindow.Tray.cs` | 核心可用，菜单待补齐 |
| 单实例激活 | `SingleInstanceCoordinator` | 可用，关闭路径可加强 |
| 窗口 bounds 防崩坏 | `WindowBoundsUtilities`, `MainWindow.Lifecycle.cs` | 可用 |
| 主题和 DWM 同步 | `WindowFrameHelper`, `MainWindow.Theme.cs`, `SettingsDialog.Theme.cs` | 可用 |
| 中英 UI 资源 | `StringResources`, `.resw` | 可用，资源键一致 |
| 日志和日志查看 | `LoggingService`, `LogViewerDialog` | 可用 |
| 诊断摘要 | `DiagnosticService` | 可用，缺导出 |

### 2.2 半成品或高收益补齐点

| 项 | 当前状态 | 后续价值 |
|---|---|---|
| 托盘菜单 | 只有 Open OpenClaw / Settings / Exit，英文硬编码 | 补齐 Reload / View Logs / 本地化，成本低且直接提升完整度 |
| 全局热键 | 未实现 | 原生壳差异化最高的功能之一 |
| 诊断包导出 | 只有诊断摘要和日志查看 | 后续所有 bug 沟通都会受益 |
| Cloudflare PoP | 只判断 `cf-ray` 是否存在 | 低成本增强网络可观测性 |
| Compact / Always-on-top | 未实现 | 长任务工作流的核心入口 |
| Toast 通知 | 未实现 | 让用户不用反复切回查看任务是否完成 |
| 自动更新 | 未实现 | 对公开分发很重要，但依赖 release/signing 流程 |

---

## 3. 架构审计

### 3.1 运行链路

```text
App.OnLaunched()
|- Configuration.Load()
|- SingleInstanceCoordinator.CreatePrimaryOrSecondary()
|- App.ApplyLanguage()
`- MainWindow.Activate()
   |- MainViewModel.InitializeWebViewAsync()
   |  |- WebViewService.InitializeAsync()
   |  |- HostedUiBridge.InitializeAsync()
   |  |- ShellSessionCoordinator.AttachAsync()
   |  `- WebViewService.Navigate()
   |- ControlUiLatencyService.Start()
   `- WebViewService.StartHeartbeat()
```

状态回传有三条主线：

1. `HostedUiBridge` 注入 JS，监听 Control UI 状态、session ready、event gap。
2. `WebViewService` 监听 WebView 导航、process failure、heartbeat、Control UI inspection。
3. `ShellSessionCoordinator` 汇总 transport/session/stream/hosted UI 状态并决定恢复策略。

### 3.2 优势

- 恢复状态机已经拆成多个 partial，命名清楚，关键路径有测试。
- 设置写入使用 `AtomicFileWriter`，deferred save 有合并与 shutdown flush。
- WebView2 profile 按环境隔离，长期可支持更强的多环境体验。
- WinUI/Win32 边界问题已沉淀在 `DEVELOPMENT_NOTES.md`，后续改窗口、托盘、主题时有明确约束。
- `.gitattributes` 已固定源码、资源、Markdown 的 LF 行尾，diff 可控。
- 中英 `.resw` 资源键一致，没有发现缺键。

### 3.3 主要风险

| 优先级 | 风险 | 影响 | 处理建议 |
|---|---|---|---|
| P0 | `WebViewService.cs` 1428 行，混合导航、heartbeat、inspection、profile 管理 | 任一改动都可能影响恢复路径 | 在做 PoP / 诊断导出 / multi-profile 前先拆小服务，不做一次性大重构 |
| P0 | `OpenClaw.Core` 通过 linked source 复用主项目文件 | 新增共享文件容易漏配 app/core 编译项 | 独立做一次物理迁移，减少 `<Compile Remove>` |
| P1 | `App.Logger` / `App.Configuration` 静态单例 | 服务继续增加后生命周期和测试替身会更难 | 先新建服务都走构造注入，v3.3 后考虑 `AppServices` 组合根 |
| P1 | `TrayIconService` 菜单硬编码英文 | 中文环境体验割裂 | 增加 `TrayMenuStrings` record，由 MainWindow 注入 |
| P1 | `SingleInstanceCoordinator.Dispose()` cancel 后不等待 listener task | 快速退出/重启时可能短暂 pipe 竞争 | 增加可等待关闭路径和测试 |
| P1 | 诊断只能看摘要，不能打包 | bug 报告仍依赖用户手动找文件 | v3.2 做诊断包导出 |
| P2 | `cf-ray` 只识别存在性 | Cloudflare 用户无法看到具体路由节点 | 解析 header 后缀，如 `LAX`、`SJC` |
| P2 | `SettingsViewModel` 直接依赖 `App.Configuration` | 单元测试和未来 DI 不友好 | 先补行为测试，后续再迁移 |

---

## 4. 深度源码观察

### 4.1 恢复状态机

`ShellSessionCoordinator` 是项目最关键的复杂子系统。它覆盖：

- reconnect：延迟重连、通知页面、请求 session refresh，失败后 reload。
- soft resync：请求 lightweight sync 和 recent messages。
- hard refresh：完整 reload，带 cooldown。
- auth / pairing / origin issue 短路。
- background hidden / visible 生命周期感知。
- event gap 检测与恢复升级。

当前测试已覆盖取消、降级、auth 短路、后台恢复、hard refresh cooldown 等路径。建议补充三类测试：

| 缺口 | 原因 |
|---|---|
| 并发恢复请求节流 | `_recoveryGate` 是核心保护，但缺显式测试 |
| reconnect delay 边界 | backoff 逻辑需要防止配置极值导致异常等待 |
| background threshold 边界 | 后续 compact/hidden 行为会依赖这些判断 |

### 4.2 WebView 重建管线

`MainWindow.WebView.cs` 使用 150ms debounce 合并重建请求，流程是：

```text
ScheduleWebViewRecreation(reason)
  -> DispatcherQueueTimer
  -> RecreateWebViewAsync()
  -> Close old WebView2
  -> new WebView2
  -> ViewModel.InitializeWebViewAsync()
```

优点是简单、有效、有 instrumentation。缺口是没有 circuit breaker。如果 WebView2 runtime 损坏或初始化连续失败，理论上可能反复重建。建议 v3.2 或 v3.3 增加 `每分钟最多 5 次重建` 的保护，超过后停止重建并显示可操作错误。

### 4.2.1 MainWindow XAML 布局

MainWindow.xaml 是 394 行的 6 行 Grid 布局：

| Row | Height | 内容 |
|---:|---|---|
| 0 | 37px | 自定义标题栏（图标 + 标题 + 拖拽区域） |
| 1 | Auto | 命令栏（环境选择器 + 状态条 + 延迟徽标 + 操作按钮 + 主题切换） |
| 2 | Auto | 连接错误 InfoBar |
| 3 | Auto | 诊断 InfoBar |
| 4 | * | WebView2 宿主区域 + 加载 ProgressRing |
| 5 | Auto | 底部状态栏（状态点 + 状态文本 + 环境名） |

状态条（Row 1 中间的 pill-shaped Border）包含 6 列：HB 文本、12 个心跳指示点、MODEL 标签+值、AUTH 标签、STATUS 标签+值、12 个 run 指示点。

这个布局对 compact mode 的影响：compact 模式应该隐藏 Row 2-4（InfoBar + WebView），只保留 Row 0（标题栏）、Row 1（状态条）和 Row 5（底部状态栏），或者更激进地只保留 Row 1 的状态条。实现方式是设置 `RowDefinition.Height = 0` 或 `Visibility = Collapsed`。

状态条中的 12 个 run 指示点有动画效果：`LIVE` 状态下以 430ms 间隔旋转高亮（`_runAnimationFrame` 循环），`IDLE`/`WAIT` 状态下静态渐变。这个动画由 `MainWindow` 的 `_runIndicatorTimer` 驱动，窗口 hidden 时自动停止（`UpdateRunIndicatorAnimationState`）。Compact mode 如果保留 run 指示点，动画逻辑无需改动。

### 4.3 WebViewService 职责边界

`WebViewService` 当前承担：

- WebView2 初始化和导航。
- reload / stop / DevTools / clear browsing data。
- heartbeat loop。
- session-aware health probe。
- Control UI inspection 和 JSON parsing。
- environment profile path 生成、删除、重命名迁移。

这解释了为什么它膨胀到 1428 行。建议不要立即大拆，而是在新增功能时顺手切出边界：

| 拆分目标 | 触发任务 | 目标 |
|---|---|---|
| `WebViewProfileService` | multi-profile 或诊断包需要 profile 信息时 | 管理 profile path、删除、重命名 |
| `ControlUiInspectionService` | PoP / 诊断增强时 | 管理 inspection JS、snapshot parsing、cache/coalesce |
| `HeartbeatService` | heartbeat 策略继续增长时 | 从 WebView lifecycle 中分离周期探测 |

### 4.4 HostedUiBridge 机制与风险

`HostedUiBridge` 通过 `AddScriptToExecuteOnDocumentCreatedAsync()` 注入约 700 行 IIFE JavaScript（由 `BuildBridgeScript()` 在 C# 端拼接生成）。

**检测侧**（JS → C#）：

- `MutationObserver` 监听 DOM 变化（`childList`、`subtree`、特定 `attributes`），debounce 180ms 后执行 `inspectControlUi()`。
- `collectSignalText()` 收集 `[role="alert"]`、`[role="status"]`、`[class*="auth"]`、`[class*="error"]` 等选择器命中的可见元素文本（最多 6 段、900 字符）。
- 用 `matchAny(text, needles)` 对收集到的文本做**英文关键词匹配**（如 `'authentication required'`、`'token missing'`、`'origin not allowed'`）。这些 needle 是硬编码英文，匹配的是上游 Control UI 输出的英文错误文案。
- 检测 `shellDetected`（页面有可见 textarea/input/contenteditable 或带特定 label 的 button）判断 Control UI 是否加载完成。
- 检测 `isBusy`（stop/abort 按钮、`aria-busy="true"`、`data-state="streaming"` 等信号）。
- `readCurrentModel()` 从 `[data-current-model]`、select 下拉、combobox 等元素提取当前模型名。

**输出侧**：通过 `chrome.webview.postMessage()` 发送三种 kind：`openclaw-control-ui-status`（状态快照）、`openclaw-session-ready`（会话就绪）、`openclaw-event-gap`（事件序列号间隙）。输出中的 `summary`/`detail` 使用 `STRINGS` 字典（从 `.resw` 注入本地化文本），所以用户看到的状态描述是中/英文的，但检测逻辑始终匹配英文。

**命令通道**（C# → JS）：暴露 `window.__openClawHostBridge.onCommand(message)`，支持 `refresh_session`、`fetch_recent_messages`、`lightweight_sync`、`reconnect_intent` 等命令。命令执行尝试调用上游可能暴露的 API（`window.chat.reconnect`、`window.__openclaw.chat.sync` 等多个候选路径），不存在则 fallback 到 `CustomEvent` 分发。

**事件间隙检测**：`reportSeq(seq, stateVersion)` 由上游调用（如果支持），检测 `currentSeq !== lastSeq + 1` 时报告 gap，触发 C# 端恢复。

**风险评估**：

- 主要风险：英文关键词匹配依赖上游 Control UI 的具体错误文案。如果上游改了 `'token missing'` 为 `'no token provided'`，bridge 会 miss。
- 缓解因素：每个状态检测都覆盖了多个同义表达（auth 检测同时匹配 10 个变体），容错性较好。
- 不存在的风险：之前担心的"中文匹配 vs 英文输出"问题不存在——检测侧始终用英文 needle，与 UI 显示语言无关。

**建议**：短期保持现状。中期如果上游改版导致 bridge 失效，把匹配模式抽到 `bridge-patterns.json`。长期推动上游暴露稳定 shell API（当前 bridge 已尝试调用 `window.__openclaw.chat.*`，说明上游可能已有部分支持）。任何改 `HostedUiBridge` 的任务都应配套测试或至少增加可观测日志。

### 4.5 延迟探测和心跳

当前有两个 HTTP 探测：

| 机制 | 服务 | 间隔 | 用途 |
|---|---|---:|---|
| 延迟探测 | `ControlUiLatencyService` | 3s | UI latency badge / tooltip |
| 心跳探测 | `WebViewService` | settings 控制 | 恢复触发 |

两者都请求 `__openclaw/control-ui-config.json`。现阶段重复请求不是问题，但 PoP 解析应优先放在 `ControlUiLatencyService`，因为它已经面向 UI tooltip。不要为了 PoP 解析提前合并两个探测机制。

### 4.6 本地化基础设施

`StringResources` 是一个静态类，约 200 个静态属性对应 193 个 `.resw` 资源键。它通过 `Microsoft.Windows.ApplicationModel.Resources.ResourceLoader` 加载资源，fallback 策略是返回 key 名本身。

**对托盘菜单本地化的影响**：`TrayIconService` 位于 `OpenClaw.Services` namespace，被 `OpenClaw.Core.csproj` 通过 link 引用。Core 是纯 .NET 10（无 WinUI），不能依赖 `StringResources`（它使用 `Microsoft.Windows.ApplicationModel.Resources`）。

**解决方案**：最简单的做法是给 `TrayIconService` 构造函数增加一个 `TrayMenuStrings` record（包含 `OpenLabel`、`ReloadLabel`、`ViewLogsLabel`、`SettingsLabel`、`ExitLabel`），由 `MainWindow.Tray.cs` 在创建时从 `StringResources` 取值注入。不需要改 `.resw`，因为这些字符串（如 "Settings"、"Reload"）已经在主 UI 中有翻译。

### 4.7 日志和诊断

`LoggingService` 已经支持后台队列、UTC 每日日志、14 天保留、best-effort flush。`DiagnosticService` 已经能判断 WebView2 runtime、HTTP status、session 状态和 instrumentation。

因此诊断包导出的实现成本低，价值高。推荐导出内容：

- 最近 7 天 `openclaw-YYYY-MM-DD.log`。
- 脱敏后的 `settings.json`。
- `DiagnosticReport.ToSummary()`。
- runtime 信息：OS、.NET、WebView2、app version。
- 当前 selected environment 的 name 和 URL host hash，不直接暴露完整 URL。

---

## 5. 功能可行性矩阵

| 功能 | 用户价值 | 工程风险 | 估算 | 推荐阶段 | 结论 |
|---|---|---|---:|---|---|
| ~~托盘菜单本地化 + Reload/View Logs~~ | ~~中~~ | ~~低~~ | ~~0.5d~~ | ~~v3.2~~ | ~~已完成~~ |
| ~~全局热键~~ | ~~高~~ | ~~中低~~ | ~~2d~~ | ~~v3.2~~ | ~~已完成~~ |
| ~~诊断包导出~~ | ~~高~~ | ~~低~~ | ~~2d~~ | ~~v3.2~~ | ~~已完成~~ |
| ~~Cloudflare PoP tooltip~~ | ~~中~~ | ~~低~~ | ~~0.5d~~ | ~~v3.2~~ | ~~已完成~~ |
| ~~SingleInstance 可等待关闭~~ | ~~中~~ | ~~低~~ | ~~0.5d~~ | ~~v3.2~~ | ~~已完成~~ |
| ~~Always-on-top~~ | ~~中高~~ | ~~低~~ | ~~1d~~ | ~~v3.3~~ | ~~已完成~~ |
| ~~Compact mode~~ | ~~高~~ | ~~中~~ | ~~2d~~ | ~~v3.3~~ | ~~已完成~~ |
| Toast 完成通知 | 高 | 中 | 2d | v3.3 | 做 |
| Velopack 自动更新 | 高，但依赖分发 | 中高 | 3-5d | v3.4 | 等分发需求明确 |
| MSIX / Winget | 外部分发高价值 | 高 | 3-5d | v3.4+ | 条件触发 |
| 多语言扩展 | 中 | 中 | 5d+ | 条件触发 | 等用户需求 |
| Credential Manager | 不明确 | 中 | 5d+ | 暂缓 | 需要威胁模型 |
| Native chat UI | 低且偏离定位 | 高 | 高 | 不做 | 不建议 |
| 离线会话缓存 | 不明确且冲突 | 高 | 高 | 不做 | 不建议 |

---

## 6. 推荐路线图

### v3.2：原生召回与支撑能力

**目标**：让应用比浏览器 tab 更容易召回，并让问题反馈能带证据。
**周期**：5-7 个工程日。
**风险**：低到中。

| 顺序 | 工作包 | 主要文件 | 测试入口 | 验收 |
|---:|---|---|---|---|
| ~~1~~ | ~~托盘菜单本地化和补齐~~ | ~~`TrayIconService`, `MainWindow.Tray.cs`, `.resw`~~ | ~~tray menu string/command tests~~ | ~~中文 UI 下菜单为中文，含 Reload / View Logs~~ |
| ~~2~~ | ~~全局热键~~ | ~~`GlobalHotkeyService`, `AppSettings`, `SettingsDialog`~~ | ~~hotkey parsing / conflict fallback tests~~ | ~~默认 `Ctrl+Shift+F12` 可显示/隐藏窗口~~ |
| ~~3~~ | ~~诊断包导出~~ | ~~`DiagnosticBundleService`, `DiagnosticService`, `LogFileUtilities`~~ | ~~redaction / zip content tests~~ | ~~生成 zip，包含日志、脱敏 settings、诊断摘要~~ |
| ~~4~~ | ~~Cloudflare PoP tooltip~~ | ~~`ControlUiLatencyService`, `LatencyHistory`~~ | ~~cf-ray parsing tests~~ | ~~tooltip 显示 `PoP: XXX`~~ |
| ~~5~~ | ~~单实例关闭修正~~ | ~~`SingleInstanceCoordinator`~~ | ~~listener cancel tests~~ | ~~dispose 后 pipe listener 释放~~ |
| ~~6~~ | ~~文档同步~~ | ~~`README.md`, `readme_zh.md`, `recommend.md`~~ | ~~markdown diff check~~ | ~~功能和设置说明同步~~ |

**v3.2 不建议夹带的内容**：Velopack、compact mode、toast、DI 重构。它们都能做，但会扩大本轮风险。

**v3.2 退出门槛**：

| 门槛 | 判断方式 |
|---|---|
| 不引入新的 WebView 恢复回归 | 42 项现有测试通过，并新增 hotkey / tray / diagnostic / cf-ray 对应测试 |
| 不扩大 release 流程范围 | 不引入签名、installer、CI artifact、自动下载 |
| 不增加敏感数据暴露面 | 诊断包默认脱敏，不导出 WebView2 cookies/cache |
| 中文体验不倒退 | 新增 UI 文案都有 en-us / zh-cn 资源或复用已有资源 |
| 用户可回滚 | 每个新功能都有设置开关或失败降级路径 |

### v3.3：长任务工作流

**目标**：让用户可以把 OpenClaw 放在角落或后台，不必反复切回查看状态。
**周期**：5-7 个工程日。
**风险**：中。

**关键技术前提**（已在源码中确认）：

Toast 通知的触发依赖 `FormatWorkStatus()` 的状态判断逻辑：
- `LIVE`：`snapshot.IsBusy == true` 或 `snapshot.WorkState == "busy"`（bridge 检测到 stop/abort 按钮或 `aria-busy` 等信号）。
- `IDLE`：`snapshot.WorkState == "idle"` 或 `snapshot.Phase == Connected`。
- `WAIT`：其他情况。

Toast 应只在 `LIVE → IDLE` 跃迁且持续 ≥ 1.5s 时触发。启动加载（`WAIT → IDLE`）和短暂抖动不应触发。

Compact mode 的窗口尺寸需要注意 `WindowBoundsUtilities` 的约束：当前 `MinimumPersistedWindowWidth = 640`、`MinimumPersistedWindowHeight = 480`。Compact 窗口（如 360×120）会低于这个阈值，因此需要：
- 要么 compact bounds 走独立的持久化字段（不经过 `CanPersistWindowBounds` 校验）。
- 要么降低阈值并区分 normal/compact 模式。
- 推荐前者，避免影响现有 normal bounds 保护逻辑。

| 顺序 | 工作包 | 主要文件 | 测试入口 | 验收 |
|---:|---|---|---|---|
| ~~1~~ | ~~Always-on-top~~ | ~~`MainWindow`, `AppSettings`, `SettingsDialog`~~ | ~~settings persistence tests~~ | ~~可切换、可持久化~~ |
| ~~2~~ | ~~Compact mode~~ | ~~`MainWindow.xaml`, `MainWindow.Lifecycle.cs`, `WindowBoundsUtilities`~~ | ~~compact bounds tests~~ | ~~compact bounds 不污染 normal bounds~~ |
| 3 | Toast 完成通知 | 新增 `NotificationService`, `MainViewModel.Status.cs` | LIVE -> IDLE debounce tests | 只有稳定完成才通知 |
| 4 | Notifications / Window 设置 | `SettingsDialog.xaml`, `SettingsViewModel`, `.resw` | settings save/load tests | 开关清晰，默认值合理 |
| ~~5~~ | ~~WebView recreation circuit breaker~~ | ~~`MainWindow.WebView.cs`~~ | ~~repeated failure tests~~ | ~~连续失败后显示错误，不无限重建~~ |

### v3.4：发布与更新链路

**触发条件**：开始面向外部用户分发，或出现“用户停留旧版本 / SmartScreen 阻拦 / 安装复杂”的真实反馈。
**周期**：4-8 个工程日。
**风险**：中到高。

建议顺序：

1. 明确 release artifact：先固定 `win-x64`。
2. 加 GitHub Releases 手动检查更新。
3. 加 Velopack 用户确认安装。
4. 明确签名策略。
5. 再考虑 CI、MSIX、Winget。

不要把自动更新和 MSIX/Winget 放在同一个版本里做。

---

## 7. v3.2 具体实施建议

### 7.1 新增设置字段

```csharp
public string GlobalHotkey { get; set; } = "Ctrl+Alt+Space";
public bool EnableGlobalHotkey { get; set; } = true;
public bool StartMinimized { get; set; } = false;
```

新增字段必须满足：

- 旧 `settings.json` 缺字段时自动使用默认值。
- 设置 UI 有中英资源。
- 禁用热键时不注册 Win32 hotkey。
- 注册失败只降级，不影响启动。

### 7.2 诊断包脱敏规则

建议先保持保守：

| 数据 | 处理 |
|---|---|
| gateway URL | 保留 scheme，host 替换为 `<host>`，path 替换为 `<path>` |
| query string | 全部替换为 `<query>` |
| token / key / secret 字段 | 值替换为 `<redacted>` |
| WebView2 cookies/cache | 不导出 |
| logs | 原样导出，但后续可加 token pattern redact |

### 7.3 PoP 解析规则

`cf-ray` 常见形态可以按最后一个 `-` 后面的部分解析：

```text
8f123456789abcd-LAX -> LAX
8f123456789abcd     -> unknown
```

解析失败时不显示 PoP 行，避免误导用户。

### 7.4 回滚策略

| 功能 | 回滚方式 |
|---|---|
| 托盘菜单本地化 | 回到硬编码英文菜单 |
| 全局热键 | 设置默认关闭，注册失败不影响启动 |
| 诊断包导出 | 隐藏按钮，不影响现有诊断摘要 |
| PoP tooltip | 只显示现有 latency history |
| 单实例关闭修正 | 保留现有 Dispose 路径，新增 async close 失败时 best-effort |

---

## 8. 架构演进计划

### 8.1 近期小步清理

| 项 | 触发时机 | 做法 |
|---|---|---|
| ~~`TrayIconService` 文案注入~~ | ~~v3.2 托盘菜单~~ | ~~增加 `TrayMenuStrings` record~~ |
| ~~`SingleInstanceCoordinator` 可等待关闭~~ | ~~v3.2 生命周期修正~~ | ~~增加 `StopAsync()`~~ |
| ~~`cf-ray` 解析模型~~ | ~~v3.2 PoP tooltip~~ | ~~`CloudflareRayParser` + `ControlUiLatencySnapshot` 增加 route hint~~ |
| settings migration 测试 | 每次新增 settings 字段 | 覆盖旧 JSON、null section、默认值 |

### 8.2 中期结构调整

| 项 | 触发条件 | 建议 |
|---|---|---|
| 物理移动 Core 文件 | v3.2 后第一个债务窗口 | 把纯 .NET 文件移入 `src/OpenClaw.Core/`，App 引用 Core |
| 拆分 `WebViewService` | PoP、诊断、multi-profile 任一继续增长时 | 先拆 profile 和 inspection，不一次性拆完 |
| 引入 `AppServices` | 新增 3 个以上服务后 | 集中创建和 dispose，不急着上完整 DI 容器 |
| 测试框架迁移 xUnit | 测试超过 80 项或需要 coverage | 当前 minimal runner 继续保留即可 |

---

## 9. 不建议近期投入

| 项 | 原因 |
|---|---|
| 完整 native chat UI | 与上游 Control UI 重复，破坏 thin shell 定位 |
| 离线会话缓存 | 与非离线设计冲突，安全和一致性边界复杂 |
| 语音输入 | 没有明确用户证据，权限和识别质量风险高 |
| Credential Manager 重构 | 缺少具体威胁模型，ROI 不清晰 |
| 多 Gateway 同时连接 | 会显著放大状态机复杂度 |
| 复杂图表面板 | 当前 tooltip + heartbeat/run 指示点已经足够 |

---

## 10. 开发执行规则

每个功能进入实现前都要满足：

1. 明确用户价值和退出标准。
2. 明确设置字段、默认值、迁移行为。
3. 明确中英资源 key。
4. 先补测试或至少补可验证的轻量测试。
5. 跑 `dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore`。
6. 更新 README / `readme_zh.md`。

建议每个 PR 只做一个功能包。v3.2 可以拆成 4 个提交：

1. tray menu localization and commands
2. global hotkey service
3. diagnostic bundle export
4. Cloudflare PoP tooltip and single-instance cleanup

这样回滚清晰，也不会把 Win32 hotkey、zip 导出和 WebView 探测混在同一处风险里。

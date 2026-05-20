# Changelog

**Language:** English | [简体中文](#简体中文)

Full release notes for OpenClaw Manager. See [README.md](README.md) / [readme_zh.md](readme_zh.md) for project overview.

---

## English

### v3.3.4 (2026-05-20)

- Updated the About dialog GitHub profile links and labels to `https://github.com/Guijianchou`.
- Applied Always-on-top and global hotkey changes immediately after Settings save.
- Tightened compact mode top-bar layout so nonessential status segments collapse cleanly at 480px.
- Guarded WebView2 status probes with WebView/navigation generation ownership so stale async script results cannot overwrite current state.
- Made heartbeat loop ownership and log viewer loading explicit: heartbeat owns its timer/task, and log tailing runs off the UI thread.
- Synced app, assembly, file, package manifest, application manifest, README, and regression-test version metadata to `3.3.4`.

### v3.3.3 (2026-05-19)

- Fixed the top MODEL value typography so it uses the same 12px text size as the native status bar.
- Hardened hosted OpenClaw model detection for app-state variants, URL session keys, Map-backed model overrides, and non-string payload normalization.
- Deferred app-state default MODEL fallbacks, including `null` session overrides, so root defaults do not mask later nested active-session models.
- Synced app, assembly, file, package manifest, application manifest, README, and regression-test version metadata to `3.3.3`.

### v3.3.2 (2026-05-19)

- Added stale busy-stream detection for hosted chat sessions: the bridge now tracks chat activity signatures and polls busy connected pages more frequently.
- Added stale-stream recovery escalation: OpenClaw Manager first soft-resyncs lightweight state and recent messages, then performs a hard refresh after the soft-resync budget is exhausted.
- Narrowed the input-focus reload guard so an empty focused editor no longer blocks recovery refreshes, while unsent user text still defers automatic reload.
- Expanded diagnostics with the latest hosted UI phase, busy state, stale duration, and focused-input text state.
- Synced app, assembly, file, package manifest, application manifest, README, and regression-test version metadata to `3.3.2`.

### v3.3.1 (2026-05-17)

- Fixed the status-bar MODEL field so it reads the current model from OpenClaw Web UI's explicit model selector.
- Hardened MODEL detection to read OpenClaw app state when DOM controls are not ready, and preserve the last non-empty model across transient empty snapshots.
- Reduced WebView2 CPU spikes while long right-sidebar content loads by ignoring status-irrelevant sidebar DOM changes and hosted preview frames.
- Expanded the top status pill so long provider/model names retain more context before AUTH/Status indicators, and moved connected OpenClaw settings/cron pages onto an app-state status fast path to avoid DOM mutation storms.
- Documented the current mitigation status after manual testing: MODEL display and WebView2 CPU spikes are improved enough for current use, while very long model names and especially heavy settings/Cron pages remain areas to monitor.
- Synced app, assembly, file, package manifest, application manifest, README, and regression-test version metadata to `3.3.1`.

### v3.3.0 (2026-05-12)

- Refined Settings with PowerToys-style settings rows, compact ToggleSwitch spacing, and localized always-on-top text.
- Reorganized Settings navigation to Language, General, Environments, Sessions, and Dev Tools.
- Polished the Environment editor by grouping Set as default and Apply into a single compact action bar.
- Removed the manual GitHub update-check UI and service from the About dialog.
- Synced app, assembly, file, package manifest, application manifest, About dialog, README, and regression-test version metadata to `3.3.0`.

### v3.2.1 (2026-05-09)

- Removed the toast notification feature because Windows toast activation is not a good fit for the current unpackaged WebView2 shell.
- Removed notification settings, notifier lifecycle wiring, and related regression coverage.
- Kept the v3.2 native features intact: global hotkey, tray commands, diagnostic export, Cloudflare PoP tooltip, always-on-top, compact mode, and WebView2 circuit breaker.
- Synced app, assembly, file, package manifest, application manifest, About dialog, and regression-test version metadata to `3.2.1`.

### v3.2.0 (2026-05-09)

- Added localized tray context menu with Reload, View Logs, status header, and full Chinese support.
- Added configurable global hotkey (default Ctrl+Alt+Space) to show/hide the main window from anywhere, including Settings UI controls, validation, and reset-to-default support.
- Added diagnostic bundle export: one-click zip of redacted settings, recent logs, runtime info, and diagnostic summary.
- Added Cloudflare PoP (Point of Presence) display in the latency tooltip by parsing the `cf-ray` response header.
- Added `StopAsync` to `SingleInstanceCoordinator` for clean listener shutdown without pipe races.
- Added always-on-top pin button in the title bar with persistent setting, native `HWND_TOPMOST` fallback, and theme-aware active/inactive colors so the Pin state remains visible in light and dark themes.
- Added compact mode: reduced window (480x120) showing only status bars, with independent position persistence.
- Added task-complete toast notification when work status transitions from LIVE to IDLE (debounced, only when window is hidden).
- Added WebView2 recreation circuit breaker: stops runaway recreation after 5 attempts per minute and shows actionable error.
- Added `AppSettings` fields for global hotkey, always-on-top, compact mode, and notification preferences.
- Synced app, assembly, file, package manifest, application manifest, and About dialog version metadata to `3.2.0`.

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

## 简体中文

### v3.3.4 (2026-05-20)

- About 对话框里的 GitHub 主页链接和文案已更新为 `https://github.com/Guijianchou`。
- Settings 保存后会立即应用 Always-on-top 和全局热键变更，不再需要重启。
- 收紧紧凑模式顶栏布局，在 480px 宽度下折叠非必要状态段并保留模型/状态可读性。
- WebView2 状态探测现在带有 WebView/导航 generation 归属，避免过期异步脚本结果覆盖当前状态。
- 明确 heartbeat loop 和日志查看器的生命周期：heartbeat 独立持有 timer/task，日志 tail 在 UI 线程之外加载。
- 同步 app、assembly、file、package manifest、application manifest、README 和回归测试版本元数据到 `3.3.4`。

### v3.3.3 (2026-05-19)

- 修复顶部 MODEL 值的字号，使其与原生状态栏的 12px 文本字号一致。
- 加固托管 OpenClaw 模型检测，支持 app-state 变体、URL session key、Map 形式模型 override，以及非字符串 payload 归一化。
- 延后 app-state 默认 MODEL fallback，包括 session 的 `null` override，避免根节点默认模型盖掉后续嵌套的当前会话模型。
- 同步 app、assembly、file、package manifest、application manifest、README 和回归测试版本元数据到 `3.3.3`。

### v3.3.2 (2026-05-19)

- 增加托管聊天会话的 stale busy-stream 检测：bridge 会跟踪聊天活动签名，并更频繁轮询 busy 的已连接页面。
- 增加 stale-stream 恢复升级链路：OpenClaw Manager 会先 soft-resync lightweight state 和 recent messages，soft-resync 预算耗尽后再执行 hard refresh。
- 收窄输入焦点 reload 保护：空的聚焦编辑器不再阻止恢复刷新，但存在未发送文本时仍会延迟自动 reload。
- 扩展诊断信息，加入最近一次 hosted UI phase、busy 状态、stale 持续时间和聚焦输入框文本状态。
- 同步 app、assembly、file、package manifest、application manifest、README 和回归测试版本元数据到 `3.3.2`。

### v3.3.1 (2026-05-17)

- 修复状态栏 MODEL 字段不显示当前模型的问题，现在会读取 OpenClaw Web UI 明确的模型选择器。
- 加固 MODEL 检测：DOM 控件尚未就绪时会读取 OpenClaw app state，并在瞬时空快照期间保留最近一次非空模型。
- 降低右侧栏长内容加载时的 WebView2 CPU 飙升风险，忽略与状态栏无关的 sidebar DOM 变化和内嵌 preview frame。
- 拉宽顶部状态 pill，让较长 provider/model 名称在 AUTH/Status 指示器前保留更多上下文；已连接的 OpenClaw settings/cron 页面改走 app-state 状态快路径，避免 DOM mutation storm。
- 记录手动验证后的当前缓解状态：MODEL 显示和 WebView2 CPU 飙升已有可用级别改善，但超长模型名和特别重的 settings/Cron 页面仍需要继续观察。
- 同步 app、assembly、file、package manifest、application manifest、README 和回归测试版本元数据到 `3.3.1`。

### v3.3.0 (2026-05-12)

- 优化 Settings，采用 PowerToys 风格设置行、紧凑 ToggleSwitch 间距，并补齐窗口置顶文案本地化。
- 将 Settings 导航整理为 Language、General、Environments、Sessions 和 Dev Tools。
- 优化 Environment 编辑区域，将 Set as default 和 Apply 收进同一个紧凑动作栏。
- 从 About 对话框移除手动 GitHub 更新检查 UI 和服务。
- 同步 app、assembly、file、package manifest、application manifest、About dialog、README 和回归测试版本元数据到 `3.3.0`。

### v3.2.1 (2026-05-09)

- 移除 toast 通知功能；当前应用是 unpackaged WebView2 shell，Windows toast activation 不适合这个分发和启动模型。
- 移除通知设置、notifier 生命周期接线和相关回归测试。
- 保留 v3.2 的纯原生能力：全局热键、托盘命令、诊断导出、Cloudflare PoP tooltip、always-on-top、compact mode 和 WebView2 circuit breaker。
- 同步 app、assembly、file、package manifest、application manifest、About dialog 和回归测试版本元数据到 `3.2.1`。

### v3.2.0 (2026-05-09)

- 添加本地化托盘右键菜单，包含 Reload、View Logs、状态标题和完整中文支持。
- 添加可配置全局热键（默认 Ctrl+Alt+Space）用于随时显示/隐藏主窗口，并在 Settings 中提供输入、校验和恢复默认值。
- 添加诊断包导出：一键打包脱敏设置、近期日志、运行时信息和诊断摘要。
- 在延迟 tooltip 中解析 `cf-ray` 响应头并显示 Cloudflare PoP。
- 为 `SingleInstanceCoordinator` 添加 `StopAsync`，关闭时等待 listener task，避免 pipe dispose 竞态。
- 添加标题栏 Always-on-top Pin 按钮，支持持久化设置、原生 `HWND_TOPMOST` fallback，并使用主题感知的启用/未启用颜色，浅色和深色主题下都能区分当前状态。
- 添加 Compact Mode：缩小为仅显示状态栏的窗口，并独立持久化 compact 位置。
- 添加任务完成 toast：工作状态从 LIVE 切换到 IDLE 且窗口不可见时发送通知，并带 debounce。
- 添加 WebView2 recreation circuit breaker：一分钟内超过 5 次重建后停止 runaway recreation，并显示可操作错误。
- 添加 global hotkey、always-on-top、compact mode 和通知偏好的 `AppSettings` 字段。
- 同步 app、assembly、file、package manifest、application manifest 和 About dialog 版本元数据到 `3.2.0`。

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

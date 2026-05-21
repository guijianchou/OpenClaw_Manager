# OpenClaw Development Notes

## Project Code Standards

Canonical checklist: [docs/code-style.md](docs/code-style.md).

This project uses C# and WinUI conventions, but follows the Linux engineering bias toward small, explicit, boring code:

- Keep control flow readable. Use braces on every `if`, loop, and branch even for one-line bodies.
- Keep files focused. New service/view-model code should prefer small partials or helper types over growing `WebViewService`, `HostedUiBridge`, or the test harness further.
- Own background work explicitly. A background loop should have a stored `Task`, a stored cancellation source, and one logging boundary for exceptions.
- WebView/CoreWebView2 async work must carry a generation or equivalent ownership token across awaits before applying results back to app state.
- Hosted bridge JavaScript belongs behind dedicated script-builder and asset seams; keep native WebView orchestration in `HostedUiBridge`, script assembly in `HostedUiBridge.Script.cs`, and executable pure JS logic in focused assets with behavior tests.
- Pure settings, diagnostics, parser, policy, telemetry, recovery, and window-bounds code should live physically under `src/OpenClaw.Core`; there are no current linked Core source exceptions.
- Settings that affect live shell behavior must map to a current-process apply path, not only persisted configuration.
- Prefer structured logs with stable event keys and context objects. Avoid interpolated operational logs for state transitions.
- Keep user-visible text in `StringResources` unless the string is diagnostic-only or a protocol/status token.
- Keep `.editorconfig` as the source of formatting truth. Do not rely on local IDE defaults.

Canonical local verification:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
```

`OpenClaw.Tests` is an executable harness. `dotnet test` is guarded because it otherwise exits successfully without running the harness.

## WinUI 3 Window Chrome And Theme Sync

This note records the debugging lesson from the v3.0.4 top-edge artifact fix.

### Symptom

The main window showed a 1px line at the very top of the custom title bar. On first launch in light mode the line appeared lighter than the title-bar surface. After switching between dark and light themes, the same edge could become black.

### Root Cause

The artifact was not a normal XAML border. It came from mixing three different ownership layers for the same visual edge:

- XAML title-bar surface (`AppTitleBar`)
- WinUI `AppWindow.TitleBar`
- native DWM caption and border attributes

The earlier `TopEdgeCover` workaround made the problem harder to reason about because it painted another 1px layer over the window. Removing that cover alone was not enough, because DWM still owned the real non-client border.

The stable fix was to make every layer use the same concrete color:

- `AppTitleBar.Background`
- `AppWindow.TitleBar.BackgroundColor`
- `AppWindow.TitleBar.InactiveBackgroundColor`
- `DWMWA_CAPTION_COLOR`
- `DWMWA_BORDER_COLOR`

Avoid relying on `Colors.Transparent` for title-bar surfaces that are not caption buttons. Avoid `DWMWA_BORDER_COLOR = COLOR_NONE` when the visual goal is a seamless colored top edge; set an explicit border color instead.

### Debugging Rules

- Treat custom title-bar artifacts as a multi-layer problem first, not as a XAML layout problem.
- Sample pixels from screenshots before changing code. A 1px white, black, or mismatched line usually reveals whether XAML, Mica, or DWM owns the visible edge.
- Do not cover native frame bugs with an extra XAML strip unless the native layer has already been proven impossible to control.
- Theme changes must go through the full native frame refresh path. Updating only managed XAML colors can leave DWM using stale light/dark state.
- Keep the main window and settings window on the same `WindowFrameHelper` contract so fixes do not diverge.

### Implementation Checklist

When changing window chrome or theme behavior:

1. Update the XAML title-bar surface.
2. Update `AppWindow.TitleBar` foreground, background, inactive, hover, and pressed colors.
3. Update DWM immersive dark mode, caption color, text color, and border color.
4. Refresh the non-client frame after theme changes.
5. Verify both startup theme and runtime dark/light switching.

Commands used for baseline verification:

```powershell
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

## System Tray Win32 Integration

This note records the v3.1.0 tray icon and right-click menu debugging path.

### Symptoms

The tray icon initially failed to appear after minimizing to tray. After the icon appeared, right-clicking the status-bar tray icon did not open the context menu and produced no visible error.

### Root Causes

The first failure came from mixed Win32 string marshalling. The service called explicit `*W` entry points such as `RegisterClassExW`, `CreateWindowExW`, `LoadImageW`, and `AppendMenuW`, but the `DllImport` declarations did not all specify `CharSet.Unicode`. That allowed the registered class name and created window class name to diverge, producing `CreateWindowExW` error `1407`.

The right-click failure had two separate causes:

- `NOTIFYICON_VERSION_4` reports the mouse event in `LOWORD(lParam)` and the icon id in the high word. Comparing the whole `lParam` against `WM_CONTEXTMENU` or `WM_RBUTTONUP` ignores the right-click event.
- `TrackPopupMenu` needs an owner window that can participate in foreground activation. A message-only `HWND_MESSAGE` window is useful for receiving messages, but it is not a reliable owner for a visible popup menu. Use a hidden normal top-level window instead.

### Implementation Rules

- Every explicit Win32 `*W` import that accepts strings must declare `CharSet = CharSet.Unicode`.
- Keep the tray callback window alive for the entire tray icon lifetime and destroy it only during `TrayIconService.Dispose()`.
- When using `NOTIFYICON_VERSION_4`, decode the callback event with a low-word helper before dispatching mouse actions.
- Use a hidden normal owner window for `TrackPopupMenu`; do not pass `HWND_MESSAGE` as the menu owner.
- Keep right-click tray commands minimal: Open OpenClaw, Settings, and Exit. Left-click can remain the quick show/hide toggle.

Regression coverage now checks the Unicode imports, `LOWORD(lParam)` callback parsing, the minimal tray command set, and the hidden normal owner-window requirement.

## Single Instance Launch Coordination

v3.1.1 keeps `AllowMultipleInstances` off by default because the common Windows workflow is one remote OpenClaw client parked in the tray. The setting lives under Settings > Advanced as "Multiple instances"; when enabled, launches keep the existing behavior and create another app window.

When multiple instances are disabled, startup creates a named mutex and the primary instance listens on a named pipe for activation requests. A secondary launch loads settings first, detects the mutex owner, sends an activation request to the primary instance, and exits. The primary dispatches that request back to the UI thread and calls `MainWindow.ActivateFromExternalLaunch()`, which restores a tray-hidden window instead of creating another tray icon.

## Window Bounds Persistence

This note records the v3.1.2 fix for a main-window visibility failure after switching the machine to dedicated-GPU direct mode.

### Symptom

The process started normally, WebView2 initialized, and the taskbar/tray entry remained present, but the main window was not visible. Debug output only showed first-chance WinRT and cancellation exceptions; the application exited cleanly when closed.

### Root Cause

The persisted window bounds in `%LOCALAPPDATA%\OpenClaw\settings.json` had been overwritten while the window was minimized or hidden:

```json
{
  "windowWidth": 160,
  "windowHeight": 28,
  "windowLeft": -32000,
  "windowTop": -32000
}
```

Those coordinates are Windows minimized-window sentinel values, not a user-visible placement. After a display topology change such as dedicated-GPU direct mode, restoring them can leave the WinUI window activated but effectively off-screen or collapsed.

### Implementation Rules

- Never persist bounds while the main window is hidden to tray or minimized.
- Treat `-32000`-style coordinates as invalid persisted state and reset them to the default visible bounds.
- Reject very small persisted sizes that match minimized caption-only dimensions.
- Before moving to saved coordinates, verify that the restored rectangle intersects one of the current `DisplayArea` work areas.
- If saved coordinates no longer intersect any display, center the window on the current display instead of trusting stale topology.

Regression coverage now checks both sides of the fix: settings load sanitizes minimized sentinel bounds, and `SaveWindowBounds()` skips hidden/minimized windows.

## Always-On-Top And Pin State

This note records the v3.2.0 always-on-top follow-up after testing on a machine using dedicated-GPU direct mode.

### Symptoms

The WinUI `OverlappedPresenter.IsAlwaysOnTop` state could appear enabled in app code while the native window did not reliably stay above other applications on that display path. After adding the title-bar Pin affordance, the inactive Pin state also became too faint on light title-bar backgrounds when its foreground fell back to the default subtle button styling.

### Implementation Rules

- Apply always-on-top through both `OverlappedPresenter.IsAlwaysOnTop` and a native `SetWindowPos` fallback using `HWND_TOPMOST` / `HWND_NOTOPMOST`.
- Keep the Pin button state theme-aware. Use `AccentTextFillColorPrimaryBrush` for the active pinned state and `TextFillColorSecondaryBrush` for the inactive state instead of clearing the foreground to `null`.
- Update both the `Button.Foreground` and the nested `FontIcon.Foreground`; the icon is the visible state indicator.
- Persist only the user preference in settings. Reapply the native topmost state from that preference when the main window is initialized.
- Cover the integration with regression tests that assert the native fallback path and the theme-aware Pin colors are present.

## Hosted OpenClaw UI Status Bridge

This note records the v3.3.1 follow-up for status-bar model display and WebView2 CPU spikes when heavy OpenClaw Control UI surfaces are open.

### Symptoms

The top status MODEL field could stay empty even though the hosted OpenClaw chat page had a selected model. After the model value was detected, the old top status pill still truncated long provider/model labels too aggressively before the AUTH indicator.

Opening heavy hosted UI areas such as Communications and Automation/Cron also caused WebView2 CPU spikes. The right-sidebar fix helped sidebar content, but settings/config/Cron pages could still trigger repeated native status probes while Lit rerendered large DOM regions.

### Root Cause

MODEL was not just a XAML binding issue. During startup and session switches, the visible DOM controls can be absent or not yet ready while the OpenClaw Lit root already has the real session state. The bridge must read `openclaw-app` state first, including `sessionKey`, `chatModelOverrides`, `sessionsResult.defaults`, `sessionsResult.sessions`, and `chatModelCatalog`.

The CPU issue came from treating most DOM mutations as status-relevant. Communications, settings/config, and Cron pages render many controls, `details` sections, status chips, markdown/JSON blocks, and run/job lists. Those mutations do not change the native shell status, but the bridge was still scheduling page-level inspection from them.

### Implementation Rules

- Prefer OpenClaw app state for connected-page status before scanning DOM text.
- Read model state from the OpenClaw Lit root before falling back to visible selectors.
- Preserve the last non-empty native model summary across transient connected snapshots.
- Exclude status-irrelevant heavy regions from status mutation probes: sidebar, hosted preview frames, settings workspace body, config content/forms, and Cron workspace/summary.
- Do not observe high-volume `class` attribute churn from Lit rerenders for native status updates.
- Use explicit low-cost events, such as `change`, for user selection changes that can affect status.
- Keep the status pill wide enough for common provider/model labels, but continue using ellipsis for extreme names or narrow windows.

### Remaining Caveat

The v3.3.1 behavior is a usable mitigation, not proof that every future OpenClaw Control UI page will remain cheap. If upstream class names or page structure changes, re-check the excluded selectors against the current OpenClaw `app-render`, `config`, `channels`, and `cron` views before tuning WebView2 or WinUI code.

## Hosted Chat Stale-Stream Recovery

This note records the v3.3.2 follow-up for hosted chat sessions that keep showing a busy output state even though a manual reload reveals the completed Gateway result.

### Symptom

After submitting a task in the hosted Control UI, output can appear stuck until the user clicks Reload. The result then appears immediately after refresh, which indicates the remote Gateway run often completed and persisted state, but the current WebView session missed or stopped applying chat events.

### Root Cause

The Manager shell only owned the hosted WebView session, not the upstream Gateway WebSocket event stream. It could detect page-level connected/auth/error states, and it had an optional `reportSeq` gap path, but it did not have a fallback signal for the common half-broken case where the page still reports `connected` and `busy` while chat activity stops advancing.

### Implementation Rules

- Treat a connected busy chat session as suspicious when its app-state or visible activity signature does not change for the stale threshold.
- Prefer soft recovery first: lightweight sync and recent-message fetch before full reload.
- Escalate stale busy recovery to hard refresh once the soft-resync budget is exhausted; do not treat a stale connected snapshot as a successful reload fallback.
- Keep reload protection for focused inputs only when the focused editor contains unsent text. An empty focused editor should not block recovery.
- Include phase, busy, stale duration, and focused-input text state in diagnostics so tunnel/proxy and app-state failures can be separated later.

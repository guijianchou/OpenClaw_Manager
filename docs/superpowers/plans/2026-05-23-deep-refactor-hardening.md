# Deep Refactor Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the v3.3.x architecture cleanup real instead of patch-stacked: stable WebView/bridge ownership, live settings application, predictable compact layout, explicit verification without the removed `tests/` harness, and documentation that matches the active codebase and development notes.

**Architecture:** Keep `src/OpenClaw.Core` as the pure .NET shared source tree and keep the active solution free of `tests/` for now. Move volatile behavior behind small services and contracts inside the existing app/Core split: WinUI owns windows/WebView2/tray/hotkey, Core owns policy/parsing/state decisions, and script behavior is verified by lightweight repo scripts instead of an active C# test project. This file is the authoritative combined plan; `2026-05-23-deep-refactor-hardening_opus.md` remains a review input.

**Tech Stack:** WinUI 3, C#/.NET 10, WebView2, embedded JavaScript assets, PowerShell verification scripts, `dotnet restore/build/format`, manual VS2026 debug checklist.

---

## Review Findings Driving This Plan

### Review Scope

This pass reviewed:

- `README.md` and `readme_zh.md`: current 3.3.6 notes, architecture diagram, feature table, Cloudflare Tunnel/VPS guidance, development workflow.
- `changelog.md`: v3.0.0 through v3.3.6 English and Chinese entries, especially WebView, recovery, MODEL, compact mode, hotkey, diagnostics, and test harness changes.
- `DEVELOPMENT_NOTES.md`: root-cause notes for title bar/DWM sync, tray Win32 integration, window bounds, always-on-top, hosted MODEL/status bridge, WebView CPU spikes, stale chat stream recovery, and active verification.
- `docs/code-style.md`: current architecture boundaries, partial ownership, Core rules, large-file rules, and no-active-harness test policy.
- `src/OpenClaw` and `src/OpenClaw.Core`: current ownership of WebView2 lifecycle, hosted bridge assets, settings save/apply flow, compact layout, diagnostics, logging, tray/hotkey, window bounds, and Core boundary.

1. `README.md` still describes a very small architecture (`MainWindow -> MainViewModel -> WebViewService`), but the real runtime now includes `HostedUiBridge`, `ShellSessionCoordinator`, heartbeat/recovery, app/Core adapters, and embedded script assets. The diagram is no longer useful for maintenance.
2. `DEVELOPMENT_NOTES.md` records real root causes for MODEL blank output, stale busy streams, WebView CPU spikes, always-on-top, compact mode, and settings live application, but those lessons are not consistently enforced by code boundaries or verification.
3. `WebViewService` was split into partial files, but it still owns lifecycle, navigation, status parsing, status cache/generation, heartbeat, retry, profile cleanup, command injection, and event handling. Partial files improved readability but did not fully separate responsibilities.
4. `HostedUiBridge.Script.js` is still a 900+ line browser asset. `HostedUiBridge.ModelResolver.js` was extracted, but session-ready, command dispatch, mutation filtering, status inspection, stale busy detection, and host-message safety are still mixed in one script.
5. `SettingsViewModel` still reads and writes `App.Configuration` directly, while `MainWindow` applies live shell settings by reacting to `SettingsSaveResult`. This fixes the current hotkey/always-on-top symptom, but persistence and runtime application remain split across UI classes.
6. `MainWindow.CompactMode.cs` manually mutates XAML element properties (`TopStatusPill.MinWidth`, margins, visibility) instead of driving a compact visual state. It works as a patch, but the XAML and code-behind now duplicate layout constants.
7. `LogViewerDialog` loads log tails off the UI thread, but cancellation and concurrent refresh ownership are not explicit. Fast repeated refresh/open/close can still race UI updates.
8. `OpenClaw.Core` is mostly WinUI-free. The only suspicious boundary is `DiagnosticBundleService` using reflection with a `Microsoft.Web.WebView2.Core` type name. It is not a direct reference, but it is platform knowledge inside Core.
9. Active regression tests were removed from the solution and repository. Current verification is restore/build/format/diff-check plus manual VS2026 debug. That is acceptable for the current decision, but deep bridge/MODEL refactors need a replacement verification path that does not resurrect `tests/`.
10. Historical plan/progress docs still mention the removed `tests/OpenClaw.Tests` harness. They can remain as history, but current README/changelog/development notes must clearly distinguish historical coverage from active verification.
11. `WebViewService.Commands.cs` still embeds large stop/abort JavaScript strings. These are browser assets and should be embedded resources like the hosted bridge assets, not long C# literals.
12. `MainWindow.WebView.cs` still owns WebView recreation scheduling, circuit-breaker coordination, and instrumentation. The Window partial should keep XAML/control swap work, while scheduling policy belongs in a service.
13. `MainViewModel.Status.cs` still reaches `App.MainWindow?.DispatcherQueue` directly. That couples presentation updates to the global Window singleton and can silently drop updates during startup, shutdown, or future window recreation.
14. `ShellSessionCoordinator.Adapters.cs` still reads `App.Configuration.Settings.RecoveryPolicy` and `App.Configuration.Settings.Heartbeat` from inside the adapter extension. Coordinator configuration should be passed explicitly by the caller.
15. `WebViewService` and its partials still use `App.Logger` directly in many places. Logger ownership should be constructor-injected so service code is easier to reason about and does not spread static App dependencies.
16. `WebViewService.Heartbeat.cs` still reads heartbeat/recovery configuration from `App.Configuration` during runtime. A heartbeat run should capture its effective settings at start time.
17. `HostedUiBridge.Script.js` uses recursive `setTimeout` polling without drift correction. Sustained busy sessions can gradually skew poll timing.
18. `MainViewModel` still owns presentation formatting, indicator state, static brushes, and service exposure across many partials. This is workable but continues the patch-stacked pattern that made MODEL/status regressions hard to review.
19. Status brushes are static `Brush` instances in the ViewModel rather than theme-aware resources. Runtime theme switching can bypass those static instances.
20. `MainViewModel.Core.Properties.cs` exposes service instances publicly (`WebViewService`, `HostedUiBridge`, `Coordinator`), which makes it easy for the view layer to bypass orchestration.
21. `ShowCircuitBreakerError()` still has a hardcoded English user-facing string. User-facing strings belong in `StringResources` and `.resw`.
22. `HostedUiBridge.cs` still logs through `App.Logger`. After script extraction, the bridge should follow the same constructor-injected logger pattern as `WebViewService` so service code does not keep spreading static `App` dependencies.

### Current Branch Snapshot

This plan was rechecked on 2026-05-23 against the active refactor branch:

```text
branch: codex/deep-refactor-hardening
baseline checkpoint: 443e9a5 chore: establish deep refactor baseline
dirty files: docs/superpowers/plans/2026-05-23-deep-refactor-hardening.md, tools/verify-bridge-scripts.ps1, tools/verify-repo-structure.ps1
```

The repository already has the approved `tests/` removal and `src/OpenClaw.Core` retention committed. Task 1 is partially present in the working tree as new verification scripts, and this plan file has been amended with the second-pass review. It still needs documentation alignment, verification, and a checkpoint commit before deeper refactor work starts.

Quick review measurements from the active branch:

| Surface | Current shape | Refactor implication |
| --- | --- | --- |
| `WebViewService*.cs` | 1,609 lines across lifecycle, inspection, heartbeat, commands, profile helpers | Partial split is not enough; Tasks 2, 3, 3A, 3B, and 3C must move ownership into services/assets. |
| `HostedUiBridge.Script.js` | 921 lines | Task 6 must turn it into a composition shell with focused browser assets. |
| `HostedUiBridge.ModelResolver.js` | 191 lines | Keep as a focused asset and extend `tools/verify-bridge-scripts.ps1` around it instead of restoring `tests/`. |
| `MainViewModel*.cs` | 17 partial files, about 1,259 lines in this branch | Tasks 7B, 7C1, and 7C2 should reduce UI dispatch, presentation formatting, and public service exposure. |
| README architecture diagram | still shows only `MainWindow -> MainViewModel -> WebViewService` | Task 9 must update README/readme_zh to the real runtime graph. |
| Development notes | historical lines still say "Regression coverage now checks" | Task 9 must mark those lines as historical after harness removal. |

## Traceability Matrix

| Source commitment or lesson | Current code reality | Plan coverage |
| --- | --- | --- |
| README v3.3.6 says bridge/WebView hardening and generation-scoped inspection are the baseline. | `WebViewService` has generation checks, but status inspection state is still inside the service partial and can grow again. | Task 2 extracts `WebViewStatusInspector` and adds repo guardrails for ownership. |
| README v3.3.6 says local regression harness was removed. | `tests/` is absent and the solution no longer references `OpenClaw.Tests`, but deep JS/bridge behavior has no replacement command. | Task 1 creates active no-`tests/` verification scripts; Task 6 expands bridge script checks. |
| README and changelog call out hosted MODEL app-state resolution. | `HostedUiBridge.ModelResolver.js` is extracted, but script-level coverage is gone with `tests/`. | Task 1 adds MODEL resolver script cases; Task 6 extends bridge script behavior checks. |
| Development notes say MODEL blank output comes from app-state/DOM timing, not XAML only. | MODEL logic exists in JS, but session-ready/status inspection/mutation filters are mixed in `HostedUiBridge.Script.js`. | Task 6 splits browser bridge assets by MODEL, status inspection, command dispatch, mutation filtering, and host messaging. |
| Development notes say stale busy output should soft-resync before hard refresh and should not be blocked by an empty focused editor. | Recovery policy exists across `HostedUiBridge.Script.js`, `WebViewService`, and `ShellSessionCoordinator`; ownership is difficult to audit. | Tasks 2, 3, and 6 isolate inspection, heartbeat loop ownership, and bridge command dispatch before further recovery tuning. |
| Development notes say WebView/CoreWebView2 async work must carry generation ownership after awaits. | Some inspection paths carry generation, but this is a convention inside `WebViewService.ControlUiInspection.cs`, not a dedicated owner. | Task 2 makes generation ownership a dependency of `WebViewStatusInspector`. |
| Changelog v3.3.4 says heartbeat loop ownership is explicit. | `_heartbeatCts`, `_heartbeatTask`, counters, timer, and recovery scheduling still live in `WebViewService.Heartbeat.cs`. | Task 3 extracts `HeartbeatRuntime` and adds guardrails against regressing to inline loop ownership. |
| Changelog v3.3.4 says settings hotkey/always-on-top apply immediately. | Current behavior works through `SettingsSaveResult`, but `SettingsViewModel` depends on `OpenClaw.Views` and runtime apply rereads global config. | Task 4 moves save-result/change records to Core models and introduces an explicit live-shell apply pipeline. |
| Changelog v3.3.4 says compact top bar was tightened for 480px. | `MainWindow.CompactMode.cs` patches XAML element widths/margins directly and duplicates resource values. | Task 5 moves compact top-bar layout into XAML `VisualStateManager` on `RootLayout`. |
| Development notes say log viewer must not block the UI thread. | Log tailing runs via `Task.Run`, but refresh/close cancellation is not owned. | Task 8 adds cancellation and refresh ownership. |
| Development notes define Core as WinUI/WebView2-free. | Core has no direct reference, but `DiagnosticBundleService` uses WebView2 type-name reflection. | Task 7 moves WebView2 runtime discovery to WinUI and passes plain runtime info into Core. |
| Development notes cover title-bar/DWM, tray Win32, single-instance, and window bounds lessons. | These areas are not the main refactor target but are regression-prone during shell changes. | Task 1 structure guardrails and Task 10 manual VS2026 checklist keep these behaviors in scope. |
| Code-style notes say large browser scripts should live as assets and remain verifiable. | Stop/abort WebView command scripts are still large inline strings in C#. | Task 3B moves command scripts into embedded resources and adds inline-JS guardrails. |
| Development notes say WebView recovery should be deliberate and observable. | WebView recreation scheduling still lives in `MainWindow.WebView.cs`. | Task 3C extracts `WebViewRecreationService` so the Window partial stays thin. |
| Code-style notes say static App coupling should not spread into services/ViewModels. | `MainViewModel.Status.cs`, `ShellSessionCoordinator.Adapters.cs`, `WebViewService`, heartbeat code, and `HostedUiBridge` still read global App state. | Tasks 3A, 7A, 7B, and 7D remove the main static-coupling hot spots. |
| README/changelog emphasize long-running hosted sessions and stale busy recovery. | Bridge polling uses recursive timeout scheduling without drift correction. | Task 6A adds self-correcting bridge polling. |
| Code-style notes call for centralized resources and localizable user strings. | Status brushes are static ViewModel objects, and circuit-breaker text is hardcoded English. | Tasks 7C1 and 7C3 move visual/text presentation to resources. |
| The goal of this refactor is to stop patch stacking. | `MainViewModel` and public service properties still expose broad mutable runtime surface. | Tasks 7B, 7C1, and 7C2 reduce dispatch, presentation, and service-surface sprawl. |
| Static `App` dependencies should stay at application edges. | `HostedUiBridge.cs` still logs through `App.Logger` even though it is a service with a clear lifetime. | Task 7D injects `IAppLogger` into `HostedUiBridge` and adds a guardrail. |

## Current Ground Rules

- Keep `src/OpenClaw.Core`.
- Do not restore the removed `tests/` directory in this refactor.
- Keep Release output folders unless a later explicit cleanup request says otherwise.
- Do not rewrite historical `docs/superpowers/plans/*` and `docs/superpowers/progress/*` records except to add a short archival note if needed.
- Every task ends with a commit-sized checkpoint and verification command list.
- Start implementation from the current `codex/deep-refactor-hardening` checkpoint. The baseline cleanup is already committed; only the Task 1 verification scripts should be carried into the next checkpoint.
- Do not bump the app version inside this plan unless the user explicitly asks for the implementation branch to become a release.
- New service types default to `internal sealed` unless a public contract is required.
- New async methods accept `CancellationToken` when there is a realistic owner that can cancel the work.
- Inline JavaScript longer than 30 lines must move to an embedded `.js` resource with a verifier or guardrail.
- Do not add new direct reads of `App.Configuration`, `App.Logger`, or `App.MainWindow` inside service/view-model internals unless the task explicitly preserves an existing boundary temporarily.
- User-visible English/Chinese text must go through `StringResources` and `.resw`; diagnostic protocol tokens and log event names may stay literal.

---

## Target Runtime Shape

```text
MainWindow (WinUI shell: XAML, WebView2 control swap, tray/window integration)
|- MainViewModel (orchestration and bindable state)
|  |- StatusPresenter
|  |- WebViewService
|  |  |- WebViewStatusInspector
|  |  |- HeartbeatRuntime
|  |  |- WebViewGenerationTracker
|  |  `- WebView command JS assets
|  |- HostedUiBridge
|  |  `- embedded bridge JS modules
|  |- WebViewRecreationService
|  |- LiveShellSettingsApplier
|  |- ShellSessionCoordinator adapters
|  `- ControlUiLatencyService
`- OpenClaw.Core
   |- settings/configuration models
   |- recovery policy/state machine
   |- diagnostics/log utilities
   `- parser/protocol helpers
```

## Target File Budgets

These are review targets, not hard build failures unless the guardrail script explicitly enforces them.

| File | Current issue | Target |
| --- | --- | --- |
| `WebViewService.cs` | lifecycle, navigation, generation, logging, event wiring | around 350 lines after delegation |
| `WebViewService.ControlUiInspection.cs` | owns inspection internals | thin wrapper only |
| `WebViewService.Heartbeat.cs` | owns loop lifetime and policy | probe policy only; lifetime in `HeartbeatRuntime` |
| `WebViewService.Commands.cs` | inline browser scripts | command orchestration plus asset loading only |
| `HostedUiBridge.Script.js` | multi-concern browser asset | composition shell under 250 lines |
| `MainWindow.WebView.cs` | scheduling plus control swap | WebView2 control swap and timer wiring only |
| `MainWindow.CompactMode.cs` | patches XAML properties | state transition call only |
| `MainViewModel` partials | formatting, brushes, dispatch, services mixed | presentation helpers and narrower service surface |

### Task 0: Preflight And Checkpoint

**Files:**
- Inspect only unless committing the pending Task 1 verification scripts.

- [x] **Step 1: Confirm working tree state**

Run:

```powershell
git status --short --branch -uall
```

Expected current state before continuing implementation is the dedicated refactor branch with this amended plan plus Task 1 verification scripts uncommitted:

```text
## codex/deep-refactor-hardening
 M docs/superpowers/plans/2026-05-23-deep-refactor-hardening.md
?? tools/verify-bridge-scripts.ps1
?? tools/verify-repo-structure.ps1
```

- [x] **Step 2: Decide checkpoint handling**

The baseline cleanup is already committed as:

```text
443e9a5 chore: establish deep refactor baseline
```

Do not recommit the removed harness. Finish Task 1 by updating docs, running verification, and committing only the verification scripts plus related current documentation.

- [x] **Step 3: Confirm Release output policy**

Run:

```powershell
Get-ChildItem -Path src -Directory -Recurse -Force -Filter Release | Select-Object -ExpandProperty FullName
```

Expected: Release directories may exist and must not be deleted by this plan. Debug directories produced by verification can be deleted after each task.

---

### Task 1: Add A No-Tests Verification Surface

**Files:**
- Create: `tools/verify-bridge-scripts.ps1`
- Create: `tools/verify-repo-structure.ps1`
- Modify: `README.md`
- Modify: `readme_zh.md`
- Modify: `DEVELOPMENT_NOTES.md`
- Modify: `docs/code-style.md`

- [x] **Step 1: Create `tools/verify-bridge-scripts.ps1`**

Add a PowerShell script that executes the embedded MODEL resolver with Node.js when available. It must not create `tests/`.

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$resolverPath = Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.ModelResolver.js'

function Resolve-NodeCommand {
    if (-not [string]::IsNullOrWhiteSpace($env:OPENCLAW_NODE)) {
        return $env:OPENCLAW_NODE
    }

    $command = Get-Command node -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

$nodeCommand = Resolve-NodeCommand
if (-not $nodeCommand) {
    Write-Host 'SKIP: node is not available; bridge model resolver script verification skipped.'
    exit 0
}

try {
    & $nodeCommand --version | Out-Null
} catch {
    Write-Host "SKIP: node is not executable; bridge model resolver script verification skipped. $($_.Exception.Message)"
    exit 0
}

$resolver = Get-Content -LiteralPath $resolverPath -Raw
$cases = @'
[
  {
    "name": "default model",
    "states": [{"sessionsResult":{"defaults":{"model":"gpt-5.5","modelProvider":"openai"},"sessions":[]}}],
    "sessionKey": "s1",
    "expected": "openai/gpt-5.5"
  },
  {
    "name": "null override falls back after session lookup",
    "states": [{"sessionKey":"s1","chatModelOverrides":{"s1":null},"sessionsResult":{"defaults":{"model":"gpt-5.4","modelProvider":"openai"},"sessions":[{"key":"s1","model":"claude-sonnet-4.5","modelProvider":"anthropic"}]}}],
    "sessionKey": "s1",
    "expected": "anthropic/claude-sonnet-4.5"
  },
  {
    "name": "object override",
    "states": [{"chatModelOverrides":{"s1":{"model":{"id":"qwen3-coder"},"provider":{"id":"dashscope"}}},"sessionsResult":{"defaults":{"model":"gpt-5.4","modelProvider":"openai"},"sessions":[]}}],
    "sessionKey": "s1",
    "expected": "dashscope/qwen3-coder"
  }
]
'@

$runner = @"
$resolver
const cases = $cases;
let failed = 0;
for (const item of cases) {
  const result = resolveOpenClawAppStateModel(item.states, item.sessionKey);
  if (!result || result.value !== item.expected) {
    console.error(`FAIL: ${item.name}: expected ${item.expected}, got ${result && result.value}`);
    failed += 1;
  } else {
    console.log(`PASS: ${item.name}`);
  }
}
process.exit(failed === 0 ? 0 : 1);
"@

$tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ('openclaw-model-resolver-' + [System.Guid]::NewGuid() + '.js')
try {
    Set-Content -LiteralPath $tempFile -Value $runner -Encoding UTF8
    & $nodeCommand $tempFile
} finally {
    Remove-Item -LiteralPath $tempFile -ErrorAction SilentlyContinue
}
```

- [x] **Step 2: Create `tools/verify-repo-structure.ps1`**

Add guardrails for the current architecture decision: no active `tests/`, Core stays WinUI-free, embedded script resources exist, and historical docs are not treated as active verification.

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

$testsPath = Join-Path $repoRoot 'tests'
if (Test-Path -LiteralPath $testsPath) {
    throw 'Active tests/ directory exists, but current checkpoint intentionally keeps tests out of the solution.'
}

$solution = Get-Content -LiteralPath (Join-Path $repoRoot 'OpenClaw.sln') -Raw
if ($solution -match 'OpenClaw\.Tests|tests\\OpenClaw\.Tests') {
    throw 'OpenClaw.sln still references the removed test harness.'
}

$coreFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/OpenClaw.Core') -Recurse -File -Include *.cs
$forbiddenCorePattern = 'using Microsoft\.UI|using Microsoft\.Web\.WebView2|using Windows\.Graphics|using Windows\.UI|using WinRT|App\.Configuration|App\.Logger|App\.MainWindow'
foreach ($file in $coreFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match $forbiddenCorePattern) {
        throw "Core boundary violation: $($file.FullName)"
    }
}

$project = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/OpenClaw.csproj') -Raw
foreach ($resource in @('HostedUiBridge.Script.js', 'HostedUiBridge.ModelResolver.js')) {
    if ($project -notmatch [regex]::Escape($resource)) {
        throw "Missing embedded bridge resource entry: $resource"
    }
}

Write-Host 'PASS: repository structure guardrails'
```

- [x] **Step 3: Document the new verification commands**

Update current docs to include:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
powershell -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
git diff --check
```

In `DEVELOPMENT_NOTES.md`, replace current active wording that implies executable regression coverage with a clear statement:

```markdown
Historical notes may mention regression coverage from removed harness versions. The active v3.3.6+ verification surface is solution restore/build/format, repo-structure guardrails, bridge MODEL script checks, whitespace checks, and VS2026 manual debug.
```

- [x] **Step 4: Run verification**

Run:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
powershell -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
git diff --check
```

Expected:

```text
Build succeeded.
PASS: repository structure guardrails
PASS: default model
PASS: null override falls back after session lookup
PASS: object override
```

- [x] **Step 5: Commit**

```powershell
git add README.md readme_zh.md DEVELOPMENT_NOTES.md docs/code-style.md tools/verify-bridge-scripts.ps1 tools/verify-repo-structure.ps1
git commit -m "chore: add active refactor verification scripts"
```

---

### Task 2: Split WebView Runtime Ownership From Status Inspection

**Files:**
- Create: `src/OpenClaw/Services/WebViewGenerationTracker.cs`
- Create: `src/OpenClaw/Services/WebViewStatusInspector.cs`
- Modify: `src/OpenClaw/Services/WebViewService.cs`
- Modify: `src/OpenClaw/Services/WebViewService.ControlUiInspection.cs`
- Modify: `src/OpenClaw/Services/WebViewService.Heartbeat.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Fields.cs`
- Modify: `src/OpenClaw/MainWindow.Shared.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Create `WebViewGenerationTracker`**

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed class WebViewGenerationTracker
{
    private int _generation;

    public int Current => Volatile.Read(ref _generation);

    public int Next()
    {
        return Interlocked.Increment(ref _generation);
    }

    public bool IsCurrent(int generation)
    {
        return Current == generation;
    }
}
```

- [x] **Step 2: Create `WebViewStatusInspector`**

Move these fields and methods out of the `WebViewService` partial into the new class:

- `_inspectionGate`
- `_statusProbeCts`
- `_inFlightInspectionTask`
- `_inFlightInspectionGeneration`
- `_latestControlUiSnapshot`
- `_latestControlUiSnapshotGeneration`
- `_lastControlUiInspectionAt`
- inspection counters
- `InspectControlUiStateAsync`
- `StartStatusProbeLoop`
- `CancelStatusProbeLoop`
- `ApplyControlUiSnapshot`
- `ExecuteControlUiInspectionAsync`
- `ParseControlUiSnapshot`

Keep the public contract narrow:

```csharp
internal sealed class WebViewStatusInspector : IDisposable
{
    public WebViewStatusInspector(
        Func<CoreWebView2?> getCoreWebView,
        WebViewGenerationTracker generations,
        IAppLogger logger);

    public event Action<ControlUiProbeSnapshot>? SnapshotUpdated;

    public ControlUiProbeSnapshot LatestSnapshot { get; }
    public int TotalRequests { get; }
    public int CachedRequests { get; }
    public int CoalescedRequests { get; }

    public Task<ControlUiProbeSnapshot> InspectAsync(CancellationToken cancellationToken = default);
    public void StartProbeLoop();
    public void CancelProbeLoop();
    public void InvalidateCache();
    public void SetLoadingSnapshot(string? uri);
    public void SetPageLoadedSnapshot(string? uri);
    public void SetUnavailableSnapshot(string summary);
    public bool TryApplyHostMessage(string json);
    public void Dispose();
}
```

- [x] **Step 3: Add explicit `WebViewService` logger/generation wiring**

Add constructor-owned dependencies to `WebViewService`. Do not use a nullable logger fallback inside the service.

```csharp
private readonly IAppLogger _logger;
private readonly WebViewGenerationTracker _generations;
private readonly WebViewStatusInspector _statusInspector;

public WebViewService(IAppLogger logger)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _generations = new WebViewGenerationTracker();
    _statusInspector = new WebViewStatusInspector(GetCoreWebView, _generations, _logger);
    _statusInspector.SnapshotUpdated += snapshot => ControlUiSnapshotUpdated?.Invoke(snapshot);
}
```

Move `_webViewService` construction out of the field initializer:

```csharp
private readonly WebViewService _webViewService;
```

Update `MainViewModel` constructor:

```csharp
public MainViewModel(IAppLogger logger)
{
    _webViewService = new WebViewService(logger);
    InitializeCommands();
    SubscribeToServiceEvents();
    InitializeCoordinator();
    LoadEnvironments();
    UpdateStatusPresentation();
}
```

Update `MainWindow.Shared.cs`:

```csharp
public MainViewModel ViewModel { get; } = new(App.Logger);
```

This keeps the App-bound logger at the application edge while removing it from `WebViewService`.

- [x] **Step 4: Keep generation checks inside inspector**

Every await in `WebViewStatusInspector` that can apply a snapshot must check:

```csharp
cancellationToken.ThrowIfCancellationRequested();
if (!_generations.IsCurrent(generation))
{
    return ControlUiProbeSnapshot.Unknown;
}
```

No caller should be able to apply an inspection result without both a generation and the currently owned cancellation token. Fire-and-forget probe loops must pass their probe-loop token into `InspectAsync`.

- [x] **Step 5: Reduce `WebViewService.ControlUiInspection.cs` to a compatibility wrapper**

After the move, this partial should only expose the existing public members and delegate to `_statusInspector`:

```csharp
public ControlUiProbeSnapshot LatestControlUiSnapshot => _statusInspector.LatestSnapshot;
public int TotalControlUiInspectionRequests => _statusInspector.TotalRequests;
public int CachedControlUiInspectionRequests => _statusInspector.CachedRequests;
public int CoalescedControlUiInspectionRequests => _statusInspector.CoalescedRequests;

public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync(CancellationToken cancellationToken = default)
{
    return _statusInspector.InspectAsync(cancellationToken);
}
```

- [x] **Step 6: Update `WebViewService` lifecycle calls**

Replace direct field manipulation with explicit inspector calls:

```csharp
_statusInspector.CancelProbeLoop();
_statusInspector.InvalidateCache();
_generations.Next();
_statusInspector.SetLoadingSnapshot(args.Uri);
_statusInspector.SetPageLoadedSnapshot(sender.Source);
_statusInspector.StartProbeLoop();
```

- [x] **Step 7: Update heartbeat hosted-session probe**

In `WebViewService.Heartbeat.cs`, replace direct inspection call with the wrapper:

```csharp
var snapshot = await InspectControlUiStateAsync();
```

Keep this line only if it now delegates to `WebViewStatusInspector`. Do not let heartbeat own inspection state.

- [x] **Step 8: Add structure guardrails**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$webViewService = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.cs') -Raw
if ($webViewService -match 'ParseControlUiSnapshot|ExecuteControlUiInspectionAsync|_latestControlUiSnapshot') {
    throw 'WebViewService.cs must not own Control UI inspection internals.'
}
```

- [x] **Step 9: Run verification and commit**

Run the full command set from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/WebViewGenerationTracker.cs src/OpenClaw/Services/WebViewStatusInspector.cs src/OpenClaw/Services/WebViewService.cs src/OpenClaw/Services/WebViewService.ControlUiInspection.cs src/OpenClaw/Services/WebViewService.Heartbeat.cs src/OpenClaw/ViewModels/MainViewModel.cs src/OpenClaw/ViewModels/MainViewModel.Fields.cs src/OpenClaw/MainWindow.Shared.cs tools/verify-repo-structure.ps1
git commit -m "refactor: isolate WebView status inspection ownership"
```

---

### Task 3: Split Heartbeat Runtime Into An Owned Loop

**Files:**
- Create: `src/OpenClaw/Services/HeartbeatRuntime.cs`
- Modify: `src/OpenClaw/Services/WebViewService.Heartbeat.cs`
- Modify: `src/OpenClaw/Services/WebViewService.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Create `HeartbeatRuntime`**

Move timer/task/cancellation ownership out of `WebViewService.Heartbeat.cs`.

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed class HeartbeatRuntime : IDisposable
{
    private readonly IAppLogger _logger;
    private CancellationTokenSource? _cancellation;
    private Task? _task;
    private string? _key;

    public HeartbeatRuntime(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsRunning => _task is { IsCompleted: false };

    public bool IsSameRun(string key)
    {
        return IsRunning && string.Equals(_key, key, StringComparison.Ordinal);
    }

    public void Start(string key, Func<CancellationToken, Task> loop)
    {
        Stop();
        _key = key;
        _cancellation = new CancellationTokenSource();
        _task = RunObservedAsync(key, loop, _cancellation);
    }

    public void Stop()
    {
        var cancellation = _cancellation;
        var task = _task;
        _cancellation = null;
        _task = null;
        _key = null;

        cancellation?.Cancel();
        if (task is null)
        {
            cancellation?.Dispose();
            return;
        }

        _ = ObserveStopAsync(task, cancellation);
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task RunObservedAsync(
        string key,
        Func<CancellationToken, Task> loop,
        CancellationTokenSource cancellation)
    {
        try
        {
            await loop(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Expected during Stop().
        }
        catch (Exception ex)
        {
            _logger.Error($"Heartbeat loop error for run '{key}': {ex.Message}");
        }
    }

    private async Task ObserveStopAsync(Task task, CancellationTokenSource? cancellation)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during Stop().
        }
        catch (Exception ex)
        {
            _logger.Error($"Heartbeat loop shutdown error: {ex.Message}");
        }
        finally
        {
            cancellation?.Dispose();
        }
    }
}
```

- [x] **Step 2: Remove direct `_heartbeatCts` and `_heartbeatTask` fields**

In `WebViewService.Heartbeat.cs`, replace:

```csharp
private CancellationTokenSource? _heartbeatCts;
private Task? _heartbeatTask;
```

with:

```csharp
private readonly HeartbeatRuntime _heartbeatRuntime;
```

Initialize it in the `WebViewService(IAppLogger logger)` constructor created in Task 2:

```csharp
public WebViewService(IAppLogger logger)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _generations = new WebViewGenerationTracker();
    _statusInspector = new WebViewStatusInspector(GetCoreWebView, _generations, _logger);
    _heartbeatRuntime = new HeartbeatRuntime(_logger);
    _statusInspector.SnapshotUpdated += snapshot => ControlUiSnapshotUpdated?.Invoke(snapshot);
}
```

- [x] **Step 3: Change start/stop logic**

`StartHeartbeat` should compute a complete key including URL, interval, thresholds, and enabled state:

```csharp
var heartbeatKey = $"{gatewayUrl}|{intervalSeconds}|{_heartbeatFailureThreshold}|{_heartbeatConnectingThreshold}|enabled";
if (_heartbeatRuntime.IsSameRun(heartbeatKey))
{
    return;
}

_heartbeatRuntime.Start(heartbeatKey, token => RunSessionAwareHeartbeatLoopAsync(gatewayUrl, TimeSpan.FromSeconds(intervalSeconds), token));
```

Change `RunSessionAwareHeartbeatLoopAsync` signature:

```csharp
private async Task RunSessionAwareHeartbeatLoopAsync(string gatewayUrl, TimeSpan interval, CancellationToken token)
```

Create and dispose `PeriodicTimer` inside that method:

```csharp
using var timer = new PeriodicTimer(interval);
while (await timer.WaitForNextTickAsync(token))
{
    // Move the existing per-tick heartbeat body here unchanged:
    // ProbeGatewayHealthAsync, LogHeartbeatObservation, status-specific counter updates,
    // TryScheduleHeartbeatReload, and the existing OperationCanceledException handling.
}
```

- [x] **Step 4: Simplify `StopHeartbeat`**

```csharp
public void StopHeartbeat()
{
    _heartbeatRuntime.Stop();
    _heartbeatFailureCount = 0;
    _heartbeatConnectingCount = 0;
    _lastHeartbeatObservationKey = null;
    _heartbeatGatewayUrl = null;
    _heartbeatIntervalSeconds = 0;
    _heartbeatFailureThreshold = DefaultHeartbeatFailureThreshold;
    _heartbeatConnectingThreshold = DefaultHeartbeatConnectingThreshold;
    _lastStartHeartbeatKey = null;
}
```

- [x] **Step 5: Add structure guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$heartbeat = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Heartbeat.cs') -Raw
if ($heartbeat -match 'CancellationTokenSource\? _heartbeatCts|Task\? _heartbeatTask|ObserveHeartbeatShutdownAsync') {
    throw 'Heartbeat loop ownership must live in HeartbeatRuntime.'
}
```

- [x] **Step 6: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/HeartbeatRuntime.cs src/OpenClaw/Services/WebViewService.Heartbeat.cs src/OpenClaw/Services/WebViewService.cs tools/verify-repo-structure.ps1
git commit -m "refactor: isolate heartbeat loop ownership"
```

---

### Task 3A: Replace Static WebView Logging And Capture Heartbeat Configuration

**Files:**
- Modify: `src/OpenClaw/Services/WebViewService.cs`
- Modify: `src/OpenClaw/Services/WebViewService.ControlUiInspection.cs`
- Modify: `src/OpenClaw/Services/WebViewService.Commands.cs`
- Modify: `src/OpenClaw/Services/WebViewService.Heartbeat.cs`
- Modify: `src/OpenClaw/Services/WebViewService.Profile.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [x] **Step 1: Replace static logger reads inside `WebViewService` partials**

In these files, replace every `App.Logger.Info`, `App.Logger.Warning`, and `App.Logger.Error` call with `_logger.Info`, `_logger.Warning`, and `_logger.Error`:

```text
src/OpenClaw/Services/WebViewService.cs
src/OpenClaw/Services/WebViewService.ControlUiInspection.cs
src/OpenClaw/Services/WebViewService.Commands.cs
src/OpenClaw/Services/WebViewService.Heartbeat.cs
src/OpenClaw/Services/WebViewService.Profile.cs
```

Do not change logger usage in `MainWindow`, `MainViewModel`, `DiagnosticService`, or `HostedUiBridge` during this task.

- [x] **Step 2: Capture heartbeat runtime options at start time**

Add fields in `WebViewService.Heartbeat.cs`:

```csharp
private int _heartbeatHardRefreshCooldownSeconds;
private bool _heartbeatEnabledForRun;
```

Change `StartHeartbeat` so it captures values from the `HeartbeatOptions` and `RecoveryPolicyOptions` supplied by the caller. If `StartHeartbeat` currently has no recovery-policy parameter, add one:

```csharp
public void StartHeartbeat(
    string? gatewayUrl,
    HeartbeatOptions heartbeatOptions,
    RecoveryPolicyOptions recoveryPolicyOptions)
{
    _heartbeatEnabledForRun = heartbeatOptions.EnableHeartbeat;
    _heartbeatHardRefreshCooldownSeconds = recoveryPolicyOptions.HardRefreshCooldownSeconds;
    // keep existing interval/threshold setup
}
```

Then replace the mid-loop `App.Configuration.Settings.RecoveryPolicy.HardRefreshCooldownSeconds` read with:

```csharp
var seconds = _heartbeatHardRefreshCooldownSeconds;
```

- [x] **Step 3: Update callers**

Update callers that start heartbeat work to pass both settings explicitly:

```csharp
webViewService.StartHeartbeat(
    environment.GatewayUrl,
    App.Configuration.Settings.Heartbeat,
    App.Configuration.Settings.RecoveryPolicy);
```

If the call flows through `ShellSessionCoordinatorAdapters`, Task 7A will remove the adapter's fallback global reads and keep this explicit configuration flow.

- [x] **Step 4: Add guardrails**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$webViewServiceFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services') -File -Filter 'WebViewService*.cs'
foreach ($file in $webViewServiceFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match 'App\.Logger') {
        throw "WebViewService partial must use injected logger, not App.Logger: $($file.Name)"
    }
}

$heartbeat = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Heartbeat.cs') -Raw
if ($heartbeat -match 'App\.Configuration\.Settings\.Heartbeat|App\.Configuration\.Settings\.RecoveryPolicy') {
    throw 'Heartbeat must capture settings at start time instead of reading App.Configuration mid-loop.'
}
```

- [x] **Step 5: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/WebViewService*.cs tools/verify-repo-structure.ps1
git commit -m "refactor: inject WebViewService logger and capture heartbeat config"
```

---

### Task 3B: Move WebView Command Scripts To Embedded Assets

**Files:**
- Create: `src/OpenClaw/Services/WebViewCommands.StopInjection.js`
- Create: `src/OpenClaw/Services/WebViewCommands.AbortRun.js`
- Modify: `src/OpenClaw/Services/WebViewService.Commands.cs`
- Modify: `src/OpenClaw/OpenClaw.csproj`
- Modify: `tools/verify-repo-structure.ps1`

- [ ] **Step 1: Extract `/stop` injection script**

Move the JavaScript currently embedded inside `TryInjectStopCommandAsync()` to `WebViewCommands.StopInjection.js` without changing selectors, input handling, event dispatch, or `/stop` text.

The C# method should become:

```csharp
private async Task<bool> TryInjectStopCommandAsync()
{
    try
    {
        var result = await ExecuteScriptAsync(WebViewCommandScripts.StopInjection);
        return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
    }
    catch (COMException ex) when (IsCoreWebViewUnavailable(ex))
    {
        _logger.Warning($"Stop skipped because CoreWebView2 became unavailable: {ex.Message}");
        return false;
    }
    catch (Exception ex)
    {
        _logger.Warning($"Failed to inject /stop command: {ex.Message}");
        return false;
    }
}
```

- [ ] **Step 2: Extract abort-run script**

Move the JavaScript currently embedded inside `TryAbortActiveRunAsync()` to `WebViewCommands.AbortRun.js` without changing stop/abort button detection or hosted API targets.

The C# method should become:

```csharp
private async Task<bool> TryAbortActiveRunAsync()
{
    try
    {
        var result = await ExecuteScriptAsync(WebViewCommandScripts.AbortRun);
        return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
    }
    catch (COMException ex) when (IsCoreWebViewUnavailable(ex))
    {
        _logger.Warning($"Abort skipped because CoreWebView2 became unavailable: {ex.Message}");
        return false;
    }
    catch (Exception ex)
    {
        _logger.Warning($"Failed to trigger hosted UI stop action: {ex.Message}");
        return false;
    }
}
```

- [ ] **Step 3: Add command script loader**

Add a small internal helper in `WebViewService.Commands.cs` or a new `WebViewCommandScripts.cs`:

```csharp
internal static class WebViewCommandScripts
{
    private const string StopInjectionResourceName = "OpenClaw.Services.WebViewCommands.StopInjection.js";
    private const string AbortRunResourceName = "OpenClaw.Services.WebViewCommands.AbortRun.js";

    public static string StopInjection => _stopInjection.Value;
    public static string AbortRun => _abortRun.Value;

    private static readonly Lazy<string> _stopInjection = new(() => Load(StopInjectionResourceName));
    private static readonly Lazy<string> _abortRun = new(() => Load(AbortRunResourceName));

    private static string Load(string resourceName)
    {
        var assembly = typeof(WebViewCommandScripts).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded WebView command script: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 4: Add embedded resources**

Add to `OpenClaw.csproj`:

```xml
<EmbeddedResource Include="Services\WebViewCommands.StopInjection.js" LogicalName="OpenClaw.Services.WebViewCommands.StopInjection.js" />
<EmbeddedResource Include="Services\WebViewCommands.AbortRun.js" LogicalName="OpenClaw.Services.WebViewCommands.AbortRun.js" />
```

- [ ] **Step 5: Add inline JavaScript guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$commandFile = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/WebViewService.Commands.cs') -Raw
if ($commandFile -match 'ExecuteScriptAsync\(@"|ExecuteScriptAsync\(\$@"') {
    throw 'WebViewService.Commands.cs must load browser scripts from embedded JS assets, not large inline strings.'
}
foreach ($asset in @('WebViewCommands.StopInjection.js', 'WebViewCommands.AbortRun.js')) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "src/OpenClaw/Services/$asset"))) {
        throw "Missing WebView command script asset: $asset"
    }
}
```

- [ ] **Step 6: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/WebViewService.Commands.cs src/OpenClaw/Services/WebViewCommandScripts.cs src/OpenClaw/Services/WebViewCommands.StopInjection.js src/OpenClaw/Services/WebViewCommands.AbortRun.js src/OpenClaw/OpenClaw.csproj tools/verify-repo-structure.ps1
git commit -m "refactor: move WebView command scripts to embedded assets"
```

---

### Task 3C: Extract WebView Recreation Scheduling

**Files:**
- Create: `src/OpenClaw/Services/WebViewRecreationService.cs`
- Modify: `src/OpenClaw/MainWindow.WebView.cs`
- Modify: `src/OpenClaw/MainWindow.Shared.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [ ] **Step 1: Create scheduling service**

Create `WebViewRecreationService` to own merged recreation requests, cooldown/circuit-breaker checks, and scheduling telemetry:

```csharp
internal sealed class WebViewRecreationService
{
    private readonly IAppLogger _logger;
    private bool _isRecreating;
    private bool _hasQueuedRequest;
    private string? _queuedReason;
    private int _mergedRequests;

    public WebViewRecreationService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsRecreating => _isRecreating;
    public int MergedRequests => _mergedRequests;

    public WebViewRecreationDecision Schedule(string reason, bool circuitBreakerAllowsRecreation)
    {
        if (!circuitBreakerAllowsRecreation)
        {
            return WebViewRecreationDecision.Suppressed(reason);
        }

        if (_isRecreating)
        {
            _hasQueuedRequest = true;
            _queuedReason = reason;
            _mergedRequests++;
            _logger.Info("webview.recreate.merged", new { reason, merged = _mergedRequests });
            return WebViewRecreationDecision.Merged(reason);
        }

        _isRecreating = true;
        return WebViewRecreationDecision.Start(reason);
    }

    public string? CompleteAndConsumeQueuedReason()
    {
        _isRecreating = false;
        if (!_hasQueuedRequest)
        {
            return null;
        }

        _hasQueuedRequest = false;
        var reason = _queuedReason;
        _queuedReason = null;
        return reason;
    }
}

internal readonly record struct WebViewRecreationDecision(
    WebViewRecreationDecisionKind Kind,
    string Reason)
{
    public static WebViewRecreationDecision Start(string reason) => new(WebViewRecreationDecisionKind.Start, reason);
    public static WebViewRecreationDecision Merged(string reason) => new(WebViewRecreationDecisionKind.Merged, reason);
    public static WebViewRecreationDecision Suppressed(string reason) => new(WebViewRecreationDecisionKind.Suppressed, reason);
}

internal enum WebViewRecreationDecisionKind
{
    Start,
    Merged,
    Suppressed,
}
```

- [ ] **Step 2: Move policy out of `MainWindow.WebView.cs`**

Keep `MainWindow.WebView.cs` responsible for:

```text
- owning the actual WebView2 control instance
- starting/stopping the existing UI timer if one exists
- swapping the control in XAML
- calling ViewModel.InitializeWebViewAsync
```

Move these responsibilities into `WebViewRecreationService`:

```text
- whether a request starts now or merges into an in-flight request
- merged request count
- queued reason
- circuit-breaker suppression decision shape
- recreation instrumentation messages
```

- [ ] **Step 3: Wire service into `MainWindow`**

Add a field in `MainWindow.Shared.cs`:

```csharp
private readonly WebViewRecreationService _webViewRecreationService = new(App.Logger);
```

Use service decisions in the existing scheduling method:

```csharp
var decision = _webViewRecreationService.Schedule(reason, circuitBreakerAllowsRecreation);
if (decision.Kind == WebViewRecreationDecisionKind.Suppressed)
{
    ViewModel.ShowCircuitBreakerError();
    return;
}

if (decision.Kind == WebViewRecreationDecisionKind.Merged)
{
    return;
}

StartWebViewRecreationTimer(decision.Reason);
```

- [ ] **Step 4: Preserve existing platform behavior**

Do not move or rewrite:

```text
- WebView2 control construction
- WebView2 environment/user-data-folder selection
- title bar/DWM code
- tray show/hide code
- window bounds persistence
```

Those are historical high-risk areas documented in `DEVELOPMENT_NOTES.md`.

- [ ] **Step 5: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$mainWindowWebView = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.WebView.cs') -Raw
if ($mainWindowWebView -match '_mergedRecreationRequests|_queuedRecreationReason|_isRecreatingWebView') {
    throw 'WebView recreation scheduling state must live in WebViewRecreationService.'
}
```

- [ ] **Step 6: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/WebViewRecreationService.cs src/OpenClaw/MainWindow.WebView.cs src/OpenClaw/MainWindow.Shared.cs tools/verify-repo-structure.ps1
git commit -m "refactor: extract WebView recreation scheduling"
```

---

### Task 4: Convert Runtime Settings To An Apply Pipeline

**Files:**
- Create: `src/OpenClaw.Core/Models/LiveShellSettings.cs`
- Create: `src/OpenClaw.Core/Models/LiveShellSettingsChange.cs`
- Create: `src/OpenClaw.Core/Models/SettingsSaveResult.cs`
- Create: `src/OpenClaw/Services/LiveShellSettingsApplier.cs`
- Modify: `src/OpenClaw/ViewModels/SettingsViewModel.cs`
- Modify: `src/OpenClaw/Views/SettingsDialog.Shared.cs`
- Modify: `src/OpenClaw/MainWindow.Commands.cs`
- Modify: `src/OpenClaw/MainWindow.Shared.cs`
- Modify: `src/OpenClaw/MainWindow.xaml.cs`
- Modify: `src/OpenClaw/MainWindow.Hotkey.cs`
- Modify: `src/OpenClaw/MainWindow.AlwaysOnTop.cs`

- [ ] **Step 1: Create Core settings snapshot**

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Models;

public sealed record LiveShellSettings(
    bool AlwaysOnTop,
    bool EnableGlobalHotkey,
    string GlobalHotkey)
{
    public static LiveShellSettings From(AppSettings settings)
    {
        return new LiveShellSettings(
            settings.AlwaysOnTop,
            settings.EnableGlobalHotkey,
            settings.GlobalHotkey.Trim());
    }
}
```

- [ ] **Step 2: Create Core change descriptor**

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Models;

public sealed record LiveShellSettingsChange(
    LiveShellSettings Before,
    LiveShellSettings After)
{
    public bool DidChangeAlwaysOnTop => Before.AlwaysOnTop != After.AlwaysOnTop;
    public bool DidChangeGlobalHotkey =>
        Before.EnableGlobalHotkey != After.EnableGlobalHotkey ||
        !string.Equals(Before.GlobalHotkey, After.GlobalHotkey, StringComparison.Ordinal);

    public bool HasChanges => DidChangeAlwaysOnTop || DidChangeGlobalHotkey;
}
```

- [ ] **Step 3: Move `SettingsSaveResult` out of the view layer**

Create `src/OpenClaw.Core/Models/SettingsSaveResult.cs`:

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Models;

public readonly record struct SettingsSaveResult(
    bool DidChangeEnvironmentState,
    bool DidChangeSessionTopology,
    bool DidChangeLanguage,
    LiveShellSettingsChange LiveShellSettingsChange)
{
    public bool DidChangeLiveShellOptions => LiveShellSettingsChange.HasChanges;
}
```

Remove this record from `src/OpenClaw/Views/SettingsDialog.Shared.cs`:

```csharp
public readonly record struct SettingsSaveResult(
    bool DidChangeEnvironmentState,
    bool DidChangeSessionTopology,
    bool DidChangeLanguage,
    bool DidChangeLiveShellOptions);
```

Add this using to `src/OpenClaw/Views/SettingsDialog.Shared.cs`:

```csharp
using OpenClaw.Models;
```

Keep the `SettingsSaved` event signature unchanged:

```csharp
public event Action<SettingsSaveResult>? SettingsSaved;
```

- [ ] **Step 4: Make `SettingsSaveResult` carry the live-shell change**

Change result construction in `SettingsViewModel.TrySave`:

```csharp
var beforeLiveSettings = new LiveShellSettings(
    _originalAlwaysOnTop,
    _originalEnableGlobalHotkey,
    _originalGlobalHotkey.Trim());
```

Place that snapshot before any `App.Configuration.Settings` mutation in `SaveAll`. After all settings have been written and before returning `true`, construct the result with:

```csharp
var afterLiveSettings = LiveShellSettings.From(App.Configuration.Settings);
result = new SettingsSaveResult(
    DidChangeEnvironmentState,
    DidChangeSessionTopology,
    !string.Equals(_originalLanguage, SelectedLanguage, StringComparison.Ordinal),
    new LiveShellSettingsChange(beforeLiveSettings, afterLiveSettings));
```

Update `SettingsSaveResult` so `DidChangeLiveShellOptions` is computed from `LiveShellSettingsChange.HasChanges`.

- [ ] **Step 5: Create `LiveShellSettingsApplier`**

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

using OpenClaw.Models;

namespace OpenClaw.Services;

internal sealed class LiveShellSettingsApplier
{
    private readonly Action<bool> _setAlwaysOnTop;
    private readonly Action _reapplyGlobalHotkey;

    public LiveShellSettingsApplier(Action<bool> setAlwaysOnTop, Action reapplyGlobalHotkey)
    {
        _setAlwaysOnTop = setAlwaysOnTop;
        _reapplyGlobalHotkey = reapplyGlobalHotkey;
    }

    public void Apply(LiveShellSettingsChange change)
    {
        if (change.DidChangeAlwaysOnTop)
        {
            _setAlwaysOnTop(change.After.AlwaysOnTop);
        }

        if (change.DidChangeGlobalHotkey)
        {
            _reapplyGlobalHotkey();
        }
    }
}
```

- [ ] **Step 6: Wire the applier into `MainWindow`**

Add a field to `src/OpenClaw/MainWindow.Shared.cs`:

```csharp
private readonly LiveShellSettingsApplier _liveShellSettingsApplier;
```

Initialize it in `src/OpenClaw/MainWindow.xaml.cs` after `InitializeComponent()` and before settings windows can be opened:

```csharp
_liveShellSettingsApplier = new LiveShellSettingsApplier(SetAlwaysOnTop, ReapplyGlobalHotkey);
```

- [ ] **Step 7: Update `MainWindow` settings handler**

Replace `ApplyLiveShellSettings()` with:

```csharp
if (saveResult.LiveShellSettingsChange.HasChanges)
{
    _liveShellSettingsApplier.Apply(saveResult.LiveShellSettingsChange);
}
```

This removes direct re-reading from `App.Configuration.Settings` when the save result already knows what changed.

- [ ] **Step 8: Add structure guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$settingsViewModel = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/SettingsViewModel.cs') -Raw
if ($settingsViewModel -match 'using OpenClaw\.Views;') {
    throw 'SettingsViewModel must not depend on the Views namespace for SettingsSaveResult.'
}

$settingsDialogShared = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Views/SettingsDialog.Shared.cs') -Raw
if ($settingsDialogShared -match 'record struct SettingsSaveResult') {
    throw 'SettingsSaveResult must live in OpenClaw.Core models, not SettingsDialog.Shared.cs.'
}
```

- [ ] **Step 9: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw.Core/Models/LiveShellSettings.cs src/OpenClaw.Core/Models/LiveShellSettingsChange.cs src/OpenClaw.Core/Models/SettingsSaveResult.cs src/OpenClaw/Services/LiveShellSettingsApplier.cs src/OpenClaw/ViewModels/SettingsViewModel.cs src/OpenClaw/Views/SettingsDialog.Shared.cs src/OpenClaw/MainWindow.Commands.cs src/OpenClaw/MainWindow.Shared.cs src/OpenClaw/MainWindow.xaml.cs src/OpenClaw/MainWindow.Hotkey.cs src/OpenClaw/MainWindow.AlwaysOnTop.cs tools/verify-repo-structure.ps1
git commit -m "refactor: centralize live shell settings application"
```

---

### Task 5: Replace Compact Mode Property Patching With Visual States

**Files:**
- Modify: `src/OpenClaw/MainWindow.xaml`
- Modify: `src/OpenClaw/MainWindow.CompactMode.cs`
- Modify: `src/OpenClaw/Styles/StatusResources.xaml`
- Modify: `tools/verify-repo-structure.ps1`

- [ ] **Step 1: Add compact resources**

In `StatusResources.xaml`, add:

```xml
<x:Double x:Key="CompactTopStatusPillMinWidth">0</x:Double>
<Thickness x:Key="CompactTopStatusPillPadding">8,5</Thickness>
<Thickness x:Key="CompactTopStatusPillMargin">0,0,8,0</Thickness>
<Thickness x:Key="CompactTopBarPadding">8,6</Thickness>
```

- [ ] **Step 2: Add visual states to `RootLayout`**

`MainWindow` derives from `Window`, not `Control`, so avoid the Window-as-Control visual-state pattern. Attach the visual-state group to the existing root `Grid x:Name="RootLayout"` and switch it with `VisualStateManager.GoToElementState(RootLayout, stateName, useTransitions: false)`.

Add this block as the first child inside `RootLayout`:

```xml
<VisualStateManager.VisualStateGroups>
    <VisualStateGroup x:Name="ShellModeStates">
        <VisualState x:Name="FullMode" />
        <VisualState x:Name="CompactMode">
            <VisualState.Setters>
                <Setter Target="CommandBarSurface.Padding" Value="{StaticResource CompactTopBarPadding}" />
                <Setter Target="EnvironmentSummaryGroup.Visibility" Value="Collapsed" />
                <Setter Target="LatencyBadge.Visibility" Value="Collapsed" />
                <Setter Target="TopStatusPill.MinWidth" Value="{StaticResource CompactTopStatusPillMinWidth}" />
                <Setter Target="TopStatusPill.Margin" Value="{StaticResource CompactTopStatusPillMargin}" />
                <Setter Target="TopStatusPill.Padding" Value="{StaticResource CompactTopStatusPillPadding}" />
                <Setter Target="ModelStatusSegment.MinWidth" Value="0" />
                <Setter Target="ModelStatusSegment.Margin" Value="4,0,0,0" />
            </VisualState.Setters>
        </VisualState>
    </VisualStateGroup>
</VisualStateManager.VisualStateGroups>
```

- [ ] **Step 3: Simplify code-behind**

Replace `ApplyCompactTopBarState(bool isCompact)` body:

```csharp
private void ApplyCompactTopBarState(bool isCompact)
{
    VisualStateManager.GoToElementState(RootLayout, isCompact ? "CompactMode" : "FullMode", useTransitions: false);
}
```

Add `using Microsoft.UI.Xaml;` if it is not already available in `MainWindow.CompactMode.cs`.

Remove duplicated constants:

```csharp
private const double FullTopStatusPillMinWidth = 440;
private const double FullModelStatusSegmentMinWidth = 190;
```

- [ ] **Step 4: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$compact = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.CompactMode.cs') -Raw
if ($compact -match 'TopStatusPill\.MinWidth|ModelStatusSegment\.MinWidth|EnvironmentSummaryGroup\.Visibility|LatencyBadge\.Visibility') {
    throw 'Compact top-bar layout should be driven by XAML visual states, not code-behind property patching.'
}
$windowStatePattern = 'VisualStateManager\.GoToState\(' + 'this'
if ($compact -match $windowStatePattern) {
    throw 'MainWindow compact mode must switch RootLayout with GoToElementState.'
}

$mainWindowXaml = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/MainWindow.xaml') -Raw
if ($mainWindowXaml -notmatch 'x:Name="RootLayout"[\s\S]*VisualStateManager\.VisualStateGroups') {
    throw 'Compact visual states must be attached to RootLayout.'
}
```

- [ ] **Step 5: Run verification and manual UI check**

Run full verification from Task 1.

Manual VS2026 debug checklist:

```text
1. Launch app.
2. Enter compact mode from tray or title command.
3. Confirm 480px compact width shows status pill without environment selector or latency badge.
4. Confirm MODEL text ellipsizes instead of overlapping buttons.
5. Exit compact mode and confirm full top bar restores.
```

- [ ] **Step 6: Commit**

```powershell
git add src/OpenClaw/MainWindow.xaml src/OpenClaw/MainWindow.CompactMode.cs src/OpenClaw/Styles/StatusResources.xaml tools/verify-repo-structure.ps1
git commit -m "refactor: drive compact top bar through visual states"
```

---

### Task 6: Split Hosted Bridge Browser Script Into Focused Assets

**Files:**
- Create: `src/OpenClaw/Services/HostedUiBridge.StatusInspection.js`
- Create: `src/OpenClaw/Services/HostedUiBridge.CommandDispatch.js`
- Create: `src/OpenClaw/Services/HostedUiBridge.MutationFilter.js`
- Create: `src/OpenClaw/Services/HostedUiBridge.HostMessaging.js`
- Modify: `src/OpenClaw/Services/HostedUiBridge.Script.js`
- Modify: `src/OpenClaw/Services/HostedUiBridge.Script.cs`
- Modify: `src/OpenClaw/OpenClaw.csproj`
- Modify: `tools/verify-bridge-scripts.ps1`
- Modify: `tools/verify-repo-structure.ps1`

- [ ] **Step 1: Extract host messaging**

Move `postHostMessage`, host message kind constants, and safe `chrome.webview.postMessage` handling into `HostedUiBridge.HostMessaging.js`.

Export one object:

```javascript
const openClawHostMessaging = (() => {
  const postHostMessage = (message) => {
    try {
      if (!window.chrome?.webview?.postMessage) return false;
      window.chrome.webview.postMessage(message);
      return true;
    } catch {
      return false;
    }
  };

  return { postHostMessage };
})();
```

- [ ] **Step 2: Extract command dispatch**

Move `invokeBridgeMethod`, `dispatchBridgeEvent`, and `onCommand` into `HostedUiBridge.CommandDispatch.js`.

Expose:

```javascript
const openClawCommandDispatch = (() => {
  const createCommandHandler = ({ inspectControlUi, postStatus, checkSessionReady }) => {
    return async (message) => {
      const command = message?.command || '';
      const payload = message?.payload;

      switch (command) {
        case 'refresh_session':
          return await runCommand(
            command,
            payload,
            ['refreshSession', 'reloadSession', 'reconnect', 'connect', 'resume'],
            { inspectControlUi, postStatus, checkSessionReady });
        case 'fetch_recent_messages':
          return await runCommand(
            command,
            payload,
            ['fetchRecentMessages', 'loadRecentMessages', 'syncMessages', 'sync'],
            { inspectControlUi, postStatus });
        case 'lightweight_sync':
          return await runCommand(
            command,
            payload,
            ['sync', 'refresh', 'refreshSession', 'fetchRecentMessages', 'loadRecentMessages'],
            { inspectControlUi, postStatus, checkSessionReady });
        case 'reconnect_intent':
          return await runCommand(
            command,
            payload,
            ['reconnect', 'connect', 'resume', 'refreshSession'],
            { inspectControlUi, postStatus });
        default:
          return dispatchBridgeEvent(command, payload);
      }
    };
  };

  return { createCommandHandler, dispatchBridgeEvent, invokeBridgeMethod };
})();
```

The helper `runCommand` should call `invokeBridgeMethod`, then post a fresh status snapshot and session-ready check when provided, then return `handled || dispatchBridgeEvent(command, payload)`.

- [ ] **Step 3: Extract mutation filtering**

Move excluded selectors and mutation relevance checks into `HostedUiBridge.MutationFilter.js`.

Expose:

```javascript
const openClawMutationFilter = (() => {
  const STATUS_PROBE_EXCLUDED_SELECTOR = '.chat-sidebar, .sidebar-panel, .sidebar-content, .chat-tool-card__preview-frame, .settings-workspace__body, .config-content, .config-form, .config-section-card, .cron-summary-strip, .cron-workspace';
  const isStatusProbeExcludedElement = (el) => Boolean(el?.closest?.(STATUS_PROBE_EXCLUDED_SELECTOR));
  const isStatusRelevantMutation = (mutation) => {
    const target = asElement(mutation.target);
    if (!target || isStatusProbeExcludedElement(target)) return false;
    if (mutation.type === 'childList') return true;
    if (mutation.type !== 'attributes') return false;
    return ['aria-busy', 'data-busy', 'data-running', 'data-state', 'data-status', 'aria-label', 'title']
      .includes(mutation.attributeName || '');
  };

  const isStatusRelevantEventTarget = (target) => {
    const element = asElement(target);
    return Boolean(element) && !isStatusProbeExcludedElement(element);
  };

  return { isStatusProbeExcludedElement, isStatusRelevantMutation, isStatusRelevantEventTarget };
})();
```

- [ ] **Step 4: Extract status inspection**

Move `inspectControlUi`, app-state status reading, DOM fallback status reading, stale busy activity signature, and current model reader wiring into `HostedUiBridge.StatusInspection.js`.

Expose:

```javascript
const openClawStatusInspection = (() => {
  const createInspector = ({ strings, mutationFilter, modelResolver }) => {
    const inspectControlUi = () => {
      // Move the existing inspectControlUi body here and replace:
      // STRINGS -> strings
      // isStatusProbeExcludedElement -> mutationFilter.isStatusProbeExcludedElement
      // readCurrentModel -> a local reader backed by modelResolver.resolveOpenClawAppStateModel
    };

    return { inspectControlUi, isEditableElement, compactText };
  };

  return { createInspector };
})();
```

Keep the current stale-busy behavior together with status inspection: `readChatActivitySignature`, `collectDomActivitySignature`, `applyBusyStaleness`, `BUSY_STALE_THRESHOLD_MS`, `inputFocused`, and `focusedInputHasText` move into this asset.

- [ ] **Step 5: Make `HostedUiBridge.Script.js` a composition file**

After extraction, `HostedUiBridge.Script.js` should only:

- define `STRINGS`
- compose imported asset snippets
- create `inspectControlUi`
- wire `window.__openClawHostBridge`
- schedule mutation/change/load events

Target size: under 250 lines.

- [ ] **Step 6: Update script builder**

In `HostedUiBridge.Script.cs`, add placeholders:

```csharp
private const string HostMessagingPlaceholder = "__OPENCLAW_HOST_MESSAGING_SCRIPT__";
private const string StatusInspectionPlaceholder = "__OPENCLAW_STATUS_INSPECTION_SCRIPT__";
private const string CommandDispatchPlaceholder = "__OPENCLAW_COMMAND_DISPATCH_SCRIPT__";
private const string MutationFilterPlaceholder = "__OPENCLAW_MUTATION_FILTER_SCRIPT__";
```

Load and replace each embedded resource in stable dependency order:

1. Host messaging
2. Mutation filter
3. Model resolver
4. Status inspection
5. Command dispatch
6. Main script

- [ ] **Step 7: Update project embedded resources**

Add to `OpenClaw.csproj`:

```xml
<EmbeddedResource Include="Services\HostedUiBridge.StatusInspection.js" LogicalName="OpenClaw.Services.HostedUiBridge.StatusInspection.js" />
<EmbeddedResource Include="Services\HostedUiBridge.CommandDispatch.js" LogicalName="OpenClaw.Services.HostedUiBridge.CommandDispatch.js" />
<EmbeddedResource Include="Services\HostedUiBridge.MutationFilter.js" LogicalName="OpenClaw.Services.HostedUiBridge.MutationFilter.js" />
<EmbeddedResource Include="Services\HostedUiBridge.HostMessaging.js" LogicalName="OpenClaw.Services.HostedUiBridge.HostMessaging.js" />
```

- [ ] **Step 8: Extend bridge script verification**

Extend the existing `tools/verify-bridge-scripts.ps1` from Task 1 with checks for:

```text
PASS: MODEL default model
PASS: MODEL null override session precedence
PASS: MODEL object override
PASS: command dispatch returns handled true when bridge method exists
PASS: command dispatch falls back to CustomEvent when method missing
PASS: host messaging returns false without chrome.webview
PASS: mutation filter ignores settings/config/cron/sidebar mutations
```

Do this with Node.js only; skip cleanly when Node is missing.

- [ ] **Step 9: Add structure guardrails**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$mainBridgeScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.Script.js') -Raw
$mainBridgeLines = ($mainBridgeScript -split "`n").Count
if ($mainBridgeLines -gt 250) {
    throw "HostedUiBridge.Script.js should be a composition file under 250 lines; found $mainBridgeLines."
}
foreach ($asset in @(
    'HostedUiBridge.StatusInspection.js',
    'HostedUiBridge.CommandDispatch.js',
    'HostedUiBridge.MutationFilter.js',
    'HostedUiBridge.HostMessaging.js'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "src/OpenClaw/Services/$asset"))) {
        throw "Missing focused bridge asset: $asset"
    }
}
```

- [ ] **Step 10: Run verification and commit**

Run full verification from Task 1, using the renamed `tools\verify-bridge-scripts.ps1` if renamed.

Commit:

```powershell
git add src/OpenClaw/Services/HostedUiBridge*.js src/OpenClaw/Services/HostedUiBridge.Script.cs src/OpenClaw/OpenClaw.csproj tools/verify-bridge-scripts.ps1 tools/verify-repo-structure.ps1
git commit -m "refactor: split hosted bridge browser assets"
```

---

### Task 6A: Correct Hosted Bridge Polling Timer Drift

**Files:**
- Modify: `src/OpenClaw/Services/HostedUiBridge.Script.js`
- Modify: `tools/verify-bridge-scripts.ps1`

- [ ] **Step 1: Replace recursive timeout scheduling with drift-aware scheduling**

After Task 6 has reduced `HostedUiBridge.Script.js` to a composition shell, keep the polling owner in that shell but make it self-correcting.

Replace the current polling state:

```javascript
let pollInterval = 8000;
let pollTimer = 0;
```

with:

```javascript
let pollInterval = 8000;
let pollTimer = 0;
let nextPollAt = 0;

const getPollInterval = (snapshot) => {
  if (snapshot.phase === 'connected' && snapshot.isBusy) return 4000;
  if (snapshot.phase === 'gateway_connecting' || snapshot.phase === 'page_loaded') return 4000;
  return 15000;
};

const scheduleNextPoll = (interval, now = Date.now()) => {
  pollInterval = interval;
  nextPollAt = nextPollAt > now ? nextPollAt + interval : now + interval;
  const delay = Math.max(0, nextPollAt - Date.now());
  pollTimer = window.setTimeout(tick, delay);
};
```

Then make `tick` compute the next interval from the snapshot and call `scheduleNextPoll(nextInterval)` after the current inspection work completes. Do not schedule the next tick before the current status snapshot is posted.

- [ ] **Step 2: Preserve restart semantics**

Keep the existing restart entry point but reset `nextPollAt` when an external event requests immediate polling:

```javascript
const restartPolling = (interval = pollInterval) => {
  if (pollTimer) {
    window.clearTimeout(pollTimer);
  }

  nextPollAt = 0;
  scheduleNextPoll(interval);
};
```

- [ ] **Step 3: Add bridge script verification for drift helper**

Extend `tools/verify-bridge-scripts.ps1` to load the composition shell and assert these strings exist after Task 6A:

```text
PASS: polling uses nextPollAt drift correction
PASS: polling interval is snapshot-driven
```

The script check can be structural:

```powershell
$bridgeScript = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.Script.js') -Raw
if ($bridgeScript -notmatch 'nextPollAt' -or $bridgeScript -notmatch 'scheduleNextPoll') {
    throw 'Bridge polling must use drift-aware scheduling.'
}
```

- [ ] **Step 4: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/HostedUiBridge.Script.js tools/verify-bridge-scripts.ps1
git commit -m "fix: correct hosted bridge polling drift"
```

---

### Task 7: Move Diagnostic WebView Runtime Knowledge Out Of Core

**Files:**
- Modify: `src/OpenClaw.Core/Services/DiagnosticBundleService.cs`
- Modify: `src/OpenClaw/Services/DiagnosticService.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Commands.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [ ] **Step 1: Remove WebView2 type-name reflection from Core**

In `DiagnosticBundleService`, remove this Core-layer WebView2 lookup:

```csharp
Type.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment, Microsoft.Web.WebView2.Core")
```

Add a pure Core runtime record near `DiagnosticBundleService`:

```csharp
public sealed record DiagnosticRuntimeInfo(
    string? WebView2RuntimeVersion,
    string OsVersion,
    string DotNetVersion,
    string AppVersion,
    string ProcessArchitecture,
    int ProcessorCount,
    string MachineHash);
```

Replace `CollectRuntimeInfo()` with overloads that accept the plain record and no longer know the WebView2 assembly name:

```csharp
public static DiagnosticRuntimeInfo CollectRuntimeInfo(string? webView2RuntimeVersion)
{
    return new DiagnosticRuntimeInfo(
        WebView2RuntimeVersion: webView2RuntimeVersion,
        OsVersion: Environment.OSVersion.ToString(),
        DotNetVersion: Environment.Version.ToString(),
        AppVersion: GetAppVersion(),
        ProcessArchitecture: System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        ProcessorCount: Environment.ProcessorCount,
        MachineHash: HashMachineName(Environment.MachineName));
}

public static string FormatRuntimeInfo(DiagnosticRuntimeInfo runtimeInfo)
{
    var webView2 = string.IsNullOrWhiteSpace(runtimeInfo.WebView2RuntimeVersion)
        ? "unavailable"
        : runtimeInfo.WebView2RuntimeVersion;

    return string.Join(Environment.NewLine, new[]
    {
        $"OS: {runtimeInfo.OsVersion}",
        $".NET: {runtimeInfo.DotNetVersion}",
        $"App: {runtimeInfo.AppVersion}",
        $"Architecture: {runtimeInfo.ProcessArchitecture}",
        $"Machine: {runtimeInfo.MachineHash}",
        $"Processors: {runtimeInfo.ProcessorCount}",
        $"WebView2: {webView2}",
    });
}
```

Change `ExportBundleAsync` to receive runtime info instead of collecting it internally:

```csharp
public static async Task<string> ExportBundleAsync(
    string settingsJson,
    string logsDirectory,
    string diagnosticSummary,
    string outputDirectory,
    DiagnosticRuntimeInfo runtimeInfo)
```

Inside `ExportBundleAsync`, replace:

```csharp
var runtimeInfo = CollectRuntimeInfo();
await AddTextEntryAsync(archive, "runtime-info.txt", runtimeInfo);
```

with:

```csharp
await AddTextEntryAsync(archive, "runtime-info.txt", FormatRuntimeInfo(runtimeInfo));
```

- [ ] **Step 2: Collect WebView2 runtime version in WinUI layer**

In `DiagnosticService`, use direct WebView2 API:

```csharp
public static string? GetWebView2RuntimeVersion()
{
    try
    {
        return CoreWebView2Environment.GetAvailableBrowserVersionString();
    }
    catch (Exception ex)
    {
        App.Logger.Warning($"WebView2 runtime version lookup failed: {ex.Message}");
        return null;
    }
}
```

Keep `CheckWebView2Runtime()` on the existing behavior path, but call the helper so there is one WinUI-owned runtime lookup:

```csharp
public static DiagnosticResult CheckWebView2Runtime()
{
    var version = GetWebView2RuntimeVersion();
    if (string.IsNullOrEmpty(version))
    {
        return DiagnosticResult.Fail(
            StringResources.DiagnosticWebViewRuntimeNotFound,
            StringResources.DiagnosticWebViewRuntimeNotFoundDetail);
    }

    return DiagnosticResult.Pass($"{StringResources.DiagnosticWebView2RuntimeLabel} v{version}");
}
```

- [ ] **Step 3: Pass runtime info from export command**

In `MainViewModel.Commands.cs`, update `OnExportDiagnosticBundleAsync()` before calling Core:

```csharp
var runtimeInfo = DiagnosticBundleService.CollectRuntimeInfo(
    DiagnosticService.GetWebView2RuntimeVersion());
```

Then pass the record to the export call:

```csharp
var outputPath = await DiagnosticBundleService.ExportBundleAsync(
    settingsJson,
    logsDirectory,
    diagnosticSummary,
    outputDirectory,
    runtimeInfo);
```

- [ ] **Step 4: Strengthen Core guardrail**

Update `tools/verify-repo-structure.ps1` to reject:

```powershell
$forbiddenCorePattern = 'using Microsoft\.UI|using Microsoft\.Web\.WebView2|using Windows\.Graphics|using Windows\.UI|using WinRT|Microsoft\.Web\.WebView2|Type\.GetType\("Microsoft\.Web\.WebView2'
```

inside `src/OpenClaw.Core`.

- [ ] **Step 5: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw.Core/Services/DiagnosticBundleService.cs src/OpenClaw/Services/DiagnosticService.cs src/OpenClaw/ViewModels/MainViewModel.Commands.cs tools/verify-repo-structure.ps1
git commit -m "refactor: keep WebView2 diagnostics in WinUI layer"
```

---

### Task 7A: Inject Coordinator Adapter Configuration

**Files:**
- Modify: `src/OpenClaw/Services/ShellSessionCoordinator.Adapters.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Lifecycle.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [ ] **Step 1: Remove global fallback reads from adapter extension**

Change `ShellSessionCoordinatorAdapters.AttachAsync` so callers must supply recovery and heartbeat options:

```csharp
public static Task AttachAsync(
    this ShellSessionCoordinator coordinator,
    WebViewService webViewService,
    HostedUiBridge bridge,
    RecoveryPolicyOptions recoveryOptions,
    HeartbeatOptions heartbeatOptions,
    IAppLogger logger)
{
    return coordinator.AttachAsync(
        new ShellSessionWebViewAdapter(webViewService),
        new ShellSessionBridgeAdapter(bridge),
        recoveryOptions,
        heartbeatOptions,
        logger);
}
```

Do not keep nullable defaults that read `App.Configuration` or `App.Logger` inside the adapter.

- [ ] **Step 2: Pass options at the call site**

In `MainViewModel.Lifecycle.cs`, replace:

```csharp
await _coordinator.AttachAsync(_webViewService, _hostedUiBridge);
```

with:

```csharp
await _coordinator.AttachAsync(
    _webViewService,
    _hostedUiBridge,
    App.Configuration.Settings.RecoveryPolicy,
    App.Configuration.Settings.Heartbeat,
    App.Logger);
```

This keeps App-bound configuration at the application orchestration edge instead of inside the adapter.

- [ ] **Step 3: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$adapter = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/ShellSessionCoordinator.Adapters.cs') -Raw
if ($adapter -match 'App\.Configuration|App\.Logger') {
    throw 'ShellSessionCoordinator adapter must receive configuration and logger explicitly.'
}
```

- [ ] **Step 4: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/ShellSessionCoordinator.Adapters.cs src/OpenClaw/ViewModels/MainViewModel.Lifecycle.cs tools/verify-repo-structure.ps1
git commit -m "refactor: inject coordinator adapter configuration"
```

---

### Task 7B: Inject UI Dispatcher Into MainViewModel

**Files:**
- Modify: `src/OpenClaw/ViewModels/MainViewModel.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Fields.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Status.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Heartbeat.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Indicators.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [ ] **Step 1: Add dispatcher abstraction**

Add a private field:

```csharp
private readonly Action<Action> _dispatchToUi;
```

Update the constructor without dropping the `IAppLogger` dependency introduced in Task 2:

```csharp
public MainViewModel(IAppLogger logger, Action<Action>? dispatchToUi = null)
{
    _webViewService = new WebViewService(logger);
    _dispatchToUi = dispatchToUi ?? DispatchThroughMainWindow;
    InitializeCommands();
    SubscribeToServiceEvents();
    InitializeCoordinator();
    LoadEnvironments();
    UpdateStatusPresentation();
}

private static void DispatchThroughMainWindow(Action action)
{
    var dispatcher = App.MainWindow?.DispatcherQueue;
    if (dispatcher is null || !dispatcher.TryEnqueue(() => action()))
    {
        action();
    }
}
```

Keep the fallback synchronous execution so startup/shutdown races do not silently drop status updates.

- [ ] **Step 2: Remove static helper from status partial**

Replace `RunOnUiThread(() => ...)` in `MainViewModel.Status.cs`, `MainViewModel.Heartbeat.cs`, and `MainViewModel.Indicators.cs` with:

```csharp
_dispatchToUi(() => ApplyConnectionState(state));
```

Use the matching existing lambda body for each call.

- [ ] **Step 3: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$viewModelStatus = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Status.cs') -Raw
if ($viewModelStatus -match 'App\.MainWindow|RunOnUiThread') {
    throw 'MainViewModel status updates must use injected UI dispatcher.'
}
```

- [ ] **Step 4: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/ViewModels/MainViewModel.cs src/OpenClaw/ViewModels/MainViewModel.Fields.cs src/OpenClaw/ViewModels/MainViewModel.Status.cs src/OpenClaw/ViewModels/MainViewModel.Heartbeat.cs src/OpenClaw/ViewModels/MainViewModel.Indicators.cs tools/verify-repo-structure.ps1
git commit -m "refactor: inject MainViewModel UI dispatcher"
```

---

### Task 7C1: Extract StatusPresenter And Theme-Aware Status Brushes

**Files:**
- Create: `src/OpenClaw/ViewModels/StatusPresenter.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Fields.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Formatting.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.StatusFormatting.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Heartbeat.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Indicators.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.RunIndicators.cs`
- Modify: `src/OpenClaw/Styles/StatusResources.xaml`
- Modify: `tools/verify-repo-structure.ps1`

- [ ] **Step 1: Add semantic status brush resources**

In `StatusResources.xaml`, add theme-aware semantic resources instead of constructing status brushes in the ViewModel:

```xml
<SolidColorBrush x:Key="StatusNeutralBrush" Color="#6B7280" />
<SolidColorBrush x:Key="StatusSuccessBrush" Color="#22C55E" />
<SolidColorBrush x:Key="StatusWarningBrush" Color="#F59E0B" />
<SolidColorBrush x:Key="StatusErrorBrush" Color="#EF4444" />
```

If the project already has equivalent resources in `Colors.xaml`, reuse those keys and document the chosen key names in `docs/code-style.md` during Task 9.

- [ ] **Step 2: Add resource lookup helpers**

Replace static brush creation in `MainViewModel.Fields.cs`:

```csharp
private static readonly Brush NeutralBrush = CreateBrush(107, 114, 128);
private static readonly Brush SuccessBrush = CreateBrush(34, 197, 94);
private static readonly Brush WarningBrush = CreateBrush(245, 158, 11);
private static readonly Brush ErrorBrush = CreateBrush(239, 68, 68);
```

with resource-backed properties:

```csharp
private static Brush NeutralBrush => GetStatusBrush("StatusNeutralBrush");
private static Brush SuccessBrush => GetStatusBrush("StatusSuccessBrush");
private static Brush WarningBrush => GetStatusBrush("StatusWarningBrush");
private static Brush ErrorBrush => GetStatusBrush("StatusErrorBrush");

private static Brush GetStatusBrush(string key)
{
    return Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
        ? brush
        : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}
```

This removes static brush instances while preserving the existing formatting call sites.

- [ ] **Step 3: Extract pure formatting into `StatusPresenter`**

Create `StatusPresenter` for formatting methods that do not need to mutate ViewModel fields. Keep brush lookup in `MainViewModel` and pass the current brushes into the presenter so `StatusPresenter` does not need to reach into private ViewModel members.

```csharp
internal readonly record struct StatusBrushes(
    Brush Neutral,
    Brush Success,
    Brush Warning,
    Brush Error);

internal readonly record struct StatusPresentation(string Text, Brush Brush);

internal sealed class StatusPresenter
{
    public StatusPresentation FormatConnectionState(ConnectionState state, StatusBrushes brushes)
    {
        return state switch
        {
            ConnectionState.Connected => new StatusPresentation(StringResources.StatusConnected, brushes.Success),
            ConnectionState.Loading => new StatusPresentation(StringResources.StatusLoading, brushes.Warning),
            ConnectionState.GatewayConnecting => new StatusPresentation(StringResources.StatusGatewayConnecting, brushes.Warning),
            ConnectionState.Reconnecting => new StatusPresentation(StringResources.StatusReconnecting, brushes.Warning),
            ConnectionState.AuthFailed => new StatusPresentation(StringResources.StatusAuthFailed, brushes.Error),
            ConnectionState.Error => new StatusPresentation(StringResources.StatusError, brushes.Error),
            _ => new StatusPresentation(StringResources.StatusOffline, brushes.Neutral),
        };
    }
}
```

In `MainViewModel`, provide the brushes at the call site:

```csharp
private StatusBrushes CurrentStatusBrushes =>
    new(NeutralBrush, SuccessBrush, WarningBrush, ErrorBrush);
```

During implementation, move the existing pure methods from `MainViewModel.Formatting.cs` and `MainViewModel.StatusFormatting.cs` into `StatusPresenter`. Keep methods that set bindable properties in `MainViewModel`. Remove the private nested `StatusPresentation` record from `MainViewModel.StatusFormatting.cs` after the new shared record compiles.

- [ ] **Step 4: Keep mutation in MainViewModel**

`MainViewModel.Status.cs` should continue to own:

```text
- event handlers
- setting bindable properties
- telemetry logging
- dispatching to the UI thread
```

`StatusPresenter` should own:

```text
- text/brush/mode calculation from input snapshots and states
- no field mutation
- no App.Configuration reads
- no App.MainWindow reads
```

- [ ] **Step 5: Add guardrails**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$fields = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Fields.cs') -Raw
if ($fields -match 'CreateBrush\(|new SolidColorBrush\(Color\.FromArgb') {
    throw 'MainViewModel must use theme-aware status brush resources.'
}

$presenter = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/StatusPresenter.cs') -Raw
if ($presenter -match 'App\.Configuration|App\.MainWindow|SetProperty\(') {
    throw 'StatusPresenter must stay pure presentation logic.'
}
```

- [ ] **Step 6: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/ViewModels/StatusPresenter.cs src/OpenClaw/ViewModels/MainViewModel.Fields.cs src/OpenClaw/ViewModels/MainViewModel.Formatting.cs src/OpenClaw/ViewModels/MainViewModel.StatusFormatting.cs src/OpenClaw/ViewModels/MainViewModel.Heartbeat.cs src/OpenClaw/ViewModels/MainViewModel.Indicators.cs src/OpenClaw/ViewModels/MainViewModel.RunIndicators.cs src/OpenClaw/Styles/StatusResources.xaml tools/verify-repo-structure.ps1
git commit -m "refactor: extract status presentation and resource brushes"
```

---

### Task 7C2: Narrow MainViewModel Service Surface

**Files:**
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Core.Properties.cs`
- Modify: `src/OpenClaw/MainWindow.WebView.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [ ] **Step 1: Audit current external consumers**

Current external consumers are expected to be `MainWindow` partials only:

```text
ViewModel.Coordinator
ViewModel.WebViewService
ViewModel.HostedUiBridge
```

Search before editing:

```powershell
rg -n "ViewModel\.(WebViewService|HostedUiBridge|Coordinator)" src/OpenClaw -g "*.cs" -g "*.xaml"
```

- [ ] **Step 2: Replace service property access with narrow methods where simple**

For instrumentation updates currently using `ViewModel.Coordinator`, add a ViewModel method:

```csharp
public void UpdateShellInstrumentation(string lastInstrumentationEvent)
{
    _coordinator?.UpdateInstrumentation(
        totalControlUiInspectionRequests: _webViewService.TotalControlUiInspectionRequests,
        cachedControlUiInspectionRequests: _webViewService.CachedControlUiInspectionRequests,
        coalescedControlUiInspectionRequests: _webViewService.CoalescedControlUiInspectionRequests,
        deferredSaveRequests: App.Configuration.DeferredSaveRequests,
        deferredSaveCoalescedRequests: App.Configuration.DeferredSaveCoalescedRequests,
        heartbeatRecoveryRequests: _webViewService.HeartbeatRecoveryRequests,
        lastInstrumentationEvent: lastInstrumentationEvent);
}
```

Use this method from `MainWindow.WebView.cs` instead of reaching into `Coordinator`.

- [ ] **Step 3: Narrow remaining service properties**

If `MainWindow` still needs a service for control binding or initialization, change public properties to `internal`:

```csharp
internal WebViewService WebViewService => _webViewService;
internal HostedUiBridge HostedUiBridge => _hostedUiBridge;
internal ShellSessionCoordinator? Coordinator => _coordinator;
```

Do not make them public unless a XAML binding requires it. Current `MainWindow.xaml` does not bind these service properties.

- [ ] **Step 4: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$coreProperties = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Core.Properties.cs') -Raw
if ($coreProperties -match 'public WebViewService|public HostedUiBridge|public ShellSessionCoordinator') {
    throw 'MainViewModel service properties must not be public.'
}
```

- [ ] **Step 5: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/ViewModels/MainViewModel.Core.Properties.cs src/OpenClaw/MainWindow.WebView.cs tools/verify-repo-structure.ps1
git commit -m "refactor: narrow MainViewModel service surface"
```

---

### Task 7C3: Localize Circuit Breaker Error Text

**Files:**
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Commands.cs`
- Modify: `src/OpenClaw/Strings/en-us/Resources.resw`
- Modify: `src/OpenClaw/Strings/zh-cn/Resources.resw`
- Modify: `tools/verify-repo-structure.ps1`

- [ ] **Step 1: Add resource keys**

Add to `Resources.resw` English:

```xml
<data name="CircuitBreakerRecreationSuppressed" xml:space="preserve">
  <value>WebView recovery is temporarily paused after repeated failures. Please wait a moment before retrying.</value>
</data>
```

Add to `Resources.resw` Chinese:

```xml
<data name="CircuitBreakerRecreationSuppressed" xml:space="preserve">
  <value>WebView 连续恢复失败后已暂时暂停自动恢复。请稍后再重试。</value>
</data>
```

- [ ] **Step 2: Replace hardcoded message**

In `ShowCircuitBreakerError()`, replace the hardcoded English message with:

```csharp
ErrorMessage = StringResources.CircuitBreakerRecreationSuppressed;
```

Keep existing visibility/retry-button behavior unchanged.

- [ ] **Step 3: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$commands = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/ViewModels/MainViewModel.Commands.cs') -Raw
if ($commands -match 'WebView recovery is temporarily paused') {
    throw 'Circuit breaker user-facing text must come from StringResources.'
}
```

- [ ] **Step 4: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/ViewModels/MainViewModel.Commands.cs src/OpenClaw/Strings/en-us/Resources.resw src/OpenClaw/Strings/zh-cn/Resources.resw tools/verify-repo-structure.ps1
git commit -m "fix: localize circuit breaker recovery message"
```

---

### Task 7D: Inject Logger Into HostedUiBridge

**Files:**
- Modify: `src/OpenClaw/Services/HostedUiBridge.cs`
- Modify: `src/OpenClaw/ViewModels/MainViewModel.Fields.cs`
- Modify: `tools/verify-repo-structure.ps1`

- [ ] **Step 1: Add constructor logger dependency**

In `HostedUiBridge.cs`, add a constructor and logger field:

```csharp
private readonly IAppLogger _logger;

public HostedUiBridge(IAppLogger logger)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
}
```

Replace every `App.Logger.Info`, `App.Logger.Warning`, and `App.Logger.Error` inside `HostedUiBridge.cs` with `_logger.Info`, `_logger.Warning`, and `_logger.Error`.

- [ ] **Step 2: Construct the bridge from the ViewModel logger**

In `MainViewModel.Fields.cs`, replace the field initializer:

```csharp
private readonly HostedUiBridge _hostedUiBridge = new();
```

with:

```csharp
private readonly HostedUiBridge _hostedUiBridge;
```

Then initialize it in the `MainViewModel(IAppLogger logger, Action<Action>? dispatchToUi = null)` constructor that Task 7B owns:

```csharp
_hostedUiBridge = new HostedUiBridge(logger);
```

- [ ] **Step 3: Add guardrail**

Extend `tools/verify-repo-structure.ps1`:

```powershell
$hostedBridge = Get-Content -LiteralPath (Join-Path $repoRoot 'src/OpenClaw/Services/HostedUiBridge.cs') -Raw
if ($hostedBridge -match 'App\.Logger') {
    throw 'HostedUiBridge must use injected IAppLogger, not App.Logger.'
}
```

- [ ] **Step 4: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Services/HostedUiBridge.cs src/OpenClaw/ViewModels/MainViewModel.Fields.cs tools/verify-repo-structure.ps1
git commit -m "refactor: inject hosted bridge logger"
```

---

### Task 8: Make Log Viewer Loading Cancellable

**Files:**
- Modify: `src/OpenClaw/Views/LogViewerDialog.xaml.cs`
- Modify: `src/OpenClaw.Core/Helpers/LogFileUtilities.cs`

- [ ] **Step 1: Add dialog cancellation**

Add fields to `LogViewerDialog`:

```csharp
private CancellationTokenSource? _loadCts;
```

Add cleanup:

```csharp
private void CancelPendingLoad()
{
    _loadCts?.Cancel();
    _loadCts?.Dispose();
    _loadCts = null;
}
```

Subscribe `Closed` and cancel:

```csharp
Closed += (_, _) => CancelPendingLoad();
```

- [ ] **Step 2: Guard refresh ownership**

Change `LoadTodayLogAsync` to create one owner token per refresh and to ignore cancellation without showing an error:

```csharp
private async Task LoadTodayLogAsync()
{
    CancelPendingLoad();
    _loadCts = new CancellationTokenSource();
    var token = _loadCts.Token;

    try
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var logFile = Path.Combine(_logDirectory, $"openclaw-{today}.log");
        LogFileLabel.Text = string.Format(StringResources.LogFileLabelFormat, $"openclaw-{today}.log");

        if (!File.Exists(logFile))
        {
            token.ThrowIfCancellationRequested();
            LogContent.Text = StringResources.LogNotFoundToday;
            return;
        }

        var tail = await Task.Run(
            () => LogFileUtilities.ReadLastLines(logFile, LogFileUtilities.DefaultTailLineCount, token),
            token);

        token.ThrowIfCancellationRequested();

        var content = string.Join(Environment.NewLine, tail.Lines);
        LogContent.Text = tail.WasTruncated
            ? string.Format(StringResources.LogShowingLastLinesFormat, tail.TotalLineCount) + Environment.NewLine + Environment.NewLine + content
            : content;
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
        LogContent.Text = string.Format(StringResources.LogReadFailedFormat, ex.Message);
    }
}
```

- [ ] **Step 3: Add cancellation to log utility**

Add overload:

```csharp
public static LogTailResult ReadLastLines(string path, int maxLines, CancellationToken cancellationToken)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentOutOfRangeException.ThrowIfNegative(maxLines);
    cancellationToken.ThrowIfCancellationRequested();

    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096);
    if (stream.Length == 0)
    {
        return new LogTailResult([], 0);
    }

    stream.Seek(0, SeekOrigin.End);
    var lastByte = ReadByteAt(stream, stream.Length - 1);
    var buffer = new byte[4096];
    var tailBytes = new List<byte>();
    var position = stream.Length;
    var totalNewLines = 0;
    var capturedNewLines = 0;
    var shouldCaptureTail = maxLines > 0;

    while (position > 0)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var readSize = (int)Math.Min(buffer.Length, position);
        position -= readSize;
        stream.Seek(position, SeekOrigin.Begin);
        var bytesRead = stream.Read(buffer, 0, readSize);

        for (var i = bytesRead - 1; i >= 0; i--)
        {
            var value = buffer[i];
            if (value == (byte)'\n')
            {
                totalNewLines++;
                if (shouldCaptureTail)
                {
                    capturedNewLines++;
                    if (capturedNewLines > maxLines)
                    {
                        shouldCaptureTail = false;
                        continue;
                    }
                }
            }

            if (shouldCaptureTail)
            {
                tailBytes.Add(value);
            }
        }
    }

    var totalLineCount = totalNewLines + (lastByte == (byte)'\n' ? 0 : 1);
    if (maxLines == 0)
    {
        return new LogTailResult([], totalLineCount);
    }

    tailBytes.Reverse();
    var text = Encoding.UTF8.GetString(tailBytes.ToArray()).Replace("\r\n", "\n");
    var lines = text.Split('\n').ToList();
    if (lines.Count > 0 && lines[^1].Length == 0)
    {
        lines.RemoveAt(lines.Count - 1);
    }

    if (lines.Count > maxLines)
    {
        lines = lines.Skip(lines.Count - maxLines).ToList();
    }

    return new LogTailResult(lines, totalLineCount);
}
```

Keep the existing overload:

```csharp
public static LogTailResult ReadLastLines(string path, int maxLines)
{
    return ReadLastLines(path, maxLines, CancellationToken.None);
}
```

- [ ] **Step 4: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add src/OpenClaw/Views/LogViewerDialog.xaml.cs src/OpenClaw.Core/Helpers/LogFileUtilities.cs
git commit -m "refactor: make log viewer tail loading cancellable"
```

---

### Task 9: Update README Architecture And Current Limitations

**Files:**
- Modify: `README.md`
- Modify: `readme_zh.md`
- Modify: `DEVELOPMENT_NOTES.md`
- Modify: `changelog.md`
- Modify: `docs/code-style.md`

- [ ] **Step 1: Replace the outdated README architecture diagram**

Use this English diagram:

```text
MainWindow
|- MainViewModel
|  |- WebViewService
|  |  |- WebViewStatusInspector
|  |  |- HeartbeatRuntime
|  |  `- WebView profile/command helpers
|  |- HostedUiBridge
|  |  `- embedded bridge JS assets
|  |- ShellSessionCoordinator adapters
|  `- ControlUiLatencyService
`- OpenClaw.Core
   |- settings/configuration
   |- recovery policy/state
   |- diagnostics/log utilities
   `- parser/policy helpers
```

Use the equivalent Chinese diagram in `readme_zh.md`.

- [ ] **Step 2: Add active verification section**

State that `tests/` is intentionally absent at this checkpoint and list the active verification commands from Task 1.

- [ ] **Step 3: Reconcile development notes**

Add a short section near the top of `DEVELOPMENT_NOTES.md`:

```markdown
## Active Verification After Test Harness Removal

Older notes mention regression tests that existed in previous checkpoints. Current active verification is:

- solution restore/build/format
- repository structure guardrails
- bridge script behavior checks
- whitespace diff checks
- VS2026 manual debug on real WebView2/Gateway behavior

When a note says "regression coverage now checks", read it as historical context unless the current verification section lists an active command for it.
```

- [ ] **Step 4: Update changelog**

Add a new unreleased entry:

```markdown
### Unreleased

- Planned a second-pass architecture hardening pass after reviewing the v3.3.6 cleanup against README and development-note commitments.
- Documented the no-`tests/` verification replacement: repo-structure guardrails and bridge script checks.
```

- [ ] **Step 5: Run verification and commit**

Run full verification from Task 1.

Commit:

```powershell
git add README.md readme_zh.md DEVELOPMENT_NOTES.md changelog.md docs/code-style.md
git commit -m "docs: align architecture notes with active refactor plan"
```

---

### Task 10: Final Integration And VS2026 Debug Checklist

**Files:**
- Modify only files needed for version/changelog if user asks for a version bump after implementation.

- [ ] **Step 1: Run full machine verification**

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
powershell -ExecutionPolicy Bypass -File tools\verify-repo-structure.ps1
powershell -ExecutionPolicy Bypass -File tools\verify-bridge-scripts.ps1
git diff --check
```

- [ ] **Step 2: Clean Debug outputs generated by verification**

Find Debug directories:

```powershell
Get-ChildItem -Path . -Directory -Recurse -Force -Filter Debug | Where-Object { $_.FullName -notmatch '\\.git(\\|$)' } | Select-Object -ExpandProperty FullName
```

Delete only verified `Debug` output directories. Keep Release folders.

- [ ] **Step 3: Run VS2026 manual debug checklist**

```text
1. Start app in VS2026 Debug.
2. Confirm selected environment loads.
3. Submit a hosted OpenClaw task and observe output stream.
4. Wait for completion without manual reload.
5. Confirm MODEL field is non-empty after startup, session switch, and page reload.
6. Open Settings/Cron/heavy pages and watch WebView2 CPU behavior.
7. Change global hotkey in Settings and confirm it works without restart.
8. Toggle Always on Top in Settings and confirm current window state changes immediately.
9. Enter and exit compact mode at 480px and confirm top bar does not clip.
10. Open log viewer, refresh repeatedly, close during loading, and confirm no UI hang.
11. Use Reload and Stop commands against the hosted UI.
12. Confirm tray show/hide and close-to-tray still work.
```

- [ ] **Step 4: Final status check**

```powershell
git status --short --branch -uall
```

- [ ] **Step 5: Commit final integration**

```powershell
git add .
git commit -m "refactor: complete second-pass architecture hardening"
```

---

## Completion Criteria

- `tests/` remains absent unless the user explicitly changes that decision.
- `OpenClaw.Core` remains in the solution and has no WinUI/WebView2 direct dependency.
- `WebViewService` no longer owns status inspection internals or heartbeat loop lifetime directly.
- `HostedUiBridge.Script.js` becomes a composition shell under 250 lines, with focused embedded JS assets for MODEL, status inspection, command dispatch, mutation filtering, and host messaging.
- `WebViewService` and `HostedUiBridge` use injected `IAppLogger` instead of static `App.Logger`.
- Settings live behavior is applied from a typed change pipeline instead of re-reading global config in multiple places.
- Compact mode uses XAML visual states for top-bar layout.
- Documentation clearly distinguishes historical removed harness coverage from active verification.
- Full verification passes and VS2026 manual debug checklist has no blocking regression.

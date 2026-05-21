# P2 Architecture Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the remaining P2 code-architecture cleanup by splitting the largest WebView responsibilities and safely migrating pure Core files out of the WinUI project tree.

**Status:** Implemented and verified through Task 4. Follow-up cleanup moved `WindowBoundsUtilities.cs` into `src/OpenClaw.Core`, so no linked Core source exception remains.

**Architecture:** Keep runtime behavior unchanged and use source-level regression tests as guardrails for refactors the executable harness cannot exercise directly. Split `WebViewService` by responsibility before attempting file moves, then migrate Core-compatible files in a staged pass that updates project files, tests, and docs together. Defer raw JavaScript asset extraction for `HostedUiBridge.Script.cs` because it requires packaging and localized-string handling.

**Tech Stack:** WinUI 3, C# 13/.NET 10, WebView2, executable `OpenClaw.Tests` harness, SDK-style project files, `dotnet format`.

---

## File Structure

- `src/OpenClaw/Services/WebViewService.cs`: Keep lifecycle, navigation, WebView2 event wiring, and disposal.
- `src/OpenClaw/Services/WebViewService.Heartbeat.cs`: Own heartbeat fields, heartbeat public API, gateway transport probing, hosted-session probing, reload scheduling, and heartbeat observation logging.
- `src/OpenClaw/Services/WebViewService.ControlUiInspection.cs`: Own Control UI inspection fields, status probe loop, snapshot application, inspection script execution, JSON parsing, and inspection counters.
- `src/OpenClaw/Services/WebViewService.Commands.cs`: Keep stop and abort command helpers.
- `src/OpenClaw/Services/WebViewService.Profile.cs`: Keep profile-folder helpers.
  - `src/OpenClaw.Core/*`: Become the physical home for Core-compatible files that are currently linked from `src/OpenClaw`.
- `src/OpenClaw/OpenClaw.csproj`: Remove app compilation for files that physically move to Core and keep WinUI-only files local.
- `src/OpenClaw.Core/OpenClaw.Core.csproj`: Compile moved Core files directly instead of linking them from the WinUI project tree.
- `tests/OpenClaw.Tests/Program.cs`: Register new guardrail tests only.
- `tests/OpenClaw.Tests/Tests.ShellAndWebView.cs`: Add WebViewService partial-boundary tests.
- `tests/OpenClaw.Tests/Tests.StyleArchitecture.cs`: Update project-architecture and documentation assertions.
- `tests/OpenClaw.Tests/Tests.Settings.cs`: Update source-path assertions after settings-related Core moves.
- `tests/OpenClaw.Tests/Tests.HostedBridge.cs`: Update source-path assertions after session model and coordinator Core moves.

---

### Task 1: Split WebViewService Heartbeat Responsibility

**Files:**
- Create: `src/OpenClaw/Services/WebViewService.Heartbeat.cs`
- Modify: `src/OpenClaw/Services/WebViewService.cs`
- Modify: `tests/OpenClaw.Tests/Program.cs`
- Modify: `tests/OpenClaw.Tests/Tests.ShellAndWebView.cs`

- [x] **Step 1: Write the failing test**

Add this registration to `tests/OpenClaw.Tests/Program.cs` near the existing WebViewService architecture tests:

```csharp
("WebView service heartbeat is split by responsibility", Tests.WebViewServiceHeartbeatIsSplitByResponsibility),
```

Add this test to `tests/OpenClaw.Tests/Tests.ShellAndWebView.cs`:

```csharp
public static Task WebViewServiceHeartbeatIsSplitByResponsibility()
{
    var serviceRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw", "Services");
    var shellPath = Path.Combine(serviceRoot, "WebViewService.cs");
    var heartbeatPath = Path.Combine(serviceRoot, "WebViewService.Heartbeat.cs");

    Assert.True(File.Exists(heartbeatPath), "Heartbeat behavior should live in a focused WebViewService partial.");

    var shell = File.ReadAllText(shellPath);
    var heartbeat = File.ReadAllText(heartbeatPath);
    var combined = ReadWebViewServiceSource();

    Assert.Contains("public partial class WebViewService", heartbeat, "Heartbeat partial should extend WebViewService.");
    Assert.Contains("public void StartHeartbeat", heartbeat, "Heartbeat partial should own heartbeat start.");
    Assert.Contains("public void StopHeartbeat", heartbeat, "Heartbeat partial should own heartbeat stop.");
    Assert.Contains("RunSessionAwareHeartbeatLoopAsync", heartbeat, "Heartbeat partial should own the session-aware loop.");
    Assert.Contains("ObserveHeartbeatShutdownAsync", heartbeat, "Heartbeat partial should observe shutdown.");
    Assert.Contains("ProbeGatewayHealthAsync", heartbeat, "Heartbeat partial should own gateway health probing.");
    Assert.Contains("TryScheduleHeartbeatReload", heartbeat, "Heartbeat partial should own reload scheduling.");
    Assert.Contains("LogHeartbeatObservation", heartbeat, "Heartbeat partial should own observation logging.");
    Assert.DoesNotContain("public void StartHeartbeat", shell, "The lifecycle shell should not own heartbeat start.");
    Assert.DoesNotContain("private async Task RunSessionAwareHeartbeatLoopAsync", shell, "The lifecycle shell should not own the heartbeat loop.");
    Assert.Contains("_heartbeatTask = RunSessionAwareHeartbeatLoopAsync(gatewayUrl, timer, _heartbeatCts.Token);", combined, "Heartbeat loop should still retain its task.");
    Assert.Contains("ObserveHeartbeatShutdownAsync", combined, "Stopping heartbeat should still observe loop completion.");
    return Task.CompletedTask;
}
```

- [x] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: FAIL with the new test reporting that `WebViewService.Heartbeat.cs` does not exist.

- [x] **Step 3: Move heartbeat fields and methods**

Move these members from `src/OpenClaw/Services/WebViewService.cs` into `src/OpenClaw/Services/WebViewService.Heartbeat.cs` without changing method bodies:

```csharp
private const int DefaultHeartbeatFailureThreshold = 3;
private const int DefaultHeartbeatConnectingThreshold = 3;
private int _heartbeatRecoveryRequests;
private CancellationTokenSource? _heartbeatCts;
private Task? _heartbeatTask;
private int _heartbeatFailureCount;
private int _heartbeatConnectingCount;
private string? _lastHeartbeatObservationKey;
private string? _heartbeatGatewayUrl;
private int _heartbeatIntervalSeconds;
private int _heartbeatFailureThreshold = DefaultHeartbeatFailureThreshold;
private int _heartbeatConnectingThreshold = DefaultHeartbeatConnectingThreshold;
private static readonly TimeSpan DefaultHeartbeatReloadCooldown = TimeSpan.FromSeconds(75);
private static readonly HttpClient HeartbeatHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
private DateTimeOffset _lastHeartbeatReloadAt = DateTimeOffset.MinValue;
private string? _lastStartHeartbeatKey;
public event Action<string>? HeartbeatFailed;
public event Action<HeartbeatProbeResult>? HeartbeatObserved;
public int HeartbeatRecoveryRequests => Volatile.Read(ref _heartbeatRecoveryRequests);
public void StartHeartbeat(string gatewayUrl, int intervalSeconds);
public void StopHeartbeat();
private Task ObserveHeartbeatShutdownAsync(Task heartbeatTask, CancellationTokenSource? heartbeatCts);
private Task RunSessionAwareHeartbeatLoopAsync(string gatewayUrl, PeriodicTimer timer, CancellationToken token);
private Task<HeartbeatProbeResult> ProbeGatewayHealthAsync(string url, CancellationToken token);
private static Task<HeartbeatProbeResult> ProbeGatewayTransportAsync(string url, CancellationToken token);
private Task<HeartbeatProbeResult?> ProbeHostedSessionAsync();
private bool TryScheduleHeartbeatReload(string message, bool preserveConnectingCounter = false);
private static TimeSpan GetHeartbeatReloadCooldown();
private void LogHeartbeatObservation(HeartbeatProbeResult result);
```

The new file starts with:

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Net;

namespace OpenClaw.Services;

public partial class WebViewService
{
    // Moved members keep their existing bodies.
}
```

Keep `_heartbeatConnectingCount = 0;` and `_lastHeartbeatObservationKey = null;` references in navigation handlers unchanged because partial classes share private state.

- [x] **Step 4: Run the test and verify GREEN**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: PASS.

---

### Task 2: Split WebViewService Control UI Inspection Responsibility

**Files:**
- Create: `src/OpenClaw/Services/WebViewService.ControlUiInspection.cs`
- Modify: `src/OpenClaw/Services/WebViewService.cs`
- Modify: `tests/OpenClaw.Tests/Program.cs`
- Modify: `tests/OpenClaw.Tests/Tests.ShellAndWebView.cs`
- Modify: `docs/code-style.md`

- [x] **Step 1: Write the failing test**

Add this registration to `tests/OpenClaw.Tests/Program.cs`:

```csharp
("WebView service Control UI inspection is split by responsibility", Tests.WebViewServiceControlUiInspectionIsSplitByResponsibility),
```

Add this test to `tests/OpenClaw.Tests/Tests.ShellAndWebView.cs`:

```csharp
public static Task WebViewServiceControlUiInspectionIsSplitByResponsibility()
{
    var serviceRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw", "Services");
    var shellPath = Path.Combine(serviceRoot, "WebViewService.cs");
    var inspectionPath = Path.Combine(serviceRoot, "WebViewService.ControlUiInspection.cs");

    Assert.True(File.Exists(inspectionPath), "Control UI inspection should live in a focused WebViewService partial.");

    var shell = File.ReadAllText(shellPath);
    var inspection = File.ReadAllText(inspectionPath);
    var combined = ReadWebViewServiceSource();

    Assert.Contains("public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync()", inspection, "Inspection partial should own the public inspection API.");
    Assert.Contains("ProbeControlUiStateAfterNavigationAsync", inspection, "Inspection partial should own the post-navigation probe loop.");
    Assert.Contains("ApplyControlUiSnapshot", inspection, "Inspection partial should own snapshot application.");
    Assert.Contains("ExecuteControlUiInspectionAsync", inspection, "Inspection partial should own WebView script execution.");
    Assert.Contains("ParseControlUiSnapshot", inspection, "Inspection partial should own snapshot JSON parsing.");
    Assert.Contains("ShouldLogInspectionInstrumentationCount", inspection, "Inspection partial should own instrumentation throttling.");
    Assert.DoesNotContain("public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync()", shell, "The lifecycle shell should not own inspection API.");
    Assert.DoesNotContain("private static ControlUiProbeSnapshot ParseControlUiSnapshot", shell, "The lifecycle shell should not own snapshot parsing.");
    Assert.Contains("InspectControlUiStateAsync(token, generation)", combined, "Status probes should still call the generation-aware inspection API.");
    Assert.Contains("IsCurrentGeneration(generation)", combined, "Inspection should still discard stale generations.");
    return Task.CompletedTask;
}
```

- [x] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: FAIL with the new test reporting that `WebViewService.ControlUiInspection.cs` does not exist.

- [x] **Step 3: Move inspection fields and methods**

Move these members from `src/OpenClaw/Services/WebViewService.cs` into `src/OpenClaw/Services/WebViewService.ControlUiInspection.cs` without changing behavior:

```csharp
private const string ControlUiStatusMessageKind = "openclaw-control-ui-status";
private CancellationTokenSource? _statusProbeCts;
private readonly object _inspectionGate = new();
private Task<ControlUiProbeSnapshot>? _inFlightInspectionTask;
private int _inFlightInspectionGeneration;
private DateTimeOffset _lastControlUiInspectionAt = DateTimeOffset.MinValue;
private ControlUiProbeSnapshot _latestControlUiSnapshot = ControlUiProbeSnapshot.Unknown;
private string? _lastReportedIssueKey;
private static readonly TimeSpan InspectionReuseWindow = TimeSpan.FromMilliseconds(350);
private int _totalControlUiInspectionRequests;
private int _cachedControlUiInspectionRequests;
private int _coalescedControlUiInspectionRequests;
public event Action<ControlUiProbeSnapshot>? ControlUiSnapshotUpdated;
public ControlUiProbeSnapshot LatestControlUiSnapshot => _latestControlUiSnapshot;
public int TotalControlUiInspectionRequests => Volatile.Read(ref _totalControlUiInspectionRequests);
public int CachedControlUiInspectionRequests => Volatile.Read(ref _cachedControlUiInspectionRequests);
public int CoalescedControlUiInspectionRequests => Volatile.Read(ref _coalescedControlUiInspectionRequests);
public Task<ControlUiProbeSnapshot> InspectControlUiStateAsync();
private Task<ControlUiProbeSnapshot> InspectControlUiStateAsync(CancellationToken token, int generation);
private void StartStatusProbeLoop();
private void CancelStatusProbeLoop();
private Task ProbeControlUiStateAfterNavigationAsync(CancellationToken token, int generation);
private void ApplyControlUiSnapshot(ControlUiProbeSnapshot snapshot, bool raiseIssueEvent);
private Task CompleteControlUiInspectionAsync(CoreWebView2 coreWebView, TaskCompletionSource<ControlUiProbeSnapshot> inspectionSource, CancellationToken token, int generation);
private static bool ShouldLogInspectionInstrumentationCount(int count);
private Task<ControlUiProbeSnapshot> ExecuteControlUiInspectionAsync(CoreWebView2 coreWebView, CancellationToken token, int generation);
private static ControlUiProbeSnapshot ParseControlUiSnapshot(string json);
private static string GetString(JsonElement root, string propertyName);
private static ControlUiPhase ParsePhase(string value);
```

The new file starts with:

```csharp
// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace OpenClaw.Services;

public partial class WebViewService
{
    // Moved members keep their existing bodies.
}
```

Keep calls from lifecycle handlers to `CancelStatusProbeLoop`, `StartStatusProbeLoop`, and `ApplyControlUiSnapshot` unchanged.

- [x] **Step 4: Update code-style documentation**

Change the `WebViewService` partial ownership bullet in `docs/code-style.md` to include lifecycle/navigation shell, heartbeat, Control UI inspection, command injection, and profile-folder helpers.

- [x] **Step 5: Run the test and verify GREEN**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: PASS.

---

### Task 3: Physically Move Core-Compatible Files

**Files:**
- Modify: `src/OpenClaw.Core/OpenClaw.Core.csproj`
- Modify: `src/OpenClaw/OpenClaw.csproj`
- Move selected files from `src/OpenClaw` to `src/OpenClaw.Core`
- Modify: `tests/OpenClaw.Tests/Tests.Settings.cs`
- Modify: `tests/OpenClaw.Tests/Tests.HostedBridge.cs`
- Modify: `tests/OpenClaw.Tests/Tests.ShellAndWebView.cs`
- Modify: `tests/OpenClaw.Tests/Tests.StyleArchitecture.cs`
- Modify: `docs/code-style.md`
- Modify: `README.md`
- Modify: `readme_zh.md`
- Modify: `DEVELOPMENT_NOTES.md`

- [x] **Step 1: Write the failing test**

Update architecture tests so they assert pure Core files live under `src/OpenClaw.Core` and are no longer linked from `src/OpenClaw`:

```csharp
var coreRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw.Core");
Assert.True(File.Exists(Path.Combine(coreRoot, "Models", "AppSettings.cs")), "AppSettings should physically live in OpenClaw.Core.");
Assert.True(File.Exists(Path.Combine(coreRoot, "Services", "SingleInstanceCoordinator.cs")), "SingleInstanceCoordinator should physically live in OpenClaw.Core.");
Assert.True(File.Exists(Path.Combine(coreRoot, "Services", "SessionProbeModels.cs")), "SessionProbeModels should physically live in OpenClaw.Core.");
Assert.DoesNotContain(@"..\OpenClaw\Services\SessionProbeModels.cs", File.ReadAllText(Path.Combine(coreRoot, "OpenClaw.Core.csproj")), "Moved Core files should compile directly.");
```

Update every path-based test that currently reads these files from `src/OpenClaw` so it reads from `src/OpenClaw.Core`:

```csharp
Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw.Core", "Models", "AppSettings.cs")
Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw.Core", "Services", "SingleInstanceCoordinator.cs")
Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw.Core", "Services", "SessionProbeModels.cs")
Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw.Core", "Services", "ShellSessionCoordinator.StateEffects.cs")
Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw.Core", "Services", "ShellSessionCoordinator.EventHandlers.cs")
Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw.Core", "Services", "ShellSessionCoordinator.cs")
Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenClaw.Core", "Helpers", "LogFileUtilities.cs")
```

- [x] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: FAIL because files still physically live under `src/OpenClaw` and the Core project still uses linked compile entries.

- [x] **Step 3: Move the low-risk Core files**

Move the files that have no WinUI or app-only dependency from `src/OpenClaw` to matching folders under `src/OpenClaw.Core`. Keep namespaces unchanged:

```text
src/OpenClaw/Helpers/AtomicFileWriter.cs -> src/OpenClaw.Core/Helpers/AtomicFileWriter.cs
src/OpenClaw/Helpers/LogFileUtilities.cs -> src/OpenClaw.Core/Helpers/LogFileUtilities.cs
src/OpenClaw/Models/AppSettings.cs -> src/OpenClaw.Core/Models/AppSettings.cs
src/OpenClaw/Models/EnvironmentConfig.cs -> src/OpenClaw.Core/Models/EnvironmentConfig.cs
src/OpenClaw/Models/RecoveryModels.cs -> src/OpenClaw.Core/Models/RecoveryModels.cs
src/OpenClaw/Models/RecoveryPolicyOptions.cs -> src/OpenClaw.Core/Models/RecoveryPolicyOptions.cs
src/OpenClaw/Services/AppTelemetry.cs -> src/OpenClaw.Core/Services/AppTelemetry.cs
src/OpenClaw/Services/CloudflareRayParser.cs -> src/OpenClaw.Core/Services/CloudflareRayParser.cs
src/OpenClaw/Services/ConfigurationService.cs -> src/OpenClaw.Core/Services/ConfigurationService.cs
src/OpenClaw/Services/ControlUiLatencyService.cs -> src/OpenClaw.Core/Services/ControlUiLatencyService.cs
src/OpenClaw/Services/DiagnosticBundleService.cs -> src/OpenClaw.Core/Services/DiagnosticBundleService.cs
src/OpenClaw/Services/HotkeyBinding.cs -> src/OpenClaw.Core/Services/HotkeyBinding.cs
src/OpenClaw/Services/IAppLogger.cs -> src/OpenClaw.Core/Services/IAppLogger.cs
src/OpenClaw/Services/LatencyHistory.cs -> src/OpenClaw.Core/Services/LatencyHistory.cs
src/OpenClaw/Services/LoggingService.cs -> src/OpenClaw.Core/Services/LoggingService.cs
src/OpenClaw/Services/SessionProbeModels.cs -> src/OpenClaw.Core/Services/SessionProbeModels.cs
src/OpenClaw/Services/SingleInstanceCoordinator.cs -> src/OpenClaw.Core/Services/SingleInstanceCoordinator.cs
src/OpenClaw/Services/ShellSessionCoordinator*.cs -> src/OpenClaw.Core/Services/ShellSessionCoordinator*.cs, except ShellSessionCoordinator.Adapters.cs
src/OpenClaw/Services/TrayClosePolicy.cs -> src/OpenClaw.Core/Services/TrayClosePolicy.cs
src/OpenClaw/Services/TrayMenuStrings.cs -> src/OpenClaw.Core/Services/TrayMenuStrings.cs
src/OpenClaw/Services/WebViewCircuitBreaker.cs -> src/OpenClaw.Core/Services/WebViewCircuitBreaker.cs
```

Keep `src/OpenClaw/Services/ShellSessionCoordinator.Adapters.cs` in the WinUI project because it owns app-local adapters. `WindowBoundsUtilities.cs` was initially left app-local in this task, then moved to Core in follow-up work after the visibility boundary was split.

- [x] **Step 4: Update project files**

Change `src/OpenClaw.Core/OpenClaw.Core.csproj` so moved files are compiled by default from their physical location. Remove linked `Compile Include="..\OpenClaw\..." Link="..."` entries for moved files.

Change `src/OpenClaw/OpenClaw.csproj` so the app project keeps referencing `OpenClaw.Core` and does not keep stale `Compile Remove` entries for files that no longer live under `src/OpenClaw`.

- [x] **Step 5: Update docs**

In `docs/code-style.md`, replace wording that says linked Core files are the current structure with wording that says the preferred structure is physical files under `src/OpenClaw.Core`; linked files are temporary exceptions only.

In `README.md`, `readme_zh.md`, and `DEVELOPMENT_NOTES.md`, update architecture notes to describe the Core project as the physical home of pure logic.

- [x] **Step 6: Run the test and verify GREEN**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: PASS.

---

### Task 4: Final Formatting And Verification

**Files:**
- Verify all files touched by Tasks 1-3.

- [x] **Step 1: Run format gate**

Run:

```powershell
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
```

Expected: exit code 0.

- [x] **Step 2: Run executable harness**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: all harness tests PASS.

- [x] **Step 3: Run x64 Debug build**

Run:

```powershell
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
```

Expected: build succeeds with 0 warnings and 0 errors.

- [x] **Step 4: Inspect whitespace and scope**

Run:

```powershell
git diff --check
git diff --stat
```

Expected: no whitespace errors. Diff should show focused WebViewService partial splits, Core physical migration, project-file updates, tests, and docs.

---

## Residual Risk

- Do not extract `HostedUiBridge.Script.cs` into a raw JavaScript asset in this plan. That change needs packaging/resource build work and must preserve localized string injection.
- `WindowBoundsUtilities.cs` was moved in follow-up work by keeping WinUI display enumeration in `MainWindow.Lifecycle.cs` and exposing plain Core `WindowBoundsUtilities`/`WindowWorkArea` types.

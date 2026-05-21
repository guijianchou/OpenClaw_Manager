# Code Style Standardization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Implemented and verified for v3.3.5.

**Goal:** Codify OpenClaw's project-specific code style and architecture rules, then make the most visible UI formatting rule enforceable through shared resources and regression tests.

**Architecture:** Keep this pass intentionally low risk: no runtime behavior changes, no broad source-file moves, and no rewrite of the current linked `OpenClaw.Core` layout. The implementation adds project documentation, centralizes repeated top-status typography resources, and extends the existing executable test harness so future patches follow the same rules.

**Tech Stack:** WinUI 3, C# 13/.NET 10, XAML resources, executable `OpenClaw.Tests` harness, `dotnet format`.

---

### Task 1: Document Project Code And Architecture Standards

**Files:**
- Create: `docs/code-style.md`
- Modify: `README.md`
- Modify: `readme_zh.md`
- Modify: `DEVELOPMENT_NOTES.md`
- Test: `tests/OpenClaw.Tests/Program.cs`

- [x] **Step 1: Add a failing documentation regression test**

Add a test entry named `Code style guide documents project conventions`. The test must read `docs/code-style.md`, `README.md`, `readme_zh.md`, and `DEVELOPMENT_NOTES.md`, then assert these exact project rules are documented:

```csharp
Assert.Contains("docs/code-style.md", readme, "README should link the code style guide.");
Assert.Contains("docs/code-style.md", readmeZh, "Chinese README should link the code style guide.");
Assert.Contains("Project Code Standards", developmentNotes, "Development notes should keep the code standards entry point.");
Assert.Contains("MainWindow", guide, "Guide should cover shell partial ownership.");
Assert.Contains("MainViewModel", guide, "Guide should cover view-model partial ownership.");
Assert.Contains("ShellSessionCoordinator", guide, "Guide should cover coordinator partial ownership.");
Assert.Contains("WebViewService", guide, "Guide should warn against growing the largest WebView service further.");
Assert.Contains("HostedUiBridge.Script.cs", guide, "Guide should keep bridge script content behind its script-builder seam.");
Assert.Contains("dotnet format OpenClaw.sln --verify-no-changes --no-restore", guide, "Guide should document the format gate.");
Assert.Contains("dotnet run --project tests\\OpenClaw.Tests\\OpenClaw.Tests.csproj -c Debug --no-restore", guide, "Guide should document the executable harness command.");
```

- [x] **Step 2: Run the new test and verify RED**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: FAIL because `docs/code-style.md` does not exist or the README files do not link it.

- [x] **Step 3: Create `docs/code-style.md`**

Document these concrete rules:

- `.editorconfig` and `.gitattributes` are the formatting source of truth.
- Use file-scoped namespaces, 4-space indentation, LF endings, final newline, no trailing whitespace.
- Keep braces on every branch and loop.
- Keep user-visible strings in resources.
- Use shared XAML resources for status-bar typography and recurring UI constants.
- Keep `MainWindow`, `MainViewModel`, and `ShellSessionCoordinator` partial files split by responsibility.
- Do not add more responsibilities to `WebViewService` or `HostedUiBridge.Script.cs` without extracting a focused helper or partial.
- Pure, platform-independent logic belongs in the `OpenClaw.Core` compilation surface.
- Version bumps must update project metadata, manifests, README, Chinese README, changelog, and tests.
- Verification commands are `dotnet run`, `dotnet format`, and `dotnet build` with x64 Debug.

- [x] **Step 4: Link the guide from README files and development notes**

Add a short "Development standards" link in both README files and make `DEVELOPMENT_NOTES.md` point to `docs/code-style.md` as the canonical checklist.

- [x] **Step 5: Run the test and verify GREEN**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: PASS.

---

### Task 2: Centralize Top Status Typography Resources

**Files:**
- Modify: `src/OpenClaw/App.xaml`
- Modify: `src/OpenClaw/MainWindow.xaml`
- Test: `tests/OpenClaw.Tests/Program.cs`

- [x] **Step 1: Add a failing XAML resource regression test**

Add a test entry named `Top status typography uses shared resources`. The test must assert:

```csharp
Assert.Contains("x:Double x:Key=\"TopStatusLabelFontSize\"", appXaml, "App resources should define top status label font size.");
Assert.Contains("x:Double x:Key=\"TopStatusValueFontSize\"", appXaml, "App resources should define top status value font size.");
Assert.Contains("x:Int32 x:Key=\"TopStatusLabelCharacterSpacing\"", appXaml, "App resources should define top status label character spacing.");
Assert.Contains("x:Int32 x:Key=\"TopStatusValueCharacterSpacing\"", appXaml, "App resources should define top status value character spacing.");
Assert.Contains("FontSize=\"{StaticResource TopStatusLabelFontSize}\"", mainWindowXaml, "Top status labels should use shared label font size.");
Assert.Contains("FontSize=\"{StaticResource TopStatusValueFontSize}\"", mainWindowXaml, "Top status values should use shared value font size.");
Assert.DoesNotContain("FontSize=\"12\"", ExtractTopStatusPillXaml(mainWindowXaml), "Top status pill should not hard-code the model value font size.");
```

If no helper exists, implement a local test helper that extracts the substring from `x:Name="TopStatusPill"` through the next `</Border>` after that marker.

- [x] **Step 2: Run the new test and verify RED**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: FAIL because `MainWindow.xaml` currently hard-codes top-status font sizes and character spacing.

- [x] **Step 3: Add shared resources to `App.xaml`**

Add the shared resources in the app-level resource dictionary:

```xml
<x:Double x:Key="TopStatusLabelFontSize">10</x:Double>
<x:Double x:Key="TopStatusValueFontSize">10</x:Double>
<x:Int32 x:Key="TopStatusLabelCharacterSpacing">10</x:Int32>
<x:Int32 x:Key="TopStatusValueCharacterSpacing">10</x:Int32>
<FontWeight x:Key="TopStatusFontWeight">SemiBold</FontWeight>
```

- [x] **Step 4: Replace top-status hard-coded typography in `MainWindow.xaml`**

Inside `TopStatusPill`, use the shared resources for heartbeat text, model label, model value, access text, status label, and status value. Preserve existing bindings, layout, brushes, and trimming.

- [x] **Step 5: Run the test and verify GREEN**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: PASS.

---

### Task 3: Add Architecture Guardrails Without Moving Files

**Files:**
- Modify: `docs/code-style.md`
- Test: `tests/OpenClaw.Tests/Program.cs`

- [x] **Step 1: Add a failing architecture guardrail test**

Add a test entry named `Architecture guide preserves current module boundaries`. The test must assert `docs/code-style.md` contains:

```csharp
Assert.Contains("WinUI layer", guide, "Guide should name the WinUI layer boundary.");
Assert.Contains("Core compilation surface", guide, "Guide should name the Core compilation boundary.");
Assert.Contains("Do not move linked Core files casually", guide, "Guide should warn against risky Core file moves.");
Assert.Contains("partial files are split by responsibility", guide, "Guide should describe partial-file ownership.");
Assert.Contains("new protocol or parser code starts in Core-compatible files", guide, "Guide should route pure protocol/parser code to Core-compatible files.");
```

- [x] **Step 2: Run the new test and verify RED**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: FAIL until the guide includes the architecture boundary language.

- [x] **Step 3: Extend `docs/code-style.md` with architecture boundaries**

Add a concise architecture section covering:

- WinUI layer owns XAML, windows, WebView2 controls, and Windows integration.
- Core compilation surface owns pure settings, diagnostics formatting, parser, policy, telemetry, and recovery logic.
- Linked Core files are current project structure; do not move them casually because the WinUI project excludes them from direct compilation and `OpenClaw.Core` links them.
- New protocol/parser/policy code should start in Core-compatible files without Microsoft.UI dependencies.

- [x] **Step 4: Run the test and verify GREEN**

Run:

```powershell
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
```

Expected: PASS.

---

### Task 4: Final Formatting And Verification

**Files:**
- Modify only files touched by Tasks 1-3.

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

- [x] **Step 4: Inspect git diff**

Run:

```powershell
git diff --stat
git diff --check
```

Expected: only docs, XAML resource, and harness test changes; no whitespace errors.

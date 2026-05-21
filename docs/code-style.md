# OpenClaw Code Style

This guide is the project-specific layer on top of `.editorconfig`, `.gitattributes`, .NET analyzers, and WinUI conventions. Keep code small, explicit, and boring.

## Formatting

- `.editorconfig` and `.gitattributes` are the source of truth for formatting.
- Use UTF-8, LF line endings, one final newline, and no trailing whitespace.
- Use 4-space indentation for C#, XAML, project files, resources, manifests, and JSON.
- Prefer file-scoped namespaces.
- Keep braces on every `if`, loop, and branch.
- Keep nullable analysis enabled and address warnings before commit.
- Run `dotnet format OpenClaw.sln --verify-no-changes --no-restore` with `Platform=x64` before handoff.

## C# Rules

- Prefer clear control flow over clever expressions.
- Use `var` only for built-in types or when the right-hand side makes the type obvious.
- Keep comments sparse and useful. Explain ownership, lifecycle, or non-obvious platform behavior.
- Keep user-visible strings in `StringResources` and `.resw` files. Diagnostic-only text and protocol/status tokens may stay close to the code that emits them.
- Prefer structured logs with stable event keys and context objects for state transitions.
- Background work owns its lifetime explicitly: store the `Task`, store cancellation state, and observe shutdown exceptions.
- WebView/CoreWebView2 async work must carry cancellation and generation ownership before applying state after an `await`.

## XAML Rules

- Shared visual constants live in focused dictionaries under `src/OpenClaw/Styles/` and are merged from `App.xaml`.
- Repeated status-bar typography, sizing, and spacing use semantic resources or styles, not repeated literals.
- Prefer `ThemeResource`, WinUI system brushes, and app resources over hard-coded theme colors.
- Settings boolean rows use CommunityToolkit `SettingsCard` plus right-aligned `ToggleSwitch`.
- Keep XAML responsible for layout and binding. Put state transitions and behavior in the view model or service layer.

## Architecture Boundaries

- The WinUI layer owns XAML, windows, WebView2 controls, title-bar behavior, tray/hotkey integration, and app-only adapters.
- The Core physical source tree (`src/OpenClaw.Core`) owns pure settings, diagnostics formatting, parser, policy, telemetry, and recovery logic.
- Define "Core" as WinUI-free. Core-compatible files must not reference `Microsoft.UI`, `Microsoft.Web.WebView2`, XAML types, Windows App SDK packages, or `App`.
- Core-compatible files physically live under `src/OpenClaw.Core`; do not add linked Core source files unless a migration plan explicitly scopes a short-lived transition.
- There are no current linked Core source exceptions. WinUI adapters should convert platform objects into plain Core types at the app boundary.
- New protocol or parser code starts in Core-compatible files unless it directly needs WinUI/WebView2 APIs.
- For guardrail tests, keep this contract explicit: new protocol or parser code starts in Core-compatible files.

## Partial Ownership

- `MainWindow` partial files are split by responsibility: lifecycle, initialization, commands, WebView host recreation, tray, hotkey, compact mode, always-on-top, and theme.
- `MainViewModel` partial files are split by responsibility: fields, bindable properties, commands, environment selection, lifecycle, status formatting, heartbeat, indicators, and telemetry.
- `ShellSessionCoordinator` partial files are split by responsibility: dependency interfaces, attach/dispose, event routing, recovery, recovery inspection, recovery state transitions, state effects, host visibility, helpers, and telemetry.
- `WebViewService` partial files are split by responsibility: lifecycle/navigation shell, heartbeat, Control UI inspection, command injection, and profile-folder helpers.
- New partial files are acceptable only when partial files are split by responsibility. Do not create catch-all "misc" or one-feature dumping grounds.

## Large File Rules

- Do not grow `WebViewService` with unrelated responsibilities. Existing command and profile helpers live in focused partials; add new focused partials for future WebView lifecycle, inspection, or recovery behavior.
- Keep native bridge orchestration in `HostedUiBridge` and script content behind the `HostedUiBridge.Script.cs` script-builder seam.
- Treat `HostedUiBridge.Script.cs` and embedded bridge JS assets as testable script surfaces. Add executable JS behavior tests before changing model detection, mutation filtering, session-ready events, or command handling.
- Avoid large source moves unless the move itself is the purpose of the change and the test plan proves no project-file duplication changed.

## Tests

- `OpenClaw.Tests` is an executable harness. `Program.cs` only owns registration and execution; test implementations live in focused `Tests.*.cs` domain files.
- Run the harness with `dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore`.
- Keep the `dotnet test` guard in the test project because `dotnet test` can otherwise report a false green result for this harness.
- Prefer behavior tests against Core services and fakes.
- Use source-text assertions only for contracts the harness cannot execute, such as XAML resource usage, project metadata, and platform integration declarations.
- Every bug fix or behavior change needs a regression test that would have failed before the change.

## Version And Documentation

- Version bumps update `OpenClaw.csproj`, package manifest, application manifest, README, Chinese README, changelog, and regression-test expectations.
- README files should summarize current behavior and link to deeper docs instead of duplicating implementation notes.
- `DEVELOPMENT_NOTES.md` records debugging history and project lessons. This guide is the canonical checklist for new changes.

## Verification

Run these commands before handing off code:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
git diff --check
```

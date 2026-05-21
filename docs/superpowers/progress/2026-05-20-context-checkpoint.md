# Context Checkpoint - 2026-05-20

## Workspace

- Branch: `codex/code-style-standardization`
- Base HEAD: `211bb11 Fix About GitHub profile label`
- Current state: uncommitted working tree with v3.3.5/style/P2/P3 cleanup changes.
- Do not assume the working tree is clean. Do not revert unrelated changes.

## Completed In Current Uncommitted Work

- Version metadata is at `3.3.5`.
- README, Chinese README, changelog, and development notes were updated for v3.3.5 and current architecture.
- `docs/code-style.md` exists as the canonical project code-style and architecture guide.
- WinUI status resources were split under `src/OpenClaw/Styles`.
- Executable test harness was split from one large `Program.cs` into focused `Tests.*.cs` files.
- `WebViewService` responsibilities were split into focused partials:
  - `WebViewService.Heartbeat.cs`
  - `WebViewService.ControlUiInspection.cs`
  - `WebViewService.Commands.cs`
  - `WebViewService.Profile.cs`
- P1/P2 issue list status:
  - Runtime settings live apply: implemented and tested.
  - Compact top bar layout: implemented and tested.
  - WebView status probe stale generation guard: implemented and tested.
  - Heartbeat loop task/timer ownership: implemented and tested.
  - Log viewer async tail loading: implemented and tested.
  - `IDisposable` contracts: implemented and tested.
- Core cleanup:
  - Core-compatible files physically live under `src/OpenClaw.Core`.
  - `WindowBoundsUtilities.cs` was moved to `src/OpenClaw.Core/Helpers/WindowBoundsUtilities.cs`.
  - There are no current linked Core source exceptions.
- MODEL/bridge cleanup:
  - Hosted MODEL app-state resolver was extracted to `src/OpenClaw/Services/HostedUiBridge.ModelResolver.js`.
  - `HostedUiBridge.Script.cs` embeds that JS asset and delegates app-state MODEL resolution to it.
  - Tests use Jint to execute real JS resolver cases for defaults, `null` overrides, Map overrides, and object-shaped payloads.

## Last Known Verification

These commands passed after the latest MODEL resolver and Core move changes:

```powershell
dotnet restore OpenClaw.sln --locked-mode
dotnet run --project tests\OpenClaw.Tests\OpenClaw.Tests.csproj -c Debug --no-restore
dotnet build OpenClaw.sln -c Debug -p:Platform=x64 --no-restore
$env:Platform='x64'; dotnet format OpenClaw.sln --verify-no-changes --no-restore
git diff --check
```

Build result was 0 warnings and 0 errors.

## Known Remaining Work

- Re-run verification after any further edits.
- Review the final diff before commit because the working tree contains many staged renames and untracked split files.
- Commit only when the user explicitly asks for a git checkpoint or final commit.

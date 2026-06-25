# AGENTS.md

Project-specific Codex instructions for **InputFlow**.

This repo also follows the user's universal Codex instructions. Do not duplicate those rules here. This file only adds InputFlow-specific guidance, constraints, and handoff references.

## Read First

Before editing, read these files in this order:

1. `AGENTS.md`
2. `HANDOFF.md`
3. `VALIDATION.md`
4. `ARCHITECTURE.md`
5. `ROADMAP.md`
6. `README.md`

Use `HANDOFF.md` for current state and prior decisions, `ROADMAP.md` for future planning, and `VALIDATION.md` for known build/run/test commands.

## Project Identity

InputFlow is a Windows C#/.NET tray utility for smarter input-method switching.

The current validated personal workflow is:

```text
English (Netherlands) / United States-International
<-> Korean Microsoft IME with Hangul/native mode enabled
```

The active implementation is a C#/.NET Windows tray app. Do not replace it with AutoHotkey or another tech stack.

## Project Lead Authorization

The user has made Codex the lead programmer for this project. That is standing authorization to create branches, commit, push, and open pull requests for coherent project work.

Default to branch-and-PR workflow for GitHub writes unless a direct `main` update is explicitly requested. Default PRs should be draft PRs until they are ready for review or merge.

Do not tag releases, publish GitHub releases, or change repository visibility/settings unless explicitly requested.

## Important Project-Specific Safety Rules

### Do Not Blindly Toggle Hangul

Do **not** send the Hangul toggle key as the default safe behavior.

Reason: Windows remembers whether Korean IME was last in Hangul mode or Latin `A` mode. Blindly pressing the Hangul key can turn Hangul off when it was already on.

The current safe path sets Korean IME open/native conversion mode explicitly through Windows IME APIs. Preserve that behavior unless the user explicitly asks for an opt-in unsafe fallback.

### Preserve The Working Input-Switching Behavior

Do not break these known-good behaviors:

```text
English (Netherlands) / US-International -> Korean
Korean -> English (Netherlands) / US-International
Korean target -> Hangul/native mode using setter-style IME API, not blind toggle
```

When changing switching logic, validate manually in Notepad or another simple non-elevated text input target.

### Be Careful With HKL/KLID Behavior

A previous approach using `LoadKeyboardLayout` by KLID caused Windows to activate plain English (United States) / US instead of the intended English (Netherlands) / US-International profile.

Prefer the exact HKL/profile already reported by Windows where possible. Do not casually simplify profile switching by reloading layouts from KLID strings.

### Keep Dependencies Light

This is a small utility. Avoid heavy UI frameworks, background services, installers, cloud features, telemetry, analytics, or account systems unless explicitly requested.

Built-in .NET/Win32/Windows APIs are preferred.

## Current Known Build Approach

Use the app project directly as the primary build target:

```powershell
dotnet build .\InputFlow.App\InputFlow.App.csproj -c Release
```

The local SDK previously created `InputFlow.slnx` rather than `InputFlow.sln`. Do not assume a `.sln` exists unless you verify it.

## Do Not Do In This Repo Unless Explicitly Requested

- Do not replace the C#/.NET implementation.
- Do not add AutoHotkey.
- Do not add a blind Hangul-key toggle to the safe default path.
- Do not introduce telemetry, analytics, ads, cloud sync, or account login.
- Do not force elevated/admin mode as the default.
- Do not delete runtime config/log files from the user's local working folder unless explicitly asked.
- Do not rewrite the app around a large new architecture before stabilizing the current prototype.
- Do not tag or release builds unless the user explicitly asks.

## Expected Codex Workflow For Changes

1. Check `git status` when a local checkout is available.
2. Read the relevant handoff and validation files.
3. Inspect the specific code paths before editing.
4. Make a coherent, focused patch. It does not need to be artificially tiny.
5. Run the smallest relevant validation command from `VALIDATION.md`.
6. Report changed files, validation result, risks, and next steps.

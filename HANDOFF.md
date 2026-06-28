# HANDOFF.md

Current handoff for **InputFlow**.

## Confirmed Project Focus

InputFlow is a Windows tray utility for smarter input-method switching.

The product goal is broader than the original Dutch/Korean workflow. InputFlow should become a polished multilingual input workflow manager for bilingual users and linguistic power users who may use several Windows language profiles, keyboard layouts, and IMEs.

The active project in this repo is only InputFlow. Ignore unrelated prior conversation topics.

## Confirmed Tech Stack

Confirmed from the project work so far:

- Language: C#
- Platform: Windows
- Runtime/framework target: `.NET 8` / `net8.0-windows`
- UI model: Windows tray app, using Windows Forms tray infrastructure
- Interop: Win32/IMM APIs through P/Invoke
- Repo modules:
  - `InputFlow.App`
  - `InputFlow.Core`
  - `InputFlow.Windows`

## Current Known Repo Structure

Expected important paths:

```text
InputFlow.App/
InputFlow.Core/
InputFlow.Windows/
samples/
.github/workflows/
README.md
AGENTS.md
HANDOFF.md
ROADMAP.md
ARCHITECTURE.md
VALIDATION.md
```

Important known files:

```text
InputFlow.App/Program.cs
InputFlow.App/InputFlow.App.csproj
InputFlow.Core/ConfigModels.cs
InputFlow.Core/FileLogger.cs
InputFlow.Core/ILogger.cs
InputFlow.Core/InputFlowManager.cs
InputFlow.Core/InputProfile.cs
InputFlow.Core/InputProfileManager.cs
InputFlow.Windows/InputApis.cs
samples/inputflow.sample.json
.github/workflows/build.yml
```

## Current GitHub State

As of the latest handoff update:

- Documentation baseline was merged into `main` through PR #1.
- Tray/diagnostics/RightAlt foundation was merged into `main` through PR #2.
- PR #2 CI succeeded and produced the `InputFlow-win-x64` artifact.
- The user manually validated the built app and reported that switching works.

## Current Confirmed Behavior

The following workflow was tested during development and reported working:

```text
English (Netherlands) / United States-International -> Korean Microsoft IME
Korean Microsoft IME -> English (Netherlands) / United States-International
Korean target enters Hangul/native mode safely
```

The user also confirmed the optional RightAlt single-key trigger workflow works for their setup.

The safe Hangul mode implementation uses explicit IME open/native conversion mode setting, not a blind Hangul-key toggle.

Manual Edge address-bar testing found an application-specific limitation: the Edge omnibox can silently fail to emit Hangul after switching to Korean even when Windows reports that the Korean HKL is active and IMM reports open/native Hangul mode. The same workflow works in normal page text inputs such as the YouTube search field. Treat this as a documented browser chrome limitation unless a reliable supported API path is identified.

A successful log line from the working approach looked like this:

```text
Default IME window Hangul/native set attempt. Open 0 -> 1, conversion 0 -> 1 requested=1.
```

A later switch while already in Hangul/native mode preserved the mode:

```text
Default IME window Hangul/native set attempt. Open 1 -> 1, conversion 1 -> 1 requested=1.
```

## Important Prior Decisions

### 1. C#/.NET Is The Actual Implementation

An early AutoHotkey direction was rejected as wrong for the project. InputFlow should remain a C#/.NET Windows utility.

### 2. Use Exact Windows-Reported HKL/Profile Handles Where Possible

Loading layouts by KLID caused wrong behavior. Specifically, Windows switched to plain English (United States) / US instead of English (Netherlands) / United States-International.

The switch logic now prefers the exact HKL Windows reported for installed/current profiles and uses KLID matching to distinguish variants where available.

### 3. Preserve The Known Working Fallback Workflow

The initial `lastNonTarget` behavior could remember or return to the wrong English layout.

The known working config uses:

```json
"ReturnBehavior": "alwaysSpecificLayout",
"Fallback": "us-intl"
```

with the fallback profile matching:

```json
"LanguageTag": "en-NL"
```

This should remain supported, even as the product grows toward multiple workflow types.

### 4. Do Not Blindly Send The Hangul Toggle Key

The Hangul key is a toggle. Sending it blindly can disable Hangul when Hangul was already active.

The safe implementation tries setter-style Windows IME APIs instead. Do not add a blind Hangul-key toggle as a default safe path.

An investigation also tried TSF current-language refresh, Hangul conversion reset, and IME owner-window notifications against the Edge address bar. Logs showed TSF success, HKL verification success, and IMM Hangul/native success, but Edge still failed in its address bar. Do not keep layering speculative switching hacks for that case.

### 5. Current Build Should Target The App Project Directly

The SDK on the user's system created `InputFlow.slnx`, not `InputFlow.sln`.

Known-safe build target:

```powershell
dotnet build .\InputFlow.App\InputFlow.App.csproj -c Release
```

GitHub Actions should also build the app project directly unless a proper solution file is verified.

### 6. Single-Key Triggers Are Opt-In And Suppress The Key

RightAlt support was added as an opt-in single-key trigger path through a low-level keyboard hook. When a configured single-key trigger fires, the key is consumed so it does not also perform its normal Windows/app behavior.

This is useful for users who want to replace AltGr/RightAlt behavior with InputFlow, but it must stay explicit and visible because it changes normal typing behavior.

## Current Sample Configuration

The current expected sample config is approximately:

```json
{
  "Version": 1,
  "Startup": false,
  "ShowTrayIcon": true,
  "LogLevel": "Info",
  "Hotkeys": [
    {
      "Name": "Korean toggle",
      "Keys": "Ctrl+Alt+Shift+K",
      "Mode": "toggle",
      "Target": "korean",
      "ReturnBehavior": "alwaysSpecificLayout",
      "Fallback": "us-intl"
    }
  ],
  "Profiles": [
    {
      "Id": "us-intl",
      "Match": {
        "LanguageTag": "en-NL",
        "LayoutNameContains": null,
        "ProfileNameContains": null
      },
      "EnterMode": null
    },
    {
      "Id": "korean",
      "Match": {
        "LanguageTag": "ko-KR",
        "LayoutNameContains": null,
        "ProfileNameContains": null
      },
      "EnterMode": "hangul"
    }
  ],
  "ExcludedProcesses": [
    "mstsc.exe",
    "vmconnect.exe"
  ]
}
```

Single-key trigger configs are supported for keys such as `RightAlt`, but they should stay opt-in and documented as behavior-changing.

## Known Issues And Unfinished Work

### Build/Repo State

Validated recently:

- GitHub Actions build/publish succeeded for PR #2.
- The user manually ran the artifact and confirmed the app worked for the core workflow.

Verify before source changes when working locally:

```powershell
git status
dotnet build .\InputFlow.App\InputFlow.App.csproj -c Release
```

### Functional Limitations

Known or likely limitations:

- No settings UI exists yet.
- Config schema v1 is still shaped around the original toggle/fallback model.
- Multi-profile and multi-workflow behavior is not first-class yet.
- Exact TSF/InputMethodTip profile matching is not implemented.
- More complex multi-language setups are untested.
- Browser address bars and other application-owned chrome fields may not refresh IME composition state even when Windows reports the requested profile and mode as active.
- Elevated foreground apps may not accept input-method switching from non-elevated InputFlow.
- Single-key triggers may not intercept keys for elevated foreground apps unless InputFlow is also elevated.
- App icon/branding may still be placeholder/default.
- Installer/release packaging is not implemented.
- Update check/auto-update UX is not implemented.
- Automated test coverage still needs to grow around config, matching, and workflow logic.

## Risks

### IME Behavior Is Fragile

Windows IME/input switching has many edge cases. Do not simplify working interop code without testing.

### Language Profile Matching Can Regress

English (Netherlands) / US-International and English (United States) / US can be confused if matching is too loose.

### Hangul Mode Must Remain Setter-Style

A blind toggle fallback is risky and should not be used as the default safe mode.

### Elevated Apps

Switching may fail when the foreground target is elevated and InputFlow is not. Do not hide such failures.

### Product Shape Should Not Stay English/Korean-Specific

Future changes should preserve the tested Dutch/Korean workflow while moving the model toward general multilingual workflows.

## Next Recommended Task

Recommended next task: **Config Engine V2 And Workflow Model**.

Reason: settings UI, installer polish, and auto-update all depend on stable product semantics. The next code slice should make configuration versioned, validated, migratable, and expressive enough for multilingual workflows before building UI on top.

Suggested scope:

1. Introduce a v2 config schema with explicit workflows.
2. Add migration from current v1 config.
3. Add last-known-good config handling.
4. Validate config before applying it.
5. Keep the known working v1-style sample behavior available through migration or compatibility.
6. Add tests for config parsing, migration, validation, hotkey parsing, and workflow state transitions.
7. Update sample configs and README after behavior is proven.

## Definition Of Done For Next Task

The next task is done when:

- `dotnet build .\InputFlow.App\InputFlow.App.csproj -c Release` succeeds.
- Existing user config either loads directly or migrates safely.
- Invalid config is rejected with a clear message and does not overwrite the active working config.
- Workflow behavior is represented in core types without Win32 dependencies.
- Tests cover representative two-profile toggle and three-profile cycle behavior.
- The known manual workflow still works:
  - English (Netherlands) / US-International -> Korean + Hangul/native
  - Korean -> English (Netherlands) / US-International
  - Optional RightAlt single-key trigger remains opt-in and functional
- No blind Hangul-toggle fallback is added to the default safe path.
- Any remaining warnings or manual-test limitations are reported honestly.

## Do Not Do For Next Task

Do not:

- Rewrite the Win32 switching engine unless a config/workflow abstraction requires a narrow adapter change.
- Replace the tray app framework.
- Add a full settings UI yet.
- Add an installer yet.
- Add telemetry, analytics, cloud sync, accounts, or monetization.
- Change the default sample away from the known working English/Korean setup unless preserving it as one sample among others.
- Add a default blind Hangul-key toggle fallback.
- Force the app to run as admin by default.

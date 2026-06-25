# HANDOFF.md

Current handoff for **InputFlow**.

## Confirmed Project Focus

InputFlow is a Windows tray utility for smarter input-method switching.

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

## Current Confirmed Behavior

The following workflow was tested during development and reported working:

```text
English (Netherlands) / United States-International -> Korean Microsoft IME
Korean Microsoft IME -> English (Netherlands) / United States-International
Korean target enters Hangul/native mode safely
```

The safe Hangul mode implementation uses explicit IME open/native conversion mode setting, not a blind Hangul-key toggle.

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

The switch logic was changed to use the exact HKL Windows reported for installed/current profiles.

### 3. Prefer Explicit Fallback Over Remembered Previous Layout For This Workflow

The initial `lastNonTarget` behavior could remember or return to the wrong English layout.

The working config uses:

```json
"ReturnBehavior": "alwaysSpecificLayout",
"Fallback": "us-intl"
```

with the fallback profile matching:

```json
"LanguageTag": "en-NL"
```

### 4. Do Not Blindly Send The Hangul Toggle Key

The Hangul key is a toggle. Sending it blindly can disable Hangul when Hangul was already active.

The safe implementation tries setter-style Windows IME APIs instead.

### 5. Current Build Should Target The App Project Directly

The SDK on the user's system created `InputFlow.slnx`, not `InputFlow.sln`.

Known-safe build target:

```powershell
dotnet build .\InputFlow.App\InputFlow.App.csproj -c Release
```

GitHub Actions should also build the app project directly unless a proper solution file is verified.

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

## Known Issues And Unfinished Work

### Build/Repo State

Confirmed earlier:

- The app built successfully.
- The project was pushed to GitHub after fixing local Git/SSH issues.
- A later cleanup pass added docs, sample config, GitHub Actions, `.gitignore`, output rename, and warning cleanup.

Verify before source changes:

```powershell
git status
dotnet build .\InputFlow.App\InputFlow.App.csproj -c Release
```

### Functional Limitations

Known or likely limitations:

- Tray menu is still minimal.
- No settings UI exists yet.
- Config reload may double-trigger from file watcher behavior.
- Hotkey reload can temporarily report "hotkey in use" when duplicate reloads happen.
- Exact TSF profile matching is not implemented.
- More complex multi-language setups are untested.
- Elevated foreground apps may not accept input-method switching from non-elevated InputFlow.
- App icon/branding may still be placeholder/default.
- Installer/release packaging is not implemented.

## Risks

### IME Behavior Is Fragile

Windows IME/input switching has many edge cases. Do not simplify working interop code without testing.

### Language Profile Matching Can Regress

English (Netherlands) / US-International and English (United States) / US can be confused if matching is too loose.

### Hangul Mode Must Remain Setter-Style

A blind toggle fallback is risky and should not be used as the default safe mode.

### Elevated Apps

Switching may fail when the foreground target is elevated and InputFlow is not. Do not hide such failures.

## Next Recommended Task

Recommended next task: **tray menu + diagnostics stabilization**.

This should make the prototype easier to use and debug without changing the core switching logic.

Suggested scope:

1. Add tray menu actions:
   - Open config
   - Open log
   - Reload config
   - Pause/resume
   - Exit
2. Add startup diagnostics logging:
   - app version/startup line
   - config path
   - log path
   - installed input profiles found
   - profile matching decisions for configured profiles
3. Debounce config file watcher reloads to avoid duplicate reloads.
4. Keep existing switching behavior unchanged.

## Definition Of Done For Next Task

The next task is done when:

- `dotnet build .\InputFlow.App\InputFlow.App.csproj -c Release` succeeds.
- The tray menu contains at least:
  - Open config
  - Open log
  - Reload config
  - Pause/resume
  - Exit
- Config reload no longer double-triggers from one file save in normal use.
- Startup log clearly lists installed input profiles and which configured profiles matched.
- Existing manual workflow still works:
  - English (Netherlands) / US-International -> Korean + Hangul/native
  - Korean -> English (Netherlands) / US-International
- No blind Hangul-toggle fallback is added to the default safe path.
- Any remaining warnings or manual-test limitations are reported honestly.

## Do Not Do For Next Task

Do not:

- Rewrite the switching engine.
- Replace the tray app framework.
- Add a settings UI yet.
- Add an installer yet.
- Add telemetry, analytics, cloud sync, accounts, or monetization.
- Change the default hotkey unless the user asks.
- Change the sample config away from the known working English/Korean setup unless preserving it as a sample.
- Add a default blind Hangul-key toggle fallback.
- Force the app to run as admin by default.

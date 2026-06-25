# ARCHITECTURE.md

Architecture notes for **InputFlow**.

This document describes the current observed/projected structure. It should be updated when the implementation changes.

## High-Level Design

InputFlow is split into three logical projects:

```text
InputFlow.App
InputFlow.Core
InputFlow.Windows
```

The intended separation is:

- `InputFlow.App`: tray app, startup, user-facing shell, config loading/reloading hooks
- `InputFlow.Core`: configuration models, profile matching, switching state machine, logging abstractions
- `InputFlow.Windows`: Win32/Windows IME interop through P/Invoke

## InputFlow.App

Expected responsibilities:

- Start the tray application.
- Load `inputflow.json`.
- Create default config if missing.
- Register configured hotkeys.
- Watch config changes.
- Provide tray menu actions.
- Start/stop/pause/reload the manager.
- Write user-facing logs through the core logger.

Known important file:

```text
InputFlow.App/Program.cs
```

Current known limitation:

- Tray menu is minimal and should be expanded.
- Config reload behavior likely needs debounce to avoid duplicate reloads.

## InputFlow.Core

Expected responsibilities:

- Define config models.
- Represent input profiles.
- Enumerate/match available profiles.
- Manage hotkey state.
- Switch to target/fallback profiles.
- Apply target enter mode such as Korean Hangul/native mode.
- Log meaningful diagnostics.

Known important files:

```text
InputFlow.Core/ConfigModels.cs
InputFlow.Core/FileLogger.cs
InputFlow.Core/ILogger.cs
InputFlow.Core/InputFlowManager.cs
InputFlow.Core/InputProfile.cs
InputFlow.Core/InputProfileManager.cs
```

## InputFlow.Windows

Expected responsibilities:

- Contain Win32/IMM P/Invoke declarations.
- Keep platform interop isolated from core logic where practical.

Known important file:

```text
InputFlow.Windows/InputApis.cs
```

Known API areas used or relevant:

- foreground window lookup
- keyboard layout/profile handles
- hotkey registration
- IME context APIs
- default IME window APIs
- IME conversion/open status messages

## Switching Model

The current switching model is profile-based.

A hotkey has:

```text
Target profile
Fallback profile
Return behavior
Optional enter mode
```

For the current English/Korean workflow:

```text
Target: korean
Fallback: us-intl
ReturnBehavior: alwaysSpecificLayout
EnterMode on Korean: hangul
```

## Why Exact HKL/Profile Handling Matters

Windows can expose multiple English layouts and language profiles. A previous approach loaded layouts from KLID and activated the wrong English profile:

```text
Wanted: English (Netherlands) / United States-International
Got:    English (United States) / US
```

The current implementation should preserve the exact HKL/profile handle reported by Windows where possible.

## Hangul/Native Mode Model

Korean Microsoft IME has separate state for:

```text
Korean input profile selected
Hangul/native conversion mode active
```

Switching to Korean does not always mean Hangul mode is active.

Unsafe approach:

```text
Press Hangul key blindly
```

Reason it is unsafe:

```text
The Hangul key toggles. It can turn Hangul off if it was already on.
```

Current safe approach:

1. Switch to Korean profile.
2. Try to set Hangul/native mode via focused/foreground IME context.
3. If no usable context is available, use the default IME window.
4. Send explicit IME open/native conversion mode commands.
5. Do not send a blind toggle in the safe default path.

## Config Model Notes

Known config concepts:

```text
Version
Startup
ShowTrayIcon
LogLevel
Hotkeys
Profiles
ExcludedProcesses
```

Known hotkey fields:

```text
Name
Keys
Mode
Target
ReturnBehavior
Fallback
```

Known profile fields:

```text
Id
Match
EnterMode
```

Known match fields:

```text
LanguageTag
LayoutNameContains
ProfileNameContains
```

## Runtime Files

Runtime files should not be committed by default:

```text
inputflow.json
inputflow.log
publish/
bin/
obj/
```

## Areas Likely Needing Architecture Work Later

- TSF/InputMethodTip matching for more exact profile identity.
- Settings UI and first-run profile picker.
- Cleaner app lifecycle and config reload debounce.
- Better diagnostics abstraction.
- Optional release packaging.

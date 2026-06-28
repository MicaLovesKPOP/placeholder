# InputFlow

InputFlow is a Windows tray app for precise multilingual input-profile workflows.

It is for people who use multiple keyboard layouts, languages, and IMEs and want to switch exactly where they mean to go instead of cycling blindly through every Windows input method.

InputFlow is intended to support workflows such as:

- toggle between two exact profiles, such as Dutch US-International <-> Korean Hangul
- cycle through a named group, such as English -> Japanese -> Korean -> Chinese
- jump directly to a specific layout or IME
- return to the previous profile
- safely enter supported IME modes, starting with Korean Hangul/native mode

## Status

InputFlow is currently a working technical preview.

The validated seed workflow is:

- switch from Dutch (Netherlands) / United States-International to Korean Microsoft IME
- switch from Korean back to Dutch (Netherlands) / United States-International
- set Korean Microsoft IME to Hangul/native mode without blindly toggling the Hangul key

The `v0.1.x` line is a technical preview for that seed workflow and nearby manual-JSON configurations. The next product goal is a first multilingual preview that is useful beyond one seeded setup: first-run setup, installed-profile picking, settings UI, and workflow configuration for regular layouts and IMEs.

See [RELEASE_PLAN.md](RELEASE_PLAN.md) for staged release gates and [ROADMAP.md](ROADMAP.md) for the broader product roadmap.

## What It Is Not

InputFlow is not:

- a translator
- a keyboard layout editor
- a general macro tool
- a replacement for every Windows language feature
- an app that blindly toggles IME modes when state-safe APIs are required

## Requirements

- Windows 10 or Windows 11
- .NET SDK 8 or newer to build
- .NET Desktop Runtime 8 or newer to run framework-dependent builds

## Build

From the repository root:

```powershell
dotnet build .\InputFlow.App\InputFlow.App.csproj -c Release
```

## Test

Core config and workflow validation tests:

```powershell
dotnet run --project .\InputFlow.Core.Tests\InputFlow.Core.Tests.csproj -c Release
```

## Publish

Framework-dependent x64 publish:

```powershell
dotnet publish .\InputFlow.App\InputFlow.App.csproj -c Release -r win-x64 --self-contained false -o .\publish\InputFlow-win-x64
```

## Run

After publishing:

```powershell
.\publish\InputFlow-win-x64\InputFlow.exe
```

If `InputFlow.exe` is not present, inspect the publish folder:

```powershell
Get-ChildItem .\publish\InputFlow-win-x64\*.exe
```

## Releases

Release tags use `v*` versions, such as `v0.1.0` or `v0.2.0`.

Pushing a version tag runs the release workflow, rebuilds and tests the app, publishes a framework-dependent Windows x64 build, packages it as a portable ZIP, and creates a GitHub Release with the ZIP attached.

Every release must pass the stage gates in [RELEASE_PLAN.md](RELEASE_PLAN.md).

## Tray Menu

The tray menu includes:

- Open Config
- Open Log
- Setup Status
- Copy Diagnostics
- Reload Config
- Pause/Resume
- Exit

`Setup Status` opens a window with configured profile health, installed profile options, and workflow readiness. It can add or remap configured profiles to installed Windows profiles, and add, edit, or remove toggle, direct-switch, and cycle workflows.

`Copy Diagnostics` copies a text report with config summary, workflows, installed input profiles, setup profile options, configured-profile match results, and workflow readiness. This is the preferred information to paste into bug reports when a Windows layout or IME does not match as expected.

Configured profile reports include health states:

- `matched`: exactly one installed profile matched the configured criteria.
- `missing`: no installed profile matched.
- `ambiguous`: more than one installed profile matched; InputFlow will not use that profile for runtime switching until the criteria are made more exact.
- `changed`: InputFlow recovered through a compatibility fallback, but the config should be reviewed.

Workflow readiness reports whether each configured workflow is ready or blocked. Blocked workflows include reasons such as missing triggers, missing targets, ambiguous profile matches, or missing profile matches.

Setup profile options show every Windows profile InputFlow can see and which configured profile IDs already point at it.

## Configuration

InputFlow stores runtime files in stable per-user locations:

```text
%APPDATA%\InputFlow\inputflow.json
%LOCALAPPDATA%\InputFlow\inputflow.log
```

On first run after upgrading from an older portable build, InputFlow copies a legacy `inputflow.json` from next to `InputFlow.exe` into `%APPDATA%\InputFlow\inputflow.json` if the per-user config does not already exist. The legacy file is left in place.

A sample configuration is available at:

```text
samples/inputflow.sample.json
```

Config version 2 uses `Workflows`. Existing version 1 configs with `Hotkeys` are migrated in memory when loaded, so existing working configs should continue to run.

On a fresh install, InputFlow creates a starter config from the input profiles Windows reports as installed. That starter config defines profile entries but does not guess which workflow or trigger you want. Add a `Workflows` entry manually or start from `samples/inputflow.sample.json` until the settings UI is available.

The sample Korean toggle uses `RightAlt`. When used as a single-key trigger, InputFlow suppresses that key while running, so it also replaces normal AltGr behavior for layouts that use AltGr. Choose a different trigger if you need AltGr for typing characters.

Triggers are written as one key with optional modifiers, such as `Ctrl+Shift+Space`, `Ctrl+Alt+K`, `F13`, or `RightAlt`. Supported modifiers are `Ctrl`, `Alt`, `Shift`, and `Win`. Supported key names include letters, digits, `F1` through `F24`, common navigation keys, and side-specific modifier keys such as `RightAlt`, `LeftAlt`, `RightCtrl`, and `LeftShift`.

Example toggle workflow:

```json
{
  "Id": "korean-toggle",
  "Name": "Korean toggle",
  "Mode": "toggle",
  "Triggers": [
    { "Keys": "RightAlt" }
  ],
  "Target": "korean",
  "ReturnBehavior": "alwaysSpecificLayout",
  "Fallback": "us-intl"
}
```

Example direct-switch workflow:

```json
{
  "Id": "switch-korean",
  "Name": "Switch to Korean",
  "Mode": "switchTo",
  "Triggers": [
    { "Keys": "Ctrl+Alt+K" }
  ],
  "Target": "korean"
}
```

Example cycle workflow:

```json
{
  "Id": "writing-cycle",
  "Name": "Writing cycle",
  "Mode": "cycle",
  "Triggers": [
    { "Keys": "Ctrl+Shift+Space" }
  ],
  "Targets": ["us-intl", "korean", "japanese"]
}
```

Example profile configuration:

```json
{
  "Id": "us-intl",
  "Match": {
    "LanguageTag": "nl-NL",
    "KLID": null,
    "LayoutNameContains": null,
    "ProfileNameContains": null
  },
  "EnterMode": null
}
```

```json
{
  "Id": "korean",
  "Match": {
    "LanguageTag": "ko-KR",
    "LayoutNameContains": null,
    "ProfileNameContains": null
  },
  "EnterMode": "hangul"
}
```

## Korean Hangul Mode Safety

InputFlow does not blindly press the Hangul toggle key in the normal safe path.

That matters because Windows remembers whether Korean IME was last in Hangul mode or Latin `A` mode. Pressing the Hangul key blindly can accidentally turn Hangul off.

The current implementation sets Korean IME open/native conversion mode through Windows IME APIs instead.

## Browser Address Bars

InputFlow is designed for normal Windows text input fields. Some browser chrome fields, especially the Microsoft Edge address bar, can keep their own IME composition state even after Windows reports that Korean, Hangul/native mode, and the requested keyboard layout are active.

Known tested behavior:

- Normal page text fields can accept Korean Hangul after InputFlow switches into Korean.
- The Edge address bar may silently accept no Hangul characters until the IME is manually toggled to Latin mode, one Latin character is typed, and Hangul mode is re-entered.

This is tracked as a browser/omnibox limitation rather than a supported InputFlow fix path until a reliable state-setting API is identified. InputFlow should not add blind Hangul-key toggles or fake text input as the default workaround.

## Project Structure

```text
InputFlow.App         Tray app and user-facing startup
InputFlow.Core        Config, matching, state machine, logging
InputFlow.Windows     Win32/IME interop
samples               Example configuration files
```

## Known Limitations

- There is no settings window yet.
- First-run setup is not implemented yet.
- There is no installer or auto-update flow yet.
- Hold-to-switch workflow mode is not implemented yet.
- IME mode automation is only intentionally supported for Korean Hangul/native mode so far.
- Browser address bars and other application-owned chrome fields may not refresh IME composition state even when Windows reports the requested profile and mode are active.
- Some Windows language/input setups may require more exact TSF profile matching.
- Elevated apps may not accept input-method changes from a non-elevated InputFlow process.
- Hotkeys already used by Windows or another app cannot be registered.

## License

InputFlow is licensed under the GNU General Public License v3.0. See [LICENSE](LICENSE).

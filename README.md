# InputFlow

InputFlow is a small Windows tray utility for smarter input-method switching.

The current working baseline focuses on an English/Korean workflow:

- switch from English (Netherlands) / United States-International to Korean Microsoft IME
- switch from Korean back to English (Netherlands) / United States-International
- safely set Korean Microsoft IME to Hangul/native mode without blindly toggling the Hangul key

## Status

InputFlow is an early working prototype.

It is currently best treated as a technical preview for the original workflow. More testing, UI polish, and diagnostics are still needed before a broad public release.

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

## Tray Menu

The tray menu includes:

- Open Config
- Open Log
- Copy Diagnostics
- Reload Config
- Pause/Resume
- Exit

`Copy Diagnostics` copies a text report with config summary, workflows, installed input profiles, and configured-profile match results. This is the preferred information to paste into bug reports when a Windows layout or IME does not match as expected.

## Configuration

On first run, InputFlow creates `inputflow.json` next to the executable.

A sample configuration is available at:

```text
samples/inputflow.sample.json
```

Config version 2 uses `Workflows`. Existing version 1 configs with `Hotkeys` are migrated in memory when loaded, so existing working configs should continue to run.

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
    "LanguageTag": "en-NL",
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

## Project Structure

```text
InputFlow.App         Tray app and user-facing startup
InputFlow.Core        Config, matching, state machine, logging
InputFlow.Core.Tests  Package-free core validation tests
InputFlow.Windows     Win32/IME interop
samples               Example configuration files
```

## Known Limitations

- There is no settings window yet.
- Hold-to-switch workflow mode is not implemented yet.
- Some Windows language/input setups may require more exact TSF profile matching.
- Elevated apps may not accept input-method changes from a non-elevated InputFlow process.
- Hotkeys already used by Windows or another app cannot be registered.

## License

No license has been selected yet.

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

## Configuration

On first run, InputFlow creates `inputflow.json` next to the executable.

A sample configuration is available at:

```text
samples/inputflow.sample.json
```

Example hotkey configuration:

```json
{
  "Name": "Korean toggle",
  "Keys": "Ctrl+Alt+Shift+K",
  "Mode": "toggle",
  "Target": "korean",
  "ReturnBehavior": "alwaysSpecificLayout",
  "Fallback": "us-intl"
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
InputFlow.App      Tray app and user-facing startup
InputFlow.Core     Config, matching, state machine, logging
InputFlow.Windows  Win32/IME interop
samples            Example configuration files
```

## Known Limitations

- The tray menu is still minimal.
- There is no settings window yet.
- Config reload may still need debounce/stabilization.
- Some Windows language/input setups may require more exact TSF profile matching.
- Elevated apps may not accept input-method changes from a non-elevated InputFlow process.
- Hotkeys already used by Windows or another app cannot be registered.

## License

No license has been selected yet.

# InputFlow

InputFlow is a small Windows tray utility for smarter input-method switching.

Current working baseline:

- English (Netherlands) / United States-International to Korean
- Korean back to English (Netherlands) / United States-International
- Safe Korean Hangul/native mode setting without blindly toggling the Hangul key

Status: early working prototype.

Requirements:

- Windows 10 or Windows 11
- .NET SDK 8 or newer to build
- .NET Desktop Runtime 8 or newer to run framework-dependent builds

Build:

dotnet build .\InputFlow.App\InputFlow.App.csproj -c Release

Publish:

dotnet publish .\InputFlow.App\InputFlow.App.csproj -c Release -r win-x64 --self-contained false -o .\publish\InputFlow-win-x64

Run:

.\publish\InputFlow-win-x64\InputFlow.exe

Configuration:

InputFlow creates inputflow.json next to the executable on first run.
See samples/inputflow.sample.json for an example config.

Korean Hangul mode safety:

InputFlow does not blindly press the Hangul toggle key in the normal safe path.
Instead, it explicitly sets Korean IME open/native conversion mode through Windows IME APIs.

Project structure:

- InputFlow.App: tray app and user-facing startup
- InputFlow.Core: config, matching, state machine, logging
- InputFlow.Windows: Win32/IME interop
- samples: example configuration files

Known limitations:

- Tray menu is still minimal.
- No settings window yet.
- Some language setups may need more exact TSF profile matching.
- Elevated apps may not accept input-method changes from a non-elevated InputFlow process.
- Hotkeys already used by Windows or another app cannot be registered.
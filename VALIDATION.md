# VALIDATION.md

Known validation, build, publish, and manual test commands for **InputFlow**.

Run commands from the repository root unless stated otherwise.

## Check Repo State

```powershell
git status
```

Do this before meaningful edits when a local checkout is available.

## Primary Build Command

Use the app project directly:

```powershell
dotnet build .\InputFlow.App\InputFlow.App.csproj -c Release
```

This is the safest known build command because the local SDK previously created `InputFlow.slnx` rather than `InputFlow.sln`.

## Core Test Command

Run package-free core config and workflow validation tests:

```powershell
dotnet run --project .\InputFlow.Core.Tests\InputFlow.Core.Tests.csproj -c Release
```

These tests cover config parsing, validation, v1 hotkey migration, v2 workflow validation, profile match diagnostics, and representative cycle workflow rules. They do not replace manual Windows IME testing.

## Publish Command

Framework-dependent Windows x64 publish:

```powershell
dotnet publish .\InputFlow.App\InputFlow.App.csproj -c Release -r win-x64 --self-contained false -o .\publish\InputFlow-win-x64
```

## Check Published Executable

```powershell
Get-ChildItem .\publish\InputFlow-win-x64\*.exe
```

Expected primary executable after the output rename:

```text
InputFlow.exe
```

If the expected file is not present, inspect `InputFlow.App/InputFlow.App.csproj` and the publish output before changing commands.

## Run Published App

```powershell
.\publish\InputFlow-win-x64\InputFlow.exe
```

If this command fails, first list the published `.exe` files:

```powershell
Get-ChildItem .\publish\InputFlow-win-x64\*.exe
```

## Manual Functional Test

Use a simple non-elevated text input target such as Notepad.

Expected workflow:

```text
Start in English (Netherlands) / United States-International
Press Ctrl+Alt+Shift+K or the configured trigger
Expected: Korean Microsoft IME + Hangul/native mode

Press the configured trigger again
Expected: English (Netherlands) / United States-International, or the configured fallback/previous profile
```

Also test:

```text
Start in Korean Latin/A mode if possible
Press the configured trigger from the fallback side
Expected after switching to Korean: Hangul/native mode enabled without blind toggle behavior
```

For v2 workflow changes, also test a direct `switchTo` workflow and a `cycle` workflow when the relevant installed Windows profiles are available.

## Diagnostics Copy Test

From the tray menu, choose:

```text
Copy Diagnostics
```

Expected clipboard content includes:

```text
InputFlow diagnostics
Configured workflows:
Installed input profiles:
Configured profile match reports:
```

When profile matching is involved, inspect the copied report for selected profiles and candidate match reasons. This is the preferred bug-report payload for layout or IME matching problems.

## Log Inspection

The runtime log is expected next to the executable unless implementation changes.

Typical publish location:

```text
publish/InputFlow-win-x64/inputflow.log
```

Useful successful Hangul/native mode log pattern:

```text
Default IME window Hangul/native set attempt. Open 0 -> 1, conversion 0 -> 1 requested=1.
```

Useful already-active pattern:

```text
Default IME window Hangul/native set attempt. Open 1 -> 1, conversion 1 -> 1 requested=1.
```

Profile matching diagnostics should list installed profiles and configured profile match results. Unmatched configured profiles should include per-candidate reasons.

## GitHub Actions

The build workflow should build the app project directly and run core tests:

```powershell
dotnet restore .\InputFlow.App\InputFlow.App.csproj
dotnet restore .\InputFlow.Core.Tests\InputFlow.Core.Tests.csproj
dotnet build .\InputFlow.App\InputFlow.App.csproj -c Release --no-restore
dotnet run --project .\InputFlow.Core.Tests\InputFlow.Core.Tests.csproj -c Release --no-restore
dotnet publish .\InputFlow.App\InputFlow.App.csproj -c Release -r win-x64 --self-contained false -o .\publish\InputFlow-win-x64
```

Do not assume `InputFlow.sln` exists unless verified.

## Release Validation

Every staged GitHub Release must satisfy [RELEASE_PLAN.md](RELEASE_PLAN.md).

Before pushing a release tag:

1. Confirm the target commit is on `main`.
2. Confirm GitHub Actions passed on that commit.
3. Confirm any stage-specific manual Windows tests are complete.
4. Confirm README, ROADMAP, VALIDATION, and release notes describe the actual behavior.
5. Confirm known limitations are not hidden.

Release tags use versions such as:

```text
v0.1.0
v0.2.0
v1.0.0
```

Pushing a tag matching `v*` runs `.github/workflows/release.yml`. That workflow should:

1. Restore dependencies.
2. Build the app.
3. Run core tests.
4. Publish Windows x64 output.
5. Package a portable ZIP named like `InputFlow-v0.2.0-win-x64.zip`.
6. Upload the ZIP as a workflow artifact.
7. Create a GitHub Release with the ZIP attached.

After the release workflow finishes:

1. Download the release ZIP from GitHub Releases.
2. Extract it to a clean folder.
3. Confirm `InputFlow.exe` exists.
4. Run the same manual functional test relevant to the release stage.
5. Use `Copy Diagnostics` and inspect the report if input-profile behavior changed.

## Expected Warning State

Target state:

```text
Build succeeds with 0 warnings.
```

If warnings remain, report them exactly. Do not claim the warning cleanup is complete.

Previously observed warnings included:

```text
CS8625: Cannot convert null literal to non-nullable reference type.
NETSDK1137: WindowsDesktop SDK no longer necessary.
CS0108: ExitThread hides inherited member.
```

These may already be fixed in the latest repo state, but verify with the primary build command.

## Validation After Switching-Code Changes

After any change touching input switching, profile matching, hotkeys, workflows, or IME mode:

1. Run the primary build command.
2. Run the core test command.
3. Publish the app.
4. Start the published app.
5. Test in Notepad:
   - English Netherlands -> Korean + Hangul/native
   - Korean -> English Netherlands
6. Use Copy Diagnostics from the tray menu and inspect the copied report.
7. Inspect `inputflow.log`.

## Validation Limitations

Automated tests for Windows IME behavior are not currently known to exist in this repo.

Manual Windows testing is required for changes to:

- hotkey registration
- workflow behavior against real installed profiles
- input profile switching
- Korean Hangul/native mode
- foreground/elevated app handling
- clipboard diagnostics from the tray process

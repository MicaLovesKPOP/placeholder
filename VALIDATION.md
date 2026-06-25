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
Press Ctrl+Alt+Shift+K
Expected: Korean Microsoft IME + Hangul/native mode

Press Ctrl+Alt+Shift+K again
Expected: English (Netherlands) / United States-International
```

Also test:

```text
Start in Korean Latin/A mode if possible
Press Ctrl+Alt+Shift+K from the fallback side
Expected after switching to Korean: Hangul/native mode enabled without blind toggle behavior
```

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

## GitHub Actions

The workflow should build the app project directly:

```powershell
dotnet restore .\InputFlow.App\InputFlow.App.csproj
dotnet build .\InputFlow.App\InputFlow.App.csproj -c Release --no-restore
```

Do not assume `InputFlow.sln` exists unless verified.

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

After any change touching input switching, profile matching, hotkeys, or IME mode:

1. Run the primary build command.
2. Publish the app.
3. Start the published app.
4. Test in Notepad:
   - English Netherlands -> Korean + Hangul/native
   - Korean -> English Netherlands
5. Inspect `inputflow.log`.

## Validation Limitations

Automated tests for Windows IME behavior are not currently known to exist in this repo.

Manual Windows testing is required for changes to:

- hotkey registration
- input profile switching
- Korean Hangul/native mode
- foreground/elevated app handling

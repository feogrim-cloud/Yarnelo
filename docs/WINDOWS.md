# Windows: installation and updates

**English** · [Русский](WINDOWS.ru.md)

[Yarnelo releases](https://github.com/feogrim-cloud/Yarnelo/releases) provide Windows executables and portable archives. Windows 10 and Windows 11 use the same compatible x64 build; separate packages for each Windows feature update are not needed.

| System | Recommended file | Portable option |
| --- | --- | --- |
| Windows 10 / 11, Intel or AMD, 64-bit | `Yarnelo-Windows-x64-Setup.exe` | `Yarnelo-Windows-x64-Portable.zip` |
| Windows 10, 32-bit | `Yarnelo-Windows-x86-Setup.exe` | `Yarnelo-Windows-x86-Portable.zip` |
| Windows 11, ARM64 (for example Snapdragon) | `Yarnelo-Windows-arm64-Setup.exe` | `Yarnelo-Windows-arm64-Portable.zip` |

Find your architecture in **Settings → System → About → System type**. Choose x64 for Intel/AMD x64 processors and ARM64 for ARM processors such as Snapdragon. Use x86 only for 32-bit Windows 10. These are separate native application builds; ARM64 is not an x64 executable repackaged for emulation.

Windows x64/x86 needs **Microsoft .NET Framework 4.8 or 4.8.1**. Native ARM64 needs **Windows 11 and .NET Framework 4.8.1**. Windows 10 ARM64, Windows 7 and Windows 8.1 are outside this release's compatibility scope. The installer checks the required runtime. Official downloads: [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) · [.NET Framework 4.8.1](https://dotnet.microsoft.com/download/dotnet-framework/net481).

## Dock appearance and position

Right-click the dock → **Settings**. The two-column window shows all controls without vertical scrolling and scales down only when the working area is smaller. **Corner rounding** spans square corners (0), intermediate rounding and a capsule (100); background opacity is independent of icon opacity.

Choose a centered **Top, Bottom, Left or Right** position on the dock's current monitor. Left/right docks are vertical with upright icons. Dragging the handle switches to free positioning. Your preferred one to four rows becomes columns at a side edge. When shortcuts exceed the screen, extra rows/columns are added automatically before reducing icon size; your saved preference is retained. Anchored positions are reapplied when the dock size changes.

## EXE installer

1. Download the matching `Setup.exe` from the release's Assets section.
2. Run the wizard. A new installation defaults to Program Files and requests administrator rights. You can choose installation for your account only instead. Updates reuse the existing installation scope and folder.
3. The installer adds a Start menu shortcut. A desktop shortcut is optional.
4. Launch Yarnelo from Start and drop shortcuts on its dock. Startup at sign-in is a separate option in the dock's settings.

The files are currently unsigned. If Windows displays SmartScreen, check that the file came from `feogrim-cloud/Yarnelo` releases. For an integrity check, download `SHA256SUMS` from the same release, open PowerShell in the download folder and run:

```powershell
Get-FileHash .\Yarnelo-Windows-x64-Setup.exe -Algorithm SHA256
```

The hash must match the same filename in `SHA256SUMS`. Substitute the correct name for x86, arm64 or ZIP files. A checksum verifies download integrity; it does not replace a publisher signature.

## Portable version

1. Download the matching `Portable.zip`.
2. Extract **all files** to a permanent writable folder, such as `Documents\Yarnelo`. Do not run the program from inside the archive.
3. Run `Yarnelo.exe`.

The portable ZIP includes `portable.mode`, which keeps settings and shortcuts in `Data` beside the program. Keep this marker. A fresh Setup installation does not install it and uses `%LOCALAPPDATA%\PolkaDock` instead. Updating an older installation that already has the marker retains its portable mode and `Data`; an unrelated leftover `Data` folder does not change a standard installation's profile. Do not move a populated portable installation: imported shortcut paths in the profile are absolute.

## Interface and language

Right-click an empty area of the dock → **Language / Язык** → **English / Русский**. The interface updates without restarting and remembers the choice. On the first launch, a Russian system locale selects Russian; other locales select English. The app language does not change your Windows language or rename your own files and shortcuts.

Use **+** to add files, folders, This PC or Recycle Bin. Drag icons to rearrange them; drag the left handle to move the dock. **Settings** controls rows, background opacity, icon size, magnification, labels, startup at sign-in and visibility above ordinary windows.

By default the dock hides when another window covers its space and returns automatically when the space is free. A window above the dock, or activity on another monitor, does not hide it. Fullscreen and borderless apps on the dock's monitor hide it even with the “above ordinary windows” option enabled. Returning the dock does not take focus.

## Startup at sign-in

Enable **Settings → Start when signing in to Windows**. Yarnelo creates a logon task for your Windows account with a 10-second delay, using the exact executable path and its folder as the working directory. The task runs with your ordinary account permissions, is allowed on battery power and has no execution time limit.

The task appears in **Task Scheduler → Task Scheduler Library** as `Yarnelo.Logon.<SID>`, where `<SID>` is your account identifier. Manage it with the app's startup switch, or inspect its **Triggers**, **Actions** and **Last Run Result** in Task Scheduler. Scheduled tasks do not normally appear in **Task Manager → Startup apps**. A legacy “Polka” row there does not show the new task's status.

On the first normal launch after updating to 1.7.1, an enabled legacy startup entry that points to this installation is migrated. The old Run entry is removed only after the new task has been saved and verified. A task or legacy startup entry disabled in Windows is not automatically enabled again. To enable startup explicitly, use the switch in Yarnelo; if needed, turn it off and on again. Changes that fail are reported, and the switch returns to its previous state.

Turning startup off removes only this installation's task and matching legacy registration. Other programs and other Yarnelo installation paths are left alone. Checking a saved task or running it manually verifies its command; automatic logon triggering is confirmed at the next Windows sign-in. Yarnelo does not sign you out or restart Windows for this check.

## Shortcuts and storage

With desktop shortcut transfer enabled, desktop `.lnk` / `.url` files are stored in the profile's `Library` folder and removed from the desktop. Their contents and launch arguments are retained. Ordinary files and folders stay in place. Right-click an icon → **Remove and restore shortcut to desktop** to restore an imported shortcut. Existing files are never overwritten.

Shared desktop shortcuts may require a Windows administrator prompt to transfer or restore. Canceling the prompt leaves the original shortcut in place. Adding This PC or Recycle Bin does not alter Windows desktop-icon settings.

With desktop transfer enabled, a new shared-desktop shortcut added automatically or manually is copied into the dock and queued for approval to move only that new copy. The Windows approval request waits until the dock is visible and the desktop or Yarnelo is active, with no fullscreen app, settings window, menu or drag interaction. Canceled or failed requests are not repeated automatically; use **+ → Move shared shortcuts into the dock** to retry. Older pending shared transfers found at startup are not added to the automatic approval queue.

Temporary installer locks on personal desktop shortcuts are retried. If an installer recreates a matching copy, Yarnelo can move it again; changed contents are retained on the desktop with a notice. A full dock still leaves a new shortcut on the desktop without queuing a later import.

For a standard Setup installation, the profile is `%LOCALAPPDATA%\PolkaDock`: `settings.json`, its backup `settings.json.bak`, and `Library`. For a portable installation, the same files are in `Data` beside the executable.

## Update without losing shortcuts

For the installed version, run the new installer. It reuses the existing installation scope and folder; keep that folder selected. It asks only that installation's Yarnelo process to close gracefully. If the process does not respond, setup stops and asks you to close it manually; it does not force-terminate it. The native profile, any existing portable marker and `Data`, and the startup preference are preserved. Setup does not switch an existing profile between native and portable storage.

For the portable version, exit Yarnelo from its notification-area menu and extract the new archive over the program files **in the original folder**, retaining `Data`. Back up the folder first. Do not simply copy an old `Data` profile to a different installation path: its absolute shortcut paths still refer to the old location.

When switching an existing Windows 11 ARM device from an emulated build to native ARM64, close Yarnelo and back up the entire original folder first. Install or extract the ARM64 package into that **same folder** and retain `Data`. Do not combine files from different architecture packages. Desktop shortcut files and JSON settings are architecture-independent; their absolute paths still need the original folder.

If Yarnelo is running from another folder, close it before launching the new copy. Installing beside an existing portable version does not import that version's shortcuts automatically.

## Uninstall

For the installed version, use **Settings → Apps → Installed apps → Yarnelo → Uninstall**, or run `Uninstall\unins000.exe` inside its program folder. Before removing files, the uninstaller closes this copy of Yarnelo and restores all desktop `.lnk` and `.url` shortcuts that it moved into its library. Existing files are never overwritten: an identical shortcut is reused, and a different same-name file is retained while the restored shortcut receives a free name. If a shortcut cannot be restored, removal stops and leaves the program and profile available for recovery.

Successful removal deletes only this installation's startup task and matching legacy entry. It keeps the native profile or portable `Data`, including the saved library. Shared desktop restoration may require administrator rights. Uninstalling one installation does not restore shortcuts from unrelated portable copies or other Windows accounts.

For a portable copy without an uninstaller, restore its moved shortcuts through the icon menus, disable startup in the app and exit before manually removing its program files. Keep its `Data` until the shortcuts have been restored.

## Building for developers

On Windows with .NET Framework 4.8:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\Build.ps1 -Architecture x64 -Output .\build\x64
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\Build.ps1 -Architecture x86 -Output .\build\x86
```

To build installers, also install official [Inno Setup 6.3 or newer](https://jrsoftware.org/isdl.php):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package.ps1 -Architecture x64 -BuildInstaller
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package.ps1 -Architecture x86 -BuildInstaller
```

For native ARM64, install the **.NET 8 SDK**. `Build.ps1` uses its Roslyn compiler with `PlatformTarget=ARM64` and restores `Microsoft.NETFramework.ReferenceAssemblies.net481` version **1.0.3** from NuGet. The first build needs network access. This can compile ARM64 on Windows x64 or ARM64, but running the result needs Windows 11 ARM64 with .NET Framework 4.8.1:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\Build.ps1 -Architecture arm64 -Output .\build\arm64
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package.ps1 -Architecture arm64 -BuildInstaller
```

Results are written to `dist`: ZIP, Setup.exe and SHA-256 files. Use `-IsccPath 'C:\path\ISCC.exe'` for a nonstandard compiler location. Packaging creates a clean directory and rejects profile data, imported shortcuts and unrelated files.

### Launching from automation

An assistant or terminal can manage its children in a Windows job with `KILL_ON_JOB_CLOSE`. A normal child-process launch can make Yarnelo exit when that host closes. Use the independent launcher for a stopped copy:

```powershell
.\scripts\windows\Start-Independent.ps1 -Executable (Resolve-Path .\build\x64\Yarnelo.exe).Path -PassThru
```

The helper asks Task Scheduler to launch the exact executable in the current user's interactive session with normal rights. Its temporary task has no triggers and is removed after startup is verified. It does not enable startup at sign-in or change an existing startup task. An optional `-ReadyLog` waits for Yarnelo's `pid=N ready` receipt. Expansion expressions such as `%PATH%` or `$(...)` in executable paths, working directories or arguments are rejected before registration because Task Scheduler interprets them.

Run `scripts/windows/Test-IndependentLaunch.ps1` to check this behavior using disposable processes and a private kill-on-close job. The test closes only its own job and verifies that the independently launched process continues and its temporary task has been removed. It never closes the assistant or an installed Yarnelo instance. [Windows job lifetimes](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).

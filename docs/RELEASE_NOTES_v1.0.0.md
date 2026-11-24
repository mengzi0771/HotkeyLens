# HotkeyLens v1.0.0

**Discover Every Shortcut.**

HotkeyLens is an on-demand Windows desktop tool that discovers installed applications, reads accessible shortcut sources, records where each binding came from, and separates proven global-hotkey competition from ordinary duplicate bindings.

## What's included

- Application discovery from uninstall entries, App Paths, Start Menu and desktop shortcuts, and Windows Shell applications.
- Seven read-only shortcut source types: `.lnk` hotkeys, VS Code, Windows Terminal, JetBrains IDEs, Notepad++, current UI Automation menus, and a bundled official-documentation catalog.
- Search and filtering by gesture, application, category, scope, source, confidence, and conflict participation.
- Evidence-based conflict analysis that distinguishes global registration competition, potential conflicts, and duplicate local bindings.
- Five-page WPF interface with light, dark, and follow-system themes.
- Atomic JSON persistence, single-instance protection, cancellation, failure diagnostics, and preservation of existing data when a save cannot complete.
- 67 automated tests.

## Install

1. Download `HotkeyLens-Setup.exe` and `SHA256SUMS.txt`.
2. Verify the installer:

   ```powershell
   Get-FileHash .\HotkeyLens-Setup.exe -Algorithm SHA256
   Get-Content .\SHA256SUMS.txt
   ```

3. Run the installer. It installs for the current user and does not require administrator rights.
4. Start HotkeyLens from the final page, Start menu, or the optional desktop shortcut.

The installed program and the portable build both keep user data in `%LOCALAPPDATA%\HotkeyLens`. The installer does not write scan data into the installation folder. Upgrading or uninstalling does not remove user data.

## Portable

Extract `HotkeyLens-Portable-win-x64.zip` and run `HotkeyLens.exe`. No installation and no .NET runtime are required.

## System requirements

- Windows 10 version 1809 or later, or Windows 11, x64.
- A desktop session. HotkeyLens is a desktop application, not a service.
- No .NET SDK or .NET runtime.

## SHA256

The authoritative checksums are attached to this release as `SHA256SUMS.txt` and are generated from the exact files uploaded here. Verify with:

```powershell
Get-FileHash .\HotkeyLens-Setup.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

## Known technical limits

- Windows has no public API that enumerates every global hotkey registered by other processes.
- UI Automation only exposes information from currently open windows. HotkeyLens neither launches applications nor expands menus to scan them.
- Official documentation represents defaults, may change between versions, and is not proof of the user's actual configuration.
- The built-in catalog covers a small, verified set of applications. Unsupported applications are shown as not automatically readable; no results are invented.
- Configuration formats vary by version. Parse failures become diagnostics rather than guessed bindings.
- Shortcut management only locates settings or source files. It does not rewrite third-party configuration.
- Release binaries are unsigned. Windows SmartScreen may show "Unknown publisher".

## Security software and SmartScreen

The release is not digitally signed. Download it only from this GitHub release and verify the files against `SHA256SUMS.txt` before running them.

Some application-control software may block the unsigned process from writing to `%LOCALAPPDATA%\HotkeyLens`. In that case HotkeyLens reports the exact failing path, leaves the previous snapshot intact, and does not claim the save succeeded. Add `HotkeyLens.exe` (and `HotkeyLens-Setup.exe` for installation) to the security product's trusted list. Do not disable system-wide protection just to run the app.

No compatibility with every antivirus product is claimed.

## Testing

- Release build: 0 warnings, 0 errors.
- Automated tests: 67 passed, 0 failed, 0 skipped.
- Portable package launched on a machine with no .NET runtime: scan completed, 53 shortcuts and 175 applications recorded, snapshot saved, application exited with code 0.
- First save, repeated atomic overwrite, restart recovery, and failed-save data preservation were exercised with a controlled data directory.
- Real UI automation covered all five pages, search, detail panels, theme switching, scanning, and clean exit.
- Installer lifecycle verification runs on a clean GitHub Windows x64 runner.

The per-run results are recorded in [TESTING.md](TESTING.md).

## Links

- [Source code](https://github.com/mengzi0771/HotkeyLens)
- [README](../README.md)
- [Architecture](ARCHITECTURE.md)
- [Technical limitations](LIMITATIONS.md)
- [Testing](TESTING.md)
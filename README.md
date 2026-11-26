# HotkeyLens

> Discover Every Shortcut.

**English** | [简体中文](README.zh-CN.md)

HotkeyLens is an on-demand Windows desktop tool for discovering installed applications, reading accessible shortcut configurations, recording where every binding came from, and classifying findings as no conflict, normal duplicate, potential conflict, confirmed conflict, or unknown.

It does not run in the background, does not continuously monitor the keyboard, and does not fabricate scan results. Scan data stays on your machine; the only optional network request is the update check described below.

## What's new in 1.2.0

- **Everyday global-hotkey check-up:** curated official global hotkeys plus plain-language "how to change it" guides for NetEase Cloud Music, QQ Music, WeChat, QQ and Snipaste. WeChat is matched by both `WeChat.exe` and the 4.x `Weixin.exe`.
- **Read-only configuration readers (first batch):** Chrome/Edge extension shortcuts stored in profile `Preferences` (both the classic nested and the newer flat `extensions.commands` shapes) and Snipaste's `config.ini`. Missing, locked or malformed files fall back to documented defaults with an explicit diagnostic — never presented as a user configuration.
- **Runtime-aware conflict grading:** when both applications that declare the same global hotkey are running, the finding becomes a medium-severity "global hotkey competition" with a plain-language warning about the first-registered app winning. When at least one is not running it drops to low severity and says competition only exists while both are open. If the process table is filtered by security software, unmatched applications become "unknown" instead of being claimed as closed; visible top-level windows are used as a read-only fallback to find apps such as WeChat and NetEase Cloud Music that protect their main process.
- **Less noise by default:** the Shortcuts page now shows only global and Windows shell hotkeys until "只看重点热键" is switched off (27 of 235 on the development machine).
- **One-click assisted resolution:** each conflict card shows both apps' running state and evidence source, with "switch to this app", "open folder / configuration file" and "recheck this conflict" actions that close the find → go fix → verify loop using only read-only reads and standard window calls. A failed explorer launch now reports the error code and target path instead of claiming the folder was revealed.
- **Verification:** 162 automated tests pass, including new cases for Chromium `Preferences`, Snipaste `config.ini`, runtime conflict grading, filtered-process-table degradation, runtime wording, explorer launch failure feedback, default focus filtering and auxiliary path validation.

## What's new in 1.1.1

- **Microsoft Edge identity:** versioned `Application\<version>\msedge.exe` paths are normalized to the installation root, while Stable, Beta, Dev and Canary installations remain separate. Edge no longer appears twice in a scan or conflict participant list.
- **Windows Terminal discovery:** the Microsoft Store `wt.exe` launcher shim is resolved to the packaged `WindowsTerminal.exe`, grouped by MSIX package family, and matched through catalog aliases. Legacy `keybindings` that only use an `id` are now read as user configuration instead of being discarded.
- **Safer upgrades:** before starting the installer, HotkeyLens verifies that the installation directory is writable and reports a clear message if security software blocks the replacement. The installer also performs its own write check and restricts its close-application handling to `HotkeyLens.exe`. The app never disables security software and never runs a silent upgrade.
- **Validation:** 108 automated tests pass; a real local scan now finds one Edge entry and 16 Windows Terminal bindings (12 documented defaults, 4 user-configured).

The screenshots in this README were captured for 1.2.0 in the dark theme. 1.2.0 adds runtime state, one-click assisted resolution and a lower-noise default view, so the layout and information density changed and the screenshots were recaptured.

![HotkeyLens dashboard in the dark theme](docs/screenshots/dashboard-dark.png)

## Contents

- [Download and install](#download-and-install)
- [Project status](#project-status)
- [Core capabilities](#core-capabilities)
- [How shortcuts are read](#how-shortcuts-are-read)
- [Data model](#data-model)
- [Conflict analysis](#conflict-analysis)
- [Automatic updates](#automatic-updates)
- [Interface](#interface)
- [Architecture](#architecture)
- [Build and run](#build-and-run)
- [Data and privacy](#data-and-privacy)
- [Testing](#testing)
- [Known limitations](#known-limitations)
- [Security software and SmartScreen](#security-software-and-smartscreen)
- [Roadmap](#roadmap)
- [Documentation](#documentation)

## Download and install

**Most users should download a release package. A .NET runtime is not required.**

| Package | Best for | Size |
|---|---|---|
| `HotkeyLens-Setup.exe` | Normal installation with Start menu and optional desktop shortcuts | 43.6 MiB |
| `HotkeyLens-Portable-win-x64.zip` | Running without installation | 62.3 MiB |
| `SHA256SUMS.txt` | Verifying downloaded files | < 1 KiB |

Download them from the [latest GitHub release](https://github.com/mengzi0771/HotkeyLens/releases/latest).

### Install

1. Download `HotkeyLens-Setup.exe` and `SHA256SUMS.txt` from the same release.
2. Optionally verify the download:

   ```powershell
   Get-FileHash .\HotkeyLens-Setup.exe -Algorithm SHA256
   Get-Content .\SHA256SUMS.txt
   ```

   The hash must match the `HotkeyLens-Setup.exe` line in `SHA256SUMS.txt`.
3. Run the installer. It installs for the current user and does not require administrator rights.
4. Choose an installation folder and whether to create a desktop shortcut.
5. Launch HotkeyLens from the final page, the Start menu, or the desktop shortcut.

The executable is not code-signed. Windows SmartScreen may say "Unknown publisher". Only continue if the download came from this repository's release page and its hash matches. See [Security software and SmartScreen](#security-software-and-smartscreen).

### Portable

Extract `HotkeyLens-Portable-win-x64.zip` to a folder you control and run `HotkeyLens.exe`. The portable and installed builds share the same user data under `%LOCALAPPDATA%\HotkeyLens`; the ZIP itself does not contain scan data or source code.

### System requirements

- Windows 10 version 1809 or later, or Windows 11, x64
- A desktop session (the app is a WPF desktop program, not a service)
- No .NET SDK or .NET runtime installation

## Project status

- Platform: Windows 10 / Windows 11 (x64)
- Stack: C# / .NET 10 / WPF / Windows UI Automation
- Current version: 1.1.1
- Automated tests: 108 / 108 passing
- Status: builds, runs, and is packaged as a self-contained Windows x64 installation and portable build. Shortcut management is intentionally limited to safe, read-only navigation to an application's settings or configuration source; HotkeyLens does not rewrite third-party configurations.

## Core capabilities

### 1. Application discovery

Candidates are merged from several read-only sources:

- Windows uninstall entries (HKLM/HKCU, 64-bit and 32-bit views)
- `App Paths`
- Start Menu and desktop shortcuts
- Windows built-in Shell applications

Executables are deduplicated by normalized path; when no path is available, name and publisher are used instead. Runtimes, updaters, installers, and other non-application components are filtered out. A meaningful executable `FileDescription` takes priority over an ambiguous `ProductName`, preventing several Microsoft Office components from appearing under the same name.

Every discovered application keeps an explicit scan state: `Scanned`, `No shortcuts found`, `Not automatically readable`, or `Scan failed`. Finding no shortcuts never means the application has none, and both the UI and documentation say so explicitly.

### 2. Shortcut source adapters

The scan engine does not depend on any single application. Each source is an independent adapter behind one interface, returning bindings, scope, and diagnostics:

| Adapter | Input | Effective scope | Source type | Typical confidence |
|---|---|---|---|---|
| Shortcut hotkeys | `HotKey` field of Start Menu / desktop `.lnk` files | Global | User configuration | High |
| VS Code configuration | `keybindings.json` (VS Code / Insiders / VSCodium / Cursor) | Window / context | User configuration | High |
| Windows Terminal | `actions` and legacy `keybindings` in `settings.json` | Window / context | User configuration | High |
| JetBrains IDEs | Explicit `keyboard-shortcut` overrides in user keymap XML | Window | User configuration | High |
| Notepad++ | `%APPDATA%\Notepad++\shortcuts.xml` | Window | User configuration | High |
| Windows UI Automation | Accessible menu-item `AcceleratorKey` in open windows | Window | Runtime observation | Medium |
| Built-in official catalog | Bundled summaries of official shortcut documentation | As documented | Official defaults | Medium |

The built-in catalog currently covers Microsoft Word, Excel, PowerPoint, Google Chrome, Microsoft Edge, Adobe Photoshop, VS Code, Windows Terminal, JetBrains IDEs, Notepad++ and Windows Shell. Every entry carries its official documentation URL, the applicable product versions, the supported Windows versions, and the date the documentation was reviewed. It represents documented defaults, not the user's current configuration. Word, Excel, PowerPoint, Chrome, Edge and Photoshop are catalog-only: HotkeyLens does not parse their local user key assignments in this release.

Adapters only read files or properties exposed by UI Automation. They do not launch scanned applications, expand menus, install keyboard hooks, or modify other applications' settings.

### 3. Search and filtering

The Shortcuts page supports:

- Searching key gestures, application names, actions, descriptions, sources, confidence rationale, and raw values
- Filtering by application, application category, scope, source, confidence, and source type
- Showing only shortcuts involved in conflicts or duplicate groups
- Sorting by gesture, application, action, scope, confidence, or source
- Grouping by application
- Opening details and the original source location for an individual shortcut

Filtering is performed in memory by `ShortcutQuery` against the current snapshot; it does not re-read files or system state on each keystroke.

### 4. Conflict analysis

The same key combination does not automatically mean a conflict. HotkeyLens classifies every group of two or more entries by evidence, using gesture, scope, context, source and confidence:

| Classification | Condition | Meaning |
|---|---|---|
| Confirmed conflict | Two or more `.lnk` entries declare the same key as a global hotkey, or one application has two different actions with the same key in an overlapping context and reliable local evidence | The same effective scope can genuinely compete for the key |
| Potential conflict | A global or unknown-scope entry overlaps a local entry, or context/evidence is incomplete | Interference is possible, but the evidence is not sufficient to prove it |
| Normal duplicate | All participants are ordinary in-application bindings in different applications | Word, Excel and Chrome can all use Ctrl+S independently; this is not counted as a conflict |
| Unknown | Scope or context cannot be established | The tool records the finding without claiming a conflict |
| No conflict | Entries do not share an effective scope or context | Excluded from conflict totals |

`Normal duplicate` and `no conflict` are never added to the conflict count. The Conflicts page shows the classification, participants, actions, scope, source, context, rationale and confidence for every finding.

## How shortcuts are read

HotkeyLens uses the following legitimate, read-only sources:

1. **Application discovery:** uninstall registry keys, `App Paths`, Start Menu and desktop shortcuts.
2. **Configuration files:** user configuration for known applications; read-only, never written back.
3. **Shortcut hotkeys:** the Shell Link header field, not simulated key presses.
4. **UI Automation:** only menu properties and `AcceleratorKey` exposed by currently open windows, bounded by a timeout and window limit.
5. **Official documentation catalog:** embedded default-shortcut summaries with their source URLs.

Windows has no public API for enumerating every global hotkey registered by other processes. HotkeyLens does not use `RegisterHotKey` for enumeration and does not use `WH_KEYBOARD_LL`. See [docs/LIMITATIONS.md](docs/LIMITATIONS.md) for the technical boundaries.

## Data model

Each `ShortcutEntry` records:

- A normalized gesture (`ShortcutGesture`) and, when applicable, the full key sequence
- Application, action name, and description
- Scope: global, application, window, contextual, or unknown
- Source type and source location
- Confidence and the rationale behind it
- Origin: official default, user configuration, runtime observation, or unknown
- Context, raw value, capture time, and enabled state

### Defaults vs. actual configuration

- `OfficialDefault`: read from official documentation. It may be a default and may vary by version.
- `UserConfigured`: read from an actual file or shortcut on this machine. It means "configured locally", not necessarily edited by hand.
- `RuntimeObserved`: observed from the current UI at scan time.
- `Unknown`: insufficient evidence; it is not forced into another category.

### Key sequences

A chord such as `Ctrl+K Ctrl+S` is stored as a full `KeySequence` while the first stroke, `Ctrl+K`, participates in ordinary gesture matching. A sequence is not treated as a single global hotkey, and it is excluded from conflict analysis.

## Conflict analysis

The analyzer groups every enabled, single-stroke entry by canonical gesture. Within one application it looks for different actions that share the same gesture in overlapping contexts; two different actions in mutually exclusive contexts are not reported. Across applications it treats ordinary window/application bindings as normal duplicates, raises a potential conflict only when a global or unknown scope is involved, and requires at least two global registration evidences before calling it confirmed. Disabled entries and multi-stroke sequences are excluded.

Windows has no public API that enumerates every global hotkey registered by other processes. A `.lnk` hotkey field is evidence that a shortcut asks Windows to register a hotkey; it is not proof that another process registered the same combination at runtime. Results are evidence-based inferences, not registry facts. The UI explains the reasoning and never claims that a hotkey has definitively been "stolen".

## Automatic updates

HotkeyLens checks the official repository for a newer stable release at most once every 24 hours, starting when the app opens. The check is a normal HTTPS request to the GitHub Releases API and stops when the app closes; there is no updater service, tray process, or startup task.

- The default update source is `https://github.com/mengzi0771/HotkeyLens/releases`.
- Draft and pre-release entries are ignored. Versions are compared numerically as Semantic Versioning, not as strings.
- A manual **Check for updates** button in Settings works even when automatic checks are off.
- When a newer release is found, the update window shows the current version, latest version, publication date, release notes, and the options **Update now**, **Remind me later**, and **Skip this version**.
- Skipping stores the exact version. The skip can be cleared from Settings.
- Network failures, GitHub rate limits, and malformed API responses are reported without blocking normal use.

Downloads are restricted to HTTPS assets under `github.com/mengzi0771/HotkeyLens/releases/download/`. Before running anything, HotkeyLens reads `SHA256SUMS.txt` from the same release, downloads the package to a temporary file, and verifies its SHA256. A mismatch deletes the temporary file and refuses to run it. This checks integrity, not publisher identity; the binaries remain unsigned.

The installed build downloads `HotkeyLens-Setup.exe` and starts the normal Inno Setup wizard after the user confirms. The portable build downloads `HotkeyLens-Portable-win-x64.zip`, opens its download folder, and explains how to close the app and replace the extracted files. HotkeyLens never silently installs an update and never overwrites the running portable executable.

Update preferences (`AutoCheckUpdates`, `LastUpdateCheckUtc`, `SkippedVersion`) are stored in `preferences.json` with the other settings.

## Interface

Five pages share one view model and scan snapshot:

- **Dashboard:** applications, shortcuts, normal duplicates, potential conflicts, confirmed conflicts, last scan time, and coverage.
- **Shortcuts:** search, filters, sorting, grouping, list, and a detail panel.
- **Applications:** categories, icons, versions, scan state, paths, and settings locations.
- **Conflicts:** five classifications, evidence, participants, context, rationale, and recommendations.
- **Settings:** light/dark/system theme, scan-source toggles, timeout and window limits, update preferences, manual update check, data management, and about.

Themes are provided by shared resource dictionaries. Changing a theme applies immediately across the whole window and is saved automatically; no restart is required. The light theme uses a white sidebar, a light-gray workspace, and white cards. The dark theme uses neutral charcoal panels, dark-gray cards, and a single blue accent.

| Shortcuts (dark, important only by default) | Applications (dark) | Settings (dark) |
|---|---|---|
| ![Shortcut list](docs/screenshots/shortcuts-dark.png) | ![Applications](docs/screenshots/applications-dark.png) | ![Settings](docs/screenshots/settings-dark.png) |

The conflict card shows both sides' runtime state, evidence source and plain-language steps, plus three assisted actions: switch to the application, open its folder or configuration file, and recheck the conflict:

![Conflict card and assisted resolution](docs/screenshots/conflicts-dark.png)

## Architecture

```
HotkeyLens.App (WPF/MVVM)
        |
        +-- Views / ViewModels / Themes
        +-- composition, single instance, lifecycle
                 |
                 v
HotkeyLens.Core
  Discovery -> ScanEngine -> Analysis -> Query
                  |
                  +-- Adapters (7 source types)
                  +-- Models / Catalog
                  +-- Updates (GitHub Releases, SHA256 verification)
                  +-- Persistence (JSON with .bak recovery)
```

- **Core** owns models, application discovery, scan orchestration, source adapters, conflict analysis, querying, and persistence.
- **App** owns the WPF UI, MVVM, themes, window lifecycle, icon loading, and file-location actions.
- The scan engine depends only on `IApplicationDiscoveryService` and `IShortcutSourceAdapter`. Supporting a new application means adding an adapter, not changing the engine.
- Scanning runs asynchronously in the background with progress callbacks. Closing the window cancels scanning and releases the single-instance lock; no service or tray process remains.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full design.

## Build and run

### Build requirements

- Windows 10 or Windows 11 (x64)
- .NET SDK 10.0.400 or later (the repository pins `10.0.404`)
- Optional: the .NET desktop development workload in Visual Studio 2022/2026
- For installers: Inno Setup 6

```powershell
git clone https://github.com/mengzi0771/HotkeyLens.git
cd HotkeyLens
dotnet restore
dotnet build HotkeyLens.sln -c Release
dotnet run --project src/HotkeyLens.App/HotkeyLens.App.csproj -c Release
```

The framework-dependent development build is:

```
src/HotkeyLens.App/bin/Release/net10.0-windows/HotkeyLens.exe
```

It requires the .NET 10 Desktop Runtime on the target machine. This is not the build used by the downloadable release packages.

### Build release packages

```powershell
.\packaging\build-release.ps1 -Version 1.1.1
```

The script publishes a self-contained `win-x64` build, creates the portable ZIP, compiles the Inno Setup installer, and writes `SHA256SUMS.txt` under `artifacts\dist`. If Inno Setup is installed in a non-standard location, pass `-InnoCompiler <path-to-ISCC.exe>`.

The release workflow under `.github/workflows` runs the tests and builds the same assets. `.github/workflows/verify-installer.yml` can be started manually to install, upgrade, uninstall, and launch both packages on a clean GitHub Windows runner.

### Local data

Default data directory:

```
%LOCALAPPDATA%\HotkeyLens
├── scan-snapshot.json
├── scan-snapshot.json.bak
├── preferences.json
└── preferences.json.bak
```

Saves write a temporary file, flush it to disk, then replace the previous file while keeping one `.bak` copy. If the primary file is missing or invalid, HotkeyLens tries the backup before reporting an error, so a failed save cannot silently destroy the last good snapshot. A version-1 snapshot from HotkeyLens 1.0.0 is migrated to the five-class schema when read.

For testing or diagnostics, override it with:

```powershell
$env:HOTKEYLENS_DATA_DIR = 'D:\HotkeyLens-data'
```

HotkeyLens never writes data into its installation directory or the registry. If security software blocks writes to the user profile, the UI reports the failure and leaves the existing snapshot untouched. Add HotkeyLens to the security software's trusted/allowed list instead of working around the protection by changing filenames.

## Data and privacy

- Scanning only happens after the user clicks Scan.
- No telemetry and no upload of scan results, shortcuts, or application names.
- The only network request is the optional update check: one HTTPS request to the GitHub Releases API at most every 24 hours while the app is open. It sends no machine identifiers, scan data, or settings. Disable it in Settings without affecting local features.
- No reading of other process memory, no modification of other applications' settings, and no writes to the system registry.
- "Open source" and "Open settings location" use the system default handler only when the user clicks them.
- Closing the main window exits the application. There is no tray icon, startup task, or resident service.

## Testing

```powershell
dotnet test tests/HotkeyLens.Core.Tests/HotkeyLens.Core.Tests.csproj `
  --nologo -p:_MSTestEnableParentProcessQuery=false
```

Latest result:

```
Failed: 0, Passed: 108, Skipped: 0, Total: 108
```

Coverage includes gesture parsing, real application-discovery smoke tests, Edge version-path canonicalization and channel isolation, Windows Terminal package identity and legacy `keybindings`, shortcut-header parsing, the built-in catalog, query filtering, conflict rules, scan-engine state transitions, snapshot/preference persistence, concurrent saves, failed-save protection, `.bak` recovery, and update version/SHA256 handling.

UI acceptance was performed with real scan data and Windows UI Automation, covering navigation across all five pages, search, details, theme switching, first scan, overwrite, failure protection, restart recovery, and clean exit. See [docs/TESTING.md](docs/TESTING.md) (Chinese).

## Known limitations

- Windows provides no API for enumerating every global hotkey registered by other processes.
- UI Automation can only see information exposed by currently open windows; HotkeyLens will not start applications or expand menus for scanning.
- Official documentation describes defaults, may change between versions, and does not reflect user configuration.
- The built-in catalog covers a small, verified set of applications. Uncovered applications are shown honestly as "Not automatically readable"; no synthetic data is produced.
- Configuration formats vary by version. Parse failures are recorded as diagnostics instead of being guessed.
- Shortcut management only locates settings or configuration sources. It does not automatically rewrite third-party configuration.
- On some machines, security software may block writes to the user profile. HotkeyLens preserves existing data and gives an actionable error instead of hiding the failure.
- Release binaries are not code-signed, so SmartScreen or "Unknown publisher" prompts may appear.

The complete list is in [docs/LIMITATIONS.md](docs/LIMITATIONS.md) (Chinese).

## Security software and SmartScreen

HotkeyLens writes its own settings and scan snapshots under `%LOCALAPPDATA%\HotkeyLens`. Some application-control products may silently deny that path to a newly downloaded, unsigned executable. The app then:

- reports the exact path and underlying error,
- keeps the previous `scan-snapshot.json` intact,
- removes only its own temporary file, and
- does not claim that the scan was saved.

If this happens, add `HotkeyLens.exe` (and, for installation, `HotkeyLens-Setup.exe`) to the security product's trusted/application list. Do not disable system-wide protection just to run HotkeyLens. No compatibility with every antivirus product is claimed or tested; the release has no digital signature, so Windows may show an "Unknown publisher" warning. Verify the SHA256 hash from the same release before running either package.

During an upgrade, application-control software may block an unsigned installer from replacing the previous `unins000.exe`. In the tested case the installer rolled back and left the old version usable. HotkeyLens 1.1.1 now probes the installation directory before launch and explains that the current version and user data are unchanged; the installer performs the same write check and keeps the wizard interactive. If the block persists, allow the installer in the security product or wait for a signed release. HotkeyLens does not disable protection, and it does not claim that the block has been eliminated.

## Roadmap

- Add more verified application adapters (browsers, communication tools, design tools), each with official sources and tests.
- Add rollback-capable configuration editing for applications that explicitly support it, with automatic backups.
- Link conflict analysis with live window verification while keeping the evidence-first principle.
- Improve UI Automation coverage by letting the user choose a target window before scanning its menu hierarchy.
- Export shortcut inventories as CSV/JSON for team review.

## License

MIT License, Copyright © 2026 mengzi0771. See [LICENSE](LICENSE).

## Documentation

- [Architecture](docs/ARCHITECTURE.md) (Chinese)
- [Technical limitations](docs/LIMITATIONS.md) (Chinese)
- [Testing](docs/TESTING.md) (Chinese)
- [Screenshot directory](docs/screenshots)
- [简体中文 README](README.zh-CN.md)

HotkeyLens follows one rule: **show what can be proven, and label clearly what cannot.**
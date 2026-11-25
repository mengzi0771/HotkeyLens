# HotkeyLens 1.1.0

Released: 2026-11-25

HotkeyLens 1.1.0 expands shortcut coverage, rebuilds conflict analysis around evidence, and adds a safe in-app update flow. The Windows x64 installer and portable package are self-contained; no .NET runtime is required.

## What changed

### Shortcut coverage

- The built-in official catalog now covers 11 applications: Microsoft Word, Excel, PowerPoint, Google Chrome, Microsoft Edge, Adobe Photoshop, Visual Studio Code, Windows Terminal, JetBrains IDEs, Notepad++ and Windows Shell.
- Every catalog entry carries its official documentation URL, applicable product versions, supported Windows versions and review date.
- The Word, Excel, PowerPoint, Chrome, Edge and Photoshop entries are documented defaults. HotkeyLens does not read their local user key assignments in this release, and the UI labels them as official defaults rather than user configuration.
- Existing user-configuration adapters (VS Code, Windows Terminal, JetBrains, Notepad++, `.lnk` global hotkeys) remain read-only.

### Conflict analysis

- Results now use five explicit classifications: no conflict, normal duplicate, potential conflict, confirmed conflict and unknown.
- Word, Excel and Chrome can all use Ctrl+S without being reported as a conflict.
- Same-application different actions are checked only when their contexts can overlap.
- A cross-application global result requires two or more `.lnk` registration evidences before it is called confirmed. Windows has no public API to enumerate every global hotkey registered by other processes, so weaker evidence stays potential or unknown.
- Normal duplicates and no-conflict groups are excluded from conflict totals. The Conflicts page shows the classification, participants, action, scope, source, context, rationale and confidence.

### Automatic updates

- HotkeyLens checks the official GitHub Releases API at most once every 24 hours when it starts, and never keeps a background process alive after the window closes.
- Draft and pre-release versions are ignored; versions are compared with Semantic Versioning.
- The update window shows current/latest version, publication date, notes, and offers Update now, Remind me later and Skip this version.
- Settings contains an automatic-check switch, last check time, manual Check for updates, skipped-version management and the current version/mode.
- Downloads are HTTPS-only and restricted to assets under `github.com/mengzi0771/HotkeyLens/releases/download/`. SHA256 is checked against `SHA256SUMS.txt`; a mismatch refuses to run the file.
- Installed builds download `HotkeyLens-Setup.exe` and start the normal Inno Setup upgrade wizard. Portable builds open the download folder and explain the manual replace procedure; the running executable is never overwritten.
- If a newer version is not available yet, the app reports that 1.1.0 is the latest stable release.

### Data safety

- Snapshot and preference saves now keep a `.bak` copy, flush to disk, and use `File.Replace` where available.
- If the primary JSON is missing or damaged, HotkeyLens falls back to the backup before reporting an error.
- Version-1 snapshots from 1.0.0 are migrated to the five-class schema on read.
- Existing user data under `%LOCALAPPDATA%\HotkeyLens` is preserved across install, upgrade and uninstall.

### Other

- 98 automated tests pass, covering conflict classification, catalog data, snapshot migration/backup, update version logic, GitHub API handling, SHA256 verification and cancellation.
- Both README files and the installer/portable notices describe the current limits and the unsigned release status.

## Install

1. Download `HotkeyLens-Setup.exe` and `SHA256SUMS.txt` from this release.
2. Verify the hash:

   ```powershell
   Get-FileHash .\HotkeyLens-Setup.exe -Algorithm SHA256
   Get-Content .\SHA256SUMS.txt
   ```

3. Run the installer. It installs for the current user and does not require administrator rights.
4. Start HotkeyLens from the final page, Start menu, or the optional desktop shortcut.

Upgrades keep the previous installation directory and user data. The installer does not delete `%LOCALAPPDATA%\HotkeyLens` on uninstall.

## Portable

Extract `HotkeyLens-Portable-win-x64.zip` and run `HotkeyLens.exe`. The ZIP contains `portable.marker`, which lets the app report Portable mode even when an installed copy exists. Portable and installed builds share `%LOCALAPPDATA%\HotkeyLens`.

To update a portable copy, download the new ZIP from this release, close HotkeyLens, and replace the extracted files. Keep your own files outside the extraction folder.

## Requirements

- Windows 10 version 1809 or later, or Windows 11, x64
- Desktop session
- No .NET runtime installation

## Known limitations

- Windows does not expose an API that enumerates every global hotkey registered by other processes. A `.lnk` hotkey is registration evidence, not proof of a runtime conflict.
- UI Automation sees only information exposed by currently open windows. HotkeyLens does not launch applications or expand menus to improve coverage.
- Official documentation describes defaults and may vary by product version. Word, Excel, PowerPoint, Chrome, Edge and Photoshop are not parsed from local user configuration in 1.1.0.
- Third-party application-control software can block an unsigned installer from replacing its own uninstaller. In that case the installer rolls back and leaves the previous version usable; allow the installer in the security product instead of disabling protection.
- Release binaries are not code-signed, so SmartScreen may show an "Unknown publisher" warning. SHA256 verifies integrity, not publisher identity.

## Security

- Official download location: <https://github.com/mengzi0771/HotkeyLens/releases>
- Verify every asset with the `SHA256SUMS.txt` from the same release.
- Do not disable SmartScreen or antivirus protection just to run HotkeyLens.
- Report a suspicious file through the repository's issue tracker.

## License

MIT License, Copyright (c) 2026 mengzi0771. See the `LICENSE` file in the source archive.

---

# HotkeyLens 1.1.0（简体中文）

发布日期：2026-11-25

HotkeyLens 1.1.0 扩充了快捷键覆盖范围，按证据重构了冲突分析，并加入安全的应用内更新流程。Windows x64 安装版和便携版均为自包含程序，不需要安装 .NET 运行时。

## 主要变化

### 快捷键覆盖

- 内置官方目录扩展到 11 个应用：Microsoft Word、Excel、PowerPoint、Google Chrome、Microsoft Edge、Adobe Photoshop、Visual Studio Code、Windows Terminal、JetBrains IDE、Notepad++ 和 Windows Shell。
- 每个目录条目都记录官方文档 URL、适用产品版本、适用 Windows 版本和核对日期。
- Word、Excel、PowerPoint、Chrome、Edge、Photoshop 提供的是官方默认值；本版本不解析它们的本地用户按键，界面会明确标为“官方默认”，不会冒充用户实际配置。
- VS Code、Windows Terminal、JetBrains、Notepad++ 和 `.lnk` 全局热键等现有适配器继续保持只读。

### 冲突分析

- 结果明确分为五类：无冲突、正常重复、潜在冲突、确认冲突、未知。
- Word、Excel、Chrome 同时使用 Ctrl+S 不会再被判为冲突。
- 同一应用内只有上下文可能重叠时，才检查同键不同功能。
- 跨应用的全局结果至少需要两个 `.lnk` 注册证据才判定为确认冲突。Windows 没有公开 API 枚举其他进程注册的全部全局热键，证据不足时保持潜在或未知。
- 正常重复和无冲突组不计入冲突总数。冲突页显示分类、参与应用、功能、作用范围、来源、上下文、判断依据和可信度。

### 自动更新

- 程序启动时最多每 24 小时检查一次 GitHub Releases API，关闭窗口后不会保留后台进程。
- 忽略 Draft 和 Pre-release；版本按 Semantic Versioning 比较。
- 更新窗口显示当前版本、最新版本、发布时间、更新说明，并提供“立即更新”“稍后提醒”“跳过此版本”。
- 设置页包含自动检查开关、上次检查时间、手动“检查更新”、跳过版本管理和当前版本/运行方式。
- 下载只允许 HTTPS，且仅限 `github.com/mengzi0771/HotkeyLens/releases/download/` 下的资产；用同一 Release 的 `SHA256SUMS.txt` 校验 SHA256，不匹配则拒绝执行。
- 安装版下载 `HotkeyLens-Setup.exe` 后启动正常的 Inno Setup 升级向导；便携版打开下载位置并提示手动替换，绝不覆盖正在运行的程序。
- 尚无更新时，程序会明确显示当前 1.1.0 已是最新正式版本。

### 数据安全

- 快照和偏好保存会保留 `.bak` 备份、刷新到磁盘，并优先使用 `File.Replace`。
- 主 JSON 缺失或损坏时先回退备份，再报告错误。
- 读取 1.0.0 的版本 1 快照时自动迁移到五类结构。
- 安装、覆盖升级和卸载都保留 `%LOCALAPPDATA%\HotkeyLens` 下的用户数据。

### 其他

- 98 项自动化测试通过，覆盖冲突分类、目录数据、快照迁移/备份、版本比较、GitHub API、SHA256 校验和取消。
- 中英文 README、安装说明和便携版说明已同步当前限制与未签名状态。

## 安装

1. 从本 Release 下载 `HotkeyLens-Setup.exe` 与 `SHA256SUMS.txt`。
2. 校验哈希：

   ```powershell
   Get-FileHash .\HotkeyLens-Setup.exe -Algorithm SHA256
   Get-Content .\SHA256SUMS.txt
   ```

3. 运行安装程序。它安装到当前用户，不要求管理员权限。
4. 从完成页、开始菜单或可选桌面快捷方式启动。

覆盖升级会沿用原安装目录和用户数据；卸载不会删除 `%LOCALAPPDATA%\HotkeyLens`。

## 便携版

解压 `HotkeyLens-Portable-win-x64.zip` 后运行 `HotkeyLens.exe`。ZIP 内含 `portable.marker`，即使机器上同时存在安装版，程序也会正确显示“便携版”。便携版与安装版共用 `%LOCALAPPDATA%\HotkeyLens`。

更新便携版时，从本 Release 下载新 ZIP，先退出 HotkeyLens，再替换解压文件；自己的文件不要放在程序目录中。

## 系统要求

- Windows 10 1809 或更高版本，或 Windows 11，x64
- 桌面会话
- 无需安装 .NET 运行时

## 已知限制

- Windows 不提供枚举其他进程全部全局热键的 API；`.lnk` 热键是注册证据，不等于运行时冲突事实。
- UI Automation 只能看到当前窗口暴露的信息；程序不会为扫描启动应用或展开菜单。
- 官方文档代表默认值，可能随产品版本变化；1.1.0 不解析 Word、Excel、PowerPoint、Chrome、Edge、Photoshop 的本地用户配置。
- 第三方应用控制软件可能拦截未签名安装器替换自身的卸载程序；此时安装器会回滚并保留旧版本可用。请在安全软件中放行安装器，不要关闭全系统防护。
- 发行文件没有代码签名，SmartScreen 可能显示“未知发布者”。SHA256 只验证完整性，不验证发布者身份。

## 安全说明

- 官方下载地址：<https://github.com/mengzi0771/HotkeyLens/releases>
- 请用同一 Release 的 `SHA256SUMS.txt` 校验所有资产。
- 不要为了运行 HotkeyLens 而关闭 SmartScreen 或杀毒软件。
- 发现可疑文件请通过仓库 Issue 反馈。

## 许可证

MIT License，Copyright (c) 2026 mengzi0771。见源码归档根目录的 `LICENSE`。
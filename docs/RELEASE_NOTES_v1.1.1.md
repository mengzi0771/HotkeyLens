# HotkeyLens 1.1.1

Released: 2026-11-25

HotkeyLens 1.1.1 is a stability release focused on application identity, Windows Terminal discovery, and safer upgrades. The Windows x64 installer and portable package remain self-contained; no .NET runtime is required.

## What changed

### Microsoft Edge identity

- Versioned paths such as `...\Microsoft\Edge\Application\<version>\msedge.exe` are normalized to the installation root, so one Edge installation is no longer discovered as two applications.
- Stable, Beta, Dev and Canary installations remain separate because their root paths and package identities differ.
- Edge now appears once in shortcut lists and conflict participants, including the normal-duplicate group for Ctrl+S.

### Windows Terminal discovery

- The Microsoft Store `wt.exe` launcher shim is resolved to the packaged `WindowsTerminal.exe` when both are present in the same `WindowsApps` package directory.
- Applications are grouped by MSIX package family, and the catalog can match either the process name, an alias (`wt.exe`), or the package family name.
- Legacy `settings.json` `keybindings` entries that use only an `id` are now read as user configuration. Previously only `command` was read, so valid user bindings were silently skipped.
- In the local verification profile, Windows Terminal now reports 16 bindings: 12 documented defaults and 4 user-configured.

### Safer upgrades

- Before starting the installer, HotkeyLens probes the installation directory with a real write test. If the directory is not writable, it reports that the current version and user data are unchanged instead of launching a likely failed upgrade.
- The installer performs its own writability preflight and restricts its close-application handling to `HotkeyLens.exe`.
- The app starts the normal interactive Inno Setup wizard with `/NORESTART`; it does not pass silent-install switches and never disables security software.
- A blocked replacement is still possible with unsigned binaries. In the tested machine, a security product blocked replacement of `unins000.exe`; the installer rolled back and the previous version remained usable. 1.1.1 makes this case visible and actionable but does not claim to bypass it.

### Validation

- 108 automated tests pass, including new tests for Edge path canonicalization, channel isolation, MSIX package identity, catalog matching by package family, and legacy Windows Terminal keybindings.
- A real local scan with the release adapters reports 171 applications, 203 shortcuts, 45 normal duplicates, and no confirmed, potential or unknown findings. Edge is one application with 31 shortcuts; Windows Terminal is one application with 16 shortcuts.

### Unchanged from 1.1.0

- Five conflict classifications: no conflict, normal duplicate, potential conflict, confirmed conflict and unknown.
- Official catalog for 11 applications, with source URL, applicable product versions, supported Windows versions and review date. Word, Excel, PowerPoint, Chrome, Edge and Photoshop are documented defaults, not parsed local user configuration.
- Optional in-app update check with Semantic Versioning, 24-hour throttling, official GitHub asset restriction and SHA256 verification.
- Snapshot and preference saves use a temporary file, flush to disk, keep a `.bak` copy, and fall back to the backup if the primary JSON is damaged.
- Light, dark and system themes; no background service, tray process, keyboard hook, or startup task.

## Install

1. Download `HotkeyLens-Setup.exe` and `SHA256SUMS.txt` from this release.
2. Verify the hash:

   ```powershell
   Get-FileHash .\HotkeyLens-Setup.exe -Algorithm SHA256
   Get-Content .\SHA256SUMS.txt
   ```

3. Run the installer. It installs for the current user and does not require administrator rights.
4. Start HotkeyLens from the final page, the Start menu, or the optional desktop shortcut.

Upgrades keep the previous installation directory and user data. Install, upgrade and uninstall do not delete `%LOCALAPPDATA%\HotkeyLens`.

## Portable

Extract `HotkeyLens-Portable-win-x64.zip` and run `HotkeyLens.exe`. The ZIP contains `portable.marker`, so the app reports Portable mode even when an installed copy exists. Portable and installed builds share `%LOCALAPPDATA%\HotkeyLens`.

To update a portable copy, download the new ZIP from this release, close HotkeyLens, and replace the extracted files. Keep your own files outside the extraction folder.

## Requirements

- Windows 10 version 1809 or later, or Windows 11, x64
- Desktop session
- No .NET runtime installation

## Known limitations

- Windows does not expose an API that enumerates every global hotkey registered by other processes. A `.lnk` hotkey is registration evidence, not proof of a runtime conflict.
- UI Automation sees only information exposed by currently open windows. HotkeyLens does not launch applications or expand menus to improve coverage.
- Official documentation describes defaults and may vary by product version. Word, Excel, PowerPoint, Chrome, Edge and Photoshop are not parsed from local user configuration in 1.1.1.
- Photoshop, JetBrains IDEs and Notepad++ were not installed on the test machine, so their adapters were not exercised against real installations in this release.
- Third-party application-control software can block an unsigned installer from replacing its own uninstaller. The installer rolls back and leaves the previous version usable. Allow the installer in the security product instead of disabling protection.
- Release binaries are not code-signed, so SmartScreen may show an "Unknown publisher" warning. SHA256 verifies integrity, not publisher identity.
- README screenshots were captured for 1.1.0. The 1.1.1 changes do not alter page layout, and screenshots were not recaptured.

## Security

- Official download location: <https://github.com/mengzi0771/HotkeyLens/releases>
- Verify every asset with the `SHA256SUMS.txt` from the same release.
- Do not disable SmartScreen or antivirus protection just to run HotkeyLens.
- Report a suspicious file through the repository's issue tracker.

## License

MIT License, Copyright (c) 2026 mengzi0771. See the `LICENSE` file in the source archive.

---

# HotkeyLens 1.1.1（简体中文）

发布日期：2026-11-25

HotkeyLens 1.1.1 是稳定性版本，重点是应用身份去重、Windows Terminal 识别和安全升级。Windows x64 安装版和便携版仍为自包含程序，不需要安装 .NET 运行时。

## 主要变化

### Microsoft Edge 身份去重

- `...\Microsoft\Edge\Application\<版本号>\msedge.exe` 会归一化到安装根目录，同一套 Edge 不再被识别成两个应用。
- Stable、Beta、Dev、Canary 的根路径和包身份不同，仍保留为独立应用。
- Edge 在快捷键列表和冲突参与者中只出现一次，包括 Ctrl+S 的正常重复组。

### Windows Terminal 识别

- 当 Microsoft Store 的 `wt.exe` 启动器与真实 `WindowsTerminal.exe` 位于同一个 `WindowsApps` 包目录时，程序会解析到真实可执行文件。
- 应用按 MSIX 包家族归并，目录可以按进程名、别名（`wt.exe`）或包家族名匹配。
- 旧式 `settings.json` 中只写 `id` 的 `keybindings` 现在会作为用户配置读取。此前只读 `command`，导致有效用户绑定被静默跳过。
- 本机验证配置中，Windows Terminal 现在读取到 16 条：12 条官方默认值和 4 条用户配置。

### 更安全的升级

- 启动安装器前，HotkeyLens 会对安装目录做真实写入探测。不可写时会明确提示当前版本和用户数据未变，而不是启动一个大概率失败的升级。
- 安装器自身也会做写权限预检，并把关闭应用的范围限制为 `HotkeyLens.exe`。
- 程序启动正常的交互式 Inno Setup 向导并显式传入 `/NORESTART`，不传静默安装参数，也绝不关闭安全软件。
- 未签名程序仍可能被拦截。本次测试机器上，安全软件拦截了替换 `unins000.exe`，安装器回滚，旧版本保持可用。1.1.1 让这种情况可见、可处理，但不声称绕过拦截。

### 验证

- 108 项自动化测试通过，新增 Edge 路径归一化、渠道隔离、MSIX 包身份、按包家族匹配目录、旧式 Windows Terminal keybindings 测试。
- 使用发行适配器进行本机真实扫描：171 个应用、203 条快捷键、45 个正常重复，确认冲突/潜在冲突/未知均为 0。Edge 为 1 个应用、31 条；Windows Terminal 为 1 个应用、16 条。

### 与 1.1.0 相同

- 五类冲突：无冲突、正常重复、潜在冲突、确认冲突、未知。
- 11 个应用的官方目录，包含来源 URL、适用产品版本、适用 Windows 版本和核对日期；Word、Excel、PowerPoint、Chrome、Edge、Photoshop 为官方默认值，不解析本地用户配置。
- 可选的应用内更新检查：SemVer 比较、24 小时节流、仅允许官方 GitHub 资产、SHA256 校验。
- 快照和偏好保存使用临时文件、刷新到磁盘、保留 `.bak`；主 JSON 损坏时回退备份。
- 浅色、深色、跟随系统主题；没有后台服务、托盘进程、键盘钩子或开机启动任务。

## 安装

1. 从本 Release 下载 `HotkeyLens-Setup.exe` 与 `SHA256SUMS.txt`。
2. 校验哈希：

   ```powershell
   Get-FileHash .\HotkeyLens-Setup.exe -Algorithm SHA256
   Get-Content .\SHA256SUMS.txt
   ```

3. 运行安装程序。它安装到当前用户，不要求管理员权限。
4. 从完成页、开始菜单或可选桌面快捷方式启动。

覆盖升级会沿用原安装目录和用户数据；安装、升级、卸载都不会删除 `%LOCALAPPDATA%\HotkeyLens`。

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
- 官方文档代表默认值，可能随产品版本变化；1.1.1 不解析 Word、Excel、PowerPoint、Chrome、Edge、Photoshop 的本地用户配置。
- 测试机器未安装 Photoshop、JetBrains IDE 和 Notepad++，本版本未对它们的真实安装进行适配器验证。
- 第三方应用控制软件可能拦截未签名安装器替换自身卸载程序；安装器会回滚并保留旧版本可用。请在安全软件中放行安装器，不要关闭全系统防护。
- 发行文件没有代码签名，SmartScreen 可能显示“未知发布者”。SHA256 只验证完整性，不验证发布者身份。
- README 截图截取自 1.1.0。1.1.1 未改变页面布局，本轮没有重新截图。

## 安全说明

- 官方下载地址：<https://github.com/mengzi0771/HotkeyLens/releases>
- 请用同一 Release 的 `SHA256SUMS.txt` 校验所有资产。
- 不要为了运行 HotkeyLens 而关闭 SmartScreen 或杀毒软件。
- 发现可疑文件请通过仓库 Issue 反馈。

## 许可证

MIT License，Copyright (c) 2026 mengzi0771。见源码归档根目录的 `LICENSE`。
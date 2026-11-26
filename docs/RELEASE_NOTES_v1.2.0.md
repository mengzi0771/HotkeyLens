# HotkeyLens 1.2.0

Released: 2026-11-27

HotkeyLens 1.2.0 turns the shortcut inventory into an everyday check-up. It adds rules for the apps people actually keep running, reads browser-extension and Snipaste configuration without modifying it, grades global hotkey competition against what is running now, and closes the loop with plain-language guidance plus switch and re-check actions. The Windows x64 installer and portable package are self-contained; no .NET runtime is required.

## What's new

### Everyday apps and read-only parsing

- The official catalog grows from 11 to 16 applications, adding WeChat, QQ, NetEase Cloud Music, QQ Music and Snipaste. Every entry carries a Chinese resolution guide, applicable versions, source and review date.
- Chrome and Edge extension shortcuts are read from the browser `Preferences` file. `global: false` commands are treated as browser-scoped, so they are not reported as global hotkey competition.
- Snipaste configuration is read as user-defined shortcuts when present.
- Parsing is read-only: HotkeyLens never edits browser or application configuration, never writes registry hotkeys, and never simulates key presses.

### Runtime state and conflict grading

- A scan now classifies each application as running, not detected, or unknown instead of forcing a boolean.
- When the process table is unusually small, for example because security software filters an unsigned build, unmatched applications become “unknown” rather than “not running”. The progress panel explains why.
- Visible top-level windows are used as a fallback, so WeChat and NetEase Cloud Music can still be recognised when their main process is filtered.
- Two applications declaring the same global hotkey and both running are graded as a medium-significance global competition. Browser-scoped bindings remain a lower-significance scope overlap. Not-running or unknown participants keep an honest low-significance note.

### Less noise, more guidance

- The shortcut page defaults to a focused view: 27 high-value global and common hotkeys instead of all 235 entries in the local verification profile.
- Every finding includes what the key does, where to change it, and whether changing it is safe. “Switch to this app”, “open its config file”, and “re-check this conflict” form a complete assisted loop.
- Activation reports whether it succeeded. Applications without a usable main window, such as a tray-only WeChat, get a clear explanation instead of a false success.
- Opening Explorer is now verified by exit code. A failed launch shows the NTSTATUS code and the exact path to open manually, instead of claiming the file was located.

### Validation

- 162 automated tests pass (108 in 1.1.1). New cases cover the three runtime states, filtered process snapshots, conflict wording, and Explorer exit-code handling.
- Local real-machine verification with WeChat, QQ, Chrome and NetEase Cloud Music running: 172 applications, 235 shortcuts, 27 global registrations, 1 potential conflict, 45 normal duplicates.
- The `Alt+A` finding (WeChat screenshot vs a browser-scoped Chrome extension command) shows both participants as running, and the switch and re-check actions work.
- README screenshots were recaptured from the real 1.2.0 dark-theme interface.

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

## Requirements

- Windows 10 version 1809 or later, or Windows 11, x64
- Desktop session
- No .NET runtime installation

## Known limitations

- Windows does not expose an API that enumerates every global hotkey registered by other processes. A `.lnk` hotkey is registration evidence, not proof of a runtime conflict.
- Security software can filter process enumeration for unsigned builds. In that case unmatched applications are shown as unknown, not as closed. Adding the program to the security product's trust list, or using a signed build, restores a complete view.
- Chrome and Edge extension commands marked browser-scoped cannot become global hotkey competition. They are reported as scope overlap only.
- UI Automation sees only information exposed by currently open windows. HotkeyLens does not launch applications or expand menus to improve coverage.
- Official documentation describes defaults and may vary by product version. Parsed local configuration is best-effort and only covers formats documented in `docs/LIMITATIONS.md`.
- Release binaries are not code-signed, so SmartScreen may show an “Unknown publisher” warning. Huorong and similar host-protection products may block an unsigned installer from creating shortcuts or its uninstall registry key. If that happens, allow the installer in the security product or use the portable package; do not disable system-wide protection.
- SHA256 verifies integrity, not publisher identity.

## Security

- Official download location: <https://github.com/mengzi0771/HotkeyLens/releases>
- Verify every asset with the `SHA256SUMS.txt` from the same release.
- Do not disable SmartScreen or antivirus protection just to run HotkeyLens.
- Report a suspicious file through the repository's issue tracker.

## License

MIT License, Copyright (c) 2026 mengzi0771. See the `LICENSE` file in the source archive.

---

# HotkeyLens 1.2.0（简体中文）

发布日期：2026-11-27

HotkeyLens 1.2.0 把“快捷键清单”变成一次日常体检。它补齐了普通人真正会常驻的软件规则，只读解析浏览器扩展和 Snipaste 配置，按当前运行状态给全局热键竞争定级，并用大白话指引加“切过去 / 复检”形成闭环。Windows x64 安装版和便携版仍为自包含程序，不需要安装 .NET 运行时。

## 主要更新

### 日常软件规则与只读解析

- 官方目录从 11 个应用扩展到 16 个，新增微信、QQ、网易云音乐、QQ音乐和 Snipaste。每条规则都带中文解决指引、适用版本、来源和核对日期。
- 从 Chrome / Edge 的 `Preferences` 读取扩展快捷键。`global: false` 的命令只按浏览器内范围处理，不会误报成全局热键竞争。
- Snipaste 配置存在时，按用户自定义快捷键读取。
- 解析全程只读：不改浏览器或应用配置，不写注册表热键，不模拟按键。

### 运行状态与冲突定级

- 扫描结果不再强行二选一，而是区分“正在运行 / 未检测到运行 / 状态未知”。
- 进程表异常偏少时（例如安全软件过滤了未签名开发版），未命中的应用标为“状态未知”，并说明原因，而不是断言用户关掉了软件。
- 增加可见顶层窗口兜底。微信、网易云音乐的主进程被过滤时，仍能通过窗口恢复识别。
- 两个应用声明同一全局热键且都在运行，标为中等显著度的“全局热键竞争”；浏览器内绑定只算作用域重叠；未运行或状态未知的参与者保留低显著度提示，不夸大风险。

### 降噪与傻瓜式闭环

- 快捷键页默认只看重点：本机验证配置下显示 27 条重点热键，而不是全部 235 条。
- 每条结果都说明“这个键做什么、去哪里改、改了是否安全”，并提供“切换到该应用”“打开配置文件”“复检此冲突”三个动作。
- 切换会如实反馈结果。微信只在托盘、没有可用主窗口时，会明确提示原因，不再假装切换成功。
- 打开资源管理器改为校验退出码。启动失败时显示 NTSTATUS 错误码和需要手动前往的完整路径，不再误报“已定位”。

### 验证

- 162 项自动化测试通过（1.1.1 为 108 项，本轮从 144 增至 162）。新增运行状态三态、受改进程快照、冲突文案和资源管理器退出码测试。
- 本机真机验证（微信、QQ、Chrome、网易云音乐同时运行）：172 个应用、235 条快捷键、27 条全局注册、1 条潜在冲突、45 条正常重复。
- `Alt+A`（微信截图 vs Chrome 扩展的浏览器内命令）双方均显示“正在运行”，“切换到该应用”和“复检此冲突”均可用。
- README 截图已用 1.2.0 真实深色界面重新截取。

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

## 系统要求

- Windows 10 1809 或更高版本，或 Windows 11，x64
- 桌面会话
- 无需安装 .NET 运行时

## 已知限制

- Windows 不提供枚举其他进程全部全局热键的 API；`.lnk` 热键是注册证据，不等于运行时冲突事实。
- 安全软件可能过滤未签名程序的进程枚举。此时未命中的应用显示“状态未知”，而不是“已关闭”。把程序加入安全软件信任列表，或使用签名版本，可以恢复完整视图。
- Chrome / Edge 中标为浏览器内的扩展快捷键不会成为全局热键竞争，只会提示作用域重叠。
- UI Automation 只能看到当前窗口暴露的信息；程序不会为扫描启动应用或展开菜单。
- 官方文档代表默认值，可能随产品版本变化；本地配置解析为尽力而为，覆盖范围以 `docs/LIMITATIONS.md` 为准。
- 发行文件没有代码签名，SmartScreen 可能显示“未知发布者”。火绒等主机防护可能拦截未签名安装器创建快捷方式或写入卸载注册表项。遇到拦截时，请在安全软件中放行安装器，或改用便携版；不要关闭全系统防护。
- SHA256 只验证完整性，不验证发布者身份。

## 安全说明

- 官方下载地址：<https://github.com/mengzi0771/HotkeyLens/releases>
- 请用同一 Release 的 `SHA256SUMS.txt` 校验所有资产。
- 不要为了运行 HotkeyLens 而关闭 SmartScreen 或杀毒软件。
- 发现可疑文件请通过仓库 Issue 反馈。

## 许可证

MIT License，Copyright (c) 2026 mengzi0771。见源码归档根目录的 `LICENSE`。

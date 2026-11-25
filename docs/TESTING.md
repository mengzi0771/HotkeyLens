# 测试说明

## 1. 测试环境

- Windows 11 build 10.0.26300，x64
- .NET SDK 10.0.404
- 中文系统区域设置
- 运行桌面：120 DPI
- 第三方安全软件：火绒（主机防御进程运行中）

## 2. 自动化测试

项目位置：

```
tests/HotkeyLens.Core.Tests/HotkeyLens.Core.Tests.csproj
```

运行命令：

```powershell
dotnet test tests/HotkeyLens.Core.Tests/HotkeyLens.Core.Tests.csproj -c Release `
  --nologo -p:_MSTestEnableParentProcessQuery=false
```

该参数需要参与构建，以便写入 testhost 的运行时配置；不要先 `--no-build` 再依赖它。

`_MSTestEnableParentProcessQuery=false` 用于绕开 Test Platform 在当前受限运行环境中尝试监听测试宿主父进程句柄时出现的 `Access denied`。这不是产品测试失败，也不影响被测程序。

### 最近一次结果（1.1.0）

```
失败: 0，通过: 98，已跳过: 0，总计: 98
```

1.1.0 新增覆盖：五类冲突（含 Word/Excel/Chrome 同时 Ctrl+S）、同应用上下文重叠、v1 快照迁移、`.bak` 备份恢复、偏好更新字段、SemVer、24 小时节流、GitHub API 限流/网络异常/draft/prerelease、官方资产 URL、SHA256 成功/不匹配/非官方资产、取消检查。

### 历史结果（1.0.0）

```
失败: 0，通过: 67，已跳过: 0，总计: 67
```

### 覆盖范围

| 测试文件 | 覆盖内容 |
|---|---|
| `ShortcutGestureTests` | 组合键解析、规范化、虚拟键映射、无效输入 |
| `ShortcutLinkReaderTests` | Shell Link 头读取、热键位标志、坏文件/短文件 |
| `CatalogTests` | 11 个应用的目录资源加载、所有键位可解析、来源完整性 |
| `ConflictAnalyzerTests` | 五类冲突规则、跨应用正常重复、同应用上下文重叠、全局注册证据、序列/禁用忽略、稳定 ID |
| `ShortcutQueryTests` | 搜索字段、筛选、分类、来源、置信度、冲突过滤、排序 |
| `ScanEngineTests` | 三种应用状态、适配器异常隔离、未知应用剔除、去重、取消、进度、诊断 |
| `PersistenceTests` | 首次保存、覆盖保存、新实例读取、损坏 JSON、版本不匹配、并发保存、失败保留旧数据、无 tmp 残留、偏好保存 |
| `JsonStoreTests` | `.bak` 生成与恢复、v1 快照迁移、偏好更新字段、Clear 清理备份 |
| `UpdateServiceTests` | SemVer、节流、跳过、限流、网络失败、draft/prerelease、官方 URL、SHA256 成功/不匹配/拒绝、取消 |
| `DiscoverySmokeTests` | 真实本机应用发现、非空、ID/路径去重、Shell 应用存在 |

### 测试隔离

测试临时目录使用 `AppContext.BaseDirectory\.test-temp`，每个测试使用独立 GUID 子目录，测试结束递归删除。测试数据与真实 `%LOCALAPPDATA%\HotkeyLens` 隔离。测试产物已加入 `.gitignore`。

## 3. 真实桌面验收

自动化测试不覆盖 WPF 窗口交互，以下验收使用真实构建、真实本机扫描数据和 Windows UI Automation 完成。

### 3.1 启动与导航

- 启动 `HotkeyLens.exe`，主窗口标题 `HotkeyLens — Discover Every Shortcut`；
- 主导航五个页面均可选择，页面标题和内容同步；
- 关闭窗口后进程退出，未残留托盘图标或后台快捷键监听；
- 单实例互斥在代码中实现，避免两个实例同时写入同一快照；
- 同一机器上重复启动会提示已有实例并退出（此行为由代码实现，未进行独立的双实例压力测试）。

### 3.2 真实扫描

- 使用真实本机应用发现，得到 173 个应用；
- 快捷键来源覆盖 6 个应用，4 个应用配置为空，167 个应用没有可读取来源；
- 读取到 53 条快捷键，冲突分析为 0 组；
- 扫描期间窗口保持响应，进度与阶段文本更新；
- 扫描完成后显示真实时间、耗时、引擎版本和统计卡；
- “无法自动读取”的应用正常展示，没有被忽略。

这些数字是验收机器的即时结果，不是产品固定承诺。

### 3.3 搜索、筛选与详情

- 在快捷键页面输入 `F12`，结果从 53 条筛到 3 条，计数显示 `3 / 53 条`；
- 选中条目后右侧详情显示按键、应用、功能、范围、来源、可信度、识别依据、上下文、原始值、抓取时间和相关冲突；
- 详情中的“打开来源”和“打开设置位置”使用已有真实路径；
- 关闭详情按钮正常，显示 X 图标；
- 清除筛选后恢复完整列表。

### 3.4 主题

- 设置页主题下拉包含“跟随系统、浅色、深色”；
- 选择“浅色”后，标题栏、侧栏、主内容、卡片、列表、输入框、按钮、开关、滚动条和详情面板立即切换；
- `preferences.json` 自动写入 `"theme": "Light"`；
- 关闭并重新启动后，浅色主题和扫描快照自动恢复；
- 深色主题下逐页检查总览、快捷键、应用程序、冲突分析和设置，未发现浅色残留；
- 当前验证覆盖 Light/Dark；“跟随系统”在启动时解析当前 Windows 主题，运行中改变系统主题未单独做实时切换测试。

截图：

| 页面 | 浅色 | 深色 |
|---|---|---|
| 总览 | `artifacts/ui-final-light-dashboard-2.png` | `artifacts/ui-final-dark-dashboard.png` |
| 快捷键 | `artifacts/ui-final-light-shortcuts-2.png` | `artifacts/ui-final-dark-shortcuts.png` |
| 应用程序 | `artifacts/ui-final-light-applications.png` | `artifacts/ui-final-dark-applications-2.png` |
| 冲突分析 | 未单独截图 | `artifacts/ui-final-dark-conflicts.png` |
| 设置 | 未单独截图 | `artifacts/ui-final-dark-settings.png` |

验收截图同时复制到 `docs/screenshots`，README 中使用的是经过确认的同一批截图。

### 3.5 保存、覆盖和重启

在使用可控数据目录 `HOTKEYLENS_DATA_DIR=D:\Project\HotkeyLens\artifacts\ui-verify` 时：

1. 首次扫描：生成 `scan-snapshot.json`，文件大小 249435 字节，无 `.tmp`；
2. 重复扫描：文件被原子替换，修改时间更新，界面仍然正常；
3. 重启：新进程读取已有快照，显示 53 条快捷键；
4. 失败保护：用独占文件流锁住目标快照后触发扫描，界面显示可操作错误，旧文件大小和修改时间不变，且没有残留临时文件；
5. 解除锁后再次扫描，保存恢复成功。

失败保护的界面提示包含目标路径、目录、权限/实例冲突、安全软件提示、旧数据不会被覆盖，以及底层系统错误。

### 3.6 默认数据目录的安全软件拦截

真实桌面测试发现：当前机器上，由本次会话构建的未签名 .NET 进程写入 `%LOCALAPPDATA%\HotkeyLens` 时，`SafeFileHandle.CreateFile` 返回 `ACCESS_DENIED`。独立的最小 FileStream probe 也复现；同一路径由 PowerShell 创建文件则成功。Windows Defender 服务未运行，火绒 `HipsDaemon`/`HipsTray` 正在运行。

因此，默认目录无法在当前机器完成保存，原因是第三方主机防御/应用控制对未受信任进程的拦截，不是目录 ACL、文件锁或原子替换实现。HotkeyLens 没有通过改变路径、关闭保存或忽略异常来伪装成功：

- 保存失败会明确提示；
- 原有快照保持不变；
- 用户在安全软件中放行 HotkeyLens 后可恢复正常；
- 验证所用的 `HOTKEYLENS_DATA_DIR` 只用于可重复测试，不是生产默认。

这一点属于当前验收环境限制，需要在发布说明中保留。

### 3.7 安装、升级与卸载（GitHub 干净 Runner）

本机上的 Inno Setup 安装器无法启动，连最小探针也被安全软件环境拦截，因此生命周期验收在 GitHub 托管的干净 Windows x64 Runner 上完成：

- 工作流：`.github/workflows/verify-installer.yml`
- 运行：`45424781224`（commit `baece74`），时长约 3 分 21 秒，全部步骤成功
- 环境：Windows Server 2025 x64，.NET SDK 10.0.404，Inno Setup 6.7.1

实际输出：

```
SetupSha256Verified: True
first.InstalledVersion: 1.0.0
first.Shortcuts: desktop + start menu
upgrade.InstalledVersion: 1.0.1
upgrade.Shortcuts: desktop + start menu
UpgradePreservedData: True
installed.Scan: apps=147, shortcuts=20, conflicts=0
installed.ExitCode: 0
Uninstall: program files and shortcuts removed; external data preserved
portable.Scan: apps=146, shortcuts=20, conflicts=0
portable.ExitCode: 0
PortableArchive: launched without an installed .NET runtime
Status: PASS
```

覆盖内容包括：静默安装、桌面与开始菜单快捷方式、安装目录不含用户数据、1.0.0 → 1.0.1 覆盖升级、升级后数据保留、安装版与便携版真实扫描与正常退出、卸载删除程序文件和快捷方式、卸载保留外部用户数据。该工作流的 `hotkeylens-v1.0.0` artifact 保留了与验证完全一致的安装包和便携包。

首次运行 `45422582440` 同样通过（3 分 19 秒）；第二次运行增加了“上传已验证发行包”的步骤并再次通过。唯一注解是 GitHub 关于 Node.js 20 Actions 的弃用提示，与本项目无关。

本机安装器无法启动的原因仍是火绒行为拦截，不能据此判定安装包损坏：同一个 Setup 在干净 Runner 上完成了全部安装和卸载步骤。

### 3.8 标签触发的发行流程

`.github/workflows/release.yml` 已用正式标签实际运行验证，而不是只提交 YAML：

- 标签：`v1.0.0`（提交 `e97b8ea`）
- 运行：`45439040247`，约 2 分 16 秒
- 结果：全部 10 个构建步骤成功，包括 67 项自动测试、自包含构建、同包生命周期验证、artifact 上传和草稿 Release 创建。

同一运行中，最终上传的安装包先接受验证，再授权进入 Release。验证输出：

```
SetupSha256Verified: True
first.InstalledVersion: 1.0.0
first.Shortcuts: desktop + start menu
installed.Scan: apps=147, shortcuts=20, conflicts=0
installed.ExitCode: 0
Uninstall: program files and shortcuts removed; external data preserved
PortableArchive: launched without an installed .NET runtime
portable.Scan: apps=146, shortcuts=20, conflicts=0
portable.ExitCode: 0
Status: PASS
```

发布流程默认创建草稿 Release，不直接公开，避免未经确认的版本被自动发布。`v1.0.0` 草稿资产：

| 文件 | 字节数 | SHA256 |
|---|---:|---|
| `HotkeyLens-Setup.exe` | 45,741,242 | `ef7571d56eb368dffe9a792f71776592abc43c823df1ce58349b50706ce4cceb` |
| `HotkeyLens-Portable-win-x64.zip` | 65,318,509 | `52515a80f1a92db91d14052bf8581e8444ff743ae37bd5b9788924e30eb837b1` |
| `SHA256SUMS.txt` | 187 | 内容与上表一致 |

该 Release 在核验时 `isDraft=true`，因此下载入口在正式公开前不可用；许可证决定后再发布。

## 4. 构建验证

```powershell
dotnet build HotkeyLens.sln -c Release --nologo
```

最终验收结果：

```
已成功生成。
    0 个警告
    0 个错误
```

Release 测试结果：

```
已通过! - 失败: 0，通过: 67，已跳过: 0，总计: 67，持续时间: 333 ms
```

## 5. 尚未自动化的部分

- WPF 窗口控件级自动化未纳入 `dotnet test`；本轮使用外部 UIA 脚本进行真实窗口操作，脚本位于被忽略的 `artifacts`，不随仓库发布；
- 未做多显示器、200% 以上 DPI、Windows 10 22H2 的完整回归；
- 未对真实第三方软件配置的所有版本组合进行矩阵测试；
- 未做长时间扫描压力测试；
- 安装器生命周期已在 GitHub 干净 Runner 验证，但 WPF 控件级交互没有纳入 CI；当前 Runner 是 Windows Server 2025，不是 Windows 10/11 桌面环境；
- 未在 CI 上覆盖主题切换和界面截图。这些项目已在真实桌面手动/UIA 验收，但不能替代完整回归。

这些项目在后续开发中应补充，但不能用推测替代。
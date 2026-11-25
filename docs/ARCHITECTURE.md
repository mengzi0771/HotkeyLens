# HotkeyLens 架构说明

## 1. 目标与边界

HotkeyLens 的职责是：发现本机应用，从可读取的静态来源和当前 UI 中收集快捷键，统一规范化和标注证据，再提供浏览、筛选、搜索与冲突分类。

它不是一个全局键盘监控器，也不是系统级热键注册表浏览器。架构刻意把“读取到的证据”和“推测的结论”分开，避免把未知信息包装成事实。

## 2. 解决方案结构

```
HotkeyLens.sln
├── src/
│   ├── HotkeyLens.Core/
│   │   ├── Abstractions/    扫描、存储和偏好的接口
│   │   ├── Adapters/        快捷键来源适配器
│   │   ├── Analysis/        冲突分析与查询
│   │   ├── Catalog/         内置官方快捷键目录
│   │   ├── Data/            嵌入的 official-shortcuts.json
│   │   ├── Discovery/       应用发现、快捷方式读取、配置定位
│   │   ├── Models/          领域模型和规范化
│   │   ├── Persistence/     JSON 快照与偏好
│   │   └── Scanning/        扫描编排
│   └── HotkeyLens.App/
│       ├── Assets/          应用图标
│       ├── Converters/      XAML 值转换器
│       ├── Infrastructure/  ObservableObject、命令
│       ├── Services/        文件操作、主题切换
│       ├── Themes/          Light / Dark / Styles
│       ├── ViewModels/
│       ├── Views/
│       └── App.xaml, MainWindow.xaml
└── tests/
    └── HotkeyLens.Core.Tests/
```

Core 的 `TargetFramework` 是 `net10.0-windows` 并启用 WPF SDK，因为 UI Automation 程序集属于 Windows Desktop 组件；它不包含任何 WPF 界面代码，也不引用 App。依赖方向始终是 App → Core。

## 3. 扫描流程

```
用户点击“开始扫描”
        │
        ▼
MainViewModel.ScanAsync
        │ 创建 ScanOptions、取消令牌、进度回调
        ▼
ScanEngine.ScanAsync
        │
        ├─ IApplicationDiscoveryService.DiscoverAsync
        │      └─ 注册表 + App Paths + 快捷方式 + Shell 内置应用
        │
        ├─ 依次调用已启用的 IShortcutSourceAdapter
        │      ├─ AdapterScanResult（条目/覆盖应用/诊断）
        │      ├─ 单个适配器异常 → Error 诊断，继续其他来源
        │      └─ 进度 10% → 95%
        │
        ├─ 合并条目、按 Id 去重
        ├─ 移除指向未知应用的条目
        ├─ ShortcutConflictAnalyzer.Analyze
        └─ 生成 ScanSnapshot + 统计
        │
        ▼
JsonScanSnapshotStore.SaveAsync（原子替换）
        │
        ▼
MainViewModel.ApplySnapshot（UI 线程）
```

扫描在异步任务中执行；进度通过 `IProgress<ScanProgress>` 回传，视图模型只在 UI 线程更新绑定属性。用户取消时抛出 `OperationCanceledException` 并保留上一次成功快照。窗口关闭时取消当前扫描。

## 4. 应用发现

`ApplicationDiscoveryService` 汇总多个 `ApplicationCandidate`：

1. 注册表卸载项（HKLM/HKCU 的 64/32 位视图）。
2. `App Paths`。
3. 开始菜单与桌面的 `.lnk`。
4. Windows 内置 Shell 应用。

候选经 `ApplicationMerger` 合并：

- 有可执行文件路径时，按规范化路径去重；
- 无路径时，按应用名和发布者；
- 来源优先级为 `WindowsBuiltIn > StartMenu > AppPath > Registry`；
- 合并后排除明显的运行时、更新器、崩溃处理程序等组件。

`KnownConfigurationLocations` 只为已知应用定位真实存在的配置文件，不创建配置，也不假定安装路径。配置定位结果填充应用的 `SettingsPath` 和 `SettingsHint`，供界面提供“打开设置位置”。

## 5. 来源适配器

所有适配器实现：

```csharp
public interface IShortcutSourceAdapter
{
    string Id { get; }
    string DisplayName { get; }
    Task<AdapterScanResult> ScanAsync(
        IReadOnlyList<DiscoveredApplication> applications,
        ScanContext context,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}
```

现有 7 类适配器：

| 类 | 说明 |
|---|---|
| `OfficialShortcutAdapter` | 内置官方目录，按进程名匹配，来源标记为官方默认 |
| `ShortcutFileAdapter` | 读取 `.lnk` 的 Shell Link 头与 WScript 目标；热键表示实际注册的全局热键 |
| `VsCodeKeybindingsAdapter` | 解析 JSONC 用户 keybindings，支持 when、移除指令和按键序列 |
| `WindowsTerminalSettingsAdapter` | 解析 `actions` / `keybindings` 与字符串/对象命令 |
| `JetBrainsKeymapAdapter` | 安全解析 XML keymap，只读取显式用户覆盖 |
| `NotepadPlusPlusShortcutsAdapter` | 解析 `shortcuts.xml` 的快捷键与宏 |
| `UiAutomationShortcutAdapter` | 只扫描当前打开的窗口和可访问菜单属性 |

### 故障隔离

单个应用配置解析失败、单个窗口不可访问、单个适配器抛异常，都不会中止整体扫描：

- 解析级错误写为 `ScanDiagnostic`；
- 适配器级异常由 `ScanEngine` 捕获并写 Error 诊断；
- 没有来源覆盖的应用标为 `NotScannable`，不生成条目；
- 来源覆盖但未发现条目的应用标为 `NoShortcutsFound`。

## 6. 模型与规范化

### ShortcutGesture

`ShortcutGesture` 保存结构化修饰键、按键名、虚拟键码和规范化字符串。`TryParse` 接受 `Ctrl+Shift+K` 等格式，输出固定顺序（Ctrl、Alt、Shift、Win），避免同一组合因书写顺序不同而产生重复记录。

`KeySequenceParser` 处理 VS Code 的 `Ctrl+K Ctrl+S` 等序列，将首段保存为 `Gesture`，完整序列保存为 `KeySequence`。无法解析的组合不会被猜测。

### 由来与可信度

`ShortcutOrigin` 表达“这个值从哪里来”：

- `OfficialDefault`
- `UserConfigured`
- `RuntimeObserved`
- `Unknown`

`ConfidenceLevel` 表达“这条记录有多可信”，适配器按来源证据分别赋值，并写入人类可读的 `ConfidenceRationale`。两者独立，不能用“官方默认”替代“用户实际配置”。

### 扫描状态

`ApplicationScanResult` 使用 `ScanState`：

- `Scanned`：至少一条快捷键；
- `NoShortcutsFound`：来源已覆盖，但没有条目；
- `NotScannable`：没有可用来源，明确说明原因；
- `Failed`：扫描或解析失败。

这些状态直接展示在应用程序页面，避免“没有读到”被误解为“没有快捷键”。

## 7. 冲突分析

`ShortcutConflictAnalyzer` 的输入是完整快捷键集合：

1. 排除禁用条目和多段序列；
2. 按 `Gesture.Canonical` 分组，保留所有至少两条目的分组；
3. 在每个应用内部检查同一按键是否绑定不同功能，仅当上下文为空或相等时才视为可能重叠；
4. 跨应用检查作用范围与注册证据；
5. 输出五类结果之一，并记录分类依据、参与者和置信度。

判定逻辑：

- 同应用、不同功能、上下文可能重叠：涉及 `Unknown` → `Unknown`；有可靠本地证据 → `ConfirmedConflict`；否则 → `PotentialConflict`；
- 跨应用至少两个 `Global` + `ShortcutFile` → `ConfirmedConflict` / `GlobalHotkeyCompetition`；
- 否则存在 `Global` → `PotentialConflict`；
- 全部 `Unknown` → `Unknown`；
- 其余 → `NormalDuplicate` / `DuplicateBinding`。

`NormalDuplicate` 与 `NoConflict` 不计入冲突统计。相同按键本身永远不直接构成冲突。分析结果包含参与应用、按键、功能、范围、上下文、依据、可信度和处理建议。冲突 ID 由类别、分类、规范按键和排序后的参与者 ID 生成，保证稳定。

## 8. 查询

`ShortcutQuery.Apply` 是无状态内存查询：

- 搜索覆盖应用名、动作、描述、按键、来源、上下文、依据和原始值；
- 过滤器包括应用、分类、范围、来源类型、最低可信度、由来和“只看冲突”；
- 冲突关联通过预计算的 `(applicationId, canonical)` 集合实现；
- 排序覆盖按键、应用、功能、范围、可信度和来源；
- 归一化时去除空格和 `+`，英文转大写，保留中文。

## 9. 持久化

扫描结果使用单文件 JSON 快照，而不是 SQLite。原因：

- 应用只需要“最近一次完整快照”，没有关系查询或事务需求；
- 单文件便于备份、迁移和用户检查；
- 避免引入数据库依赖和数据库文件生命周期问题。

保存流程：

1. 在目标目录创建唯一临时文件；
2. `FileMode.CreateNew`、`FileShare.None`、`WriteThrough`；
3. 序列化后 Flush，确保数据落盘；
4. 优先 `File.Replace(..., backupPath)`，保留一份 `.bak`；
5. 不支持或重试耗尽时，把旧文件移到 `.bak`，再移入新文件；失败则尝试还原备份；
6. 失败时删除本次临时文件，旧快照保持完整；
7. IO/权限异常包装为带路径、权限、安全软件提示的可操作错误。

读取时先解主文件，主文件缺失或损坏再解 `.bak`。读取 1.0.0 的 schemaVersion 1 快照会迁移分类与统计。`preferences.json` 保存主题、扫描开关、超时、窗口上限、自动检查更新、上次检查时间和跳过版本，使用同样的临时文件、备份与替换机制。

更新相关实现位于 `Core/Updates`：`SemanticVersion` 负责数值比较，`GitHubReleaseClient` 只读取稳定 Release 并校验官方资产 URL，`UpdateService` 负责 24 小时节流/跳过版本/状态，`UpdateDownloadService` 负责 HTTPS 下载、SHA256SUMS 校验与临时文件清理。App 层通过 `InstallationModeDetector` 区分安装版与便携版，并由 `UpdateViewModel` 驱动更新窗口。

App 使用 `Local\HotkeyLens.SingleInstance` 互斥量避免两个实例同时扫描和保存。`HOTKEYLENS_DATA_DIR` 可在测试或便携场景覆盖数据目录。

## 10. 界面与主题

- `MainViewModel` 持有当前快照、查询结果、导航和命令；页面只做绑定。
- `SettingsViewModel` 管理偏好和主题；主题选择立即应用、立即保存，其他扫描选项由保存按钮提交。
- `ThemeManager` 替换 MergedDictionaries 中的 Light/Dark 资源字典；所有颜色通过 DynamicResource 使用统一 key。
- 自定义 `WindowChrome` 保留原生窗口行为；主题化 ScrollBar、按钮、输入框、列表和 `ConfirmDialog` 均使用同一套资源。
- 关闭窗口触发 `MainViewModel.Shutdown`，取消扫描，App 退出，无托盘或后台服务。

## 11. 安全设计

- 扫描代码只读取公开文件、快捷方式属性和 UI Automation。
- 不调用 `RegisterHotKey` 枚举、不安装 `WH_KEYBOARD_LL`、不读取其他进程内存。
- 不写注册表，不修改第三方应用配置。
- 快捷键管理只提供“打开设置位置”；不承诺自动改写。
- UI Automation 只访问当前窗口，不启动应用、不展开菜单，并设置整体超时。

## 12. 扩展方式

新增软件支持：

1. 实现 `IShortcutSourceAdapter`；
2. 在 `AdapterScanResult` 中声明覆盖的应用和诊断；
3. 提供可核实的来源、可信度和理由；
4. 在 App 的适配器装配列表中注册；
5. 增加解析、规范化与失败场景测试。

扫描引擎、冲突分析和 UI 不需要修改。若新增的是“从配置文件读取”的同类软件，优先复用 `TextFileReader` 和 `KeySequenceParser`。
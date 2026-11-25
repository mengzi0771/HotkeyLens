using HotkeyLens.Core.Models;

namespace HotkeyLens.App.ViewModels;

public enum PageKind
{
    Dashboard,
    Shortcuts,
    Applications,
    Conflicts,
    Settings
}

public sealed record OptionItem(string Label, string Value)
{
    public override string ToString() => Label;
}

public sealed record NavItem(PageKind Page, string Label, string Glyph)
{
    public override string ToString() => Label;
}

public sealed record DistributionItem(string Label, int Count, double Percent, string AccentHex);

public static class DisplayLabels
{
    public static string Scope(ShortcutScope scope) => scope switch
    {
        ShortcutScope.Global => "全局",
        ShortcutScope.Application => "应用内",
        ShortcutScope.Window => "窗口内",
        ShortcutScope.Contextual => "上下文",
        _ => "未知"
    };

    public static string Origin(ShortcutOrigin origin) => origin switch
    {
        ShortcutOrigin.OfficialDefault => "官方默认",
        ShortcutOrigin.UserConfigured => "用户配置",
        ShortcutOrigin.RuntimeObserved => "运行时观察",
        _ => "未知"
    };

    public static string Confidence(ConfidenceLevel confidence) => confidence switch
    {
        ConfidenceLevel.High => "高",
        ConfidenceLevel.Medium => "中",
        ConfidenceLevel.Low => "低",
        _ => "未知"
    };

    public static string Source(ShortcutSourceKind source) => source switch
    {
        ShortcutSourceKind.ShortcutFile => "快捷方式热键",
        ShortcutSourceKind.UserConfiguration => "用户配置文件",
        ShortcutSourceKind.UiAutomation => "UI Automation",
        ShortcutSourceKind.OfficialDocumentation => "官方文档",
        ShortcutSourceKind.ApplicationInterface => "应用公开接口",
        ShortcutSourceKind.Observed => "运行时观察",
        _ => "未知"
    };

    public static string Category(ApplicationCategory category) => category switch
    {
        ApplicationCategory.System => "系统",
        ApplicationCategory.Productivity => "效率办公",
        ApplicationCategory.Development => "开发工具",
        ApplicationCategory.Browser => "浏览器",
        ApplicationCategory.Communication => "通讯",
        ApplicationCategory.Media => "影音",
        ApplicationCategory.Graphics => "图形设计",
        ApplicationCategory.Utility => "实用工具",
        ApplicationCategory.Security => "安全",
        _ => "其他"
    };

    public static string ApplicationSource(ApplicationSourceKind source) => source switch
    {
        ApplicationSourceKind.WindowsBuiltIn => "Windows 内置",
        ApplicationSourceKind.Registry => "注册表",
        ApplicationSourceKind.StartMenu => "开始菜单",
        ApplicationSourceKind.AppPath => "App Paths",
        ApplicationSourceKind.Package => "应用包",
        _ => "未知"
    };

    public static string ScanStateLabel(ScanState state) => state switch
    {
        ScanState.NotRun => "尚未扫描",
        ScanState.Scanned => "已读取快捷键",
        ScanState.NoShortcutsFound => "未发现快捷键",
        ScanState.NotScannable => "无法自动读取",
        ScanState.Failed => "读取失败",
        _ => "未知"
    };

    public static string ConflictKindLabel(ConflictKind kind) => kind switch
    {
        ConflictKind.GlobalHotkeyCompetition => "全局注册竞争",
        ConflictKind.SameApplicationOverlap => "同应用按键重叠",
        ConflictKind.PotentialConflict => "潜在作用域重叠",
        ConflictKind.DuplicateBinding => "跨应用正常重复",
        ConflictKind.Unknown => "证据不足",
        _ => "未知"
    };

    public static string ConflictClassificationLabel(ConflictClassification classification) => classification switch
    {
        ConflictClassification.NoConflict => "无冲突",
        ConflictClassification.NormalDuplicate => "正常重复",
        ConflictClassification.PotentialConflict => "潜在冲突",
        ConflictClassification.ConfirmedConflict => "确认冲突",
        _ => "未知"
    };

    public static string ConflictTypeLabel(ConflictClassification classification, ConflictKind kind)
        => $"{ConflictClassificationLabel(classification)} · {ConflictKindLabel(kind)}";

    public static string Severity(ConflictSeverity severity) => severity switch
    {
        ConflictSeverity.High => "高",
        ConflictSeverity.Medium => "中",
        ConflictSeverity.Low => "低",
        _ => "提示"
    };

    public static string DiagnosticSeverityLabel(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "错误",
        DiagnosticSeverity.Warning => "注意",
        _ => "信息"
    };

    public static string ConflictAccent(ConflictKind kind) => kind switch
    {
        ConflictKind.GlobalHotkeyCompetition => "#D1495B",
        ConflictKind.SameApplicationOverlap => "#D1495B",
        ConflictKind.PotentialConflict => "#C47F17",
        _ => "#2F6FDB"
    };

    public static string ConflictAccent(ConflictClassification classification) => classification switch
    {
        ConflictClassification.ConfirmedConflict => "#D1495B",
        ConflictClassification.PotentialConflict => "#C47F17",
        ConflictClassification.NormalDuplicate => "#2F6FDB",
        ConflictClassification.Unknown => "#8792A6",
        _ => "#8792A6"
    };
}
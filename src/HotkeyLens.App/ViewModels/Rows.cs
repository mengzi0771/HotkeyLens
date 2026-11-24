using HotkeyLens.Core.Models;

namespace HotkeyLens.App.ViewModels;

public sealed class ShortcutRowViewModel
{
    private readonly IReadOnlyList<ShortcutConflict> _conflicts;

    public ShortcutRowViewModel(ShortcutEntry entry, IReadOnlyList<ShortcutConflict> conflicts)
    {
        Entry = entry;
        _conflicts = conflicts;
    }

    public ShortcutEntry Entry { get; }
    public string DisplayGesture => Entry.DisplayGesture;
    public string ApplicationName => Entry.ApplicationName;
    public string ActionName => Entry.ActionName;
    public string Description => string.IsNullOrWhiteSpace(Entry.Description)
        ? "没有提供更详细的功能说明。"
        : Entry.Description;
    public string ScopeLabel => DisplayLabels.Scope(Entry.Scope);
    public string OriginLabel => DisplayLabels.Origin(Entry.Origin);
    public string ConfidenceLabel => DisplayLabels.Confidence(Entry.Confidence);
    public string SourceLabel => string.IsNullOrWhiteSpace(Entry.SourceName)
        ? DisplayLabels.Source(Entry.SourceKind)
        : Entry.SourceName;
    public string SourceKindLabel => DisplayLabels.Source(Entry.SourceKind);
    public string SourceReference => string.IsNullOrWhiteSpace(Entry.SourceReference)
        ? "未记录具体来源位置"
        : Entry.SourceReference;
    public bool HasSourceReference => !string.IsNullOrWhiteSpace(Entry.SourceReference);
    public string ContextText => string.IsNullOrWhiteSpace(Entry.Context) ? "无特定上下文" : Entry.Context;
    public string ConfidenceRationale => string.IsNullOrWhiteSpace(Entry.ConfidenceRationale)
        ? "没有记录可信度依据。"
        : Entry.ConfidenceRationale;
    public string CapturedAtText => Entry.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string EnabledLabel => Entry.IsEnabled ? "启用" : "已禁用";
    public string RawValueText => string.IsNullOrWhiteSpace(Entry.RawValue) ? "无" : Entry.RawValue;
    public bool HasRawValue => !string.IsNullOrWhiteSpace(Entry.RawValue);
    public bool IsSequence => Entry.IsSequence;
    public bool IsUserConfigured => Entry.Origin == ShortcutOrigin.UserConfigured;
    public bool IsOfficialDefault => Entry.Origin == ShortcutOrigin.OfficialDefault;
    public bool IsRuntimeObserved => Entry.Origin == ShortcutOrigin.RuntimeObserved;

    public int ConflictCount => _conflicts.Count;
    public bool IsConflicting => _conflicts.Count > 0;
    public string ConflictSummary => !IsConflicting
        ? "未发现与其他应用的重复或潜在冲突。"
        : $"参与 {_conflicts.Count} 组重复或冲突分析：{string.Join("、", _conflicts.Select(c => DisplayLabels.ConflictKindLabel(c.Kind)).Distinct())}";

    public IReadOnlyList<ConflictRowViewModel> RelatedConflicts =>
        _conflicts.Select(conflict => new ConflictRowViewModel(conflict)).ToList();

    public override string ToString() => $"{DisplayGesture}，{ApplicationName}，{ActionName}";
}

public sealed class ParticipantRowViewModel
{
    public ParticipantRowViewModel(ConflictParticipant participant)
    {
        ApplicationName = participant.ApplicationName;
        ActionName = participant.ActionName;
        ScopeLabel = DisplayLabels.Scope(participant.Scope);
        OriginLabel = DisplayLabels.Origin(participant.Origin);
        ConfidenceLabel = DisplayLabels.Confidence(participant.Confidence);
        SourceLabel = DisplayLabels.Source(participant.SourceKind);
    }

    public string ApplicationName { get; }
    public string ActionName { get; }
    public string ScopeLabel { get; }
    public string OriginLabel { get; }
    public string ConfidenceLabel { get; }
    public string SourceLabel { get; }

    public override string ToString() => $"{ApplicationName}，{ActionName}，{ScopeLabel}";
}

public sealed class ConflictRowViewModel
{
    public ConflictRowViewModel(ShortcutConflict conflict)
    {
        Conflict = conflict;
        Participants = conflict.Participants
            .Select(participant => new ParticipantRowViewModel(participant))
            .ToList();
    }

    public ShortcutConflict Conflict { get; }
    public string GestureText => Conflict.Gesture.Display;
    public string KindLabel => DisplayLabels.ConflictKindLabel(Conflict.Kind);
    public string SeverityLabel => DisplayLabels.Severity(Conflict.Severity);
    public string ConfidenceLabel => DisplayLabels.Confidence(Conflict.Confidence);
    public string Summary => Conflict.Summary;
    public string Rationale => Conflict.Rationale;
    public string Recommendation => Conflict.Recommendation;
    public string AccentHex => DisplayLabels.ConflictAccent(Conflict.Kind);
    public bool IsRegistrationRace => Conflict.Kind == ConflictKind.GlobalHotkeyCompetition;
    public bool IsPotential => Conflict.Kind == ConflictKind.PotentialConflict;
    public IReadOnlyList<ParticipantRowViewModel> Participants { get; }
    public string ParticipantCountText => $"{Participants.Count} 个应用";

    public override string ToString() => $"{GestureText}，{KindLabel}，{Summary}";
}

public sealed class ApplicationRowViewModel
{
    public ApplicationRowViewModel(DiscoveredApplication application, ApplicationScanResult? result)
    {
        Application = application;
        Result = result;
    }

    public DiscoveredApplication Application { get; }
    public ApplicationScanResult? Result { get; }
    public string Name => Application.Name;
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1].ToUpperInvariant();
    public string Publisher => string.IsNullOrWhiteSpace(Application.Publisher) ? "发布者未知" : Application.Publisher;
    public string VersionText => string.IsNullOrWhiteSpace(Application.Version) ? "版本未知" : $"v{Application.Version}";
    public string PublisherVersionText => $"{Publisher}  ·  {VersionText}";
    public string CategoryLabel => DisplayLabels.Category(Application.Category);
    public string SourceLabel => DisplayLabels.ApplicationSource(Application.SourceKind);
    public string ShortcutCountText => Result?.ShortcutCount > 0 ? Result.ShortcutCount.ToString() : "0";
    public bool HasShortcuts => Result?.ShortcutCount > 0;
    public string StateLabel => DisplayLabels.ScanStateLabel(Result?.State ?? ScanState.NotRun);
    public string StateDetail => Result?.Detail ?? "尚未运行扫描。";
    public string CheckedSourcesText => Result is null || Result.CheckedSources.Count == 0
        ? "无已检查来源"
        : string.Join("、", Result.CheckedSources);
    public string? IconPath => !string.IsNullOrWhiteSpace(Application.IconPath)
        ? Application.IconPath
        : Application.ExecutablePath;
    public string ExecutablePath => string.IsNullOrWhiteSpace(Application.ExecutablePath)
        ? "未记录可执行文件路径"
        : Application.ExecutablePath;
    public bool HasExecutablePath => !string.IsNullOrWhiteSpace(Application.ExecutablePath);
    public string InstallLocation => string.IsNullOrWhiteSpace(Application.InstallLocation)
        ? "未记录安装位置"
        : Application.InstallLocation;
    public string SettingsPath => Application.SettingsPath ?? string.Empty;
    public bool HasSettingsPath => !string.IsNullOrWhiteSpace(Application.SettingsPath);
    public string SettingsHint => string.IsNullOrWhiteSpace(Application.SettingsHint)
        ? "该应用没有已知的安全配置入口。HotkeyLens 不会修改应用配置，也不会模拟按键。"
        : Application.SettingsHint;
    public string SourceDetail => string.IsNullOrWhiteSpace(Application.SourceDetail)
        ? "来源明细未知"
        : Application.SourceDetail;
    public bool IsSystemComponent => Application.IsSystemComponent;
    public string KindLabel => Application.IsSystemComponent ? "系统组件" : CategoryLabel;

    public override string ToString() => $"{Name}，{PublisherVersionText}，{StateLabel}";
}

public sealed class DiagnosticRowViewModel
{
    public DiagnosticRowViewModel(ScanDiagnostic diagnostic)
    {
        Severity = diagnostic.Severity;
        SeverityLabel = DisplayLabels.DiagnosticSeverityLabel(diagnostic.Severity);
        Source = diagnostic.Source;
        Message = diagnostic.Message;
        Detail = diagnostic.Detail;
        TimeText = diagnostic.Timestamp.ToLocalTime().ToString("HH:mm:ss");
    }

    public DiagnosticSeverity Severity { get; }
    public string SeverityLabel { get; }
    public string Source { get; }
    public string Message { get; }
    public string? Detail { get; }
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public string TimeText { get; }
    public string AccentHex => Severity switch
    {
        DiagnosticSeverity.Error => "#D1495B",
        DiagnosticSeverity.Warning => "#C47F17",
        _ => "#2F6FDB"
    };

    public override string ToString() => $"{SeverityLabel}，{Message}";
}
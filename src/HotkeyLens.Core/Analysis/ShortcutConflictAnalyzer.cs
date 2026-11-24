using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Analysis;

/// <summary>
/// Finds equal chords and classifies them conservatively. Equal chords alone
/// are never reported as a global registration conflict: at least two
/// independently evidenced global registrations are required.
/// </summary>
public static class ShortcutConflictAnalyzer
{
    public static IReadOnlyList<ShortcutConflict> Analyze(IReadOnlyList<ShortcutEntry> shortcuts)
    {
        var groups = shortcuts
            .Where(shortcut => shortcut.IsEnabled && !shortcut.IsSequence)
            .GroupBy(shortcut => shortcut.Gesture.Canonical, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(entry => entry.ApplicationId).Distinct(StringComparer.Ordinal).Count() > 1);

        var conflicts = new List<ShortcutConflict>();
        foreach (var group in groups)
        {
            var entries = group.ToList();
            var byApplication = entries
                .GroupBy(entry => entry.ApplicationId, StringComparer.Ordinal)
                .ToList();
            var appStates = byApplication.ToDictionary(
                pair => pair.Key,
                pair => new EvidenceState(
                    HasRegistration: pair.Any(IsGlobalRegistrationEvidence),
                    HasGlobal: pair.Any(entry => entry.Scope == ShortcutScope.Global),
                    HasUnknown: pair.Any(entry => entry.Scope == ShortcutScope.Unknown)),
                StringComparer.Ordinal);

            var kind = Classify(appStates.Values);
            if (kind is null)
            {
                continue;
            }

            var participants = byApplication
                .Select(pair => SelectParticipant(pair.ToList()))
                .OrderBy(participant => participant.ApplicationName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var gesture = entries[0].Gesture;
            var names = string.Join("、", participants.Select(participant => participant.ApplicationName));
            var hasGlobalEvidence = appStates.Values.Any(state => state.HasGlobal);
            var hasRegistrationEvidence = appStates.Values.Count(state => state.HasRegistration) >= 2;

            var (severity, confidence, summary, rationale, recommendation) = kind.Value switch
            {
                ConflictKind.GlobalHotkeyCompetition => (
                    ConflictSeverity.High,
                    ConfidenceLevel.High,
                    $"{gesture.Canonical} 被 {names} 同时请求注册为全局热键。",
                    "至少两个应用的快捷方式 .lnk 热键字段指向同一组合。Windows 的全局热键注册由先注册者取得，后注册者可能失败；这是登记竞争，不代表两个功能都实际可用。",
                    "打开涉及的快捷方式属性或应用设置，保留真正需要的全局热键，修改另一个。HotkeyLens 不会自动改写成他应用配置。"),
                ConflictKind.PotentialConflict => (
                    hasGlobalEvidence ? ConflictSeverity.Medium : ConflictSeverity.Low,
                    hasGlobalEvidence ? ConfidenceLevel.Medium : ConfidenceLevel.Low,
                    $"{gesture.Canonical} 在 {names} 中的生效范围可能重叠。",
                    hasGlobalEvidence
                        ? "至少一方被标记为全局热键，另一方在应用窗口内使用同一组合；全局注册生效时可能抢占窗口内按键，但当前证据不足以确认实际冲突。"
                        : "至少一方的生效范围无法确定。缺少足够的注册或运行时证据，不能把相同按键直接判定为冲突。",
                    "如果两个应用会同时使用，建议在应用内改用不同组合；若只在各自窗口使用，可先观察是否真的被拦截。"),
                _ => (
                    ConflictSeverity.Informational,
                    hasRegistrationEvidence ? ConfidenceLevel.High : ConfidenceLevel.Medium,
                    $"{gesture.Canonical} 被 {names} 分别用在不同应用范围内，属于重复绑定。",
                    "双方的作用范围均为应用、窗口或上下文内，按键只在各自获得焦点时生效；相同组合本身不构成系统级冲突。",
                    "通常无需处理。只有在实际使用中发生按键被拦截时，再检查是否存在未识别的全局注册。")
            };

            conflicts.Add(new ShortcutConflict
            {
                Id = StableId.Create(
                    "cf",
                    kind.Value.ToString(),
                    gesture.Canonical,
                    string.Join(",", participants.Select(p => p.ApplicationId).OrderBy(id => id, StringComparer.Ordinal))),
                Kind = kind.Value,
                Severity = severity,
                Gesture = gesture,
                Participants = participants,
                Summary = summary,
                Rationale = rationale,
                Confidence = confidence,
                Recommendation = recommendation
            });
        }

        return conflicts
            .OrderByDescending(conflict => conflict.Severity)
            .ThenBy(conflict => conflict.Gesture.SortKey, StringComparer.Ordinal)
            .ToList();
    }

    private static ConflictKind? Classify(IEnumerable<EvidenceState> states)
    {
        var list = states.ToList();
        if (list.Count < 2)
        {
            return null;
        }

        if (list.Count(state => state.HasRegistration) >= 2)
        {
            return ConflictKind.GlobalHotkeyCompetition;
        }

        if (list.Any(state => state.HasGlobal || state.HasUnknown))
        {
            return ConflictKind.PotentialConflict;
        }

        return ConflictKind.DuplicateBinding;
    }

    private static bool IsGlobalRegistrationEvidence(ShortcutEntry entry)
        => entry.Scope == ShortcutScope.Global &&
           entry.SourceKind == ShortcutSourceKind.ShortcutFile;

    private static ConflictParticipant SelectParticipant(IReadOnlyList<ShortcutEntry> entries)
    {
        var selected = entries
            .OrderByDescending(IsGlobalRegistrationEvidence)
            .ThenByDescending(entry => entry.Scope == ShortcutScope.Global)
            .ThenByDescending(entry => entry.Origin == ShortcutOrigin.UserConfigured)
            .ThenByDescending(entry => entry.Origin == ShortcutOrigin.RuntimeObserved)
            .ThenByDescending(entry => entry.Confidence)
            .ThenBy(entry => entry.ActionName, StringComparer.CurrentCultureIgnoreCase)
            .First();

        return new ConflictParticipant
        {
            ShortcutId = selected.Id,
            ApplicationId = selected.ApplicationId,
            ApplicationName = selected.ApplicationName,
            ActionName = selected.ActionName,
            Scope = selected.Scope,
            SourceKind = selected.SourceKind,
            Origin = selected.Origin,
            Confidence = selected.Confidence
        };
    }

    private sealed record EvidenceState(bool HasRegistration, bool HasGlobal, bool HasUnknown);
}
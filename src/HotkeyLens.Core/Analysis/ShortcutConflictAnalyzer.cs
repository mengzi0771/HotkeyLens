using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Analysis;

/// <summary>
/// Finds equal chords and classifies them conservatively.
///
/// Cross-application duplicates are normal when both bindings are scoped to
/// their own application/window. They only become a conflict when a global
/// registration can intercept the chord, or when two different actions share
/// the same effective scope inside one application. Equal chords alone are
/// never reported as a confirmed global registration conflict.
/// </summary>
public static class ShortcutConflictAnalyzer
{
    public static IReadOnlyList<ShortcutConflict> Analyze(
        IReadOnlyList<ShortcutEntry> shortcuts,
        IReadOnlyDictionary<string, bool?>? runningApplications = null)
    {
        var groups = shortcuts
            .Where(shortcut => shortcut.IsEnabled && !shortcut.IsSequence)
            .GroupBy(shortcut => shortcut.Gesture.Canonical, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        var findings = new List<ShortcutConflict>();
        foreach (var group in groups)
        {
            var entries = group.ToList();

            foreach (var applicationGroup in entries.GroupBy(entry => entry.ApplicationId, StringComparer.Ordinal))
            {
                AddSameApplicationFindings(applicationGroup.ToList(), findings);
            }

            if (entries.Select(entry => entry.ApplicationId).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                findings.Add(BuildCrossApplicationFinding(entries, runningApplications));
            }
        }

        return findings
            .OrderByDescending(conflict => conflict.Severity)
            .ThenBy(conflict => conflict.Gesture.SortKey, StringComparer.Ordinal)
            .ThenBy(conflict => conflict.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddSameApplicationFindings(
        IReadOnlyList<ShortcutEntry> applicationEntries,
        List<ShortcutConflict> findings)
    {
        var distinctActions = applicationEntries
            .Select(entry => NormalizeAction(entry.ActionName))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Count();
        if (distinctActions < 2)
        {
            return;
        }

        var involved = new Dictionary<string, ShortcutEntry>(StringComparer.Ordinal);
        for (var left = 0; left < applicationEntries.Count; left++)
        {
            for (var right = left + 1; right < applicationEntries.Count; right++)
            {
                var first = applicationEntries[left];
                var second = applicationEntries[right];
                if (string.Equals(NormalizeAction(first.ActionName), NormalizeAction(second.ActionName),
                        StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                if (!ContextsMayOverlap(first.Context, second.Context))
                {
                    continue;
                }

                involved[first.Id] = first;
                involved[second.Id] = second;
            }
        }

        if (involved.Count < 2)
        {
            return;
        }

        var participants = involved.Values
            .Select(entry => ToParticipant(entry))
            .OrderBy(participant => participant.ActionName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(participant => participant.ShortcutId, StringComparer.Ordinal)
            .ToList();
        var gesture = applicationEntries[0].Gesture;
        var applicationName = applicationEntries[0].ApplicationName;
        var actionNames = string.Join("、", participants.Select(participant => $"「{participant.ActionName}」"));
        var contexts = participants
            .Select(participant => participant.Context)
            .Where(context => !string.IsNullOrWhiteSpace(context))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var contextText = contexts.Count == 0 ? "应用内" : string.Join(" / ", contexts);

        var hasUnknownScope = involved.Values.Any(entry => entry.Scope == ShortcutScope.Unknown);
        var hasReliableEvidence = involved.Values.Any(HasReliableUserEvidence);
        var classification = hasUnknownScope
            ? ConflictClassification.Unknown
            : hasReliableEvidence
                ? ConflictClassification.ConfirmedConflict
                : ConflictClassification.PotentialConflict;

        var (severity, confidence, summary, rationale, recommendation) = classification switch
        {
            ConflictClassification.ConfirmedConflict => (
                ConflictSeverity.Medium,
                ConfidenceLevel.High,
                $"{gesture.Canonical} 在 {applicationName} 中同时对应 {actionNames}，作用域为 {contextText}。",
                "同一应用内、同一有效作用域和上下文中出现两个不同操作。至少一条证据来自用户实际配置、运行时观察或应用接口，因此可以确认存在按键归属竞争。",
                "在应用自身的快捷键设置中检查这两个操作，保留真正需要的一个绑定。HotkeyLens 只报告证据，不会自动修改第三方配置。"),
            ConflictClassification.PotentialConflict => (
                ConflictSeverity.Low,
                ConfidenceLevel.Medium,
                $"{gesture.Canonical} 在 {applicationName} 中可能同时对应 {actionNames}，作用域为 {contextText}。",
                "同一应用内出现了相同组合和可能重叠的作用域，但当前证据来自官方默认值或静态数据，尚未读取到用户实际配置，无法确认两个绑定在当前安装中同时生效。",
                "打开应用设置核对这些操作是否都处于启用状态；只有实际发生按键被抢占时再修改。"),
            _ => (
                ConflictSeverity.Informational,
                ConfidenceLevel.Unknown,
                $"{gesture.Canonical} 在 {applicationName} 中对应 {actionNames}，但作用域信息不足。",
                "缺少作用域或上下文证据，无法判断两个操作是否会同时接收该按键。HotkeyLens 不会在证据不足时把它算作确认冲突。",
                "可在应用设置中确认这两个操作的适用范围，或重新扫描以获取更完整的配置。")
        };

        findings.Add(CreateConflict(
            gesture,
            ConflictKind.SameApplicationOverlap,
            classification,
            severity,
            participants,
            summary,
            rationale,
            confidence,
            recommendation));
    }

    private static ShortcutConflict BuildCrossApplicationFinding(
        IReadOnlyList<ShortcutEntry> entries,
        IReadOnlyDictionary<string, bool?>? runningApplications)
    {
        var byApplication = entries
            .GroupBy(entry => entry.ApplicationId, StringComparer.Ordinal)
            .ToList();
        var participants = byApplication
            .Select(group => SelectParticipant(group.ToList(), runningApplications))
            .OrderBy(participant => participant.ApplicationName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(participant => participant.ApplicationId, StringComparer.Ordinal)
            .ToList();
        var gesture = entries[0].Gesture;
        var names = string.Join("、", participants.Select(participant => participant.ApplicationName));

        var registeredApplications = byApplication
            .Where(group => group.Any(IsGlobalRegistrationEvidence))
            .Select(group => group.Key)
            .ToList();
        var globalApplications = byApplication
            .Where(group => group.Any(entry => entry.Scope == ShortcutScope.Global))
            .Select(group => group.Key)
            .ToList();
        var unknownOnly = byApplication.All(group => group.All(entry => entry.Scope == ShortcutScope.Unknown));
        var hasUnknown = byApplication.Any(group => group.Any(entry => entry.Scope == ShortcutScope.Unknown));

        if (registeredApplications.Count >= 2)
        {
            var namesWithRegistration = participants
                .Where(participant => registeredApplications.Contains(participant.ApplicationId, StringComparer.Ordinal))
                .Select(participant => participant.ApplicationName);
            return CreateConflict(
                gesture,
                ConflictKind.GlobalHotkeyCompetition,
                ConflictClassification.ConfirmedConflict,
                ConflictSeverity.High,
                participants,
                $"{gesture.Canonical} 被 {string.Join("、", namesWithRegistration)} 同时请求注册为全局热键。",
                "至少两个应用的快捷方式 .lnk 热键字段指向同一组合。Windows 的全局热键注册由先注册者取得，后注册者可能失败；这证明存在登记竞争，不代表两个功能当前都实际可用。",
                ConfidenceLevel.High,
                "打开涉及的快捷方式属性或应用设置，保留真正需要的全局热键，修改另一个。HotkeyLens 不会自动改写第三方应用配置。");
        }

        var allGlobal = byApplication.All(group => group.All(entry => entry.Scope == ShortcutScope.Global));
        if (registeredApplications.Count == 0 && allGlobal)
        {
            var states = runningApplications is null
                ? null
                : participants
                    .Select(participant => runningApplications.TryGetValue(participant.ApplicationId, out var isRunning)
                        ? (bool?)isRunning
                        : null)
                    .ToList();
            var allKnown = states is not null && states.All(state => state.HasValue);
            var allRunning = allKnown && states!.All(state => state == true);
            var someRunning = states is not null && states.Any(state => state == true);

            var summary = allRunning
                ? $"{gesture.Canonical} 被 {names} 同时用作全局热键，涉及软件当前都在运行，按下时可能被先启动的软件抢占。"
                : !allKnown
                    ? $"{gesture.Canonical} 被 {names} 同时用作全局热键，但本次扫描没有采集到全部运行状态，无法判断当前是否会竞争。"
                    : someRunning
                        ? $"{gesture.Canonical} 被 {names} 同时用作全局热键，但至少一款当前未检测到运行，仅在同时开启时才可能竞争。"
                        : $"{gesture.Canonical} 被 {names} 同时用作全局热键，但涉及软件当前均未检测到运行，仅在同时开启时才可能竞争。";

            var rationale = allRunning
                ? "这些记录都把同一组合声明为全局（不限于窗口内）热键，且扫描时相关进程都在运行。Windows 的全局热键由先注册者取得，后注册者通常收不到按键，因此存在真实的按键竞争；这里仍标为「潜在冲突」，因为系统没有公开 API 能枚举全部已注册的全局热键。"
                : "这些记录都把同一组合声明为全局（不限于窗口内）热键。扫描时至少一款软件没有检测到运行（可能已关闭，或被安全软件隐藏了进程）或运行状态未知，目前不一定发生抢占；只有相关软件同时开启时才可能竞争，因此降低显著度以避免日常误报。";

            return CreateConflict(
                gesture,
                ConflictKind.GlobalHotkeyCompetition,
                ConflictClassification.PotentialConflict,
                allRunning ? ConflictSeverity.Medium : ConflictSeverity.Low,
                participants,
                summary,
                rationale,
                ConfidenceLevel.Medium,
                "在其中一个软件的「设置 → 快捷键」里更换组合或关闭全局热键；改完后在冲突卡片上点「复检此冲突」确认是否已解决。HotkeyLens 不会修改第三方配置。");
        }

        if (globalApplications.Count > 0)
        {
            var globalNames = participants
                .Where(participant => globalApplications.Contains(participant.ApplicationId, StringComparer.Ordinal))
                .Select(participant => participant.ApplicationName)
                .Distinct(StringComparer.CurrentCultureIgnoreCase);
            return CreateConflict(
                gesture,
                ConflictKind.PotentialConflict,
                ConflictClassification.PotentialConflict,
                ConflictSeverity.Medium,
                participants,
                $"{gesture.Canonical} 在 {names} 中的生效范围可能重叠。",
                $"其中 {string.Join("、", globalNames)} 被标记为全局范围。全局注册生效时可能抢占其他应用窗口内的同一按键，但 Windows 没有公开 API 可以枚举其他进程注册的全部全局热键，当前证据不足以确认实际拦截。",
                ConfidenceLevel.Medium,
                "如果两个应用会同时使用，建议在应用内改用不同组合；若只在各自窗口使用，可先观察是否真的被拦截。");
        }

        if (unknownOnly)
        {
            return CreateConflict(
                gesture,
                ConflictKind.Unknown,
                ConflictClassification.Unknown,
                ConflictSeverity.Informational,
                participants,
                $"{gesture.Canonical} 出现在 {names} 中，但无法确定作用域。",
                "所有相关记录都缺少可用的作用域或上下文信息。相同组合本身不能证明冲突，因此这里只标记为未知，不计入冲突总数。",
                ConfidenceLevel.Unknown,
                "重新扫描并在应用设置中确认这些快捷键的生效范围。");
        }

        if (hasUnknown)
        {
            return CreateConflict(
                gesture,
                ConflictKind.PotentialConflict,
                ConflictClassification.PotentialConflict,
                ConflictSeverity.Low,
                participants,
                $"{gesture.Canonical} 在 {names} 中的部分作用域无法确定。",
                "至少一方的生效范围未知，而其他方在应用窗口内使用同一组合；不能排除重叠，但也没有足够证据确认冲突。",
                ConfidenceLevel.Low,
                "确认未知项的适用范围；如果实际使用中发生拦截，再调整绑定。");
        }

        var hasStrongEvidence = entries.Any(HasReliableUserEvidence);
        return CreateConflict(
            gesture,
            ConflictKind.DuplicateBinding,
            ConflictClassification.NormalDuplicate,
            ConflictSeverity.Informational,
            participants,
            $"{gesture.Canonical} 被 {names} 分别用在不同应用范围内，属于正常重复。",
            "各条记录的作用范围均为应用、窗口或上下文内，按键只在对应应用获得焦点时生效；不同应用不会同时接收同一按键，因此相同组合本身不构成系统级冲突。",
            hasStrongEvidence ? ConfidenceLevel.High : ConfidenceLevel.Medium,
            "通常无需处理。只有在实际使用中发生按键被拦截时，再检查是否存在未识别的全局注册。");
    }

    private static ShortcutConflict CreateConflict(
        ShortcutGesture gesture,
        ConflictKind kind,
        ConflictClassification classification,
        ConflictSeverity severity,
        IReadOnlyList<ConflictParticipant> participants,
        string summary,
        string rationale,
        ConfidenceLevel confidence,
        string recommendation)
    {
        var participantIds = participants
            .Select(participant => participant.ShortcutId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return new ShortcutConflict
        {
            Id = StableId.Create(
                "cf",
                kind.ToString(),
                classification.ToString(),
                gesture.Canonical,
                string.Join(",", participantIds)),
            Kind = kind,
            Classification = classification,
            Severity = severity,
            Gesture = gesture,
            Participants = participants,
            Summary = summary,
            Rationale = rationale,
            Confidence = confidence,
            Recommendation = recommendation
        };
    }

    private static ConflictParticipant SelectParticipant(
        IReadOnlyList<ShortcutEntry> entries,
        IReadOnlyDictionary<string, bool?>? runningApplications)
    {
        var selected = entries
            .OrderByDescending(IsGlobalRegistrationEvidence)
            .ThenByDescending(entry => entry.Scope == ShortcutScope.Global)
            .ThenByDescending(entry => entry.Origin == ShortcutOrigin.UserConfigured)
            .ThenByDescending(entry => entry.Origin == ShortcutOrigin.RuntimeObserved)
            .ThenByDescending(entry => entry.Confidence)
            .ThenBy(entry => entry.ActionName, StringComparer.CurrentCultureIgnoreCase)
            .First();

        return ToParticipant(selected, runningApplications);
    }

    private static ConflictParticipant ToParticipant(
        ShortcutEntry entry,
        IReadOnlyDictionary<string, bool?>? runningApplications = null)
    {
        bool? isRunning = null;
        if (runningApplications is not null &&
            runningApplications.TryGetValue(entry.ApplicationId, out var running))
        {
            isRunning = running;
        }

        return new ConflictParticipant
        {
            ShortcutId = entry.Id,
            ApplicationId = entry.ApplicationId,
            ApplicationName = entry.ApplicationName,
            ActionName = entry.ActionName,
            Scope = entry.Scope,
            SourceKind = entry.SourceKind,
            Origin = entry.Origin,
            Confidence = entry.Confidence,
            Context = entry.Context,
            IsRunning = isRunning,
            ResolutionGuide = entry.ResolutionGuide
        };
    }

    private static bool IsGlobalRegistrationEvidence(ShortcutEntry entry)
        => entry.Scope == ShortcutScope.Global &&
           entry.SourceKind == ShortcutSourceKind.ShortcutFile;

    private static bool HasReliableUserEvidence(ShortcutEntry entry)
        => entry.Origin is ShortcutOrigin.UserConfigured or ShortcutOrigin.RuntimeObserved ||
           entry.SourceKind is ShortcutSourceKind.UserConfiguration
               or ShortcutSourceKind.ApplicationInterface
               or ShortcutSourceKind.Observed &&
           entry.Confidence >= ConfidenceLevel.Medium;

    private static string NormalizeAction(string actionName) => actionName.Trim();

    private static bool ContextsMayOverlap(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return true;
        }

        return string.Equals(first.Trim(), second.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }
}
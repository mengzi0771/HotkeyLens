using HotkeyLens.Core.Analysis;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

public sealed class RuntimeConflictTests
{
    private static readonly DiscoveredApplication NetEase =
        TestData.Application("netease", "网易云音乐");

    private static readonly DiscoveredApplication QQMusic =
        TestData.Application("qqmusic", "QQ音乐");

    private static IReadOnlyList<ShortcutEntry> SameGlobalGesture(ShortcutSourceKind sourceKind = ShortcutSourceKind.OfficialDocumentation, ShortcutOrigin origin = ShortcutOrigin.OfficialDefault)
        =>
        [
            TestData.Entry(NetEase, "Ctrl+Alt+P", ShortcutScope.Global, sourceKind, origin, ConfidenceLevel.Medium, "播放 / 暂停"),
            TestData.Entry(QQMusic, "Ctrl+Alt+P", ShortcutScope.Global, sourceKind, origin, ConfidenceLevel.Medium, "播放 / 暂停")
        ];

    [Fact]
    public void BothRunningRaisesMediumGlobalCompetition()
    {
        var conflicts = ShortcutConflictAnalyzer.Analyze(
            SameGlobalGesture(),
            new Dictionary<string, bool?>(StringComparer.Ordinal)
            {
                [NetEase.Id] = true,
                [QQMusic.Id] = true
            });

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictKind.GlobalHotkeyCompetition, conflict.Kind);
        Assert.Equal(ConflictClassification.PotentialConflict, conflict.Classification);
        Assert.Equal(ConflictSeverity.Medium, conflict.Severity);
        Assert.Contains("都在运行", conflict.Summary, StringComparison.Ordinal);
        Assert.Contains("抢占", conflict.Summary, StringComparison.Ordinal);
        Assert.All(conflict.Participants, participant => Assert.True(participant.IsRunning));
        Assert.Contains("复检", conflict.Recommendation, StringComparison.Ordinal);
    }

    [Fact]
    public void OneAppNotRunningDowngradesAndExplainsWhy()
    {
        var conflicts = ShortcutConflictAnalyzer.Analyze(
            SameGlobalGesture(),
            new Dictionary<string, bool?>(StringComparer.Ordinal)
            {
                [NetEase.Id] = true,
                [QQMusic.Id] = false
            });

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictSeverity.Low, conflict.Severity);
        Assert.Contains("未检测到运行", conflict.Summary, StringComparison.Ordinal);
        Assert.Contains("同时开启", conflict.Summary, StringComparison.Ordinal);
        Assert.False(conflict.Participants.Single(participant => participant.ApplicationId == QQMusic.Id).IsRunning);
        Assert.True(conflict.Participants.Single(participant => participant.ApplicationId == NetEase.Id).IsRunning);
    }

    [Fact]
    public void NeitherRunningKeepsLowSeverity()
    {
        var conflicts = ShortcutConflictAnalyzer.Analyze(
            SameGlobalGesture(),
            new Dictionary<string, bool?>(StringComparer.Ordinal)
            {
                [NetEase.Id] = false,
                [QQMusic.Id] = false
            });

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictSeverity.Low, conflict.Severity);
        Assert.Contains("均未检测到运行", conflict.Summary, StringComparison.Ordinal);
        Assert.Equal(true, conflict.CountsAsConflict);
    }

    [Fact]
    public void MissingRunningSnapshotIsReportedAsUnknown()
    {
        var conflicts = ShortcutConflictAnalyzer.Analyze(SameGlobalGesture());

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictSeverity.Low, conflict.Severity);
        Assert.Contains("没有采集到", conflict.Summary, StringComparison.Ordinal);
        Assert.All(conflict.Participants, participant => Assert.Null(participant.IsRunning));
    }

    [Fact]
    public void PartialRunningSnapshotIsNotTreatedAsNotRunning()
    {
        var conflicts = ShortcutConflictAnalyzer.Analyze(
            SameGlobalGesture(),
            new Dictionary<string, bool?>(StringComparer.Ordinal) { [NetEase.Id] = true });

        var conflict = Assert.Single(conflicts);
        Assert.Contains("没有采集到全部运行状态", conflict.Summary, StringComparison.Ordinal);
        Assert.True(conflict.Participants.Single(participant => participant.ApplicationId == NetEase.Id).IsRunning);
        Assert.Null(conflict.Participants.Single(participant => participant.ApplicationId == QQMusic.Id).IsRunning);
    }

    [Fact]
    public void PerWindowShortcutsStayNormalDuplicatesEvenWhenBothRun()
    {
        var entries = new List<ShortcutEntry>
        {
            TestData.Entry(NetEase, "Ctrl+S", ShortcutScope.Window),
            TestData.Entry(QQMusic, "Ctrl+S", ShortcutScope.Window)
        };

        var conflicts = ShortcutConflictAnalyzer.Analyze(
            entries,
            new Dictionary<string, bool?>(StringComparer.Ordinal)
            {
                [NetEase.Id] = true,
                [QQMusic.Id] = true
            });

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictClassification.NormalDuplicate, conflict.Classification);
        Assert.Equal(ConflictSeverity.Informational, conflict.Severity);
        Assert.False(conflict.CountsAsConflict);
    }

    [Fact]
    public void ShortcutFileRegistrationStillWinsOverRunningState()
    {
        var entries = new List<ShortcutEntry>
        {
            TestData.Entry(NetEase, "Ctrl+Alt+P", ShortcutScope.Global, ShortcutSourceKind.ShortcutFile, ShortcutOrigin.RuntimeObserved, ConfidenceLevel.High),
            TestData.Entry(QQMusic, "Ctrl+Alt+P", ShortcutScope.Global, ShortcutSourceKind.ShortcutFile, ShortcutOrigin.RuntimeObserved, ConfidenceLevel.High)
        };

        var conflicts = ShortcutConflictAnalyzer.Analyze(
            entries,
            new Dictionary<string, bool?>(StringComparer.Ordinal)
            {
                [NetEase.Id] = true,
                [QQMusic.Id] = true
            });

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictClassification.ConfirmedConflict, conflict.Classification);
        Assert.Equal(ConflictSeverity.High, conflict.Severity);
    }

    [Fact]
    public void ParticipantsCarryResolutionGuideFromEntries()
    {
        var entries = new List<ShortcutEntry>
        {
            TestData.Entry(NetEase, "Ctrl+Alt+P", ShortcutScope.Global, ShortcutSourceKind.OfficialDocumentation, ShortcutOrigin.OfficialDefault, ConfidenceLevel.Medium) with
            {
                ResolutionGuide = "网易云音乐 → 设置 → 快捷键"
            },
            TestData.Entry(QQMusic, "Ctrl+Alt+P", ShortcutScope.Global, ShortcutSourceKind.OfficialDocumentation, ShortcutOrigin.OfficialDefault, ConfidenceLevel.Medium) with
            {
                ResolutionGuide = "QQ音乐 → 设置 → 快捷键"
            }
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.All(conflict.Participants, participant =>
            Assert.False(string.IsNullOrWhiteSpace(participant.ResolutionGuide)));
    }
}


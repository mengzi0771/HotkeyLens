using HotkeyLens.Core.Analysis;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

public sealed class ConflictAnalyzerTests
{
    private static readonly DiscoveredApplication Alpha = TestData.Application("alpha", "Alpha");
    private static readonly DiscoveredApplication Beta = TestData.Application("beta", "Beta");

    [Fact]
    public void SameApplicationIsNotAConflict()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+K", action: "动作一"),
            TestData.Entry(Alpha, "Ctrl+K", action: "动作二")
        };

        Assert.Empty(ShortcutConflictAnalyzer.Analyze(entries));
    }

    [Fact]
    public void TwoWindowBindingsAreClassifiedAsDuplicate_NotConflict()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+Shift+T", ShortcutScope.Window),
            TestData.Entry(Beta, "Ctrl+Shift+T", ShortcutScope.Window)
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.Equal(ConflictKind.DuplicateBinding, conflict.Kind);
        Assert.Equal(ConflictSeverity.Informational, conflict.Severity);
        Assert.Equal(ConfidenceLevel.Medium, conflict.Confidence);
        Assert.Equal(2, conflict.Participants.Count);
        Assert.Contains("不构成系统级冲突", conflict.Rationale);
    }

    [Fact]
    public void TwoGlobalShortcutFileRegistrationsAreCompetition()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+Shift+A", ShortcutScope.Global, ShortcutSourceKind.ShortcutFile),
            TestData.Entry(Beta, "Ctrl+Shift+A", ShortcutScope.Global, ShortcutSourceKind.ShortcutFile)
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.Equal(ConflictKind.GlobalHotkeyCompetition, conflict.Kind);
        Assert.Equal(ConflictSeverity.High, conflict.Severity);
        Assert.Equal(ConfidenceLevel.High, conflict.Confidence);
        Assert.True(conflict.IsConfirmedRegistrationRace);
        Assert.Contains("先注册者取得", conflict.Rationale);
    }

    [Fact]
    public void GlobalPlusWindowIsOnlyPotential()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Alt+Space", ShortcutScope.Global, ShortcutSourceKind.ShortcutFile),
            TestData.Entry(Beta, "Alt+Space", ShortcutScope.Window)
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.Equal(ConflictKind.PotentialConflict, conflict.Kind);
        Assert.Equal(ConflictSeverity.Medium, conflict.Severity);
        Assert.Contains("可能抢占", conflict.Rationale);
    }

    [Fact]
    public void UnknownScopeProducesLowConfidencePotential()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+F1", ShortcutScope.Unknown),
            TestData.Entry(Beta, "Ctrl+F1", ShortcutScope.Application)
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.Equal(ConflictKind.PotentialConflict, conflict.Kind);
        Assert.Equal(ConflictSeverity.Low, conflict.Severity);
        Assert.Equal(ConfidenceLevel.Low, conflict.Confidence);
    }

    [Fact]
    public void OneRegistrationAndOneDocumentedGlobalIsPotential()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+Alt+P", ShortcutScope.Global, ShortcutSourceKind.ShortcutFile),
            TestData.Entry(
                Beta,
                "Ctrl+Alt+P",
                ShortcutScope.Global,
                ShortcutSourceKind.OfficialDocumentation,
                ShortcutOrigin.OfficialDefault,
                ConfidenceLevel.Medium)
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.Equal(ConflictKind.PotentialConflict, conflict.Kind);
    }

    [Fact]
    public void SequencesAndDisabledBindingsAreIgnored()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+K", sequence: "Ctrl+K Ctrl+S"),
            TestData.Entry(Beta, "Ctrl+K", sequence: "Ctrl+K Ctrl+S"),
            TestData.Entry(Alpha, "Ctrl+J", isEnabled: false),
            TestData.Entry(Beta, "Ctrl+J", isEnabled: false)
        };

        Assert.Empty(ShortcutConflictAnalyzer.Analyze(entries));
    }

    [Fact]
    public void ThreeApplicationsProduceOneGroupedFinding()
    {
        var gamma = TestData.Application("gamma", "Gamma");
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+Shift+T", ShortcutScope.Window),
            TestData.Entry(Beta, "Ctrl+Shift+T", ShortcutScope.Window),
            TestData.Entry(gamma, "Ctrl+Shift+T", ShortcutScope.Window)
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.Equal(3, conflict.Participants.Count);
        Assert.Equal(ConflictKind.DuplicateBinding, conflict.Kind);
    }

    [Fact]
    public void ConflictIdIsStableRegardlessOfInputOrder()
    {
        var one = TestData.Entry(Alpha, "Ctrl+Shift+T", ShortcutScope.Window);
        var two = TestData.Entry(Beta, "Ctrl+Shift+T", ShortcutScope.Window);

        var first = ShortcutConflictAnalyzer.Analyze([one, two]).Single();
        var second = ShortcutConflictAnalyzer.Analyze([two, one]).Single();

        Assert.Equal(first.Id, second.Id);
    }
}
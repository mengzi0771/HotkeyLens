using HotkeyLens.Core.Analysis;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

public sealed class ConflictAnalyzerTests
{
    private static readonly DiscoveredApplication Alpha = TestData.Application("alpha", "Alpha");
    private static readonly DiscoveredApplication Beta = TestData.Application("beta", "Beta");

    [Fact]
    public void SameApplicationSameContextWithDifferentActionsIsConfirmed()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+K", action: "动作一"),
            TestData.Entry(Alpha, "Ctrl+K", action: "动作二")
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.Equal(ConflictKind.SameApplicationOverlap, conflict.Kind);
        Assert.Equal(ConflictClassification.ConfirmedConflict, conflict.Classification);
        Assert.True(conflict.CountsAsConflict);
        Assert.Equal(2, conflict.Participants.Count);
        Assert.All(conflict.Participants, participant => Assert.Equal(Alpha.Id, participant.ApplicationId));
    }

    [Fact]
    public void SameApplicationDifferentContextsAreNotAConflict()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+K", ShortcutScope.Contextual, action: "表格中查找", context: "表格获得焦点时"),
            TestData.Entry(Alpha, "Ctrl+K", ShortcutScope.Contextual, action: "图表中查找", context: "图表获得焦点时")
        };

        Assert.Empty(ShortcutConflictAnalyzer.Analyze(entries));
    }

    [Fact]
    public void SameApplicationSameActionFromTwoSourcesIsNotAConflict()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+S", action: "保存"),
            TestData.Entry(
                Alpha,
                "Ctrl+S",
                sourceKind: ShortcutSourceKind.OfficialDocumentation,
                origin: ShortcutOrigin.OfficialDefault,
                confidence: ConfidenceLevel.Medium,
                action: "保存")
        };

        Assert.Empty(ShortcutConflictAnalyzer.Analyze(entries));
    }

    [Fact]
    public void SameApplicationOfficialDefaultsAreOnlyPotential()
    {
        var entries = new[]
        {
            TestData.Entry(
                Alpha,
                "Ctrl+K",
                sourceKind: ShortcutSourceKind.OfficialDocumentation,
                origin: ShortcutOrigin.OfficialDefault,
                confidence: ConfidenceLevel.Medium,
                action: "动作一"),
            TestData.Entry(
                Alpha,
                "Ctrl+K",
                sourceKind: ShortcutSourceKind.OfficialDocumentation,
                origin: ShortcutOrigin.OfficialDefault,
                confidence: ConfidenceLevel.Medium,
                action: "动作二")
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.Equal(ConflictClassification.PotentialConflict, conflict.Classification);
        Assert.True(conflict.CountsAsConflict);
        Assert.Equal(ConflictSeverity.Low, conflict.Severity);
    }

    [Fact]
    public void SameApplicationUnknownScopeIsUnknown()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+K", ShortcutScope.Unknown, action: "动作一"),
            TestData.Entry(Alpha, "Ctrl+K", ShortcutScope.Unknown, action: "动作二")
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.Equal(ConflictClassification.Unknown, conflict.Classification);
        Assert.Equal(ConflictKind.SameApplicationOverlap, conflict.Kind);
        Assert.False(conflict.CountsAsConflict);
    }

    [Fact]
    public void WordExcelAndChromeCtrlSIsANormalDuplicate_NotConflict()
    {
        var word = TestData.Application("word", "Microsoft Word");
        var excel = TestData.Application("excel", "Microsoft Excel");
        var chrome = TestData.Application("chrome", "Google Chrome");
        var entries = new[]
        {
            TestData.Entry(word, "Ctrl+S", ShortcutScope.Application, action: "保存文档"),
            TestData.Entry(excel, "Ctrl+S", ShortcutScope.Application, action: "保存工作簿"),
            TestData.Entry(chrome, "Ctrl+S", ShortcutScope.Window, action: "保存网页")
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.Equal(ConflictClassification.NormalDuplicate, conflict.Classification);
        Assert.Equal(ConflictKind.DuplicateBinding, conflict.Kind);
        Assert.False(conflict.CountsAsConflict);
        Assert.Equal(ConflictSeverity.Informational, conflict.Severity);
        Assert.Equal(3, conflict.Participants.Count);
        Assert.Contains("正常重复", conflict.Summary);
        Assert.Contains("不构成系统级冲突", conflict.Rationale);
    }

    [Fact]
    public void TwoGlobalShortcutFileRegistrationsAreConfirmedCompetition()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+Shift+A", ShortcutScope.Global, ShortcutSourceKind.ShortcutFile),
            TestData.Entry(Beta, "Ctrl+Shift+A", ShortcutScope.Global, ShortcutSourceKind.ShortcutFile)
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.Equal(ConflictKind.GlobalHotkeyCompetition, conflict.Kind);
        Assert.Equal(ConflictClassification.ConfirmedConflict, conflict.Classification);
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
        Assert.Equal(ConflictClassification.PotentialConflict, conflict.Classification);
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
        Assert.Equal(ConflictClassification.PotentialConflict, conflict.Classification);
        Assert.Equal(ConflictSeverity.Low, conflict.Severity);
        Assert.Equal(ConfidenceLevel.Low, conflict.Confidence);
    }

    [Fact]
    public void TwoUnknownScopesAreUnknown_NotConfirmed()
    {
        var entries = new[]
        {
            TestData.Entry(Alpha, "Ctrl+F1", ShortcutScope.Unknown),
            TestData.Entry(Beta, "Ctrl+F1", ShortcutScope.Unknown)
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.Equal(ConflictClassification.Unknown, conflict.Classification);
        Assert.Equal(ConflictKind.Unknown, conflict.Kind);
        Assert.False(conflict.CountsAsConflict);
        Assert.Equal(ConfidenceLevel.Unknown, conflict.Confidence);
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
        Assert.Equal(ConflictClassification.PotentialConflict, conflict.Classification);
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
    public void ThreeApplicationsProduceOneNormalDuplicate()
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
        Assert.Equal(ConflictClassification.NormalDuplicate, conflict.Classification);
    }

    [Fact]
    public void ParticipantSourcesAndConfidenceArePreserved()
    {
        var entries = new[]
        {
            TestData.Entry(
                Alpha,
                "Ctrl+Shift+A",
                ShortcutScope.Global,
                ShortcutSourceKind.ShortcutFile,
                ShortcutOrigin.UserConfigured,
                ConfidenceLevel.High),
            TestData.Entry(
                Beta,
                "Ctrl+Shift+A",
                ShortcutScope.Global,
                ShortcutSourceKind.ShortcutFile,
                ShortcutOrigin.UserConfigured,
                ConfidenceLevel.High)
        };

        var conflict = Assert.Single(ShortcutConflictAnalyzer.Analyze(entries));
        Assert.All(conflict.Participants, participant =>
        {
            Assert.Equal(ShortcutSourceKind.ShortcutFile, participant.SourceKind);
            Assert.Equal(ShortcutOrigin.UserConfigured, participant.Origin);
            Assert.Equal(ConfidenceLevel.High, participant.Confidence);
        });
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
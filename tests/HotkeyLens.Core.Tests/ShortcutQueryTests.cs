using HotkeyLens.Core.Analysis;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

public sealed class ShortcutQueryTests
{
    private static readonly DiscoveredApplication Code = TestData.Application(
        "code",
        "Visual Studio Code",
        category: ApplicationCategory.Development);
    private static readonly DiscoveredApplication Terminal = TestData.Application(
        "terminal",
        "Windows Terminal",
        category: ApplicationCategory.Utility);

    private static readonly ShortcutEntry[] Entries =
    [
        TestData.Entry(Code, "Ctrl+Shift+P", action: "显示命令面板", confidence: ConfidenceLevel.Medium,
            sourceKind: ShortcutSourceKind.OfficialDocumentation, origin: ShortcutOrigin.OfficialDefault),
        TestData.Entry(Code, "Ctrl+S", action: "保存文件", confidence: ConfidenceLevel.High),
        TestData.Entry(Terminal, "Ctrl+Shift+T", action: "打开新标签页", confidence: ConfidenceLevel.High),
        TestData.Entry(Terminal, "Ctrl+S", action: "清空屏幕", confidence: ConfidenceLevel.Low,
            scope: ShortcutScope.Contextual)
    ];

    [Theory]
    [InlineData("保存")]
    [InlineData("Ctrl+S")]
    [InlineData("ctrl s")]
    [InlineData("Visual Studio")]
    [InlineData("VISUAL STUDIO")]
    public void SearchMatchesActionDescriptionApplicationAndGesture(string search)
    {
        var result = ShortcutQuery.Apply(Entries, new ShortcutFilter { SearchText = search });
        Assert.Contains(result, entry => entry.ActionName == "保存文件");
    }

    [Fact]
    public void SearchByApplicationNameUsesDisplayName()
    {
        var result = ShortcutQuery.Apply(Entries, new ShortcutFilter { SearchText = "Visual Studio Code" });
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ScopeAndSourceFiltersCombine()
    {
        var result = ShortcutQuery.Apply(Entries, new ShortcutFilter
        {
            Scope = ShortcutScope.Contextual,
            SourceKind = ShortcutSourceKind.UserConfiguration
        });

        var entry = Assert.Single(result);
        Assert.Equal("Ctrl+S", entry.Gesture.Canonical);
    }

    [Fact]
    public void MinimumConfidenceExcludesLowEntries()
    {
        var result = ShortcutQuery.Apply(Entries, new ShortcutFilter
        {
            MinimumConfidence = ConfidenceLevel.Medium
        });

        Assert.Equal(3, result.Count);
        Assert.DoesNotContain(result, entry => entry.Confidence == ConfidenceLevel.Low);
    }

    [Fact]
    public void ApplicationAndCategoryFiltersWork()
    {
        var byApplication = ShortcutQuery.Apply(Entries, new ShortcutFilter { ApplicationId = "code" });
        Assert.Equal(2, byApplication.Count);

        var categories = new Dictionary<string, ApplicationCategory>
        {
            ["code"] = ApplicationCategory.Development,
            ["terminal"] = ApplicationCategory.Utility
        };
        var byCategory = ShortcutQuery.Apply(
            Entries,
            new ShortcutFilter { Category = ApplicationCategory.Development },
            categories: categories);
        Assert.Equal(2, byCategory.Count);
    }

    [Fact]
    public void OnlyConflictingUsesAnalyzedParticipants()
    {
        var conflicts = ShortcutConflictAnalyzer.Analyze(Entries);
        Assert.NotEmpty(conflicts);

        var result = ShortcutQuery.Apply(
            Entries,
            new ShortcutFilter { OnlyConflicting = true },
            conflicts);

        Assert.Equal(2, result.Count);
        Assert.All(result, entry => Assert.Contains(entry.ActionName, new[] { "保存文件", "清空屏幕" }));
    }

    [Fact]
    public void SortByConfidenceDescending()
    {
        var result = ShortcutQuery.Apply(Entries, new ShortcutFilter
        {
            SortBy = ShortcutSortField.Confidence,
            SortDescending = true
        });

        Assert.Equal(ConfidenceLevel.High, result[0].Confidence);
        Assert.Equal(ConfidenceLevel.Low, result[^1].Confidence);
    }

    [Fact]
    public void SortByApplicationName()
    {
        var result = ShortcutQuery.Apply(Entries, new ShortcutFilter
        {
            SortBy = ShortcutSortField.Application
        });

        Assert.Equal("Visual Studio Code", result[0].ApplicationName);
    }

    [Fact]
    public void NormalizeRemovesFormattingButKeepsChinese()
    {
        Assert.Equal("CTRLSHIFTP", ShortcutQuery.Normalize(" Ctrl + Shift + P "));
        Assert.Equal("保存文件", ShortcutQuery.Normalize("保 存 文 件"));
    }
}
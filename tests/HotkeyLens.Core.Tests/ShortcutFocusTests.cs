using HotkeyLens.Core.Analysis;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

public sealed class ShortcutFocusTests
{
    private static readonly DiscoveredApplication Code = TestData.Application(
        "code", "Visual Studio Code", category: ApplicationCategory.Development);
    private static readonly DiscoveredApplication Shell = TestData.Application(
        "shell", "Windows 资源管理器", @"C:\Windows\explorer.exe", ApplicationCategory.System);
    private static readonly DiscoveredApplication Music = TestData.Application(
        "music", "网易云音乐", category: ApplicationCategory.Media);

    private static readonly IReadOnlyDictionary<string, ApplicationCategory> Categories =
        new Dictionary<string, ApplicationCategory>(StringComparer.Ordinal)
        {
            [Code.Id] = ApplicationCategory.Development,
            [Shell.Id] = ApplicationCategory.System,
            [Music.Id] = ApplicationCategory.Media
        };

    private static readonly ShortcutEntry[] Entries =
    [
        TestData.Entry(Code, "Ctrl+Shift+P", scope: ShortcutScope.Window),
        TestData.Entry(Code, "Ctrl+Alt+E", scope: ShortcutScope.Global),
        TestData.Entry(Shell, "Win+D", scope: ShortcutScope.Global),
        TestData.Entry(Shell, "Alt+Tab", scope: ShortcutScope.Window),
        TestData.Entry(Music, "Ctrl+Alt+P", scope: ShortcutScope.Global),
        TestData.Entry(Music, "Ctrl+S", scope: ShortcutScope.Window)
    ];

    [Fact]
    public void DefaultFocusKeepsGlobalAndSystemKeysOnly()
    {
        var result = ShortcutQuery.Apply(
            Entries,
            new ShortcutFilter { ImportantOnly = true }, categories: Categories);

        Assert.Equal(4, result.Count);
        Assert.DoesNotContain(result, entry => entry.Gesture.Canonical == "Ctrl+Shift+P");
        Assert.DoesNotContain(result, entry => entry.Gesture.Canonical == "Ctrl+S");
        Assert.Contains(result, entry => entry.Gesture.Canonical == "Alt+Tab");
    }

    [Fact]
    public void FocusDoesNotHideEverythingWhenCategoriesAreUnknown()
    {
        var result = ShortcutQuery.Apply(Entries, new ShortcutFilter { ImportantOnly = true });

        Assert.Equal(3, result.Count);
        Assert.All(result, entry => Assert.Equal(ShortcutScope.Global, entry.Scope));
    }

    [Fact]
    public void FocusIsOffWhenTheUserAsksForEverything()
    {
        var result = ShortcutQuery.Apply(Entries, new ShortcutFilter { ImportantOnly = false }, categories: Categories);

        Assert.Equal(Entries.Length, result.Count);
    }

    [Fact]
    public void FocusCombinesWithApplicationAndScopeFilters()
    {
        var result = ShortcutQuery.Apply(
            Entries,
            new ShortcutFilter
            {
                ApplicationId = Code.Id,
                Scope = ShortcutScope.Global,
                ImportantOnly = true
            }, categories: Categories);

        var entry = Assert.Single(result);
        Assert.Equal("Ctrl+Alt+E", entry.Gesture.Canonical);
    }

    [Fact]
    public void UnknownApplicationIdWithCategoryFilterIsExcluded()
    {
        var result = ShortcutQuery.Apply(
            [TestData.Entry(Code, "Ctrl+Alt+Q", scope: ShortcutScope.Global)],
            new ShortcutFilter
            {
                Category = ApplicationCategory.System,
                ImportantOnly = true
            }, categories: Categories);

        Assert.Empty(result);
    }

    [Fact]
    public void EmptySnapshotProducesEmptyFocusList()
    {
        var result = ShortcutQuery.Apply([], new ShortcutFilter { ImportantOnly = true }, categories: Categories);

        Assert.Empty(result);
    }

    [Fact]
    public void FocusTreatsSystemShellAsImportantEvenInsideWindows()
    {
        var result = ShortcutQuery.Apply(
            [TestData.Entry(Shell, "Alt+Tab", scope: ShortcutScope.Window)],
            new ShortcutFilter { ImportantOnly = true }, categories: Categories);

        Assert.Single(result);
    }

    [Fact]
    public void SequencedShortcutsRemainListedWhenGlobal()
    {
        var sequence = TestData.Entry(Code, "Ctrl+K", scope: ShortcutScope.Global, sequence: "Ctrl+K, Ctrl+C");
        var result = ShortcutQuery.Apply(
            [sequence],
            new ShortcutFilter { ImportantOnly = true },
            categories: new Dictionary<string, ApplicationCategory> { [Code.Id] = ApplicationCategory.Development });

        Assert.Single(result);
    }
}


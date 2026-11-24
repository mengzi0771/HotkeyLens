using HotkeyLens.Core.Adapters;
using HotkeyLens.Core.Catalog;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void EmbeddedCatalogLoadsWithSupportedApplications()
    {
        var catalog = ShortcutCatalog.Default;

        Assert.Equal(1, catalog.SchemaVersion);
        Assert.NotEmpty(catalog.CatalogUpdated);
        Assert.True(catalog.Applications.Count >= 5);
        Assert.Contains(catalog.Applications, app => app.Key == "vscode");
        Assert.All(catalog.Applications, app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Name));
            Assert.NotEmpty(app.ProcessNames);
            Assert.StartsWith("https://", app.SourceReference);
            Assert.NotEmpty(app.Shortcuts);
        });
    }

    [Fact]
    public void EveryCatalogShortcutCanBeRepresentedWithoutGuessing()
    {
        var catalog = ShortcutCatalog.Default;
        var failures = new List<string>();

        foreach (var app in catalog.Applications)
        {
            foreach (var shortcut in app.Shortcuts)
            {
                if (!KeySequenceParser.TryParse(shortcut.Keys, out var gesture, out var sequence))
                {
                    failures.Add($"{app.Key}: {shortcut.Keys}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(shortcut.Sequence))
                {
                    if (!KeySequenceParser.TryParse(shortcut.Sequence, out _, out var fullSequence))
                    {
                        failures.Add($"{app.Key}: {shortcut.Sequence}");
                        continue;
                    }

                    if (fullSequence is null)
                    {
                        failures.Add($"{app.Key}: sequence has one chord");
                    }
                }

                Assert.NotNull(gesture);
                Assert.NotEmpty(gesture!.Canonical);
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void MatchesByProcessNameCaseInsensitively()
    {
        var catalog = ShortcutCatalog.Default;
        var app = TestData.Application(
            "code",
            "Visual Studio Code",
            @"C:\Users\example\AppData\Local\Programs\Microsoft VS Code\Code.EXE");

        var matches = catalog.Match(app).ToList();

        var match = Assert.Single(matches);
        Assert.Equal("vscode", match.Key);
    }

    [Fact]
    public void UnsupportedProcessHasNoCatalogMatch()
    {
        var catalog = ShortcutCatalog.Default;
        var app = TestData.Application("unknown", "Unknown", @"C:\Tools\unknown.exe");
        Assert.Empty(catalog.Match(app));
    }
}
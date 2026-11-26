using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Adapters;
using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

public sealed class SnipasteConfigTests
{
    private static readonly DiscoveredApplication Snipaste = TestData.Application(
        "snipaste",
        "Snipaste",
        @"C:\Program Files\Snipaste\Snipaste.exe");

    [Fact]
    public async Task ReadsCustomHotkeysFromConfigIni()
    {
        using var directory = new TempDirectory();
        var config = directory.File("config.ini");
        await File.WriteAllTextAsync(config, """
            ; Snipaste settings
            [general]
            language=zh-cn
            start_with_system=1

            [hotkey]
            snip=F1
            paste=F3
            hide=Ctrl+Shift+H
            """);

        var result = await RunAsync(config);

        Assert.Equal(3, result.Shortcuts.Count);
        Assert.All(result.Shortcuts, entry =>
        {
            Assert.Equal(ShortcutScope.Global, entry.Scope);
            Assert.Equal(ShortcutSourceKind.UserConfiguration, entry.SourceKind);
            Assert.Equal(ShortcutOrigin.UserConfigured, entry.Origin);
        });
        Assert.Contains(result.Shortcuts, entry => entry.Gesture.Canonical == "F1" && entry.ActionName == "截图");
        Assert.Contains(result.Shortcuts, entry => entry.Gesture.Canonical == "F3" && entry.ActionName == "贴图");
        Assert.Contains(result.Shortcuts, entry => entry.Gesture.Canonical == "Ctrl+Shift+H");
    }

    [Fact]
    public async Task MissingConfigFallsBackToOfficialDefaultsWithDiagnostic()
    {
        var result = await RunAsync(null);

        Assert.Empty(result.Shortcuts);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("沿用官方默认", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnparseableHotkeyValuesAreSkippedNotGuessed()
    {
        using var directory = new TempDirectory();
        var config = directory.File("config.ini");
        await File.WriteAllTextAsync(config, """
            [hotkey]
            snip=0x00400000
            paste=not-a-key
            """);

        var result = await RunAsync(config);

        Assert.Empty(result.Shortcuts);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("无法解析", StringComparison.Ordinal));
    }

    [Fact]
    public void IniParserIgnoresCommentsAndKeepsSections()
    {
        var entries = SnipasteConfigAdapter.ParseIni("\uFEFF" + """
            # comment
            [Hotkeys]
            ; disabled=Ctrl+Alt+X
            Snip = "Ctrl+Alt+S"
            [Other]
            key=value
            """);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Hotkeys", entries[0].Section);
        Assert.Equal("Ctrl+Alt+S", entries[0].Value);
        Assert.Equal("Other", entries[1].Section);
    }

    private static async Task<AdapterScanResult> RunAsync(string? configPath)
    {
        var descriptor = new ConfigurationFileDescriptor
        {
            Format = ConfigurationFormat.SnipasteConfig,
            ProcessName = "Snipaste.exe",
            ProductName = "Snipaste",
            FilePath = configPath ?? string.Empty,
            Description = "Snipaste 用户 config.ini",
            SettingsHint = "test"
        };

        var adapter = new SnipasteConfigAdapter(() =>
            configPath is null ? Array.Empty<ConfigurationFileDescriptor>() : [descriptor]);

        return await adapter.ScanAsync(
            new ScanContext
            {
                Applications = [Snipaste],
                Options = new ScanOptions(),
                DataDirectory = Path.GetTempPath()
            },
            null,
            CancellationToken.None);
    }
}

using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Adapters;
using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

public sealed class ChromiumPreferencesTests
{
    private static readonly DiscoveredApplication Chrome = TestData.Application(
        "chrome",
        "Google Chrome",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe");

    private static readonly DiscoveredApplication Edge = TestData.Application(
        "edge",
        "Microsoft Edge",
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe");

    [Fact]
    public async Task ReadsFlatCommandShapeWithGlobalAndBrowserScope()
    {
        using var directory = new TempDirectory();
        var preferences = directory.File("Preferences");
        await File.WriteAllTextAsync(preferences, """
            {
              "extensions": {
                "settings": {
                  "abcdefghijklmnopqrstuvwxyz123456": { "manifest": { "name": "沉浸式翻译" } }
                },
                "commands": {
                  "windows:Alt+A": { "command_name": "toggleTranslatePage", "extension": "abcdefghijklmnopqrstuvwxyz123456", "global": true },
                  "windows:Alt+Shift+S": { "command_name": "toggleSidePanel", "extension": "abcdefghijklmnopqrstuvwxyz123456", "global": false }
                }
              }
            }
            """);

        var result = await RunAsync(Chrome, preferences, "chrome.exe", "Google Chrome");

        Assert.Equal(2, result.Shortcuts.Count);
        var global = Assert.Single(result.Shortcuts, entry => entry.Scope == ShortcutScope.Global);
        Assert.Equal("Alt+A", global.Gesture.Canonical);
        Assert.Contains("沉浸式翻译", global.ActionName);
        Assert.Equal(ShortcutOrigin.UserConfigured, global.Origin);
        Assert.Equal(ShortcutSourceKind.UserConfiguration, global.SourceKind);
        Assert.False(string.IsNullOrWhiteSpace(global.ResolutionGuide));

        var browserScoped = Assert.Single(result.Shortcuts, entry => entry.Scope == ShortcutScope.Application);
        Assert.Equal("Alt+Shift+S", browserScoped.Gesture.Canonical);
        Assert.Contains("浏览器内", browserScoped.Context);
    }

    [Fact]
    public async Task ReadsClassicNestedCommandShape()
    {
        using var directory = new TempDirectory();
        var preferences = directory.File("Preferences");
        await File.WriteAllTextAsync(preferences, """
            {
              "extensions": {
                "commands": {
                  "abcdefghijklmnop": {
                    "toggle": { "keystroke": "Ctrl+Shift+Y", "global": false },
                    "pin": { "keystroke": "Ctrl+Shift+U", "global": true }
                  }
                }
              }
            }
            """);

        var result = await RunAsync(Chrome, preferences, "chrome.exe", "Google Chrome");

        Assert.Equal(2, result.Shortcuts.Count);
        Assert.Contains(result.Shortcuts, entry => entry.Scope == ShortcutScope.Global);
        Assert.Contains(result.Shortcuts, entry => entry.Scope == ShortcutScope.Application);
        Assert.Contains(result.Shortcuts, entry => entry.ActionName.Contains("abcdefgh", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingPreferencesDegradesToDiagnostic()
    {
        var result = await RunAsync(Chrome, null, "chrome.exe", "Google Chrome");

        Assert.Empty(result.Shortcuts);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("没有找到", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CorruptJsonIsReportedWithoutThrowing()
    {
        using var directory = new TempDirectory();
        var preferences = directory.File("Preferences");
        await File.WriteAllTextAsync(preferences, "{ \"extensions\": { \"commands\": ");

        var result = await RunAsync(Chrome, preferences, "chrome.exe", "Google Chrome");

        Assert.Empty(result.Shortcuts);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Warning &&
            diagnostic.Message.Contains("不是有效的 JSON", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EdgeProfileUsesItsOwnApplicationIdentity()
    {
        using var directory = new TempDirectory();
        var preferences = directory.File("Preferences");
        await File.WriteAllTextAsync(preferences, """
            {
              "extensions": {
                "commands": {
                  "windows:Alt+Shift+M": { "command_name": "openSidebar", "extension": "edgeextensionid01", "global": true }
                }
              }
            }
            """);

        var result = await RunAsync(Edge, preferences, "msedge.exe", "Microsoft Edge");

        var entry = Assert.Single(result.Shortcuts);
        Assert.Equal(Edge.Id, entry.ApplicationId);
        Assert.Equal(ShortcutScope.Global, entry.Scope);
        Assert.Contains("Edge", entry.SourceName);
    }

    [Fact]
    public async Task EmptyKeystrokeIsNotTreatedAsShortcut()
    {
        using var directory = new TempDirectory();
        var preferences = directory.File("Preferences");
        await File.WriteAllTextAsync(preferences, """
            {
              "extensions": {
                "commands": {
                  "abcdefghijklmnop": {
                    "emptyCommand": { "keystroke": "", "global": false },
                    "realCommand": { "keystroke": "Alt+R", "global": false }
                  }
                }
              }
            }
            """);

        var result = await RunAsync(Chrome, preferences, "chrome.exe", "Google Chrome");

        Assert.Single(result.Shortcuts);
        Assert.Equal("Alt+R", result.Shortcuts[0].Gesture.Canonical);
    }

    private static async Task<AdapterScanResult> RunAsync(
        DiscoveredApplication application,
        string? preferencesPath,
        string processName,
        string productName)
    {
        var descriptor = new ConfigurationFileDescriptor
        {
            Format = ConfigurationFormat.ChromiumPreferences,
            ProcessName = processName,
            ProductName = productName,
            FilePath = preferencesPath ?? string.Empty,
            Description = $"{productName} Preferences",
            SettingsHint = "test"
        };

        var adapter = new ChromiumExtensionShortcutsAdapter(() =>
            preferencesPath is null ? Array.Empty<ConfigurationFileDescriptor>() : [descriptor]);

        return await adapter.ScanAsync(
            new ScanContext
            {
                Applications = [application],
                Options = new ScanOptions(),
                DataDirectory = Path.GetTempPath()
            },
            null,
            CancellationToken.None);
    }
}

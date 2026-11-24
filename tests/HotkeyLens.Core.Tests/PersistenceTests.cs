using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Models;
using HotkeyLens.Core.Persistence;

namespace HotkeyLens.Core.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task SnapshotRoundTripsWithoutLosingEvidence()
    {
        using var directory = new TempDirectory();
        var store = new JsonScanSnapshotStore(directory.Path);
        var app = TestData.Application("code", "Visual Studio Code", @"C:\Tools\Code.exe");
        var snapshot = new ScanSnapshot
        {
            ScannedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero),
            DurationMilliseconds = 1234,
            Options = new ScanOptions { IncludeUiAutomation = false, UiAutomationTimeoutSeconds = 30 },
            Applications = [app],
            Shortcuts =
            [
                TestData.Entry(app, "Ctrl+Shift+P", sequence: "Ctrl+K Ctrl+S")
            ],
            ApplicationResults =
            [
                new ApplicationScanResult
                {
                    ApplicationId = app.Id,
                    ApplicationName = app.Name,
                    State = ScanState.Scanned,
                    ShortcutCount = 1,
                    Detail = "来自测试",
                    CheckedSources = ["测试来源"]
                }
            ],
            Diagnostics =
            [
                new ScanDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Source = "测试",
                    Message = "诊断消息"
                }
            ],
            Statistics = new ScanStatistics
            {
                TotalApplications = 1,
                TotalShortcuts = 1,
                ByScope = new Dictionary<ShortcutScope, int> { [ShortcutScope.Window] = 1 }
            }
        };

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.ScannedAt, loaded!.ScannedAt);
        Assert.Equal(1234, loaded.DurationMilliseconds);
        Assert.False(loaded.Options.IncludeUiAutomation);
        Assert.Equal(30, loaded.Options.UiAutomationTimeoutSeconds);
        Assert.Single(loaded.Applications);
        var shortcut = Assert.Single(loaded.Shortcuts);
        Assert.Equal("Ctrl+K Ctrl+S", shortcut.KeySequence);
        Assert.Equal("Ctrl+Shift+P", shortcut.Gesture.Canonical);
        Assert.Equal(ConfidenceLevel.High, shortcut.Confidence);
        Assert.Single(loaded.ApplicationResults);
        Assert.Single(loaded.Diagnostics);
        Assert.Equal(1, loaded.Statistics.ByScope[ShortcutScope.Window]);
        Assert.True(File.Exists(store.SnapshotPath));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task MissingSnapshotReturnsNull()
    {
        using var directory = new TempDirectory();
        var store = new JsonScanSnapshotStore(directory.Path);
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task CorruptSnapshotRaisesInvalidData()
    {
        using var directory = new TempDirectory();
        var store = new JsonScanSnapshotStore(directory.Path);
        await File.WriteAllTextAsync(store.SnapshotPath, "{ this is not json");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
    }

    [Fact]
    public async Task IncompatibleSnapshotVersionRaisesInvalidData()
    {
        using var directory = new TempDirectory();
        var store = new JsonScanSnapshotStore(directory.Path);
        await File.WriteAllTextAsync(store.SnapshotPath, """{"schemaVersion":999}""");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
    }

    [Fact]
    public async Task SavingTwiceReplacesSnapshotAtomically()
    {
        using var directory = new TempDirectory();
        var store = new JsonScanSnapshotStore(directory.Path);
        await store.SaveAsync(ScanSnapshot.Empty() with { DurationMilliseconds = 1 });
        await store.SaveAsync(ScanSnapshot.Empty() with { DurationMilliseconds = 2 });

        var reloadedStore = new JsonScanSnapshotStore(directory.Path);
        var loaded = await reloadedStore.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.DurationMilliseconds);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentWritersProduceOneReadableSnapshotAndNoTempFiles()
    {
        using var directory = new TempDirectory();
        var firstStore = new JsonScanSnapshotStore(directory.Path);
        var secondStore = new JsonScanSnapshotStore(directory.Path);

        await Task.WhenAll(
            firstStore.SaveAsync(ScanSnapshot.Empty() with { DurationMilliseconds = 11 }),
            secondStore.SaveAsync(ScanSnapshot.Empty() with { DurationMilliseconds = 22 }));

        var loaded = await firstStore.LoadAsync();
        Assert.NotNull(loaded);
        Assert.True(loaded!.DurationMilliseconds is 11 or 22);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task FailedSaveKeepsExistingSnapshotAndReportsActionableError()
    {
        using var directory = new TempDirectory();
        var store = new JsonScanSnapshotStore(directory.Path);
        await store.SaveAsync(ScanSnapshot.Empty() with { DurationMilliseconds = 1 });

        await using (var exclusiveLock = new FileStream(
                         store.SnapshotPath,
                         FileMode.Open,
                         FileAccess.ReadWrite,
                         FileShare.None))
        {
            var exception = await Assert.ThrowsAsync<IOException>(
                () => store.SaveAsync(ScanSnapshot.Empty() with { DurationMilliseconds = 2 }));
            Assert.Contains("原有扫描结果不会被覆盖", exception.Message);
            Assert.True(exception.InnerException is IOException or UnauthorizedAccessException);
        }

        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.DurationMilliseconds);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task ClearRemovesSnapshot()
    {
        using var directory = new TempDirectory();
        var store = new JsonScanSnapshotStore(directory.Path);
        await store.SaveAsync(ScanSnapshot.Empty());
        await store.ClearAsync();

        Assert.False(File.Exists(store.SnapshotPath));
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task PreferencesDefaultAndRoundTrip()
    {
        using var directory = new TempDirectory();
        var store = new JsonUserPreferencesStore(directory.Path);
        var defaults = await store.LoadAsync();

        Assert.Equal("System", defaults.Theme);
        Assert.True(defaults.ScanOptions.IncludeUiAutomation);

        var preferences = defaults with
        {
            Theme = "Dark",
            ScanOptions = defaults.ScanOptions with { IncludeUiAutomation = false },
            ShowWelcomeOnStartup = false
        };
        await store.SaveAsync(preferences);
        var loaded = await store.LoadAsync();

        Assert.Equal("Dark", loaded.Theme);
        Assert.False(loaded.ScanOptions.IncludeUiAutomation);
        Assert.False(loaded.ShowWelcomeOnStartup);
    }
}
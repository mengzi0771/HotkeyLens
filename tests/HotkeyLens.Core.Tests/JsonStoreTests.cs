using System.Text.Json;
using System.Text.Json.Serialization;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Analysis;
using HotkeyLens.Core.Models;
using HotkeyLens.Core.Persistence;

namespace HotkeyLens.Core.Tests;

public sealed class JsonStoreTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task SavingTwiceKeepsPreviousSnapshotAsBackup()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new JsonScanSnapshotStore(directory);
            var first = Snapshot(new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero), 1);
            var second = Snapshot(new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero), 2);

            await store.SaveAsync(first);
            Assert.True(File.Exists(store.SnapshotPath));
            Assert.False(File.Exists(store.SnapshotBackupPath));

            await store.SaveAsync(second);
            Assert.True(File.Exists(store.SnapshotPath));
            Assert.True(File.Exists(store.SnapshotBackupPath));

            var loaded = await store.LoadAsync();
            Assert.NotNull(loaded);
            Assert.Equal(second.ScannedAt, loaded!.ScannedAt);
            Assert.Equal(2, loaded.Statistics.TotalShortcuts);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptPrimarySnapshotFallsBackToBackup()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new JsonScanSnapshotStore(directory);
            var first = Snapshot(new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero), 1);
            var second = Snapshot(new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero), 2);
            await store.SaveAsync(first);
            await store.SaveAsync(second);

            await File.WriteAllTextAsync(store.SnapshotPath, "{ this is not json");

            var loaded = await store.LoadAsync();
            Assert.NotNull(loaded);
            Assert.Equal(first.ScannedAt, loaded!.ScannedAt);
            Assert.Equal(1, loaded.Statistics.TotalShortcuts);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task VersionOneSnapshotIsMigratedToFiveClassClassification()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var alpha = TestData.Application("alpha", "Alpha");
            var beta = TestData.Application("beta", "Beta");
            var entries = new[]
            {
                TestData.Entry(alpha, "Ctrl+Shift+A", ShortcutScope.Global, ShortcutSourceKind.ShortcutFile),
                TestData.Entry(beta, "Ctrl+Shift+A", ShortcutScope.Global, ShortcutSourceKind.ShortcutFile)
            };
            var conflicts = ShortcutConflictAnalyzer.Analyze(entries);
            var legacy = new ScanSnapshot
            {
                SchemaVersion = 1,
                ScannedAt = new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero),
                Shortcuts = entries,
                Conflicts = conflicts,
                Statistics = new ScanStatistics { TotalShortcuts = 2, GlobalCompetitions = 1 }
            };

            var json = JsonSerializer.Serialize(legacy, JsonOptions)
                .Replace("\"classification\": \"ConfirmedConflict\"", "\"classification\": \"NoConflict\"");
            var store = new JsonScanSnapshotStore(directory);
            await File.WriteAllTextAsync(store.SnapshotPath, json);

            var loaded = await store.LoadAsync();

            Assert.NotNull(loaded);
            Assert.Equal(ScanSnapshot.CurrentSchemaVersion, loaded!.SchemaVersion);
            var conflict = Assert.Single(loaded.Conflicts);
            Assert.Equal(ConflictKind.GlobalHotkeyCompetition, conflict.Kind);
            Assert.Equal(ConflictClassification.ConfirmedConflict, conflict.Classification);
            Assert.Equal(1, loaded.Statistics.ConfirmedConflicts);
            Assert.Equal(0, loaded.Statistics.NoConflictGroups);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PreferencesKeepUpdateFieldsAndBackup()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new JsonUserPreferencesStore(directory);
            var first = new UserPreferences
            {
                Theme = "Dark",
                AutoCheckUpdates = true,
                LastUpdateCheckUtc = new DateTimeOffset(2026, 11, 24, 7, 0, 0, TimeSpan.Zero),
                SkippedVersion = "1.0.0"
            };
            var second = first with
            {
                AutoCheckUpdates = false,
                LastUpdateCheckUtc = new DateTimeOffset(2026, 11, 25, 7, 0, 0, TimeSpan.Zero),
                SkippedVersion = "1.1.0"
            };

            await store.SaveAsync(first);
            await store.SaveAsync(second);

            var loaded = await store.LoadAsync();
            Assert.False(loaded.AutoCheckUpdates);
            Assert.Equal(second.LastUpdateCheckUtc, loaded.LastUpdateCheckUtc);
            Assert.Equal("1.1.0", loaded.SkippedVersion);
            Assert.True(File.Exists(store.PreferencesBackupPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ClearRemovesSnapshotAndBackup()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new JsonScanSnapshotStore(directory);
            await store.SaveAsync(Snapshot(DateTimeOffset.UtcNow, 1));
            await store.SaveAsync(Snapshot(DateTimeOffset.UtcNow.AddMinutes(1), 2));

            await store.ClearAsync();

            Assert.False(File.Exists(store.SnapshotPath));
            Assert.False(File.Exists(store.SnapshotBackupPath));
            Assert.Null(await store.LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ScanSnapshot Snapshot(DateTimeOffset scannedAt, int shortcutCount) => new()
    {
        ScannedAt = scannedAt,
        Statistics = new ScanStatistics { TotalShortcuts = shortcutCount }
    };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "HotkeyLens-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
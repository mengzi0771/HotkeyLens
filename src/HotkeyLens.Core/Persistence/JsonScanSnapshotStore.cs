using System.Text.Json;
using System.Text.Json.Serialization;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Persistence;

/// <summary>
/// Stores exactly one immutable scan snapshot. A single JSON document is
/// sufficient for this read-mostly app and avoids a database dependency.
/// Replacements keep a .bak copy so a failed write or a locked temporary file
/// never destroys the last readable snapshot.
/// </summary>
public sealed class JsonScanSnapshotStore : IScanSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    private readonly string _dataDirectory;

    public JsonScanSnapshotStore(string? dataDirectory = null)
    {
        _dataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HotkeyLens")
            : dataDirectory;
        SnapshotPath = Path.Combine(_dataDirectory, "scan-snapshot.json");
        SnapshotBackupPath = SnapshotPath + ".bak";
    }

    public string SnapshotPath { get; }

    public string SnapshotBackupPath { get; }

    public async Task<ScanSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var primaryExists = File.Exists(SnapshotPath);
        var backupExists = File.Exists(SnapshotBackupPath);
        if (!primaryExists && !backupExists)
        {
            return null;
        }

        InvalidDataException? primaryError = null;
        if (primaryExists)
        {
            try
            {
                return await ReadSnapshotAsync(SnapshotPath, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
            {
                primaryError = exception;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                primaryError = new InvalidDataException("扫描快照无法读取。", exception);
            }
        }

        if (backupExists)
        {
            try
            {
                return await ReadSnapshotAsync(SnapshotBackupPath, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    "扫描快照和它的备份都无法读取，请重新扫描。原始错误：" + exception.Message,
                    primaryError ?? exception);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException(
                    "扫描快照备份无法读取，请重新扫描。原始错误：" + exception.Message,
                    primaryError ?? exception);
            }
        }

        throw primaryError ?? new InvalidDataException("扫描快照不存在。");
    }

    public async Task SaveAsync(ScanSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var temporaryPath = Path.Combine(
            _dataDirectory,
            $"scan-snapshot.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            await AtomicFileReplace.MoveWithRetryAsync(
                temporaryPath,
                SnapshotPath,
                SnapshotBackupPath,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"无法保存扫描结果到“{SnapshotPath}”。请确认当前用户对“{_dataDirectory}”有写入权限，且没有其他 HotkeyLens 实例正在写入同一文件。若目录权限正常，请检查安全软件的受控文件夹/应用防护规则是否阻止了 HotkeyLens。原有扫描结果不会被覆盖。系统错误：{exception.Message}",
                exception);
        }
        finally
        {
            AtomicFileReplace.TryDelete(temporaryPath);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteIfExists(SnapshotPath);
        DeleteIfExists(SnapshotBackupPath);
        return Task.CompletedTask;
    }

    private static async Task<ScanSnapshot> ReadSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        ScanSnapshot? snapshot;
        try
        {
            snapshot = await JsonSerializer.DeserializeAsync<ScanSnapshot>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("扫描快照不是有效的 JSON。", exception);
        }

        if (snapshot is null)
        {
            throw new InvalidDataException("扫描快照为空。");
        }

        if (snapshot.SchemaVersion == 1)
        {
            snapshot = SnapshotMigrations.MigrateV1(snapshot);
        }

        if (snapshot.SchemaVersion != ScanSnapshot.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"扫描快照版本 {snapshot.SchemaVersion} 与当前版本 {ScanSnapshot.CurrentSchemaVersion} 不兼容。");
        }

        return snapshot;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class JsonUserPreferencesStore : IUserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dataDirectory;

    public JsonUserPreferencesStore(string? dataDirectory = null)
    {
        _dataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HotkeyLens")
            : dataDirectory;
        PreferencesPath = Path.Combine(_dataDirectory, "preferences.json");
        PreferencesBackupPath = PreferencesPath + ".bak";
    }

    public string PreferencesPath { get; }

    public string PreferencesBackupPath { get; }

    public async Task<UserPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(PreferencesPath) && !File.Exists(PreferencesBackupPath))
        {
            return new UserPreferences();
        }

        InvalidDataException? primaryError = null;
        if (File.Exists(PreferencesPath))
        {
            try
            {
                return await ReadPreferencesAsync(PreferencesPath, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
            {
                primaryError = exception;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                primaryError = new InvalidDataException("用户设置无法读取。", exception);
            }
        }

        if (File.Exists(PreferencesBackupPath))
        {
            try
            {
                return await ReadPreferencesAsync(PreferencesBackupPath, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    "用户设置和它的备份都无法读取。可以删除设置文件后重新配置。原始错误：" + exception.Message,
                    primaryError ?? exception);
            }
        }

        // Preferences are not critical scan evidence. Keep the application usable
        // if an older version left a damaged file without a backup.
        return new UserPreferences();
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var temporaryPath = Path.Combine(
            _dataDirectory,
            $"preferences.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            await AtomicFileReplace.MoveWithRetryAsync(
                temporaryPath,
                PreferencesPath,
                PreferencesBackupPath,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"无法保存用户设置到“{PreferencesPath}”。请确认当前用户对“{_dataDirectory}”有写入权限。若目录权限正常，请检查安全软件的受控文件夹/应用防护规则是否阻止了 HotkeyLens。原有设置不会被覆盖。系统错误：{exception.Message}",
                exception);
        }
        finally
        {
            AtomicFileReplace.TryDelete(temporaryPath);
        }
    }

    private static async Task<UserPreferences> ReadPreferencesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);
        try
        {
            return await JsonSerializer.DeserializeAsync<UserPreferences>(
                       stream,
                       JsonOptions,
                       cancellationToken).ConfigureAwait(false)
                   ?? new UserPreferences();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("用户设置不是有效的 JSON。", exception);
        }
    }
}

internal static class SnapshotMigrations
{
    public static ScanSnapshot MigrateV1(ScanSnapshot snapshot)
    {
        var conflicts = snapshot.Conflicts
            .Select(conflict => conflict with
            {
                Classification = LegacyClassification(conflict.Kind)
            })
            .ToList();

        var statistics = snapshot.Statistics with
        {
            DuplicateGroups = conflicts.Count(conflict =>
                conflict.Classification == ConflictClassification.NormalDuplicate),
            NormalDuplicates = conflicts.Count(conflict =>
                conflict.Classification == ConflictClassification.NormalDuplicate),
            PotentialConflicts = conflicts.Count(conflict =>
                conflict.Classification == ConflictClassification.PotentialConflict),
            ConfirmedConflicts = conflicts.Count(conflict =>
                conflict.Classification == ConflictClassification.ConfirmedConflict),
            GlobalCompetitions = conflicts.Count(conflict =>
                conflict.Classification == ConflictClassification.ConfirmedConflict &&
                conflict.Kind == ConflictKind.GlobalHotkeyCompetition),
            UnknownFindings = conflicts.Count(conflict =>
                conflict.Classification == ConflictClassification.Unknown),
            NoConflictGroups = Math.Max(
                0,
                snapshot.Shortcuts
                    .Where(entry => entry.IsEnabled && !entry.IsSequence)
                    .Select(entry => entry.Gesture.Canonical)
                    .Distinct(StringComparer.Ordinal)
                    .Count() -
                conflicts.Select(conflict => conflict.Gesture.Canonical)
                    .Distinct(StringComparer.Ordinal)
                    .Count())
        };

        return snapshot with
        {
            SchemaVersion = ScanSnapshot.CurrentSchemaVersion,
            Conflicts = conflicts,
            Statistics = statistics,
            EngineVersion = "1.0 (migrated)"
        };
    }

    private static ConflictClassification LegacyClassification(ConflictKind kind) => kind switch
    {
        ConflictKind.GlobalHotkeyCompetition => ConflictClassification.ConfirmedConflict,
        ConflictKind.DuplicateBinding => ConflictClassification.NormalDuplicate,
        ConflictKind.SameApplicationOverlap => ConflictClassification.ConfirmedConflict,
        ConflictKind.Unknown => ConflictClassification.Unknown,
        _ => ConflictClassification.PotentialConflict
    };
}

internal static class AtomicFileReplace
{
    public static async Task MoveWithRetryAsync(
        string temporaryPath,
        string destinationPath,
        string backupPath,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 5;
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(destinationPath))
                {
                    // File.Replace is atomic on NTFS and keeps the previous file
                    // as the backup in a single operation.
                    File.Replace(temporaryPath, destinationPath, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath, overwrite: false);
                }

                return;
            }
            catch (PlatformNotSupportedException)
            {
                break;
            }
            catch (IOException exception)
            {
                lastFailure = exception;
            }
            catch (UnauthorizedAccessException exception)
            {
                lastFailure = exception;
            }

            if (attempt < maximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // Fallback for file systems that do not implement File.Replace. The old
        // destination is moved to .bak first, so a failed second move can be
        // rolled back without losing the previous snapshot.
        var movedOldFile = false;
        try
        {
            if (File.Exists(destinationPath))
            {
                File.Move(destinationPath, backupPath, overwrite: true);
                movedOldFile = true;
            }

            File.Move(temporaryPath, destinationPath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (movedOldFile && !File.Exists(destinationPath) && File.Exists(backupPath))
            {
                try
                {
                    File.Move(backupPath, destinationPath, overwrite: false);
                }
                catch (Exception restoreException) when (restoreException is IOException or UnauthorizedAccessException)
                {
                    throw new IOException(
                        $"新文件替换失败，且旧文件仍保存在“{backupPath}”，可手动恢复。系统错误：{restoreException.Message}",
                        exception);
                }
            }

            throw new IOException(
                $"无法替换“{destinationPath}”。旧文件保存在“{backupPath}”。系统错误：{exception.Message}",
                lastFailure ?? exception);
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
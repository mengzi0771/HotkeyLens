using System.Text.Json;
using System.Text.Json.Serialization;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Persistence;

/// <summary>
/// Stores exactly one immutable scan snapshot. A single JSON document is
/// sufficient for this read-mostly app and avoids a database dependency.
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
    }

    public string SnapshotPath { get; }

    public async Task<ScanSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SnapshotPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                SnapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);
            var snapshot = await JsonSerializer.DeserializeAsync<ScanSnapshot>(
                stream,
                JsonOptions,
                cancellationToken);
            if (snapshot is null)
            {
                throw new InvalidDataException("扫描快照为空。");
            }

            if (snapshot.SchemaVersion != ScanSnapshot.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"扫描快照版本 {snapshot.SchemaVersion} 与当前版本 {ScanSnapshot.CurrentSchemaVersion} 不兼容。");
            }

            return snapshot;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("扫描快照不是有效的 JSON。", exception);
        }
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

            await AtomicFileReplace.MoveWithRetryAsync(temporaryPath, SnapshotPath, cancellationToken);
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
        if (File.Exists(SnapshotPath))
        {
            File.Delete(SnapshotPath);
        }

        return Task.CompletedTask;
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
    }

    public string PreferencesPath { get; }

    public async Task<UserPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(PreferencesPath))
        {
            return new UserPreferences();
        }

        try
        {
            await using var stream = new FileStream(
                PreferencesPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);
            return await JsonSerializer.DeserializeAsync<UserPreferences>(
                       stream,
                       JsonOptions,
                       cancellationToken)
                   ?? new UserPreferences();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("用户设置不是有效的 JSON。", exception);
        }
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
}

internal static class AtomicFileReplace
{
    public static async Task MoveWithRetryAsync(
        string temporaryPath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temporaryPath, destinationPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(40 * attempt), cancellationToken);
            }
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
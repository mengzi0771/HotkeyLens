using HotkeyLens.Core.Abstractions;

namespace HotkeyLens.Core.Updates;

/// <summary>
/// Coordinates the 24-hour automatic check throttle, skipped versions and the
/// public GitHub Releases lookup. Automatic checks only discover updates; they
/// never download or install anything.
/// </summary>
public sealed class UpdateService
{
    public static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);

    private readonly GitHubReleaseClient _client;
    private readonly IUserPreferencesStore _preferencesStore;

    public UpdateService(
        GitHubReleaseClient client,
        IUserPreferencesStore preferencesStore,
        string currentVersion)
    {
        _client = client;
        _preferencesStore = preferencesStore;
        if (!SemanticVersion.TryParse(currentVersion, out var parsed))
        {
            throw new ArgumentException($"无效的当前版本号：{currentVersion}", nameof(currentVersion));
        }

        CurrentVersion = parsed;
        CurrentVersionText = parsed.ToString();
    }

    public SemanticVersion CurrentVersion { get; }
    public string CurrentVersionText { get; }

    public async Task<UpdateCheckResult> CheckAsync(
        bool manual,
        CancellationToken cancellationToken = default)
    {
        UserPreferences preferences;
        try
        {
            preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            preferences = new UserPreferences();
        }

        if (!manual && !preferences.AutoCheckUpdates)
        {
            return UpdateCheckResult.Simple(
                UpdateCheckStatus.Disabled,
                CurrentVersionText,
                "自动检查更新已关闭。可随时手动检查。");
        }

        if (!manual &&
            preferences.LastUpdateCheckUtc is { } lastChecked &&
            DateTimeOffset.UtcNow - lastChecked < AutomaticCheckInterval)
        {
            return UpdateCheckResult.Simple(
                UpdateCheckStatus.Throttled,
                CurrentVersionText,
                $"距上次检查不足 24 小时（{lastChecked.ToLocalTime():yyyy-MM-dd HH:mm}）。",
                lastChecked);
        }

        var lookup = await _client.FindLatestStableAsync(cancellationToken).ConfigureAwait(false);
        switch (lookup.Status)
        {
            case ReleaseLookupStatus.RateLimited:
                return UpdateCheckResult.Simple(
                    UpdateCheckStatus.RateLimited, CurrentVersionText, lookup.Message);
            case ReleaseLookupStatus.NetworkError:
                return UpdateCheckResult.Simple(
                    UpdateCheckStatus.NetworkError, CurrentVersionText, lookup.Message);
            case ReleaseLookupStatus.InvalidResponse:
                return UpdateCheckResult.Simple(
                    UpdateCheckStatus.InvalidResponse, CurrentVersionText, lookup.Message);
            case ReleaseLookupStatus.NoStableRelease:
                return UpdateCheckResult.Simple(
                    UpdateCheckStatus.NoStableRelease, CurrentVersionText, lookup.Message);
        }

        var release = lookup.Release;
        if (release is null)
        {
            return UpdateCheckResult.Simple(
                UpdateCheckStatus.InvalidResponse, CurrentVersionText, "GitHub Release 响应不完整。");
        }

        var now = DateTimeOffset.UtcNow;
        try
        {
            await _preferencesStore.SaveAsync(
                preferences with { LastUpdateCheckUtc = now },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed timestamp write must not hide an available update.
        }

        if (release.Version.CompareTo(CurrentVersion) <= 0)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.UpToDate,
                CurrentVersionText,
                release.Version,
                release,
                $"当前已是最新正式版本 {CurrentVersionText}。",
                now);
        }

        if (SemanticVersion.TryParse(preferences.SkippedVersion, out var skipped) &&
            skipped.CompareTo(release.Version) == 0)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Skipped,
                CurrentVersionText,
                release.Version,
                release,
                $"版本 {release.Version} 已被跳过。",
                now);
        }

        return new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            CurrentVersionText,
            release.Version,
            release,
            $"发现新版本 {release.Version}。",
            now);
    }

    public async Task SkipVersionAsync(string version, CancellationToken cancellationToken = default)
    {
        var preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _preferencesStore.SaveAsync(
            preferences with { SkippedVersion = version },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SetAutoCheckAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _preferencesStore.SaveAsync(
            preferences with { AutoCheckUpdates = enabled },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetSkippedVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            return preferences.SkippedVersion;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task ClearSkippedVersionAsync(CancellationToken cancellationToken = default)
    {
        var preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _preferencesStore.SaveAsync(
            preferences with { SkippedVersion = null },
            cancellationToken).ConfigureAwait(false);
    }
}
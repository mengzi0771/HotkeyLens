namespace HotkeyLens.Core.Updates;

public enum UpdateCheckStatus
{
    Disabled,
    Throttled,
    UpToDate,
    UpdateAvailable,
    Skipped,
    NetworkError,
    RateLimited,
    NoStableRelease,
    InvalidResponse
}

public enum ReleaseLookupStatus
{
    Found,
    NoStableRelease,
    RateLimited,
    NetworkError,
    InvalidResponse
}

public sealed record UpdateAsset(string Name, string DownloadUrl, long Size);

public sealed record UpdateRelease(
    SemanticVersion Version,
    string TagName,
    string Name,
    DateTimeOffset? PublishedAt,
    string Notes,
    string HtmlUrl,
    IReadOnlyList<UpdateAsset> Assets)
{
    public UpdateAsset? FindAsset(string name)
        => Assets.FirstOrDefault(asset =>
            asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public sealed record ReleaseLookupResult(
    ReleaseLookupStatus Status,
    UpdateRelease? Release,
    string Message);

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    SemanticVersion? LatestVersion,
    UpdateRelease? Release,
    string Message,
    DateTimeOffset? LastCheckedUtc = null)
{
    public bool HasUpdate => Status == UpdateCheckStatus.UpdateAvailable && Release is not null;

    public static UpdateCheckResult Simple(
        UpdateCheckStatus status,
        string currentVersion,
        string message,
        DateTimeOffset? lastCheckedUtc = null)
        => new(status, currentVersion, null, null, message, lastCheckedUtc);
}
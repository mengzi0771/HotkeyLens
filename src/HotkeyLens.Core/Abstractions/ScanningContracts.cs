using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Abstractions;

public sealed record ApplicationDiscoveryResult
{
    public IReadOnlyList<DiscoveredApplication> Applications { get; init; } = Array.Empty<DiscoveredApplication>();
    public IReadOnlyList<ScanDiagnostic> Diagnostics { get; init; } = Array.Empty<ScanDiagnostic>();
}

public interface IApplicationDiscoveryService
{
    Task<ApplicationDiscoveryResult> DiscoverAsync(ScanOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// One-shot process list used to tell whether two applications can compete for
/// a global hotkey right now. Implementations must not install hooks or monitor
/// the keyboard; this is a read of the process table at scan time only.
/// </summary>
public interface IProcessSnapshotProvider
{
    ProcessSnapshot GetSnapshot();
}

/// <summary>
/// One read-only view of the process table. <see cref="IsComplete"/> is false when
/// the enumeration returned an implausibly small list (security software or session
/// isolation can filter it); callers must then treat unmatched applications as
/// unknown instead of "not running".
/// </summary>
public sealed record ProcessSnapshot(IReadOnlySet<string> ProcessNames, bool IsComplete)
{
    public bool Contains(string processName) => ProcessNames.Contains(processName);
}

/// <summary>
/// One evidence collector. Implementations must never invent shortcuts: every
/// result needs a source reference and a confidence rationale.
/// </summary>
public interface IShortcutSourceAdapter
{
    string Id { get; }
    string DisplayName { get; }
    ShortcutSourceKind SourceKind { get; }
    bool IsEnabled(ScanOptions options);

    Task<AdapterScanResult> ScanAsync(
        ScanContext context,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IScanEngine
{
    Task<ScanSnapshot> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IScanSnapshotStore
{
    Task<ScanSnapshot?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ScanSnapshot snapshot, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    string SnapshotPath { get; }
}

public sealed record UserPreferences
{
    public string Theme { get; init; } = "System";
    public ScanOptions ScanOptions { get; init; } = new();
    public bool ShowWelcomeOnStartup { get; init; } = true;

    /// <summary>Automatic update checks are opt-out and never install without confirmation.</summary>
    public bool AutoCheckUpdates { get; init; } = true;

    /// <summary>UTC timestamp of the last successful GitHub release check.</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; init; }

    /// <summary>Version the user explicitly skipped. Exact SemVer match only.</summary>
    public string? SkippedVersion { get; init; }
}

public interface IUserPreferencesStore
{
    Task<UserPreferences> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default);
    string PreferencesPath { get; }
}
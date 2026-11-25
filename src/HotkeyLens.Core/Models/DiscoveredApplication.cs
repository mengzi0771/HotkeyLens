namespace HotkeyLens.Core.Models;

/// <summary>
/// A deduplicated application candidate assembled from local discovery
/// sources. Absent metadata is represented as null rather than guessed.
/// </summary>
public sealed record DiscoveredApplication
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ExecutablePath { get; init; }
    public string? IconPath { get; init; }
    public string? InstallLocation { get; init; }
    public string? Version { get; init; }
    public string? Publisher { get; init; }
    public string? PackageFamilyName { get; init; }
    public ApplicationCategory Category { get; init; } = ApplicationCategory.Other;
    public ApplicationSourceKind SourceKind { get; init; } = ApplicationSourceKind.Unknown;
    public string SourceDetail { get; init; } = string.Empty;
    public string? SettingsPath { get; init; }
    public string? SettingsHint { get; init; }
    public bool IsSystemComponent { get; init; }

    public string ProcessName => string.IsNullOrWhiteSpace(ExecutablePath)
        ? string.Empty
        : Path.GetFileName(ExecutablePath);
}
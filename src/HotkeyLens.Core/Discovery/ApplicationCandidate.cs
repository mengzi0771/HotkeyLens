using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Discovery;

/// <summary>A raw observation from one discovery source before deduplication.</summary>
public sealed record ApplicationCandidate
{
    public required string Name { get; init; }
    public string? ExecutablePath { get; init; }
    public string? IconPath { get; init; }
    public string? InstallLocation { get; init; }
    public string? Version { get; init; }
    public string? Publisher { get; init; }
    public ApplicationCategory? Category { get; init; }
    public required ApplicationSourceKind SourceKind { get; init; }
    public string SourceDetail { get; init; } = string.Empty;
    public string? SettingsPath { get; init; }
    public string? SettingsHint { get; init; }
    public bool IsSystemComponent { get; init; }
}
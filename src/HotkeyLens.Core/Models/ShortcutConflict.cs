using System.Text.Json.Serialization;

namespace HotkeyLens.Core.Models;

public sealed record ConflictParticipant
{
    public required string ShortcutId { get; init; }
    public required string ApplicationId { get; init; }
    public required string ApplicationName { get; init; }
    public required string ActionName { get; init; }
    public required ShortcutScope Scope { get; init; }
    public required ShortcutSourceKind SourceKind { get; init; }
    public required ShortcutOrigin Origin { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public string? Context { get; init; }
}

/// <summary>
/// A grouped finding. Classification is deliberately conservative; equal key
/// chords alone never justify claiming a hotkey registration conflict.
/// </summary>
public sealed record ShortcutConflict
{
    public required string Id { get; init; }
    public ConflictKind Kind { get; init; }
    public ConflictClassification Classification { get; init; } = ConflictClassification.Unknown;
    public ConflictSeverity Severity { get; init; }
    public required ShortcutGesture Gesture { get; init; }
    public required IReadOnlyList<ConflictParticipant> Participants { get; init; }
    public required string Summary { get; init; }
    public required string Rationale { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public required string Recommendation { get; init; }

    [JsonIgnore]
    public bool CountsAsConflict =>
        Classification is ConflictClassification.PotentialConflict or ConflictClassification.ConfirmedConflict;

    [JsonIgnore]
    public bool IsNormalDuplicate => Classification == ConflictClassification.NormalDuplicate;

    [JsonIgnore]
    public bool IsUnknown => Classification == ConflictClassification.Unknown;

    [JsonIgnore]
    public bool IsConfirmedRegistrationRace =>
        Classification == ConflictClassification.ConfirmedConflict &&
        Kind == ConflictKind.GlobalHotkeyCompetition;
}
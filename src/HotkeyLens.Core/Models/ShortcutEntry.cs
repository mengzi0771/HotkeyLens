namespace HotkeyLens.Core.Models;

/// <summary>A single discovered shortcut binding with its evidence trail.</summary>
public sealed record ShortcutEntry
{
    public required string Id { get; init; }
    public required string ApplicationId { get; init; }
    public required string ApplicationName { get; init; }
    public required ShortcutGesture Gesture { get; init; }
    public required string ActionName { get; init; }
    public string Description { get; init; } = string.Empty;
    public ShortcutScope Scope { get; init; } = ShortcutScope.Unknown;
    public ShortcutSourceKind SourceKind { get; init; } = ShortcutSourceKind.Observed;
    public string SourceName { get; init; } = string.Empty;
    public string? SourceReference { get; init; }
    public ConfidenceLevel Confidence { get; init; } = ConfidenceLevel.Unknown;
    public string ConfidenceRationale { get; init; } = string.Empty;
    public ShortcutOrigin Origin { get; init; } = ShortcutOrigin.Unknown;
    public string? Context { get; init; }
    public string? RawValue { get; init; }

    /// <summary>
    /// Full display text for a multi-chord sequence (for example
    /// Ctrl+K Ctrl+S). Gesture describes the first chord; consumers must not
    /// treat a sequence as a single global hotkey.
    /// </summary>
    public string? KeySequence { get; init; }
    public bool IsEnabled { get; init; } = true;
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSequence => !string.IsNullOrWhiteSpace(KeySequence);

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayGesture => IsSequence ? KeySequence! : Gesture.Display;

    public static string BuildId(
        string applicationId,
        string gesture,
        string actionName,
        ShortcutSourceKind sourceKind,
        string? sourceReference = null,
        string? context = null)
        => StableId.Create(
            "sc",
            applicationId,
            gesture,
            actionName,
            sourceKind.ToString(),
            sourceReference,
            context);
}
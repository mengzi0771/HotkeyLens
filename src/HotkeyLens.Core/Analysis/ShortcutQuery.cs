using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Analysis;

public enum ShortcutSortField
{
    Gesture,
    Application,
    Action,
    Scope,
    Confidence,
    Source
}

/// <summary>Filter and sort options for the shortcut browser.</summary>
public sealed record ShortcutFilter
{
    public string SearchText { get; init; } = string.Empty;
    public string? ApplicationId { get; init; }
    public ApplicationCategory? Category { get; init; }
    public ShortcutScope? Scope { get; init; }
    public ShortcutSourceKind? SourceKind { get; init; }
    public ConfidenceLevel? MinimumConfidence { get; init; }
    public ShortcutOrigin? Origin { get; init; }
    public bool OnlyConflicting { get; init; }
    public ShortcutSortField SortBy { get; init; } = ShortcutSortField.Gesture;
    public bool SortDescending { get; init; }
}

/// <summary>
/// In-memory filtering over a saved snapshot. This is intentionally pure and
/// synchronous so the UI can debounce search without touching the disk.
/// </summary>
public static class ShortcutQuery
{
    public static IReadOnlyList<ShortcutEntry> Apply(
        IEnumerable<ShortcutEntry> shortcuts,
        ShortcutFilter filter,
        IReadOnlyList<ShortcutConflict>? conflicts = null,
        IReadOnlyDictionary<string, ApplicationCategory>? categories = null)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        filter ??= new ShortcutFilter();
        var conflictKeys = filter.OnlyConflicting
            ? BuildConflictKeys(conflicts ?? Array.Empty<ShortcutConflict>())
            : null;
        var needle = Normalize(filter.SearchText);

        var query = shortcuts.Where(shortcut =>
        {
            if (filter.Scope is not null && shortcut.Scope != filter.Scope)
            {
                return false;
            }

            if (filter.SourceKind is not null && shortcut.SourceKind != filter.SourceKind)
            {
                return false;
            }

            if (filter.Origin is not null && shortcut.Origin != filter.Origin)
            {
                return false;
            }

            if (filter.MinimumConfidence is not null && shortcut.Confidence < filter.MinimumConfidence)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(filter.ApplicationId) &&
                !string.Equals(shortcut.ApplicationId, filter.ApplicationId, StringComparison.Ordinal))
            {
                return false;
            }

            if (filter.Category is not null)
            {
                if (categories is null ||
                    !categories.TryGetValue(shortcut.ApplicationId, out var category) ||
                    category != filter.Category)
                {
                    return false;
                }
            }

            if (conflictKeys is not null &&
                !conflictKeys.Contains(ConflictKey(shortcut.ApplicationId, shortcut.Gesture.Canonical)))
            {
                return false;
            }

            return needle.Length == 0 || MatchesSearch(shortcut, needle);
        });

        return Sort(query, filter).ToList();
    }

    public static HashSet<string> BuildConflictKeys(IEnumerable<ShortcutConflict> conflicts)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var conflict in conflicts)
        {
            foreach (var participant in conflict.Participants)
            {
                keys.Add(ConflictKey(participant.ApplicationId, conflict.Gesture.Canonical));
            }
        }

        return keys;
    }

    public static bool MatchesSearch(ShortcutEntry shortcut, string normalizedNeedle)
    {
        if (normalizedNeedle.Length == 0)
        {
            return true;
        }

        var values = new[]
        {
            shortcut.ApplicationName,
            shortcut.ActionName,
            shortcut.Description,
            shortcut.DisplayGesture,
            shortcut.Gesture.Canonical,
            shortcut.KeySequence,
            shortcut.Context,
            shortcut.SourceName,
            shortcut.ConfidenceRationale,
            shortcut.RawValue
        };

        return values.Any(value =>
            !string.IsNullOrEmpty(value) &&
            Normalize(value).Contains(normalizedNeedle, StringComparison.OrdinalIgnoreCase));
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character > 0x7F)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string ConflictKey(string applicationId, string canonical)
        => applicationId + "\u001f" + canonical;

    private static IOrderedEnumerable<ShortcutEntry> Sort(
        IEnumerable<ShortcutEntry> shortcuts,
        ShortcutFilter filter)
    {
        Func<ShortcutEntry, object> selector = filter.SortBy switch
        {
            ShortcutSortField.Application => entry => entry.ApplicationName,
            ShortcutSortField.Action => entry => entry.ActionName,
            ShortcutSortField.Scope => entry => entry.Scope,
            ShortcutSortField.Confidence => entry => entry.Confidence,
            ShortcutSortField.Source => entry => entry.SourceKind,
            _ => entry => entry.Gesture.SortKey
        };

        var ordered = filter.SortDescending
            ? shortcuts.OrderByDescending(selector, Comparer<object>.Create(CompareValues))
            : shortcuts.OrderBy(selector, Comparer<object>.Create(CompareValues));
        return ordered
            .ThenBy(entry => entry.ApplicationName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.DisplayGesture, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.ActionName, StringComparer.CurrentCultureIgnoreCase);
    }

    private static int CompareValues(object? left, object? right) => (left, right) switch
    {
        (string leftText, string rightText) => StringComparer.CurrentCultureIgnoreCase.Compare(leftText, rightText),
        (Enum leftEnum, Enum rightEnum) => leftEnum.CompareTo(rightEnum),
        _ => Comparer<object>.Default.Compare(left, right)
    };
}
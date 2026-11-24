using System.Text.RegularExpressions;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Adapters;

/// <summary>
/// Parses a source-specific key string into one chord or, for applications
/// such as VS Code, a multi-chord sequence (Ctrl+K Ctrl+S).
/// </summary>
internal static class KeySequenceParser
{
    public static bool TryParse(
        string? raw,
        out ShortcutGesture? first,
        out string? sequence)
    {
        first = null;
        sequence = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var cleaned = raw.Trim().Trim('"').Replace('＋', '+').Replace('⌘', '+');
        var normalized = Regex.Replace(cleaned, @"\s*\+\s*", "+").Trim();
        var segments = Regex.Split(normalized, @"\s+")
            .Where(segment => segment.Length > 0)
            .ToArray();
        if (segments.Length == 0 || segments.Length > 8)
        {
            return false;
        }

        var gestures = new List<ShortcutGesture>(segments.Length);
        foreach (var segment in segments)
        {
            if (!ShortcutGesture.TryParse(segment, out var gesture))
            {
                return false;
            }

            gestures.Add(gesture!);
        }

        first = gestures[0];
        if (gestures.Count > 1)
        {
            sequence = string.Join(" ", gestures.Select(gesture => gesture.Canonical));
        }

        return true;
    }
}
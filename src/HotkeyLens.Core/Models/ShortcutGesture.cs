using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HotkeyLens.Core.Models;

/// <summary>
/// A normalized keyboard chord. Modifier order is normalized so strings
/// originating from UI Automation, JSON configurations and shortcut files
/// compare reliably.
/// </summary>
public sealed record ShortcutGesture
{
    public ShortcutModifiers Modifiers { get; init; }
    public string Key { get; init; } = string.Empty;
    public int VirtualKey { get; init; }

    [JsonIgnore]
    public bool HasModifier => Modifiers != ShortcutModifiers.None;

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Key);

    [JsonIgnore]
    public string Canonical => BuildDisplay(Modifiers, Key);

    [JsonIgnore]
    public string Display => Canonical;

    [JsonIgnore]
    public string SortKey => BuildSortKey(Modifiers, Key);

    public override string ToString() => Canonical;

    public static ShortcutGesture FromVirtualKey(
        int virtualKey,
        ShortcutModifiers modifiers = ShortcutModifiers.None)
    {
        var key = VirtualKeyCatalog.GetDisplayName(virtualKey);
        if (key is null)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualKey), virtualKey, "Unsupported virtual key.");
        }

        return new ShortcutGesture { Modifiers = modifiers, Key = key, VirtualKey = virtualKey };
    }

    public static bool TryParse(string? input, out ShortcutGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var cleaned = input.Trim();
        // UIA sometimes appends a descriptive suffix, for example "Ctrl+S (Save)".
        var suffixIndex = cleaned.IndexOf(" (", StringComparison.Ordinal);
        if (suffixIndex > 0)
        {
            cleaned = cleaned[..suffixIndex].TrimEnd();
        }

        if (cleaned.Contains(", ", StringComparison.Ordinal) &&
            !cleaned.Contains('+') &&
            !cleaned.Contains(' '))
        {
            cleaned = cleaned.Split(", ", StringSplitOptions.RemoveEmptyEntries)[0];
        }

        cleaned = cleaned.Replace('＋', '+').Replace('⌘', '+');
        var tokens = Tokenize(cleaned);
        if (tokens.Count == 0)
        {
            return false;
        }

        var modifiers = ShortcutModifiers.None;
        var keyTokens = new List<string>();
        foreach (var token in tokens)
        {
            if (TryParseModifier(token, out var modifier))
            {
                modifiers |= modifier;
            }
            else if (token.Length > 0)
            {
                keyTokens.Add(token);
            }
        }

        if (keyTokens.Count != 1)
        {
            return false;
        }

        var key = keyTokens[0];
        if (!VirtualKeyCatalog.TryGetVirtualKey(key, out var virtualKey, out var display))
        {
            return false;
        }

        gesture = new ShortcutGesture { Modifiers = modifiers, Key = display, VirtualKey = virtualKey };
        return true;
    }

    public static ShortcutGesture Parse(string input)
        => TryParse(input, out var gesture)
            ? gesture!
            : throw new FormatException($"'{input}' is not a supported keyboard shortcut.");

    private static List<string> Tokenize(string value)
    {
        var normalized = Regex.Replace(value, @"\s*\+\s*", "+").Trim();
        var pieces = normalized.Split('+', StringSplitOptions.None).ToList();

        // "Ctrl++" denotes the plus key. Keep it representable without losing the modifier.
        if (pieces.Count >= 2 && pieces[^1].Length == 0 && pieces[^2].Length == 0)
        {
            pieces.RemoveAt(pieces.Count - 1);
            pieces[^1] = "Plus";
        }

        if (pieces.Count == 1 && pieces[0].Contains(' '))
        {
            pieces = pieces[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        return pieces
            .Select(token => token.Trim())
            .Where(token => token.Length > 0 || pieces.Count == 1)
            .ToList();
    }

    private static bool TryParseModifier(string token, out ShortcutModifiers modifier)
    {
        switch (token.Trim().ToLowerInvariant())
        {
            case "ctrl":
            case "control":
            case "ctl":
                    modifier = ShortcutModifiers.Control;
                return true;
            case "alt":
            case "option":
            case "opt":
                modifier = ShortcutModifiers.Alt;
                return true;
            case "shift":
                modifier = ShortcutModifiers.Shift;
                return true;
            case "win":
            case "windows":
            case "meta":
            case "super":
            case "cmd":
            case "command":
                modifier = ShortcutModifiers.Win;
                return true;
            default:
                modifier = ShortcutModifiers.None;
                return false;
        }
    }

    private static string BuildDisplay(ShortcutModifiers modifiers, string key)
    {
        var builder = new StringBuilder();
        if (modifiers.HasFlag(ShortcutModifiers.Control)) builder.Append("Ctrl+");
        if (modifiers.HasFlag(ShortcutModifiers.Alt)) builder.Append("Alt+");
        if (modifiers.HasFlag(ShortcutModifiers.Shift)) builder.Append("Shift+");
        if (modifiers.HasFlag(ShortcutModifiers.Win)) builder.Append("Win+");
        builder.Append(key);
        return builder.ToString();
    }

    private static string BuildSortKey(ShortcutModifiers modifiers, string key)
    {
        var modifierRank = 0;
        if (modifiers.HasFlag(ShortcutModifiers.Control)) modifierRank |= 1;
        if (modifiers.HasFlag(ShortcutModifiers.Alt)) modifierRank |= 2;
        if (modifiers.HasFlag(ShortcutModifiers.Shift)) modifierRank |= 4;
        if (modifiers.HasFlag(ShortcutModifiers.Win)) modifierRank |= 8;
        return $"{modifierRank:D2}:{key}";
    }
}

internal static class VirtualKeyCatalog
{
    private static readonly Dictionary<string, (int VirtualKey, string Display)> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["backspace"] = (0x08, "Backspace"),
        ["bksp"] = (0x08, "Backspace"),
        ["tab"] = (0x09, "Tab"),
        ["clear"] = (0x0C, "Clear"),
        ["enter"] = (0x0D, "Enter"),
        ["return"] = (0x0D, "Enter"),
        ["shift"] = (0x10, "Shift"),
        ["ctrl"] = (0x11, "Ctrl"),
        ["control"] = (0x11, "Ctrl"),
        ["alt"] = (0x12, "Alt"),
        ["pause"] = (0x13, "Pause"),
        ["break"] = (0x13, "Pause"),
        ["capslock"] = (0x14, "CapsLock"),
        ["esc"] = (0x1B, "Esc"),
        ["escape"] = (0x1B, "Esc"),
        ["space"] = (0x20, "Space"),
        ["spacebar"] = (0x20, "Space"),
        ["pageup"] = (0x21, "PageUp"),
        ["pgup"] = (0x21, "PageUp"),
        ["pagedown"] = (0x22, "PageDown"),
        ["pgdn"] = (0x22, "PageDown"),
        ["end"] = (0x23, "End"),
        ["home"] = (0x24, "Home"),
        ["left"] = (0x25, "Left"),
        ["arrowleft"] = (0x25, "Left"),
        ["up"] = (0x26, "Up"),
        ["arrowup"] = (0x26, "Up"),
        ["right"] = (0x27, "Right"),
        ["arrowright"] = (0x27, "Right"),
        ["down"] = (0x28, "Down"),
        ["arrowdown"] = (0x28, "Down"),
        ["select"] = (0x29, "Select"),
        ["print"] = (0x2A, "Print"),
        ["execute"] = (0x2B, "Execute"),
        ["printscreen"] = (0x2C, "PrintScreen"),
        ["prtsc"] = (0x2C, "PrintScreen"),
        ["insert"] = (0x2D, "Insert"),
        ["ins"] = (0x2D, "Insert"),
        ["delete"] = (0x2E, "Delete"),
        ["del"] = (0x2E, "Delete"),
        ["help"] = (0x2F, "Help"),
        ["numlock"] = (0x90, "NumLock"),
        ["scrolllock"] = (0x91, "ScrollLock"),
        ["scroll"] = (0x91, "ScrollLock"),
        ["menu"] = (0x5D, "Menu"),
        ["apps"] = (0x5D, "Menu"),
        ["semicolon"] = (0xBA, ";"),
        ["plus"] = (0xBB, "+"),
        ["equals"] = (0xBB, "+"),
        ["comma"] = (0xBC, ","),
        ["minus"] = (0xBD, "-"),
        ["period"] = (0xBE, "."),
        ["dot"] = (0xBE, "."),
        ["slash"] = (0xBF, "/"),
        ["forwardslash"] = (0xBF, "/"),
        ["backtick"] = (0xC0, "`"),
        ["grave"] = (0xC0, "`"),
        ["backquote"] = (0xC0, "`"),
        ["leftbracket"] = (0xDB, "["),
        ["openbracket"] = (0xDB, "["),
        ["backslash"] = (0xDC, "\\"),
        ["rightbracket"] = (0xDD, "]"),
        ["closebracket"] = (0xDD, "]"),
        ["quote"] = (0xDE, "'"),
        ["apostrophe"] = (0xDE, "'"),
        ["oem102"] = (0xE2, "\\")
    };

    private static readonly Dictionary<int, string> DisplayByKey = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0C] = "Clear", [0x0D] = "Enter",
        [0x10] = "Shift", [0x11] = "Ctrl", [0x12] = "Alt", [0x13] = "Pause",
        [0x14] = "CapsLock", [0x1B] = "Esc", [0x20] = "Space", [0x21] = "PageUp",
        [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home", [0x25] = "Left",
        [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x29] = "Select",
        [0x2A] = "Print", [0x2B] = "Execute", [0x2C] = "PrintScreen", [0x2D] = "Insert",
        [0x2E] = "Delete", [0x2F] = "Help", [0x5D] = "Menu", [0x90] = "NumLock",
        [0x91] = "ScrollLock", [0xBA] = ";", [0xBB] = "+", [0xBC] = ",",
        [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`", [0xDB] = "[",
        [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'", [0xE2] = "\\"
    };

    static VirtualKeyCatalog()
    {
        for (var i = 0x30; i <= 0x39; i++)
        {
            var value = ((char)i).ToString();
            NamedKeys[value] = (i, value);
            DisplayByKey[i] = value;
        }

        for (var i = 0x41; i <= 0x5A; i++)
        {
            var value = ((char)i).ToString();
            NamedKeys[value] = (i, value);
            DisplayByKey[i] = value;
        }

        for (var i = 1; i <= 24; i++)
        {
            var value = $"F{i}";
            NamedKeys[value] = (0x6F + i, value);
            DisplayByKey[0x6F + i] = value;
        }

        foreach (var symbol in new[] { "+", "-", ",", ".", "/", "`", ";", "'", "[", "]", "\\" })
        {
            NamedKeys[symbol] = NamedKeys.First(pair => pair.Value.Display == symbol).Value;
        }
    }

    public static bool TryGetVirtualKey(string token, out int virtualKey, out string display)
    {
        if (NamedKeys.TryGetValue(token.Trim(), out var named))
        {
            virtualKey = named.VirtualKey;
            display = named.Display;
            return true;
        }

        if (token.Length == 1 && char.IsLetterOrDigit(token[0]))
        {
            var upper = char.ToUpperInvariant(token[0]);
            var code = upper <= 'Z' ? 0x41 + (upper - 'A') : 0x30 + (upper - '0');
            virtualKey = code;
            display = upper.ToString();
            return true;
        }

        virtualKey = 0;
        display = string.Empty;
        return false;
    }

    public static string? GetDisplayName(int virtualKey)
        => DisplayByKey.GetValueOrDefault(virtualKey);
}
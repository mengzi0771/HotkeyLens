namespace HotkeyLens.Core.Updates;

/// <summary>Stable semantic version used for release comparisons.</summary>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch)
    : IComparable<SemanticVersion>
{
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var metadata = value.IndexOf('+');
        if (metadata >= 0)
        {
            value = value[..metadata];
        }

        var prerelease = value.IndexOf('-');
        if (prerelease >= 0)
        {
            value = value[..prerelease];
        }

        var parts = value.Split('.');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch) ||
            major < 0 || minor < 0 || patch < 0)
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch);
        return true;
    }

    public static SemanticVersion Parse(string text)
        => TryParse(text, out var version) ? version : throw new FormatException($"无效版本号：{text}");

    public int CompareTo(SemanticVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        return result != 0 ? result : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
using System.Security.Cryptography;
using System.Text;

namespace HotkeyLens.Core.Models;

/// <summary>Creates deterministic, non-reversible identifiers for local records.</summary>
public static class StableId
{
    public static string Create(string prefix, params string?[] parts)
    {
        var material = string.Join('\u001f', parts.Select(part => (part ?? string.Empty).Trim().ToLowerInvariant()));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"{prefix}-{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim().Trim('"');
        try
        {
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant();
        }
        catch (Exception)
        {
            return trimmed.ToLowerInvariant();
        }
    }
}
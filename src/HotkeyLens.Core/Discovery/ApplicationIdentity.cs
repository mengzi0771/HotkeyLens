using System.Text.RegularExpressions;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Discovery;

/// <summary>
/// Resolves several read-only observations of one installation to a stable
/// application identity. It folds Chromium version folders and WindowsApps
/// package folders without starting or modifying the target application.
/// </summary>
public static class ApplicationIdentity
{
    private static readonly Regex VersionFolder = new(
        @"^\d+(?:\.\d+){1,3}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PublisherId = new(
        @"^[a-z0-9]{13}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] ChromiumExecutables = ["msedge.exe", "chrome.exe"];

    private static readonly string[] WindowsTerminalPackagePrefixes =
    [
        "Microsoft.WindowsTerminal_",
        "Microsoft.WindowsTerminalPreview_"
    ];

    /// <summary>
    /// Returns the executable path used for grouping and identification. The
    /// returned path preserves the on-disk casing; callers should normalize it
    /// with <see cref="StableId.NormalizePath"/> when building identifiers.
    /// </summary>
    public static string CanonicalizeExecutablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return StableId.NormalizePath(path);
        }

        var fileName = Path.GetFileName(full);
        var directory = Path.GetDirectoryName(full);
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(directory))
        {
            return full;
        }

        if (string.Equals(fileName, "wt.exe", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = ResolveWindowsTerminalExecutable(directory);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (!ChromiumExecutables.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return full;
        }

        var applicationDirectory = Path.GetDirectoryName(directory);
        if (applicationDirectory is null ||
            !string.Equals(Path.GetFileName(applicationDirectory), "Application", StringComparison.OrdinalIgnoreCase) ||
            !VersionFolder.IsMatch(Path.GetFileName(directory)))
        {
            return full;
        }

        var rootExecutable = Path.Combine(applicationDirectory, fileName);
        return File.Exists(rootExecutable) ? rootExecutable : full;
    }

    /// <summary>
    /// Extracts the package family name from a WindowsApps package path, for
    /// example Microsoft.WindowsTerminal_8wekyb3d8bbwe. Returns null for
    /// unpackaged applications and for paths outside WindowsApps.
    /// </summary>
    public static string? GetPackageFamilyName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(full);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var name = Path.GetFileName(directory);
            var parent = Path.GetDirectoryName(directory);
            if (string.Equals(Path.GetFileName(parent), "WindowsApps", StringComparison.OrdinalIgnoreCase))
            {
                return ParsePackageFamilyName(name);
            }

            directory = parent;
        }

        return null;
    }

    /// <summary>
    /// Returns the user-facing name for a package with a known family name.
    /// Unknown packages keep the discovered name.
    /// </summary>
    public static string? GetPackageDisplayName(string? packageFamilyName)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyName))
        {
            return null;
        }

        if (packageFamilyName.StartsWith("Microsoft.WindowsTerminalPreview_", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows Terminal Preview";
        }

        if (packageFamilyName.StartsWith("Microsoft.WindowsTerminal_", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows Terminal";
        }

        return null;
    }

    public static string? ParsePackageFamilyName(string? packageDirectoryName)
    {
        if (string.IsNullOrWhiteSpace(packageDirectoryName))
        {
            return null;
        }

        var segments = packageDirectoryName.Split('_');
        if (segments.Length < 2)
        {
            return null;
        }

        var publisherId = segments[^1];
        if (!PublisherId.IsMatch(publisherId))
        {
            return null;
        }

        return $"{segments[0]}_{publisherId}";
    }

    private static string? ResolveWindowsTerminalExecutable(string packageDirectory)
    {
        if (!WindowsTerminalPackagePrefixes.Any(prefix =>
                Path.GetFileName(packageDirectory).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var executable = Path.Combine(packageDirectory, "WindowsTerminal.exe");
        return File.Exists(executable) ? executable : null;
    }
}
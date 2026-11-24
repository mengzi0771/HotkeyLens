using System.Text.RegularExpressions;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Discovery;

/// <summary>
/// Merges observations of the same executable into one application record and
/// keeps deterministic IDs across scans.
/// </summary>
public static class ApplicationMerger
{
    private static readonly Regex ParentheticalSuffix = new(
        @"\s*\((?:x64|x86|64-bit|32-bit)\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<DiscoveredApplication> Merge(IEnumerable<ApplicationCandidate> candidates)
    {
        var result = new List<DiscoveredApplication>();
        var valid = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .GroupBy(CreateGroupingKey, StringComparer.Ordinal)
            .ToList();

        foreach (var group in valid)
        {
            var records = group
                .OrderByDescending(c => SourcePriority(c.SourceKind))
                .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var primary = records[0];
            var executable = records
                .Select(c => c.ExecutablePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!.Trim())
                .OrderByDescending(File.Exists)
                .FirstOrDefault();
            var icon = records
                .Select(c => c.IconPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!.Trim())
                .OrderByDescending(path => path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault() ?? executable;

            var name = records
                .Select(c => c.Name.Trim())
                .OrderByDescending(c => SourcePriority(records.First(r => r.Name.Trim() == c).SourceKind))
                .First();
            name = ParentheticalSuffix.Replace(name, string.Empty);

            var installLocation = records
                .Select(c => c.InstallLocation)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(installLocation) && !string.IsNullOrWhiteSpace(executable))
            {
                installLocation = Path.GetDirectoryName(executable);
            }

            var publisher = records.Select(c => c.Publisher).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            var version = records
                .OrderByDescending(c => c.SourceKind == ApplicationSourceKind.Registry)
                .Select(c => c.Version)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            var category = records.Select(c => c.Category).FirstOrDefault(c => c is not null)
                ?? ApplicationClassifier.Classify(name, executable);
            if (string.Equals(name, "Windows Shell", StringComparison.OrdinalIgnoreCase))
            {
                category = ApplicationCategory.System;
            }

            var sourceDetail = string.Join(
                " · ",
                records.Select(c => c.SourceDetail)
                    .Where(detail => !string.IsNullOrWhiteSpace(detail))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase));

            var settings = records.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.SettingsPath));
            var id = string.IsNullOrWhiteSpace(executable)
                ? StableId.Create("app", NormalizeName(name), publisher)
                : StableId.Create("app", StableId.NormalizePath(executable));

            result.Add(new DiscoveredApplication
            {
                Id = id,
                Name = name,
                ExecutablePath = executable,
                IconPath = icon,
                InstallLocation = installLocation,
                Version = version,
                Publisher = publisher,
                Category = category,
                SourceKind = primary.SourceKind,
                SourceDetail = sourceDetail,
                SettingsPath = settings?.SettingsPath,
                SettingsHint = settings?.SettingsHint,
                IsSystemComponent = records.Any(c => c.IsSystemComponent)
            });
        }

        return result
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string CreateGroupingKey(ApplicationCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ExecutablePath))
        {
            return "exe:" + StableId.NormalizePath(candidate.ExecutablePath);
        }

        return "name:" + NormalizeName(candidate.Name) + "|" + (candidate.Publisher ?? string.Empty).ToLowerInvariant();
    }

    private static string NormalizeName(string name)
    {
        var cleaned = ParentheticalSuffix.Replace(name.Trim(), string.Empty);
        return Regex.Replace(cleaned, @"\s+", " ").ToLowerInvariant();
    }

    private static int SourcePriority(ApplicationSourceKind kind) => kind switch
    {
        ApplicationSourceKind.WindowsBuiltIn => 5,
        ApplicationSourceKind.StartMenu => 4,
        ApplicationSourceKind.AppPath => 3,
        ApplicationSourceKind.Package => 2,
        ApplicationSourceKind.Registry => 1,
        _ => 0
    };
}
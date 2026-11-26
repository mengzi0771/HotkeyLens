using System.Text.Json;
using System.Text.Json.Serialization;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Catalog;

/// <summary>
/// The embedded catalog contains only curated, officially documented default
/// shortcuts. It is intentionally not a list of guesses for arbitrary software.
/// </summary>
public sealed class ShortcutCatalog
{
    public const string ResourceName = "HotkeyLens.Core.Data.official-shortcuts.json";

    private static readonly Lazy<ShortcutCatalog> DefaultCatalog = new(LoadEmbedded);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public required int SchemaVersion { get; init; }
    public required string CatalogUpdated { get; init; }
    public required IReadOnlyList<CatalogApplicationDefinition> Applications { get; init; }

    public static ShortcutCatalog Default => DefaultCatalog.Value;

    public static ShortcutCatalog LoadEmbedded()
    {
        using var stream = typeof(ShortcutCatalog).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Embedded catalog '{ResourceName}' is missing.");
        var catalog = JsonSerializer.Deserialize<ShortcutCatalog>(stream, JsonOptions)
                      ?? throw new InvalidDataException("Embedded shortcut catalog is empty.");
        return catalog;
    }

    public IEnumerable<CatalogApplicationDefinition> Match(DiscoveredApplication application)
    {
        var processName = application.ProcessName;
        if (string.IsNullOrWhiteSpace(processName) &&
            string.IsNullOrWhiteSpace(application.PackageFamilyName))
        {
            yield break;
        }

        foreach (var catalogApplication in Applications)
        {
            var matchesProcessName = !string.IsNullOrWhiteSpace(processName) &&
                                     catalogApplication.ProcessNames
                                         .Concat(catalogApplication.Aliases)
                                         .Any(candidate => string.Equals(candidate, processName, StringComparison.OrdinalIgnoreCase));
            var matchesPackage = !string.IsNullOrWhiteSpace(application.PackageFamilyName) &&
                                 catalogApplication.PackageFamilyNames.Any(candidate =>
                                     string.Equals(candidate, application.PackageFamilyName, StringComparison.OrdinalIgnoreCase));
            if (matchesProcessName || matchesPackage)
            {
                yield return catalogApplication;
            }
        }
    }
}

public sealed record CatalogApplicationDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Vendor { get; init; }
    public ApplicationCategory Category { get; init; } = ApplicationCategory.Other;
    public IReadOnlyList<string> ProcessNames { get; init; } = Array.Empty<string>();
    /// <summary>Additional process names accepted for this application, such as a launcher shim.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
    /// <summary>MSIX/AppX package family names accepted for this application.</summary>
    public IReadOnlyList<string> PackageFamilyNames { get; init; } = Array.Empty<string>();
    public required string SourceName { get; init; }
    public required string SourceReference { get; init; }
    public IReadOnlyList<string> Platforms { get; init; } = Array.Empty<string>();
    public string AppliesTo { get; init; } = string.Empty;
    public string LastReviewed { get; init; } = string.Empty;
    public string SettingsHint { get; init; } = string.Empty;

    /// <summary>
    /// Plain-language, menu-level instructions for changing or disabling this
    /// application's global hotkeys. Written for non-technical users.
    /// </summary>
    public string ResolutionGuide { get; init; } = string.Empty;

    public IReadOnlyList<CatalogShortcutDefinition> Shortcuts { get; init; } = Array.Empty<CatalogShortcutDefinition>();
}

public sealed record CatalogShortcutDefinition
{
    public required string Keys { get; init; }
    public string? Sequence { get; init; }
    public required string Action { get; init; }
    public string Description { get; init; } = string.Empty;
    public ShortcutScope Scope { get; init; } = ShortcutScope.Unknown;
    public string? Context { get; init; }
}
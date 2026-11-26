namespace HotkeyLens.Core.Models;

public sealed record ScanOptions
{
    public bool ScanShortcutFiles { get; init; } = true;
    public bool IncludeUserConfigurations { get; init; } = true;
    public bool IncludeUiAutomation { get; init; } = true;
    public bool IncludeDocumentedCatalog { get; init; } = true;
    public bool IncludeRegistryOnlyApplications { get; init; } = true;
    public bool IncludeSystemApplications { get; init; } = true;
    public int UiAutomationTimeoutSeconds { get; init; } = 20;
    public int UiAutomationMaxWindows { get; init; } = 20;

    public ScanOptions Sanitize() => this with
    {
        UiAutomationTimeoutSeconds = Math.Clamp(UiAutomationTimeoutSeconds, 5, 120),
        UiAutomationMaxWindows = Math.Clamp(UiAutomationMaxWindows, 1, 100),
        // The documented catalog is the only source that can attach OS-level
        // defaults without reading a local file. It is harmless to keep enabled.
        IncludeDocumentedCatalog = IncludeDocumentedCatalog
    };
}

public sealed record ScanProgress
{
    public required string Phase { get; init; }
    public required string Message { get; init; }
    public double Percent { get; init; }
    public int CompletedSteps { get; init; }
    public int TotalSteps { get; init; }
}

public sealed record ScanDiagnostic
{
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Warning;
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? ApplicationId { get; init; }
    public string? Detail { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}

public sealed record ApplicationScanResult
{
    public required string ApplicationId { get; init; }
    public required string ApplicationName { get; init; }
    public ScanState State { get; init; } = ScanState.NotRun;
    public int ShortcutCount { get; init; }
    public string Detail { get; init; } = string.Empty;
    public IReadOnlyList<string> CheckedSources { get; init; } = Array.Empty<string>();
}

public sealed record ScanStatistics
{
    public int TotalApplications { get; init; }
    public int ScannedApplications { get; init; }
    public int NoShortcutsApplications { get; init; }
    public int NotScannableApplications { get; init; }
    public int FailedApplications { get; init; }
    public int TotalShortcuts { get; init; }
    public int HighConfidenceShortcuts { get; init; }
    public int UserConfiguredShortcuts { get; init; }
    public int DocumentedDefaultShortcuts { get; init; }
    public int RuntimeObservedShortcuts { get; init; }
    /// <summary>Shortcuts that act while another application has focus.</summary>
    public int GlobalShortcuts { get; init; }
    /// <summary>Global shortcuts whose owning application was running at scan time.</summary>
    public int GlobalShortcutsFromRunningApplications { get; init; }
    public int RunningApplications { get; init; }
    /// <summary>Normal cross-application duplicates. These are not conflicts.</summary>
    public int DuplicateGroups { get; init; }
    public int NormalDuplicates { get; init; }
    public int PotentialConflicts { get; init; }
    public int ConfirmedConflicts { get; init; }
    public int GlobalCompetitions { get; init; }
    public int UnknownFindings { get; init; }
    public int NoConflictGroups { get; init; }
    public IReadOnlyDictionary<ShortcutScope, int> ByScope { get; init; } =
        new Dictionary<ShortcutScope, int>();
    public IReadOnlyDictionary<ShortcutSourceKind, int> BySource { get; init; } =
        new Dictionary<ShortcutSourceKind, int>();
}

public sealed record ScanSnapshot
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset ScannedAt { get; init; }
    public long DurationMilliseconds { get; init; }
    public ScanOptions Options { get; init; } = new();
    public IReadOnlyList<DiscoveredApplication> Applications { get; init; } = Array.Empty<DiscoveredApplication>();
    public IReadOnlyList<ShortcutEntry> Shortcuts { get; init; } = Array.Empty<ShortcutEntry>();
    public IReadOnlyList<ShortcutConflict> Conflicts { get; init; } = Array.Empty<ShortcutConflict>();
    public IReadOnlyList<ApplicationScanResult> ApplicationResults { get; init; } = Array.Empty<ApplicationScanResult>();
    public IReadOnlyList<ScanDiagnostic> Diagnostics { get; init; } = Array.Empty<ScanDiagnostic>();
    public ScanStatistics Statistics { get; init; } = new();
    public string EngineVersion { get; init; } = "1.2";

    public static ScanSnapshot Empty() => new()
    {
        ScannedAt = DateTimeOffset.MinValue,
        Statistics = new ScanStatistics()
    };
}

public sealed class AdapterScanResult
{
    public List<ShortcutEntry> Shortcuts { get; } = new();
    public HashSet<string> CoveredApplicationIds { get; } = new(StringComparer.Ordinal);
    public List<ScanDiagnostic> Diagnostics { get; } = new();

    public static AdapterScanResult Empty() => new();
}

public sealed class ScanContext
{
    public required IReadOnlyList<DiscoveredApplication> Applications { get; init; }
    public required ScanOptions Options { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public string DataDirectory { get; init; } = string.Empty;

    public DiscoveredApplication? FindByExecutable(string? executablePath)
    {
        var normalized = StableId.NormalizePath(executablePath);
        return string.IsNullOrEmpty(normalized)
            ? null
            : Applications.FirstOrDefault(app =>
                string.Equals(StableId.NormalizePath(app.ExecutablePath), normalized, StringComparison.Ordinal));
    }

    public DiscoveredApplication? FindByProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        return Applications.FirstOrDefault(app =>
            string.Equals(app.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
    }
}
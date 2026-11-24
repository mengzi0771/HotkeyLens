namespace HotkeyLens.Core.Models;

/// <summary>High-level grouping used by the application browser.</summary>
public enum ApplicationCategory
{
    System,
    Productivity,
    Development,
    Browser,
    Communication,
    Media,
    Graphics,
    Utility,
    Security,
    Other
}

/// <summary>Where the application record was discovered.</summary>
public enum ApplicationSourceKind
{
    WindowsBuiltIn,
    Registry,
    StartMenu,
    AppPath,
    Package,
    Unknown
}

public enum ShortcutScope
{
    Global,
    Application,
    Window,
    Contextual,
    Unknown
}

public enum ShortcutSourceKind
{
    ShortcutFile,
    UserConfiguration,
    UiAutomation,
    OfficialDocumentation,
    ApplicationInterface,
    Observed
}

public enum ConfidenceLevel
{
    Unknown,
    Low,
    Medium,
    High
}

/// <summary>
/// Describes whether a binding is an official default, an actual user
/// configuration, a runtime observation, or cannot be determined.
/// </summary>
public enum ShortcutOrigin
{
    Unknown,
    OfficialDefault,
    UserConfigured,
    RuntimeObserved
}

public enum ScanState
{
    NotRun,
    Scanned,
    NoShortcutsFound,
    NotScannable,
    Failed
}

public enum ConflictKind
{
    /// <summary>At least two applications have evidence of registering the same global hotkey.</summary>
    GlobalHotkeyCompetition,

    /// <summary>Different applications use the same chord only inside their own windows.</summary>
    DuplicateBinding,

    /// <summary>Overlap is possible, but the available evidence cannot prove interference.</summary>
    PotentialConflict
}

public enum ConflictSeverity
{
    Informational,
    Low,
    Medium,
    High
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8
}
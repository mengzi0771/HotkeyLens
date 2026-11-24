using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Adapters;

internal static class AdapterResultExtensions
{
    public static void AddUnique(this AdapterScanResult result, ShortcutEntry entry)
    {
        if (result.Shortcuts.All(existing => !string.Equals(existing.Id, entry.Id, StringComparison.Ordinal)))
        {
            result.Shortcuts.Add(entry);
        }
    }

    public static void AddDiagnostic(
        this AdapterScanResult result,
        DiagnosticSeverity severity,
        string source,
        string message,
        string? detail = null)
        => result.Diagnostics.Add(new ScanDiagnostic
        {
            Severity = severity,
            Source = source,
            Message = message,
            Detail = detail
        });
}
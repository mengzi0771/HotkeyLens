using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Catalog;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Adapters;

/// <summary>
/// Contributes vendor-documented defaults from the reviewed embedded catalog.
/// These entries are explicitly marked as documented defaults and are never
/// mixed with the user's actual configuration.
/// </summary>
public sealed class OfficialShortcutAdapter : IShortcutSourceAdapter
{
    private readonly ShortcutCatalog _catalog;

    public OfficialShortcutAdapter(ShortcutCatalog? catalog = null)
        => _catalog = catalog ?? ShortcutCatalog.Default;

    public string Id => "documented-defaults";
    public string DisplayName => "官方快捷键文档";
    public ShortcutSourceKind SourceKind => ShortcutSourceKind.OfficialDocumentation;

    public bool IsEnabled(ScanOptions options) => options.IncludeDocumentedCatalog;

    public Task<AdapterScanResult> ScanAsync(
        ScanContext context,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = AdapterScanResult.Empty();
        var matched = context.Applications
            .SelectMany(application => _catalog.Match(application)
                .Select(catalog => (Application: application, Catalog: catalog)))
            .ToList();

        var invalid = 0;
        var completed = 0;
        foreach (var (application, catalog) in matched)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.CoveredApplicationIds.Add(application.Id);

            foreach (var shortcut in catalog.Shortcuts)
            {
                if (!KeySequenceParser.TryParse(shortcut.Keys, out var gesture, out _))
                {
                    invalid++;
                    continue;
                }

                string? sequence = null;
                if (!string.IsNullOrWhiteSpace(shortcut.Sequence))
                {
                    if (!KeySequenceParser.TryParse(shortcut.Sequence, out _, out sequence))
                    {
                        invalid++;
                        continue;
                    }
                }

                var displayGesture = sequence ?? gesture!.Canonical;
                var action = shortcut.Action;
                var platforms = catalog.Platforms.Count > 0
                    ? string.Join("、", catalog.Platforms)
                    : "Windows 10、Windows 11";
                var reviewed = string.IsNullOrWhiteSpace(catalog.LastReviewed)
                    ? _catalog.CatalogUpdated
                    : catalog.LastReviewed;
                var appliesTo = string.IsNullOrWhiteSpace(catalog.AppliesTo)
                    ? $"{application.Name} 的受支持桌面版本"
                    : catalog.AppliesTo;
                var applicability = $"{appliesTo}；{platforms}；文档核对 {reviewed}";
                var entry = new ShortcutEntry
                {
                    Id = ShortcutEntry.BuildId(
                        application.Id,
                        displayGesture,
                        action,
                        ShortcutSourceKind.OfficialDocumentation,
                        catalog.SourceReference,
                        shortcut.Context),
                    ApplicationId = application.Id,
                    ApplicationName = application.Name,
                    Gesture = gesture!,
                    KeySequence = sequence,
                    ActionName = action,
                    Description = string.IsNullOrWhiteSpace(shortcut.Description)
                        ? $"厂商文档列出的 {application.Name} 默认快捷键。"
                        : shortcut.Description,
                    Scope = shortcut.Scope,
                    SourceKind = ShortcutSourceKind.OfficialDocumentation,
                    SourceName = catalog.SourceName,
                    SourceReference = catalog.SourceReference,
                    Applicability = applicability,
                    Confidence = ConfidenceLevel.Medium,
                    ConfidenceRationale =
                        $"来自厂商公开的默认键位文档；适用 {appliesTo}（{platforms}），文档核对日期 {reviewed}。" +
                        "文档可能随版本变化，也不代表用户在当前安装中实际修改过的绑定。",
                    Origin = ShortcutOrigin.OfficialDefault,
                    Context = shortcut.Context,
                    RawValue = shortcut.Sequence ?? shortcut.Keys,
                    CapturedAt = context.StartedAt
                };

                result.AddUnique(entry);
            }

            completed++;
            progress?.Report(new ScanProgress
            {
                Phase = "官方文档",
                Message = $"正在整理 {application.Name} 的文档默认键位",
                Percent = matched.Count == 0 ? 100 : completed * 100.0 / matched.Count,
                CompletedSteps = completed,
                TotalSteps = matched.Count
            });
        }

        if (invalid > 0)
        {
            result.AddDiagnostic(
                DiagnosticSeverity.Info,
                DisplayName,
                $"官方目录中有 {invalid} 条键位无法用当前手势模型表达，已跳过而不是猜测。");
        }

        return Task.FromResult(result);
    }
}
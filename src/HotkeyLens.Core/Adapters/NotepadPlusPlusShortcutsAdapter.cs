using System.Xml;
using System.Xml.Linq;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Adapters;

/// <summary>
/// Reads Notepad++'s local shortcuts.xml. The Key attribute is a decimal
/// Windows virtual-key code, so it can be converted without text guessing.
/// </summary>
public sealed class NotepadPlusPlusShortcutsAdapter : IShortcutSourceAdapter
{
    public string Id => "notepad-plus-plus-shortcuts";
    public string DisplayName => "Notepad++ 用户快捷键";
    public ShortcutSourceKind SourceKind => ShortcutSourceKind.UserConfiguration;

    public bool IsEnabled(ScanOptions options) => options.IncludeUserConfigurations;

    public Task<AdapterScanResult> ScanAsync(
        ScanContext context,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
        => Task.Run(() => Scan(context, progress, cancellationToken), cancellationToken);

    private AdapterScanResult Scan(
        ScanContext context,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = AdapterScanResult.Empty();
        var files = KnownConfigurationLocations.GetExistingConfigurationFiles()
            .Where(file => file.Format == ConfigurationFormat.NotepadPlusPlusShortcuts)
            .ToList();

        var completed = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completed++;
            var application = context.FindByProcessName(file.ProcessName);
            progress?.Report(new ScanProgress
            {
                Phase = "用户键位配置",
                Message = "正在读取 Notepad++ shortcuts.xml",
                Percent = completed * 100.0 / Math.Max(1, files.Count),
                CompletedSteps = completed,
                TotalSteps = files.Count
            });

            if (application is null)
            {
                continue;
            }

            result.CoveredApplicationIds.Add(application.Id);
            if (!TextFileReader.TryReadAllText(file.FilePath, out var text, out var readError))
            {
                result.AddDiagnostic(
                    DiagnosticSeverity.Warning,
                    DisplayName,
                    $"无法读取 {file.Description}。",
                    readError);
                continue;
            }

            try
            {
                using var stringReader = new StringReader(text);
                using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                });
                var document = XDocument.Load(xmlReader);
                var parsed = 0;
                var invalid = 0;

                foreach (var element in document.Descendants()
                             .Where(element =>
                                 element.Name.LocalName == "Shortcut" ||
                                 element.Name.LocalName == "Macro"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var keyAttribute = element.Attribute("Key")?.Value;
                    var name = element.Attribute("name")?.Value;
                    if (string.IsNullOrWhiteSpace(keyAttribute) ||
                        string.IsNullOrWhiteSpace(name) ||
                        !int.TryParse(keyAttribute, out var virtualKey) ||
                        virtualKey == 0)
                    {
                        continue;
                    }

                    var modifiers = ShortcutModifiers.None;
                    if (IsYes(element.Attribute("Ctrl")?.Value)) modifiers |= ShortcutModifiers.Control;
                    if (IsYes(element.Attribute("Alt")?.Value)) modifiers |= ShortcutModifiers.Alt;
                    if (IsYes(element.Attribute("Shift")?.Value)) modifiers |= ShortcutModifiers.Shift;
                    if (IsYes(element.Attribute("Win")?.Value)) modifiers |= ShortcutModifiers.Win;

                    ShortcutGesture gesture;
                    try
                    {
                        gesture = ShortcutGesture.FromVirtualKey(virtualKey, modifiers);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        invalid++;
                        continue;
                    }

                    result.AddUnique(new ShortcutEntry
                    {
                        Id = ShortcutEntry.BuildId(
                            application.Id,
                            gesture.Canonical,
                            name,
                            ShortcutSourceKind.UserConfiguration,
                            file.FilePath,
                            "Notepad++ 窗口"),
                        ApplicationId = application.Id,
                        ApplicationName = application.Name,
                        Gesture = gesture,
                        ActionName = name,
                        Description =
                            $"Notepad++ 本地 shortcuts.xml 为动作“{name}”记录的键位。",
                        Scope = ShortcutScope.Window,
                        SourceKind = ShortcutSourceKind.UserConfiguration,
                        SourceName = file.Description,
                        SourceReference = file.FilePath,
                        Confidence = ConfidenceLevel.High,
                        ConfidenceRationale =
                            "直接读取本地 shortcuts.xml 的 Key（十进制 Windows 虚拟键码）及 Ctrl/Alt/Shift 标志；文件可能记录当前生效的显式绑定。",
                        Origin = ShortcutOrigin.UserConfigured,
                        Context = "Notepad++ 窗口",
                        RawValue = $"Key={virtualKey}; Ctrl={IsYes(element.Attribute("Ctrl")?.Value)}",
                        CapturedAt = context.StartedAt
                    });
                    parsed++;
                }

                if (invalid > 0)
                {
                    result.AddDiagnostic(
                        DiagnosticSeverity.Info,
                        DisplayName,
                        $"Notepad++ 中有 {invalid} 个虚拟键码无法由当前手势模型表示，已跳过。");
                }

                if (parsed == 0)
                {
                    result.AddDiagnostic(
                        DiagnosticSeverity.Info,
                        DisplayName,
                        $"{file.Description} 中没有可读取的显式快捷键记录。");
                }
            }
            catch (XmlException exception)
            {
                result.AddDiagnostic(
                    DiagnosticSeverity.Warning,
                    DisplayName,
                    $"{file.Description} 不是有效的 XML。",
                    exception.Message);
            }
        }

        return result;
    }

    private static bool IsYes(string? value)
        => string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
}
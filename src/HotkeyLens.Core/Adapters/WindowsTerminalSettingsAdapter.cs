using System.Text.Json;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Adapters;

/// <summary>
/// Reads Windows Terminal's user settings.json, including both the modern
/// "actions" section and the legacy "keybindings" section.
/// </summary>
public sealed class WindowsTerminalSettingsAdapter : IShortcutSourceAdapter
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 64
    };

    private static readonly IReadOnlyDictionary<string, string> KnownActions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["copy"] = "复制",
            ["paste"] = "粘贴",
            ["find"] = "查找",
            ["newTab"] = "新建标签页",
            ["newWindow"] = "新建窗口",
            ["closePane"] = "关闭窗格",
            ["closeTab"] = "关闭标签页",
            ["closeWindow"] = "关闭窗口",
            ["duplicateTab"] = "复制标签页",
            ["renameTab"] = "重命名标签页",
            ["moveTab"] = "移动标签页",
            ["openSettings"] = "打开设置",
            ["commandPalette"] = "命令面板",
            ["toggleFocusMode"] = "切换焦点模式",
            ["toggleFullscreen"] = "切换全屏",
            ["splitPane"] = "拆分窗格",
            ["splitPaneDown"] = "向下拆分窗格",
            ["splitPaneRight"] = "向右拆分窗格",
            ["nextTab"] = "下一个标签页",
            ["prevTab"] = "上一个标签页",
            ["scrollUp"] = "向上滚动",
            ["scrollDown"] = "向下滚动",
            ["increaseFontSize"] = "增大字号",
            ["decreaseFontSize"] = "减小字号",
            ["resetFontSize"] = "重置字号"
        };

    public string Id => "windows-terminal-settings";
    public string DisplayName => "Windows Terminal 用户设置";
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
            .Where(file => file.Format == ConfigurationFormat.WindowsTerminal)
            .ToList();

        var completed = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var application = context.FindByProcessName(file.ProcessName);
            completed++;
            progress?.Report(new ScanProgress
            {
                Phase = "用户键位配置",
                Message = $"正在读取 {file.ProductName} settings.json",
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
                using var document = JsonDocument.Parse(text, JsonOptions);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    result.AddDiagnostic(
                        DiagnosticSeverity.Warning,
                        DisplayName,
                        $"{file.Description} 的根结构不是对象，已跳过。");
                    continue;
                }

                var invalid = 0;
                var unbound = 0;
                foreach (var section in new[] { "actions", "keybindings" })
                {
                    if (!document.RootElement.TryGetProperty(section, out var entries) ||
                        entries.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var item in entries.EnumerateArray())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (item.ValueKind != JsonValueKind.Object ||
                            !item.TryGetProperty("keys", out var keysElement))
                        {
                            continue;
                        }

                        var command = ReadCommand(item);
                        if (string.IsNullOrWhiteSpace(command))
                        {
                            continue;
                        }

                        if (command.Equals("unbound", StringComparison.OrdinalIgnoreCase))
                        {
                            unbound++;
                            continue;
                        }

                        var when = item.TryGetProperty("when", out var whenElement) &&
                                   whenElement.ValueKind == JsonValueKind.String
                            ? whenElement.GetString()
                            : null;
                        var scope = string.IsNullOrWhiteSpace(when)
                            ? ShortcutScope.Window
                            : ShortcutScope.Contextual;
                        var contextText = string.IsNullOrWhiteSpace(when)
                            ? "Windows Terminal 窗口"
                            : $"when: {when}";
                        var action = KnownActions.TryGetValue(command, out var known)
                            ? known
                            : $"Terminal 命令“{command}”";

                        foreach (var key in EnumerateKeys(keysElement))
                        {
                            if (!KeySequenceParser.TryParse(key, out var gesture, out var sequence))
                            {
                                invalid++;
                                continue;
                            }

                            var displayGesture = sequence ?? gesture!.Canonical;
                            result.AddUnique(new ShortcutEntry
                            {
                                Id = ShortcutEntry.BuildId(
                                    application.Id,
                                    displayGesture,
                                    action,
                                    ShortcutSourceKind.UserConfiguration,
                                    file.FilePath,
                                    contextText),
                                ApplicationId = application.Id,
                                ApplicationName = application.Name,
                                Gesture = gesture!,
                                KeySequence = sequence,
                                ActionName = action,
                                Description =
                                    $"用户在 {file.ProductName} 的 settings.json 中为动作“{command}”配置的键位。",
                                Scope = scope,
                                SourceKind = ShortcutSourceKind.UserConfiguration,
                                SourceName = file.Description,
                                SourceReference = file.FilePath,
                                Confidence = ConfidenceLevel.High,
                                ConfidenceRationale =
                                    "直接读取 Windows Terminal 用户 settings.json 的 keys 与 action；终端内部模式或 when 条件可能缩小实际生效范围。",
                                Origin = ShortcutOrigin.UserConfigured,
                                Context = contextText,
                                RawValue = key,
                                CapturedAt = context.StartedAt
                            });
                        }
                    }
                }

                if (invalid > 0)
                {
                    result.AddDiagnostic(
                        DiagnosticSeverity.Info,
                        DisplayName,
                        $"{file.ProductName} 中有 {invalid} 条键位无法解析，已跳过。");
                }

                if (unbound > 0)
                {
                    result.AddDiagnostic(
                        DiagnosticSeverity.Info,
                        DisplayName,
                        $"{file.ProductName} 中有 {unbound} 条 unbound 解除绑定设置。");
                }
            }
            catch (JsonException exception)
            {
                result.AddDiagnostic(
                    DiagnosticSeverity.Warning,
                    DisplayName,
                    $"{file.Description} 不是有效 JSON（允许注释和尾随逗号）。",
                    exception.Message);
            }
        }

        return result;
    }

    private static string? ReadCommand(JsonElement item)
    {
        if (!item.TryGetProperty("command", out var command))
        {
            return null;
        }

        if (command.ValueKind == JsonValueKind.String)
        {
            return command.GetString();
        }

        if (command.ValueKind == JsonValueKind.Object &&
            command.TryGetProperty("action", out var action) &&
            action.ValueKind == JsonValueKind.String)
        {
            return action.GetString();
        }

        return null;
    }

    private static IEnumerable<string> EnumerateKeys(JsonElement keys)
    {
        if (keys.ValueKind == JsonValueKind.String)
        {
            var single = keys.GetString();
            if (!string.IsNullOrWhiteSpace(single))
            {
                yield return single;
            }
        }
        else if (keys.ValueKind == JsonValueKind.Array)
        {
            foreach (var key in keys.EnumerateArray())
            {
                if (key.ValueKind == JsonValueKind.String)
                {
                    var value = key.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value;
                    }
                }
            }
        }
    }
}
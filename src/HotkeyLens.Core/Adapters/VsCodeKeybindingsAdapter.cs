using System.Text.Json;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Adapters;

/// <summary>
/// Reads the user's VS Code-compatible keybindings.json. The file is read-only:
/// HotkeyLens never rewrites another product's configuration.
/// </summary>
public sealed class VsCodeKeybindingsAdapter : IShortcutSourceAdapter
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 64
    };

    private static readonly IReadOnlyDictionary<string, string> KnownCommands =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["workbench.action.showCommands"] = "显示命令面板",
            ["workbench.action.quickOpen"] = "快速打开文件",
            ["workbench.action.files.newUntitledFile"] = "新建文件",
            ["workbench.action.files.save"] = "保存文件",
            ["workbench.action.files.saveAs"] = "另存为",
            ["workbench.action.files.saveAll"] = "保存所有文件",
            ["workbench.action.closeActiveEditor"] = "关闭当前编辑器",
            ["workbench.action.nextEditor"] = "下一个编辑器",
            ["workbench.action.previousEditor"] = "上一个编辑器",
            ["workbench.action.splitEditor"] = "拆分编辑器",
            ["workbench.action.findInFiles"] = "在文件中查找",
            ["workbench.action.replaceInFiles"] = "在文件中替换",
            ["workbench.action.toggleSidebarVisibility"] = "切换侧边栏",
            ["workbench.action.terminal.toggleTerminal"] = "切换集成终端",
            ["workbench.action.terminal.new"] = "新建终端",
            ["workbench.view.explorer"] = "显示资源管理器",
            ["workbench.view.extensions"] = "显示扩展视图",
            ["workbench.view.scm"] = "显示源代码管理",
            ["editor.action.commentLine"] = "切换行注释",
            ["editor.action.blockComment"] = "切换块注释",
            ["editor.action.formatDocument"] = "格式化文档",
            ["editor.action.revealDefinition"] = "转到定义",
            ["editor.action.rename"] = "重命名符号",
            ["editor.action.quickFix"] = "快速修复",
            ["editor.action.copyLinesDownAction"] = "向下复制当前行",
            ["editor.action.moveLinesUpAction"] = "向上移动当前行",
            ["editor.action.moveLinesDownAction"] = "向下移动当前行",
            ["editor.action.selectAll"] = "全选"
        };

    public string Id => "vscode-keybindings";
    public string DisplayName => "VS Code 用户键位";
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
            .Where(file => file.Format == ConfigurationFormat.VisualStudioCode)
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
                Message = $"正在读取 {file.ProductName} keybindings.json",
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
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    result.AddDiagnostic(
                        DiagnosticSeverity.Warning,
                        DisplayName,
                        $"{file.Description} 的根结构不是数组，已跳过。");
                    continue;
                }

                var invalid = 0;
                var removalDirectives = 0;
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.ValueKind != JsonValueKind.Object ||
                        !item.TryGetProperty("key", out var keyElement) ||
                        !item.TryGetProperty("command", out var commandElement) ||
                        keyElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var command = commandElement.ValueKind == JsonValueKind.String
                        ? commandElement.GetString()
                        : null;
                    var key = keyElement.GetString();
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        continue;
                    }

                    // "-command" removes a binding; it is not a shortcut itself.
                    if (command.StartsWith('-'))
                    {
                        removalDirectives++;
                        continue;
                    }

                    if (!KeySequenceParser.TryParse(key, out var gesture, out var sequence))
                    {
                        invalid++;
                        continue;
                    }

                    var when = item.TryGetProperty("when", out var whenElement) &&
                               whenElement.ValueKind == JsonValueKind.String
                        ? whenElement.GetString()
                        : null;
                    var scope = string.IsNullOrWhiteSpace(when)
                        ? ShortcutScope.Window
                        : ShortcutScope.Contextual;
                    var displayGesture = sequence ?? gesture!.Canonical;
                    var contextText = string.IsNullOrWhiteSpace(when)
                        ? "VS Code 窗口"
                        : $"when: {when}";
                    var action = KnownCommands.TryGetValue(command, out var known)
                        ? known
                        : command;

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
                            $"用户在 {file.ProductName} 的 keybindings.json 中为命令“{command}”配置了此键位。",
                        Scope = scope,
                        SourceKind = ShortcutSourceKind.UserConfiguration,
                        SourceName = file.Description,
                        SourceReference = file.FilePath,
                        Confidence = ConfidenceLevel.High,
                        ConfidenceRationale =
                            "直接读取用户 keybindings.json 的 key 与 command；扩展贡献、后置同键绑定及 when 条件仍可能影响实际触发。",
                        Origin = ShortcutOrigin.UserConfigured,
                        Context = contextText,
                        RawValue = key,
                        CapturedAt = context.StartedAt
                    });
                }

                if (invalid > 0)
                {
                    result.AddDiagnostic(
                        DiagnosticSeverity.Info,
                        DisplayName,
                        $"{file.ProductName} 中有 {invalid} 条键位无法解析，已跳过。");
                }

                if (removalDirectives > 0)
                {
                    result.AddDiagnostic(
                        DiagnosticSeverity.Info,
                        DisplayName,
                        $"{file.ProductName} 中有 {removalDirectives} 条“-command”移除指令，未当作快捷键。");
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
}
using System.Xml;
using System.Xml.Linq;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Adapters;

/// <summary>
/// Parses user-created JetBrains keymap XML. Only explicitly listed actions are
/// reported; inherited defaults from the IDE's parent keymap are not invented.
/// </summary>
public sealed class JetBrainsKeymapAdapter : IShortcutSourceAdapter
{
    private static readonly IReadOnlyDictionary<string, string> KnownActions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["$Copy"] = "复制",
            ["$Paste"] = "粘贴",
            ["$Cut"] = "剪切",
            ["$Delete"] = "删除",
            ["$SelectAll"] = "全选",
            ["$Undo"] = "撤销",
            ["$Redo"] = "重做",
            ["EditorDuplicate"] = "复制当前行",
            ["EditorDeleteLine"] = "删除当前行",
            ["EditorToggleCase"] = "切换大小写",
            ["CommentByLineComment"] = "切换行注释",
            ["CommentByBlockComment"] = "切换块注释",
            ["ReformatCode"] = "重新格式化代码",
            ["OptimizeImports"] = "优化导入",
            ["GotoDeclaration"] = "转到声明",
            ["GotoImplementation"] = "转到实现",
            ["Find"] = "查找",
            ["FindInPath"] = "在路径中查找",
            ["Replace"] = "替换",
            ["ReplaceInPath"] = "在路径中替换",
            ["ShowIntentionActions"] = "显示上下文操作",
            ["ActivateTerminalToolWindow"] = "打开终端工具窗口",
            ["ActivateProjectToolWindow"] = "打开项目工具窗口",
            ["SearchEverywhere"] = "搜索所有位置",
            ["GotoFile"] = "转到文件",
            ["GotoSymbol"] = "转到符号",
            ["GotoLine"] = "转到行",
            ["RecentFiles"] = "最近打开的文件",
            ["Back"] = "后退",
            ["Forward"] = "前进",
            ["CloseContent"] = "关闭当前标签页",
            ["NextTab"] = "下一个标签页",
            ["PreviousTab"] = "上一个标签页",
            ["SplitVertically"] = "垂直拆分编辑器",
            ["SplitHorizontally"] = "水平拆分编辑器",
            ["Run"] = "运行",
            ["Debug"] = "调试",
            ["StepOver"] = "单步跳过",
            ["StepInto"] = "单步进入",
            ["Resume"] = "恢复运行",
            ["Stop"] = "停止运行"
        };

    public string Id => "jetbrains-keymap";
    public string DisplayName => "JetBrains 用户 keymap";
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
            .Where(file => file.Format == ConfigurationFormat.JetBrainsKeymap)
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
                Message = $"正在读取 {file.ProductName} keymap XML",
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
                var invalid = 0;
                var parsed = 0;

                foreach (var actionElement in document.Descendants()
                             .Where(element => element.Name.LocalName == "action"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var actionId = actionElement.Attribute("id")?.Value;
                    if (string.IsNullOrWhiteSpace(actionId))
                    {
                        continue;
                    }

                    var action = DescribeAction(actionId);
                    foreach (var shortcutElement in actionElement.Elements()
                                 .Where(element => element.Name.LocalName == "keyboard-shortcut"))
                    {
                        var firstStroke = shortcutElement.Attribute("first-keystroke")?.Value;
                        var secondStroke = shortcutElement.Attribute("second-keystroke")?.Value;
                        if (string.IsNullOrWhiteSpace(firstStroke))
                        {
                            continue;
                        }

                        if (!TryConvertStroke(firstStroke, out var gesture, out var firstDisplay))
                        {
                            invalid++;
                            continue;
                        }

                        string? sequence = null;
                        if (!string.IsNullOrWhiteSpace(secondStroke))
                        {
                            if (!TryConvertStroke(secondStroke, out _, out var secondDisplay))
                            {
                                invalid++;
                                continue;
                            }

                            sequence = $"{firstDisplay} {secondDisplay}";
                        }

                        var displayGesture = sequence ?? gesture!.Canonical;
                        var actionText = $"JetBrains 动作：{action}";
                        result.AddUnique(new ShortcutEntry
                        {
                            Id = ShortcutEntry.BuildId(
                                application.Id,
                                displayGesture,
                                actionId,
                                ShortcutSourceKind.UserConfiguration,
                                file.FilePath,
                                file.ProcessName),
                            ApplicationId = application.Id,
                            ApplicationName = application.Name,
                            Gesture = gesture!,
                            KeySequence = sequence,
                            ActionName = actionText,
                            Description =
                                $"用户在 {file.ProductName} 的自定义 keymap 中为动作“{actionId}”指定的键位。",
                            Scope = ShortcutScope.Window,
                            SourceKind = ShortcutSourceKind.UserConfiguration,
                            SourceName = file.Description,
                            SourceReference = file.FilePath,
                            Confidence = ConfidenceLevel.High,
                            ConfidenceRationale =
                                "直接读取用户 keymap 中显式列出的 keyboard-shortcut；未列出的动作沿用 IDE 父 keymap 默认值，本工具不会虚构这些默认值。",
                            Origin = ShortcutOrigin.UserConfigured,
                            Context = $"{file.ProductName} 自定义 keymap",
                            RawValue = secondStroke is null
                                ? firstStroke
                                : $"{firstStroke} {secondStroke}",
                            CapturedAt = context.StartedAt
                        });
                        parsed++;
                    }
                }

                if (invalid > 0)
                {
                    result.AddDiagnostic(
                        DiagnosticSeverity.Info,
                        DisplayName,
                        $"{file.ProductName} 中有 {invalid} 个 JetBrains 键位无法转换，已跳过。");
                }

                if (parsed == 0)
                {
                    result.AddDiagnostic(
                        DiagnosticSeverity.Info,
                        DisplayName,
                        $"{file.Description} 中没有显式覆盖的动作；未把 IDE 默认 keymap 当作已读取。");
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

    internal static bool TryConvertStroke(
        string stroke,
        out ShortcutGesture? gesture,
        out string display)
    {
        gesture = null;
        display = string.Empty;
        var modifiers = new List<string>();
        string? key = null;
        foreach (var rawToken in stroke.Split(
                     [' ', '\t', '+'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            switch (token.ToLowerInvariant())
            {
                case "control":
                case "ctrl":
                    modifiers.Add("Ctrl");
                    continue;
                case "alt":
                    modifiers.Add("Alt");
                    continue;
                case "shift":
                    modifiers.Add("Shift");
                    continue;
                case "meta":
                case "win":
                    modifiers.Add("Win");
                    continue;
            }

            if (key is not null)
            {
                // More than one non-modifier token is ambiguous; skip instead of guessing.
                return false;
            }

            key = MapKey(token);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var candidate = string.Join("+", modifiers.Append(key));
        if (!ShortcutGesture.TryParse(candidate, out gesture))
        {
            return false;
        }

        display = gesture!.Canonical;
        return true;
    }

    private static string? MapKey(string token)
    {
        var upper = token.ToUpperInvariant();
        if (upper.Length == 1 && char.IsLetterOrDigit(upper[0]))
        {
            return upper;
        }

        if (upper.StartsWith('F') && int.TryParse(upper[1..], out var functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            return upper;
        }

        return upper switch
        {
            "PERIOD" => ".",
            "COMMA" => ",",
            "SLASH" => "/",
            "BACK_SLASH" => "Backslash",
            "SEMICOLON" => ";",
            "OPEN_BRACKET" => "[",
            "CLOSE_BRACKET" => "]",
            "MINUS" => "Minus",
            "EQUALS" => "Equals",
            "BACK_QUOTE" => "Backtick",
            "QUOTE" => "'",
            "SPACE" => "Space",
            "ESCAPE" => "Esc",
            "ENTER" => "Enter",
            "TAB" => "Tab",
            "BACK_SPACE" => "Backspace",
            "PAGE_UP" => "PageUp",
            "PAGE_DOWN" => "PageDown",
            "HOME" => "Home",
            "END" => "End",
            "INSERT" => "Insert",
            "DELETE" => "Delete",
            "UP" => "Up",
            "DOWN" => "Down",
            "LEFT" => "Left",
            "RIGHT" => "Right",
            "ARROW_UP" => "Up",
            "ARROW_DOWN" => "Down",
            "ARROW_LEFT" => "Left",
            "ARROW_RIGHT" => "Right",
            "CONTEXT_MENU" => "Menu",
            "CAPS_LOCK" => "CapsLock",
            _ => null
        };
    }

    private static string DescribeAction(string actionId)
    {
        if (KnownActions.TryGetValue(actionId, out var known))
        {
            return known;
        }

        var bare = actionId.TrimStart('$');
        if (bare.Length == 0)
        {
            return actionId;
        }

        var builder = new System.Text.StringBuilder(bare.Length + 8);
        for (var index = 0; index < bare.Length; index++)
        {
            var character = bare[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(bare[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
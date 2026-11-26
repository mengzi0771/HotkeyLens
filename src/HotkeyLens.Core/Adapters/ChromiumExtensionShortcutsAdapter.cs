using System.Text.Json;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Adapters;

/// <summary>
/// Reads the extension shortcut assignments that Chrome and Edge persist in
/// their profile Preferences file. The file is opened read-only and never
/// written back; a profile that is currently being rewritten simply degrades to
/// a diagnostic instead of a guess.
/// </summary>
public sealed class ChromiumExtensionShortcutsAdapter : IShortcutSourceAdapter
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 128
    };

    private const long MaximumManifestBytes = 1024 * 1024;

    private readonly Func<IReadOnlyList<ConfigurationFileDescriptor>> _locate;

    public ChromiumExtensionShortcutsAdapter(
        Func<IReadOnlyList<ConfigurationFileDescriptor>>? locatePreferences = null)
    {
        _locate = locatePreferences ?? KnownConfigurationLocations.GetChromiumPreferenceFiles;
    }

    public string Id => "chromium-extension-shortcuts";
    public string DisplayName => "浏览器扩展快捷键";
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
        var files = _locate();
        var completed = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completed++;
            progress?.Report(new ScanProgress
            {
                Phase = "浏览器扩展快捷键",
                Message = $"正在读取 {file.Description}",
                Percent = completed * 100.0 / Math.Max(1, files.Count),
                CompletedSteps = completed,
                TotalSteps = files.Count
            });

            var application = context.FindByProcessName(file.ProcessName);
            if (application is null)
            {
                continue;
            }

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
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("extensions", out var extensions) ||
                    extensions.ValueKind != JsonValueKind.Object ||
                    !extensions.TryGetProperty("commands", out var commands) ||
                    commands.ValueKind != JsonValueKind.Object)
                {
                    result.CoveredApplicationIds.Add(application.Id);
                    result.AddDiagnostic(
                        DiagnosticSeverity.Info,
                        DisplayName,
                        $"{file.Description} 中没有 extensions.commands 段，可能尚未为任何扩展设置快捷键。");
                    continue;
                }

                var profileDirectory = Path.GetDirectoryName(file.FilePath);
                var parsedCommands = EnumerateCommands(commands, cancellationToken).ToList();
                var extensionNames = ReadExtensionNames(extensions);
                if (!string.IsNullOrWhiteSpace(profileDirectory))
                {
                    foreach (var extensionId in parsedCommands
                                 .Select(command => command.ExtensionId)
                                 .Where(id => !string.IsNullOrWhiteSpace(id) && !extensionNames.ContainsKey(id!))
                                 .Distinct(StringComparer.Ordinal))
                    {
                        var discovered = TryReadManifestName(profileDirectory, extensionId!);
                        if (!string.IsNullOrWhiteSpace(discovered))
                        {
                            extensionNames[extensionId!] = discovered;
                        }
                    }
                }

                var profileName = string.IsNullOrWhiteSpace(profileDirectory)
                    ? "配置文件"
                    : Path.GetFileName(profileDirectory);

                var imported = 0;
                var invalid = 0;
                foreach (var command in parsedCommands)
                {
                    if (string.IsNullOrWhiteSpace(command.Chord) ||
                        !KeySequenceParser.TryParse(command.Chord, out var gesture, out _))
                    {
                        invalid++;
                        continue;
                    }

                    var extensionName = command.ExtensionId is null
                        ? null
                        : extensionNames.GetValueOrDefault(command.ExtensionId);
                    var action = BuildActionName(extensionName, command.CommandName, command.ExtensionId);                    var scope = command.Global ? ShortcutScope.Global : ShortcutScope.Application;
                    var contextText = $"{file.ProductName} · {profileName}（{(command.Global ? "全局" : "浏览器内")}）";

                    result.AddUnique(new ShortcutEntry
                    {
                        Id = ShortcutEntry.BuildId(
                            application.Id,
                            gesture!.Canonical,
                            action,
                            ShortcutSourceKind.UserConfiguration,
                            file.FilePath,
                            contextText),
                        ApplicationId = application.Id,
                        ApplicationName = application.Name,
                        Gesture = gesture!,
                        ActionName = action,
                        Description =
                            $"浏览器扩展的快捷键由用户在 {file.ProductName} 中设置，当前作用域为{(command.Global ? "全局（其他窗口也可能响应）" : "浏览器窗口内")}。",
                        Scope = scope,
                        SourceKind = ShortcutSourceKind.UserConfiguration,
                        SourceName = $"{file.ProductName} 扩展快捷键（用户配置）",
                        SourceReference = file.FilePath,
                        Applicability = $"Preferences 中的 extensions.commands；读取于 {context.StartedAt:yyyy-MM-dd HH:mm}",
                        Confidence = ConfidenceLevel.High,
                        ConfidenceRationale =
                            "直接读取浏览器 Preferences 中该配置文件的 extensions.commands 键位；扩展被停用或卸载后记录可能仍残留，实际触发还取决于扩展是否启用。",
                        Origin = ShortcutOrigin.UserConfigured,
                        Context = contextText,
                        RawValue = command.Chord,
                        ResolutionGuide =
                            $"打开 {(file.ProductName.Contains("Edge", StringComparison.OrdinalIgnoreCase) ? "edge://extensions/shortcuts" : "chrome://extensions/shortcuts")}，在对应扩展的快捷键输入框中重新按键或点「清除」。",
                        CapturedAt = context.StartedAt
                    });
                    imported++;
                }

                result.CoveredApplicationIds.Add(application.Id);
                if (imported == 0)
                {
                    result.AddDiagnostic(
                        DiagnosticSeverity.Info,
                        DisplayName,
                        $"{file.Description} 中没有可用的扩展快捷键。");
                }

                if (invalid > 0)
                {
                    result.AddDiagnostic(
                        DiagnosticSeverity.Info,
                        DisplayName,
                        $"{file.Description} 中有 {invalid} 条扩展快捷键无法解析，已跳过而不是猜测。");
                }
            }
            catch (JsonException exception)
            {
                result.AddDiagnostic(
                    DiagnosticSeverity.Warning,
                    DisplayName,
                    $"{file.Description} 目前不是有效的 JSON（浏览器可能正在写入），已跳过该配置文件。",
                    exception.Message);
            }
        }

        if (files.Count == 0)
        {
            result.AddDiagnostic(
                DiagnosticSeverity.Info,
                DisplayName,
                "没有找到 Chrome/Edge 的用户 Preferences，扩展快捷键未扫描。");
        }

        return result;
    }

    /// <summary>
    /// Supports both the classic nested shape
    /// <c>commands[extensionId][commandName] = { keystroke, global }</c> and the
    /// newer flat shape <c>commands["windows:Alt+A"] = { command_name, extension, global }</c>.
    /// </summary>
    private static IEnumerable<ExtensionCommand> EnumerateCommands(
        JsonElement commands,
        CancellationToken cancellationToken)
    {
        foreach (var property in commands.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var value = property.Value;
            if (value.TryGetProperty("keystroke", out var keystroke) &&
                keystroke.ValueKind == JsonValueKind.String)
            {
                yield return new ExtensionCommand(
                    keystroke.GetString(),
                    ReadString(value, "command_name") ?? property.Name,
                    ReadString(value, "extension"),
                    ReadBoolean(value, "global"));
                continue;
            }

            if (value.TryGetProperty("command_name", out var commandName) &&
                commandName.ValueKind == JsonValueKind.String)
            {
                yield return new ExtensionCommand(
                    ChordFromPropertyName(property.Name),
                    commandName.GetString(),
                    ReadString(value, "extension"),
                    ReadBoolean(value, "global"));
                continue;
            }

            // Classic shape: the property name is the extension id and each
            // nested property is one command.
            foreach (var nested in value.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (nested.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var chord = ReadString(nested.Value, "keystroke");
                if (string.IsNullOrWhiteSpace(chord))
                {
                    continue;
                }

                yield return new ExtensionCommand(
                    chord,
                    ReadString(nested.Value, "command_name") ?? nested.Name,
                    ReadString(nested.Value, "extension") ?? property.Name,
                    ReadBoolean(nested.Value, "global"));
            }
        }
    }

    private static string? ChordFromPropertyName(string name)
    {
        var separators = new[] { ':', '/' };
        var index = name.IndexOfAny(separators);
        return index >= 0 && index < name.Length - 1 ? name[(index + 1)..] : name;
    }

    private static Dictionary<string, string> ReadExtensionNames(JsonElement extensions)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (extensions.TryGetProperty("settings", out var settings) &&
            settings.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in settings.EnumerateObject())
            {
                var name = ReadManifestName(item.Value);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names[item.Name] = name;
                }
            }
        }

        return names;
    }

    private static string? ReadManifestName(JsonElement settingsEntry)
    {
        if (settingsEntry.ValueKind == JsonValueKind.Object &&
            settingsEntry.TryGetProperty("manifest", out var manifest) &&
            manifest.ValueKind == JsonValueKind.Object)
        {
            var name = ReadString(manifest, "name");
            return IsLocalizedPlaceholder(name) ? null : name;
        }

        return null;
    }

    private static string? TryReadManifestName(string profileDirectory, string extensionId)
    {
        try
        {
            var root = Path.Combine(profileDirectory, "Extensions", extensionId);
            if (!Directory.Exists(root))
            {
                return null;
            }

            foreach (var versionDirectory in Directory.EnumerateDirectories(root)
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var manifestPath = Path.Combine(versionDirectory, "manifest.json");
                if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > MaximumManifestBytes)
                {
                    continue;
                }

                using var stream = new FileStream(
                    manifestPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var document = JsonDocument.Parse(stream, JsonOptions);
                var name = ReadString(document.RootElement, "name");
                if (!string.IsNullOrWhiteSpace(name) && !IsLocalizedPlaceholder(name))
                {
                    return name;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool IsLocalizedPlaceholder(string? name)
        => string.IsNullOrWhiteSpace(name) ||
           (name.StartsWith("__MSG_", StringComparison.Ordinal) && name.EndsWith("__", StringComparison.Ordinal));

    private static string BuildActionName(string? extensionName, string? commandName, string? extensionId)
    {
        var commandLabel = string.IsNullOrWhiteSpace(commandName)
            ? "扩展命令"
            : commandName;
        if (!string.IsNullOrWhiteSpace(extensionName))
        {
            return $"{extensionName} · {commandLabel}";
        }

        var shortId = extensionId is { Length: > 8 } ? extensionId[..8] : extensionId;
        return string.IsNullOrWhiteSpace(shortId)
            ? $"扩展命令 · {commandLabel}"
            : $"扩展 {shortId} · {commandLabel}";
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(propertyName, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(propertyName, out var value) &&
           value.ValueKind is JsonValueKind.True;

    private sealed record ExtensionCommand(
        string? Chord,
        string? CommandName,
        string? ExtensionId,
        bool Global);
}

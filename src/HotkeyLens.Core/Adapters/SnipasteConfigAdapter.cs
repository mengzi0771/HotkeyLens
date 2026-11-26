using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Adapters;

/// <summary>
/// Reads Snipaste's user config.ini (INI format) in a read-only share mode.
/// Only keys that clearly describe a hotkey are interpreted; anything else is
/// ignored, and an unreadable or missing file falls back to the catalog's
/// documented defaults instead of being guessed.
/// </summary>
public sealed class SnipasteConfigAdapter : IShortcutSourceAdapter
{
    private static readonly IReadOnlyDictionary<string, string> KnownActions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["snip"] = "截图",
            ["screenshot"] = "截图",
            ["capture"] = "截图",
            ["paste"] = "贴图",
            ["pin"] = "贴图",
            ["hide"] = "隐藏贴图",
            ["show"] = "显示贴图",
            ["close"] = "关闭贴图",
            ["copy"] = "复制图片",
            ["save"] = "保存截图",
            ["ocr"] = "文字识别",
            ["colorpicker"] = "取色",
            ["color"] = "取色"
        };

    private readonly Func<IReadOnlyList<ConfigurationFileDescriptor>> _locate;

    public SnipasteConfigAdapter(
        Func<IReadOnlyList<ConfigurationFileDescriptor>>? locateConfig = null)
    {
        _locate = locateConfig ?? KnownConfigurationLocations.GetSnipasteConfigurationFiles;
    }

    public string Id => "snipaste-config";
    public string DisplayName => "Snipaste 用户配置";
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
        var application = context.FindByProcessName("Snipaste.exe");
        if (application is null)
        {
            return result;
        }

        var files = _locate();
        if (files.Count == 0)
        {
            result.CoveredApplicationIds.Add(application.Id);
            result.AddDiagnostic(
                DiagnosticSeverity.Info,
                DisplayName,
                "没有找到 Snipaste 的 config.ini，沿用官方默认热键规则。");
            return result;
        }

        result.CoveredApplicationIds.Add(application.Id);
        var file = files[0];
        progress?.Report(new ScanProgress
        {
            Phase = "Snipaste 配置",
            Message = "正在只读解析 Snipaste config.ini",
            Percent = 100
        });

        if (!TextFileReader.TryReadAllText(file.FilePath, out var text, out var readError))
        {
            result.AddDiagnostic(
                DiagnosticSeverity.Warning,
                DisplayName,
                $"无法读取 {file.Description}，已回退到官方默认规则。",
                readError);
            return result;
        }

        var candidateCount = 0;
        var invalid = 0;
        foreach (var entry in ParseIni(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!LooksLikeHotkey(entry.Section, entry.Key))
            {
                continue;
            }

            candidateCount++;
            if (!KeySequenceParser.TryParse(entry.Value, out var gesture, out _))
            {
                invalid++;
                continue;
            }

            var action = KnownActions.GetValueOrDefault(entry.Key) ?? $"自定义热键（{entry.Key}）";
            var contextText = $"Snipaste · [{entry.Section}] {entry.Key}";
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
                Description = $"用户在 Snipaste 的 config.ini 中为「{entry.Key}」设置了此热键。",
                Scope = ShortcutScope.Global,
                SourceKind = ShortcutSourceKind.UserConfiguration,
                SourceName = file.Description,
                SourceReference = file.FilePath,
                Applicability = $"config.ini [{entry.Section}]；读取于 {context.StartedAt:yyyy-MM-dd HH:mm}",
                Confidence = ConfidenceLevel.Medium,
                ConfidenceRationale =
                    "直接读取 Snipaste config.ini 的键值；该文件不记录作用域字段，Snipaste 的热键默认在全局生效，因此按全局判定。若你在 Snipaste 内取消了全局启用，以应用内实际状态为准。",
                Origin = ShortcutOrigin.UserConfigured,
                Context = contextText,
                RawValue = entry.Value,
                ResolutionGuide = string.IsNullOrWhiteSpace(application.ResolutionGuide)
                    ? "系统托盘右键 Snipaste 图标 →「首选项」→「控制」→ 修改或清空对应全局快捷键。"
                    : application.ResolutionGuide,
                CapturedAt = context.StartedAt
            });
        }

        if (candidateCount == 0)
        {
            result.AddDiagnostic(
                DiagnosticSeverity.Info,
                DisplayName,
                "config.ini 中没有发现热键段，沿用官方默认规则。");
        }
        else if (invalid > 0)
        {
            result.AddDiagnostic(
                DiagnosticSeverity.Info,
                DisplayName,
                $"config.ini 中有 {invalid} 条热键值无法解析，已跳过；这些条目不会被猜测。");
        }

        return result;
    }

    private static bool LooksLikeHotkey(string section, string key)
    {
        var normalizedSection = section.ToLowerInvariant();
        var normalizedKey = key.ToLowerInvariant();
        return normalizedSection.Contains("hotkey", StringComparison.Ordinal) ||
               normalizedSection.Contains("shortcut", StringComparison.Ordinal) ||
               normalizedSection.Contains("快捷键", StringComparison.Ordinal) ||
               normalizedKey.Contains("hotkey", StringComparison.Ordinal) ||
               normalizedKey.StartsWith("key_", StringComparison.Ordinal);
    }

    internal static IReadOnlyList<IniEntry> ParseIni(string text)
    {
        var entries = new List<IniEntry>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return entries;
        }

        var section = string.Empty;
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().Trim('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (key.Length == 0)
            {
                continue;
            }

            entries.Add(new IniEntry(section, key, value));
        }

        return entries;
    }

    internal sealed record IniEntry(string Section, string Key, string Value);
}

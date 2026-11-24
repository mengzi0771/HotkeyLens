using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Adapters;

/// <summary>
/// Reads the HotKey field of Windows .lnk files. A shortcut hotkey is a real
/// user-configured global registration request, not a guess derived from text.
/// </summary>
public sealed class ShortcutFileAdapter : IShortcutSourceAdapter
{
    public string Id => "shortcut-files";
    public string DisplayName => "快捷方式热键";
    public ShortcutSourceKind SourceKind => ShortcutSourceKind.ShortcutFile;

    public bool IsEnabled(ScanOptions options) => options.ScanShortcutFiles;

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
        var roots = GetRoots();
        var files = ShortcutLinkReader.EnumerateShortcutFiles(roots.ToArray());
        var hotkeyFiles = 0;
        var unresolved = 0;
        var duplicate = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        using var resolver = new WScriptShortcutResolver();
        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[index];
            progress?.Report(new ScanProgress
            {
                Phase = "快捷方式热键",
                Message = "正在读取开始菜单快捷方式",
                Percent = index * 100.0 / Math.Max(1, files.Count),
                CompletedSteps = index,
                TotalSteps = files.Count
            });

            if (!ShortcutLinkReader.TryReadHotkey(path, out var gesture))
            {
                continue;
            }

            hotkeyFiles++;
            if (!resolver.TryResolve(path, out var metadata) || string.IsNullOrWhiteSpace(metadata.TargetPath))
            {
                unresolved++;
                continue;
            }

            var application = context.FindByExecutable(metadata.TargetPath);
            if (application is null)
            {
                // The target may be a helper, an uninstaller or a launcher that
                // was intentionally filtered from the application list.
                unresolved++;
                continue;
            }

            if (!seen.Add(application.Id + "\u001f" + gesture!.Canonical))
            {
                duplicate++;
                continue;
            }

            var linkName = Path.GetFileNameWithoutExtension(path);
            var action = string.IsNullOrWhiteSpace(metadata.Description)
                ? $"通过快捷方式启动 {application.Name}"
                : metadata.Description!;
            result.AddUnique(new ShortcutEntry
            {
                Id = ShortcutEntry.BuildId(
                    application.Id,
                    gesture.Canonical,
                    action,
                    ShortcutSourceKind.ShortcutFile,
                    path,
                    linkName),
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                Gesture = gesture,
                ActionName = action,
                Description =
                    $"Windows 快捷方式“{linkName}”保存了全局热键，用于启动 {application.Name}。",
                Scope = ShortcutScope.Global,
                SourceKind = ShortcutSourceKind.ShortcutFile,
                SourceName = ".lnk 快捷方式热键字段",
                SourceReference = path,
                Confidence = ConfidenceLevel.High,
                ConfidenceRationale =
                    "直接读取快捷方式头部保存的 HotKey 字段；这能证明用户配置了注册请求，但目标程序未运行或注册被占用时可能不生效。",
                Origin = ShortcutOrigin.UserConfigured,
                Context = $"开始菜单/桌面快捷方式：{linkName}",
                RawValue = $"{gesture.Canonical} (VK {gesture.VirtualKey})",
                CapturedAt = context.StartedAt
            });
            result.CoveredApplicationIds.Add(application.Id);
        }

        if (hotkeyFiles == 0)
        {
            result.AddDiagnostic(
                DiagnosticSeverity.Info,
                DisplayName,
                $"检查了 {files.Count} 个开始菜单和桌面快捷方式，未发现设置了全局热键的 .lnk 文件。");
        }

        if (unresolved > 0)
        {
            result.AddDiagnostic(
                DiagnosticSeverity.Info,
                DisplayName,
                $"{unresolved} 个带热键的快捷方式未能映射到已发现应用，可能是启动器、卸载器或目标不可读。");
        }

        if (duplicate > 0)
        {
            result.AddDiagnostic(
                DiagnosticSeverity.Info,
                DisplayName,
                $"{duplicate} 个快捷方式与同一应用的现有热键重复，已合并显示。");
        }

        return result;
    }

    private static IReadOnlyList<string> GetRoots()
    {
        var roots = new List<string>();
        Add(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        return roots;

        void Add(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                roots.Add(path);
            }
        }
    }
}
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Models;
using Microsoft.Win32;

namespace HotkeyLens.Core.Discovery;

/// <summary>
/// Read-only discovery across uninstall registry keys, App Paths, and Start
/// Menu/desktop shortcuts. It never starts another application.
/// </summary>
public sealed class ApplicationDiscoveryService : IApplicationDiscoveryService
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string AppPathsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    private static readonly Regex DisplayIconIndex = new(@",\s*-?\d+\s*$", RegexOptions.Compiled);
    private static readonly Regex UpdateName = new(
        @"^(?:update for|security update|hotfix|kb\d{5,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] ComponentMarkers =
    [
        "redistributable", "runtime", "software development kit", "windows sdk", "update health",
        "edge update", "google update", "bonjour", "crash reporter", "diagnostics hub"
    ];

    public Task<ApplicationDiscoveryResult> DiscoverAsync(
        ScanOptions options,
        CancellationToken cancellationToken)
        => Task.Run(() => Discover(options, cancellationToken), cancellationToken);

    private static ApplicationDiscoveryResult Discover(ScanOptions options, CancellationToken cancellationToken)
    {
        var candidates = new List<ApplicationCandidate>();
        var diagnostics = new List<ScanDiagnostic>();

        if (options.IncludeSystemApplications)
        {
            AddWindowsShell(candidates);
        }

        cancellationToken.ThrowIfCancellationRequested();
        AddRegistryCandidates(candidates, diagnostics, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        AddAppPathCandidates(candidates, diagnostics, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        AddStartMenuCandidates(candidates, diagnostics, cancellationToken);

        var merged = ApplicationMerger.Merge(candidates)
            .Select(ApplicationSettingsEnricher.Enrich)
            .Where(app => !ShouldExclude(app, options))
            .ToList();

        if (merged.Count == 0)
        {
            diagnostics.Add(new ScanDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Source = "应用发现",
                Message = "没有从本地来源发现可展示的应用程序。",
                Detail = "注册表和开始菜单均未返回可用的应用记录。"
            });
        }

        return new ApplicationDiscoveryResult
        {
            Applications = merged,
            Diagnostics = diagnostics
        };
    }

    private static void AddWindowsShell(List<ApplicationCandidate> candidates)
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (!File.Exists(explorer))
        {
            return;
        }

        candidates.Add(new ApplicationCandidate
        {
            Name = "Windows Shell",
            ExecutablePath = explorer,
            IconPath = explorer,
            InstallLocation = Path.GetDirectoryName(explorer),
            Version = GetFileVersion(explorer),
            Publisher = "Microsoft",
            Category = ApplicationCategory.System,
            SourceKind = ApplicationSourceKind.WindowsBuiltIn,
            SourceDetail = "Windows 内置",
            IsSystemComponent = true
        });
    }

    private static void AddRegistryCandidates(
        List<ApplicationCandidate> candidates,
        List<ScanDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var views = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32)
        };

        foreach (var (hive, view) in views)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(UninstallPath);
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var key = uninstall.OpenSubKey(subKeyName);
                        if (key is null)
                        {
                            continue;
                        }

                        var name = ReadString(key, "DisplayName");
                        if (string.IsNullOrWhiteSpace(name) ||
                            UpdateName.IsMatch(name) ||
                            ReadInt(key, "SystemComponent") == 1 ||
                            ReadInt(key, "NoDisplay") == 1 ||
                            !string.IsNullOrWhiteSpace(ReadString(key, "ParentKeyName")) ||
                            IsUpdateReleaseType(ReadString(key, "ReleaseType")))
                        {
                            continue;
                        }

                        var displayIcon = ReadString(key, "DisplayIcon");
                        var iconPath = ExtractIconPath(displayIcon);
                        var executable = iconPath is not null &&
                                         iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                         File.Exists(iconPath)
                            ? iconPath
                            : null;
                        var icon = iconPath is not null && File.Exists(iconPath) ? iconPath : null;

                        candidates.Add(new ApplicationCandidate
                        {
                            Name = name.Trim(),
                            ExecutablePath = executable,
                            IconPath = icon,
                            InstallLocation = ExistingPath(ReadString(key, "InstallLocation")),
                            Version = ReadString(key, "DisplayVersion"),
                            Publisher = ReadString(key, "Publisher"),
                            Category = ApplicationClassifier.Classify(name, executable),
                            SourceKind = ApplicationSourceKind.Registry,
                            SourceDetail = "注册表卸载信息"
                        });
                    }
                    catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or ArgumentException)
                    {
                        // Continue with the remaining entries; one malformed value
                        // must not invalidate the whole registry scan.
                    }
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException or PlatformNotSupportedException)
            {
                diagnostics.Add(new ScanDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Source = "应用发现 · 注册表",
                    Message = "部分注册表卸载信息无法读取。",
                    Detail = exception.Message
                });
            }
        }
    }

    private static void AddAppPathCandidates(
        List<ApplicationCandidate> candidates,
        List<ScanDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                     (RegistryHive.CurrentUser, RegistryView.Registry64)
                 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var appPaths = baseKey.OpenSubKey(AppPathsPath);
                if (appPaths is null)
                {
                    continue;
                }

                foreach (var subKeyName in appPaths.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var key = appPaths.OpenSubKey(subKeyName);
                        var path = ReadString(key, null);
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            continue;
                        }

                        var executable = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                        if (!File.Exists(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var info = FileVersionInfo.GetVersionInfo(executable);
                        // App Paths 的 ProductName 常被办公套件统一写成 "Microsoft Office"，
                        // 会让 Word、Excel 等可执行文件在列表中重名。FileDescription 通常是
                        // 面向用户的应用程序名，因此这里优先使用它。
                        var name = !string.IsNullOrWhiteSpace(info.FileDescription)
                            ? info.FileDescription.Trim()
                            : !string.IsNullOrWhiteSpace(info.ProductName)
                                ? info.ProductName.Trim()
                                : Path.GetFileNameWithoutExtension(executable);

                        candidates.Add(new ApplicationCandidate
                        {
                            Name = name,
                            ExecutablePath = executable,
                            IconPath = executable,
                            InstallLocation = Path.GetDirectoryName(executable),
                            Version = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion,
                            Publisher = info.CompanyName,
                            Category = ApplicationClassifier.Classify(name, executable),
                            SourceKind = ApplicationSourceKind.AppPath,
                            SourceDetail = "App Paths"
                        });
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                    }
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException or PlatformNotSupportedException)
            {
                diagnostics.Add(new ScanDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Source = "应用发现 · App Paths",
                    Message = "部分 App Paths 记录无法读取。",
                    Detail = exception.Message
                });
            }
        }
    }

    private static void AddStartMenuCandidates(
        List<ApplicationCandidate> candidates,
        List<ScanDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var roots = new List<(string Path, string Detail)>();
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "开始菜单快捷方式");
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "当前用户开始菜单快捷方式");
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "公共桌面快捷方式");
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "当前用户桌面快捷方式");

        var files = ShortcutLinkReader.EnumerateShortcutFiles(roots.Select(root => root.Path).ToArray());
        if (files.Count == 0)
        {
            return;
        }

        var sourceFor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root.Path, "*.lnk", SearchOption.AllDirectories))
                {
                    sourceFor[file] = root.Detail;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        using var resolver = new WScriptShortcutResolver();
        var resolutionFailed = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shortcutName = Path.GetFileNameWithoutExtension(file);
            if (IsUnwantedShortcutName(shortcutName))
            {
                continue;
            }

            if (!resolver.TryResolve(file, out var metadata))
            {
                resolutionFailed++;
                continue;
            }

            var target = metadata.TargetPath;
            if (string.IsNullOrWhiteSpace(target) ||
                !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(target))
            {
                continue;
            }

            var isExplorer = Path.GetFileName(target).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
                             && target.Contains(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                 StringComparison.OrdinalIgnoreCase);
            var name = CleanShortcutName(shortcutName);
            if (isExplorer)
            {
                name = "Windows Shell";
            }

            var info = TryGetVersionInfo(target);
            var icon = ExtractIconPath(metadata.IconLocation);
            if (icon is null || !File.Exists(icon))
            {
                icon = target;
            }

            candidates.Add(new ApplicationCandidate
            {
                Name = name,
                ExecutablePath = target,
                IconPath = icon,
                InstallLocation = Path.GetDirectoryName(target),
                Version = info?.ProductVersion ?? info?.FileVersion ?? GetFileVersion(target),
                Publisher = info?.CompanyName,
                Category = isExplorer
                    ? ApplicationCategory.System
                    : ApplicationClassifier.Classify(name, target),
                SourceKind = isExplorer ? ApplicationSourceKind.WindowsBuiltIn : ApplicationSourceKind.StartMenu,
                SourceDetail = sourceFor.GetValueOrDefault(file, "快捷方式"),
                IsSystemComponent = isExplorer
            });
        }

        if (resolutionFailed > 0)
        {
            diagnostics.Add(new ScanDiagnostic
            {
                Severity = DiagnosticSeverity.Info,
                Source = "应用发现 · 快捷方式",
                Message = $"{resolutionFailed} 个快捷方式无法解析目标路径，已跳过。",
                Detail = "可能指向已卸载的程序，或目标由系统策略保护。"
            });
        }
    }

    private static void AddRoot(List<(string Path, string Detail)> roots, string path, string detail)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) ||
            roots.Any(root => string.Equals(root.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        roots.Add((path, detail));
    }

    private static bool ShouldExclude(DiscoveredApplication app, ScanOptions options)
    {
        if (!options.IncludeRegistryOnlyApplications &&
            string.IsNullOrWhiteSpace(app.ExecutablePath) &&
            app.SourceKind == ApplicationSourceKind.Registry)
        {
            return true;
        }

        if (!options.IncludeSystemApplications && app.Category == ApplicationCategory.System)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(app.ExecutablePath))
        {
            return false;
        }

        var lowered = app.Name.ToLowerInvariant();
        if (lowered.Contains("visual c++", StringComparison.Ordinal) ||
            lowered.Contains("update health", StringComparison.Ordinal))
        {
            return true;
        }

        return ComponentMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal));
    }

    private static bool IsUpdateReleaseType(string? releaseType)
        => !string.IsNullOrWhiteSpace(releaseType) &&
           (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
            releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase) ||
            releaseType.Contains("Security", StringComparison.OrdinalIgnoreCase));

    private static bool IsUnwantedShortcutName(string name)
    {
        var lowered = name.ToLowerInvariant();
        string[] markers = ["uninstall", "卸载", "readme", "license", "release notes", "更新说明", "help", "帮助"];
        return markers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal));
    }

    private static string CleanShortcutName(string name)
    {
        var cleaned = Regex.Replace(name, @"\s*-\s*Shortcut$", string.Empty, RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? name.Trim() : cleaned;
    }

    private static string? ReadString(RegistryKey? key, string? name)
    {
        if (key is null)
        {
            return null;
        }

        var value = name is null ? key.GetValue(null) : key.GetValue(name);
        return value as string;
    }

    private static int? ReadInt(RegistryKey key, string name)
    {
        try
        {
            var value = key.GetValue(name);
            return value is null ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static string? ExtractIconPath(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        var value = displayIcon.Trim();
        if (value.StartsWith('"'))
        {
            var closing = value.IndexOf('"', 1);
            if (closing > 1)
            {
                value = value[1..closing];
            }
        }
        else
        {
            value = DisplayIconIndex.Replace(value, string.Empty);
        }

        value = Environment.ExpandEnvironmentVariables(value.Trim());
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ExistingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Directory.Exists(expanded) ? expanded : null;
    }

    private static FileVersionInfo? TryGetVersionInfo(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? GetFileVersion(string path) => TryGetVersionInfo(path)?.ProductVersion;
}
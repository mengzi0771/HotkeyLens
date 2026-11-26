using HotkeyLens.Core.Catalog;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Discovery;

public enum ConfigurationFormat
{
    VisualStudioCode,
    WindowsTerminal,
    JetBrainsKeymap,
    NotepadPlusPlusShortcuts,
    ChromiumPreferences,
    SnipasteConfig
}

public sealed record ConfigurationFileDescriptor
{
    public required ConfigurationFormat Format { get; init; }
    public required string ProcessName { get; init; }
    public required string ProductName { get; init; }
    public required string FilePath { get; init; }
    public required string Description { get; init; }
    public required string SettingsHint { get; init; }
}

/// <summary>
/// Locates only well-known, user-readable configuration files. Nothing here
/// writes to another application's files.
/// </summary>
public static class KnownConfigurationLocations
{
    private static readonly (string[] Prefixes, string ProcessName, string ProductName)[] JetBrainsProducts =
    [
        (["IntelliJIdea", "IdeaIC"], "idea64.exe", "IntelliJ IDEA"),
        (["PyCharm"], "pycharm64.exe", "PyCharm"),
        (["WebStorm"], "webstorm64.exe", "WebStorm"),
        (["CLion"], "clion64.exe", "CLion"),
        (["GoLand"], "goland64.exe", "GoLand"),
        (["Rider"], "rider64.exe", "Rider"),
        (["PhpStorm"], "phpstorm64.exe", "PhpStorm"),
        (["RubyMine"], "rubymine64.exe", "RubyMine"),
        (["DataGrip"], "datagrip64.exe", "DataGrip"),
        (["AndroidStudio"], "studio64.exe", "Android Studio")
    ];

    private static readonly (string ProcessName, string ProductName, string RelativeUserFolder)[] VsCodeProducts =
    [
        ("Code.exe", "Visual Studio Code", "Code"),
        ("Code - Insiders.exe", "Visual Studio Code Insiders", "Code - Insiders"),
        ("VSCodium.exe", "VSCodium", "VSCodium"),
        ("Cursor.exe", "Cursor", "Cursor")
    ];

    public static IReadOnlyList<ConfigurationFileDescriptor> GetExistingConfigurationFiles()
    {
        var result = new List<ConfigurationFileDescriptor>();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var (processName, productName, folder) in VsCodeProducts)
        {
            var path = Path.Combine(appData, folder, "User", "keybindings.json");
            if (File.Exists(path))
            {
                result.Add(new ConfigurationFileDescriptor
                {
                    Format = ConfigurationFormat.VisualStudioCode,
                    ProcessName = processName,
                    ProductName = productName,
                    FilePath = path,
                    Description = $"{productName} 用户 keybindings.json",
                    SettingsHint = $"{productName} 用户键位配置"
                });
            }
        }

        foreach (var path in FindWindowsTerminalSettings())
        {
            result.Add(new ConfigurationFileDescriptor
            {
                Format = ConfigurationFormat.WindowsTerminal,
                ProcessName = path.Contains("Preview", StringComparison.OrdinalIgnoreCase)
                    ? "WindowsTerminalPreview.exe"
                    : "WindowsTerminal.exe",
                ProductName = path.Contains("Preview", StringComparison.OrdinalIgnoreCase)
                    ? "Windows Terminal Preview"
                    : "Windows Terminal",
                FilePath = path,
                Description = "Windows Terminal 用户 settings.json",
                SettingsHint = "Windows Terminal 用户设置"
            });
        }

        result.AddRange(GetJetBrainsKeymapFiles());
        result.AddRange(GetChromiumPreferenceFiles());
        result.AddRange(GetSnipasteConfigurationFiles());

        var notepadPlusPlusSettings = Path.Combine(appData, "Notepad++", "shortcuts.xml");
        if (File.Exists(notepadPlusPlusSettings))
        {
            result.Add(new ConfigurationFileDescriptor
            {
                Format = ConfigurationFormat.NotepadPlusPlusShortcuts,
                ProcessName = "notepad++.exe",
                ProductName = "Notepad++",
                FilePath = notepadPlusPlusSettings,
                Description = "Notepad++ 用户 shortcuts.xml",
                SettingsHint = "Notepad++ 快捷键配置（HotkeyLens 只读扫描）"
            });
        }

        return result;
    }

    public static string? FindSettingsPath(DiscoveredApplication application)
    {
        var processName = application.ProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var (knownProcess, _, folder) in VsCodeProducts)
        {
            if (string.Equals(processName, knownProcess, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(appData, folder, "User", "keybindings.json");
            }
        }

        if (processName.Equals("WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("WindowsTerminalPreview.exe", StringComparison.OrdinalIgnoreCase))
        {
            var existing = FindWindowsTerminalSettings()
                .FirstOrDefault(path => path.Contains("Preview", StringComparison.OrdinalIgnoreCase) ==
                                        processName.Contains("Preview", StringComparison.OrdinalIgnoreCase));
            return existing ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows Terminal", "settings.json");
        }

        if (processName.Equals("notepad++.exe", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(appData, "Notepad++", "shortcuts.xml");
        }

        if (processName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
        {
            var product = processName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)
                ? "Google Chrome"
                : "Microsoft Edge";
            return GetChromiumPreferenceFiles()
                .FirstOrDefault(file => string.Equals(file.ProductName, product, StringComparison.OrdinalIgnoreCase))
                ?.FilePath;
        }

        if (processName.Equals("Snipaste.exe", StringComparison.OrdinalIgnoreCase))
        {
            return GetSnipasteConfigurationFiles().FirstOrDefault()?.FilePath;
        }

        var jetBrains = GetJetBrainsKeymapFiles()
            .FirstOrDefault(file => string.Equals(file.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
        if (jetBrains is not null)
        {
            return Path.GetDirectoryName(jetBrains.FilePath);
        }

        return null;
    }

    public static string? FindSettingsHint(DiscoveredApplication application)
    {
        var processName = application.ProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        if (VsCodeProducts.Any(product =>
                string.Equals(product.ProcessName, processName, StringComparison.OrdinalIgnoreCase)))
        {
            return "VS Code 兼容的用户键位配置文件（只读扫描；修改需在应用内完成）";
        }

        if (processName.Equals("WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("WindowsTerminalPreview.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows Terminal 用户设置（只读扫描；修改需在应用内完成）";
        }

        if (processName.Equals("notepad++.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Notepad++ 快捷键配置（HotkeyLens 仅定位，不自动改写）";
        }

        if (processName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "浏览器扩展快捷键保存在 Preferences 中（只读扫描）；修改请在 chrome://extensions/shortcuts 或 edge://extensions/shortcuts 完成。";
        }

        if (processName.Equals("Snipaste.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Snipaste 热键配置（只读扫描）；修改请在 Snipaste 首选项 →「控制」中完成。";
        }

        if (JetBrainsProducts.Any(product =>
                string.Equals(product.ProcessName, processName, StringComparison.OrdinalIgnoreCase)))
        {
            return "JetBrains 自定义 keymap（HotkeyLens 只读扫描，不自动改写）";
        }

        return null;
    }

    /// <summary>
    /// Locates the read-only Chromium Preferences file of every signed-in
    /// browser profile. Only stable channels are inspected; nothing is written.
    /// </summary>
    public static IReadOnlyList<ConfigurationFileDescriptor> GetChromiumPreferenceFiles()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var browsers = new[]
        {
            (ProcessName: "chrome.exe", ProductName: "Google Chrome", RelativeUserData: Path.Combine("Google", "Chrome", "User Data")),
            (ProcessName: "msedge.exe", ProductName: "Microsoft Edge", RelativeUserData: Path.Combine("Microsoft", "Edge", "User Data"))
        };

        var result = new List<ConfigurationFileDescriptor>();
        foreach (var browser in browsers)
        {
            var userData = Path.Combine(localAppData, browser.RelativeUserData);
            if (!Directory.Exists(userData))
            {
                continue;
            }

            foreach (var profileDirectory in EnumerateProfileDirectories(userData))
            {
                var preferences = Path.Combine(profileDirectory, "Preferences");
                if (!File.Exists(preferences))
                {
                    continue;
                }

                var profileName = Path.GetFileName(profileDirectory);
                result.Add(new ConfigurationFileDescriptor
                {
                    Format = ConfigurationFormat.ChromiumPreferences,
                    ProcessName = browser.ProcessName,
                    ProductName = browser.ProductName,
                    FilePath = preferences,
                    Description = $"{browser.ProductName} · {profileName} 配置文件的扩展快捷键（Preferences）",
                    SettingsHint = "浏览器扩展快捷键保存在 Preferences 中；HotkeyLens 只读解析，修改请在浏览器内完成。"
                });
            }
        }

        return result;
    }

    public static IReadOnlyList<ConfigurationFileDescriptor> GetSnipasteConfigurationFiles()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snipaste", "config.ini"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snipaste", "config.ini")
        };

        var result = new List<ConfigurationFileDescriptor>();
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            result.Add(new ConfigurationFileDescriptor
            {
                Format = ConfigurationFormat.SnipasteConfig,
                ProcessName = "Snipaste.exe",
                ProductName = "Snipaste",
                FilePath = path,
                Description = "Snipaste 用户 config.ini",
                SettingsHint = "Snipaste 热键保存在 config.ini 中；HotkeyLens 只读解析，修改请在首选项里完成。"
            });
        }

        return result;
    }

    private static IReadOnlyList<string> EnumerateProfileDirectories(string userData)
    {
        try
        {
            return Directory.EnumerateDirectories(userData)
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    return string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static IEnumerable<string> FindWindowsTerminalSettings()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var packageRoot = Path.Combine(localAppData, "Packages");
        if (Directory.Exists(packageRoot))
        {
            foreach (var prefix in new[] { "Microsoft.WindowsTerminal_", "Microsoft.WindowsTerminalPreview_" })
            {
                foreach (var package in EnumerateDirectoriesSafe(packageRoot, prefix + "*"))
                {
                    var settings = Path.Combine(package, "LocalState", "settings.json");
                    if (File.Exists(settings))
                    {
                        yield return settings;
                    }
                }
            }
        }

        var unpackaged = Path.Combine(localAppData, "Microsoft", "Windows Terminal", "settings.json");
        if (File.Exists(unpackaged))
        {
            yield return unpackaged;
        }
    }

    private static IReadOnlyList<string> EnumerateDirectoriesSafe(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateDirectories(root, pattern).ToList();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static IReadOnlyList<ConfigurationFileDescriptor> GetJetBrainsKeymapFiles()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = Path.Combine(appData, "JetBrains");
        if (!Directory.Exists(root))
        {
            return Array.Empty<ConfigurationFileDescriptor>();
        }

        var result = new List<ConfigurationFileDescriptor>();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var folderName = Path.GetFileName(directory);
                var product = JetBrainsProducts.FirstOrDefault(entry =>
                    entry.Prefixes.Any(prefix => folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
                if (product.ProcessName is null)
                {
                    continue;
                }

                var keymapDirectory = Path.Combine(directory, "keymaps");
                var files = Directory.Exists(keymapDirectory)
                    ? Directory.EnumerateFiles(keymapDirectory, "*.xml", SearchOption.TopDirectoryOnly).ToList()
                    : [];
                if (files.Count == 0)
                {
                    var optionFile = Path.Combine(directory, "options", "keymap.xml");
                    if (File.Exists(optionFile))
                    {
                        files.Add(optionFile);
                    }
                }

                foreach (var file in files)
                {
                    result.Add(new ConfigurationFileDescriptor
                    {
                        Format = ConfigurationFormat.JetBrainsKeymap,
                        ProcessName = product.ProcessName,
                        ProductName = product.ProductName,
                        FilePath = file,
                        Description = $"{product.ProductName} 用户 keymap",
                        SettingsHint = $"{product.ProductName} 自定义 keymap（只读）"
                    });
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        return result;
    }
}

public static class ApplicationSettingsEnricher
{
    /// <summary>
    /// Fills settings locations and plain-language resolution guidance from the
    /// local read-only signals and the curated catalog. Unknown values stay
    /// empty instead of being guessed.
    /// </summary>
    public static DiscoveredApplication Enrich(
        DiscoveredApplication application,
        ShortcutCatalog? catalog = null)
    {
        var profile = WellKnownApplications.Match(application.ProcessName);
        var catalogApplication = catalog is null
            ? null
            : catalog.Match(application).FirstOrDefault();

        var settingsPath = !string.IsNullOrWhiteSpace(application.SettingsPath)
            ? application.SettingsPath
            : KnownConfigurationLocations.FindSettingsPath(application);
        var settingsHint = FirstNonEmpty(
            application.SettingsHint,
            catalogApplication?.SettingsHint,
            profile?.SettingsHint,
            KnownConfigurationLocations.FindSettingsHint(application));
        var resolutionGuide = FirstNonEmpty(
            application.ResolutionGuide,
            catalogApplication?.ResolutionGuide,
            profile?.ResolutionGuide);

        return application with
        {
            Category = profile?.Category ?? application.Category,
            SettingsPath = settingsPath,
            SettingsHint = settingsHint,
            ResolutionGuide = resolutionGuide ?? string.Empty
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
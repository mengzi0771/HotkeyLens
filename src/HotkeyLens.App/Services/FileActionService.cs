using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace HotkeyLens.App.Services;

public static class FileActionService
{
    public static bool TryOpenUrl(string? url, out string error)
    {
        error = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "来源不是可打开的网页地址。";
            return false;
        }

        return TryStart(uri.AbsoluteUri, string.Empty, out error);
    }

    public static bool TryOpenPath(string? path, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "没有记录文件位置。";
            return false;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            error = "目标文件或文件夹不存在，可能已被移动。";
            return false;
        }

        return TryStart(path, string.Empty, out error);
    }

    public static bool TryReveal(string? path, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "没有记录文件位置。";
            return false;
        }

        if (File.Exists(path))
        {
            return TryStart("explorer.exe", $"/select,\"{path}\"", out error);
        }

        if (Directory.Exists(path))
        {
            return TryStart(path, string.Empty, out error);
        }

        error = "目标文件或文件夹不存在，可能已被移动。";
        return false;
    }

    private static bool TryStart(string fileName, string arguments, out string error)
    {
        error = string.Empty;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            };
            Process.Start(startInfo);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = exception.Message;
            return false;
        }
    }
}

public static class ThemeManager
{
    private const string ThemeMarker = "Themes/";

    public static void Apply(string? preference)
    {
        var effectiveDark = preference switch
        {
            "Dark" => true,
            "Light" => false,
            _ => IsSystemDark()
        };

        var source = new Uri(
            effectiveDark ? "Themes/Dark.xaml" : "Themes/Light.xaml",
            UriKind.Relative);
        var resources = System.Windows.Application.Current.Resources;
        var dictionaries = resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains(ThemeMarker, StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new System.Windows.ResourceDictionary { Source = source };

        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
            return;
        }

        var index = dictionaries.IndexOf(existing);
        dictionaries[index] = replacement;
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }
}
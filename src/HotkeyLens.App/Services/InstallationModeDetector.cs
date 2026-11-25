using Microsoft.Win32;

namespace HotkeyLens.App.Services;

public enum InstallationMode
{
    Installed,
    Portable
}

public static class InstallationModeDetector
{
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{26F1B708-3044-44E6-BA58-9D3BD08AB3D9}_is1";

    public static InstallationMode Detect()
    {
        // A marker travels with the extracted archive. Check it first: a user can
        // run the portable build on a machine that also has the installed build.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.marker")))
        {
            return InstallationMode.Portable;
        }

        if (IsInnoInstallation())
        {
            return InstallationMode.Installed;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (IsUnder(baseDirectory, programFiles) || IsUnder(baseDirectory, programFilesX86))
        {
            return InstallationMode.Installed;
        }

        return InstallationMode.Portable;
    }

    private static bool IsInnoInstallation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key is not null;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsUnder(string path, string root)
        => !string.IsNullOrWhiteSpace(root) &&
           path.StartsWith(
               root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
               StringComparison.OrdinalIgnoreCase);
}
using System.Runtime.InteropServices;
using System.Text;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Discovery;

public sealed record ShortcutLinkMetadata
{
    public string? TargetPath { get; init; }
    public string? Arguments { get; init; }
    public string? IconLocation { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Reads the 76-byte Shell Link header (MS-SHLLINK) and resolves .lnk targets
/// through the Windows Shell's WScript.Shell COM object.
/// </summary>
public static class ShortcutLinkReader
{
    private const int HeaderLength = 76;
    private const int HotKeyOffset = 64;

    public static bool TryReadHotkey(string shortcutPath, out ShortcutGesture? gesture)
    {
        gesture = null;
        try
        {
            using var stream = new FileStream(shortcutPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[HeaderLength];
            if (stream.Read(header, 0, header.Length) < HeaderLength)
            {
                return false;
            }

            // HeaderSize must be 0x0000004C, then the standard ShellLink CLSID.
            if (BitConverter.ToUInt32(header, 0) != 0x4C)
            {
                return false;
            }

            var virtualKey = header[HotKeyOffset];
            var flagByte = header[HotKeyOffset + 1];
            if (virtualKey == 0)
            {
                return false;
            }

            var modifiers = ShortcutModifiers.None;
            if ((flagByte & 0x01) != 0) modifiers |= ShortcutModifiers.Shift;
            if ((flagByte & 0x02) != 0) modifiers |= ShortcutModifiers.Control;
            if ((flagByte & 0x04) != 0) modifiers |= ShortcutModifiers.Alt;
            // 0x08 is HOTKEYF_EXT: an extended scan-code flag, not a modifier.

            gesture = ShortcutGesture.FromVirtualKey(virtualKey, modifiers);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            // The VK is valid in Windows but not in our display catalog.
            return false;
        }
    }

    public static IReadOnlyList<string> EnumerateShortcutFiles(params string[] roots)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    files.Add(file);
                }
            }
            catch (IOException)
            {
                // A single unreadable folder must not abort application discovery.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return files.OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

/// <summary>Thin, disposable wrapper around WScript.Shell's shortcut resolver.</summary>
public sealed class WScriptShortcutResolver : IDisposable
{
    private readonly object? _shell;
    private readonly Type? _shellType;
    private bool _disposed;

    public WScriptShortcutResolver()
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is not null)
            {
                _shellType = type;
                _shell = Activator.CreateInstance(type);
            }
        }
        catch (COMException)
        {
            _shell = null;
            _shellType = null;
        }
    }

    public bool TryResolve(string shortcutPath, out ShortcutLinkMetadata metadata)
    {
        metadata = new ShortcutLinkMetadata();
        if (_disposed || _shell is null || _shellType is null)
        {
            return false;
        }

        object? shortcut = null;
        try
        {
            shortcut = _shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                _shell,
                [shortcutPath]);

            if (shortcut is null)
            {
                return false;
            }

            string Read(string property)
                => Convert.ToString(
                       shortcut.GetType().InvokeMember(
                           property,
                           System.Reflection.BindingFlags.GetProperty,
                           null,
                           shortcut,
                           null),
                       System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;

            var target = NormalizeShellPath(Read("TargetPath"));
            var icon = NormalizeShellPath(Read("IconLocation"));
            metadata = new ShortcutLinkMetadata
            {
                TargetPath = target,
                Arguments = Read("Arguments"),
                IconLocation = icon,
                Description = Read("Description")
            };

            return !string.IsNullOrWhiteSpace(target);
        }
        catch (Exception exception) when (exception is COMException or TargetInvocationException or InvalidCastException)
        {
            return false;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
    }

    private static string? NormalizeShellPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (!File.Exists(expanded) && !Directory.Exists(expanded))
        {
            return expanded;
        }

        return Path.GetFullPath(expanded);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_shell is not null && Marshal.IsComObject(_shell))
        {
            Marshal.FinalReleaseComObject(_shell);
        }
    }
}
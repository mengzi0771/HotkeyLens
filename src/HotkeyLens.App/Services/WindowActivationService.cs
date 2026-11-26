using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HotkeyLens.App.Services;

/// <summary>
/// Brings an already-running application's main window to the foreground.
/// It only uses standard Win32 window calls on a window that belongs to the
/// process the user picked; it never launches, restarts or injects into the
/// target application.
/// </summary>
public static class WindowActivationService
{
    private const int SwShowNormal = 1;
    private const int SwShow = 5;
    private const int SwRestore = 9;

    public static bool TryActivate(string? processName, out string error)
    {
        error = string.Empty;
        var normalized = NormalizeProcessName(processName);
        if (normalized.Length == 0)
        {
            error = "该应用没有记录进程名，无法定位窗口。";
            return false;
        }

        var candidates = new List<(int ProcessId, nint WindowHandle)>();
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(normalized);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            error = $"读取进程列表失败：{exception.Message}";
            return false;
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var mainWindow = process.MainWindowHandle;
                    if (mainWindow != nint.Zero)
                    {
                        candidates.Add((process.Id, mainWindow));
                        continue;
                    }

                    var enumerated = FindTopLevelWindow(process.Id);
                    if (enumerated != nint.Zero)
                    {
                        candidates.Add((process.Id, enumerated));
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process may have exited between enumeration and query.
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        if (candidates.Count == 0)
        {
            error = "该应用当前没有可用的主窗口（可能已退出，或仍在托盘启动中）。请先在任务栏打开它。";
            return false;
        }

        foreach (var candidate in candidates)
        {
            if (TryBringToFront(candidate.WindowHandle))
            {
                return true;
            }
        }

        error = "Windows 阻止了窗口切换（目标窗口可能以管理员权限运行）。请直接从任务栏点击该应用。";
        return false;
    }

    private static bool TryBringToFront(nint window)
    {
        try
        {
            if (IsIconic(window))
            {
                ShowWindow(window, SwRestore);
            }
            else if (!IsWindowVisible(window))
            {
                ShowWindow(window, SwShow);
            }
            else
            {
                ShowWindow(window, SwShowNormal);
            }

            var foreground = GetForegroundWindow();
            if (foreground == window)
            {
                return true;
            }

            var foregroundThread = GetWindowThreadProcessId(foreground, out _);
            var targetThread = GetWindowThreadProcessId(window, out _);
            var attached = foregroundThread != 0 && targetThread != 0 &&
                           AttachThreadInput(foregroundThread, targetThread, true);
            try
            {
                SetForegroundWindow(window);
                BringWindowToTop(window);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(foregroundThread, targetThread, false);
                }
            }

            return GetForegroundWindow() == window;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static nint FindTopLevelWindow(int processId)
    {
        nint found = nint.Zero;
        var visibleWithTitle = nint.Zero;
        EnumWindows((handle, _) =>
        {
            if (GetWindowThreadProcessId(handle, out var owner) == 0 || owner != processId)
            {
                return true;
            }

            if (GetWindowTextLength(handle) == 0)
            {
                return true;
            }

            if (IsWindowVisible(handle))
            {
                visibleWithTitle = handle;
                return false;
            }

            if (found == nint.Zero)
            {
                found = handle;
            }

            return true;
        }, nint.Zero);

        return visibleWithTitle != nint.Zero ? visibleWithTitle : found;
    }

    private static string NormalizeProcessName(string? processName)
    {
        var name = processName?.Trim() ?? string.Empty;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachThread, uint attachToThread, bool attach);
}

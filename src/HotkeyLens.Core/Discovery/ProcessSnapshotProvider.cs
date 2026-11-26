using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HotkeyLens.Core.Abstractions;

namespace HotkeyLens.Core.Discovery;

/// <summary>
/// Reads the Windows process table once per scan and augments it with the owning
/// processes of visible top-level windows. It never starts an application, never
/// opens a handle to another process' keyboard queue and never stays resident as a
/// watcher. A process table filtered by security software is reported as incomplete
/// so callers can degrade to "unknown" instead of claiming an app is not running.
/// </summary>
public sealed class ProcessSnapshotProvider : IProcessSnapshotProvider
{
    /// <summary>
    /// A normal Windows session exposes dozens of processes. A much shorter list
    /// usually means a host firewall / HIPS filtered the enumeration; 25 keeps a
    /// safe margin while still allowing unusually lean sessions to be trusted.
    /// </summary>
    public const int MinimumPlausibleProcessCount = 25;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public ProcessSnapshot GetSnapshot()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Process[]? processes = null;
        try
        {
            processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(process.ProcessName))
                    {
                        names.Add(process.ProcessName + ".exe");
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
                {
                    // A protected or exiting process simply drops out.
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            // An unreadable process table degrades to a partial snapshot.
        }
        finally
        {
            if (processes is not null)
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        // Judge completeness on the raw process list only: windows can reveal a
        // handful of protected applications without proving the table is complete.
        var isComplete = names.Count >= MinimumPlausibleProcessCount;
        AddVisibleWindowProcessNames(names);

        return new ProcessSnapshot(names, isComplete);
    }

    /// <summary>
    /// Some applications (WeChat, NetEase Cloud Music) protect their main process
    /// from enumeration but still own visible windows. Reading the window owner PID
    /// through user32 is cheap and read-only, so it recovers those names without
    /// opening the process or touching its keyboard queue.
    /// </summary>
    private static void AddVisibleWindowProcessNames(ISet<string> names)
    {
        try
        {
            EnumWindows((hWnd, _) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd))
                    {
                        return true;
                    }

                    if (GetWindowThreadProcessId(hWnd, out var processId) == 0 || processId == 0)
                    {
                        return true;
                    }

                    using var process = Process.GetProcessById((int)processId);
                    if (!string.IsNullOrWhiteSpace(process.ProcessName))
                    {
                        names.Add(process.ProcessName + ".exe");
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or ArgumentException)
                {
                    // The owning process exited or is protected; skip that window.
                }

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // Window enumeration is best-effort; the process list remains usable.
        }
    }
}

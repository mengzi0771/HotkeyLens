using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Adapters;

/// <summary>
/// Observes menu accelerators exposed by windows that are already open. It
/// never starts an application, expands menus, injects input or installs a
/// keyboard hook. UI Automation availability is entirely controlled by each
/// application's accessibility provider.
/// </summary>
public sealed class UiAutomationShortcutAdapter : IShortcutSourceAdapter
{
    private static readonly Condition MenuItemCondition =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem);

    public string Id => "ui-automation-menu";
    public string DisplayName => "UI Automation 菜单";
    public ShortcutSourceKind SourceKind => ShortcutSourceKind.UiAutomation;

    public bool IsEnabled(ScanOptions options) => options.IncludeUiAutomation;

    public async Task<AdapterScanResult> ScanAsync(
        ScanContext context,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(context.Options.UiAutomationTimeoutSeconds);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        var work = Task.Run(() => ScanCore(context, progress, linked.Token), linked.Token);
        try
        {
            return await work.WaitAsync(timeout + TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return TimeoutResult(context.Options.UiAutomationTimeoutSeconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TimeoutResult(context.Options.UiAutomationTimeoutSeconds);
        }
    }

    private AdapterScanResult ScanCore(
        ScanContext context,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = AdapterScanResult.Empty();
        AutomationElementCollection windows;
        try
        {
            windows = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                Condition.TrueCondition);
        }
        catch (Exception exception) when (IsUiAutomationFailure(exception))
        {
            result.AddDiagnostic(
                DiagnosticSeverity.Warning,
                DisplayName,
                "无法枚举桌面窗口，UI Automation 不可用。",
                exception.Message);
            return result;
        }

        var currentProcessId = Environment.ProcessId;
        var inspected = 0;
        var unreadable = 0;
        var unmapped = 0;
        var windowLimit = Math.Min(
            windows.Count,
            Math.Clamp(context.Options.UiAutomationMaxWindows, 1, 100));

        for (var index = 0; index < windows.Count && inspected < windowLimit; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = windows[index];
            int processId;
            string windowTitle;
            try
            {
                processId = window.Current.ProcessId;
                windowTitle = window.Current.Name;
            }
            catch (Exception exception) when (IsUiAutomationFailure(exception))
            {
                unreadable++;
                continue;
            }

            if (processId == 0 || processId == currentProcessId)
            {
                continue;
            }

            var application = ResolveApplication(context, processId);
            if (application is null)
            {
                unmapped++;
                continue;
            }

            inspected++;
            progress?.Report(new ScanProgress
            {
                Phase = "UI Automation",
                Message = $"正在读取窗口菜单：{windowTitle}",
                Percent = inspected * 100.0 / Math.Max(1, windowLimit),
                CompletedSteps = inspected,
                TotalSteps = windowLimit
            });

            try
            {
                var menuItems = window.FindAll(TreeScope.Descendants, MenuItemCondition);
                result.CoveredApplicationIds.Add(application.Id);
                for (var menuIndex = 0; menuIndex < menuItems.Count; menuIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = menuItems[menuIndex];
                    string? accelerator;
                    string name;
                    try
                    {
                        accelerator = item.Current.AcceleratorKey;
                        name = item.Current.Name;
                    }
                    catch (Exception exception) when (IsUiAutomationFailure(exception))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(accelerator) ||
                        !KeySequenceParser.TryParse(accelerator, out var gesture, out var sequence))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "未命名菜单项";
                    }

                    var displayGesture = sequence ?? gesture!.Canonical;
                    result.AddUnique(new ShortcutEntry
                    {
                        Id = ShortcutEntry.BuildId(
                            application.Id,
                            displayGesture,
                            name,
                            ShortcutSourceKind.UiAutomation,
                            $"uia://{application.Id}",
                            "当前窗口菜单"),
                        ApplicationId = application.Id,
                        ApplicationName = application.Name,
                        Gesture = gesture!,
                        KeySequence = sequence,
                        ActionName = name,
                        Description =
                            $"从已打开窗口“{windowTitle}”的 UI Automation 菜单项读取到的快捷键标注。",
                        Scope = ShortcutScope.Window,
                        SourceKind = ShortcutSourceKind.UiAutomation,
                        SourceName = "Windows UI Automation 菜单项",
                        SourceReference = $"uia://{application.ProcessName}",
                        Confidence = ConfidenceLevel.Medium,
                        ConfidenceRationale =
                            "读取当前已打开窗口的 MenuItem.AcceleratorKey；仅覆盖应用此刻暴露的 UI 树，未展开的菜单可能尚未加载，也无法证明这是官方默认值。",
                        Origin = ShortcutOrigin.RuntimeObserved,
                        Context = "当前窗口菜单",
                        RawValue = accelerator,
                        CapturedAt = context.StartedAt
                    });
                }
            }
            catch (Exception exception) when (IsUiAutomationFailure(exception))
            {
                unreadable++;
            }
        }

        if (inspected == 0)
        {
            result.AddDiagnostic(
                DiagnosticSeverity.Info,
                DisplayName,
                "没有找到可映射到已发现应用的其他进程窗口；UI Automation 只观察当前已打开窗口。");
        }

        if (unreadable > 0)
        {
            result.AddDiagnostic(
                DiagnosticSeverity.Info,
                DisplayName,
                $"{unreadable} 个窗口的 UI Automation 树无法读取，可能被应用禁用或已关闭。");
        }

        if (unmapped > 0)
        {
            result.AddDiagnostic(
                DiagnosticSeverity.Info,
                DisplayName,
                $"{unmapped} 个窗口的宿主进程未出现在应用清单中（常见于 UWP 宿主或辅助进程）。");
        }

        progress?.Report(new ScanProgress
        {
            Phase = "UI Automation",
            Message = $"已检查 {inspected} 个窗口的当前菜单",
            Percent = 100,
            CompletedSteps = inspected,
            TotalSteps = Math.Max(1, inspected)
        });

        return result;
    }

    private static DiscoveredApplication? ResolveApplication(ScanContext context, int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var processName = process.ProcessName + ".exe";
            var application = context.FindByProcessName(processName);
            if (application is not null)
            {
                return application;
            }

            try
            {
                var executable = process.MainModule?.FileName;
                return context.FindByExecutable(executable);
            }
            catch (Exception exception) when (
                exception is Win32Exception or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static AdapterScanResult TimeoutResult(int seconds)
    {
        var result = AdapterScanResult.Empty();
        result.AddDiagnostic(
            DiagnosticSeverity.Warning,
            "UI Automation 菜单",
            $"UI Automation 在 {seconds} 秒内未完成。已返回已经读取到的结果之外的其余来源；部分应用可能没有响应无障碍查询。");
        return result;
    }

    private static bool IsUiAutomationFailure(Exception exception)
        => exception is ElementNotAvailableException
            or COMException
            or InvalidOperationException
            or TimeoutException
            or NotSupportedException
            or UnauthorizedAccessException;
}
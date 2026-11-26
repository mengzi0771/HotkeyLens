namespace HotkeyLens.Core.Presentation;

/// <summary>
/// Single source of truth for runtime-state wording. "False" never claims the
/// application is closed, because a filtered process table can hide a running app.
/// </summary>
public static class RuntimeStatePresentation
{
    public static string Label(bool? isRunning) => isRunning switch
    {
        true => "正在运行",
        false => "未检测到运行",
        _ => "状态未知"
    };

    public static string Hint(string applicationName, bool? isRunning) => isRunning switch
    {
        true => $"{applicationName} 当前正在运行，可以切到它去修改快捷键。",
        false => $"{applicationName} 本次扫描没有检测到运行，可能已关闭，或被安全软件隐藏了进程。先打开它再修改快捷键。",
        _ => $"本次扫描没有取得 {applicationName} 的运行状态（进程列表可能被安全软件限制），请先确认它是否已打开。"
    };

    public static string ParticipantsSummary(int running, int unknown, int total)
    {
        if (unknown == total)
        {
            return "运行状态未知";
        }

        if (unknown > 0)
        {
            return running > 0
                ? $"{running} 个正在运行，{unknown} 个状态未知"
                : "运行状态未知";
        }

        return running switch
        {
            0 => "涉及软件均未检测到运行",
            _ when running == total => "涉及软件当前都在运行",
            _ => $"{running} / {total} 个软件正在运行"
        };
    }
}

namespace HotkeyLens.Core.Presentation;

/// <summary>
/// Explains the result of handing a path to explorer.exe. explorer.exe normally
/// returns exit code 1 after the existing shell takes over the request, so only a
/// negative (NTSTATUS / crash) code is treated as a failed launch.
/// </summary>
public static class RevealFeedback
{
    public static bool IsFailedExitCode(int? exitCode) => exitCode is < 0;

    public static string Failure(string? path, int? exitCode)
    {
        var target = string.IsNullOrWhiteSpace(path) ? "目标路径" : path;
        if (exitCode is int code && code < 0)
        {
            return $"无法打开资源管理器（错误 0x{unchecked((uint)code):X8}），请手动前往：{target}";
        }

        return $"无法打开资源管理器，请手动前往：{target}";
    }
}

using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Analysis;

public enum AuxiliaryTargetKind
{
    None,
    ConfigurationFile,
    ExecutableFile,
    InstallFolder
}

/// <summary>
/// A validated Explorer target for the "open folder / configuration" helper.
/// Validation is pure so the UI never launches a path that does not exist.
/// </summary>
public sealed record AuxiliaryTarget
{
    public AuxiliaryTargetKind Kind { get; init; } = AuxiliaryTargetKind.None;
    public string Path { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public bool CanOpen => Kind != AuxiliaryTargetKind.None;
}

public static class AuxiliaryTargetResolver
{
    private const string LabelConfiguration = "打开配置文件";
    private const string LabelExecutable = "打开所在目录";
    private const string LabelFolder = "打开安装目录";

    public static AuxiliaryTarget Resolve(DiscoveredApplication? application)
    {
        if (application is null)
        {
            return Unavailable("没有找到该快捷键所属应用程序的记录。");
        }

        var settingsPath = Normalize(application.SettingsPath);
        if (!string.IsNullOrEmpty(settingsPath))
        {
            if (File.Exists(settingsPath))
            {
                return new AuxiliaryTarget
                {
                    Kind = AuxiliaryTargetKind.ConfigurationFile,
                    Path = settingsPath,
                    Label = LabelConfiguration,
                    Message = $"已在资源管理器中定位 {application.Name} 的配置文件。"
                };
            }

            if (Directory.Exists(settingsPath))
            {
                return new AuxiliaryTarget
                {
                    Kind = AuxiliaryTargetKind.InstallFolder,
                    Path = settingsPath,
                    Label = LabelFolder,
                    Message = $"已打开 {application.Name} 的配置目录。"
                };
            }
        }

        var executable = Normalize(application.ExecutablePath);
        if (!string.IsNullOrEmpty(executable) && File.Exists(executable))
        {
            return new AuxiliaryTarget
            {
                Kind = AuxiliaryTargetKind.ExecutableFile,
                Path = executable,
                Label = LabelExecutable,
                Message = $"已在资源管理器中定位 {application.Name} 的可执行文件。"
            };
        }

        var installLocation = Normalize(application.InstallLocation);
        if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
        {
            return new AuxiliaryTarget
            {
                Kind = AuxiliaryTargetKind.InstallFolder,
                Path = installLocation,
                Label = LabelFolder,
                Message = $"已打开 {application.Name} 的安装目录。"
            };
        }

        return Unavailable(
            $"{application.Name} 没有可定位的安装位置或配置文件：记录的文件可能已被移动、卸载，或该应用由系统商店托管。");
    }

    private static AuxiliaryTarget Unavailable(string message) => new()
    {
        Kind = AuxiliaryTargetKind.None,
        Label = LabelExecutable,
        Message = message
    };

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return expanded.Length == 0 ? null : expanded;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

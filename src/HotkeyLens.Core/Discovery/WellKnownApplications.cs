using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Discovery;

/// <summary>
/// Daily-use desktop software that ordinary users most often run in the
/// background and whose global hotkeys are the usual source of key stealing.
/// Paths are only used to detect an existing installation with File.Exists;
/// nothing here launches, modifies or probes a running application.
/// </summary>
public sealed record WellKnownApplicationProfile
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> ProcessNames { get; init; }
    public required ApplicationCategory Category { get; init; }
    public IReadOnlyList<string> CandidatePaths { get; init; } = Array.Empty<string>();
    public string ResolutionGuide { get; init; } = string.Empty;
    public string SettingsHint { get; init; } = string.Empty;
}

public static class WellKnownApplications
{
    private const string GuideShareX =
        "ShareX → 左侧「任务设置」→「热键」→「热键设置…」→ 选中已绑定的全局热键点「移除」，或重新按键。ShareX 默认不启用全局热键，出现条目说明曾手动设置。";

    private static readonly IReadOnlyList<WellKnownApplicationProfile> KnownProfiles =
    [
        new()
        {
            Name = "微信",
            ProcessNames = ["Weixin.exe", "WeChat.exe"],
            Category = ApplicationCategory.Communication,
            CandidatePaths =
            [
                @"%ProgramFiles%\Tencent\WeChat\WeChat.exe",
                @"%ProgramFiles%\Tencent\Weixin\Weixin.exe",
                @"%ProgramFiles(x86)%\Tencent\WeChat\WeChat.exe"
            ],
            SettingsHint = "微信的本地配置为私有格式，HotkeyLens 只展示官方默认规则，不读取也不修改。"
        },
        new()
        {
            Name = "QQ",
            ProcessNames = ["QQ.exe"],
            Category = ApplicationCategory.Communication,
            CandidatePaths =
            [
                @"%ProgramFiles%\Tencent\QQNT\QQ.exe",
                @"%ProgramFiles%\Tencent\QQ\Bin\QQ.exe",
                @"%ProgramFiles(x86)%\Tencent\QQ\Bin\QQ.exe"
            ],
            SettingsHint = "QQ 的本地配置为私有格式，HotkeyLens 只展示官方默认规则，不读取也不修改。"
        },
        new()
        {
            Name = "网易云音乐",
            ProcessNames = ["cloudmusic.exe"],
            Category = ApplicationCategory.Media,
            CandidatePaths =
            [
                @"%ProgramFiles%\Netease\CloudMusic\cloudmusic.exe",
                @"%ProgramFiles%\NetEase\CloudMusic\cloudmusic.exe",
                @"%ProgramFiles(x86)%\Netease\CloudMusic\cloudmusic.exe"
            ]
        },
        new()
        {
            Name = "QQ音乐",
            ProcessNames = ["QQMusic.exe"],
            Category = ApplicationCategory.Media,
            CandidatePaths =
            [
                @"%ProgramFiles%\Tencent\QQMusic\QQMusic.exe",
                @"%ProgramFiles(x86)%\Tencent\QQMusic\QQMusic.exe"
            ]
        },
        new()
        {
            Name = "Snipaste",
            ProcessNames = ["Snipaste.exe"],
            Category = ApplicationCategory.Utility,
            CandidatePaths =
            [
                @"%ProgramFiles%\Snipaste\Snipaste.exe",
                @"%LOCALAPPDATA%\Programs\Snipaste\Snipaste.exe"
            ]
        },
        new()
        {
            Name = "ShareX",
            ProcessNames = ["ShareX.exe"],
            Category = ApplicationCategory.Utility,
            CandidatePaths =
            [
                @"%LOCALAPPDATA%\Programs\ShareX\ShareX.exe",
                @"%ProgramFiles%\ShareX\ShareX.exe"
            ],
            ResolutionGuide = GuideShareX,
            SettingsHint = "ShareX 的全局热键保存在应用自己的设置里；HotkeyLens 只定位位置，不修改。"
        }
    ];

    public static IReadOnlyList<WellKnownApplicationProfile> Profiles => KnownProfiles;

    public static WellKnownApplicationProfile? Match(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        return KnownProfiles.FirstOrDefault(profile =>
            profile.ProcessNames.Any(candidate =>
                string.Equals(candidate, processName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Returns one existing executable per known product. Missing paths are
    /// skipped instead of guessed, so a candidate only appears when the file is
    /// really on disk.
    /// </summary>
    public static IReadOnlyList<(WellKnownApplicationProfile Profile, string ExecutablePath)> FindInstallations()
    {
        var result = new List<(WellKnownApplicationProfile, string)>();
        foreach (var profile in KnownProfiles)
        {
            string? found = null;
            foreach (var candidate in profile.CandidatePaths)
            {
                try
                {
                    var expanded = Environment.ExpandEnvironmentVariables(candidate);
                    if (!string.IsNullOrWhiteSpace(expanded) && File.Exists(expanded))
                    {
                        found = expanded;
                        break;
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // A malformed candidate path must not stop discovery.
                }
            }

            if (found is not null)
            {
                result.Add((profile, found));
            }
        }

        return result;
    }
}

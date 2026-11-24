using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Discovery;

public static class ApplicationClassifier
{
    private static readonly (ApplicationCategory Category, string[] Keywords)[] Rules =
    [
        (ApplicationCategory.System, ["windows shell", "windows 系统", "system settings", "控制面板", "任务管理器"]),
        (ApplicationCategory.Development, ["visual studio", "vs code", "vscode", "jetbrains", "intellij", "pycharm", "webstorm", "goland", "clion", "phpstorm", "rubymine", "datagrip", "rider", "android studio", "git", "github", "docker", "postman", "insomnia", "terminal", "powershell", "command prompt", "python", "node.js", "unity", "unreal", "sublime", "vim", "emacs", "db browser", "sqlite", "developer"]),
        (ApplicationCategory.Browser, ["chrome", "edge", "firefox", "opera", "brave", "vivaldi", "browser", "浏览器"]),
        (ApplicationCategory.Communication, ["teams", "slack", "discord", "zoom", "skype", "telegram", "whatsapp", "wechat", "微信", "qq", "outlook", "thunderbird", "dingtalk", "钉钉", "feishu", "飞书", "mail", "邮件"]),
        (ApplicationCategory.Media, ["spotify", "vlc", "media player", "itunes", "obs studio", "audacity", "potplayer", "mpv", "foobar", "music", "video", "音乐", "视频", "netflix"]),
        (ApplicationCategory.Graphics, ["photoshop", "illustrator", "gimp", "inkscape", "blender", "figma", "canva", "paint", "snagit", "capture", "affinity", "krita", "graphics", "图像"]),
        (ApplicationCategory.Security, ["defender", "antivirus", "firewall", "bitdefender", "kaspersky", "malwarebytes", "password", "keepass", "bitwarden", "1password", "安全", "杀毒"]),
        (ApplicationCategory.Productivity, ["office", "word", "excel", "powerpoint", "onenote", "notion", "obsidian", "evernote", "acrobat", "pdf", "todoist", "trello", "notepad", "记事本", "explorer", "文件资源管理器", "calculator", "计算器", "calendar", "日历", "snipping", "截图", "settings", "设置"])
    ];

    public static ApplicationCategory Classify(string name, string? executablePath = null)
    {
        var evidence = $"{name} {executablePath}".ToLowerInvariant();
        foreach (var (category, keywords) in Rules)
        {
            if (keywords.Any(keyword => evidence.Contains(keyword, StringComparison.Ordinal)))
            {
                return category;
            }
        }

        return string.IsNullOrWhiteSpace(executablePath)
            ? ApplicationCategory.Other
            : ApplicationCategory.Utility;
    }
}
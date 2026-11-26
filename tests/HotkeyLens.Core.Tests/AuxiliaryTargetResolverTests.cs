using HotkeyLens.Core.Analysis;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

public sealed class AuxiliaryTargetResolverTests
{
    [Fact]
    public void ExistingSettingsFileWins()
    {
        using var directory = new TempDirectory();
        var settings = directory.File("config.ini");
        File.WriteAllText(settings, "x");
        var application = TestData.Application("snipaste", "Snipaste", directory.File("Snipaste.exe")) with
        {
            SettingsPath = settings
        };

        var target = AuxiliaryTargetResolver.Resolve(application);

        Assert.Equal(AuxiliaryTargetKind.ConfigurationFile, target.Kind);
        Assert.True(target.CanOpen);
        Assert.Equal("打开配置文件", target.Label);
        Assert.Equal(settings, target.Path);
    }

    [Fact]
    public void MissingSettingsFallsBackToExecutable()
    {
        using var directory = new TempDirectory();
        var executable = directory.File("Snipaste.exe");
        File.WriteAllText(executable, "x");
        var application = TestData.Application("snipaste", "Snipaste", executable) with
        {
            SettingsPath = directory.File("missing.ini")
        };

        var target = AuxiliaryTargetResolver.Resolve(application);

        Assert.Equal(AuxiliaryTargetKind.ExecutableFile, target.Kind);
        Assert.Equal("打开所在目录", target.Label);
    }

    [Fact]
    public void MissingExecutableFallsBackToInstallFolder()
    {
        using var directory = new TempDirectory();
        var application = TestData.Application("snipaste", "Snipaste", directory.File("gone.exe")) with
        {
            InstallLocation = directory.Path
        };

        var target = AuxiliaryTargetResolver.Resolve(application);

        Assert.Equal(AuxiliaryTargetKind.InstallFolder, target.Kind);
        Assert.Equal(directory.Path, target.Path);
        Assert.Equal("打开安装目录", target.Label);
    }

    [Fact]
    public void NothingKnownReturnsFriendlyUnavailable()
    {
        var application = TestData.Application("ghost", "Ghost");

        var target = AuxiliaryTargetResolver.Resolve(application);

        Assert.False(target.CanOpen);
        Assert.Equal(AuxiliaryTargetKind.None, target.Kind);
        Assert.Contains("Ghost", target.Message, StringComparison.Ordinal);
        Assert.Contains("没有可定位", target.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullApplicationDoesNotThrow()
    {
        var target = AuxiliaryTargetResolver.Resolve(null);

        Assert.False(target.CanOpen);
        Assert.False(string.IsNullOrWhiteSpace(target.Message));
    }

    [Fact]
    public void InvalidPathCharactersDegradeInsteadOfThrowing()
    {
        var application = TestData.Application("broken", "Broken", "C:\\bad\u0001path\\app.exe") with
        {
            SettingsPath = "C:\\bad\u0001settings.ini"
        };

        var target = AuxiliaryTargetResolver.Resolve(application);

        Assert.False(target.CanOpen);
        Assert.Equal(AuxiliaryTargetKind.None, target.Kind);
    }

    [Fact]
    public void SettingsPathThatIsADirectoryOpensAsFolder()
    {
        using var directory = new TempDirectory();
        var application = TestData.Application("code", "VS Code") with
        {
            SettingsPath = directory.Path
        };

        var target = AuxiliaryTargetResolver.Resolve(application);

        Assert.Equal(AuxiliaryTargetKind.InstallFolder, target.Kind);
        Assert.Equal(directory.Path, target.Path);
    }
}

using HotkeyLens.Core.Presentation;

namespace HotkeyLens.Core.Tests;

public sealed class RevealFeedbackTests
{
    [Theory]
    [InlineData(-1073741206, true)]
    [InlineData(-1073741819, true)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    [InlineData(null, false)]
    public void OnlyNegativeNtStatusCodesCountAsFailedLaunch(int? exitCode, bool expected)
    {
        Assert.Equal(expected, RevealFeedback.IsFailedExitCode(exitCode));
    }

    [Fact]
    public void FailureMessageShowsHexCodeAndTargetPath()
    {
        const string path = @"C:\Users\demo\AppData\Local\Google\Chrome\User Data\Default\Preferences";

        var message = RevealFeedback.Failure(path, unchecked((int)0xC0000142));

        Assert.Contains("无法打开资源管理器", message, StringComparison.Ordinal);
        Assert.Contains("0xC0000142", message, StringComparison.Ordinal);
        Assert.Contains(path, message, StringComparison.Ordinal);
        Assert.DoesNotContain("已在资源管理器", message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureWithoutExitCodeStillProvidesManualPath()
    {
        var message = RevealFeedback.Failure(@"D:\Apps\weixin.exe", null);

        Assert.Contains("无法打开资源管理器", message, StringComparison.Ordinal);
        Assert.Contains(@"D:\Apps\weixin.exe", message, StringComparison.Ordinal);
    }
}

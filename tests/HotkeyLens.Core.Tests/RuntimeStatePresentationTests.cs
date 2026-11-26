using HotkeyLens.Core.Presentation;

namespace HotkeyLens.Core.Tests;

public sealed class RuntimeStatePresentationTests
{
    [Theory]
    [InlineData(true, "正在运行")]
    [InlineData(false, "未检测到运行")]
    [InlineData(null, "状态未知")]
    public void LabelNeverClaimsNotRunningForFalse(bool? state, string expected)
    {
        Assert.Equal(expected, RuntimeStatePresentation.Label(state));
    }

    [Fact]
    public void NotRunningHintMentionsSecuritySoftwareInsteadOfAssertingClosed()
    {
        var hint = RuntimeStatePresentation.Hint("微信", false);

        Assert.Contains("没有检测到运行", hint, StringComparison.Ordinal);
        Assert.Contains("安全软件", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("当前未运行", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownHintExplainsFilteredProcessList()
    {
        var hint = RuntimeStatePresentation.Hint("微信", null);

        Assert.Contains("没有取得", hint, StringComparison.Ordinal);
        Assert.Contains("进程列表", hint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 2, 2, "运行状态未知")]
    [InlineData(1, 1, 2, "1 个正在运行，1 个状态未知")]
    [InlineData(0, 0, 2, "涉及软件均未检测到运行")]
    [InlineData(2, 0, 2, "涉及软件当前都在运行")]
    [InlineData(1, 0, 2, "1 / 2 个软件正在运行")]
    public void ParticipantsSummaryDistinguishesUnknownFromNotRunning(
        int running,
        int unknown,
        int total,
        string expected)
    {
        Assert.Equal(expected, RuntimeStatePresentation.ParticipantsSummary(running, unknown, total));
    }
}

using HotkeyLens.Core.Adapters;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

public sealed class ShortcutGestureTests
{
    [Theory]
    [InlineData("Ctrl+Shift+T", "Ctrl+Shift+T", 0x54)]
    [InlineData("shift+ctrl+k", "Ctrl+Shift+K", 0x4B)]
    [InlineData("Ctrl + Alt + Delete", "Ctrl+Alt+Delete", 0x2E)]
    [InlineData("Win+.", "Win+.", 0xBE)]
    [InlineData("Alt+F4", "Alt+F4", 0x73)]
    [InlineData("Ctrl++", "Ctrl++", 0xBB)]
    public void ParsesAndNormalizesCommonGestures(string input, string canonical, int virtualKey)
    {
        Assert.True(ShortcutGesture.TryParse(input, out var gesture));
        Assert.NotNull(gesture);
        Assert.Equal(canonical, gesture!.Canonical);
        Assert.Equal(virtualKey, gesture.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+L")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Shift")]
    public void RejectsMalformedGestures(string input)
    {
        Assert.False(ShortcutGesture.TryParse(input, out var gesture));
        Assert.Null(gesture);
    }

    [Fact]
    public void ModifierOrderIsStable()
    {
        var first = ShortcutGesture.Parse("Win+Shift+Alt+Ctrl+P");
        var second = ShortcutGesture.Parse("Ctrl+Alt+Shift+Win+P");

        Assert.Equal(second.Canonical, first.Canonical);
        Assert.Equal(second.SortKey, first.SortKey);
    }

    [Fact]
    public void FromVirtualKeyRejectsUnknownKey()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutGesture.FromVirtualKey(0xFFFF));
    }

    [Fact]
    public void StableIdIgnoresCaseAndWhitespace()
    {
        Assert.Equal(
            StableId.Create("sc", "A", "B"),
            StableId.Create("sc", " a ", "b"));
    }

    [Theory]
    [InlineData("Ctrl+K Ctrl+S", "Ctrl+K", "Ctrl+K Ctrl+S")]
    [InlineData("Ctrl+Shift+P", "Ctrl+Shift+P", null)]
    public void ParsesChordSequences(string raw, string first, string? sequence)
    {
        Assert.True(KeySequenceParser.TryParse(raw, out var gesture, out var parsedSequence));
        Assert.Equal(first, gesture!.Canonical);
        Assert.Equal(sequence, parsedSequence);
    }

    [Fact]
    public void SequenceDisplayDoesNotReplaceFirstGesture()
    {
        var app = TestData.Application("app", "示例");
        var entry = TestData.Entry(app, "Ctrl+K", sequence: "Ctrl+K Ctrl+S");

        Assert.True(entry.IsSequence);
        Assert.Equal("Ctrl+K Ctrl+S", entry.DisplayGesture);
        Assert.Equal("Ctrl+K", entry.Gesture.Canonical);
    }

    [Fact]
    public void EmptyVirtualKeyNameIsNotAParsedGesture()
    {
        Assert.True(ShortcutGesture.TryParse("F12", out var gesture));
        Assert.Equal(0x7B, gesture!.VirtualKey);
    }
}
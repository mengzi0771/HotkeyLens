using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

public sealed class ShortcutLinkReaderTests
{
    [Fact]
    public void ReadsModifiersAndVirtualKeyFromShellLinkHeader()
    {
        using var directory = new TempDirectory();
        var path = directory.File("test.lnk");
        var bytes = BuildHeader(virtualKey: 0x4B, flags: 0x03);
        File.WriteAllBytes(path, bytes);

        Assert.True(ShortcutLinkReader.TryReadHotkey(path, out var gesture));
        Assert.Equal("Ctrl+Shift+K", gesture!.Canonical);
    }

    [Fact]
    public void LinkWithoutHotkeyReturnsFalse()
    {
        using var directory = new TempDirectory();
        var path = directory.File("no-hotkey.lnk");
        File.WriteAllBytes(path, BuildHeader(virtualKey: 0, flags: 0));

        Assert.False(ShortcutLinkReader.TryReadHotkey(path, out var gesture));
        Assert.Null(gesture);
    }

    [Fact]
    public void InvalidHeaderReturnsFalse()
    {
        using var directory = new TempDirectory();
        var path = directory.File("bad.lnk");
        var bytes = BuildHeader(virtualKey: 0x4B, flags: 0);
        BitConverter.GetBytes(0xDEADBEEF).CopyTo(bytes, 0);
        File.WriteAllBytes(path, bytes);

        Assert.False(ShortcutLinkReader.TryReadHotkey(path, out _));
    }

    [Fact]
    public void TruncatedFileReturnsFalse()
    {
        using var directory = new TempDirectory();
        var path = directory.File("short.lnk");
        File.WriteAllBytes(path, new byte[10]);

        Assert.False(ShortcutLinkReader.TryReadHotkey(path, out _));
    }

    private static byte[] BuildHeader(byte virtualKey, byte flags)
    {
        var header = new byte[76];
        BitConverter.GetBytes(0x4C).CopyTo(header, 0);
        header[64] = virtualKey;
        header[65] = flags;
        return header;
    }
}
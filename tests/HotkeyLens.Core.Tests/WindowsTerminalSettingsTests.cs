using System.Text.Json;
using HotkeyLens.Core.Adapters;

namespace HotkeyLens.Core.Tests;

public sealed class WindowsTerminalSettingsTests
{
    [Fact]
    public void LegacyKeybindingIdIsAccepted()
    {
        using var document = JsonDocument.Parse(
            """{ "id": "Terminal.CopyToClipboard", "keys": "ctrl+c" }""");

        var command = WindowsTerminalSettingsAdapter.ReadCommand(document.RootElement);

        Assert.Equal("Terminal.CopyToClipboard", command);
        Assert.Equal("复制", WindowsTerminalSettingsAdapter.ResolveActionName(command!));
    }

    [Fact]
    public void ModernCommandObjectIsPreferredOverLegacyId()
    {
        using var document = JsonDocument.Parse(
            """{ "id": "legacy", "command": { "action": "copy" }, "keys": "ctrl+c" }""");

        var command = WindowsTerminalSettingsAdapter.ReadCommand(document.RootElement);

        Assert.Equal("copy", command);
        Assert.Equal("复制", WindowsTerminalSettingsAdapter.ResolveActionName(command!));
    }

    [Fact]
    public void MissingCommandAndIdIsIgnored()
    {
        using var document = JsonDocument.Parse("""{ "keys": "ctrl+c" }""");
        Assert.Null(WindowsTerminalSettingsAdapter.ReadCommand(document.RootElement));
    }
}
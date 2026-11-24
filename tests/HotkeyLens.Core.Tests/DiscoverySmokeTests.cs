using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Tests;

public sealed class DiscoverySmokeTests
{
    [Fact]
    public async Task DiscoversRealLocalApplicationsWithoutDuplicateExecutables()
    {
        var service = new ApplicationDiscoveryService();
        var result = await service.DiscoverAsync(new ScanOptions(), CancellationToken.None);

        Assert.NotEmpty(result.Applications);
        Assert.All(result.Applications, application =>
        {
            Assert.False(string.IsNullOrWhiteSpace(application.Id));
            Assert.False(string.IsNullOrWhiteSpace(application.Name));
        });

        var duplicatePaths = result.Applications
            .Where(application => !string.IsNullOrWhiteSpace(application.ExecutablePath))
            .GroupBy(
                application => StableId.NormalizePath(application.ExecutablePath),
                StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToList();
        Assert.Empty(duplicatePaths);

        Assert.Contains(result.Applications, application =>
            string.Equals(application.ProcessName, "explorer.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumeratesShortcutFilesInNestedDirectories()
    {
        using var directory = new TempDirectory();
        var nested = System.IO.Path.Combine(directory.Path, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "one.lnk"), "x");
        File.WriteAllText(System.IO.Path.Combine(nested, "two.lnk"), "x");
        File.WriteAllText(System.IO.Path.Combine(nested, "ignored.txt"), "x");

        var files = ShortcutLinkReader.EnumerateShortcutFiles(directory.Path);

        Assert.Equal(2, files.Count);
        Assert.All(files, path => Assert.EndsWith(".lnk", path, StringComparison.OrdinalIgnoreCase));
    }
}
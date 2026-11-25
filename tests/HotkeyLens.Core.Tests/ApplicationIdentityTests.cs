using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Models;
using AppIdentity = HotkeyLens.Core.Discovery.ApplicationIdentity;

namespace HotkeyLens.Core.Tests;

public sealed class ApplicationIdentityTests
{
    [Fact]
    public void EdgeVersionFolderIsFoldedToTheChannelRoot()
    {
        using var directory = new TempDirectory();
        var applicationRoot = Directory.CreateDirectory(
            Path.Combine(directory.Path, "Microsoft", "Edge", "Application")).FullName;
        var rootExecutable = Path.Combine(applicationRoot, "msedge.exe");
        File.WriteAllText(rootExecutable, "root");
        var versionDirectory = Directory.CreateDirectory(
            Path.Combine(applicationRoot, "158.0.4370.33")).FullName;
        var versionExecutable = Path.Combine(versionDirectory, "msedge.exe");
        File.WriteAllText(versionExecutable, "version");

        var canonical = AppIdentity.CanonicalizeExecutablePath(versionExecutable);

        Assert.Equal(
            StableId.NormalizePath(rootExecutable),
            StableId.NormalizePath(canonical));
    }

    [Fact]
    public void EdgeStableAndBetaStayDistinctApplications()
    {
        using var directory = new TempDirectory();
        var stable = CreateChromiumInstall(directory.Path, "Edge", "158.0.4370.33", "msedge.exe");
        var beta = CreateChromiumInstall(directory.Path, "Edge Beta", "158.0.4370.34", "msedge.exe");

        var applications = ApplicationMerger.Merge(
        [
            Candidate("Microsoft Edge", stable.VersionExecutable),
            Candidate("Microsoft Edge", stable.RootExecutable),
            Candidate("Microsoft Edge Beta", beta.VersionExecutable),
            Candidate("Microsoft Edge Beta", beta.RootExecutable)
        ]);

        Assert.Equal(2, applications.Count);
        Assert.Equal(2, applications.Select(app => app.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(applications, app =>
            Assert.Equal("msedge.exe", app.ProcessName, StringComparer.OrdinalIgnoreCase));
        Assert.Contains(applications, app => app.Name.Contains("Beta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EdgeVersionFolderWithoutARootExecutableIsNotInvented()
    {
        using var directory = new TempDirectory();
        var versionDirectory = Directory.CreateDirectory(
            Path.Combine(directory.Path, "Edge", "Application", "158.0.4370.33")).FullName;
        var versionExecutable = Path.Combine(versionDirectory, "msedge.exe");
        File.WriteAllText(versionExecutable, "version");

        var canonical = AppIdentity.CanonicalizeExecutablePath(versionExecutable);

        Assert.Equal(
            StableId.NormalizePath(versionExecutable),
            StableId.NormalizePath(canonical));
    }

    [Fact]
    public void WindowsTerminalShimResolvesToThePackageExecutable()
    {
        using var directory = new TempDirectory();
        var package = Directory.CreateDirectory(Path.Combine(
            directory.Path,
            "WindowsApps",
            "Microsoft.WindowsTerminal_1.25.3131.0_x64__8wekyb3d8bbwe")).FullName;
        var shim = Path.Combine(package, "wt.exe");
        var terminal = Path.Combine(package, "WindowsTerminal.exe");
        File.WriteAllText(shim, "shim");
        File.WriteAllText(terminal, "terminal");

        var canonical = AppIdentity.CanonicalizeExecutablePath(shim);

        Assert.Equal(StableId.NormalizePath(terminal), StableId.NormalizePath(canonical));
        Assert.Equal(
            "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
            AppIdentity.GetPackageFamilyName(shim));
        Assert.Equal("Windows Terminal", AppIdentity.GetPackageDisplayName(
            AppIdentity.GetPackageFamilyName(shim)));
    }

    [Fact]
    public void WindowsTerminalPackageIdentitySurvivesMergingAndCatalogMatching()
    {
        using var directory = new TempDirectory();
        var package = Directory.CreateDirectory(Path.Combine(
            directory.Path,
            "WindowsApps",
            "Microsoft.WindowsTerminal_1.25.3131.0_x64__8wekyb3d8bbwe")).FullName;
        var shim = Path.Combine(package, "wt.exe");
        var terminal = Path.Combine(package, "WindowsTerminal.exe");
        File.WriteAllText(shim, "shim");
        File.WriteAllText(terminal, "terminal");

        var merged = Assert.Single(ApplicationMerger.Merge(
        [
            Candidate("Windows Terminal Launcher Shim", shim),
            Candidate("Windows Terminal", terminal)
        ]));

        Assert.Equal("Windows Terminal", merged.Name);
        Assert.Equal("Microsoft.WindowsTerminal_8wekyb3d8bbwe", merged.PackageFamilyName);
        Assert.Equal("WindowsTerminal.exe", merged.ProcessName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            StableId.Create("app", "package", "Microsoft.WindowsTerminal_8wekyb3d8bbwe"),
            merged.Id);

        var catalog = HotkeyLens.Core.Catalog.ShortcutCatalog.Default;
        var match = Assert.Single(catalog.Match(merged));
        Assert.Equal("windows-terminal", match.Key);
    }

    [Fact]
    public void PackageFamilyNameAloneCanMatchTheCatalog()
    {
        var application = new DiscoveredApplication
        {
            Id = StableId.Create("app", "package", "Microsoft.WindowsTerminal_8wekyb3d8bbwe"),
            Name = "Windows Terminal",
            PackageFamilyName = "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
            SourceKind = ApplicationSourceKind.Package
        };

        var match = Assert.Single(HotkeyLens.Core.Catalog.ShortcutCatalog.Default.Match(application));
        Assert.Equal("windows-terminal", match.Key);
    }

    [Fact]
    public void PackageFamilyNameIsNullOutsideWindowsApps()
    {
        Assert.Null(AppIdentity.GetPackageFamilyName(@"C:\Tools\example.exe"));
        Assert.Null(AppIdentity.ParsePackageFamilyName("not-a-package"));
        Assert.Null(AppIdentity.ParsePackageFamilyName(
            "Microsoft.WindowsTerminal_1.25.3131.0_x64__not-a-publisher"));
    }

    private static (string RootExecutable, string VersionExecutable) CreateChromiumInstall(
        string root,
        string product,
        string version,
        string executableName)
    {
        var application = Directory.CreateDirectory(
            Path.Combine(root, product, "Application")).FullName;
        var rootExecutable = Path.Combine(application, executableName);
        File.WriteAllText(rootExecutable, "root");
        var versionDirectory = Directory.CreateDirectory(Path.Combine(application, version)).FullName;
        var versionExecutable = Path.Combine(versionDirectory, executableName);
        File.WriteAllText(versionExecutable, "version");
        return (rootExecutable, versionExecutable);
    }

    private static ApplicationCandidate Candidate(string name, string executable) => new()
    {
        Name = name,
        ExecutablePath = executable,
        SourceKind = ApplicationSourceKind.StartMenu,
        SourceDetail = "test"
    };
}
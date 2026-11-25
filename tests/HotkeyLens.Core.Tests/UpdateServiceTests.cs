using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Updates;

namespace HotkeyLens.Core.Tests;

public sealed class UpdateServiceTests
{
    private const string SetupName = "HotkeyLens-Setup.exe";
    private const string PortableName = "HotkeyLens-Portable-win-x64.zip";
    private const string SumsName = "SHA256SUMS.txt";

    [Theory]
    [InlineData("1.10.0", "1.9.9", 1)]
    [InlineData("v1.1.0", "1.1.0", 0)]
    [InlineData("1.1.0+abc", "1.1.0", 0)]
    [InlineData("1.1.0-beta", "1.1.0", 0)]
    [InlineData("1.0.9", "1.1.0", -1)]
    public void SemanticVersionComparesNumerically(string left, string right, int expected)
    {
        Assert.True(SemanticVersion.TryParse(left, out var a));
        Assert.True(SemanticVersion.TryParse(right, out var b));
        Assert.Equal(expected, Math.Sign(a.CompareTo(b)));
    }

    [Fact]
    public async Task AutomaticCheckIsSkippedWhenDisabled()
    {
        var store = new MemoryPreferencesStore(new UserPreferences { AutoCheckUpdates = false });
        var handler = new StubHandler(_ => throw new InvalidOperationException("HTTP must not be called"));
        var service = Service(store, handler);

        var result = await service.CheckAsync(manual: false);

        Assert.Equal(UpdateCheckStatus.Disabled, result.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task AutomaticCheckIsThrottledWithinTwentyFourHours()
    {
        var store = new MemoryPreferencesStore(new UserPreferences
        {
            AutoCheckUpdates = true,
            LastUpdateCheckUtc = DateTimeOffset.UtcNow.AddHours(-2)
        });
        var handler = new StubHandler(_ => throw new InvalidOperationException("HTTP must not be called"));
        var service = Service(store, handler);

        var result = await service.CheckAsync(manual: false);

        Assert.Equal(UpdateCheckStatus.Throttled, result.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ManualCheckBypassesThrottleAndFindsNewVersion()
    {
        var store = new MemoryPreferencesStore(new UserPreferences
        {
            AutoCheckUpdates = false,
            LastUpdateCheckUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        var handler = new StubHandler(_ => JsonResponse(ReleaseListJson("1.2.0")));
        var service = Service(store, handler);

        var result = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.2.0", result.LatestVersion?.ToString());
        Assert.True(store.Preferences.LastUpdateCheckUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task UpToDateReleaseIsReported()
    {
        var service = Service(new MemoryPreferencesStore(), new StubHandler(_ => JsonResponse(ReleaseListJson("1.1.0"))));

        var result = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public async Task SkippedVersionIsNotOffered()
    {
        var store = new MemoryPreferencesStore(new UserPreferences { SkippedVersion = "1.2.0" });
        var service = Service(store, new StubHandler(_ => JsonResponse(ReleaseListJson("1.2.0"))));

        var result = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.Skipped, result.Status);
    }

    [Fact]
    public async Task RateLimitIsReported()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var service = Service(new MemoryPreferencesStore(), handler);

        var result = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.RateLimited, result.Status);
    }

    [Fact]
    public async Task NetworkFailureIsReportedWithoutThrowing()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        var service = Service(new MemoryPreferencesStore(), handler);

        var result = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.NetworkError, result.Status);
    }

    [Fact]
    public async Task DraftAndPrereleaseAreIgnored()
    {
        var json = "[" + ReleaseJson("1.1.0") + "," +
                   ReleaseJson("1.2.0", prerelease: true) + "," +
                   ReleaseJson("1.3.0", draft: true) + "]";
        Assert.Contains("\"tag_name\":\"v1.1.0\"", json);
        var service = Service(new MemoryPreferencesStore(), new StubHandler(_ => JsonResponse(json)));

        var result = await service.CheckAsync(manual: true);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Equal("1.1.0", result.LatestVersion?.ToString());
    }

    [Fact]
    public async Task SuccessfulCheckPersistsTimestampAndSkipClearsIt()
    {
        var store = new MemoryPreferencesStore();
        var service = Service(store, new StubHandler(_ => JsonResponse(ReleaseListJson("1.2.0"))));

        await service.CheckAsync(manual: true);
        Assert.NotNull(store.Preferences.LastUpdateCheckUtc);

        await service.SkipVersionAsync("1.2.0");
        Assert.Equal("1.2.0", await service.GetSkippedVersionAsync());

        await service.ClearSkippedVersionAsync();
        Assert.Null(await service.GetSkippedVersionAsync());
    }

    [Fact]
    public void OfficialAssetUrlValidationRejectsForeignSources()
    {
        Assert.True(GitHubReleaseClient.IsOfficialAssetUrl(
            "https://github.com/mengzi0771/HotkeyLens/releases/download/v1.1.0/HotkeyLens-Setup.exe"));
        Assert.False(GitHubReleaseClient.IsOfficialAssetUrl(
            "http://github.com/mengzi0771/HotkeyLens/releases/download/v1.1.0/HotkeyLens-Setup.exe"));
        Assert.False(GitHubReleaseClient.IsOfficialAssetUrl(
            "https://example.com/mengzi0771/HotkeyLens/releases/download/v1.1.0/HotkeyLens-Setup.exe"));
        Assert.False(GitHubReleaseClient.IsOfficialAssetUrl(
            "https://github.com/other/HotkeyLens/releases/download/v1.1.0/HotkeyLens-Setup.exe"));
    }

    [Fact]
    public void ReleaseFindsAssetCaseInsensitively()
    {
        var release = Release("1.2.0", (SetupName, Url("1.2.0", SetupName)));
        Assert.NotNull(release.FindAsset("hotkeylens-setup.exe"));
        Assert.Null(release.FindAsset(PortableName));
    }

    [Fact]
    public async Task DownloadVerifiesSha256AndLeavesTheCurrentVersionAlone()
    {
        var bytes = Encoding.UTF8.GetBytes("verified setup payload");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith(SumsName, StringComparison.Ordinal))
            {
                return TextResponse($"{hash}  {SetupName}\n");
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        var directory = CreateTemporaryDirectory();
        try
        {
            var release = Release(
                "1.2.0",
                (SetupName, Url("1.2.0", SetupName)),
                (SumsName, Url("1.2.0", SumsName)));
            var download = new UpdateDownloadService(new HttpClient(handler));

            var path = await download.DownloadVerifiedAsync(release, SetupName, directory);

            Assert.True(File.Exists(path));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadRejectsHashMismatchAndDeletesPartialFile()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith(SumsName, StringComparison.Ordinal)
                ? TextResponse(new string('0', 64) + "  " + SetupName + "\n")
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("tampered payload"))
                });
        var directory = CreateTemporaryDirectory();
        try
        {
            var release = Release(
                "1.2.0",
                (SetupName, Url("1.2.0", SetupName)),
                (SumsName, Url("1.2.0", SumsName)));
            var download = new UpdateDownloadService(new HttpClient(handler));

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                download.DownloadVerifiedAsync(release, SetupName, directory));

            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadRejectsForeignAssetWithoutHttpRequest()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("HTTP must not be called"));
        var release = Release(
            "1.2.0",
            (SetupName, "https://example.com/HotkeyLens-Setup.exe"),
            (SumsName, Url("1.2.0", SumsName)));
        var download = new UpdateDownloadService(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            download.DownloadVerifiedAsync(release, SetupName, CreateTemporaryDirectory()));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CancelledCheckDoesNotBecomeAnUpdate()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = Service(new MemoryPreferencesStore(), new StubHandler(_ => JsonResponse(ReleaseListJson("1.2.0"))));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckAsync(manual: true, cancellation.Token));
    }

    private static UpdateService Service(MemoryPreferencesStore store, StubHandler handler)
        => new(new GitHubReleaseClient(new HttpClient(handler)), store, "1.1.0");

    private static string ReleaseListJson(string version, bool draft = false, bool prerelease = false)
        => "[" + ReleaseJson(version, draft, prerelease) + "]";

    private static UpdateRelease Release(string version, params (string Name, string Url)[] assets)
        => new(
            SemanticVersion.Parse(version),
            "v" + version,
            "HotkeyLens v" + version,
            new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero),
            "测试更新说明",
            $"https://github.com/mengzi0771/HotkeyLens/releases/tag/v{version}",
            assets.Select(asset => new UpdateAsset(asset.Name, asset.Url, 0)).ToArray());

    private static string Url(string version, string name)
        => $"https://github.com/mengzi0771/HotkeyLens/releases/download/v{version}/{name}";

    private static string ReleaseJson(string version, bool draft = false, bool prerelease = false)
    {
        var payload = new
        {
            tag_name = "v" + version,
            name = "HotkeyLens v" + version,
            draft,
            prerelease,
            published_at = "2026-11-25T08:00:00Z",
            body = "测试更新说明",
            html_url = $"https://github.com/mengzi0771/HotkeyLens/releases/tag/v{version}",
            assets = new[]
            {
                new { name = SetupName, browser_download_url = Url(version, SetupName), size = 1024 },
                new { name = PortableName, browser_download_url = Url(version, PortableName), size = 2048 },
                new { name = SumsName, browser_download_url = Url(version, SumsName), size = 128 }
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TextResponse(string text)
        => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "HotkeyLens-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class MemoryPreferencesStore(UserPreferences? initial = null) : IUserPreferencesStore
    {
        public UserPreferences Preferences { get; private set; } = initial ?? new UserPreferences();
        public string PreferencesPath => "memory://preferences.json";

        public Task<UserPreferences> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Preferences);

        public Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
        {
            Preferences = preferences;
            return Task.CompletedTask;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(responder(request));
        }
    }
}
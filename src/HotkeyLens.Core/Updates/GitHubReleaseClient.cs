using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HotkeyLens.Core.Updates;

/// <summary>
/// Reads the public GitHub Releases API. No credentials are persisted or required.
/// Draft and pre-release entries are ignored, and only official release asset URLs
/// under the project's releases/download path are accepted.
/// </summary>
public sealed class GitHubReleaseClient
{
    public const string ApiUrl =
        "https://api.github.com/repos/mengzi0771/HotkeyLens/releases?per_page=100";

    public const string ReleaseDownloadPrefix =
        "https://github.com/mengzi0771/HotkeyLens/releases/download/";

    public const string RepositoryUrl = "https://github.com/mengzi0771/HotkeyLens";

    private readonly HttpClient _http;

    public GitHubReleaseClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? CreateClient();
    }

    public async Task<ReleaseLookupResult> FindLatestStableAsync(CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(ApiUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new ReleaseLookupResult(
                ReleaseLookupStatus.NetworkError,
                null,
                $"无法连接 GitHub Releases：{exception.Message}");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                return new ReleaseLookupResult(
                    ReleaseLookupStatus.RateLimited,
                    null,
                    "GitHub API 请求受到限流，请稍后再试。");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ReleaseLookupResult(
                    ReleaseLookupStatus.NoStableRelease,
                    null,
                    "GitHub 上没有找到 HotkeyLens 的正式 Release。");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ReleaseLookupResult(
                    ReleaseLookupStatus.InvalidResponse,
                    null,
                    $"GitHub API 返回了异常状态：{(int)response.StatusCode}。");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return ParseReleases(document.RootElement);
            }
            catch (JsonException exception)
            {
                return new ReleaseLookupResult(
                    ReleaseLookupStatus.InvalidResponse,
                    null,
                    $"无法解析 GitHub Release 响应：{exception.Message}");
            }
        }
    }

    private static ReleaseLookupResult ParseReleases(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return new ReleaseLookupResult(
                ReleaseLookupStatus.InvalidResponse,
                null,
                "GitHub Release 响应格式不正确。");
        }

        UpdateRelease? latest = null;
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (GetBoolean(item, "draft") || GetBoolean(item, "prerelease"))
            {
                continue;
            }

            var tag = GetString(item, "tag_name");
            if (!SemanticVersion.TryParse(tag, out var version))
            {
                continue;
            }

            if (latest is not null && version.CompareTo(latest.Version) <= 0)
            {
                continue;
            }

            var assets = new List<UpdateAsset>();
            if (item.TryGetProperty("assets", out var assetArray) && assetArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetArray.EnumerateArray())
                {
                    var name = GetString(asset, "name");
                    var url = GetString(asset, "browser_download_url");
                    if (string.IsNullOrWhiteSpace(name) ||
                        string.IsNullOrWhiteSpace(url) ||
                        !IsOfficialAssetUrl(url))
                    {
                        continue;
                    }

                    var size = asset.TryGetProperty("size", out var sizeElement) &&
                               sizeElement.TryGetInt64(out var parsedSize)
                        ? parsedSize
                        : 0;
                    assets.Add(new UpdateAsset(name, url, size));
                }
            }

            DateTimeOffset? publishedAt = null;
            if (DateTimeOffset.TryParse(GetString(item, "published_at"), out var parsed))
            {
                publishedAt = parsed;
            }

            latest = new UpdateRelease(
                version,
                tag ?? version.ToString(),
                GetString(item, "name") ?? tag ?? version.ToString(),
                publishedAt,
                GetString(item, "body") ?? string.Empty,
                GetString(item, "html_url") ?? RepositoryUrl + "/releases",
                assets);
        }

        return latest is null
            ? new ReleaseLookupResult(
                ReleaseLookupStatus.NoStableRelease,
                null,
                "当前没有可用的正式版本。")
            : new ReleaseLookupResult(ReleaseLookupStatus.Found, latest, string.Empty);
    }

    public static bool IsOfficialAssetUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps &&
               uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.StartsWith(
                   "/mengzi0771/HotkeyLens/releases/download/",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBoolean(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) &&
           value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
           value.GetBoolean();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("HotkeyLens", "1.1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
using System.Net.Http;
using System.Security.Cryptography;

namespace HotkeyLens.Core.Updates;

/// <summary>
/// Downloads a single official GitHub release asset over HTTPS and verifies it
/// against SHA256SUMS.txt before the file is handed to the installer or user.
/// The running application is never overwritten here.
/// </summary>
public sealed class UpdateDownloadService
{
    public const string ChecksumsAssetName = "SHA256SUMS.txt";

    private readonly HttpClient _http;
    private readonly string _checksumsAssetName;

    public UpdateDownloadService(HttpClient? httpClient = null, string checksumsAssetName = ChecksumsAssetName)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _checksumsAssetName = checksumsAssetName;
    }

    public async Task<string> DownloadVerifiedAsync(
        UpdateRelease release,
        string assetName,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var asset = release.FindAsset(assetName)
                    ?? throw new InvalidOperationException($"Release 中缺少文件 {assetName}。");
        if (!GitHubReleaseClient.IsOfficialAssetUrl(asset.DownloadUrl))
        {
            throw new InvalidOperationException("下载地址不属于官方 GitHub Release 资产。");
        }

        var sumsAsset = release.FindAsset(_checksumsAssetName)
                        ?? throw new InvalidOperationException($"Release 中缺少 {_checksumsAssetName}，无法校验下载。");
        var checksums = await ReadChecksumsAsync(sumsAsset.DownloadUrl, cancellationToken)
            .ConfigureAwait(false);
        if (!checksums.TryGetValue(asset.Name, out var expectedHash))
        {
            throw new InvalidDataException($"SHA256SUMS.txt 中没有 {asset.Name} 的校验值。");
        }

        Directory.CreateDirectory(destinationDirectory);
        var safeName = Path.GetFileName(asset.Name);
        var targetPath = Path.Combine(destinationDirectory, safeName);
        var temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            var actualHash = await DownloadToFileAsync(
                asset.DownloadUrl,
                temporaryPath,
                progress,
                cancellationToken).ConfigureAwait(false);

            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"SHA256 校验失败。期望 {expectedHash}，实际 {actualHash}。已拒绝执行该文件。");
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
            return targetPath;
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private async Task<Dictionary<string, string>> ReadChecksumsAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (!GitHubReleaseClient.IsOfficialAssetUrl(url))
        {
            throw new InvalidOperationException("校验文件地址不属于官方 GitHub Release 资产。");
        }

        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 66)
            {
                continue;
            }

            var hash = trimmed[..64];
            if (!hash.All(Uri.IsHexDigit))
            {
                continue;
            }

            var name = trimmed[64..].TrimStart(' ', '*');
            if (name.Length > 0)
            {
                result[Path.GetFileName(name)] = hash;
            }
        }

        return result;
    }

    private async Task<string> DownloadToFileAsync(
        string url,
        string path,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);
            received += read;
            if (total is > 0)
            {
                progress?.Report(Math.Clamp(received * 100d / total.Value, 0, 100));
            }
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(100);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using HotkeyLens.App.Infrastructure;
using HotkeyLens.App.Services;
using HotkeyLens.Core.Updates;

namespace HotkeyLens.App.ViewModels;

public sealed class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _service;
    private readonly UpdateDownloadService _downloadService;
    private readonly string _updatesDirectory;
    private readonly InstallationMode _installationMode;

    private UpdateRelease? _release;
    private bool _isChecking;
    private bool _isDownloading;
    private double _downloadProgress;
    private string _statusMessage = string.Empty;
    private string _lastCheckedText = "尚未检查";
    private string? _downloadedPath;
    private string? _skippedVersion;

    public UpdateViewModel(
        UpdateService service,
        UpdateDownloadService downloadService,
        string updatesDirectory,
        InstallationMode installationMode)
    {
        _service = service;
        _downloadService = downloadService;
        _updatesDirectory = updatesDirectory;
        _installationMode = installationMode;

        CheckCommand = new AsyncRelayCommand(() => CheckAsync(manual: true), () => !IsBusy);
        DownloadCommand = new AsyncRelayCommand(DownloadAsync, () => HasUpdate && !IsBusy);
        SkipVersionCommand = new AsyncRelayCommand(SkipAsync, () => _release is not null && !IsBusy);
        RemindLaterCommand = new RelayCommand(() => CloseRequested?.Invoke());
        OpenReleasePageCommand = new RelayCommand(OpenReleasePage);
        OpenDownloadFolderCommand = new RelayCommand(OpenDownloadFolder, () => _downloadedPath is not null);
        ClearSkippedVersionCommand = new AsyncRelayCommand(
            ClearSkippedVersionAsync,
            () => HasSkippedVersion && !IsBusy);
    }

    public event Action? UpdateAvailable;
    public event Action? CloseRequested;

    public string CurrentVersionText => _service.CurrentVersionText;
    public string ModeLabel => _installationMode == InstallationMode.Installed ? "安装版" : "便携版";
    public bool IsPortable => _installationMode == InstallationMode.Portable;
    public string UpdatesDirectory => _updatesDirectory;

    public bool HasUpdate => _release is not null;
    public bool IsBusy => IsChecking || IsDownloading;

    public string LatestVersionText => _release?.Version.ToString() ?? "—";
    public string ReleaseName => _release?.Name ?? string.Empty;

    public string PublishedText => _release?.PublishedAt is { } published
        ? $"发布时间：{published.ToLocalTime():yyyy-MM-dd HH:mm}"
        : "发布时间：未提供";

    public string Notes => string.IsNullOrWhiteSpace(_release?.Notes)
        ? "该版本没有提供更新说明。"
        : _release.Notes.Trim();

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
            {
                RaiseBusyState();
            }
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                RaiseBusyState();
            }
        }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        private set => SetProperty(ref _downloadProgress, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string LastCheckedText
    {
        get => _lastCheckedText;
        private set => SetProperty(ref _lastCheckedText, value);
    }

    public string SkippedVersionText => string.IsNullOrWhiteSpace(_skippedVersion) ? "无" : _skippedVersion;
    public bool HasSkippedVersion => !string.IsNullOrWhiteSpace(_skippedVersion);

    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand DownloadCommand { get; }
    public AsyncRelayCommand SkipVersionCommand { get; }
    public AsyncRelayCommand ClearSkippedVersionCommand { get; }
    public RelayCommand RemindLaterCommand { get; }
    public RelayCommand OpenReleasePageCommand { get; }
    public RelayCommand OpenDownloadFolderCommand { get; }

    public async Task CheckOnStartupAsync()
        => await CheckAsync(manual: false);

    public async Task RefreshPreferencesAsync()
    {
        _skippedVersion = await _service.GetSkippedVersionAsync();
        OnPropertiesChanged(nameof(SkippedVersionText), nameof(HasSkippedVersion));
        ClearSkippedVersionCommand.RaiseCanExecuteChanged();
    }

    public async Task CheckAsync(bool manual)
    {
        if (IsBusy)
        {
            return;
        }

        IsChecking = true;
        StatusMessage = manual ? "正在检查 GitHub 上的正式版本…" : string.Empty;
        try
        {
            var result = await _service.CheckAsync(manual);
            LastCheckedText = (result.LastCheckedUtc ?? DateTimeOffset.UtcNow)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm");

            if (result.HasUpdate && result.Release is not null)
            {
                SetRelease(result.Release);
                StatusMessage = result.Message;
                UpdateAvailable?.Invoke();
            }
            else
            {
                SetRelease(null);
                _downloadedPath = null;
                if (manual || result.Status is not (UpdateCheckStatus.Disabled or UpdateCheckStatus.Throttled))
                {
                    StatusMessage = result.Message;
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException
                                           or UnauthorizedAccessException or InvalidOperationException
                                           or FormatException)
        {
            SetRelease(null);
            StatusMessage = $"检查更新失败：{exception.Message}";
        }
        finally
        {
            IsChecking = false;
            RaiseCommandStates();
        }
    }

    private async Task DownloadAsync()
    {
        if (_release is null || IsBusy)
        {
            return;
        }

        var assetName = _installationMode == InstallationMode.Installed
            ? "HotkeyLens-Setup.exe"
            : "HotkeyLens-Portable-win-x64.zip";
        IsDownloading = true;
        DownloadProgress = 0;
        StatusMessage = "正在下载并校验更新包…";
        try
        {
            var progress = new Progress<double>(value => DownloadProgress = value);
            var path = await _downloadService.DownloadVerifiedAsync(
                _release,
                assetName,
                _updatesDirectory,
                progress);
            _downloadedPath = path;
            DownloadProgress = 100;

            if (_installationMode == InstallationMode.Installed)
            {
                var installDirectory = InstallationModeDetector.GetInstallDirectory();
                var writeError = ProbeInstallDirectory(installDirectory);
                if (writeError is not null)
                {
                    StatusMessage = $"安装目录不可写，未启动安装：{writeError}";
                    MessageBox.Show(
                        Application.Current?.MainWindow,
                        $"HotkeyLens 未启动安装程序，当前版本和用户数据都没有变化。\n\n" +
                        $"安装目录：{installDirectory}\n" +
                        $"写入检查：{writeError}\n\n" +
                        "这通常由目录权限或安全软件主动防御造成。请允许安装程序写入该目录后重试；" +
                        "不要为了安装而关闭安全软件。也可以稍后手动运行已下载的安装包。",
                        "HotkeyLens · 无法准备升级",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var confirmed = MessageBox.Show(
                    Application.Current?.MainWindow,
                    $"安装包已下载并通过 SHA256 校验。\n\n{path}\n\n现在将关闭 HotkeyLens 并启动安装程序完成覆盖升级；" +
                    "用户数据和设置不会被删除。如果取消安装，可以稍后重新打开 HotkeyLens。\n\n是否继续？",
                    "HotkeyLens · 安装更新",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question,
                    MessageBoxResult.OK) == MessageBoxResult.OK;
                if (!confirmed)
                {
                    StatusMessage = "已取消安装。更新包保留在本地，可稍后重新检查。";
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo(path, "/NORESTART")
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory
                    });
                }
                catch (System.ComponentModel.Win32Exception exception)
                {
                    StatusMessage = $"无法启动安装程序，当前版本不受影响：{exception.Message}";
                    return;
                }

                StatusMessage = "正在启动安装程序…";
                Application.Current?.Shutdown();
            }
            else
            {
                StatusMessage =
                    $"便携版更新包已下载并校验：{path}\n请关闭 HotkeyLens，将压缩包解压覆盖到当前程序目录；" +
                    "不要删除 %LOCALAPPDATA%\\HotkeyLens 中的数据。";
                OpenDownloadFolder();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException
                                           or UnauthorizedAccessException or InvalidOperationException
                                           or TaskCanceledException)
        {
            StatusMessage = $"下载或校验失败，当前版本不受影响：{exception.Message}";
        }
        finally
        {
            IsDownloading = false;
            RaiseCommandStates();
        }
    }

    private async Task SkipAsync()
    {
        if (_release is null)
        {
            return;
        }

        try
        {
            await _service.SkipVersionAsync(_release.Version.ToString());
            _skippedVersion = _release.Version.ToString();
            OnPropertiesChanged(nameof(SkippedVersionText), nameof(HasSkippedVersion));
            ClearSkippedVersionCommand.RaiseCanExecuteChanged();
            StatusMessage = $"已跳过版本 {_release.Version}，该版本不会再提示。";
            CloseRequested?.Invoke();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusMessage = $"无法保存跳过设置：{exception.Message}";
        }
    }

    private async Task ClearSkippedVersionAsync()
    {
        try
        {
            await _service.ClearSkippedVersionAsync();
            _skippedVersion = null;
            OnPropertiesChanged(nameof(SkippedVersionText), nameof(HasSkippedVersion));
            StatusMessage = "已清除跳过版本记录。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusMessage = $"无法清除跳过记录：{exception.Message}";
        }
        finally
        {
            ClearSkippedVersionCommand.RaiseCanExecuteChanged();
        }
    }

    private void OpenReleasePage()
    {
        var url = _release?.HtmlUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            url = GitHubReleaseClient.RepositoryUrl + "/releases";
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OpenDownloadFolder()
    {
        if (_downloadedPath is null || !File.Exists(_downloadedPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_downloadedPath}\"")
        {
            UseShellExecute = true
        });
    }

    private static string? ProbeInstallDirectory(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".hotkeylens-write-{Guid.NewGuid():N}.tmp");
            using (new FileStream(
                       probe,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       1,
                       FileOptions.DeleteOnClose))
            {
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                           or System.Security.SecurityException or NotSupportedException
                                           or ArgumentException)
        {
            return exception.Message;
        }
    }

    private void SetRelease(UpdateRelease? release)
    {
        _release = release;
        OnPropertiesChanged(
            nameof(HasUpdate),
            nameof(LatestVersionText),
            nameof(ReleaseName),
            nameof(PublishedText),
            nameof(Notes));
        RaiseCommandStates();
    }

    private void RaiseBusyState()
    {
        OnPropertiesChanged(nameof(IsBusy));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        CheckCommand.RaiseCanExecuteChanged();
        DownloadCommand.RaiseCanExecuteChanged();
        SkipVersionCommand.RaiseCanExecuteChanged();
        ClearSkippedVersionCommand.RaiseCanExecuteChanged();
        OpenDownloadFolderCommand.RaiseCanExecuteChanged();
    }
}
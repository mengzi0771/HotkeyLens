using System.Diagnostics;
using System.Reflection;
using System.Windows;
using HotkeyLens.App.Infrastructure;
using HotkeyLens.App.Services;
using HotkeyLens.App.Views;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Models;

namespace HotkeyLens.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IUserPreferencesStore _preferencesStore;
    private readonly IScanSnapshotStore _snapshotStore;
    private readonly Action<UserPreferences> _onSaved;
    private readonly Action _onDataCleared;
    private readonly Action<string> _onThemePreview;
    private readonly bool _showWelcomeOnStartup;

    private OptionItem _selectedTheme;
    private bool _scanShortcutFiles;
    private bool _includeUserConfigurations;
    private bool _includeUiAutomation;
    private bool _includeDocumentedCatalog;
    private bool _includeRegistryOnlyApplications;
    private bool _includeSystemApplications;
    private OptionItem _selectedUiTimeout;
    private OptionItem _selectedMaxWindows;
    private bool _isDirty;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public SettingsViewModel(
        IUserPreferencesStore preferencesStore,
        IScanSnapshotStore snapshotStore,
        UserPreferences preferences,
        Action<UserPreferences> onSaved,
        Action onDataCleared,
        Action<string> onThemePreview)
    {
        _preferencesStore = preferencesStore;
        _snapshotStore = snapshotStore;
        _onSaved = onSaved;
        _onDataCleared = onDataCleared;
        _onThemePreview = onThemePreview;
        _showWelcomeOnStartup = preferences.ShowWelcomeOnStartup;

        ThemeOptions =
        [
            new OptionItem("跟随系统", "System"),
            new OptionItem("浅色", "Light"),
            new OptionItem("深色", "Dark")
        ];
        UiTimeoutOptions =
        [
            new OptionItem("10 秒", "10"),
            new OptionItem("20 秒（推荐）", "20"),
            new OptionItem("30 秒", "30"),
            new OptionItem("45 秒", "45"),
            new OptionItem("60 秒", "60")
        ];
        MaxWindowOptions =
        [
            new OptionItem("10 个窗口", "10"),
            new OptionItem("20 个窗口（推荐）", "20"),
            new OptionItem("30 个窗口", "30"),
            new OptionItem("50 个窗口", "50")
        ];

        _selectedTheme = ThemeOptions.FirstOrDefault(option =>
                              option.Value.Equals(preferences.Theme, StringComparison.OrdinalIgnoreCase))
                          ?? ThemeOptions[0];

        var options = preferences.ScanOptions ?? new ScanOptions();
        _scanShortcutFiles = options.ScanShortcutFiles;
        _includeUserConfigurations = options.IncludeUserConfigurations;
        _includeUiAutomation = options.IncludeUiAutomation;
        _includeDocumentedCatalog = options.IncludeDocumentedCatalog;
        _includeRegistryOnlyApplications = options.IncludeRegistryOnlyApplications;
        _includeSystemApplications = options.IncludeSystemApplications;
        _selectedUiTimeout = UiTimeoutOptions.FirstOrDefault(option =>
                                 option.Value == options.UiAutomationTimeoutSeconds.ToString())
                             ?? UiTimeoutOptions[1];
        _selectedMaxWindows = MaxWindowOptions.FirstOrDefault(option =>
                                  option.Value == options.UiAutomationMaxWindows.ToString())
                              ?? MaxWindowOptions[1];

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        OpenSnapshotCommand = new RelayCommand(OpenSnapshot);
        ClearDataCommand = new AsyncRelayCommand(ClearDataAsync, () => HasSnapshot);
        ResetOptionsCommand = new RelayCommand(ResetOptions);
    }

    public IReadOnlyList<OptionItem> ThemeOptions { get; }
    public IReadOnlyList<OptionItem> UiTimeoutOptions { get; }
    public IReadOnlyList<OptionItem> MaxWindowOptions { get; }

    public OptionItem SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value) && value is not null)
            {
                _onThemePreview(value.Value);
                StatusMessage = string.Empty;
                ErrorMessage = string.Empty;
                // 主题切换立即生效，并自动持久化；扫描选项仍由“保存设置”统一提交。
                _ = SaveAsync();
            }
        }
    }

    public bool ScanShortcutFiles
    {
        get => _scanShortcutFiles;
        set => SetOption(ref _scanShortcutFiles, value);
    }

    public bool IncludeUserConfigurations
    {
        get => _includeUserConfigurations;
        set => SetOption(ref _includeUserConfigurations, value);
    }

    public bool IncludeUiAutomation
    {
        get => _includeUiAutomation;
        set => SetOption(ref _includeUiAutomation, value);
    }

    public bool IncludeDocumentedCatalog
    {
        get => _includeDocumentedCatalog;
        set => SetOption(ref _includeDocumentedCatalog, value);
    }

    public bool IncludeRegistryOnlyApplications
    {
        get => _includeRegistryOnlyApplications;
        set => SetOption(ref _includeRegistryOnlyApplications, value);
    }

    public bool IncludeSystemApplications
    {
        get => _includeSystemApplications;
        set => SetOption(ref _includeSystemApplications, value);
    }

    public OptionItem SelectedUiTimeout
    {
        get => _selectedUiTimeout;
        set
        {
            if (SetProperty(ref _selectedUiTimeout, value))
            {
                IsDirty = true;
            }
        }
    }

    public OptionItem SelectedMaxWindows
    {
        get => _selectedMaxWindows;
        set
        {
            if (SetProperty(ref _selectedMaxWindows, value))
            {
                IsDirty = true;
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string DataDirectory => Path.GetDirectoryName(_snapshotStore.SnapshotPath) ?? string.Empty;
    public string SnapshotPath => _snapshotStore.SnapshotPath;
    public string PreferencesPath => _preferencesStore.PreferencesPath;
    public bool HasSnapshot => File.Exists(_snapshotStore.SnapshotPath);
    public string VersionText => GetVersionText();

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand OpenSnapshotCommand { get; }
    public AsyncRelayCommand ClearDataCommand { get; }
    public RelayCommand ResetOptionsCommand { get; }

    public ScanOptions BuildOptions() => new ScanOptions()
    {
        ScanShortcutFiles = ScanShortcutFiles,
        IncludeUserConfigurations = IncludeUserConfigurations,
        IncludeUiAutomation = IncludeUiAutomation,
        IncludeDocumentedCatalog = IncludeDocumentedCatalog,
        IncludeRegistryOnlyApplications = IncludeRegistryOnlyApplications,
        IncludeSystemApplications = IncludeSystemApplications,
        UiAutomationTimeoutSeconds = ParseNumber(SelectedUiTimeout, 20),
        UiAutomationMaxWindows = ParseNumber(SelectedMaxWindows, 20)
    }.Sanitize();

    public void NotifyDataChanged()
    {
        OnPropertiesChanged(nameof(HasSnapshot), nameof(SnapshotPath), nameof(DataDirectory));
        ClearDataCommand.RaiseCanExecuteChanged();
    }

    private async Task SaveAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            ErrorMessage = string.Empty;
            StatusMessage = string.Empty;
            var preferences = new UserPreferences
            {
                Theme = SelectedTheme.Value,
                ScanOptions = BuildOptions(),
                ShowWelcomeOnStartup = _showWelcomeOnStartup
            };
            await _preferencesStore.SaveAsync(preferences);
            _onSaved(preferences);
            IsDirty = false;
            StatusMessage = $"设置已保存 · {DateTime.Now:HH:mm}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
                                           or System.Text.Json.JsonException or NotSupportedException)
        {
            ErrorMessage = $"保存设置失败：{exception.Message}";
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task ClearDataAsync()
    {
        if (!HasSnapshot)
        {
            StatusMessage = "当前没有已保存的扫描数据。";
            return;
        }

        var confirmed = ConfirmDialog.Show(
            Application.Current?.MainWindow,
            "清除扫描数据",
            "将删除 HotkeyLens 保存的扫描结果（scan-snapshot.json）。删除后可以通过重新扫描生成新数据；偏好设置不会受影响。",
            "清除数据");
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _snapshotStore.ClearAsync();
            _onDataCleared();
            NotifyDataChanged();
            StatusMessage = "扫描数据已清除，偏好设置保持不变。";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"清除扫描数据失败：{exception.Message}";
        }
    }

    private void OpenDataFolder()
    {
        if (!FileActionService.TryOpenPath(DataDirectory, out var error))
        {
            ErrorMessage = $"无法打开数据目录：{error}";
        }
    }

    private void OpenSnapshot()
    {
        if (!FileActionService.TryReveal(SnapshotPath, out var error))
        {
            ErrorMessage = error;
        }
    }

    private void ResetOptions()
    {
        var defaults = new ScanOptions();
        _scanShortcutFiles = defaults.ScanShortcutFiles;
        _includeUserConfigurations = defaults.IncludeUserConfigurations;
        _includeUiAutomation = defaults.IncludeUiAutomation;
        _includeDocumentedCatalog = defaults.IncludeDocumentedCatalog;
        _includeRegistryOnlyApplications = defaults.IncludeRegistryOnlyApplications;
        _includeSystemApplications = defaults.IncludeSystemApplications;
        _selectedUiTimeout = UiTimeoutOptions[1];
        _selectedMaxWindows = MaxWindowOptions[1];
        OnPropertiesChanged(
            nameof(ScanShortcutFiles),
            nameof(IncludeUserConfigurations),
            nameof(IncludeUiAutomation),
            nameof(IncludeDocumentedCatalog),
            nameof(IncludeRegistryOnlyApplications),
            nameof(IncludeSystemApplications),
            nameof(SelectedUiTimeout),
            nameof(SelectedMaxWindows));
        IsDirty = true;
        StatusMessage = "已恢复推荐扫描选项，点击“保存设置”后生效。";
    }

    private void SetOption(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value, name))
        {
            IsDirty = true;
            StatusMessage = string.Empty;
        }
    }

    private static int ParseNumber(OptionItem? option, int fallback)
        => int.TryParse(option?.Value, out var value) ? value : fallback;

    private static string GetVersionText()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                      ?? "1.0.0";
        var plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }
}
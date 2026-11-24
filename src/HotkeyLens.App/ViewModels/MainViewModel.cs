using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using HotkeyLens.App.Infrastructure;
using HotkeyLens.App.Services;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Analysis;
using HotkeyLens.Core.Models;

namespace HotkeyLens.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IScanEngine _scanEngine;
    private readonly IScanSnapshotStore _snapshotStore;
    private readonly IUserPreferencesStore _preferencesStore;
    private readonly string _dataDirectory;

    private readonly List<ConflictRowViewModel> _allConflictRows = [];
    private readonly List<ApplicationRowViewModel> _allApplicationRows = [];
    private readonly Dictionary<string, List<ShortcutConflict>> _conflictsByShortcut = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApplicationCategory> _categoriesByApplication = new(StringComparer.Ordinal);

    private ScanSnapshot _snapshot = ScanSnapshot.Empty();
    private UserPreferences _preferences = new();
    private CancellationTokenSource? _scanCancellation;
    private bool _suppressFilters;

    private NavItem _selectedNav;
    private OptionItem _selectedShortcutScope;
    private OptionItem _selectedShortcutSource;
    private OptionItem _selectedShortcutConfidence;
    private OptionItem _selectedShortcutCategory;
    private OptionItem _selectedShortcutSort;
    private OptionItem _selectedConflictFilter;
    private OptionItem _selectedApplicationCategory;
    private string _shortcutSearchText = string.Empty;
    private string _applicationSearchText = string.Empty;
    private bool _onlyConflicting;
    private bool _groupShortcutsByApplication;
    private ShortcutRowViewModel? _selectedShortcut;
    private ApplicationRowViewModel? _selectedApplication;
    private ICollectionView? _shortcutsView;
    private ICollectionView? _applicationsView;
    private bool _isScanning;
    private double _scanPercent;
    private string _scanPhase = "准备扫描";
    private string _scanMessage = "尚未开始扫描。";
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;

    public MainViewModel(
        IScanEngine scanEngine,
        IScanSnapshotStore snapshotStore,
        IUserPreferencesStore preferencesStore,
        string dataDirectory)
    {
        _scanEngine = scanEngine;
        _snapshotStore = snapshotStore;
        _preferencesStore = preferencesStore;
        _dataDirectory = dataDirectory;

        NavItems =
        [
            new NavItem(PageKind.Dashboard, "总览", "\uE80F"),
            new NavItem(PageKind.Shortcuts, "快捷键", "\uE765"),
            new NavItem(PageKind.Applications, "应用程序", "\uE71D"),
            new NavItem(PageKind.Conflicts, "冲突分析", "\uE7BA"),
            new NavItem(PageKind.Settings, "设置", "\uE713")
        ];
        _selectedNav = NavItems[0];

        ShortcutScopeOptions =
        [
            new OptionItem("全部作用范围", ""),
            new OptionItem("全局", nameof(ShortcutScope.Global)),
            new OptionItem("应用内", nameof(ShortcutScope.Application)),
            new OptionItem("窗口内", nameof(ShortcutScope.Window)),
            new OptionItem("上下文", nameof(ShortcutScope.Contextual)),
            new OptionItem("未知", nameof(ShortcutScope.Unknown))
        ];
        ShortcutSourceOptions =
        [
            new OptionItem("全部信息来源", ""),
            new OptionItem("快捷方式热键", nameof(ShortcutSourceKind.ShortcutFile)),
            new OptionItem("用户配置文件", nameof(ShortcutSourceKind.UserConfiguration)),
            new OptionItem("UI Automation", nameof(ShortcutSourceKind.UiAutomation)),
            new OptionItem("官方文档", nameof(ShortcutSourceKind.OfficialDocumentation)),
            new OptionItem("应用公开接口", nameof(ShortcutSourceKind.ApplicationInterface)),
            new OptionItem("运行时观察", nameof(ShortcutSourceKind.Observed))
        ];
        ShortcutConfidenceOptions =
        [
            new OptionItem("全部可信度", ""),
            new OptionItem("高可信度", nameof(ConfidenceLevel.High)),
            new OptionItem("中可信度", nameof(ConfidenceLevel.Medium)),
            new OptionItem("低可信度", nameof(ConfidenceLevel.Low)),
            new OptionItem("未知", nameof(ConfidenceLevel.Unknown))
        ];
        ShortcutCategoryOptions = BuildCategoryOptions("全部软件分类");
        ApplicationCategoryOptions = BuildCategoryOptions("全部软件分类");
        ShortcutSortOptions =
        [
            new OptionItem("按键组合", nameof(ShortcutSortField.Gesture)),
            new OptionItem("应用程序", nameof(ShortcutSortField.Application)),
            new OptionItem("功能名称", nameof(ShortcutSortField.Action)),
            new OptionItem("作用范围", nameof(ShortcutSortField.Scope)),
            new OptionItem("可信度", nameof(ShortcutSortField.Confidence)),
            new OptionItem("信息来源", nameof(ShortcutSortField.Source))
        ];
        ConflictFilterOptions =
        [
            new OptionItem("全部结果", ""),
            new OptionItem("全局注册竞争", nameof(ConflictKind.GlobalHotkeyCompetition)),
            new OptionItem("潜在冲突", nameof(ConflictKind.PotentialConflict)),
            new OptionItem("重复绑定", nameof(ConflictKind.DuplicateBinding))
        ];

        _selectedShortcutScope = ShortcutScopeOptions[0];
        _selectedShortcutSource = ShortcutSourceOptions[0];
        _selectedShortcutConfidence = ShortcutConfidenceOptions[0];
        _selectedShortcutCategory = ShortcutCategoryOptions[0];
        _selectedShortcutSort = ShortcutSortOptions[0];
        _selectedConflictFilter = ConflictFilterOptions[0];
        _selectedApplicationCategory = ApplicationCategoryOptions[0];

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning);
        CancelScanCommand = new RelayCommand(CancelScan, () => IsScanning);
        ClearShortcutFiltersCommand = new RelayCommand(ClearShortcutFilters);
        OpenShortcutSourceCommand = new RelayCommand(OpenShortcutSource);
        OpenShortcutSettingsCommand = new RelayCommand(OpenShortcutSettings);
        OpenApplicationFolderCommand = new RelayCommand(OpenApplicationFolder);
        OpenApplicationSettingsCommand = new RelayCommand(OpenApplicationSettings);
        ShowConflictShortcutsCommand = new RelayCommand(ShowConflictShortcuts);
        CopyShortcutCommand = new RelayCommand(CopyShortcut);
        NavigateCommand = new RelayCommand(Navigate);
        CloseShortcutDetailsCommand = new RelayCommand(() => SelectedShortcut = null, () => SelectedShortcut is not null);
        CloseApplicationDetailsCommand = new RelayCommand(() => SelectedApplication = null, () => SelectedApplication is not null);
        DismissErrorCommand = new RelayCommand(() => ErrorMessage = string.Empty);
        RefreshDataCommand = new RelayCommand(RefreshFilterViews);
    }

    public IReadOnlyList<NavItem> NavItems { get; }
    public IReadOnlyList<OptionItem> ShortcutScopeOptions { get; }
    public IReadOnlyList<OptionItem> ShortcutSourceOptions { get; }
    public IReadOnlyList<OptionItem> ShortcutConfidenceOptions { get; }
    public IReadOnlyList<OptionItem> ShortcutCategoryOptions { get; }
    public IReadOnlyList<OptionItem> ShortcutSortOptions { get; }
    public IReadOnlyList<OptionItem> ConflictFilterOptions { get; }
    public IReadOnlyList<OptionItem> ApplicationCategoryOptions { get; }

    public ObservableCollection<ShortcutRowViewModel> FilteredShortcuts { get; } = [];
    public ObservableCollection<ApplicationRowViewModel> FilteredApplications { get; } = [];
    public ObservableCollection<ConflictRowViewModel> FilteredConflicts { get; } = [];
    public ObservableCollection<DiagnosticRowViewModel> Diagnostics { get; } = [];
    public ObservableCollection<DistributionItem> ScopeDistribution { get; } = [];
    public ObservableCollection<DistributionItem> OriginDistribution { get; } = [];

    public SettingsViewModel Settings { get; private set; } = null!;

    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand ClearShortcutFiltersCommand { get; }
    public RelayCommand OpenShortcutSourceCommand { get; }
    public RelayCommand OpenShortcutSettingsCommand { get; }
    public RelayCommand OpenApplicationFolderCommand { get; }
    public RelayCommand OpenApplicationSettingsCommand { get; }
    public RelayCommand ShowConflictShortcutsCommand { get; }
    public RelayCommand CopyShortcutCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand CloseShortcutDetailsCommand { get; }
    public RelayCommand CloseApplicationDetailsCommand { get; }
    public RelayCommand DismissErrorCommand { get; }
    public RelayCommand RefreshDataCommand { get; }

    public NavItem SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (value is not null && SetProperty(ref _selectedNav, value))
            {
                OnPropertiesChanged(
                    nameof(CurrentPage),
                    nameof(PageTitle),
                    nameof(PageSubtitle),
                    nameof(IsDashboardVisible),
                    nameof(IsShortcutsVisible),
                    nameof(IsApplicationsVisible),
                    nameof(IsConflictsVisible),
                    nameof(IsSettingsVisible));
            }
        }
    }

    public PageKind CurrentPage => SelectedNav.Page;
    public string PageTitle => CurrentPage switch
    {
        PageKind.Shortcuts => "快捷键",
        PageKind.Applications => "应用程序",
        PageKind.Conflicts => "冲突分析",
        PageKind.Settings => "设置",
        _ => "总览"
    };

    public string PageSubtitle => CurrentPage switch
    {
        PageKind.Shortcuts => "检索每一条按键绑定，核对它的来源和生效范围",
        PageKind.Applications => "查看本机应用的可读取配置与扫描覆盖情况",
        PageKind.Conflicts => "区分已证实的全局竞争、重复绑定与只是可能出现的干扰",
        PageKind.Settings => "控制扫描来源、界面主题与本地数据",
        _ => "本机快捷键清单、来源证据与冲突概况"
    };

    public bool IsDashboardVisible => CurrentPage == PageKind.Dashboard;
    public bool IsShortcutsVisible => CurrentPage == PageKind.Shortcuts;
    public bool IsApplicationsVisible => CurrentPage == PageKind.Applications;
    public bool IsConflictsVisible => CurrentPage == PageKind.Conflicts;
    public bool IsSettingsVisible => CurrentPage == PageKind.Settings;

    public string ShortcutSearchText
    {
        get => _shortcutSearchText;
        set
        {
            if (SetProperty(ref _shortcutSearchText, value))
            {
                ApplyShortcutFilters();
            }
        }
    }

    public string ApplicationSearchText
    {
        get => _applicationSearchText;
        set
        {
            if (SetProperty(ref _applicationSearchText, value))
            {
                ApplyApplicationFilters();
            }
        }
    }

    public OptionItem SelectedShortcutScope
    {
        get => _selectedShortcutScope;
        set
        {
            if (SetProperty(ref _selectedShortcutScope, value))
            {
                ApplyShortcutFilters();
            }
        }
    }

    public OptionItem SelectedShortcutSource
    {
        get => _selectedShortcutSource;
        set
        {
            if (SetProperty(ref _selectedShortcutSource, value))
            {
                ApplyShortcutFilters();
            }
        }
    }

    public OptionItem SelectedShortcutConfidence
    {
        get => _selectedShortcutConfidence;
        set
        {
            if (SetProperty(ref _selectedShortcutConfidence, value))
            {
                ApplyShortcutFilters();
            }
        }
    }

    public OptionItem SelectedShortcutCategory
    {
        get => _selectedShortcutCategory;
        set
        {
            if (SetProperty(ref _selectedShortcutCategory, value))
            {
                ApplyShortcutFilters();
            }
        }
    }

    public OptionItem SelectedShortcutSort
    {
        get => _selectedShortcutSort;
        set
        {
            if (SetProperty(ref _selectedShortcutSort, value))
            {
                ApplyShortcutFilters();
            }
        }
    }

    public OptionItem SelectedConflictFilter
    {
        get => _selectedConflictFilter;
        set
        {
            if (SetProperty(ref _selectedConflictFilter, value))
            {
                ApplyConflictFilter();
            }
        }
    }

    public OptionItem SelectedApplicationCategory
    {
        get => _selectedApplicationCategory;
        set
        {
            if (SetProperty(ref _selectedApplicationCategory, value))
            {
                ApplyApplicationFilters();
            }
        }
    }

    public bool OnlyConflicting
    {
        get => _onlyConflicting;
        set
        {
            if (SetProperty(ref _onlyConflicting, value))
            {
                ApplyShortcutFilters();
            }
        }
    }

    public bool GroupShortcutsByApplication
    {
        get => _groupShortcutsByApplication;
        set
        {
            if (SetProperty(ref _groupShortcutsByApplication, value))
            {
                RefreshShortcutView();
            }
        }
    }

    public ShortcutRowViewModel? SelectedShortcut
    {
        get => _selectedShortcut;
        set
        {
            if (SetProperty(ref _selectedShortcut, value))
            {
                OnPropertyChanged(nameof(HasSelectedShortcut));
                CloseShortcutDetailsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ApplicationRowViewModel? SelectedApplication
    {
        get => _selectedApplication;
        set
        {
            if (SetProperty(ref _selectedApplication, value))
            {
                OnPropertyChanged(nameof(HasSelectedApplication));
                CloseApplicationDetailsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ICollectionView? ShortcutsView
    {
        get => _shortcutsView;
        private set => SetProperty(ref _shortcutsView, value);
    }

    public ICollectionView? ApplicationsView
    {
        get => _applicationsView;
        private set => SetProperty(ref _applicationsView, value);
    }

    public bool HasSelectedShortcut => SelectedShortcut is not null;
    public bool HasSelectedApplication => SelectedApplication is not null;

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertiesChanged(
                    nameof(ScanButtonText),
                    nameof(IsWelcomeVisible),
                    nameof(CanNavigateToDashboard));
                ScanCommand.RaiseCanExecuteChanged();
                CancelScanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double ScanPercent
    {
        get => _scanPercent;
        private set => SetProperty(ref _scanPercent, value);
    }

    public string ScanPhase
    {
        get => _scanPhase;
        private set => SetProperty(ref _scanPhase, value);
    }

    public string ScanMessage
    {
        get => _scanMessage;
        private set => SetProperty(ref _scanMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasErrorMessage));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

    public void ReportUiFailure(string detail)
    {
        ErrorMessage = $"界面操作未完成：{detail}。可以关闭提示后继续使用其他页面。";
        StatusMessage = "界面操作出现异常，详细信息已写入本地日志。";
    }

    public bool IsWelcomeVisible => !HasSnapshot && !IsScanning;
    public bool CanNavigateToDashboard => true;
    public string ScanButtonText => HasSnapshot ? "重新扫描" : "开始扫描";

    public bool HasSnapshot => _snapshot.ScannedAt != DateTimeOffset.MinValue;
    public bool HasNoSnapshot => !HasSnapshot;
    public bool HasShortcuts => _snapshot.Statistics.TotalShortcuts > 0;
    public bool HasNoShortcuts => !HasShortcuts;
    public bool HasConflicts => _snapshot.Conflicts.Count > 0;
    public bool HasNoConflicts => !HasConflicts;
    public bool HasApplications => _snapshot.Statistics.TotalApplications > 0;
    public bool HasDiagnostics => Diagnostics.Count > 0;

    public int TotalApplications => _snapshot.Statistics.TotalApplications;
    public int TotalShortcuts => _snapshot.Statistics.TotalShortcuts;
    public int DuplicateGroups => _snapshot.Statistics.DuplicateGroups;
    public int PotentialConflicts => _snapshot.Statistics.PotentialConflicts;
    public int GlobalCompetitions => _snapshot.Statistics.GlobalCompetitions;
    public int HighConfidenceShortcuts => _snapshot.Statistics.HighConfidenceShortcuts;
    public int UserConfiguredShortcuts => _snapshot.Statistics.UserConfiguredShortcuts;
    public int DocumentedDefaultShortcuts => _snapshot.Statistics.DocumentedDefaultShortcuts;
    public int RuntimeObservedShortcuts => _snapshot.Statistics.RuntimeObservedShortcuts;
    public int ScannedApplications => _snapshot.Statistics.ScannedApplications;
    public int NotScannableApplications => _snapshot.Statistics.NotScannableApplications;
    public int NoShortcutsApplications => _snapshot.Statistics.NoShortcutsApplications;
    public int FailedApplications => _snapshot.Statistics.FailedApplications;

    public string LastScanText => !HasSnapshot
        ? "尚未扫描"
        : $"{_snapshot.ScannedAt.ToLocalTime():yyyy年M月d日 HH:mm}";
    public string LastScanDetail => !HasSnapshot
        ? "扫描只会在你主动点击后执行"
        : $"用时 {_snapshot.DurationMilliseconds / 1000d:F1} 秒 · 引擎 {_snapshot.EngineVersion}";
    public string CoverageText => HasSnapshot
        ? $"已检查 {ScannedApplications} / {TotalApplications} 个应用；{NotScannableApplications} 个应用暂时无法自动读取"
        : "等待第一次扫描";
    public string OriginSummary =>
        $"{UserConfiguredShortcuts} 条用户配置 · {DocumentedDefaultShortcuts} 条官方默认 · {RuntimeObservedShortcuts} 条运行时观察";
    public string ShortcutFilterSummary => $"{FilteredShortcuts.Count} / {TotalShortcuts} 条";
    public string ApplicationFilterSummary => $"{FilteredApplications.Count} / {TotalApplications} 个";
    public string ConflictFilterSummary => $"{FilteredConflicts.Count} / {_allConflictRows.Count} 组";
    public string DataLocation => _dataDirectory;

    public async Task InitializeAsync()
    {
        var startupMessages = new List<string>();
        try
        {
            _preferences = await _preferencesStore.LoadAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _preferences = new UserPreferences();
            startupMessages.Add($"用户设置无法读取，已使用默认值：{exception.Message}");
        }

        ThemeManager.Apply(_preferences.Theme);
        Settings = new SettingsViewModel(
            _preferencesStore,
            _snapshotStore,
            _preferences,
            OnSettingsSaved,
            OnDataCleared,
            ThemeManager.Apply);
        OnPropertyChanged(nameof(Settings));

        try
        {
            var snapshot = await _snapshotStore.LoadAsync();
            if (snapshot is not null)
            {
                ApplySnapshot(snapshot);
                StatusMessage = $"已载入上次扫描结果 · {LastScanText}";
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            startupMessages.Add(
                $"上次扫描数据无法读取，请重新扫描。文件：{_snapshotStore.SnapshotPath}。原因：{exception.Message}");
            ResetSnapshotView();
        }

        if (!HasSnapshot)
        {
            ResetSnapshotView();
        }

        Settings?.NotifyDataChanged();
        if (startupMessages.Count > 0)
        {
            ErrorMessage = string.Join(Environment.NewLine, startupMessages);
        }
    }

    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        ScanPercent = 0;
        ScanPhase = "准备扫描";
        ScanMessage = "正在读取本机应用清单…";
        _scanCancellation = new CancellationTokenSource();

        var progress = new Progress<ScanProgress>(value =>
        {
            ScanPercent = Math.Clamp(value.Percent, 0, 100);
            ScanPhase = value.Phase;
            ScanMessage = value.Message;
        });

        try
        {
            var options = Settings?.BuildOptions() ?? _preferences.ScanOptions;
            var snapshot = await _scanEngine.ScanAsync(options, progress, _scanCancellation.Token);
            ApplySnapshot(snapshot);

            try
            {
                await _snapshotStore.SaveAsync(snapshot);
                Settings?.NotifyDataChanged();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ErrorMessage = $"扫描已完成，但结果无法保存到本地：{exception.Message}";
            }

            ScanPhase = "完成";
            ScanPercent = 100;
            ScanMessage = $"扫描完成：{snapshot.Statistics.TotalShortcuts} 条快捷键，{snapshot.Conflicts.Count} 组分析结果。";
            StatusMessage = $"扫描完成 · {LastScanText}";
        }
        catch (OperationCanceledException)
        {
            ScanPhase = "已取消";
            ScanMessage = "扫描已取消，原有结果保持不变。";
            StatusMessage = "扫描已取消";
        }
        catch (Exception exception)
        {
            ScanPhase = "扫描失败";
            ScanMessage = "扫描没有完成，请查看错误详情。";
            ErrorMessage = $"扫描失败：{exception.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }

    private void CancelScan()
    {
        if (!IsScanning)
        {
            return;
        }

        ScanPhase = "正在取消";
        ScanMessage = "等待当前读取步骤结束…";
        try
        {
            _scanCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Shutdown()
    {
        try
        {
            _scanCancellation?.Cancel();
            _scanCancellation?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _scanCancellation = null;
        }
    }

    private void ApplySnapshot(ScanSnapshot snapshot)
    {
        _snapshot = snapshot;
        _conflictsByShortcut.Clear();
        _categoriesByApplication.Clear();

        foreach (var application in snapshot.Applications)
        {
            _categoriesByApplication[application.Id] = application.Category;
        }

        var conflictRows = snapshot.Conflicts
            .Select(conflict => new ConflictRowViewModel(conflict))
            .ToList();
        foreach (var conflict in snapshot.Conflicts)
        {
            foreach (var participant in conflict.Participants)
            {
                if (!_conflictsByShortcut.TryGetValue(participant.ShortcutId, out var list))
                {
                    list = [];
                    _conflictsByShortcut[participant.ShortcutId] = list;
                }

                if (!list.Contains(conflict))
                {
                    list.Add(conflict);
                }
            }
        }

        _allConflictRows.Clear();
        _allConflictRows.AddRange(conflictRows);

        var resultsById = snapshot.ApplicationResults
            .GroupBy(result => result.ApplicationId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        _allApplicationRows.Clear();
        foreach (var application in snapshot.Applications
                     .OrderBy(app => app.Category)
                     .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            resultsById.TryGetValue(application.Id, out var result);
            _allApplicationRows.Add(new ApplicationRowViewModel(application, result));
        }

        SelectedShortcut = null;
        SelectedApplication = null;
        ApplyShortcutFilters();
        ApplyApplicationFilters();
        ApplyConflictFilter();

        Diagnostics.Clear();
        foreach (var diagnostic in snapshot.Diagnostics
                     .OrderByDescending(item => item.Severity)
                     .ThenByDescending(item => item.Timestamp)
                     .Take(60))
        {
            Diagnostics.Add(new DiagnosticRowViewModel(diagnostic));
        }

        BuildDistributions();
        OnPropertiesChanged(
            nameof(HasSnapshot),
            nameof(HasNoSnapshot),
            nameof(HasShortcuts),
            nameof(HasNoShortcuts),
            nameof(HasConflicts),
            nameof(HasNoConflicts),
            nameof(HasApplications),
            nameof(HasDiagnostics),
            nameof(TotalApplications),
            nameof(TotalShortcuts),
            nameof(DuplicateGroups),
            nameof(PotentialConflicts),
            nameof(GlobalCompetitions),
            nameof(HighConfidenceShortcuts),
            nameof(UserConfiguredShortcuts),
            nameof(DocumentedDefaultShortcuts),
            nameof(RuntimeObservedShortcuts),
            nameof(ScannedApplications),
            nameof(NotScannableApplications),
            nameof(NoShortcutsApplications),
            nameof(FailedApplications),
            nameof(LastScanText),
            nameof(LastScanDetail),
            nameof(CoverageText),
            nameof(OriginSummary),
            nameof(ScanButtonText),
            nameof(IsWelcomeVisible),
            nameof(ShortcutFilterSummary),
            nameof(ApplicationFilterSummary),
            nameof(ConflictFilterSummary));
        Settings?.NotifyDataChanged();
    }

    private void ResetSnapshotView()
    {
        _snapshot = ScanSnapshot.Empty();
        _conflictsByShortcut.Clear();
        _categoriesByApplication.Clear();
        _allConflictRows.Clear();
        _allApplicationRows.Clear();
        FilteredShortcuts.Clear();
        FilteredApplications.Clear();
        FilteredConflicts.Clear();
        Diagnostics.Clear();
        ScopeDistribution.Clear();
        OriginDistribution.Clear();
        SelectedShortcut = null;
        SelectedApplication = null;
        ShortcutsView = null;
        ApplicationsView = null;
        RefreshFilterViews();
        OnPropertiesChanged(
            nameof(HasSnapshot),
            nameof(HasNoSnapshot),
            nameof(HasShortcuts),
            nameof(HasNoShortcuts),
            nameof(HasConflicts),
            nameof(HasNoConflicts),
            nameof(HasApplications),
            nameof(HasDiagnostics),
            nameof(TotalApplications),
            nameof(TotalShortcuts),
            nameof(DuplicateGroups),
            nameof(PotentialConflicts),
            nameof(GlobalCompetitions),
            nameof(HighConfidenceShortcuts),
            nameof(UserConfiguredShortcuts),
            nameof(DocumentedDefaultShortcuts),
            nameof(RuntimeObservedShortcuts),
            nameof(ScannedApplications),
            nameof(NotScannableApplications),
            nameof(NoShortcutsApplications),
            nameof(FailedApplications),
            nameof(LastScanText),
            nameof(LastScanDetail),
            nameof(CoverageText),
            nameof(OriginSummary),
            nameof(ScanButtonText),
            nameof(IsWelcomeVisible));
    }

    private void ApplyShortcutFilters()
    {
        if (_suppressFilters)
        {
            return;
        }

        var filter = new ShortcutFilter
        {
            SearchText = ShortcutSearchText,
            Scope = ParseEnum<ShortcutScope>(SelectedShortcutScope.Value),
            SourceKind = ParseEnum<ShortcutSourceKind>(SelectedShortcutSource.Value),
            MinimumConfidence = ParseEnum<ConfidenceLevel>(SelectedShortcutConfidence.Value),
            Category = ParseEnum<ApplicationCategory>(SelectedShortcutCategory.Value),
            OnlyConflicting = OnlyConflicting,
            SortBy = ParseEnum<ShortcutSortField>(SelectedShortcutSort.Value) ?? ShortcutSortField.Gesture
        };

        var selectedId = SelectedShortcut?.Entry.Id;
        var rows = ShortcutQuery.Apply(
                _snapshot.Shortcuts,
                filter,
                _snapshot.Conflicts,
                _categoriesByApplication);
        FilteredShortcuts.Clear();
        foreach (var entry in rows)
        {
            _conflictsByShortcut.TryGetValue(entry.Id, out var conflicts);
            FilteredShortcuts.Add(new ShortcutRowViewModel(entry, conflicts ?? []));
        }

        SelectedShortcut = selectedId is null
            ? null
            : FilteredShortcuts.FirstOrDefault(row => row.Entry.Id == selectedId);
        RefreshShortcutView();
        OnPropertyChanged(nameof(ShortcutFilterSummary));
    }

    private void RefreshShortcutView()
    {
        var view = new ListCollectionView(FilteredShortcuts);
        if (GroupShortcutsByApplication)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShortcutRowViewModel.ApplicationName)));
        }

        ShortcutsView = view;
    }

    private void ApplyApplicationFilters()
    {
        var term = ApplicationSearchText.Trim();
        var category = ParseEnum<ApplicationCategory>(SelectedApplicationCategory.Value);
        var selectedId = SelectedApplication?.Application.Id;

        FilteredApplications.Clear();
        foreach (var row in _allApplicationRows.Where(row =>
                     (category is null || row.Application.Category == category) &&
                     (term.Length == 0 || MatchesApplication(row, term))))
        {
            FilteredApplications.Add(row);
        }

        SelectedApplication = selectedId is null
            ? null
            : FilteredApplications.FirstOrDefault(row => row.Application.Id == selectedId);

        var view = new ListCollectionView(FilteredApplications);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ApplicationRowViewModel.CategoryLabel)));
        ApplicationsView = view;
        OnPropertyChanged(nameof(ApplicationFilterSummary));
    }

    private static bool MatchesApplication(ApplicationRowViewModel row, string term)
        => row.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
           row.Publisher.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
           row.ExecutablePath.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
           row.CategoryLabel.Contains(term, StringComparison.CurrentCultureIgnoreCase);

    private void ApplyConflictFilter()
    {
        var kind = ParseEnum<ConflictKind>(SelectedConflictFilter.Value);
        FilteredConflicts.Clear();
        foreach (var row in _allConflictRows.Where(row => kind is null || row.Conflict.Kind == kind))
        {
            FilteredConflicts.Add(row);
        }

        OnPropertyChanged(nameof(ConflictFilterSummary));
    }

    private void BuildDistributions()
    {
        ScopeDistribution.Clear();
        var scopeAccents = new Dictionary<ShortcutScope, string>
        {
            [ShortcutScope.Global] = "#D1495B",
            [ShortcutScope.Application] = "#2F6FDB",
            [ShortcutScope.Window] = "#1B998B",
            [ShortcutScope.Contextual] = "#C47F17",
            [ShortcutScope.Unknown] = "#8792A6"
        };
        var total = Math.Max(1, _snapshot.Statistics.TotalShortcuts);
        foreach (var scope in Enum.GetValues<ShortcutScope>())
        {
            _snapshot.Statistics.ByScope.TryGetValue(scope, out var count);
            ScopeDistribution.Add(new DistributionItem(
                DisplayLabels.Scope(scope),
                count,
                Math.Round(count * 100d / total, 1),
                scopeAccents[scope]));
        }

        OriginDistribution.Clear();
        var origins = new[]
        {
            (ShortcutOrigin.UserConfigured, "#1B998B"),
            (ShortcutOrigin.OfficialDefault, "#6F5BD3"),
            (ShortcutOrigin.RuntimeObserved, "#2F6FDB"),
            (ShortcutOrigin.Unknown, "#8792A6")
        };
        foreach (var (origin, accent) in origins)
        {
            var count = _snapshot.Shortcuts.Count(entry => entry.Origin == origin);
            OriginDistribution.Add(new DistributionItem(
                DisplayLabels.Origin(origin),
                count,
                Math.Round(count * 100d / total, 1),
                accent));
        }
    }

    private void ClearShortcutFilters()
    {
        _suppressFilters = true;
        ShortcutSearchText = string.Empty;
        OnlyConflicting = false;
        SelectedShortcutScope = ShortcutScopeOptions[0];
        SelectedShortcutSource = ShortcutSourceOptions[0];
        SelectedShortcutConfidence = ShortcutConfidenceOptions[0];
        SelectedShortcutCategory = ShortcutCategoryOptions[0];
        SelectedShortcutSort = ShortcutSortOptions[0];
        _suppressFilters = false;
        ApplyShortcutFilters();
        StatusMessage = "已清除快捷键筛选条件。";
    }

    private void OpenShortcutSource()
    {
        if (SelectedShortcut is null)
        {
            return;
        }

        var reference = SelectedShortcut.Entry.SourceReference;
        var isUrl = Uri.TryCreate(reference, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        var opened = isUrl
            ? FileActionService.TryOpenUrl(reference, out var error)
            : FileActionService.TryReveal(reference, out error);
        if (!opened)
        {
            ErrorMessage = $"无法打开来源：{error}";
        }
    }

    private void OpenShortcutSettings()
    {
        if (SelectedShortcut is null)
        {
            return;
        }

        var application = _snapshot.Applications.FirstOrDefault(app =>
            app.Id == SelectedShortcut.Entry.ApplicationId);
        OpenApplicationSettingsCore(application);
    }

    private void OpenApplicationFolder()
    {
        if (SelectedApplication is null)
        {
            return;
        }

        var path = SelectedApplication.Application.ExecutablePath;
        if (!string.IsNullOrWhiteSpace(path) && FileActionService.TryReveal(path, out _))
        {
            return;
        }

        if (FileActionService.TryOpenPath(SelectedApplication.Application.InstallLocation, out _))
        {
            return;
        }

        ErrorMessage = string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(SelectedApplication.Application.InstallLocation)
            ? "该应用没有记录可执行文件路径或安装位置。"
            : "无法打开安装位置，目标可能已被移动或删除。";
    }

    private void OpenApplicationSettings()
    {
        if (SelectedApplication is null)
        {
            return;
        }

        OpenApplicationSettingsCore(SelectedApplication.Application);
    }

    private void OpenApplicationSettingsCore(DiscoveredApplication? application)
    {
        if (application is null)
        {
            ErrorMessage = "没有找到该快捷键所属应用程序的记录。";
            return;
        }

        if (!string.IsNullOrWhiteSpace(application.SettingsPath) &&
            FileActionService.TryReveal(application.SettingsPath, out _))
        {
            StatusMessage = $"已在资源管理器中定位 {application.Name} 的配置文件。";
            return;
        }

        var hint = string.IsNullOrWhiteSpace(application.SettingsHint)
            ? "该应用没有已知的安全配置入口。HotkeyLens 不会自动修改快捷键配置。"
            : application.SettingsHint;
        MessageBox.Show(
            $"HotkeyLens 不会直接改写其他应用的配置文件。\n\n{application.Name} 的快捷键通常可以在以下位置调整：\n{hint}",
            $"{application.Name} · 快捷键设置指引",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowConflictShortcuts(object? parameter)
    {
        if (parameter is not ConflictRowViewModel conflict)
        {
            return;
        }

        _suppressFilters = true;
        ShortcutSearchText = conflict.GestureText;
        SelectedShortcutScope = ShortcutScopeOptions[0];
        SelectedShortcutSource = ShortcutSourceOptions[0];
        SelectedShortcutConfidence = ShortcutConfidenceOptions[0];
        SelectedShortcutCategory = ShortcutCategoryOptions[0];
        OnlyConflicting = false;
        _suppressFilters = false;
        ApplyShortcutFilters();
        SelectedNav = NavItems.First(item => item.Page == PageKind.Shortcuts);
        StatusMessage = $"已筛选与 {conflict.GestureText} 相关的快捷键。";
    }

    private void Navigate(object? parameter)
    {
        PageKind page;
        switch (parameter)
        {
            case PageKind value:
                page = value;
                break;
            case string text when Enum.TryParse<PageKind>(text, ignoreCase: true, out var parsed):
                page = parsed;
                break;
            default:
                page = PageKind.Dashboard;
                break;
        }

        SelectedNav = NavItems.First(item => item.Page == page);
    }

    private void CopyShortcut()
    {
        if (SelectedShortcut is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(SelectedShortcut.DisplayGesture);
            StatusMessage = $"已复制 {SelectedShortcut.DisplayGesture}";
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            ErrorMessage = $"复制到剪贴板失败：{exception.Message}";
        }
    }

    private void RefreshFilterViews()
    {
        ApplyShortcutFilters();
        ApplyApplicationFilters();
        ApplyConflictFilter();
    }

    private void OnSettingsSaved(UserPreferences preferences)
    {
        _preferences = preferences;
        StatusMessage = "扫描设置与界面主题已保存。";
    }

    private void OnDataCleared()
    {
        ResetSnapshotView();
        SelectedNav = NavItems.First(item => item.Page == PageKind.Dashboard);
        StatusMessage = "扫描数据已清除。重新扫描可以再次生成。";
    }

    private static TEnum? ParseEnum<TEnum>(string? value) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private static IReadOnlyList<OptionItem> BuildCategoryOptions(string allLabel)
    {
        var options = new List<OptionItem> { new(allLabel, string.Empty) };
        foreach (var category in Enum.GetValues<ApplicationCategory>())
        {
            options.Add(new OptionItem(DisplayLabels.Category(category), category.ToString()));
        }

        return options;
    }
}
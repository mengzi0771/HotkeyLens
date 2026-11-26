using System.IO;
using System.Windows;
using System.Windows.Threading;
using HotkeyLens.App.Services;
using HotkeyLens.App.ViewModels;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Adapters;
using HotkeyLens.Core.Catalog;
using HotkeyLens.Core.Discovery;
using HotkeyLens.Core.Persistence;
using HotkeyLens.Core.Scanning;
using HotkeyLens.Core.Updates;

namespace HotkeyLens.App;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;
    private Mutex? _instanceMutex;
    private bool _ownsInstance;
    private string? _dataDirectory;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(
            initiallyOwned: true,
            @"Local\HotkeyLens.SingleInstance",
            out var createdNew);
        _ownsInstance = createdNew;
        if (!createdNew)
        {
            MessageBox.Show(
                "HotkeyLens 已经在运行。请切换到现有窗口，不要同时启动多个实例。",
                "HotkeyLens",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        try
        {
            var dataDirectory = ResolveDataDirectory();
            _dataDirectory = dataDirectory;

            IShortcutSourceAdapter[] adapters =
            [
                new OfficialShortcutAdapter(ShortcutCatalog.Default),
                new ShortcutFileAdapter(),
                new ChromiumExtensionShortcutsAdapter(),
                new SnipasteConfigAdapter(),
                new VsCodeKeybindingsAdapter(),
                new WindowsTerminalSettingsAdapter(),
                new JetBrainsKeymapAdapter(),
                new NotepadPlusPlusShortcutsAdapter(),
                new UiAutomationShortcutAdapter()
            ];

            var engine = new ScanEngine(
                new ApplicationDiscoveryService(),
                adapters,
                new ProcessSnapshotProvider());
            var snapshotStore = new JsonScanSnapshotStore(dataDirectory);
            var preferencesStore = new JsonUserPreferencesStore(dataDirectory);
            var updateService = new UpdateService(
                new GitHubReleaseClient(),
                preferencesStore,
                AppVersion.Current);

            _mainViewModel = new MainViewModel(
                engine,
                snapshotStore,
                preferencesStore,
                dataDirectory,
                updateService,
                new UpdateDownloadService());
            await _mainViewModel.InitializeAsync();

            var window = new MainWindow { DataContext = _mainViewModel };
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            WriteDiagnosticLog("startup-errors.log", exception);

            MessageBox.Show(
                $"HotkeyLens 无法启动。\n\n{exception.Message}",
                "HotkeyLens · 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Shutdown();
        if (_instanceMutex is not null)
        {
            if (_ownsInstance)
            {
                _instanceMutex.ReleaseMutex();
                _ownsInstance = false;
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        base.OnExit(e);
    }

    private bool _uiErrorQueued;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        WriteDiagnosticLog("ui-errors.log", e.Exception);

        if (_uiErrorQueued || _mainViewModel is null)
        {
            return;
        }

        _uiErrorQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _uiErrorQueued = false;
            _mainViewModel?.ReportUiFailure(e.Exception.Message);
        });
    }

    private static string ResolveDataDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable("HOTKEYLENS_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.GetFullPath(overridden.Trim());
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HotkeyLens");
    }

    private void WriteDiagnosticLog(string fileName, Exception exception)
    {
        try
        {
            var directory = _dataDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HotkeyLens");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, fileName),
                $"[{DateTimeOffset.Now:O}] {exception}\n\n");
        }
        catch
        {
            // Diagnostics must never replace the original failure with another exception.
        }
    }
}
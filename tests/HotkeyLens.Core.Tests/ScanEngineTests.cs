using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Models;
using HotkeyLens.Core.Scanning;

namespace HotkeyLens.Core.Tests;

public sealed class ScanEngineTests
{
    private static readonly DiscoveredApplication Alpha = TestData.Application("alpha", "Alpha");
    private static readonly DiscoveredApplication Beta = TestData.Application("beta", "Beta");
    private static readonly DiscoveredApplication Gamma = TestData.Application("gamma", "Gamma");

    [Fact]
    public async Task ScanAggregatesSourcesAndMarksEveryApplicationState()
    {
        var discovery = new FakeDiscovery([Alpha, Beta, Gamma]);
        var adapter = new FakeAdapter(result =>
        {
            result.CoveredApplicationIds.Add(Alpha.Id);
            result.CoveredApplicationIds.Add(Beta.Id);
            result.Shortcuts.Add(TestData.Entry(Alpha, "Ctrl+K"));
            return result;
        });
        var progress = new InlineProgress();
        var engine = new ScanEngine(discovery, [adapter]);

        var snapshot = await engine.ScanAsync(
            new ScanOptions { UiAutomationTimeoutSeconds = 1 },
            progress,
            CancellationToken.None);

        Assert.Single(snapshot.Shortcuts);
        Assert.Equal(3, snapshot.Statistics.TotalApplications);
        Assert.Equal(2, snapshot.Statistics.ScannedApplications);
        Assert.Equal(1, snapshot.Statistics.NoShortcutsApplications);
        Assert.Equal(1, snapshot.Statistics.NotScannableApplications);
        Assert.Equal(1, snapshot.Statistics.TotalShortcuts);
        Assert.Equal(
            ScanState.Scanned,
            snapshot.ApplicationResults.Single(result => result.ApplicationId == Alpha.Id).State);
        Assert.Equal(
            ScanState.NoShortcutsFound,
            snapshot.ApplicationResults.Single(result => result.ApplicationId == Beta.Id).State);
        Assert.Equal(
            ScanState.NotScannable,
            snapshot.ApplicationResults.Single(result => result.ApplicationId == Gamma.Id).State);

        Assert.Equal(5, adapter.LastContext!.Options.UiAutomationTimeoutSeconds);
        Assert.NotEmpty(progress.Reports);
        Assert.Equal(100, progress.Reports[^1].Percent);
        Assert.True(IsMonotonic(progress.Reports.Select(report => report.Percent)));
    }

    [Fact]
    public async Task OneAdapterFailureDoesNotAbortTheScan()
    {
        var discovery = new FakeDiscovery([Alpha]);
        var failing = new FakeAdapter(result => throw new InvalidOperationException("broken source"));
        var working = new FakeAdapter(result =>
        {
            result.CoveredApplicationIds.Add(Alpha.Id);
            result.Shortcuts.Add(TestData.Entry(Alpha, "Ctrl+J"));
            return result;
        });

        var snapshot = await new ScanEngine(discovery, [failing, working])
            .ScanAsync(new ScanOptions(), null, CancellationToken.None);

        Assert.Single(snapshot.Shortcuts);
        Assert.Contains(snapshot.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.Detail?.Contains("broken source", StringComparison.Ordinal) == true);
        Assert.Equal(ScanState.Scanned, snapshot.ApplicationResults.Single().State);
    }

    [Fact]
    public async Task EntriesForUnknownApplicationsAreRemovedAndReported()
    {
        var ghost = TestData.Application("ghost", "Ghost");
        var discovery = new FakeDiscovery([Alpha]);
        var adapter = new FakeAdapter(result =>
        {
            result.Shortcuts.Add(TestData.Entry(ghost, "Ctrl+G"));
            return result;
        });

        var snapshot = await new ScanEngine(discovery, [adapter])
            .ScanAsync(new ScanOptions(), null, CancellationToken.None);

        Assert.Empty(snapshot.Shortcuts);
        Assert.Contains(snapshot.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("1 条快捷键", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DuplicateIdsFromDifferentAdaptersAreCountedOnce()
    {
        var entry = TestData.Entry(Alpha, "Ctrl+D");
        var discovery = new FakeDiscovery([Alpha]);
        var first = new FakeAdapter(result =>
        {
            result.CoveredApplicationIds.Add(Alpha.Id);
            result.Shortcuts.Add(entry);
            return result;
        });
        var second = new FakeAdapter(result =>
        {
            result.CoveredApplicationIds.Add(Alpha.Id);
            result.Shortcuts.Add(entry);
            return result;
        });

        var snapshot = await new ScanEngine(discovery, [first, second])
            .ScanAsync(new ScanOptions(), null, CancellationToken.None);

        Assert.Single(snapshot.Shortcuts);
    }

    [Fact]
    public async Task NoEnabledAdapterProducesWarningButCompletes()
    {
        var discovery = new FakeDiscovery([Alpha]);
        var disabled = new FakeAdapter(result => result, enabled: false);

        var snapshot = await new ScanEngine(discovery, [disabled])
            .ScanAsync(new ScanOptions(), null, CancellationToken.None);

        Assert.Empty(snapshot.Shortcuts);
        Assert.Contains(snapshot.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Warning &&
            diagnostic.Message.Contains("没有启用任何快捷键来源", StringComparison.Ordinal));
        Assert.Equal(ScanState.NotScannable, snapshot.ApplicationResults.Single().State);
    }

    [Fact]
    public async Task CancellationBeforeScanningStopsWork()
    {
        var discovery = new FakeDiscovery([Alpha]);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ScanEngine(discovery, []).ScanAsync(new ScanOptions(), null, source.Token));
    }

    [Fact]
    public async Task DiscoveryDiagnosticsArePreserved()
    {
        var diagnostic = new ScanDiagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Source = "发现",
            Message = "部分注册表项不可读"
        };
        var discovery = new FakeDiscovery([Alpha], [diagnostic]);

        var snapshot = await new ScanEngine(discovery, [])
            .ScanAsync(new ScanOptions(), null, CancellationToken.None);

        Assert.Contains(diagnostic, snapshot.Diagnostics);
    }

    [Fact]
    public async Task RunningProcessSnapshotMarksApplicationsAndGlobalCompetition()
    {
        var alpha = TestData.Application("alpha", "网易云音乐", @"C:\Apps\cloudmusic.exe");
        var beta = TestData.Application("beta", "QQ音乐", @"C:\Apps\QQMusic.exe");
        var discovery = new FakeDiscovery([alpha, beta]);
        var adapter = new FakeAdapter(result =>
        {
            result.CoveredApplicationIds.Add(alpha.Id);
            result.CoveredApplicationIds.Add(beta.Id);
            result.Shortcuts.Add(TestData.Entry(
                alpha, "Ctrl+Alt+P", ShortcutScope.Global, ShortcutSourceKind.OfficialDocumentation,
                ShortcutOrigin.OfficialDefault, ConfidenceLevel.Medium, "播放 / 暂停"));
            result.Shortcuts.Add(TestData.Entry(
                beta, "Ctrl+Alt+P", ShortcutScope.Global, ShortcutSourceKind.OfficialDocumentation,
                ShortcutOrigin.OfficialDefault, ConfidenceLevel.Medium, "播放 / 暂停"));
            return result;
        });
        var probe = new FakeProbe(["cloudmusic.exe", "QQMusic.exe"]);

        var snapshot = await new ScanEngine(discovery, [adapter], probe)
            .ScanAsync(new ScanOptions(), null, CancellationToken.None);

        Assert.All(snapshot.Applications, application => Assert.True(application.IsRunning));
        Assert.Equal(2, snapshot.Statistics.RunningApplications);
        Assert.Equal(2, snapshot.Statistics.GlobalShortcuts);
        Assert.Equal(2, snapshot.Statistics.GlobalShortcutsFromRunningApplications);

        var conflict = Assert.Single(snapshot.Conflicts);
        Assert.Equal(ConflictKind.GlobalHotkeyCompetition, conflict.Kind);
        Assert.Equal(ConflictSeverity.Medium, conflict.Severity);
        Assert.All(conflict.Participants, participant => Assert.True(participant.IsRunning));
    }

    [Fact]
    public async Task OneProcessMissingDowngradesGlobalCompetition()
    {
        var alpha = TestData.Application("alpha", "网易云音乐", @"C:\Apps\cloudmusic.exe");
        var beta = TestData.Application("beta", "QQ音乐", @"C:\Apps\QQMusic.exe");
        var discovery = new FakeDiscovery([alpha, beta]);
        var adapter = new FakeAdapter(result =>
        {
            result.CoveredApplicationIds.Add(alpha.Id);
            result.CoveredApplicationIds.Add(beta.Id);
            result.Shortcuts.Add(TestData.Entry(alpha, "Ctrl+Alt+Left", ShortcutScope.Global));
            result.Shortcuts.Add(TestData.Entry(beta, "Ctrl+Alt+Left", ShortcutScope.Global));
            return result;
        });
        var probe = new FakeProbe(["cloudmusic.exe"]);

        var snapshot = await new ScanEngine(discovery, [adapter], probe)
            .ScanAsync(new ScanOptions(), null, CancellationToken.None);

        Assert.False(snapshot.Applications.Single(application => application.Id == beta.Id).IsRunning);
        var conflict = Assert.Single(snapshot.Conflicts);
        Assert.Equal(ConflictSeverity.Low, conflict.Severity);
        Assert.Contains("未检测到运行", conflict.Summary, StringComparison.Ordinal);
        Assert.Equal(1, snapshot.Statistics.RunningApplications);
        Assert.Equal(1, snapshot.Statistics.GlobalShortcutsFromRunningApplications);
    }

    [Fact]
    public async Task ScanWithoutProbeKeepsRuntimeStateUnknown()
    {
        var discovery = new FakeDiscovery([Alpha]);
        var adapter = new FakeAdapter(result => result);

        var snapshot = await new ScanEngine(discovery, [adapter])
            .ScanAsync(new ScanOptions(), null, CancellationToken.None);

        Assert.Null(snapshot.Applications.Single().IsRunning);
        Assert.Equal(0, snapshot.Statistics.RunningApplications);
    }

    [Fact]
    public async Task FilteredProcessSnapshotDegradesUnmatchedApplicationsToUnknown()
    {
        var alpha = TestData.Application("alpha", "网易云音乐", @"C:\Apps\cloudmusic.exe");
        var beta = TestData.Application("beta", "QQ音乐", @"C:\Apps\QQMusic.exe");
        var discovery = new FakeDiscovery([alpha, beta]);
        var adapter = new FakeAdapter(result =>
        {
            result.CoveredApplicationIds.Add(alpha.Id);
            result.CoveredApplicationIds.Add(beta.Id);
            result.Shortcuts.Add(TestData.Entry(alpha, "Ctrl+Alt+Left", ShortcutScope.Global));
            result.Shortcuts.Add(TestData.Entry(beta, "Ctrl+Alt+Left", ShortcutScope.Global));
            return result;
        });
        var probe = new FakeProbe(["cloudmusic.exe"], isComplete: false);

        var snapshot = await new ScanEngine(discovery, [adapter], probe)
            .ScanAsync(new ScanOptions(), null, CancellationToken.None);

        var alphaApp = snapshot.Applications.Single(application => application.Id == alpha.Id);
        var betaApp = snapshot.Applications.Single(application => application.Id == beta.Id);
        Assert.True(alphaApp.IsRunning);
        Assert.Null(betaApp.IsRunning);
        Assert.Equal(1, snapshot.Statistics.RunningApplications);

        var conflict = Assert.Single(snapshot.Conflicts);
        Assert.Equal(ConflictSeverity.Low, conflict.Severity);
        Assert.Contains("没有采集到全部运行状态", conflict.Summary, StringComparison.Ordinal);
        Assert.Null(conflict.Participants.Single(participant => participant.ApplicationId == beta.Id).IsRunning);
    }

    private static bool IsMonotonic(IEnumerable<double> values)
    {
        double? previous = null;
        foreach (var value in values)
        {
            if (previous is not null && value < previous)
            {
                return false;
            }

            previous = value;
        }

        return true;
    }

    private sealed class FakeDiscovery : IApplicationDiscoveryService
    {
        private readonly IReadOnlyList<DiscoveredApplication> _applications;
        private readonly IReadOnlyList<ScanDiagnostic> _diagnostics;

        public FakeDiscovery(
            IReadOnlyList<DiscoveredApplication> applications,
            IReadOnlyList<ScanDiagnostic>? diagnostics = null)
        {
            _applications = applications;
            _diagnostics = diagnostics ?? Array.Empty<ScanDiagnostic>();
        }

        public Task<ApplicationDiscoveryResult> DiscoverAsync(
            ScanOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ApplicationDiscoveryResult
            {
                Applications = _applications,
                Diagnostics = _diagnostics
            });
        }
    }

    private sealed class FakeAdapter : IShortcutSourceAdapter
    {
        private readonly Func<AdapterScanResult, AdapterScanResult> _scan;
        private readonly bool _enabled;

        public FakeAdapter(
            Func<AdapterScanResult, AdapterScanResult> scan,
            bool enabled = true)
        {
            _scan = scan;
            _enabled = enabled;
        }

        public string Id => "fake";
        public string DisplayName => "测试适配器";
        public ShortcutSourceKind SourceKind => ShortcutSourceKind.UserConfiguration;
        public ScanContext? LastContext { get; private set; }

        public bool IsEnabled(ScanOptions options) => _enabled;

        public Task<AdapterScanResult> ScanAsync(
            ScanContext context,
            IProgress<ScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastContext = context;
            progress?.Report(new ScanProgress
            {
                Phase = "测试",
                Message = "测试中",
                Percent = 50
            });
            return Task.FromResult(_scan(AdapterScanResult.Empty()));
        }
    }

    private sealed class FakeProbe : IProcessSnapshotProvider
    {
        private readonly IReadOnlySet<string> _names;
        private readonly bool _isComplete;

        public FakeProbe(IEnumerable<string> processNames, bool isComplete = true)
        {
            _names = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
            _isComplete = isComplete;
        }

        public ProcessSnapshot GetSnapshot() => new(_names, _isComplete);
    }

    private sealed class InlineProgress : IProgress<ScanProgress>
    {
        public List<ScanProgress> Reports { get; } = new();

        public void Report(ScanProgress value) => Reports.Add(value);
    }
}
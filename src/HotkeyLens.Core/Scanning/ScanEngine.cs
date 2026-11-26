using System.Diagnostics;
using HotkeyLens.Core.Abstractions;
using HotkeyLens.Core.Analysis;
using HotkeyLens.Core.Models;

namespace HotkeyLens.Core.Scanning;

/// <summary>
/// Orchestrates discovery, evidence adapters and conflict analysis. Adapters
/// are isolated: one unreadable source does not abort the whole scan.
/// </summary>
public sealed class ScanEngine : IScanEngine
{
    private readonly IApplicationDiscoveryService _discovery;
    private readonly IReadOnlyList<IShortcutSourceAdapter> _adapters;
    private readonly IProcessSnapshotProvider? _processProbe;

    public ScanEngine(
        IApplicationDiscoveryService discovery,
        IEnumerable<IShortcutSourceAdapter> adapters,
        IProcessSnapshotProvider? processProbe = null)
    {
        _discovery = discovery;
        _adapters = adapters.ToList();
        _processProbe = processProbe;
    }

    public async Task<ScanSnapshot> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sanitized = options.Sanitize();
        var stopwatch = Stopwatch.StartNew();
        progress?.Report(new ScanProgress
        {
            Phase = "应用发现",
            Message = "正在读取已安装应用和开始菜单",
            Percent = 2,
            TotalSteps = 1
        });

        var discovery = await _discovery.DiscoverAsync(sanitized, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var processSnapshot = _processProbe?.GetSnapshot();
        var applications = discovery.Applications
            .Select(application => application with
            {
                IsRunning = ResolveRunningState(application.ProcessName, processSnapshot)
            })
            .ToList();
        var runningByApplication = applications.ToDictionary(
            application => application.Id,
            application => application.IsRunning,
            StringComparer.Ordinal);

        progress?.Report(new ScanProgress
        {
            Phase = "应用发现",
            Message = $"发现 {applications.Count} 个应用程序，其中 {applications.Count(application => application.IsRunning == true)} 个正在运行",
            Percent = 10,
            CompletedSteps = 1,
            TotalSteps = 1
        });

        if (processSnapshot is { IsComplete: false })
        {
            progress?.Report(new ScanProgress
            {
                Phase = "运行状态",
                Message = "进程列表数量异常偏少（可能被安全软件或会话隔离过滤），未命中应用的运行状态将标为未知。",
                Percent = 12,
                CompletedSteps = 1,
                TotalSteps = 1
            });
        }

        var adapters = _adapters.Where(adapter => adapter.IsEnabled(sanitized)).ToList();
        var diagnostics = new List<ScanDiagnostic>(discovery.Diagnostics);
        var entries = new List<ShortcutEntry>();
        var entryIds = new HashSet<string>(StringComparer.Ordinal);
        var coveredSources = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var duplicateIds = 0;
        var adapterFailures = 0;

        if (adapters.Count == 0)
        {
            diagnostics.Add(new ScanDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Source = "扫描引擎",
                Message = "当前扫描选项没有启用任何快捷键来源。",
                Detail = "请至少启用一种来源后重新扫描。"
            });
        }

        for (var index = 0; index < adapters.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = adapters[index];
            var basePercent = 10 + (85.0 * index / adapters.Count);
            var span = 85.0 / adapters.Count;
            var scaledProgress = progress is null
                ? null
                : new ScaledProgress(progress, basePercent, span);

            AdapterScanResult adapterResult;
            try
            {
                adapterResult = await adapter.ScanAsync(
                        new ScanContext
                        {
                            Applications = applications,
                            Options = sanitized,
                            StartedAt = DateTimeOffset.Now,
                            DataDirectory = GetDataDirectory()
                        },
                        scaledProgress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                adapterFailures++;
                diagnostics.Add(new ScanDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Source = adapter.DisplayName,
                    Message = $"{adapter.DisplayName} 扫描失败，其余来源会继续执行。",
                    Detail = exception.Message
                });
                progress?.Report(new ScanProgress
                {
                    Phase = adapter.DisplayName,
                    Message = "来源读取失败，已跳过",
                    Percent = basePercent + span,
                    CompletedSteps = index + 1,
                    TotalSteps = adapters.Count
                });
                continue;
            }

            diagnostics.AddRange(adapterResult.Diagnostics);
            foreach (var applicationId in adapterResult.CoveredApplicationIds)
            {
                if (!coveredSources.TryGetValue(applicationId, out var sources))
                {
                    sources = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                    coveredSources[applicationId] = sources;
                }

                sources.Add(adapter.DisplayName);
            }

            foreach (var entry in adapterResult.Shortcuts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entryIds.Add(entry.Id))
                {
                    duplicateIds++;
                    continue;
                }

                entries.Add(entry);
            }
        }

        progress?.Report(new ScanProgress
        {
            Phase = "冲突分析",
            Message = "正在按应用、作用域、上下文和数据来源分析相同按键组合",
            Percent = 95
        });

        var applicationsById = applications
            .GroupBy(application => application.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var unknownApplicationEntries = entries.RemoveAll(entry => !applicationsById.ContainsKey(entry.ApplicationId));
        if (unknownApplicationEntries > 0)
        {
            diagnostics.Add(new ScanDiagnostic
            {
                Severity = DiagnosticSeverity.Info,
                Source = "扫描引擎",
                Message = $"{unknownApplicationEntries} 条快捷键指向未出现在应用清单中的记录，已移除。"
            });
        }

        var conflicts = ShortcutConflictAnalyzer.Analyze(entries, runningByApplication);
        var applicationResults = BuildApplicationResults(
            applications,
            entries,
            coveredSources);
        var statistics = BuildStatistics(entries, applicationResults, applications, conflicts, adapterFailures);
        stopwatch.Stop();
        progress?.Report(new ScanProgress
        {
            Phase = "完成",
            Message = $"扫描完成：{entries.Count} 条快捷键，{conflicts.Count} 条冲突分析结果",
            Percent = 100,
            CompletedSteps = 1,
            TotalSteps = 1
        });

        return new ScanSnapshot
        {
            ScannedAt = DateTimeOffset.Now,
            DurationMilliseconds = stopwatch.ElapsedMilliseconds,
            Options = sanitized,
            Applications = applications,
            Shortcuts = entries
                .OrderBy(entry => entry.ApplicationName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.Scope)
                .ThenBy(entry => entry.Gesture.SortKey, StringComparer.Ordinal)
                .ThenBy(entry => entry.DisplayGesture, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.ActionName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            Conflicts = conflicts,
            ApplicationResults = applicationResults,
            Diagnostics = diagnostics,
            Statistics = statistics,
            EngineVersion = "1.2"
        };
    }

    private static bool? ResolveRunningState(string? processName, ProcessSnapshot? snapshot)
    {
        if (snapshot is null || string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        if (snapshot.Contains(processName))
        {
            return true;
        }

        // A filtered process table cannot prove that an application is closed.
        return snapshot.IsComplete ? false : null;
    }

    private static IReadOnlyList<ApplicationScanResult> BuildApplicationResults(
        IReadOnlyList<DiscoveredApplication> applications,
        IReadOnlyList<ShortcutEntry> shortcuts,
        IReadOnlyDictionary<string, HashSet<string>> coveredSources)
    {
        var counts = shortcuts
            .GroupBy(entry => entry.ApplicationId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var sourcesByApplication = shortcuts
            .GroupBy(entry => entry.ApplicationId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.SourceName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
                StringComparer.Ordinal);

        var results = new List<ApplicationScanResult>(applications.Count);
        foreach (var application in applications)
        {
            counts.TryGetValue(application.Id, out var count);
            coveredSources.TryGetValue(application.Id, out var checkedSourceSet);
            sourcesByApplication.TryGetValue(application.Id, out var discoveredSources);
            var checkedSources = (checkedSourceSet ?? [])
                .Concat(discoveredSources ?? [])
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            ScanState state;
            string detail;
            if (count > 0)
            {
                state = ScanState.Scanned;
                detail = $"从 {checkedSources.Count} 个来源读取到 {count} 条快捷键。";
            }
            else if (checkedSources.Count > 0)
            {
                state = ScanState.NoShortcutsFound;
                detail = $"已检查 {string.Join("、", checkedSources)}，没有找到带快捷键标注的条目。";
            }
            else
            {
                state = ScanState.NotScannable;
                detail =
                    "没有找到该应用的可读键位配置、快捷方式热键或官方目录记录。这不等于应用没有快捷键。";
            }

            results.Add(new ApplicationScanResult
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                State = state,
                ShortcutCount = count,
                Detail = detail,
                CheckedSources = checkedSources
            });
        }

        return results;
    }

    private static ScanStatistics BuildStatistics(
        IReadOnlyList<ShortcutEntry> shortcuts,
        IReadOnlyList<ApplicationScanResult> applicationResults,
        IReadOnlyList<DiscoveredApplication> applications,
        IReadOnlyList<ShortcutConflict> conflicts,
        int adapterFailures)
    {
        var byScope = Enum.GetValues<ShortcutScope>()
            .ToDictionary(scope => scope, scope => shortcuts.Count(entry => entry.Scope == scope));
        var bySource = Enum.GetValues<ShortcutSourceKind>()
            .ToDictionary(kind => kind, kind => shortcuts.Count(entry => entry.SourceKind == kind));
        var analyzedGroups = shortcuts
            .Where(entry => entry.IsEnabled && !entry.IsSequence)
            .Select(entry => entry.Gesture.Canonical)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var findingGroups = conflicts
            .Select(conflict => conflict.Gesture.Canonical)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var runningIds = applications
            .Where(application => application.IsRunning == true)
            .Select(application => application.Id)
            .ToHashSet(StringComparer.Ordinal);

        return new ScanStatistics
        {
            TotalApplications = applicationResults.Count,
            ScannedApplications = applicationResults.Count(result =>
                result.State is ScanState.Scanned or ScanState.NoShortcutsFound),
            NoShortcutsApplications = applicationResults.Count(result =>
                result.State == ScanState.NoShortcutsFound),
            NotScannableApplications = applicationResults.Count(result =>
                result.State == ScanState.NotScannable),
            FailedApplications = 0,
            TotalShortcuts = shortcuts.Count,
            HighConfidenceShortcuts = shortcuts.Count(entry => entry.Confidence == ConfidenceLevel.High),
            UserConfiguredShortcuts = shortcuts.Count(entry => entry.Origin == ShortcutOrigin.UserConfigured),
            DocumentedDefaultShortcuts = shortcuts.Count(entry =>
                entry.Origin == ShortcutOrigin.OfficialDefault),
            RuntimeObservedShortcuts = shortcuts.Count(entry =>
                entry.Origin == ShortcutOrigin.RuntimeObserved),
            GlobalShortcuts = shortcuts.Count(entry => entry.Scope == ShortcutScope.Global),
            GlobalShortcutsFromRunningApplications = shortcuts.Count(entry =>
                entry.Scope == ShortcutScope.Global && runningIds.Contains(entry.ApplicationId)),
            RunningApplications = runningIds.Count,
            DuplicateGroups = conflicts.Count(conflict =>
                conflict.Classification == ConflictClassification.NormalDuplicate),
            NormalDuplicates = conflicts.Count(conflict =>
                conflict.Classification == ConflictClassification.NormalDuplicate),
            PotentialConflicts = conflicts.Count(conflict =>
                conflict.Classification == ConflictClassification.PotentialConflict),
            ConfirmedConflicts = conflicts.Count(conflict =>
                conflict.Classification == ConflictClassification.ConfirmedConflict),
            GlobalCompetitions = conflicts.Count(conflict =>
                conflict.Classification == ConflictClassification.ConfirmedConflict &&
                conflict.Kind == ConflictKind.GlobalHotkeyCompetition),
            UnknownFindings = conflicts.Count(conflict =>
                conflict.Classification == ConflictClassification.Unknown),
            NoConflictGroups = Math.Max(0, analyzedGroups - findingGroups),
            ByScope = byScope,
            BySource = bySource
        };
    }

    private static string GetDataDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HotkeyLens");

    private sealed class ScaledProgress : IProgress<ScanProgress>
    {
        private readonly IProgress<ScanProgress> _inner;
        private readonly double _basePercent;
        private readonly double _span;

        public ScaledProgress(IProgress<ScanProgress> inner, double basePercent, double span)
        {
            _inner = inner;
            _basePercent = basePercent;
            _span = span;
        }

        public void Report(ScanProgress value)
        {
            var innerPercent = Math.Clamp(value.Percent, 0, 100);
            _inner.Report(value with
            {
                Percent = Math.Clamp(_basePercent + (_span * innerPercent / 100.0), 0, 100)
            });
        }
    }
}
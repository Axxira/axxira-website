using System.Collections.Concurrent;

namespace RaidPilot.Overlay;

internal sealed class ScreenshotFolderWatcher : IDisposable
{
    private static readonly TimeSpan InitialCatchUpClockTolerance = TimeSpan.FromSeconds(2);
    private sealed record StartupConfiguration(
        string ScreenshotsPath,
        Func<DateTimeOffset, ScreenshotCleanupResult> Cleanup);

    private readonly string _screenshotsPath;
    private readonly Func<DateTimeOffset, ScreenshotCleanupResult>? _startupCleanup;
    private readonly TimeSpan _probeInterval;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _knownFileNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _folderProbe;
    private FileSystemWatcher? _watcher;
    private DateTimeOffset _activatedAtUtc;
    private bool _initialCatchUpPending;
    private bool _startupCleanupPlanned;
    private bool _startupCleanupInProgress;
    private bool _started;
    private bool _disposed;
    private int _startupCleanupCompletionSignaled;
    private ScreenshotCleanupResult? _lastStartupCleanupResult;

    internal ScreenshotFolderWatcher()
        : this(CreateProductionConfiguration())
    {
    }

    private ScreenshotFolderWatcher(StartupConfiguration configuration)
        : this(
            configuration.ScreenshotsPath,
            TimeSpan.FromSeconds(5),
            static () => DateTimeOffset.UtcNow,
            IsStartupCleanupSuppressed(Environment.GetEnvironmentVariable("RAIDPILOT_PRESERVE_SCREENSHOTS_ONCE"))
                ? null
                : configuration.Cleanup)
    {
    }

    internal ScreenshotFolderWatcher(
        string screenshotsPath,
        TimeSpan probeInterval,
        Func<DateTimeOffset>? utcNow = null)
        : this(screenshotsPath, probeInterval, utcNow, startupCleanup: null)
    {
    }

    private ScreenshotFolderWatcher(
        string screenshotsPath,
        TimeSpan probeInterval,
        Func<DateTimeOffset>? utcNow,
        Func<DateTimeOffset, ScreenshotCleanupResult>? startupCleanup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenshotsPath);
        if (probeInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(probeInterval));
        }

        _screenshotsPath = Path.GetFullPath(screenshotsPath);
        _startupCleanup = startupCleanup;
        _probeInterval = probeInterval;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _folderProbe = new System.Threading.Timer(_ => EnsureWatcher(), null, Timeout.Infinite, Timeout.Infinite);
    }

    internal event EventHandler<ScreenshotObservation>? ObservationCreated;
    internal event EventHandler<string>? StatusChanged;

    internal string ScreenshotsPath => _screenshotsPath;

    internal ScreenshotCleanupResult? LastStartupCleanupResult
    {
        get
        {
            lock (_gate)
            {
                return _lastStartupCleanupResult;
            }
        }
    }

    internal IReadOnlySet<string> SnapshotKnownFileNames()
    {
        return _knownFileNames.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal void Start()
    {
        Func<DateTimeOffset, ScreenshotCleanupResult>? cleanup = null;
        DateTimeOffset activatedAtUtc;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _started = true;
            _activatedAtUtc = _utcNow();
            activatedAtUtc = _activatedAtUtc;
            if (!_startupCleanupPlanned && _startupCleanup is not null)
            {
                _startupCleanupPlanned = true;
                _startupCleanupInProgress = true;
                Volatile.Write(ref _startupCleanupCompletionSignaled, 0);
                cleanup = _startupCleanup;
            }
            _initialCatchUpPending = true;
            _seen.Clear();
            _knownFileNames.Clear();
        }

        // Attach the lightweight native watcher before scheduling any scan or
        // cleanup. Start therefore returns promptly, while Created/Renamed
        // events are already active for a raid beginning at this instant.
        EnsureWatcher(allowCatchUp: false, notifyStatus: false);
        lock (_gate)
        {
            if (_started && !_disposed)
            {
                _folderProbe.Change(TimeSpan.Zero, _probeInterval);
            }
        }

        if (cleanup is not null)
        {
            try
            {
                _ = Task.Run(() => RunStartupCleanup(cleanup, activatedAtUtc));
            }
            catch
            {
                CompleteStartupCleanup(ScreenshotCleanupResult.FatalFailure());
            }
        }
    }

    internal static ScreenshotFolderWatcher CreateWithStartupCleanupForTesting(
        string myDocumentsPath,
        TimeSpan probeInterval,
        Func<DateTimeOffset>? utcNow = null,
        Func<DateTimeOffset, ScreenshotCleanupResult>? cleanupOverride = null)
    {
        if (!ScreenshotFolderCleaner.TryResolveScreenshotsPath(
                myDocumentsPath,
                out var screenshotsPath))
        {
            throw new ArgumentException("Le dossier Documents de test doit être absolu et sûr.", nameof(myDocumentsPath));
        }

        return new ScreenshotFolderWatcher(
            screenshotsPath,
            probeInterval,
            utcNow,
            cleanupOverride
                ?? (cutoff => ScreenshotFolderCleaner.ExecuteForDocumentsRoot(myDocumentsPath, cutoff)));
    }

    internal static bool IsStartupCleanupSuppressed(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal);

    private static StartupConfiguration CreateProductionConfiguration()
    {
        if (!ScreenshotFolderCleaner.TryGetProductionPaths(
                out var myDocumentsPath,
                out var screenshotsPath))
        {
            throw new InvalidOperationException("Le dossier Documents Windows est indisponible.");
        }

        return new StartupConfiguration(
            screenshotsPath,
            cutoff => ScreenshotFolderCleaner.ExecuteForDocumentsRoot(myDocumentsPath, cutoff));
    }

    internal void Stop()
    {
        lock (_gate)
        {
            _started = false;
            _initialCatchUpPending = false;
            _folderProbe.Change(Timeout.Infinite, Timeout.Infinite);
            DisposeWatcherLocked();
        }
    }

    private void EnsureWatcher(bool allowCatchUp = true, bool notifyStatus = true)
    {
        string? recentFileName = null;
        string? status = null;
        DateTimeOffset activatedAtUtc = default;
        DateTimeOffset nowUtc = default;
        lock (_gate)
        {
            if (!_started || _disposed)
            {
                return;
            }

            if (!Directory.Exists(_screenshotsPath))
            {
                if (_watcher is not null)
                {
                    DisposeWatcherLocked();
                    _activatedAtUtc = _utcNow();
                    _initialCatchUpPending = true;
                }

                status = "Guidage prêt · dossier captures attendu";
            }
            else
            {
                if (_watcher is null)
                {
                    try
                    {
                        var watcher = new FileSystemWatcher(_screenshotsPath, "*.png")
                        {
                            IncludeSubdirectories = false,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                            EnableRaisingEvents = false,
                        };
                        watcher.Created += OnFileAppeared;
                        watcher.Renamed += OnFileAppeared;
                        watcher.Error += OnWatcherError;
                        watcher.EnableRaisingEvents = true;
                        _watcher = watcher;
                    }
                    catch (Exception exception) when (exception is IOException
                        or UnauthorizedAccessException
                        or ArgumentException)
                    {
                        status = "Guidage en attente · reprise des captures";
                    }
                }

                if (_watcher is not null)
                {
                    status = "Guidage local actif · captures surveillées";
                    if (allowCatchUp
                        && _initialCatchUpPending
                        && !_startupCleanupInProgress)
                    {
                        _initialCatchUpPending = false;
                        activatedAtUtc = _activatedAtUtc;
                        nowUtc = _utcNow();
                    }
                }
            }
        }

        if (notifyStatus && status is not null)
        {
            StatusChanged?.Invoke(this, status);
        }

        if (activatedAtUtc != default)
        {
            // The disk scan stays on the timer/cleanup worker, never Start's
            // UI thread. Created events are already active and _seen removes a
            // duplicate if this bounded catch-up finds the same image.
            recentFileName = FindNewestRecentScreenshot(
                _screenshotsPath,
                activatedAtUtc,
                nowUtc);
            if (recentFileName is not null)
            {
                ProcessFileName(recentFileName);
            }
        }
    }

    private void RunStartupCleanup(
        Func<DateTimeOffset, ScreenshotCleanupResult> cleanup,
        DateTimeOffset activatedAtUtc)
    {
        var result = ScreenshotCleanupResult.FatalFailure();
        try
        {
            result = cleanup(activatedAtUtc) ?? ScreenshotCleanupResult.FatalFailure();
        }
        catch
        {
            result = ScreenshotCleanupResult.FatalFailure();
        }
        finally
        {
            CompleteStartupCleanup(result);
        }
    }

    private void CompleteStartupCleanup(ScreenshotCleanupResult result)
    {
        if (Interlocked.Exchange(ref _startupCleanupCompletionSignaled, 1) != 0)
        {
            return;
        }

        var requestCatchUp = false;
        lock (_gate)
        {
            _lastStartupCleanupResult = result;
            foreach (var deletedFileName in result.DeletedFileNames)
            {
                _knownFileNames.TryRemove(deletedFileName, out _);
            }
            _startupCleanupInProgress = false;
            if (_started && !_disposed)
            {
                requestCatchUp = true;
                _folderProbe.Change(TimeSpan.Zero, _probeInterval);
            }
        }

        var residualCount = Math.Max(result.FailedCount, result.ResidualFileNames.Count);
        if (requestCatchUp && residualCount > 0)
        {
            StatusChanged?.Invoke(
                this,
                $"Guidage actif · {residualCount} ancienne(s) capture(s) conservée(s)");
        }
    }

    private void OnFileAppeared(object sender, FileSystemEventArgs args)
    {
        ProcessFileName(args.Name);
    }

    private void ProcessFileName(string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            _knownFileNames.TryAdd(Path.GetFileName(fileName), 0);
        }

        if (!ScreenshotNameParser.TryParse(fileName, out var observation) || observation is null)
        {
            return;
        }

        var now = _utcNow();
        if (!_seen.TryAdd(observation.FileName, now))
        {
            return;
        }

        foreach (var stale in _seen.Where(pair => now - pair.Value > TimeSpan.FromMinutes(10)).Select(pair => pair.Key))
        {
            _seen.TryRemove(stale, out _);
        }

        ObservationCreated?.Invoke(this, observation);
    }

    internal static string? FindNewestRecentScreenshot(
        string screenshotsPath,
        DateTimeOffset activatedAtUtc,
        DateTimeOffset nowUtc)
    {
        var earliestAccepted = activatedAtUtc - InitialCatchUpClockTolerance;
        var latestAccepted = nowUtc + InitialCatchUpClockTolerance;
        string? newestName = null;
        var newestTimestamp = DateTimeOffset.MinValue;

        try
        {
            foreach (var file in new DirectoryInfo(screenshotsPath).EnumerateFiles("*.png", SearchOption.TopDirectoryOnly))
            {
                if (!ScreenshotNameParser.TryParse(file.Name, out _))
                {
                    continue;
                }

                var creationTime = new DateTimeOffset(file.CreationTimeUtc);
                var lastWriteTime = new DateTimeOffset(file.LastWriteTimeUtc);
                var timestamp = creationTime > lastWriteTime ? creationTime : lastWriteTime;
                if (timestamp < earliestAccepted
                    || timestamp > latestAccepted
                    || timestamp <= newestTimestamp)
                {
                    continue;
                }

                newestName = file.Name;
                newestTimestamp = timestamp;
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException)
        {
            return null;
        }

        return newestName;
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        lock (_gate)
        {
            DisposeWatcherLocked();
            _activatedAtUtc = _utcNow();
            _initialCatchUpPending = true;
        }

        StatusChanged?.Invoke(this, "Guidage en attente · reprise des captures");
    }

    private void DisposeWatcherLocked()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileAppeared;
        _watcher.Renamed -= OnFileAppeared;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _started = false;
            _initialCatchUpPending = false;
            _folderProbe.Change(Timeout.Infinite, Timeout.Infinite);
            DisposeWatcherLocked();
        }

        _folderProbe.Dispose();
    }
}

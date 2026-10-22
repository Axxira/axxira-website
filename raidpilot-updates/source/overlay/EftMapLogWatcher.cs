using System.Buffers;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace RaidPilot.Overlay;

public sealed class EftMapDetectedEventArgs(EftMapRoute map) : EventArgs
{
    public EftMapRoute Map { get; } = map;
    public string NormalizedName => Map.NormalizedName;
    public string Route => Map.Route;
}

/// <summary>
/// Watches only bytes appended to EFT application logs. No raw line, account id,
/// raid id, or profile id is retained or exposed.
/// </summary>
public sealed class EftMapLogWatcher : IDisposable
{
    private const int ReadBufferSize = 16 * 1024;
    private const int MaximumLineBytes = 128 * 1024;
    private const int InitialTailBytes = 1024 * 1024;
    private static readonly TimeSpan DefaultReconciliationInterval = TimeSpan.FromMilliseconds(1500);

    private static readonly string[] UninstallRegistryPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 3932890",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 3932890",
        @"SOFTWARE\Battlestate Games\EscapeFromTarkov",
        @"SOFTWARE\Battlestate Games\EFT",
    ];

    private readonly object _gate = new();
    private readonly object _notificationGate = new();
    private readonly string? _logsDirectory;
    private readonly TimeSpan _reconciliationInterval;
    private readonly bool _watchFileSystemEvents;
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _reconciliationTimer;
    private string? _currentLogPath;
    private long _position;
    private bool _discardUntilNewline;
    private string? _lastMapName;
    private DateTime? _lastMapDetectionLogTime;
    private bool _raidEndPending;
    private bool _lastLifecycleEventWasRaidEnd;
    private bool _started;
    private bool _disposed;

    public EftMapLogWatcher(string? logsDirectory = null)
        : this(logsDirectory, DefaultReconciliationInterval, watchFileSystemEvents: true)
    {
    }

    internal EftMapLogWatcher(
        string? logsDirectory,
        TimeSpan reconciliationInterval,
        bool watchFileSystemEvents)
    {
        if (reconciliationInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconciliationInterval),
                "The reconciliation interval must be positive.");
        }

        _logsDirectory = logsDirectory is null
            ? DiscoverLogsDirectory()
            : NormalizeExistingDirectory(logsDirectory);
        _reconciliationInterval = reconciliationInterval;
        _watchFileSystemEvents = watchFileSystemEvents;
    }

    public event EventHandler<EftMapDetectedEventArgs>? MapDetected;
    public event EventHandler? RaidEnded;

    /// <summary>Starts silently. False means EFT logs are not currently available.</summary>
    public bool Start()
    {
        lock (_notificationGate)
        {
            EftMapRoute? initialMap = null;
            bool available;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_started)
                {
                    return _logsDirectory is not null && Directory.Exists(_logsDirectory);
                }

                _started = true;
                if (_logsDirectory is null || !Directory.Exists(_logsDirectory))
                {
                    return false;
                }

                var latest = FindLatestApplicationLog(_logsDirectory);
                if (latest is not null)
                {
                    AttachLogLocked(latest, startAtEnd: true);
                    initialMap = ReadLatestMapFromBoundedTailLocked();
                }

                if (_watchFileSystemEvents)
                {
                    TryCreateWatcherLocked();
                }
                CreateReconciliationTimerLocked();
                available = true;
            }

            if (initialMap is not null)
            {
                MapDetected?.Invoke(this, new EftMapDetectedEventArgs(initialMap));
            }

            return available;
        }
    }

    public void Stop()
    {
        FileSystemWatcher? watcherToDispose;
        System.Threading.Timer? timerToDispose;
        lock (_notificationGate)
        {
            lock (_gate)
            {
                _started = false;
                timerToDispose = DetachReconciliationTimerLocked();
                watcherToDispose = DetachWatcherLocked();
                ResetTailLocked();
            }
        }

        timerToDispose?.Dispose();
        DisposeDetachedWatcher(watcherToDispose);
    }

    /// <summary>
    /// Resolves known registry entries and conventional install locations only;
    /// it never scans drives or walks arbitrary folders.
    /// </summary>
    public static string? DiscoverLogsDirectory()
    {
        foreach (var installDirectory in EnumerateInstallDirectoryCandidates())
        {
            var logsDirectory = ResolveLogsDirectory(installDirectory);
            if (logsDirectory is not null)
            {
                return logsDirectory;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves one known EFT install location without searching outside it.
    /// </summary>
    public static string? ResolveLogsDirectory(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return null;
        }

        foreach (var candidate in new[]
        {
            Path.Combine(installDirectory, "Logs"),
            Path.Combine(installDirectory, "build", "Logs"),
        })
        {
            var existing = NormalizeExistingDirectory(candidate);
            if (existing is not null)
            {
                return existing;
            }
        }

        var direct = NormalizeExistingDirectory(installDirectory);
        if (direct is not null
            && (string.Equals(
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(direct)),
                    "Logs",
                    StringComparison.OrdinalIgnoreCase)
                || ContainsApplicationLogDirectly(direct)))
        {
            return direct;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateInstallDirectoryCandidates()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var registryPath in ReadRegistryInstallDirectories())
        {
            candidates.Add(registryPath);
        }

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        AddConventionalRoots(candidates, systemRoot);
        AddConventionalRoots(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddConventionalRoots(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        return candidates;
    }

    private static void AddConventionalRoots(HashSet<string> candidates, string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        candidates.Add(Path.Combine(root, "Battlestate Games", "EFT"));
        candidates.Add(Path.Combine(root, "Battlestate Games", "EscapeFromTarkov"));
        candidates.Add(Path.Combine(root, "Battlestate Games", "Escape from Tarkov"));
        candidates.Add(Path.Combine(root, "Games", "EFT"));
        candidates.Add(Path.Combine(root, "Games", "Escape from Tarkov"));
        candidates.Add(Path.Combine(root, "Steam", "steamapps", "common", "Escape from Tarkov"));
    }

    private static IEnumerable<string> ReadRegistryInstallDirectories()
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hives = new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
        var views = new[] { RegistryView.Default, RegistryView.Registry32, RegistryView.Registry64 };

        foreach (var hive in hives)
        {
            foreach (var view in views)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    foreach (var keyPath in UninstallRegistryPaths)
                    {
                        using var key = baseKey.OpenSubKey(keyPath, writable: false);
                        foreach (var valueName in new[] { "InstallLocation", "Path", "InstallPath" })
                        {
                            var value = key?.GetValue(valueName) as string;
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                results.Add(value.Trim().Trim('"'));
                            }
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException
                    or System.Security.SecurityException
                    or PlatformNotSupportedException)
                {
                    // An unavailable registry view is a normal, silent discovery miss.
                }
            }
        }

        return results;
    }

    private static bool ContainsApplicationLogDirectly(string directory)
    {
        try
        {
            return Directory
                .EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly)
                .Any(IsApplicationLog);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string? NormalizeExistingDirectory(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)
                ? null
                : Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    private void CreateWatcherLocked()
    {
        if (_logsDirectory is null || !Directory.Exists(_logsDirectory))
        {
            return;
        }

        var watcher = new FileSystemWatcher(_logsDirectory, "*application*.log")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = false,
        };
        watcher.Created += OnLogChanged;
        watcher.Changed += OnLogChanged;
        watcher.Renamed += OnLogChanged;
        watcher.Error += OnWatcherError;
        _watcher = watcher;
        try
        {
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher = null;
            watcher.Created -= OnLogChanged;
            watcher.Changed -= OnLogChanged;
            watcher.Renamed -= OnLogChanged;
            watcher.Error -= OnWatcherError;
            watcher.Dispose();
            throw;
        }
    }

    private void TryCreateWatcherLocked()
    {
        if (_watcher is not null)
        {
            return;
        }

        try
        {
            CreateWatcherLocked();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            // The periodic reconciliation remains authoritative and retries.
        }
    }

    private void OnLogChanged(object sender, FileSystemEventArgs args)
    {
        if (!IsApplicationLog(args.FullPath))
        {
            return;
        }

        lock (_notificationGate)
        {
            List<EftMapRoute>? detections = null;
            lock (_gate)
            {
                if (!_started || _disposed || !ReferenceEquals(sender, _watcher))
                {
                    return;
                }

                detections = string.Equals(
                    _currentLogPath,
                    args.FullPath,
                    StringComparison.OrdinalIgnoreCase)
                    ? ReadAppendedMapsLocked()
                    : ReconcileLatestLogLocked();
            }

            Publish(detections);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        FileSystemWatcher? watcherToDispose = null;
        lock (_notificationGate)
        {
            List<EftMapRoute>? detections = null;
            lock (_gate)
            {
                if (!_started || _disposed || !ReferenceEquals(sender, _watcher))
                {
                    return;
                }

                watcherToDispose = DetachWatcherLocked();
                if (_logsDirectory is not null && Directory.Exists(_logsDirectory))
                {
                    detections = ReconcileLatestLogLocked();
                    TryCreateWatcherLocked();
                }
            }

            Publish(detections);
        }

        if (watcherToDispose is not null)
        {
            ThreadPool.QueueUserWorkItem(
                static state => DisposeDetachedWatcher((FileSystemWatcher?)state),
                watcherToDispose);
        }
    }

    private void CreateReconciliationTimerLocked()
    {
        _reconciliationTimer ??= new System.Threading.Timer(
            callback: OnReconciliationTimer,
            state: null,
            dueTime: _reconciliationInterval,
            period: Timeout.InfiniteTimeSpan);
    }

    private void OnReconciliationTimer(object? state)
    {
        lock (_notificationGate)
        {
            List<EftMapRoute>? detections = null;
            try
            {
                lock (_gate)
                {
                    if (!_started || _disposed)
                    {
                        return;
                    }

                    detections = ReconcileLatestLogLocked();
                    if (_watchFileSystemEvents && _watcher is null)
                    {
                        TryCreateWatcherLocked();
                    }
                }

                Publish(detections);
            }
            finally
            {
                lock (_gate)
                {
                    if (_started && !_disposed)
                    {
                        _reconciliationTimer?.Change(
                            _reconciliationInterval,
                            Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }
    }

    private List<EftMapRoute>? ReconcileLatestLogLocked()
    {
        if (_logsDirectory is null || !Directory.Exists(_logsDirectory))
        {
            return null;
        }

        var latest = FindLatestApplicationLog(_logsDirectory);
        if (latest is null)
        {
            return null;
        }

        if (!string.Equals(latest, _currentLogPath, StringComparison.OrdinalIgnoreCase))
        {
            AttachLogLocked(latest, startAtEnd: true);
            var latestMap = ReadLatestMapFromBoundedTailLocked();
            return latestMap is null ? null : [latestMap];
        }

        return ReadAppendedMapsLocked();
    }

    private void AttachLogLocked(string path, bool startAtEnd)
    {
        _currentLogPath = path;
        _discardUntilNewline = false;
        _lastMapName = null;
        _lastMapDetectionLogTime = null;
        _raidEndPending = false;
        _lastLifecycleEventWasRaidEnd = false;
        try
        {
            _position = startAtEnd ? new FileInfo(path).Length : 0;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or FileNotFoundException)
        {
            _position = 0;
        }
    }

    private List<EftMapRoute>? ReadAppendedMapsLocked()
    {
        if (_currentLogPath is null)
        {
            return null;
        }

        byte[]? readBuffer = null;
        byte[]? lineBuffer = null;
        List<EftMapRoute>? detections = null;
        try
        {
            using var stream = new FileStream(
                _currentLogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                ReadBufferSize,
                FileOptions.SequentialScan);

            if (stream.Length < _position)
            {
                _position = 0;
                _discardUntilNewline = false;
                _lastMapName = null;
                _lastMapDetectionLogTime = null;
            }

            stream.Position = _position;
            var committedPosition = _position;
            var absolutePosition = _position;
            var lineLength = 0;
            readBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
            lineBuffer = ArrayPool<byte>.Shared.Rent(MaximumLineBytes);

            int bytesRead;
            while ((bytesRead = stream.Read(readBuffer, 0, ReadBufferSize)) > 0)
            {
                for (var index = 0; index < bytesRead; index++)
                {
                    var value = readBuffer[index];
                    absolutePosition++;

                    if (_discardUntilNewline)
                    {
                        committedPosition = absolutePosition;
                        if (value == (byte)'\n')
                        {
                            _discardUntilNewline = false;
                        }
                        continue;
                    }

                    if (value == (byte)'\n')
                    {
                        var contentLength = lineLength > 0 && lineBuffer[lineLength - 1] == (byte)'\r'
                            ? lineLength - 1
                            : lineLength;
                        if (contentLength > 0)
                        {
                            var line = Encoding.UTF8.GetString(lineBuffer, 0, contentLength);
                            if (IsRaidEndedLine(line))
                            {
                                _raidEndPending = true;
                                _lastLifecycleEventWasRaidEnd = true;
                                // Solo training can reload the same scene without a
                                // NetworkGameCreate line. An ended raid must not
                                // suppress the next scene as a same-map duplicate.
                                _lastMapName = null;
                                _lastMapDetectionLogTime = null;
                            }
                            if (EftMapLogParser.TryParseLine(line, out var map)
                                && map is not null)
                            {
                                _lastLifecycleEventWasRaidEnd = false;
                                var sameMap = string.Equals(
                                    map.NormalizedName,
                                    _lastMapName,
                                    StringComparison.Ordinal);
                                var logTime = TryParseLogTimestamp(line);
                                var sameMapNewRaid = sameMap
                                    && line.Contains("TRACE-NetworkGameCreate", StringComparison.Ordinal)
                                    && logTime is not null
                                    && (_lastMapDetectionLogTime is null
                                        || logTime.Value - _lastMapDetectionLogTime.Value
                                            >= TimeSpan.FromSeconds(30));
                                if (!sameMap || sameMapNewRaid)
                                {
                                    _lastMapName = map.NormalizedName;
                                    _lastMapDetectionLogTime = logTime;
                                    (detections ??= []).Add(map);
                                }
                            }
                        }

                        lineLength = 0;
                        committedPosition = absolutePosition;
                        continue;
                    }

                    if (lineLength < MaximumLineBytes)
                    {
                        lineBuffer[lineLength++] = value;
                    }
                    else
                    {
                        // Skip pathological lines without retaining their content.
                        lineLength = 0;
                        _discardUntilNewline = true;
                        committedPosition = absolutePosition;
                    }
                }
            }

            // An incomplete final line is deliberately not committed; it will be
            // re-read once its terminating newline is appended.
            _position = committedPosition;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or FileNotFoundException)
        {
            // Files can rotate between a watcher event and the shared-read open.
        }
        finally
        {
            if (readBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(readBuffer, clearArray: true);
            }
            if (lineBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(lineBuffer, clearArray: true);
            }
        }

        return detections;
    }

    private EftMapRoute? ReadLatestMapFromBoundedTailLocked()
    {
        if (_currentLogPath is null)
        {
            return null;
        }

        long endPosition;
        try
        {
            endPosition = new FileInfo(_currentLogPath).Length;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or FileNotFoundException)
        {
            return null;
        }

        _position = Math.Max(0, endPosition - InitialTailBytes);
        _discardUntilNewline = _position > 0;
        _lastMapName = null;
        _lastMapDetectionLogTime = null;
        var detections = ReadAppendedMapsLocked();

        // ReadAppendedMapsLocked leaves the cursor on the last complete line it
        // actually consumed, including bytes appended while this snapshot ran.
        var latestMap = _lastLifecycleEventWasRaidEnd
            ? null
            : detections?.LastOrDefault();
        _lastMapName = latestMap?.NormalizedName;
        // The bounded tail establishes startup state synchronously. Do not
        // replay an older end marker on the next reconciliation tick.
        _raidEndPending = false;
        return latestMap;
    }

    private static DateTime? TryParseLogTimestamp(string line)
    {
        const int TimestampLength = 23;
        if (line.Length < TimestampLength)
        {
            return null;
        }

        return DateTime.TryParseExact(
            line.AsSpan(0, TimestampLength),
            "yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var timestamp)
            ? timestamp
            : null;
    }

    private void Publish(List<EftMapRoute>? detections)
    {
        var raidEnded = _raidEndPending;
        _raidEndPending = false;

        if (raidEnded)
        {
            RaidEnded?.Invoke(this, EventArgs.Empty);
        }

        if (detections is null)
        {
            return;
        }

        foreach (var map in detections)
        {
            MapDetected?.Invoke(this, new EftMapDetectedEventArgs(map));
        }
    }

    internal static bool IsRaidEndedLine(string? line) =>
        !string.IsNullOrWhiteSpace(line)
        && line.Contains(
            "BEClient exit successfully",
            StringComparison.OrdinalIgnoreCase);

    private static string? FindLatestApplicationLog(string logsDirectory)
    {
        string? latestPath = null;
        DateTime latestWrite = DateTime.MinValue;

        void ConsiderDirectory(string directory)
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
                {
                    if (!IsApplicationLog(path))
                    {
                        continue;
                    }

                    var writeTime = File.GetLastWriteTimeUtc(path);
                    if (latestPath is null
                        || writeTime > latestWrite
                        || (writeTime == latestWrite
                            && string.Compare(path, latestPath, StringComparison.OrdinalIgnoreCase) > 0))
                    {
                        latestPath = path;
                        latestWrite = writeTime;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or DirectoryNotFoundException)
            {
                // A rotating session folder can disappear during enumeration.
            }
        }

        ConsiderDirectory(logsDirectory);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(logsDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                ConsiderDirectory(directory);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException)
        {
        }

        return latestPath;
    }

    private static bool IsApplicationLog(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith("application.log", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("application_000.log", StringComparison.OrdinalIgnoreCase);
    }

    private FileSystemWatcher? DetachWatcherLocked()
    {
        var watcher = _watcher;
        if (watcher is null)
        {
            return null;
        }

        _watcher = null;
        watcher.Created -= OnLogChanged;
        watcher.Changed -= OnLogChanged;
        watcher.Renamed -= OnLogChanged;
        watcher.Error -= OnWatcherError;
        return watcher;
    }

    private System.Threading.Timer? DetachReconciliationTimerLocked()
    {
        var timer = _reconciliationTimer;
        _reconciliationTimer = null;
        return timer;
    }

    private static void DisposeDetachedWatcher(FileSystemWatcher? watcher)
    {
        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.EnableRaisingEvents = false;
        }
        catch (ObjectDisposedException)
        {
        }
        watcher.Dispose();
    }

    private void ResetTailLocked()
    {
        _currentLogPath = null;
        _position = 0;
        _discardUntilNewline = false;
        _lastMapName = null;
        _lastMapDetectionLogTime = null;
    }

    public void Dispose()
    {
        FileSystemWatcher? watcherToDispose;
        System.Threading.Timer? timerToDispose;
        lock (_notificationGate)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _started = false;
                timerToDispose = DetachReconciliationTimerLocked();
                watcherToDispose = DetachWatcherLocked();
                ResetTailLocked();
            }
        }

        timerToDispose?.Dispose();
        DisposeDetachedWatcher(watcherToDispose);
    }
}

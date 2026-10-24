using System.Text;

namespace RaidPilot.Overlay;

/// <summary>
/// Read-only, bounded tail of EFT push-notification logs. Starts at EOF, never
/// replays previous sessions implicitly and never writes to the game folder.
/// Raw log lines and message bodies are not persisted or transmitted.
/// </summary>
internal sealed class EftQuestLogWatcher : IDisposable
{
    private const int MaximumReadBytesPerPoll = 256 * 1024;
    private const int MaximumPayloadCharacters = 64 * 1024;
    private readonly object _gate = new();
    private readonly string? _logsDirectory;
    private readonly TimeSpan _pollEvery;
    private System.Threading.Timer? _timer;
    private bool _started, _disposed;
    private string? _currentPath;
    private long _cursor;
    private readonly StringBuilder _payload = new();
    private bool _collecting, _insideString, _escapeNext;
    private int _braceDepth;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _recent = new();

    internal event Action<EftQuestLogEvent>? QuestChanged;

    internal EftQuestLogWatcher(string? logsDirectory = null, TimeSpan? interval = null)
    {
        _logsDirectory = logsDirectory ?? EftMapLogWatcher.DiscoverLogsDirectory();
        _pollEvery = interval ?? TimeSpan.FromSeconds(2);
        if (_pollEvery <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
    }

    internal bool Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return _logsDirectory is not null && Directory.Exists(_logsDirectory);
            if (_logsDirectory is null || !Directory.Exists(_logsDirectory)) return false;
            _currentPath = FindLatestPushLog(_logsDirectory);
            if (_currentPath is not null)
            {
                try { _cursor = new FileInfo(_currentPath).Length; }
                catch (IOException) { _cursor = 0; }
                catch (UnauthorizedAccessException) { _cursor = 0; }
            }
            _started = true;
            _timer = new System.Threading.Timer(_ => Poll(), null, _pollEvery, _pollEvery);
            return true;
        }
    }

    internal void Poll()
    {
        List<EftQuestLogEvent> events;
        lock (_gate)
        {
            if (!_started || _disposed) return;
            events = ReadCurrentLocked();
        }
        foreach (var change in events)
        {
            try { QuestChanged?.Invoke(change); }
            catch { /* Event subscriber may be closing; tail still advances. */ }
        }
    }

    private List<EftQuestLogEvent> ReadCurrentLocked()
    {
        var output = new List<EftQuestLogEvent>();
        var found = _logsDirectory is null ? null : FindLatestPushLog(_logsDirectory);
        if (found is null) return output;
        if (!string.Equals(found, _currentPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentPath = found;
            _cursor = 0; // Newly rotated log may contain its first event already.
            ResetJson();
        }

        try
        {
            using var stream = new FileStream(found, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 16 * 1024, FileOptions.SequentialScan);
            if (stream.Length < _cursor)
            {
                _cursor = 0;
                ResetJson();
            }
            if (stream.Length == _cursor) return output;
            stream.Position = _cursor;
            var available = (int)Math.Min(MaximumReadBytesPerPoll, stream.Length - _cursor);
            var bytes = new byte[available];
            var count = stream.ReadAtLeast(bytes, 1, throwOnEndOfStream: false);
            // ReadAtLeast may be short on a live file. Do not commit partial lines.
            if (count == 0) return output;
            var lastNewline = Array.LastIndexOf(bytes, (byte)'\n', count - 1, count);
            if (lastNewline < 0)
            {
                if (count >= MaximumReadBytesPerPoll)
                {
                    _cursor += count;
                    ResetJson();
                }
                return output;
            }
            int start = 0;
            for (int i = 0; i <= lastNewline; i++)
            {
                if (bytes[i] != (byte)'\n') continue;
                var length = i - start;
                if (length > 0 && bytes[i - 1] == (byte)'\r') length--;
                if (length > 0)
                    ConsumeLine(Encoding.UTF8.GetString(bytes, start, length), output);
                start = i + 1;
            }
            _cursor += lastNewline + 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or DirectoryNotFoundException)
        {
            // A concurrently rotated/deleted log is retried on next poll.
        }
        return output;
    }

    private void ConsumeLine(string line, List<EftQuestLogEvent> results)
    {
        const string marker = "Got notification | ChatMessageReceived";
        var header = line.IndexOf(marker, StringComparison.Ordinal);
        if (header >= 0)
        {
            ResetJson();
            _collecting = true;
            var start = line.IndexOf('{', header + marker.Length);
            if (start >= 0) AppendFragment(line.AsSpan(start), results);
            return;
        }
        if (!_collecting) return;
        var content = line.AsSpan();
        if (_braceDepth == 0)
        {
            var at = line.IndexOf('{');
            if (at < 0)
            {
                // Only one following line may begin the JSON payload.
                if (!string.IsNullOrWhiteSpace(line)) ResetJson();
                return;
            }
            content = content[at..];
        }
        AppendFragment(content, results);
    }

    private void AppendFragment(ReadOnlySpan<char> segment, List<EftQuestLogEvent> results)
    {
        if (_payload.Length + segment.Length + 1 > MaximumPayloadCharacters)
        {
            ResetJson();
            return;
        }
        foreach (var ch in segment)
        {
            _payload.Append(ch);
            if (_insideString)
            {
                if (_escapeNext) _escapeNext = false;
                else if (ch == '\\') _escapeNext = true;
                else if (ch == '"') _insideString = false;
                continue;
            }
            if (ch == '"') _insideString = true;
            else if (ch == '{') _braceDepth++;
            else if (ch == '}')
            {
                if (--_braceDepth == 0)
                {
                    if (EftQuestLogParser.TryParseNotification(_payload.ToString(), out var parsed)
                        && parsed is not null)
                    {
                        var key = parsed.TaskId + ":" + parsed.Status + ":" + parsed.AtUnixSeconds;
                        if (_seen.Add(key))
                        {
                            _recent.Enqueue(key);
                            if (_recent.Count > 256) _seen.Remove(_recent.Dequeue());
                            results.Add(parsed);
                        }
                    }
                    ResetJson();
                    return;
                }
                if (_braceDepth < 0) { ResetJson(); return; }
            }
        }
        _payload.Append('\n');
    }

    private void ResetJson()
    {
        _payload.Clear();
        _collecting = false;
        _insideString = _escapeNext = false;
        _braceDepth = 0;
    }

    private static string? FindLatestPushLog(string root)
    {
        try
        {
            IEnumerable<string> folders = new[] { root }
                .Concat(Directory.EnumerateDirectories(root)
                    .OrderByDescending(Directory.GetLastWriteTimeUtc).Take(16));
            string? latest = null;
            DateTime newest = DateTime.MinValue;
            foreach (var folder in folders)
            {
                foreach (var path in Directory.EnumerateFiles(folder, "*.log",
                    SearchOption.TopDirectoryOnly).Take(200))
                {
                    var name = Path.GetFileName(path);
                    if (!name.Contains("push-notifications", StringComparison.OrdinalIgnoreCase)
                        && !name.Contains("notifications", StringComparison.OrdinalIgnoreCase)) continue;
                    var modified = File.GetLastWriteTimeUtc(path);
                    if (modified <= newest) continue;
                    latest = path;
                    newest = modified;
                }
            }
            return latest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or DirectoryNotFoundException) { return null; }
    }

    public void Dispose()
    {
        System.Threading.Timer? previous;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _started = false;
            previous = _timer; _timer = null;
            ResetJson();
            _seen.Clear(); _recent.Clear();
        }
        previous?.Dispose();
    }
}

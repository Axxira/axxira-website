using System.Diagnostics;
using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// Native QuestPilot evidence bridge. Quest events come only from EFT's
/// read-only push-notification tail and are never sent to the squad relay
/// unless the user separately opts in to squad quest sharing.
/// </summary>
internal sealed partial class OverlayForm
{
    private EftQuestLogWatcher? _questLogWatcher;
    private string? _logVerifiedGameMode;
    private readonly Queue<EftQuestLogEvent> _unscopedQuestLogEvents = new();
    private readonly Dictionary<string, (EftQuestLogEvent Event, string Mode)> _questLogOutbox =
        new(StringComparer.Ordinal);
    private bool _questLogSending;
    private long _lastQuestLogSeenAt;

    private void EnsureQuestLogWatcherStarted()
    {
        if (_questLogWatcher is not null || _formLifetime.IsCancellationRequested) return;
        EftQuestLogWatcher? watcher = null;
        try
        {
            watcher = new EftQuestLogWatcher();
            watcher.QuestChanged += OnQuestLogChanged;
            if (watcher.Start())
                _questLogWatcher = watcher;
            else
            {
                watcher.QuestChanged -= OnQuestLogChanged;
                watcher.Dispose();
            }
        }
        catch
        {
            if (watcher is not null)
            {
                watcher.QuestChanged -= OnQuestLogChanged;
                watcher.Dispose();
            }
        }
    }

    private void OnEftGameModeDetected(object? sender, EftGameModeDetectedEventArgs args)
    {
        RunOnUiThread(() =>
        {
            if (!ReferenceEquals(sender, _mapLogWatcher)) return;
            if (args.GameMode is not ("pve" or "regular" or "pvp-season")) return;
            _logVerifiedGameMode = args.GameMode;
            _raidQuestGameMode = args.GameMode;
            while (_unscopedQuestLogEvents.TryDequeue(out var pending))
                QueueNativeQuestLogEvent(pending, args.GameMode);
            _ = FlushQuestLogEventsAsync();
        });
    }

    private void OnQuestLogChanged(EftQuestLogEvent change)
    {
        RunOnUiThread(() =>
        {
            if (_formLifetime.IsCancellationRequested || IsDisposed) return;
            if (_logVerifiedGameMode is { } mode)
                QueueNativeQuestLogEvent(change, mode);
            else if (_unscopedQuestLogEvents.Count < 24)
                _unscopedQuestLogEvents.Enqueue(change);
            // Never guess PvE/PvP. Queued events without verified game mode remain local.
            _ = FlushQuestLogEventsAsync();
        });
    }

    private void QueueNativeQuestLogEvent(EftQuestLogEvent change, string mode)
    {
        var key = mode + ":" + change.TaskId;
        if (_questLogOutbox.TryGetValue(key, out var previous)
            && previous.Event.AtUnixSeconds >= change.AtUnixSeconds) return;
        if (_questLogOutbox.Count >= 120)
            _questLogOutbox.Remove(_questLogOutbox.Keys.First());
        _questLogOutbox[key] = (change, mode);
        _lastQuestLogSeenAt = Stopwatch.GetTimestamp();
    }

    private async Task FlushQuestLogEventsAsync()
    {
        if (_questLogSending || _questLogOutbox.Count == 0 || !_webReady
            || _webView.CoreWebView2 is null || _navigationInProgress
            || _formLifetime.IsCancellationRequested) return;
        _questLogSending = true;
        try
        {
            var batch = _questLogOutbox.Take(60).ToArray();
            var payload = JsonSerializer.Serialize(batch.Select(e => new
            {
                taskId = e.Value.Event.TaskId,
                status = e.Value.Event.Status,
                mode = e.Value.Mode,
                at = e.Value.Event.AtUnixSeconds,
                source = EftQuestLogEvent.Source
            }));
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.__raidPilotApplyQuestLogEvents?.({payload}) ?? 0;");
            if (int.TryParse(result, out var accepted) && accepted == batch.Length)
            {
                foreach (var item in batch)
                    if (_questLogOutbox.TryGetValue(item.Key, out var current)
                        && current.Event.AtUnixSeconds == item.Value.Event.AtUnixSeconds)
                        _questLogOutbox.Remove(item.Key);
            }
        }
        catch
        {
            // WebView navigation is retryable; keep bounded evidence in memory.
        }
        finally { _questLogSending = false; }
    }

    private void DisposeQuestLog()
    {
        if (_questLogWatcher is not null)
        {
            _questLogWatcher.QuestChanged -= OnQuestLogChanged;
            _questLogWatcher.Dispose();
            _questLogWatcher = null;
        }
        _unscopedQuestLogEvents.Clear();
        _questLogOutbox.Clear();
    }
}

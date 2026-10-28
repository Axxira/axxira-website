using System.Diagnostics;
using System.Text.Json;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private readonly QuestContextAdvisor _questContextAdvisor = new();
    private readonly System.Windows.Forms.Timer _questContextTimer = new() { Interval = 500 };
    private readonly ToolStripMenuItem _questContextMenuItem = new("Aide des quêtes proches")
    { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem _ignoreQuestObjectiveMenuItem = new("Masquer cet objectif pour ce raid")
    { Enabled = false };
    private readonly HashSet<string> _ignoredContextObjectives = new(StringComparer.Ordinal);
    private bool _questContextVisible;
    private bool _questContextRenderBusy;
    private long _questContextPoseTimestamp;
    private long _questContextSequence;
    private string? _questContextRenderedSignature;
    private QuestContextHint? _currentQuestContext;

    private void InitializeQuestContext()
    {
        var menu = _trayIcon.ContextMenuStrip;
        if (menu is not null)
        {
            var index = menu.Items.IndexOf(_questMenuItem) + 1;
            menu.Items.Insert(index, _questContextMenuItem);
            menu.Items.Insert(index + 1, _ignoreQuestObjectiveMenuItem);
        }
        _questContextMenuItem.Click += (_, _) =>
        {
            _questContextRenderedSignature = null;
            _ = UpdateQuestContextAsync();
        };
        _ignoreQuestObjectiveMenuItem.Click += (_, _) =>
        {
            if (_currentQuestContext is { } hint) _ignoredContextObjectives.Add(hint.Key);
            _questContextRenderedSignature = null;
            _ = UpdateQuestContextAsync();
        };
        _questContextTimer.Tick += async (_, _) => await UpdateQuestContextAsync();
        _questContextTimer.Start();
    }

    private void ResetQuestContext()
    {
        _questContextAdvisor.Reset();
        _ignoredContextObjectives.Clear();
        _questContextPoseTimestamp = 0;
        _currentQuestContext = null;
        _ignoreQuestObjectiveMenuItem.Enabled = false;
        _questContextRenderedSignature = null;
        var sequence = ++_questContextSequence;
        SetQuestContextVisible(false);
        var core = _webView.CoreWebView2;
        if (core is not null && !_formLifetime.IsCancellationRequested)
            _ = ClearQuestContextSafelyAsync(sequence);
    }

    private async Task ClearQuestContextSafelyAsync(long sequence)
    {
        try
        {
            if (_webView.CoreWebView2 is { } core)
                await core.ExecuteScriptAsync($"window.__raidPilotClearQuestContext?.({sequence}); void 0;");
        }
        catch { /* A destroyed document already removed its context panel. */ }
    }

    private async Task UpdateQuestContextAsync()
    {
        if (_questContextRenderBusy || _formLifetime.IsCancellationRequested || IsDisposed) return;
        _questContextRenderBusy = true;
        try
        {
            var now = Stopwatch.GetTimestamp();
            var age = _questContextPoseTimestamp > 0
                ? (now - _questContextPoseTimestamp) / (double)Stopwatch.Frequency
                : double.PositiveInfinity;
            var active = _questContextMenuItem.Checked && _webReady && !_navigationInProgress
                && !_navigationStartPending && _raidActive
                && _controlTrackingActive && _tarkovIsForeground
                && _liveTrackingEnabled && _questsMap == _currentMap
                && _questsGameMode == _raidQuestGameMode;
            if (active && _cortexMenu.Checked
                && _realtimeEstimator.CurrentPose is { } trackedPose)
            {
                var tracking = CortexAssess(now, trackedPose).Quality;
                active = tracking != CortexTrackingQuality.Degraded
                    && tracking != CortexTrackingQuality.WaitingForF8;
            }
            var core = _webView.CoreWebView2;
            if (core is null) { SetQuestContextVisible(false); return; }
            var done = new HashSet<string>(_ignoredContextObjectives,
                StringComparer.OrdinalIgnoreCase);
            var completedQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var questPilotHudVisible = false;
            if (active && _quests.Count > 0)
            {
                // The native nearby panel must respect the same manual ticks
                // and EFT-confirmed quest status as QuestPilot's saved plan.
                // This is a READ of local public quest IDs, never a completion
                // decision or relay transmission.
                var navigationBefore = _navigationSequence;
                var raidBefore = _raidGeneration;
                var result = await core.ExecuteScriptAsync(
                    "window.RaidPilotLocalMap?.questPilotContextProgress?.() ?? null");
                if (navigationBefore != _navigationSequence
                    || raidBefore != _raidGeneration) return;
                if (result is not ("null" or "undefined" or ""))
                {
                    var progress = JsonSerializer.Deserialize<QuestPilotContextProgress>(
                        result, new JsonSerializerOptions
                        { PropertyNameCaseInsensitive = true });
                    questPilotHudVisible = progress?.HudVisible == true;
                    foreach (var id in (progress?.Done ?? Array.Empty<string>())
                        .Where(v => v is { Length: > 0 and <= 256 }).Take(240))
                        done.Add(id);
                    foreach (var id in (progress?.CompletedQuests ?? Array.Empty<string>())
                        .Where(v => v is { Length: > 0 and <= 128 }).Take(240))
                        completedQuests.Add(id);
                }
            }
            var quests = _quests.Select(q => q with
            {
                Objectives = completedQuests.Contains(q.Id)
                    ? Array.Empty<QuestContextObjective>()
                    : q.Objectives.Where(o => !done.Contains(q.Id + ":" + o.Id)).ToArray()
            }).ToArray();
            var hint = _questContextAdvisor.Select(quests, _confirmedQuestIds, _currentMap,
                _raidGeneration, _realtimeEstimator.CurrentPose, age,
                active && !questPilotHudVisible);
            _currentQuestContext = hint;
            _ignoreQuestObjectiveMenuItem.Enabled = hint is not null;
            var signature = _navigationSequence + "|" + JsonSerializer.Serialize(hint);
            if (signature == _questContextRenderedSignature) return;
            var sequence = ++_questContextSequence;
            var navigation = _navigationSequence;
            var generation = _raidGeneration;
            // Quest space opens BELOW the map; the extraction header and map stay anchored top-right.
            SetQuestContextVisible(hint is not null);
            if (hint is null)
                await core.ExecuteScriptAsync($"window.__raidPilotClearQuestContext?.({sequence}); void 0;");
            else
            {
                var payload = JsonSerializer.Serialize(new
                {
                    sequence, generation, map = _currentMap,
                    name = hint.Name, where = hint.Where, instruction = hint.Instruction,
                    note = hint.Note, source = hint.Source, nearby = hint.NearbyCount
                });
                var applied = await core.ExecuteScriptAsync($"window.__raidPilotShowQuestContext?.({payload}) ?? false;");
                if (applied != "true") { SetQuestContextVisible(false); return; }
            }
            if (sequence == _questContextSequence && generation == _raidGeneration && navigation == _navigationSequence)
                _questContextRenderedSignature = signature;
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested) { }
        catch
        {
            _questContextRenderedSignature = null;
            SetQuestContextVisible(false);
        }
        finally { _questContextRenderBusy = false; }
    }

    private void SetQuestContextVisible(bool visible)
    {
        if (_questContextVisible == visible || IsDisposed) return;
        var extra = (int)Math.Round(OverlayLayoutCalculator.QuestContextHeightDip
            * ((DeviceDpi > 0 ? DeviceDpi : 96) / 96.0), MidpointRounding.AwayFromZero);
        var delta = visible ? extra : -extra;
        var rectangle = Bounds;
        _questContextVisible = visible;
        SetBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height + delta);
    }

    private sealed record QuestPilotContextProgress(
        string[] Done, string[] CompletedQuests, bool HudVisible);

    private void DisposeQuestContext()
    {
        _questContextTimer.Stop();
        _questContextTimer.Dispose();
    }
}

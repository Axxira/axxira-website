using System.Diagnostics;
using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// Optional, consent-based personal QuestPilot plans for a squad room.
/// Only public task/step IDs are exchanged; automatic logs remain local.
/// Every room requires a fresh opt-in, and the relay must advertise support.
/// </summary>
internal sealed partial class OverlayForm
{
    private readonly ToolStripMenuItem _shareSquadQuestPlanMenuItem =
        new("Partager mes quêtes avec l'escouade (facultatif)")
        { CheckOnClick = true };
    private bool _shareQuestPlanInCurrentRoom;
    private long _questPlanRoomGeneration;
    private bool _questPlanSendInProgress;
    private long _lastQuestPlanPollAt;
    private long _lastQuestPlanBroadcastAt;
    private string? _lastQuestPlanSignature;

    private void InitializeSquadQuestMenu()
    {
        _shareSquadQuestPlanMenuItem.Checked = false;
        _shareSquadQuestPlanMenuItem.Click += async (_, _) =>
        {
            if (!_shareSquadQuestPlanMenuItem.Checked)
            {
                _shareQuestPlanInCurrentRoom = false;
                return;
            }
            var client = _squadClient;
            if (client is null)
            {
                _shareSquadQuestPlanMenuItem.Checked = false;
                SetStatus("Rejoins d'abord une escouade pour partager tes quêtes.", Color.Khaki);
                return;
            }
            var confirm = MessageBox.Show(this,
                "Partager ton plan QuestPilot dans cette escouade ?\n\n"
                + "Seuls les identifiants publics des quêtes, les étapes que tu as "
                + "cochées et le nom choisi dans RaidPilot seront visibles par les "
                + "joueurs ayant le même code. Tes captures, journaux EFT, "
                + "inventaire et identité Tarkov ne seront pas envoyés.\n\n"
                + "Ce choix concerne uniquement cette connexion. Tu peux "
                + "désactiver le partage à tout moment.",
                "QuestPilot — partage facultatif", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                _shareSquadQuestPlanMenuItem.Checked = false;
                return;
            }
            if (!client.QuestPlanSupported
                && !await client.ProbeQuestPlanSupportAsync(_formLifetime.Token))
            {
                _shareSquadQuestPlanMenuItem.Checked = false;
                SetStatus("Le relais Cloudflare ne prend pas encore en charge les quêtes. "
                    + "Utilise « Copier mon plan » dans QuestPilot.", Color.Khaki);
                return;
            }
            _shareQuestPlanInCurrentRoom = true;
            _lastQuestPlanPollAt = _lastQuestPlanBroadcastAt = 0;
            _lastQuestPlanSignature = null;
            await MaybeShareQuestPlanAsync(force: true);
        };
        _squadMenuItem.DropDownItems.Add(_shareSquadQuestPlanMenuItem);
    }

    private void ResetSquadQuestSharing()
    {
        _questPlanRoomGeneration++;
        _shareQuestPlanInCurrentRoom = false;
        _shareSquadQuestPlanMenuItem.Checked = false;
        _lastQuestPlanPollAt = _lastQuestPlanBroadcastAt = 0;
        _lastQuestPlanSignature = null;
    }

    private async Task MaybeShareQuestPlanAsync(bool force = false)
    {
        var client = _squadClient;
        if (client is null || !client.QuestPlanSupported
            || !_shareQuestPlanInCurrentRoom || _questPlanSendInProgress
            || !_webReady || _webView.CoreWebView2 is null
            || _formLifetime.IsCancellationRequested || _navigationInProgress)
            return;
        var now = Stopwatch.GetTimestamp();
        if (!force && _lastQuestPlanPollAt > 0
            && (now - _lastQuestPlanPollAt) / (double)Stopwatch.Frequency < 4)
            return;
        _lastQuestPlanPollAt = now;
        _questPlanSendInProgress = true;
        try
        {
            var result = await _webView.CoreWebView2.ExecuteScriptAsync(
                "window.RaidPilotLocalMap?.questPilotLiveSnapshot?.() ?? null");
            if (string.IsNullOrWhiteSpace(result) || result == "null") return;
            var payload = JsonSerializer.Deserialize<SquadQuestPlanSnapshot>(
                result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (!SquadRelayClient.ValidQuestPlan(payload)) return;
            var signature = JsonSerializer.Serialize(payload);
            if (!force && signature == _lastQuestPlanSignature
                && _lastQuestPlanBroadcastAt > 0
                && (now - _lastQuestPlanBroadcastAt) / (double)Stopwatch.Frequency < 25)
                return;
            if (await client.SendQuestPlanAsync(payload!, _formLifetime.Token))
            {
                _lastQuestPlanSignature = signature;
                _lastQuestPlanBroadcastAt = now;
            }
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested) { }
        catch
        {
            // Optional team quest data can never disrupt player positions.
        }
        finally { _questPlanSendInProgress = false; }
    }

    private void OnSquadQuestPlanReceived(SquadQuestPlan plan)
    {
        if (_formLifetime.IsCancellationRequested || IsDisposed
            || string.Equals(plan.PeerId, _squadProfile.MemberId, StringComparison.Ordinal))
            return;
        var generation = _questPlanRoomGeneration;
        RunOnUiThread(() =>
        {
            if (generation == _questPlanRoomGeneration)
                _ = ApplyIncomingSquadQuestPlanAsync(plan, generation);
        });
    }

    private async Task ApplyIncomingSquadQuestPlanAsync(
        SquadQuestPlan plan, long generation)
    {
        if (generation != _questPlanRoomGeneration
            || !_webReady || _webView.CoreWebView2 is null
            || _squadClient is null || !_squadClient.QuestPlanSupported) return;
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                id = plan.PeerId,
                name = plan.Name,
                mode = plan.Mode,
                quests = plan.Quests,
                done = plan.Done,
                at = plan.SentAtUnixMilliseconds
            });
            await _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.__raidPilotSetTeamQuestPlan?.({payload}); void 0;");
        }
        catch { }
    }

    private void OnSquadQuestPeerLeft(string peerId)
    {
        var generation = _questPlanRoomGeneration;
        RunOnUiThread(() =>
        {
            if (generation == _questPlanRoomGeneration)
                _ = ClearIncomingSquadQuestPlanAsync(peerId);
        });
    }

    private async Task ClearIncomingSquadQuestPlanAsync(string? peerId)
    {
        if (_webView.CoreWebView2 is null) return;
        try
        {
            if (peerId is null)
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    "window.__raidPilotClearAllTeamQuestPlans?.(); void 0;");
            else
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.__raidPilotClearTeamQuestPlan?.({JsonSerializer.Serialize(peerId)}); void 0;");
        }
        catch { }
    }
}

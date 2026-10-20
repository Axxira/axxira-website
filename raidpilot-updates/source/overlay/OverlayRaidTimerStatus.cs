using System.Diagnostics;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private readonly System.Windows.Forms.Timer _raidClockUiTimer = new() { Interval = 1000 };
    private readonly ToolStripMenuItem _raidTimerStatusMenuItem = new("Chrono : temps non lu");
    private RaidTimerReadState _raidTimerReadState = RaidTimerReadState.Waiting;
    private long _raidTimerUiSequence;
    private bool _raidClockUiBusy;
    private bool _raidTimerLastRenderAccepted;
    private int _raidTimerReadAttempts;

    private void InitializeRaidClockUi()
    {
        var menu = _trayIcon.ContextMenuStrip;
        if (menu is not null) menu.Items.Insert(Math.Min(5, menu.Items.Count), _raidTimerStatusMenuItem);
        _raidTimerStatusMenuItem.Click += (_, _) => MessageBox.Show(
            this,
            $"État du chrono : {TimerStateLabel(_raidTimerReadState)}\n"
            + $"Lectures du panneau dans ce raid : {_raidTimerReadAttempts}\n"
            + $"Dernier affichage confirmé : {(_raidTimerLastRenderAccepted ? "oui" : "non")}\n\n"
            + "Une lecture illisible ne remet pas à zéro un compte à rebours déjà synchronisé. "
            + "Une nouvelle capture manuelle du panneau O peut le synchroniser de nouveau, même si les sorties sont déjà reconnues. "
            + "Le compteur ne suppose jamais la durée d'un raid Scav ou PMC.",
            "RaidPilot — Diagnostic du chrono", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _raidClockUiTimer.Tick += async (_, _) =>
        {
            if (_raidClockUiBusy || IsDisposed || !Visible || _formLifetime.IsCancellationRequested
                || !_webReady || _navigationInProgress || _navigationStartPending) return;
            _raidClockUiBusy = true;
            try
            {
                // Re-send the monotonic native clock, not another OCR/capture request.
                // This also restores a replaced DOM node or a delayed renderer.
                await RestoreRaidCountdownAsync();
            }
            finally { _raidClockUiBusy = false; }
        };
        _raidClockUiTimer.Start();
    }

    private static string TimerStateLabel(RaidTimerReadState state) => state switch
    {
        RaidTimerReadState.Reading => "lecture en cours",
        RaidTimerReadState.Unreadable => "temps non lu — panneau illisible",
        RaidTimerReadState.ConfirmationRequired => "correction à confirmer",
        RaidTimerReadState.Synchronized => "synchronisé",
        RaidTimerReadState.Failed => "lecture indisponible",
        _ => "en attente du panneau",
    };

    private void SetRaidTimerReadState(RaidTimerReadState state)
    {
        _raidTimerReadState = state;
        var snapshot = _raidCountdown.GetSnapshot(Stopwatch.GetTimestamp());
        var label = snapshot.IsSynchronized ? "synchronisé" : TimerStateLabel(state);
        _raidTimerStatusMenuItem.Text = "Chrono : " + label;
        _raidTimerStatusMenuItem.ToolTipText = "Dernière lecture : " + TimerStateLabel(state);
    }

    private void ResetRaidTimerUi()
    {
        _raidTimerReadAttempts = 0;
        _raidTimerLastRenderAccepted = false;
        SetRaidTimerReadState(RaidTimerReadState.Waiting);
    }

    private void DisposeRaidClockUi()
    {
        _raidClockUiTimer.Stop();
        _raidClockUiTimer.Dispose();
    }
}

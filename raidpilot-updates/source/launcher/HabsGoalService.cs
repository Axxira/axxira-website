namespace RaidPilot.Launcher;

internal sealed class HabsGoalService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 2_000_000 };
    private readonly CancellationTokenSource _stop = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly HabsGoalTracker _tracker = new();
    private readonly HabsGoalOverlay _overlay;
    private readonly Mutex _mutex = new(false, @"Local\ForeverPilot.HabsGoals.v1");
    private bool _ownsMonitor;
    private DateTimeOffset _nextPoll, _nextSettings;
    private bool _polling, _disposed;
    private int _generation, _failures;
    internal bool Enabled { get; private set; }
    internal string Status { get; private set; } = "Désactivé · Lumières visibles uniquement dans WoW";
    internal event Action? Changed;

    internal HabsGoalService(Func<string?> gamePath)
    {
        _overlay = new HabsGoalOverlay(gamePath);
        _overlay.PreviewStatus += SetStatus;
        try { _ownsMonitor = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMonitor = true; }
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ForeverPilot-GoalLights/1.0");
        ApplyEnabled(HabsGoalSettingsStore.Load().Enabled);
        _timer.Tick += async (_, _) =>
        {
            var now = DateTimeOffset.UtcNow;
            if (!_ownsMonitor)
            {
                try { _ownsMonitor = _mutex.WaitOne(0); }
                catch (AbandonedMutexException) { _ownsMonitor = true; }
            }
            if (now >= _nextSettings)
            {
                _nextSettings = now.AddSeconds(2);
                ApplyEnabled(HabsGoalSettingsStore.Load().Enabled);
            }
            if (Enabled && _ownsMonitor && !_polling && now >= _nextPoll) await PollAsync();
        };
        _timer.Start();
    }

    internal void SetEnabled(bool enabled)
    {
        HabsGoalSettingsStore.Save(new(enabled));
        ApplyEnabled(enabled);
    }

    private void ApplyEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        _generation++;
        _tracker.Reset();
        _nextPoll = DateTimeOffset.MinValue;
        if (!enabled) _overlay.Stop();
        SetStatus(!enabled ? "Désactivé · Lumières visibles uniquement dans WoW"
            : !_ownsMonitor ? "Actif dans l'autre fenêtre du launcher"
            : "Connexion au match de Montréal…");
    }

    internal void Preview()
    {
        // A preview is explicitly labeled and never fabricates a real score.
        _overlay.ArmPreview();
        SetStatus("Test prêt : retourne dans WoW dans les 20 secondes.");
    }

    private async Task PollAsync()
    {
        _polling = true;
        int generation = _generation;
        try
        {
            var board = await _http.GetStringAsync(HabsGoalFeed.ScoreboardUrl, _stop.Token);
            var game = HabsGoalFeed.SelectGame(board, DateTimeOffset.UtcNow);
            if (_disposed || generation != _generation) return;
            if (game is null)
            {
                _tracker.Reset();
                _nextPoll = DateTimeOffset.UtcNow.AddMinutes(2);
                SetStatus("En veille · Aucun match en direct · Garde le launcher ouvert");
            }
            else
            {
                var json = await _http.GetStringAsync($"https://api-web.nhle.com/v1/gamecenter/{game.Id}/play-by-play", _stop.Token);
                if (_disposed || generation != _generation) return;
                var snapshot = HabsGoalFeed.ParseGame(json, game.Id);
                if (_tracker.Observe(snapshot, DateTimeOffset.UtcNow)) _overlay.Celebrate(snapshot.ScoreLine);
                var live = HabsGoalFeed.IsLive(snapshot.State);
                _nextPoll = DateTimeOffset.UtcNow.AddSeconds(live || snapshot.State == "PRE" ? 20 : 120);
                SetStatus($"{snapshot.ScoreLine} · {(live ? "En direct" : snapshot.State == "PRE" ? "Avant-match" : "Match terminé")} · WoW seulement");
            }
            _failures = 0;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or
            InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            if (!_disposed && generation == _generation)
            {
                _failures = Math.Min(_failures + 1, 4);
                _nextPoll = DateTimeOffset.UtcNow.AddSeconds(20 * _failures);
                SetStatus("Données LNH indisponibles · Nouvelle tentative automatique");
            }
        }
        finally { _polling = false; }
    }

    private void SetStatus(string text) { Status = text; Changed?.Invoke(); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generation++;
        _timer.Stop();
        _timer.Dispose();
        _stop.Cancel();
        _http.Dispose();
        _overlay.Dispose();
        if (_ownsMonitor) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _stop.Dispose();
    }
}

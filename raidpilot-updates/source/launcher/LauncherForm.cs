using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using RaidPilot.Overlay;

namespace RaidPilot.Launcher;

internal sealed class LauncherForm : Form
{
    private readonly UpdateService _updates = new();
    private readonly LauncherSurface _surface = new();
    private LauncherSettings _settings;
    private SquadProfile _squadProfile;
    private UpdateManifest? _manifest;
    private bool _busy;

    internal LauncherForm()
    {
        _settings = LauncherSettingsStore.Load();
        _squadProfile = SquadProfileStore.LoadOrCreate();
        PushSquadProfileToSurface();

        Text = "RaidPilot Launcher";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(1180, 760);
        MaximumSize = new Size(1180, 760);
        BackColor = Color.FromArgb(5, 9, 12);
        KeyPreview = true;

        _surface.Dock = DockStyle.Fill;
        _surface.ActionRequested += OnActionRequested;
        _surface.DragRequested += (_, _) => BeginWindowDrag();
        Controls.Add(_surface);

        Shown += OnShown;
        FormClosed += (_, _) => _updates.Dispose();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                WindowState = FormWindowState.Minimized;
            }
        };
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        try
        {
            UpdateService.EnsureBundledPayloadInstalled();
        }
        catch (Exception exception)
        {
            SetStatus($"Installation initiale impossible : {exception.Message}", LauncherStatus.Error);
        }

        RefreshInstalledVersions();
        RefreshFeedLabel();

        if (!string.IsNullOrWhiteSpace(_settings.ManifestUrl))
        {
            await CheckUpdatesAsync(showErrors: false);
        }
        else
        {
            SetStatus("Tout est prêt !", LauncherStatus.Success);
        }
    }

    private async void OnActionRequested(object? sender, LauncherAction action)
    {
        switch (action)
        {
            case LauncherAction.Minimize:
                WindowState = FormWindowState.Minimized;
                break;

            case LauncherAction.Close:
                Close();
                break;

            case LauncherAction.Home:
                _surface.ActiveSection = LauncherSection.Home;
                _surface.Invalidate();
                break;

            case LauncherAction.CheckUpdates:
                _surface.ActiveSection = LauncherSection.Update;
                _surface.Invalidate();
                await CheckUpdatesAsync(showErrors: true);
                break;

            case LauncherAction.Play:
            case LauncherAction.LaunchRaidPilot:
                _surface.ActiveSection = LauncherSection.Play;
                _surface.Invalidate();
                LaunchApplication(LauncherPaths.RaidPilotExe, "RaidPilot Overlay");
                break;

            case LauncherAction.Squad:
                _surface.ActiveSection = LauncherSection.Squad;
                PushSquadProfileToSurface();
                _surface.Invalidate();
                break;

            case LauncherAction.LaunchBeacon:
                _surface.ActiveSection = LauncherSection.Squad;
                _surface.Invalidate();
                LaunchApplication(LauncherPaths.BeaconExe, "RaidPilot Beacon");
                break;

            case LauncherAction.CreateSquad:
                CreateSquadFromLauncher();
                break;

            case LauncherAction.CopySquadCode:
                CopySquadCode();
                break;

            case LauncherAction.JoinSquad:
                JoinSquadFromLauncher();
                break;

            case LauncherAction.EditSquadName:
                EditSquadNameFromLauncher();
                break;

            case LauncherAction.LeaveSquad:
                LeaveSquadFromLauncher();
                break;

            case LauncherAction.Settings:
            case LauncherAction.ConfigureFeed:
                _surface.ActiveSection = LauncherSection.Settings;
                _surface.Invalidate();
                ConfigureFeed();
                break;

            case LauncherAction.ReleaseNotes:
                _surface.ActiveSection = LauncherSection.ReleaseNotes;
                _surface.Invalidate();
                ShowReleaseNotes();
                break;

            case LauncherAction.Help:
                _surface.ActiveSection = LauncherSection.Help;
                _surface.Invalidate();
                ShowHelp();
                break;

            case LauncherAction.InstallUpdates:
                await InstallUpdatesAsync();
                break;

            case LauncherAction.Rollback:
                RollbackRaidPilot();
                break;
        }
    }

    private void RefreshInstalledVersions()
    {
        var raidVersion = UpdateService.GetInstalledVersion(LauncherPaths.RaidPilotExe);
        var beaconVersion = UpdateService.GetInstalledVersion(LauncherPaths.BeaconExe);

        _surface.CurrentRaidPilotVersion = raidVersion is null ? "—" : TrimVersion(raidVersion);
        _surface.CurrentBeaconVersion = beaconVersion is null ? "—" : TrimVersion(beaconVersion);

        if (_manifest?.RaidPilot is { } raid)
        {
            _surface.LatestRaidPilotVersion = raid.Version;
            _surface.UpdateAvailable = UpdateService.IsNewer(raidVersion, raid.Version)
                || (_manifest.Beacon is { } beacon && UpdateService.IsNewer(beaconVersion, beacon.Version));
        }
        else
        {
            _surface.LatestRaidPilotVersion = _surface.CurrentRaidPilotVersion;
            _surface.UpdateAvailable = false;
        }

        _surface.CanLaunchRaidPilot = File.Exists(LauncherPaths.RaidPilotExe) && !_busy;
        _surface.CanLaunchBeacon = File.Exists(LauncherPaths.BeaconExe) && !_busy;
        _surface.CanRollback = Directory.Exists(LauncherPaths.RaidPilot + ".previous") && !_busy;
        _surface.Invalidate();
    }

    private void RefreshFeedLabel()
    {
        if (string.IsNullOrWhiteSpace(_settings.ManifestUrl))
        {
            _surface.ChannelLabel = "Stable";
            _surface.FeedLabel = "Source non configurée";
            _surface.Invalidate();
            return;
        }

        try
        {
            var uri = new Uri(_settings.ManifestUrl);
            _surface.ChannelLabel = "Stable";
            _surface.FeedLabel = uri.IsFile ? "Manifeste local" : uri.Host;
        }
        catch
        {
            _surface.ChannelLabel = "Stable";
            _surface.FeedLabel = "Source invalide";
        }
        _surface.Invalidate();
    }

    private async Task CheckUpdatesAsync(bool showErrors)
    {
        if (_busy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.ManifestUrl))
        {
            if (showErrors)
            {
                ConfigureFeed();
            }
            return;
        }

        SetBusy(true);
        _surface.Progress = 10;
        try
        {
            SetStatus("Recherche d'une nouvelle version…", LauncherStatus.Info);
            _manifest = await _updates.LoadManifestAsync(
                _settings.ManifestUrl,
                CancellationToken.None);

            _surface.Progress = 100;

            var raidInstalled = UpdateService.GetInstalledVersion(LauncherPaths.RaidPilotExe);
            var beaconInstalled = UpdateService.GetInstalledVersion(LauncherPaths.BeaconExe);
            var raidNew = _manifest.RaidPilot is { } raid
                && UpdateService.IsNewer(raidInstalled, raid.Version);
            var beaconNew = _manifest.Beacon is { } beacon
                && UpdateService.IsNewer(beaconInstalled, beacon.Version);
            var launcherNew = _manifest.Launcher is { } launcher
                && UpdateService.IsNewer(Application.ProductVersion, launcher.Version);

            _surface.LatestRaidPilotVersion = _manifest.RaidPilot?.Version
                ?? _surface.CurrentRaidPilotVersion;
            _surface.UpdateAvailable = raidNew || beaconNew || launcherNew;
            _surface.ReleaseNotes = BuildReleaseNotes(_manifest);

            SetStatus(
                raidNew || beaconNew || launcherNew
                    ? "Une nouvelle version est disponible."
                    : "À jour · tout est prêt !",
                raidNew || beaconNew || launcherNew
                    ? LauncherStatus.Update
                    : LauncherStatus.Success);
        }
        catch (Exception exception)
        {
            _manifest = null;
            _surface.UpdateAvailable = false;
            SetStatus("Impossible de vérifier les mises à jour.", LauncherStatus.Error);

            if (showErrors)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "RaidPilot Launcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            SetBusy(false);
            RefreshInstalledVersions();
        }
    }

    private async Task InstallUpdatesAsync()
    {
        if (_busy || _manifest is null)
        {
            return;
        }

        var raidInstalled = UpdateService.GetInstalledVersion(LauncherPaths.RaidPilotExe);
        var beaconInstalled = UpdateService.GetInstalledVersion(LauncherPaths.BeaconExe);
        var updateRaid = _manifest.RaidPilot is { } raid
            && UpdateService.IsNewer(raidInstalled, raid.Version);
        var updateBeacon = _manifest.Beacon is { } beacon
            && UpdateService.IsNewer(beaconInstalled, beacon.Version);
        var updateLauncher = _manifest.Launcher is { } launcher
            && UpdateService.IsNewer(Application.ProductVersion, launcher.Version);

        if (!updateRaid && !updateBeacon && !updateLauncher)
        {
            SetStatus("Déjà à jour.", LauncherStatus.Success);
            return;
        }

        if (MessageBox.Show(
                this,
                "RaidPilot Launcher va fermer les composants actifs, vérifier le SHA-256, installer la nouvelle version et conserver une copie de retour arrière.\n\nContinuer ?",
                "Installer la mise à jour",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1) != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true);
        var progress = new Progress<int>(value =>
        {
            _surface.Progress = Math.Clamp(value, 0, 100);
            _surface.Invalidate();
        });

        try
        {
            if (updateRaid)
            {
                SetStatus($"Installation de RaidPilot v{_manifest.RaidPilot!.Version}…", LauncherStatus.Info);
                await StopProcessAsync("RaidPilot.Overlay");
                await _updates.InstallComponentAsync(
                    _manifest.RaidPilot!,
                    LauncherPaths.RaidPilot,
                    "RaidPilot.Overlay.exe",
                    progress,
                    CancellationToken.None);
            }

            if (updateBeacon)
            {
                _surface.Progress = 0;
                SetStatus($"Installation de Beacon v{_manifest.Beacon!.Version}…", LauncherStatus.Info);
                await StopProcessAsync("RaidPilot.Beacon");
                await _updates.InstallComponentAsync(
                    _manifest.Beacon!,
                    LauncherPaths.Beacon,
                    "RaidPilot.Beacon.exe",
                    progress,
                    CancellationToken.None);
            }

            if (updateLauncher)
            {
                _surface.Progress = 0;
                SetStatus(
                    $"Préparation du Launcher v{_manifest.Launcher!.Version}…",
                    LauncherStatus.Info);
                await _updates.PrepareLauncherSelfUpdateAsync(
                    _manifest.Launcher!,
                    progress,
                    CancellationToken.None);
                SetStatus(
                    "Le launcher va redémarrer automatiquement…",
                    LauncherStatus.Success);
                Close();
                return;
            }

            _surface.UpdateAvailable = false;
            _surface.ReleaseNotes = BuildReleaseNotes(_manifest);
            SetStatus("Mise à jour installée et vérifiée ✓", LauncherStatus.Success);
        }
        catch (Exception exception)
        {
            SetStatus(
                "Échec de mise à jour · version précédente préservée.",
                LauncherStatus.Error);

            MessageBox.Show(
                this,
                exception.Message,
                "RaidPilot Launcher — mise à jour",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            RefreshInstalledVersions();
        }
    }

    private static async Task StopProcessAsync(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (process.CloseMainWindow())
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        try
                        {
                            await process.WaitForExitAsync(timeout.Token);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }

                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                }
                catch
                {
                }
            }
        }
    }

    private void RollbackRaidPilot()
    {
        if (_busy)
        {
            return;
        }

        if (!Directory.Exists(LauncherPaths.RaidPilot + ".previous"))
        {
            SetStatus("Aucune version précédente disponible.", LauncherStatus.Warning);
            return;
        }

        if (MessageBox.Show(
                this,
                "Revenir à la version précédente de RaidPilot ?\n\nLes calibrations, routes apprises et préférences ne seront pas touchées.",
                "RaidPilot Launcher — retour arrière",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            foreach (var process in Process.GetProcessesByName("RaidPilot.Overlay"))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                process.Dispose();
            }

            var ok = UpdateService.Rollback(
                LauncherPaths.RaidPilot,
                "RaidPilot.Overlay.exe");

            SetStatus(
                ok
                    ? "Version précédente restaurée."
                    : "Aucune sauvegarde valide à restaurer.",
                ok ? LauncherStatus.Success : LauncherStatus.Warning);
        }
        catch (Exception exception)
        {
            SetStatus($"Retour arrière impossible : {exception.Message}", LauncherStatus.Error);
        }

        RefreshInstalledVersions();
    }

    private void ConfigureFeed()
    {
        using var dialog = new UpdateFeedDialog(_settings.ManifestUrl);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var location = dialog.ManifestLocation;
        if (!string.IsNullOrWhiteSpace(location)
            && !UpdateService.IsAllowedManifestLocation(location))
        {
            MessageBox.Show(
                this,
                "Utilise une adresse HTTPS. file:// est également accepté pour les tests locaux.",
                "RaidPilot Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _settings = new LauncherSettings(location, "stable");
        _ = LauncherSettingsStore.Save(_settings);
        _manifest = null;
        _surface.UpdateAvailable = false;
        _surface.LatestRaidPilotVersion = _surface.CurrentRaidPilotVersion;
        RefreshFeedLabel();

        SetStatus(
            string.IsNullOrWhiteSpace(location)
                ? "Source des mises à jour désactivée."
                : "Source enregistrée · utilise « Mise à jour » pour vérifier.",
            LauncherStatus.Info);
    }

    private void PushSquadProfileToSurface()
    {
        _surface.SquadCode = _squadProfile.HasRoom
            ? _squadProfile.RoomCode!
            : "AUCUNE ESCOUADE";
        _surface.SquadName = SquadRelayClient.NormalizeName(_squadProfile.Name);
        _surface.SquadConfigured = _squadProfile.HasRoom && _squadProfile.AutoJoin;
        _surface.Invalidate();
    }

    private void CreateSquadFromLauncher()
    {
        var roomCode = SquadRoomCode.Generate();
        _squadProfile = _squadProfile with
        {
            Name = SquadRelayClient.NormalizeName(_squadProfile.Name),
            RoomCode = roomCode,
            AutoJoin = true,
        };

        if (!SquadProfileStore.TrySave(_squadProfile))
        {
            SetStatus("Impossible d'enregistrer le code d'escouade.", LauncherStatus.Error);
            return;
        }

        PushSquadProfileToSurface();
        TryCopyToClipboard(roomCode);
        SetStatus(
            $"Escouade créée · {roomCode} · code copié.",
            LauncherStatus.Success);
        WarnIfRaidPilotAlreadyRunning();
    }

    private void CopySquadCode()
    {
        if (!_squadProfile.HasRoom || string.IsNullOrWhiteSpace(_squadProfile.RoomCode))
        {
            SetStatus("Crée ou rejoins d'abord une escouade.", LauncherStatus.Warning);
            return;
        }

        if (TryCopyToClipboard(_squadProfile.RoomCode))
        {
            SetStatus(
                $"Code copié · {_squadProfile.RoomCode}",
                LauncherStatus.Success);
        }
        else
        {
            SetStatus("Impossible de copier le code.", LauncherStatus.Error);
        }
    }

    private void JoinSquadFromLauncher()
    {
        if (!SquadLauncherDialog.TryShow(
                this,
                "Rejoindre une escouade",
                _squadProfile.RoomCode ?? string.Empty,
                _squadProfile.Name,
                codeReadOnly: false,
                actionText: "REJOINDRE",
                out var roomCode,
                out var playerName))
        {
            return;
        }

        _squadProfile = _squadProfile with
        {
            RoomCode = roomCode,
            Name = playerName,
            AutoJoin = true,
        };

        if (!SquadProfileStore.TrySave(_squadProfile))
        {
            SetStatus("Impossible d'enregistrer l'escouade.", LauncherStatus.Error);
            return;
        }

        PushSquadProfileToSurface();
        SetStatus(
            $"Escouade prête · {roomCode}",
            LauncherStatus.Success);
        WarnIfRaidPilotAlreadyRunning();
    }

    private void EditSquadNameFromLauncher()
    {
        if (!SquadLauncherDialog.TryShow(
                this,
                "Profil d'escouade",
                _squadProfile.RoomCode ?? string.Empty,
                _squadProfile.Name,
                codeReadOnly: true,
                actionText: "ENREGISTRER",
                out _,
                out var playerName))
        {
            return;
        }

        _squadProfile = _squadProfile with
        {
            Name = playerName,
        };

        if (!SquadProfileStore.TrySave(_squadProfile))
        {
            SetStatus("Impossible d'enregistrer ton nom.", LauncherStatus.Error);
            return;
        }

        PushSquadProfileToSurface();
        SetStatus($"Nom d'escouade : {playerName}", LauncherStatus.Success);
        WarnIfRaidPilotAlreadyRunning();
    }

    private void LeaveSquadFromLauncher()
    {
        if (!_squadProfile.HasRoom)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                "Quitter cette escouade ?\n\nRaidPilot ne rejoindra plus automatiquement ce code au prochain lancement.",
                "RaidPilot — escouade",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        _squadProfile = _squadProfile with
        {
            RoomCode = null,
            AutoJoin = false,
        };

        if (!SquadProfileStore.TrySave(_squadProfile))
        {
            SetStatus("Impossible de quitter l'escouade.", LauncherStatus.Error);
            return;
        }

        PushSquadProfileToSurface();
        SetStatus("Escouade quittée.", LauncherStatus.Info);
        WarnIfRaidPilotAlreadyRunning();
    }

    private void WarnIfRaidPilotAlreadyRunning()
    {
        if (Process.GetProcessesByName("RaidPilot.Overlay").Length == 0)
        {
            return;
        }

        SetStatus(
            "Profil enregistré · RaidPilot est déjà ouvert : le nouveau code sera appliqué à son prochain lancement.",
            LauncherStatus.Warning);
    }

    private static bool TryCopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ShowReleaseNotes()
    {
        var text = string.IsNullOrWhiteSpace(_surface.ReleaseNotes)
            ? "RaidPilot v0.11.0\n\n• Escouade temps réel et pions teammates\n• Position locale autonome\n• Beacon léger pour les équipiers\n• Nouveau launcher et système de mise à jour"
            : _surface.ReleaseNotes;

        using var dialog = new ReleaseNotesDialog(text);
        dialog.ShowDialog(this);
    }

    private void ShowHelp()
    {
        MessageBox.Show(
            this,
            "RaidPilot Launcher\n\n"
            + "• Accueil : état général et lancement rapide.\n"
            + "• Mise à jour : vérifie le manifeste HTTPS et installe les nouvelles versions.\n"
            + "• Jouer : lance RaidPilot Overlay.\n"
            + "• Escouade : lance RaidPilot Beacon.\n"
            + "• Paramètres : configure la source des mises à jour.\n\n"
            + "Le launcher vérifie le SHA-256 avant toute installation et conserve une version de retour arrière.",
            "RaidPilot — aide",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void LaunchApplication(string path, string friendlyName)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(
                this,
                $"{friendlyName} n'est pas installé.",
                "RaidPilot Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(path)
            {
                WorkingDirectory = Path.GetDirectoryName(path)!,
                UseShellExecute = true,
            };
            if (string.Equals(
                    Path.GetFileName(path),
                    "RaidPilot.Overlay.exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                startInfo.Arguments = "--preserve-screenshots";
            }

            Process.Start(startInfo);

            SetStatus($"{friendlyName} lancé.", LauncherStatus.Success);
        }
        catch (Exception exception)
        {
            SetStatus($"Lancement impossible : {exception.Message}", LauncherStatus.Error);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _surface.Busy = busy;
        _surface.CanLaunchRaidPilot = File.Exists(LauncherPaths.RaidPilotExe) && !busy;
        _surface.CanLaunchBeacon = File.Exists(LauncherPaths.BeaconExe) && !busy;
        _surface.Invalidate();
        UseWaitCursor = busy;
    }

    private void SetStatus(string text, LauncherStatus status)
    {
        _surface.StatusText = text;
        _surface.Status = status;
        _surface.Invalidate();
    }

    private void BeginWindowDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 0x2, 0);
    }

    private static string TrimVersion(string version)
    {
        if (Version.TryParse(version, out var parsed))
        {
            return parsed.Build > 0
                ? $"{parsed.Major}.{parsed.Minor}.{parsed.Build}"
                : $"{parsed.Major}.{parsed.Minor}";
        }

        return version;
    }

    private static string BuildReleaseNotes(UpdateManifest manifest)
    {
        var sections = new List<string>();
        if (manifest.RaidPilot is { } raid)
        {
            sections.Add(
                $"RaidPilot v{raid.Version}\r\n"
                + (raid.ReleaseNotes ?? "Mise à jour RaidPilot."));
        }

        if (manifest.Beacon is { } beacon)
        {
            sections.Add(
                $"Beacon v{beacon.Version}\r\n"
                + (beacon.ReleaseNotes ?? "Mise à jour Beacon."));
        }

        if (manifest.Launcher is { } launcher)
        {
            sections.Add(
                $"Launcher v{launcher.Version}\r\n"
                + (launcher.ReleaseNotes ?? "Mise à jour du launcher."));
        }

        return sections.Count == 0
            ? "Aucune note de version disponible."
            : string.Join("\r\n\r\n", sections);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
}

internal enum LauncherSection
{
    Home,
    Update,
    Play,
    Squad,
    Settings,
    ReleaseNotes,
    Help,
}

internal enum LauncherStatus
{
    Info,
    Success,
    Warning,
    Error,
    Update,
}

internal enum LauncherAction
{
    None,
    Home,
    CheckUpdates,
    Play,
    Squad,
    Settings,
    ReleaseNotes,
    Help,
    LaunchRaidPilot,
    LaunchBeacon,
    CreateSquad,
    CopySquadCode,
    JoinSquad,
    EditSquadName,
    LeaveSquad,
    InstallUpdates,
    Rollback,
    ConfigureFeed,
    Minimize,
    Close,
}

internal sealed class LauncherSurface : Control
{
    private static readonly Color Background = Color.FromArgb(5, 9, 12);
    private static readonly Color Sidebar = Color.FromArgb(8, 14, 18);
    private static readonly Color Panel = Color.FromArgb(12, 20, 25);
    private static readonly Color Panel2 = Color.FromArgb(16, 27, 33);
    private static readonly Color Border = Color.FromArgb(29, 49, 58);
    private static readonly Color Muted = Color.FromArgb(139, 158, 168);
    private static readonly Color Cyan = Color.FromArgb(63, 207, 244);
    private static readonly Color CyanBright = Color.FromArgb(106, 233, 255);
    private static readonly Color Blue = Color.FromArgb(0, 126, 255);
    private static readonly Color Green = Color.FromArgb(56, 230, 115);
    private static readonly Color Amber = Color.FromArgb(245, 189, 62);
    private static readonly Color Red = Color.FromArgb(250, 104, 116);

    private readonly Dictionary<LauncherAction, Rectangle> _hitAreas = new();
    private LauncherAction _hoverAction = LauncherAction.None;
    private Bitmap? _appIcon;

    internal LauncherSurface()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Background;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            _appIcon = icon?.ToBitmap();
        }
        catch
        {
        }
    }

    internal event EventHandler<LauncherAction>? ActionRequested;
    internal event EventHandler? DragRequested;

    internal LauncherSection ActiveSection = LauncherSection.Home;
    internal string CurrentRaidPilotVersion = "—";
    internal string LatestRaidPilotVersion = "—";
    internal string CurrentBeaconVersion = "—";
    internal string ChannelLabel = "Stable";
    internal string FeedLabel = "Source non configurée";
    internal string StatusText = "Initialisation…";
    internal LauncherStatus Status = LauncherStatus.Info;
    internal string ReleaseNotes = string.Empty;
    internal string SquadCode = "AUCUNE ESCOUADE";
    internal string SquadName = "Joueur";
    internal bool SquadConfigured;
    internal bool UpdateAvailable;
    internal bool CanLaunchRaidPilot;
    internal bool CanLaunchBeacon;
    internal bool CanRollback;
    internal bool Busy;
    internal int Progress;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _appIcon?.Dispose();
            _appIcon = null;
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        _hitAreas.Clear();

        g.Clear(Background);

        DrawSidebar(g);
        DrawTitleBar(g);
        DrawHero(g);

        if (ActiveSection == LauncherSection.Squad)
        {
            DrawSquadPage(g);
            DrawStatusAndProgress(g);
            return;
        }

        DrawVersionPanel(g);
        DrawFeatureCards(g);
        DrawReleaseHistory(g);
        DrawStatusAndProgress(g);
    }

    private void DrawSidebar(Graphics g)
    {
        var sidebarRect = new Rectangle(0, 0, 190, Height);
        using var brush = new SolidBrush(Sidebar);
        g.FillRectangle(brush, sidebarRect);

        using var linePen = new Pen(Color.FromArgb(25, 42, 49));
        g.DrawLine(linePen, 189, 0, 189, Height);

        if (_appIcon is not null)
        {
            g.DrawImage(_appIcon, new Rectangle(26, 28, 44, 44));
        }
        else
        {
            DrawCompassGlyph(g, new Rectangle(26, 28, 44, 44), CyanBright);
        }

        using var brandFont = FontOf("Segoe UI Semibold", 17F, FontStyle.Bold);
        using var brandWhite = new SolidBrush(Color.White);
        using var brandCyan = new SolidBrush(CyanBright);

        g.DrawString("RAID", brandFont, brandWhite, 78, 32);
        var raidWidth = g.MeasureString("RAID", brandFont).Width;
        g.DrawString("PILOT", brandFont, brandCyan, 76 + raidWidth, 32);

        var navStart = 108;
        DrawNavItem(g, LauncherAction.Home, LauncherSection.Home, navStart, "⌂", "Accueil");
        DrawNavItem(g, LauncherAction.CheckUpdates, LauncherSection.Update, navStart + 47, "↻", "Mise à jour");
        DrawNavItem(g, LauncherAction.Play, LauncherSection.Play, navStart + 94, "▶", "Jouer");
        DrawNavItem(g, LauncherAction.Squad, LauncherSection.Squad, navStart + 141, "♟", "Escouade");
        DrawNavItem(g, LauncherAction.Settings, LauncherSection.Settings, navStart + 188, "⚙", "Paramètres");
        DrawNavItem(g, LauncherAction.ReleaseNotes, LauncherSection.ReleaseNotes, navStart + 235, "▤", "Notes de version");
        DrawNavItem(g, LauncherAction.Help, LauncherSection.Help, navStart + 282, "?", "Aide");

        using var small = FontOf("Segoe UI", 8.5F);
        using var mutedBrush = new SolidBrush(Color.FromArgb(90, 111, 122));
        g.DrawString("Launcher v0.2.3", small, mutedBrush, 24, Height - 34);
    }

    private void DrawNavItem(
        Graphics g,
        LauncherAction action,
        LauncherSection section,
        int y,
        string glyph,
        string label)
    {
        var rect = new Rectangle(12, y, 166, 39);
        _hitAreas[action] = rect;

        var active = ActiveSection == section;
        var hovered = _hoverAction == action;

        if (active || hovered)
        {
            using var path = RoundedRect(rect, 7);
            using var fill = new SolidBrush(
                active
                    ? Color.FromArgb(8, 81, 126)
                    : Color.FromArgb(17, 37, 46));
            g.FillPath(fill, path);

            if (active)
            {
                using var glow = new Pen(Cyan, 1.2f);
                g.DrawPath(glow, path);
            }
        }

        using var glyphFont = FontOf("Segoe UI Symbol", 13F, FontStyle.Bold);
        using var labelFont = FontOf("Segoe UI", 9F, active ? FontStyle.Bold : FontStyle.Regular);
        using var glyphBrush = new SolidBrush(active ? CyanBright : Color.FromArgb(167, 184, 192));
        using var labelBrush = new SolidBrush(active ? Color.White : Color.FromArgb(204, 214, 219));

        g.DrawString(glyph, glyphFont, glyphBrush, 28, y + 8);
        g.DrawString(label, labelFont, labelBrush, 58, y + 9);
    }

    private void DrawTitleBar(Graphics g)
    {
        var x = 190;

        using var titleFill = new SolidBrush(Color.FromArgb(6, 12, 15));
        g.FillRectangle(titleFill, x, 0, Width - x, 38);

        using var borderPen = new Pen(Color.FromArgb(22, 39, 47));
        g.DrawLine(borderPen, x, 37, Width, 37);

        using var titleFont = FontOf("Segoe UI Semibold", 8.5F, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(128, 148, 158));
        g.DrawString("TACTICAL COMPANION  ·  WINDOWS", titleFont, titleBrush, x + 22, 12);

        var minimize = new Rectangle(Width - 86, 5, 34, 28);
        var close = new Rectangle(Width - 46, 5, 34, 28);
        _hitAreas[LauncherAction.Minimize] = minimize;
        _hitAreas[LauncherAction.Close] = close;

        if (_hoverAction == LauncherAction.Minimize)
        {
            using var h = new SolidBrush(Color.FromArgb(21, 39, 48));
            g.FillRectangle(h, minimize);
        }
        if (_hoverAction == LauncherAction.Close)
        {
            using var h = new SolidBrush(Color.FromArgb(126, 38, 48));
            g.FillRectangle(h, close);
        }

        using var iconPen = new Pen(Color.FromArgb(190, 208, 216), 1.4f);
        g.DrawLine(iconPen, minimize.Left + 11, minimize.Top + 16, minimize.Right - 11, minimize.Top + 16);
        g.DrawLine(iconPen, close.Left + 11, close.Top + 9, close.Right - 11, close.Bottom - 9);
        g.DrawLine(iconPen, close.Right - 11, close.Top + 9, close.Left + 11, close.Bottom - 9);
    }

    private void DrawHero(Graphics g)
    {
        var rect = new Rectangle(205, 48, Width - 225, 180);

        using (var path = RoundedRect(rect, 14))
        using (var gradient = new LinearGradientBrush(
                   rect,
                   Color.FromArgb(13, 31, 41),
                   Color.FromArgb(4, 11, 15),
                   LinearGradientMode.Horizontal))
        {
            g.FillPath(gradient, path);
        }

        g.SetClip(rect);

        DrawSky(g, rect);
        DrawIndustrialSilhouette(g, rect);

        g.ResetClip();

        using var borderPen = new Pen(Color.FromArgb(31, 72, 87), 1.1f);
        using var borderPath = RoundedRect(rect, 14);
        g.DrawPath(borderPen, borderPath);

        DrawCompassGlyph(g, new Rectangle(rect.Left + 34, rect.Top + 36, 62, 62), CyanBright);

        using var raidFont = FontOf("Bahnschrift SemiBold", 34F, FontStyle.Bold);
        using var white = new SolidBrush(Color.White);
        using var cyan = new SolidBrush(CyanBright);

        var brandX = rect.Left + 119;
        var brandY = rect.Top + 50;
        g.DrawString("RAID", raidFont, white, brandX, brandY);
        var raidW = g.MeasureString("RAID", raidFont).Width;
        g.DrawString("PILOT", raidFont, cyan, brandX + raidW - 4, brandY);

        using var subtitleFont = FontOf("Segoe UI Semibold", 9.2F, FontStyle.Bold);
        using var subtitleBrush = new SolidBrush(Color.FromArgb(185, 223, 233));
        g.DrawString(
            "VOTRE GUIDAGE TACTIQUE POUR TARKOV",
            subtitleFont,
            subtitleBrush,
            brandX + 3,
            brandY + 54);

        using var gearPath = RoundedRect(new Rectangle(rect.Right - 50, rect.Top + 16, 32, 32), 8);
        using var gearFill = new SolidBrush(Color.FromArgb(15, 31, 38));
        g.FillPath(gearFill, gearPath);
        using var gearPen = new Pen(Color.FromArgb(68, 110, 124));
        g.DrawPath(gearPen, gearPath);
        using var gearFont = FontOf("Segoe UI Symbol", 13F, FontStyle.Bold);
        using var gearBrush = new SolidBrush(Color.FromArgb(222, 238, 242));
        g.DrawString("⚙", gearFont, gearBrush, rect.Right - 44, rect.Top + 21);

        _hitAreas[LauncherAction.ConfigureFeed] = new Rectangle(rect.Right - 50, rect.Top + 16, 32, 32);
    }

    private void DrawSquadPage(Graphics g)
    {
        var rect = new Rectangle(205, 242, Width - 225, 463);

        using (var path = RoundedRect(rect, 11))
        using (var fill = new SolidBrush(Color.FromArgb(9, 16, 20)))
        using (var border = new Pen(Color.FromArgb(27, 52, 62), 1.1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        using var titleFont = FontOf("Segoe UI Semibold", 15F, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.White);
        g.DrawString("ESCOUADE RAIDPILOT", titleFont, titleBrush, rect.Left + 22, rect.Top + 18);

        using var subtitleFont = FontOf("Segoe UI", 8.5F);
        using var subtitleBrush = new SolidBrush(Color.FromArgb(150, 174, 184));
        g.DrawString(
            "Crée ton code ici, copie-le à tes teammates, puis lance RaidPilot. Le même code sera repris automatiquement.",
            subtitleFont,
            subtitleBrush,
            rect.Left + 23,
            rect.Top + 49);

        var codeCard = new Rectangle(rect.Left + 20, rect.Top + 80, 560, 210);
        using (var path = RoundedRect(codeCard, 10))
        using (var fill = new LinearGradientBrush(
                   codeCard,
                   Color.FromArgb(13, 29, 36),
                   Color.FromArgb(10, 20, 25),
                   LinearGradientMode.Vertical))
        using (var border = new Pen(
                   SquadConfigured ? Color.FromArgb(40, 132, 157) : Color.FromArgb(36, 61, 70),
                   1.15f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        using var smallLabel = FontOf("Segoe UI Semibold", 8F, FontStyle.Bold);
        using var smallMuted = new SolidBrush(Color.FromArgb(131, 157, 168));
        g.DrawString("CODE D'ESCOUADE", smallLabel, smallMuted, codeCard.Left + 18, codeCard.Top + 15);

        using var codeFont = FontOf("Consolas", SquadConfigured ? 17F : 14F, FontStyle.Bold);
        using var codeBrush = new SolidBrush(SquadConfigured ? CyanBright : Color.FromArgb(131, 148, 157));
        var codeText = SquadConfigured ? SquadCode : "AUCUNE ESCOUADE";
        g.DrawString(codeText, codeFont, codeBrush, codeCard.Left + 18, codeCard.Top + 42);

        using var nameFont = FontOf("Segoe UI Semibold", 8.8F, FontStyle.Bold);
        using var nameBrush = new SolidBrush(Color.FromArgb(218, 229, 234));
        g.DrawString($"Nom : {SquadName}", nameFont, nameBrush, codeCard.Left + 18, codeCard.Top + 80);

        using var statusFont = FontOf("Segoe UI", 8F);
        using var activeBrush = new SolidBrush(SquadConfigured ? Green : Amber);
        g.FillEllipse(activeBrush, codeCard.Left + 18, codeCard.Top + 109, 8, 8);
        g.DrawString(
            SquadConfigured
                ? "Prêt · RaidPilot rejoindra automatiquement ce code"
                : "Crée ou rejoins une escouade avant de lancer RaidPilot",
            statusFont,
            activeBrush,
            codeCard.Left + 32,
            codeCard.Top + 104);

        var createRect = new Rectangle(codeCard.Left + 18, codeCard.Bottom - 54, 150, 34);
        var copyRect = new Rectangle(codeCard.Left + 178, codeCard.Bottom - 54, 120, 34);
        var joinRect = new Rectangle(codeCard.Left + 308, codeCard.Bottom - 54, 126, 34);
        var nameRect = new Rectangle(codeCard.Left + 444, codeCard.Bottom - 54, 98, 34);

        _hitAreas[LauncherAction.CreateSquad] = createRect;
        _hitAreas[LauncherAction.CopySquadCode] = copyRect;
        _hitAreas[LauncherAction.JoinSquad] = joinRect;
        _hitAreas[LauncherAction.EditSquadName] = nameRect;

        DrawSecondaryButton(g, createRect, "＋  CRÉER", LauncherAction.CreateSquad, true, Cyan);
        DrawSecondaryButton(
            g,
            copyRect,
            "COPIER",
            LauncherAction.CopySquadCode,
            SquadConfigured,
            Color.FromArgb(110, 220, 255));
        DrawSecondaryButton(g, joinRect, "REJOINDRE", LauncherAction.JoinSquad, true, Color.FromArgb(144, 150, 255));
        DrawSecondaryButton(g, nameRect, "NOM", LauncherAction.EditSquadName, true, Color.FromArgb(173, 198, 208));

        var howCard = new Rectangle(codeCard.Right + 14, codeCard.Top, rect.Right - codeCard.Right - 34, 210);
        using (var path = RoundedRect(howCard, 10))
        using (var fill = new SolidBrush(Panel2))
        using (var border = new Pen(Color.FromArgb(30, 55, 65)))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        using var howTitle = FontOf("Segoe UI Semibold", 10F, FontStyle.Bold);
        using var white = new SolidBrush(Color.White);
        g.DrawString("COMMENT ÇA MARCHE", howTitle, white, howCard.Left + 18, howCard.Top + 15);

        DrawSquadStep(
            g,
            howCard.Left + 18,
            howCard.Top + 49,
            "1",
            "Crée ton escouade",
            "Le launcher génère un code privé.");
        DrawSquadStep(
            g,
            howCard.Left + 18,
            howCard.Top + 94,
            "2",
            "Envoie le code",
            "Tes amis l'entrent dans RaidPilot Beacon.");
        DrawSquadStep(
            g,
            howCard.Left + 18,
            howCard.Top + 139,
            "3",
            "F8 au début du raid",
            "Leurs pions apparaissent ensuite sur ta carte.");

        var launchCard = new Rectangle(rect.Left + 20, rect.Top + 306, rect.Width - 40, 137);
        using (var path = RoundedRect(launchCard, 10))
        using (var fill = new SolidBrush(Color.FromArgb(11, 22, 27)))
        using (var border = new Pen(Color.FromArgb(28, 57, 68)))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        using var launchTitle = FontOf("Segoe UI Semibold", 10F, FontStyle.Bold);
        using var launchMuted = new SolidBrush(Color.FromArgb(150, 174, 184));
        g.DrawString("PRÊT À JOUER", launchTitle, white, launchCard.Left + 18, launchCard.Top + 15);
        g.DrawString(
            SquadConfigured
                ? "RaidPilot utilisera ce code dès son prochain lancement. Tu peux aussi ouvrir Beacon ici pour tester ou aider un teammate."
                : "Crée ou rejoins d'abord une escouade. Le code restera enregistré pour les prochains lancements.",
            subtitleFont,
            launchMuted,
            launchCard.Left + 18,
            launchCard.Top + 42);

        var playRect = new Rectangle(launchCard.Left + 18, launchCard.Bottom - 54, 245, 38);
        var beaconRect = new Rectangle(playRect.Right + 12, playRect.Top, 185, 38);
        var leaveRect = new Rectangle(launchCard.Right - 145, playRect.Top, 127, 38);

        _hitAreas[LauncherAction.LaunchRaidPilot] = playRect;
        _hitAreas[LauncherAction.LaunchBeacon] = beaconRect;
        _hitAreas[LauncherAction.LeaveSquad] = leaveRect;

        DrawPrimaryButton(
            g,
            playRect,
            "▶   LANCER RAIDPILOT",
            CanLaunchRaidPilot,
            LauncherAction.LaunchRaidPilot);
        DrawSecondaryButton(
            g,
            beaconRect,
            "LANCER BEACON",
            LauncherAction.LaunchBeacon,
            CanLaunchBeacon,
            Color.FromArgb(143, 132, 255));
        DrawSecondaryButton(
            g,
            leaveRect,
            "QUITTER",
            LauncherAction.LeaveSquad,
            SquadConfigured,
            Red);
    }

    private static void DrawSquadStep(
        Graphics g,
        int x,
        int y,
        string number,
        string title,
        string detail)
    {
        using var circle = new SolidBrush(Color.FromArgb(10, 79, 108));
        g.FillEllipse(circle, x, y, 27, 27);

        using var numberFont = FontOf("Segoe UI Semibold", 8.7F, FontStyle.Bold);
        using var numberBrush = new SolidBrush(CyanBright);
        var numberSize = g.MeasureString(number, numberFont);
        g.DrawString(
            number,
            numberFont,
            numberBrush,
            x + (27 - numberSize.Width) / 2,
            y + (27 - numberSize.Height) / 2 - 1);

        using var titleFont = FontOf("Segoe UI Semibold", 8.6F, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(225, 235, 239));
        g.DrawString(title, titleFont, titleBrush, x + 38, y - 1);

        using var detailFont = FontOf("Segoe UI", 7.4F);
        using var detailBrush = new SolidBrush(Color.FromArgb(137, 158, 168));
        g.DrawString(detail, detailFont, detailBrush, x + 38, y + 15);
    }

    private static void DrawSky(Graphics g, Rectangle rect)
    {
        var sky = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
        using var skyGradient = new LinearGradientBrush(
            sky,
            Color.FromArgb(22, 47, 62),
            Color.FromArgb(7, 16, 22),
            LinearGradientMode.Vertical);
        g.FillRectangle(skyGradient, sky);

        using var hazeBrush = new SolidBrush(Color.FromArgb(35, 141, 187, 210));
        g.FillEllipse(
            hazeBrush,
            rect.Left + rect.Width / 2 - 90,
            rect.Top + 15,
            260,
            95);
    }

    private static void DrawIndustrialSilhouette(Graphics g, Rectangle rect)
    {
        var baseY = rect.Bottom - 22;
        using var ground = new SolidBrush(Color.FromArgb(220, 4, 10, 13));
        g.FillRectangle(ground, rect.Left, baseY, rect.Width, rect.Height - (baseY - rect.Top));

        using var silhouette = new SolidBrush(Color.FromArgb(218, 6, 13, 17));
        using var line = new Pen(Color.FromArgb(104, 29, 59, 70), 1.2f);

        for (var i = 0; i < 15; i++)
        {
            var x = rect.Left + 15 + (i * 61);
            var height = 26 + ((i * 17) % 56);
            var tree = new[]
            {
                new Point(x, baseY),
                new Point(x + 16, baseY - height),
                new Point(x + 32, baseY),
            };
            g.FillPolygon(silhouette, tree);
        }

        var towerX = rect.Right - 255;
        g.FillRectangle(silhouette, towerX, baseY - 105, 30, 105);
        g.DrawRectangle(line, towerX - 8, baseY - 119, 46, 20);
        g.DrawLine(line, towerX + 15, baseY - 135, towerX + 15, baseY - 119);
        g.DrawLine(line, towerX - 2, baseY - 105, towerX + 32, baseY);

        var craneX = rect.Right - 440;
        g.DrawLine(line, craneX, baseY, craneX, baseY - 92);
        g.DrawLine(line, craneX, baseY - 92, craneX + 150, baseY - 92);
        g.DrawLine(line, craneX + 12, baseY - 92, craneX + 45, baseY - 114);
        g.DrawLine(line, craneX + 104, baseY - 92, craneX + 104, baseY - 51);

        var soldierX = rect.Left + 630;
        using var soldier = new SolidBrush(Color.FromArgb(235, 2, 6, 8));
        g.FillEllipse(soldier, soldierX, baseY - 84, 21, 21);
        g.FillRectangle(soldier, soldierX + 4, baseY - 64, 16, 45);
        g.FillRectangle(soldier, soldierX - 3, baseY - 56, 8, 36);
        g.FillRectangle(soldier, soldierX + 18, baseY - 55, 8, 36);
        g.FillRectangle(soldier, soldierX + 4, baseY - 24, 7, 28);
        g.FillRectangle(soldier, soldierX + 14, baseY - 24, 7, 28);
    }

    private void DrawVersionPanel(Graphics g)
    {
        var rect = new Rectangle(205, 242, Width - 225, 128);

        using var path = RoundedRect(rect, 10);
        using var fill = new SolidBrush(Panel);
        using var border = new Pen(Border, 1.2f);
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        using var labelFont = FontOf("Segoe UI", 8.5F);
        using var labelBrush = new SolidBrush(Muted);
        using var versionFont = FontOf("Segoe UI Semibold", 15F, FontStyle.Bold);
        using var valueBrush = new SolidBrush(Color.White);

        g.DrawString("Version actuelle", labelFont, labelBrush, rect.Left + 20, rect.Top + 17);
        g.DrawString($"v{CurrentRaidPilotVersion}", versionFont, valueBrush, rect.Left + 20, rect.Top + 39);

        using var okFont = FontOf("Segoe UI Semibold", 8.5F, FontStyle.Bold);
        using var okBrush = new SolidBrush(UpdateAvailable ? Amber : Green);
        var currentMessage = UpdateAvailable ? "Nouvelle version disponible" : "✓ À jour";
        g.DrawString(currentMessage, okFont, okBrush, rect.Left + 20, rect.Top + 75);

        var col2 = rect.Left + 255;
        g.DrawString("Dernière version", labelFont, labelBrush, col2, rect.Top + 17);
        g.DrawString($"v{LatestRaidPilotVersion}", versionFont, valueBrush, col2, rect.Top + 39);

        using var small = FontOf("Segoe UI", 8F);
        g.DrawString(
            UpdateAvailable ? "Prête à installer" : "Aucune mise à jour disponible",
            small,
            labelBrush,
            col2,
            rect.Top + 77);

        var col3 = rect.Left + 485;
        g.DrawString("Canal", labelFont, labelBrush, col3, rect.Top + 17);

        var channelRect = new Rectangle(col3, rect.Top + 41, 145, 31);
        using (var channelPath = RoundedRect(channelRect, 7))
        using (var channelFill = new SolidBrush(Color.FromArgb(11, 25, 31)))
        using (var channelPen = new Pen(Color.FromArgb(39, 70, 82)))
        {
            g.FillPath(channelFill, channelPath);
            g.DrawPath(channelPen, channelPath);
        }

        using var green = new SolidBrush(Green);
        g.FillEllipse(green, channelRect.Left + 11, channelRect.Top + 11, 8, 8);
        using var channelFont = FontOf("Segoe UI Semibold", 8.5F, FontStyle.Bold);
        using var channelBrush = new SolidBrush(Color.FromArgb(221, 232, 237));
        g.DrawString(ChannelLabel, channelFont, channelBrush, channelRect.Left + 26, channelRect.Top + 7);
        using var chevronFont = FontOf("Segoe UI Symbol", 8F);
        g.DrawString("⌄", chevronFont, channelBrush, channelRect.Right - 22, channelRect.Top + 7);
        _hitAreas[LauncherAction.ConfigureFeed] = channelRect;

        var launchRect = new Rectangle(rect.Right - 220, rect.Top + 45, 194, 43);
        _hitAreas[LauncherAction.LaunchRaidPilot] = launchRect;
        DrawPrimaryButton(
            g,
            launchRect,
            "▶   LANCER RAIDPILOT",
            CanLaunchRaidPilot,
            LauncherAction.LaunchRaidPilot);

        if (UpdateAvailable)
        {
            var updateRect = new Rectangle(rect.Right - 220, rect.Top + 94, 194, 23);
            _hitAreas[LauncherAction.InstallUpdates] = updateRect;

            using var updatePath = RoundedRect(updateRect, 6);
            using var updateFill = new SolidBrush(Color.FromArgb(86, 69, 28));
            g.FillPath(updateFill, updatePath);

            using var updateFont = FontOf("Segoe UI Semibold", 8F, FontStyle.Bold);
            using var updateBrush = new SolidBrush(Color.FromArgb(255, 226, 134));
            g.DrawString("INSTALLER LA MISE À JOUR", updateFont, updateBrush, updateRect.Left + 20, updateRect.Top + 4);
        }
    }

    private void DrawFeatureCards(Graphics g)
    {
        var y = 387;
        var startX = 205;
        var gap = 10;
        var totalWidth = Width - 225;
        var cardWidth = (totalWidth - (gap * 3)) / 4;

        DrawFeatureCard(
            g,
            new Rectangle(startX, y, cardWidth, 105),
            "▧",
            "Carte interactive",
            "Localisation précise\nQuêtes, extractions, loot");

        DrawFeatureCard(
            g,
            new Rectangle(startX + cardWidth + gap, y, cardWidth, 105),
            "♟",
            "Escouade",
            "Positions en temps réel\nMême sans overlay complet");

        DrawFeatureCard(
            g,
            new Rectangle(startX + (cardWidth + gap) * 2, y, cardWidth, 105),
            "⌖",
            "Guidage intelligent",
            "Chemins, étages,\nobjectifs et extractions");

        DrawFeatureCard(
            g,
            new Rectangle(startX + (cardWidth + gap) * 3, y, cardWidth, 105),
            "↻",
            "Mises à jour",
            "SHA-256 + rollback\nToujours la bonne version");
    }

    private static void DrawFeatureCard(
        Graphics g,
        Rectangle rect,
        string icon,
        string title,
        string description)
    {
        using var path = RoundedRect(rect, 9);
        using var fill = new SolidBrush(Panel2);
        using var border = new Pen(Color.FromArgb(29, 53, 63));
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        var iconRect = new Rectangle(rect.Left + 13, rect.Top + 16, 38, 38);
        using (var iconPath = RoundedRect(iconRect, 8))
        using (var iconFill = new SolidBrush(Color.FromArgb(7, 68, 102)))
        {
            g.FillPath(iconFill, iconPath);
        }

        using var iconFont = FontOf("Segoe UI Symbol", 16F, FontStyle.Bold);
        using var cyanBrush = new SolidBrush(Cyan);
        g.DrawString(icon, iconFont, cyanBrush, iconRect.Left + 7, iconRect.Top + 7);

        using var titleFont = FontOf("Segoe UI Semibold", 9.2F, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.White);
        g.DrawString(title, titleFont, titleBrush, rect.Left + 60, rect.Top + 16);

        using var descriptionFont = FontOf("Segoe UI", 7.7F);
        using var descriptionBrush = new SolidBrush(Color.FromArgb(159, 177, 185));
        g.DrawString(description, descriptionFont, descriptionBrush, rect.Left + 60, rect.Top + 43);
    }

    private void DrawReleaseHistory(Graphics g)
    {
        var rect = new Rectangle(205, 510, Width - 225, 195);

        using var path = RoundedRect(rect, 10);
        using var fill = new SolidBrush(Panel);
        using var border = new Pen(Border);
        g.FillPath(fill, path);
        g.DrawPath(border, path);

        using var titleFont = FontOf("Segoe UI Semibold", 9F, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(219, 229, 234));
        g.DrawString("Dernières mises à jour", titleFont, titleBrush, rect.Left + 17, rect.Top + 12);

        DrawReleaseRow(
            g,
            rect.Left + 17,
            rect.Top + 42,
            rect.Width - 34,
            "v0.11.0",
            "Escouade temps réel · Beacon · nouvelle identité visuelle",
            "2026-10-19");

        DrawReleaseRow(
            g,
            rect.Left + 17,
            rect.Top + 85,
            rect.Width - 34,
            "v0.10.2",
            "Point local autonome · localisation indépendante de tarkov.dev",
            "2026-10-19");

        DrawReleaseRow(
            g,
            rect.Left + 17,
            rect.Top + 128,
            rect.Width - 34,
            "v0.10.1",
            "Chrono OCR fiabilisé · corrections et optimisations",
            "2026-10-19");

        var notesRect = new Rectangle(rect.Right - 152, rect.Top + 8, 128, 25);
        _hitAreas[LauncherAction.ReleaseNotes] = notesRect;

        using var notesPath = RoundedRect(notesRect, 6);
        using var notesFill = new SolidBrush(Color.FromArgb(15, 32, 39));
        g.FillPath(notesFill, notesPath);

        using var notesFont = FontOf("Segoe UI Semibold", 7.8F, FontStyle.Bold);
        using var notesBrush = new SolidBrush(CyanBright);
        g.DrawString("VOIR LES NOTES", notesFont, notesBrush, notesRect.Left + 18, notesRect.Top + 5);
    }

    private static void DrawReleaseRow(
        Graphics g,
        int x,
        int y,
        int width,
        string version,
        string summary,
        string date)
    {
        using var linePen = new Pen(Color.FromArgb(28, 44, 51));
        g.DrawLine(linePen, x, y + 35, x + width, y + 35);

        using var versionFont = FontOf("Segoe UI Semibold", 9F, FontStyle.Bold);
        using var white = new SolidBrush(Color.White);
        using var summaryFont = FontOf("Segoe UI", 8.1F);
        using var summaryBrush = new SolidBrush(Color.FromArgb(175, 189, 196));
        using var dateFont = FontOf("Segoe UI", 7.8F);
        using var dateBrush = new SolidBrush(Color.FromArgb(112, 132, 143));

        g.DrawString(version, versionFont, white, x, y + 2);
        g.DrawString(summary, summaryFont, summaryBrush, x + 90, y + 3);
        var dateSize = g.MeasureString(date, dateFont);
        g.DrawString(date, dateFont, dateBrush, x + width - dateSize.Width, y + 4);
    }

    private void DrawStatusAndProgress(Graphics g)
    {
        var statusRect = new Rectangle(205, 715, Width - 225, 30);

        var statusColor = Status switch
        {
            LauncherStatus.Success => Green,
            LauncherStatus.Warning => Amber,
            LauncherStatus.Error => Red,
            LauncherStatus.Update => Amber,
            _ => Cyan,
        };

        using var dot = new SolidBrush(statusColor);
        g.FillEllipse(dot, statusRect.Left + 2, statusRect.Top + 8, 8, 8);

        using var statusFont = FontOf("Segoe UI Semibold", 8.5F, FontStyle.Bold);
        using var statusBrush = new SolidBrush(Color.FromArgb(188, 206, 214));
        g.DrawString(StatusText, statusFont, statusBrush, statusRect.Left + 17, statusRect.Top + 3);

        using var feedFont = FontOf("Segoe UI", 7.6F);
        using var feedBrush = new SolidBrush(Color.FromArgb(92, 114, 124));
        var feedText = $"{ChannelLabel} · {FeedLabel}";
        var feedSize = g.MeasureString(feedText, feedFont);
        g.DrawString(
            feedText,
            feedFont,
            feedBrush,
            statusRect.Right - feedSize.Width,
            statusRect.Top + 4);

        if (Busy || Progress is > 0 and < 100)
        {
            var bar = new Rectangle(statusRect.Left, statusRect.Bottom - 4, statusRect.Width, 3);
            using var track = new SolidBrush(Color.FromArgb(28, 44, 51));
            g.FillRectangle(track, bar);

            var fillWidth = (int)Math.Round(bar.Width * Math.Clamp(Progress, 0, 100) / 100.0);
            using var progress = new LinearGradientBrush(
                bar,
                Cyan,
                Blue,
                LinearGradientMode.Horizontal);
            g.FillRectangle(progress, new Rectangle(bar.Left, bar.Top, fillWidth, bar.Height));
        }
    }

    private void DrawSecondaryButton(
        Graphics g,
        Rectangle rect,
        string text,
        LauncherAction action,
        bool enabled,
        Color accent)
    {
        var hovered = _hoverAction == action;
        var background = enabled
            ? hovered
                ? Color.FromArgb(25, 47, 57)
                : Color.FromArgb(17, 32, 39)
            : Color.FromArgb(23, 30, 34);

        using var path = RoundedRect(rect, 7);
        using var fill = new SolidBrush(background);
        g.FillPath(fill, path);

        using var border = new Pen(
            enabled
                ? Color.FromArgb(hovered ? 225 : 150, accent)
                : Color.FromArgb(54, 65, 70),
            1f);
        g.DrawPath(border, path);

        using var font = FontOf("Segoe UI Semibold", 8.1F, FontStyle.Bold);
        using var brush = new SolidBrush(
            enabled
                ? Color.FromArgb(hovered ? 255 : 226, 236, 241)
                : Color.FromArgb(92, 106, 113));
        var size = g.MeasureString(text, font);
        g.DrawString(
            text,
            font,
            brush,
            rect.Left + (rect.Width - size.Width) / 2,
            rect.Top + (rect.Height - size.Height) / 2 - 1);
    }

    private void DrawPrimaryButton(
        Graphics g,
        Rectangle rect,
        string text,
        bool enabled,
        LauncherAction action)
    {
        var hovered = _hoverAction == action;
        var c1 = enabled
            ? hovered ? Color.FromArgb(18, 169, 255) : Color.FromArgb(0, 141, 242)
            : Color.FromArgb(35, 56, 65);
        var c2 = enabled
            ? hovered ? Color.FromArgb(0, 101, 230) : Color.FromArgb(0, 93, 218)
            : Color.FromArgb(29, 45, 52);

        using var path = RoundedRect(rect, 8);
        using var fill = new LinearGradientBrush(rect, c1, c2, LinearGradientMode.Vertical);
        g.FillPath(fill, path);

        using var border = new Pen(enabled ? Color.FromArgb(67, 191, 255) : Color.FromArgb(55, 75, 84));
        g.DrawPath(border, path);

        using var font = FontOf("Segoe UI Semibold", 9F, FontStyle.Bold);
        using var brush = new SolidBrush(enabled ? Color.White : Color.FromArgb(117, 136, 145));

        var size = g.MeasureString(text, font);
        g.DrawString(
            text,
            font,
            brush,
            rect.Left + (rect.Width - size.Width) / 2,
            rect.Top + (rect.Height - size.Height) / 2 - 1);
    }

    private static void DrawCompassGlyph(Graphics g, Rectangle rect, Color color)
    {
        var cx = rect.Left + rect.Width / 2f;
        var cy = rect.Top + rect.Height / 2f;

        using var ringPen = new Pen(Color.FromArgb(110, color), Math.Max(1.2f, rect.Width * 0.035f));
        g.DrawEllipse(
            ringPen,
            rect.Left + rect.Width * 0.14f,
            rect.Top + rect.Height * 0.14f,
            rect.Width * 0.72f,
            rect.Height * 0.72f);

        var needle = new[]
        {
            new PointF(cx, rect.Top + rect.Height * 0.08f),
            new PointF(rect.Right - rect.Width * 0.26f, rect.Bottom - rect.Height * 0.16f),
            new PointF(cx, rect.Bottom - rect.Height * 0.30f),
            new PointF(rect.Left + rect.Width * 0.26f, rect.Bottom - rect.Height * 0.16f),
        };

        using var brush = new LinearGradientBrush(
            new PointF(cx, rect.Top),
            new PointF(cx, rect.Bottom),
            Color.FromArgb(235, 224, 255, 255),
            color);

        g.FillPolygon(brush, needle);

        using var outline = new Pen(Color.FromArgb(210, 207, 250, 255), Math.Max(1f, rect.Width * 0.035f));
        g.DrawPolygon(outline, needle);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var next = LauncherAction.None;
        foreach (var pair in _hitAreas)
        {
            if (pair.Value.Contains(e.Location))
            {
                next = pair.Key;
                break;
            }
        }

        if (next != _hoverAction)
        {
            _hoverAction = next;
            Cursor = next == LauncherAction.None ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverAction != LauncherAction.None)
        {
            _hoverAction = LauncherAction.None;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        foreach (var pair in _hitAreas)
        {
            if (pair.Value.Contains(e.Location))
            {
                if (pair.Key == LauncherAction.LaunchRaidPilot && !CanLaunchRaidPilot)
                {
                    return;
                }
                if (pair.Key == LauncherAction.LaunchBeacon && !CanLaunchBeacon)
                {
                    return;
                }
                if (pair.Key == LauncherAction.InstallUpdates && !UpdateAvailable)
                {
                    return;
                }
                ActionRequested?.Invoke(this, pair.Key);
                return;
            }
        }

        if (e.Y <= 38 && e.X >= 190)
        {
            DragRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private static Font FontOf(
        string family,
        float size,
        FontStyle style = FontStyle.Regular)
    {
        try
        {
            return new Font(family, size, style, GraphicsUnit.Point);
        }
        catch
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}

internal sealed class SquadLauncherDialog : Form
{
    private readonly TextBox _code = new();
    private readonly TextBox _name = new();

    private SquadLauncherDialog(
        string title,
        string roomCode,
        string playerName,
        bool codeReadOnly,
        string actionText)
    {
        Text = "RaidPilot — " + title;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 274);
        BackColor = Color.FromArgb(9, 15, 19);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9F);

        Controls.Add(new Label
        {
            Text = "ESCOUADE RAIDPILOT",
            Location = new Point(22, 18),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
            ForeColor = Color.FromArgb(106, 233, 255),
        });

        Controls.Add(new Label
        {
            Text = codeReadOnly
                ? "Modifie le nom qui sera visible par ton équipe."
                : "Entre le code reçu du joueur qui a créé l'escouade.",
            Location = new Point(24, 55),
            Size = new Size(510, 32),
            ForeColor = Color.FromArgb(166, 184, 193),
        });

        Controls.Add(new Label
        {
            Text = "Code d'escouade",
            Location = new Point(24, 94),
            AutoSize = true,
            ForeColor = Color.Gainsboro,
        });

        _code.Location = new Point(24, 116);
        _code.Size = new Size(326, 27);
        _code.Text = roomCode;
        _code.CharacterCasing = CharacterCasing.Upper;
        _code.ReadOnly = codeReadOnly;
        _code.BackColor = Color.FromArgb(18, 28, 34);
        _code.ForeColor = Color.White;
        _code.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_code);

        Controls.Add(new Label
        {
            Text = "Ton nom",
            Location = new Point(370, 94),
            AutoSize = true,
            ForeColor = Color.Gainsboro,
        });

        _name.Location = new Point(370, 116);
        _name.Size = new Size(164, 27);
        _name.Text = playerName;
        _name.MaxLength = 24;
        _name.BackColor = Color.FromArgb(18, 28, 34);
        _name.ForeColor = Color.White;
        _name.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_name);

        Controls.Add(new Label
        {
            Text = "Le code est enregistré dans le profil RaidPilot local. RaidPilot le reprendra automatiquement au prochain lancement.",
            Location = new Point(24, 157),
            Size = new Size(510, 40),
            ForeColor = Color.FromArgb(126, 149, 160),
        });

        var ok = new Button
        {
            Text = actionText,
            DialogResult = DialogResult.OK,
            Location = new Point(326, 216),
            Size = new Size(112, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 109, 183),
            ForeColor = Color.White,
        };
        ok.FlatAppearance.BorderColor = Color.FromArgb(79, 211, 245);

        var cancel = new Button
        {
            Text = "ANNULER",
            DialogResult = DialogResult.Cancel,
            Location = new Point(446, 216),
            Size = new Size(88, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(32, 42, 48),
            ForeColor = Color.White,
        };
        cancel.FlatAppearance.BorderColor = Color.FromArgb(66, 83, 91);

        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    internal static bool TryShow(
        IWin32Window owner,
        string title,
        string roomCode,
        string playerName,
        bool codeReadOnly,
        string actionText,
        out string normalizedRoomCode,
        out string normalizedName)
    {
        normalizedRoomCode = roomCode;
        normalizedName = SquadRelayClient.NormalizeName(playerName);

        using var dialog = new SquadLauncherDialog(
            title,
            roomCode,
            playerName,
            codeReadOnly,
            actionText);

        while (dialog.ShowDialog(owner) == DialogResult.OK)
        {
            if (!codeReadOnly)
            {
                if (!SquadRoomCode.TryNormalize(dialog._code.Text, out var normalized))
                {
                    MessageBox.Show(
                        dialog,
                        "Le code doit ressembler à RP-ABCD-EFGH-JKLM-NPQR.",
                        "RaidPilot — escouade",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    continue;
                }

                normalizedRoomCode = SquadRoomCode.Format(normalized);
            }

            normalizedName = SquadRelayClient.NormalizeName(dialog._name.Text);
            return true;
        }

        return false;
    }
}

internal sealed class ReleaseNotesDialog : Form
{
    internal ReleaseNotesDialog(string text)
    {
        Text = "RaidPilot — notes de version";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(680, 430);
        BackColor = Color.FromArgb(10, 16, 20);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9F);

        var header = new Label
        {
            Text = "NOTES DE VERSION",
            AutoSize = true,
            Location = new Point(22, 19),
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
            ForeColor = Color.FromArgb(86, 218, 247),
        };

        var box = new RichTextBox
        {
            Location = new Point(22, 58),
            Size = new Size(636, 315),
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(17, 25, 30),
            ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI", 9.2F),
            Text = text,
        };

        var close = new Button
        {
            Text = "Fermer",
            DialogResult = DialogResult.OK,
            Location = new Point(566, 386),
            Size = new Size(92, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(31, 45, 52),
            ForeColor = Color.White,
        };
        close.FlatAppearance.BorderColor = Color.FromArgb(63, 207, 244);

        Controls.Add(header);
        Controls.Add(box);
        Controls.Add(close);
        AcceptButton = close;
    }
}

internal sealed class UpdateFeedDialog : Form
{
    private readonly TextBox _location = new();

    internal UpdateFeedDialog(string current)
    {
        Text = "RaidPilot — source des mises à jour";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(590, 210);
        BackColor = Color.FromArgb(10, 16, 20);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9F);

        Controls.Add(new Label
        {
            Text = "SOURCE DES MISES À JOUR",
            Location = new Point(20, 18),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(86, 218, 247),
        });

        Controls.Add(new Label
        {
            Text = "Adresse HTTPS du manifeste RaidPilot",
            Location = new Point(22, 61),
            AutoSize = true,
            ForeColor = Color.Gainsboro,
        });

        Controls.Add(new Label
        {
            Text = "Le launcher vérifie le SHA-256 avant toute installation.",
            Location = new Point(22, 86),
            AutoSize = true,
            ForeColor = Color.FromArgb(142, 160, 169),
        });

        _location.Location = new Point(22, 116);
        _location.Size = new Size(545, 27);
        _location.Text = current;
        _location.BackColor = Color.FromArgb(20, 29, 34);
        _location.ForeColor = Color.White;
        _location.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_location);

        var ok = new Button
        {
            Text = "Enregistrer",
            DialogResult = DialogResult.OK,
            Location = new Point(374, 160),
            Size = new Size(92, 31),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 107, 176),
            ForeColor = Color.White,
        };
        ok.FlatAppearance.BorderColor = Color.FromArgb(86, 218, 247);

        var cancel = new Button
        {
            Text = "Annuler",
            DialogResult = DialogResult.Cancel,
            Location = new Point(475, 160),
            Size = new Size(92, 31),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(34, 43, 48),
            ForeColor = Color.White,
        };
        cancel.FlatAppearance.BorderColor = Color.FromArgb(71, 86, 94);

        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    internal string ManifestLocation => _location.Text.Trim();
}
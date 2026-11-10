using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace RaidPilot.Launcher;

internal sealed partial class LauncherForm
{
    private bool _foreverInstalling;

    private void SyncHabsStatus()
    {
        _surface.HabsEnabled = _habs.Enabled;
        _surface.HabsStatus = _habs.Status;
        _surface.Invalidate();
    }

    private void RefreshForeverPilot()
    {
        try { _surface.Forever = ForeverPilotService.Inspect(); _surface.ChangeLayerVersion = ChangeLayerService.InstalledVersion(_surface.Forever.GamePath); }
        catch (Exception e) { _surface.ForeverMessage = "Vérification impossible : " + e.Message; }
        _surface.Invalidate();
    }

    private async Task<bool> HandleForeverPilotAction(LauncherAction action)
    {
        if (action is not (LauncherAction.ForeverPilot or LauncherAction.LaunchBattleNet or
            LauncherAction.ChooseForeverFolder or LauncherAction.OpenForeverFolder or
            LauncherAction.RefreshForeverPilot or LauncherAction.InstallForeverPilot or LauncherAction.InstallChangeLayer or LauncherAction.InstallDungeonMaps or
            LauncherAction.ToggleHabsGoals or LauncherAction.PreviewHabsGoals)) return false;
        if (_foreverInstalling || _busy) return true;
        try
        {
            _surface.ActiveSection = LauncherSection.ForeverPilot;
            RefreshForeverPilot();
            var state = _surface.Forever;
            switch (action)
            {
                case LauncherAction.InstallDungeonMaps:
                    if (state.GamePath is null) throw new InvalidOperationException("Choisis d'abord ton dossier WoW Forever.");
                    _foreverInstalling = true; SetBusy(true);
                    _surface.ForeverMessage = "Sauvegarde et installation des cartes gratuites…"; _surface.Invalidate();
                    try { await Task.Run(() => DungeonMapService.Install(state.GamePath, DungeonMapService.BundlePath));
                        _surface.ForeverMessage = "DungeonMapPilot installé gratuitement. Relance WoW et tape /dmp. FR/EN dans Langue."; }
                    finally { _foreverInstalling = false; SetBusy(false); RefreshForeverPilot(); }
                    break;
                case LauncherAction.InstallChangeLayer:
                    if (state.GamePath is null) throw new InvalidOperationException("Choisis d'abord ton dossier WoW Forever.");
                    _foreverInstalling = true; SetBusy(true);
                    _surface.ForeverMessage = "Téléchargement de ChangeLayer (Foofi) depuis CurseForge…";
                    _surface.Invalidate();
                    try { _surface.ForeverMessage = await ChangeLayerService.DownloadAndInstall(state.GamePath); }
                    finally { _foreverInstalling = false; SetBusy(false); RefreshForeverPilot(); }
                    break;
                case LauncherAction.ToggleHabsGoals:
                    _habs.SetEnabled(!_habs.Enabled);
                    break;
                case LauncherAction.PreviewHabsGoals:
                    _habs.Preview();
                    WindowState = FormWindowState.Minimized;
                    break;
                case LauncherAction.ForeverPilot:
                    break;
                case LauncherAction.RefreshForeverPilot:
                    _surface.ForeverMessage = state.Ready
                        ? "Fichiers vérifiés. Active Ding et ForeverPilot_Leveling dans le jeu."
                        : state.Problem ?? "Installation à compléter avec le bouton Installer.";
                    break;
                case LauncherAction.LaunchBattleNet:
                    if (state.BattleNetPath is null)
                        throw new InvalidOperationException("Battle.net est introuvable. Ouvre ton launcher Battle.net habituel.");
                    Process.Start(new ProcessStartInfo(state.BattleNetPath) { UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(state.BattleNetPath)! });
                    _surface.ForeverMessage = "Battle.net ouvert : sélectionne WoW Forever, puis Jouer.";
                    break;
                case LauncherAction.ChooseForeverFolder:
                    using (var dialog = new FolderBrowserDialog { Description = "Choisis World of Warcraft ou son dossier _forever_",
                        UseDescriptionForTitle = true, InitialDirectory = state.GamePath ?? "", ShowNewFolderButton = false })
                    {
                        if (dialog.ShowDialog(this) == DialogResult.OK)
                        {
                            ForeverPilotService.SaveGamePath(dialog.SelectedPath);
                            _surface.ForeverMessage = "Dossier WoW Forever enregistré.";
                            RefreshForeverPilot();
                        }
                    }
                    break;
                case LauncherAction.OpenForeverFolder:
                    var folder = state.GamePath is null ? null : Path.Combine(state.GamePath, "Interface", "AddOns");
                    if (folder is null || !Directory.Exists(folder)) throw new DirectoryNotFoundException("Dossier AddOns introuvable.");
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                    break;
                case LauncherAction.InstallForeverPilot:
                    if (state.GamePath is null) throw new InvalidOperationException("Choisis d'abord ton dossier WoW Forever.");
                    if (state.BundledVersion is null) throw new InvalidOperationException("Le package ForeverPilot est absent ou incomplet.");
                    _foreverInstalling = true;
                    SetBusy(true);
                    _surface.ForeverMessage = "Sauvegarde et installation de ForeverPilot et Ding…";
                    _surface.Invalidate();
                    try
                    {
                        var backup = await Task.Run(() => ForeverPilotService.Install(state.GamePath, ForeverPilotService.BundlePath));
                        _surface.ForeverMessage = backup.Length > 0
                            ? "Addons installés avec sauvegarde. Active Ding et ForeverPilot_Leveling dans le jeu."
                            : "ForeverPilot et Ding installés. Active les deux à la sélection du personnage.";
                    }
                    finally
                    {
                        _foreverInstalling = false;
                        SetBusy(false);
                        RefreshForeverPilot();
                    }
                    break;
            }
        }
        catch (Exception e)
        {
            _surface.ForeverMessage = e is UnauthorizedAccessException
                ? "Accès refusé au dossier du jeu. Relance le launcher comme administrateur pour installer."
                : e.Message;
        }
        _surface.Invalidate();
        return true;
    }
}

internal sealed partial class LauncherSurface
{
    internal ForeverPilotStatus Forever = new(null, null, false, null, null, false, null);
    internal string ForeverMessage = "Les addons se chargent à l'intérieur de World of Warcraft.";
    internal string? ChangeLayerVersion;
    internal bool HabsEnabled;
    internal string HabsStatus = "Désactivé · Lumières visibles uniquement dans WoW";
    private static readonly Color ForeverAccent = Color.FromArgb(84, 224, 200);

    private void DrawForeverPilotPage(Graphics g)
    {
        var hero = new Rectangle(214, 60, 940, 205);
        using (var shape = RoundedRect(hero, 14))
        using (var fill = new LinearGradientBrush(hero, Color.FromArgb(19, 53, 58), Color.FromArgb(10, 23, 31), 10f))
            g.FillPath(fill, shape);
        using (var ring = new Pen(Color.FromArgb(25, ForeverAccent), 1.3f))
        {
            g.DrawEllipse(ring, 936, 52, 195, 195);
            g.DrawEllipse(ring, 963, 79, 141, 141);
        }
        DrawCompassGlyph(g, new Rectangle(987, 106, 92, 92), Color.FromArgb(255, 203, 78));
        ForeverText(g, "WORLD OF WARCRAFT  /  FOREVER", 244, 84, 630, 20, 9, ForeverAccent, true);
        ForeverText(g, "ForeverPilot", 242, 113, 680, 55, 32, Color.White, true);
        ForeverText(g, "Ton cap vers le niveau 60.", 245, 176, 680, 28, 14, Color.FromArgb(210, 231, 230));
        ForeverButton(g, new Rectangle(890, 215, 242, 34), "Cartes donjons · gratuit", LauncherAction.InstallDungeonMaps,
            Forever.GamePath is not null && !Forever.GameRunning && !Busy && Directory.Exists(DungeonMapService.BundlePath));
        ForeverText(g, "Édition de test · Toutes les fonctions gratuites · Aucun abonnement requis", 245, 217, 630, 25, 10, Muted);

        var statusTitle = Forever.Ready ? "INSTALLATION PRÊTE" : "INSTALLATION À VÉRIFIER";
        var accent = Forever.Ready ? ForeverAccent : Amber;
        using (var fill = new SolidBrush(Panel))
        using (var shape = RoundedRect(new Rectangle(214, 282, 940, 187), 12))
            g.FillPath(fill, shape);
        ForeverText(g, statusTitle, 238, 302, 560, 22, 10, accent, true);
        ForeverText(g, "ForeverPilot", 238, 339, 210, 22, 10, Muted);
        ForeverText(g, Forever.Version is null ? "Non installé" : "v" + Forever.Version, 238, 367, 210, 32, 18, Color.White, true);
        ForeverText(g, "Dépendance Ding", 476, 339, 210, 22, 10, Muted);
        ForeverText(g, Forever.DingInstalled ? "Présente" : "Manquante", 476, 367, 210, 32, 18,
            Forever.DingInstalled ? ForeverAccent : Amber, true);
        ForeverText(g, "Version fournie", 714, 339, 210, 22, 10, Muted);
        ForeverText(g, Forever.BundledVersion is null ? "Indisponible" : "v" + Forever.BundledVersion, 714, 367, 210, 32, 18, Color.White, true);
        ForeverText(g, Forever.Problem ?? Forever.GamePath ?? "Sélectionne ton installation WoW Forever pour commencer.",
            238, 425, 880, 23, 9, Forever.Problem is null ? Muted : Amber);

        ForeverButton(g, new Rectangle(214, 488, 300, 46), "Ouvrir Battle.net  →", LauncherAction.LaunchBattleNet,
            Forever.BattleNetPath is not null && !Busy, primary: true);
        var installText = Forever.Version is null ? "Installer ForeverPilot + Ding" : !Forever.DingInstalled ? "Installer / réparer Ding"
            : Forever.UpdateAvailable ? "Mettre l'addon à jour" : "Réinstaller l'addon";
        ForeverButton(g, new Rectangle(530, 488, 250, 46), installText, LauncherAction.InstallForeverPilot,
            Forever.GamePath is not null && Forever.BundledVersion is not null && !Forever.GameRunning && !Busy &&
            (!Forever.DingInstalled || Forever.Version is null || Version.Parse(Forever.Version) <= Version.Parse(Forever.BundledVersion)));
        ForeverButton(g, new Rectangle(796, 488, 170, 46), "Dossier AddOns", LauncherAction.OpenForeverFolder,
            Forever.GamePath is not null && !Busy);
        ForeverButton(g, new Rectangle(982, 488, 172, 46), "Vérifier", LauncherAction.RefreshForeverPilot, !Busy);

        var tip = Forever.GameRunning ? "WoW est ouvert : ferme le jeu avant d'installer une mise à jour de l'addon."
            : !Forever.DingInstalled ? "Ding est inclus : clique sur Installer, puis active Ding et ForeverPilot_Leveling dans le jeu."
            : "Dans le jeu : Options pour la langue et le profil. Garde Ding et ForeverPilot_Leveling activés.";
        ForeverText(g, tip, 218, 544, 932, 22, 9, Muted);
        ForeverText(g, "Layers : réseau public ChangeLayer par Foofi · téléchargement officiel CurseForge", 218, 575, 690, 20, 9, Muted);
        ForeverButton(g, new Rectangle(914, 566, 240, 30), ChangeLayerVersion is null ? "Installer ChangeLayer" : "ChangeLayer " + ChangeLayerVersion + " installé",
            LauncherAction.InstallChangeLayer, ChangeLayerVersion is null && Forever.GamePath is not null && !Forever.GameRunning && !Busy);
        using (var fill = new SolidBrush(Panel2))
        using (var shape = RoundedRect(new Rectangle(214, 600, 940, 90), 10))
            g.FillPath(fill, shape);
        ForeverText(g, "LE BUT  ·  CANADIENS DE MONTRÉAL", 236, 610, 650, 20, 10, Color.FromArgb(255, 115, 133), true);
        ForeverText(g, "Deux gyrophares + bande lumineuse · 10 secondes · Launcher ouvert, même réduit", 236, 636, 650, 20, 9, Muted);
        ForeverText(g, HabsStatus, 236, 660, 650, 20, 9, HabsEnabled ? ForeverAccent : Muted);
        ForeverButton(g, new Rectangle(914, 610, 218, 30), HabsEnabled ? "Lumières activées  ✓" : "Activer les lumières",
            LauncherAction.ToggleHabsGoals, !Busy);
        ForeverButton(g, new Rectangle(914, 650, 218, 30), "Tester dans WoW  →", LauncherAction.PreviewHabsGoals,
            Forever.GamePath is not null && !Busy);
        ForeverText(g, ForeverMessage, 218, 702, 720, 43, 9, Muted);
        ForeverButton(g, new Rectangle(982, 704, 172, 32), "Choisir le dossier WoW", LauncherAction.ChooseForeverFolder, !Busy);
    }

    private void ForeverButton(Graphics g, Rectangle rect, string label, LauncherAction action, bool enabled, bool primary = false)
    {
        if (enabled) _hitAreas[action] = rect;
        if (primary) DrawPrimaryButton(g, rect, label, enabled, action);
        else DrawSecondaryButton(g, rect, label, action, enabled, ForeverAccent);
    }

    private static void ForeverText(Graphics g, string text, int x, int y, int width, int height, float size, Color color, bool bold = false)
    {
        using var font = FontOf("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
        g.DrawString(text, font, brush, new RectangleF(x, y, width, height), format);
    }
}

using System.Drawing;
using System.Windows.Forms;

namespace RaidPilot.Overlay;

/// <summary>
/// First StashPilot UI: explicit user-reviewed data import, no unsafe claim
/// that screenshots or player profile have already been scanned.
/// </summary>
internal sealed class StashPilotForm : Form
{
    private readonly StashPilotF8Gate _f8Gate = new();
    internal bool HasPendingF8Scan => _f8Gate.IsArmed;
    internal bool TryReceiveF8(string path, long now, bool automatic, bool verified)
    {
        if (!_f8Gate.TryConsume(now, automatic, verified)) return false;
        _ = ScanAndReviewAsync(path);
        return true;
    }

    private readonly TextBox _inventory = new()
    {
        Multiline = true, ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill, Font = new Font("Consolas", 10),
        Text = "# Objet;quantité;FIR ou non;CONFIRMÉ si vérifié\r\n"
            + "Boulons;5;FIR;CONFIRMÉ\r\nRuban isolant;3;;CONFIRMÉ"
    };
    private readonly TextBox _requirements = new()
    {
        Multiline = true, ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill, Font = new Font("Consolas", 10),
        Text = "# Objet;quantité;Quête ou module;FIR si nécessaire;CONFIRMÉ\r\n"
            + "Boulons;3;Planque - module choisi;;CONFIRMÉ\r\n"
            + "Ruban isolant;2;Quête active;FIR;CONFIRMÉ"
    };
    private readonly DataGridView _results = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.FromArgb(22, 28, 33),
        ForeColor = Color.Black, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    private readonly Label _status = new()
    {
        Text = "Données d'exemple — remplace-les par tes objets et objectifs vérifiés.",
        AutoSize = true, ForeColor = Color.FromArgb(185, 213, 223),
        Dock = DockStyle.Top, Padding = new Padding(6)
    };

    internal StashPilotForm()
    {
        Text = "RaidPilot — StashPilot (conseiller d'inventaire)";
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                Analyze();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.S)
            {
                _status.Text = StashPilotWorkspace.TrySave(
                    _inventory.Text, _requirements.Text)
                    ? "Données enregistrées sur ce PC."
                    : "Sauvegarde impossible.";
                e.SuppressKeyPress = true;
            }
        };
        Size = new Size(1080, 740);
        MinimumSize = new Size(780, 580);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(17, 23, 30);
        ForeColor = Color.White;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "STASHPILOT — F8 dans le stash  →  Vérifier  →  Analyser",
            Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(104, 226, 212),
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);

        var inventoryPanel = CreatePanel("INVENTAIRE — une ligne par objet", _inventory);
        var needsPanel = CreatePanel("OBJECTIFS — quêtes et modules de planque", _requirements);
        layout.Controls.Add(inventoryPanel, 0, 1);
        layout.Controls.Add(needsPanel, 1, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            AutoSize = false, WrapContents = true, AutoScroll = true
        };
        var calculate = new Button { Text = "Analyser (Ctrl+Entrée)", AutoSize = true, Height = 35 };
        calculate.Click += (_, _) => Analyze();
        var pasteInventory = new Button { Text = "Coller inventaire", AutoSize = true, Height = 35 };
        pasteInventory.Click += (_, _) => { if (Clipboard.ContainsText()) _inventory.Text = Clipboard.GetText(); };
        var pasteNeeds = new Button { Text = "Coller objectifs", AutoSize = true, Height = 35 };
        pasteNeeds.Click += (_, _) => { if (Clipboard.ContainsText()) _requirements.Text = Clipboard.GetText(); };
        var copyResult = new Button { Text = "Copier résultats", AutoSize = true, Height = 35 };
        copyResult.Click += (_, _) =>
        {
            var rows = _results.Rows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r => string.Join("; ", r.Cells.Cast<DataGridViewCell>()
                    .Select(c => Convert.ToString(c.Value) ?? "")));
            Clipboard.SetText(string.Join(Environment.NewLine, rows));
        };
        var save = new Button { Text = "Sauvegarder sur ce PC", AutoSize = true, Height = 35 };
        save.Click += (_, _) =>
        {
            _status.Text = StashPilotWorkspace.TrySave(_inventory.Text, _requirements.Text)
                ? "Progression enregistrée localement. Aucune synchronisation réseau."
                : "Sauvegarde impossible — aucune donnée modifiée.";
        };
        var reload = new Button { Text = "Charger ma progression", AutoSize = true, Height = 35 };
        reload.Click += (_, _) =>
        {
            if (!StashPilotWorkspace.TryLoad(out var items, out var needs))
            {
                _status.Text = "Aucune progression locale valide trouvée.";
                return;
            }
            _inventory.Text = items;
            _requirements.Text = needs;
            Analyze();
        };
        var scan = new Button { Text = "1. Scanner avec F8", AutoSize = true, Height = 35 };
        scan.Click += (_, _) =>
        {
            _f8Gate.Arm(System.Diagnostics.Stopwatch.GetTimestamp());
            _status.Text = "Scan armé pour 90 secondes : ouvre ton inventaire dans Tarkov, puis appuie sur F8 une fois.";
        };
        var fromFile = new Button { Text = "Choisir PNG", AutoSize = true, Height = 35 };
        fromFile.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Capture Tarkov PNG (*.png)|*.png",
                Title = "Choisir une capture PNG",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                await ScanAndReviewAsync(dialog.FileName);
        };
        var cancel = new Button { Text = "Annuler F8", AutoSize = true, Height = 35 };
        cancel.Click += (_, _) =>
        {
            _f8Gate.Cancel();
            _status.Text = "Scan F8 annulé.";
        };
        actions.Controls.AddRange([scan, cancel, fromFile, calculate,
            pasteInventory, pasteNeeds, save, reload, copyResult]);
        layout.Controls.Add(actions, 0, 2);
        layout.SetColumnSpan(actions, 2);

        var bottom = new Panel { Dock = DockStyle.Fill };
        bottom.Controls.Add(_results);
        bottom.Controls.Add(_status);
        layout.Controls.Add(bottom, 0, 3);
        layout.SetColumnSpan(bottom, 2);
        Controls.Add(layout);
        _results.Columns.Add("item", "Objet");
        _results.Columns.Add("owned", "Possédé");
        _results.Columns.Add("status", "Conseil");
        _results.Columns.Add("required", "Besoin vérifié");
        _results.Columns.Add("keep", "Réserver");
        _results.Columns.Add("surplus", "Excédent théorique");
        _results.Columns.Add("fir", "FIR manquant");
        _results.Columns.Add("why", "Pour");
        if (StashPilotWorkspace.TryLoad(out var storedInventory, out var storedNeeds))
        {
            _inventory.Text = storedInventory;
            _requirements.Text = storedNeeds;
        }
        Analyze();
        Shown += (_, _) =>
        {
            _f8Gate.Arm(System.Diagnostics.Stopwatch.GetTimestamp());
            _status.Text = "Scan prêt : ouvre l'inventaire Tarkov et appuie sur F8. Le prochain F8 manuel sera analysé (90 s).";
        };
    }

    private async Task ScanAndReviewAsync(string screenshotPath)
    {
            try
            {
                var lines = await StashPilotImageScanner.ScanAsync(
                    screenshotPath, CancellationToken.None);
                using var review = new Form
                {
                    Text = "StashPilot — lignes OCR à vérifier",
                    Size = new Size(790, 570),
                    StartPosition = FormStartPosition.CenterParent
                };
                var editor = new TextBox
                {
                    Multiline = true, ScrollBars = ScrollBars.Both,
                    Dock = DockStyle.Fill,
                    Text = string.Join(Environment.NewLine, lines),
                    Font = new Font("Consolas", 10)
                };
                var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
                var toInventory = new Button { Text = "Joindre à inventaire (non confirmé)", AutoSize = true };
                var toObjectives = new Button { Text = "Joindre aux objectifs (non confirmé)", AutoSize = true };
                var propose = new Button
                {
                    Text = "Proposer quantités (à confirmer)",
                    AutoSize = true
                };
                footer.Controls.AddRange([toInventory, toObjectives, propose]);
                propose.Click += (_, _) =>
                {
                    var candidates = StashPilotOcrProposals.Parse(editor.Lines);
                    if (candidates.Count == 0)
                    {
                        MessageBox.Show(review,
                            "Aucune ligne au format « Boulons x4 » ou « 4 x Boulons » reconnue.",
                            "StashPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    _inventory.AppendText(Environment.NewLine
                        + "# PROPOSITIONS OCR : vérifier identité, quantité et FIR."
                        + Environment.NewLine
                        + StashPilotOcrProposals.AsReviewLines(candidates));
                    review.Close();
                };
                toInventory.Click += (_, _) =>
                {
                    _inventory.AppendText(Environment.NewLine + "# OCR non vérifié, corriger les quantités et le nom" +
                        Environment.NewLine + string.Join(Environment.NewLine,
                            StashPilotImageScanner.FilterCandidates(editor.Lines)
                                .Select(line => "# OCR : " + line)));
                    review.Close();
                };
                toObjectives.Click += (_, _) =>
                {
                    _requirements.AppendText(Environment.NewLine +
                        string.Join(Environment.NewLine,
                            StashPilotImageScanner.FilterCandidates(editor.Lines)
                                .Select(line => "# OCR : " + line)));
                    review.Close();
                };
                review.Controls.Add(editor);
                review.Controls.Add(footer);
                review.ShowDialog(this);
                _status.Text = "OCR importé comme notes non vérifiées. Confirme manuellement chaque objet et quantité avant analyse.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException
                or System.Runtime.InteropServices.COMException)
            {
                MessageBox.Show(this, "Lecture impossible : " + error.Message,
                    "StashPilot OCR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
    }

    private static Control CreatePanel(string title, TextBox editor)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        panel.Controls.Add(editor);
        panel.Controls.Add(new Label
        {
            Text = title, Dock = DockStyle.Top,
            Height = 24, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        return panel;
    }

    private void Analyze()
    {
        var items = StashPilotAdvisor.ParseInventory(_inventory.Text);
        var needs = StashPilotAdvisor.ParseNeeds(_requirements.Text);
        var advice = StashPilotAdvisor.Analyze(items, needs);
        _results.Rows.Clear();
        foreach (var row in advice)
            _results.Rows.Add(row.Item, row.Quantity, row.Recommendation,
                row.Required, row.ToKeep, row.Excess,
                row.MissingFoundInRaid, row.Reasons);
        _status.Text = $"{items.Count} lignes inventaire • {needs.Count} objectifs • "
            + "EXCÉDENT = estimation, pas une autorisation de vendre. "
            + "Aucune capture de Tarkov ni synchronisation automatique effectuée.";
    }
}

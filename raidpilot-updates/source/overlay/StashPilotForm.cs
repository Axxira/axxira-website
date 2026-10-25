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
    private bool _menuScanRunning;
    internal void ReceiveMenuF8(string path)
    {
        if (_menuScanRunning || IsDisposed) return;
        _f8Gate.Cancel();
        _ = ReceiveMenuF8Async(path);
    }

    private async Task ReceiveMenuF8Async(string path)
    {
        _menuScanRunning = true;
        try
        {
            _status.Text = "F8 détecté dans le menu — lecture de l'inventaire...";
            // FileSystemWatcher often signals Created before Tarkov has
            // finished flushing the PNG. Wait briefly; never delete it.
            long previousLength = -1;
            var stableProbes = 0;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    var info = new FileInfo(path);
                    var length = info.Exists ? info.Length : 0;
                    stableProbes = length > 4096 && length == previousLength
                        ? stableProbes + 1 : 0;
                    previousLength = length;
                    if (stableProbes >= 2)
                    {
                        await ScanAndReviewAsync(path);
                        return;
                    }
                }
                catch (IOException) { stableProbes = 0; }
                await Task.Delay(150);
            }
            _status.Text = "Capture F8 incomplète — relance le scan.";
        }
        finally { _menuScanRunning = false; }
    }

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

        // Essential screen: one instruction, one results table, advanced data hidden.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var heading = new Label
        {
            Text = "STASHPILOT  •  Ouvre ton inventaire dans Tarkov, puis F8",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(104, 226, 212)
        };
        root.Controls.Add(heading, 0, 0);

        _results.Columns.Add("item", "Objet");
        _results.Columns.Add("owned", "Quantité");
        _results.Columns.Add("status", "Décision");
        _results.Columns.Add("why", "Pourquoi");
        _results.Columns["item"]!.FillWeight = 30;
        _results.Columns["owned"]!.FillWeight = 12;
        _results.Columns["status"]!.FillWeight = 20;
        _results.Columns["why"]!.FillWeight = 38;
        root.Controls.Add(_results, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        var details = new Button { Text = "Corriger mes données", AutoSize = true };
        details.Click += (_, _) => OpenDetails();
        var fromFile = new Button { Text = "Ouvrir une capture", AutoSize = true };
        fromFile.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Capture Tarkov PNG (*.png)|*.png",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                await ScanAndReviewAsync(dialog.FileName);
        };
        actions.Controls.AddRange([details, fromFile]);
        root.Controls.Add(actions, 0, 2);
        root.Controls.Add(_status, 0, 3);
        Controls.Add(root);

        if (StashPilotWorkspace.TryLoad(out var storedInventory, out var storedNeeds))
        {
            _inventory.Text = storedInventory;
            _requirements.Text = storedNeeds;
        }
        else
        {
            // Never present demonstration inventory as real player progress.
            _inventory.Clear();
            _requirements.Clear();
        }
        Analyze();
        Shown += (_, _) =>
        {
            _f8Gate.Arm(System.Diagnostics.Stopwatch.GetTimestamp());
            _status.Text = "Prêt : F8 dans le menu Tarkov lance le scan. Les résultats incertains restent à vérifier.";
        };
    }

    private void OpenDetails()
    {
        using var dialog = new Form
        {
            Text = "StashPilot — corriger mes objets et objectifs",
            Size = new Size(820, 540), StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(17, 23, 30), ForeColor = Color.White
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Padding = new Padding(10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var itemsEditor = new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill, Text = _inventory.Text
        };
        var needsEditor = new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill, Text = _requirements.Text
        };
        layout.Controls.Add(CreatePanel("Mes objets : nom;quantité;FIR;CONFIRMÉ", itemsEditor), 0, 0);
        layout.Controls.Add(CreatePanel("Mes besoins : nom;quantité;source;FIR;CONFIRMÉ", needsEditor), 1, 0);
        var save = new Button { Text = "Enregistrer et actualiser", AutoSize = true };
        save.Click += (_, _) =>
        {
            if (StashPilotWorkspace.TrySave(itemsEditor.Text, needsEditor.Text))
            {
                _inventory.Text = itemsEditor.Text;
                _requirements.Text = needsEditor.Text;
                Analyze();
                dialog.Close();
            }
            else
                MessageBox.Show(dialog, "Sauvegarde impossible. Tes données restent affichées.",
                    "StashPilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        layout.Controls.Add(save, 0, 1);
        layout.SetColumnSpan(save, 2);
        dialog.Controls.Add(layout);
        dialog.ShowDialog(this);
    }

    private async Task ScanAndReviewAsync(string screenshotPath)
    {
        try
        {
            _status.Text = "Lecture de la capture F8...";
            var lines = await StashPilotImageScanner.ScanAsync(
                screenshotPath, CancellationToken.None);
            var suggestions = StashPilotOcrProposals.Parse(lines);
            if (suggestions.Count == 0)
            {
                _status.Text = "Aucun nom et quantité fiables détectés. Rien n'a été changé.";
                MessageBox.Show(this,
                    "Le texte de cette capture ne permet pas d'identifier les objets. " +
                    "Les icônes seules ne sont pas encore reconnues. " +
                    "Essaie une capture avec les noms et quantités visibles.",
                    "StashPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var review = new Form
            {
                Text = "StashPilot — confirmer le scan",
                Size = new Size(570, 460),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(17, 23, 30), ForeColor = Color.White
            };
            var heading = new Label
            {
                Text = "Vérifie les objets et quantités. Corrige les erreurs avant de confirmer.",
                Dock = DockStyle.Top, Height = 56,
                ForeColor = Color.White, Padding = new Padding(10)
            };
            var editor = new TextBox
            {
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill, Font = new Font("Consolas", 11),
                Text = string.Join(Environment.NewLine,
                    suggestions.Select(i => i.Item + " x" + i.Quantity))
            };
            var confirm = new Button
            {
                Text = "Confirmer mon inventaire",
                Dock = DockStyle.Bottom, Height = 48
            };
            confirm.Click += (_, _) =>
            {
                var verified = StashPilotOcrProposals.Parse(editor.Lines);
                if (verified.Count == 0)
                {
                    MessageBox.Show(review, "Aucun objet valide à confirmer.",
                        "StashPilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                // The user explicitly checked names and quantities. Do not
                // claim Found-in-Raid from OCR or mark a sale as safe.
                var inventoryText = string.Join(Environment.NewLine,
                    verified.Select(i => i.Item + ";" + i.Quantity + ";;CONFIRMÉ"));
                if (!StashPilotWorkspace.TrySave(inventoryText, _requirements.Text))
                {
                    MessageBox.Show(review, "Impossible d'enregistrer le scan.",
                        "StashPilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _inventory.Text = inventoryText;
                Analyze();
                review.Close();
            };
            review.Controls.Add(editor);
            review.Controls.Add(confirm);
            review.Controls.Add(heading);
            review.ShowDialog(this);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException
            or System.Runtime.InteropServices.COMException)
        {
            _status.Text = "Scan impossible. Aucun changement enregistré.";
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
        {
            var category = row.VerifiedNeed ? "À garder" : "À vérifier";
            var why = row.VerifiedNeed ? row.Reasons
                : "Utilité non confirmée — ne pas vendre sans vérifier";
            _results.Rows.Add(row.Item, row.Quantity, category, why);
        }
        _status.Text = $"{items.Count} objets • {needs.Count} besoins confirmés • "
            + "Aucune vente automatique. Les objets incertains restent à vérifier.";
    }
}

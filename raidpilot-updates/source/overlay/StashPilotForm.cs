using System.Drawing;
using System.Windows.Forms;

namespace RaidPilot.Overlay;

/// <summary>
/// First StashPilot UI: explicit user-reviewed data import, no unsafe claim
/// that screenshots or player profile have already been scanned.
/// </summary>
internal sealed class StashPilotForm : Form
{
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "STASHPILOT — Progression • Quêtes • Planque • Inventaire",
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
            AutoSize = false, WrapContents = false
        };
        var calculate = new Button { Text = "Analyser les besoins", AutoSize = true, Height = 35 };
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
        actions.Controls.AddRange([calculate, pasteInventory, pasteNeeds,
            save, reload, copyResult]);
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

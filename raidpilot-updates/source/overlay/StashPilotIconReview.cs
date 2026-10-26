using System.Drawing;
using System.Windows.Forms;

namespace RaidPilot.Overlay;

/// <summary>
/// Optional visual review: user marks ONE icon in the F8 screenshot. Searches
/// a bounded reference set without claiming automatic inventory segmentation.
/// </summary>
internal static class StashPilotIconReview
{
    internal static async Task<string?> ReviewAsync(
        IWin32Window owner, string screenshotPath,
        IReadOnlyList<StashPilotItemCatalog.Item> catalog)
    {
        if (catalog.Count == 0) return null;
        using var screenshot = new Bitmap(screenshotPath);
        using var form = new Form
        {
            Text = "StashPilot — comparer une icône",
            Size = new Size(900, 700), StartPosition = FormStartPosition.CenterParent
        };
        var picture = new PictureBox
        {
            Image = screenshot, SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 28, 33)
        };
        var status = new Label
        {
            Text = "Trace un rectangle sur l'icône à reconnaître.",
            Dock = DockStyle.Top, Height = 32
        };
        var results = new ListBox { Dock = DockStyle.Right, Width = 280 };
        var compare = new Button
        {
            Text = "Comparer la sélection", Dock = DockStyle.Bottom, Height = 42
        };
        var accept = new Button
        {
            Text = "Utiliser cet objet", Dock = DockStyle.Bottom, Height = 42,
            Enabled = false
        };
        Rectangle selected = Rectangle.Empty;
        Point start = Point.Empty;
        bool selecting = false;
        Point ToImage(Point p)
        {
            var scale = Math.Min(picture.ClientSize.Width / (double)screenshot.Width,
                picture.ClientSize.Height / (double)screenshot.Height);
            var width = screenshot.Width * scale;
            var height = screenshot.Height * scale;
            var dx = (picture.ClientSize.Width - width) / 2;
            var dy = (picture.ClientSize.Height - height) / 2;
            return new Point(
                Math.Clamp((int)((p.X - dx) / scale), 0, screenshot.Width - 1),
                Math.Clamp((int)((p.Y - dy) / scale), 0, screenshot.Height - 1));
        }
        picture.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            selecting = true;
            start = ToImage(e.Location);
        };
        picture.MouseUp += (_, e) =>
        {
            if (!selecting) return;
            selecting = false;
            var end = ToImage(e.Location);
            selected = Rectangle.FromLTRB(Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y), Math.Max(start.X, end.X),
                Math.Max(start.Y, end.Y));
            status.Text = selected.Width < 12 || selected.Height < 12
                ? "Sélection trop petite." : "Icône sélectionnée : comparer.";
        };
        var matches = new List<StashPilotIconIndex.Candidate>();
        compare.Click += async (_, _) =>
        {
            if (selected.Width < 12 || selected.Height < 12) return;
            compare.Enabled = false;
            accept.Enabled = false;
            matches.Clear();
            results.Items.Clear();
            status.Text = "Comparaison des références disponibles...";
            try
            {
                // Limit network requests during interactive review; a complete
                // preindexed library must be built separately for full coverage.
                var refs = new List<StashPilotIconIndex.Reference>();
                foreach (var item in catalog.Take(120))
                {
                    var found = await StashPilotIconIndex.GetAsync(item, CancellationToken.None);
                    if (found is not null) refs.Add(found);
                }
                if (refs.Count > 0)
                {
                    using var crop = screenshot.Clone(selected, screenshot.PixelFormat);
                    matches.AddRange(StashPilotIconIndex.FindCandidates(crop, refs));
                    foreach (var match in matches)
                        results.Items.Add(match.Name + " — écart " + match.Distance);
                    accept.Enabled = matches.Count > 0;
                }
                status.Text = matches.Count == 0
                    ? "Aucune référence visuelle disponible."
                    : "Résultats exploratoires : sélectionne et vérifie un objet.";
            }
            catch (Exception ex) when (ex is IOException or ArgumentException
                or System.Runtime.InteropServices.ExternalException)
            {
                status.Text = "Comparaison impossible : " + ex.Message;
            }
            finally { compare.Enabled = true; }
        };
        string? chosen = null;
        accept.Click += (_, _) =>
        {
            if (results.SelectedIndex < 0 || results.SelectedIndex >= matches.Count)
                return;
            chosen = matches[results.SelectedIndex].Name;
            form.Close();
        };
        form.Controls.Add(picture);
        form.Controls.Add(results);
        form.Controls.Add(compare);
        form.Controls.Add(accept);
        form.Controls.Add(status);
        form.ShowDialog(owner);
        return chosen;
    }
}

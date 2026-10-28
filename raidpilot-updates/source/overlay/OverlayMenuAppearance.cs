using System.Drawing;
using System.Windows.Forms;

namespace RaidPilot.Overlay;

/// <summary>
/// Compact, accessible Windows tray menu for routine play. Grouping only
/// re-parents the existing ToolStripMenuItem instances and therefore keeps
/// their click handlers, checked states, saved preferences and hotkeys.
/// </summary>
internal sealed partial class OverlayForm
{
    private void PolishTrayMenu()
    {
        if (_trayIcon.ContextMenuStrip is not { } menu) return;

        menu.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        menu.ForeColor = Color.FromArgb(235, 243, 248);
        menu.BackColor = Color.FromArgb(16, 28, 39);
        menu.Padding = new Padding(5, 6, 5, 6);
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
        menu.Renderer = new ToolStripProfessionalRenderer(new RaidPilotMenuColors());

        var nav = new ToolStripMenuItem("Navigation et CORTEX");
        var quest = new ToolStripMenuItem("QuestPilot et progression");
        var map = new ToolStripMenuItem("Carte et affichage");

        void Move(ToolStripMenuItem destination, ToolStripItem item)
        {
            if (!menu.Items.Contains(item)) return;
            menu.Items.Remove(item);
            destination.DropDownItems.Add(item);
        }

        Move(nav, _liveTrackingMenuItem);
        Move(nav, _precisionModeMenu);
        Move(nav, _precisionV2ToggleMenuItem);
        Move(nav, _precisionV3GpuMenuItem);
        Move(nav, _precisionV4Menu);
        Move(nav, _cortexMenu);
        Move(nav, _cortexV4Menu);
        nav.DropDownItems.Add(new ToolStripSeparator());
        Move(nav, _autoReanchorMenuItem);
        Move(nav, _mobileExactAnchorMenuItem);
        Move(nav, _positionKeyMenuItem);
        Move(nav, _gpsMenuItem);
        nav.DropDownItems.Add(new ToolStripSeparator());
        Move(nav, _precisionV4ExportMenu);
        Move(nav, _cortexV4ReportMenu);

        Move(quest, _questMenuItem);
        Move(quest, _questContextMenuItem);
        Move(quest, _ignoreQuestObjectiveMenuItem);

        var mapChooser = menu.Items.OfType<ToolStripMenuItem>()
            .FirstOrDefault(x => x.Text == "Choisir la carte");
        if (mapChooser is not null) Move(map, mapChooser);
        Move(map, _automaticFloorMenuItem);
        Move(map, _mapSizeMenuItem);
        Move(map, _mapCornerMenuItem);
        Move(map, _streamVisibilityMenuItem);
        Move(map, _collectiveAtlasShareMenuItem);
        map.DropDownItems.Add(new ToolStripSeparator());
        Move(map, _extractionMenuItem);
        var raidType = menu.Items.OfType<ToolStripMenuItem>()
            .FirstOrDefault(x => x.Text == "Type de raid pour les extractions");
        if (raidType is not null) Move(map, raidType);

        var exit = menu.Items.OfType<ToolStripMenuItem>()
            .FirstOrDefault(x => x.Text == "Quitter");
        if (exit is not null) menu.Items.Remove(exit);
        if (menu.Items.Contains(_squadMenuItem))
            menu.Items.Remove(_squadMenuItem);
        foreach (var separator in menu.Items.OfType<ToolStripSeparator>().ToArray())
            menu.Items.Remove(separator);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(nav);
        menu.Items.Add(quest);
        menu.Items.Add(map);
        menu.Items.Add(_squadMenuItem);
        if (exit is not null)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);
        }
    }

    private sealed class RaidPilotMenuColors : ProfessionalColorTable
    {
        private static readonly Color Surface = Color.FromArgb(16, 28, 39);
        private static readonly Color Hover = Color.FromArgb(37, 67, 83);
        private static readonly Color Border = Color.FromArgb(62, 92, 107);
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Border;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientMiddle => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
        public override Color CheckBackground => Hover;
        public override Color CheckPressedBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
    }
}

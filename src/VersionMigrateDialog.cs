using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ModProfileSwitcher.Models;

namespace ModProfileSwitcher
{
    /// <summary>
    /// Shows the migration analysis for each mod when upgrading/downgrading to a
    /// different Minecraft version. Color-coded rows:
    ///   Green  = version available for target MC version (checked)
    ///   Yellow = incompatible — no version for target (unchecked, will be removed)
    ///   Gray   = could not identify mod (unchecked)
    /// Also displays dependency information.
    /// </summary>
    public class VersionMigrateDialog : Form
    {
        private ListView lvMods;

        public List<MigratableMod> SelectedMods { get; private set; } = new List<MigratableMod>();

        /// <summary>If true, the user chose to keep incompatible mods in the folder (don't delete them).</summary>
        public bool KeepIncompatible { get; private set; } = true;

        private readonly List<MigratableMod> _all;

        public VersionMigrateDialog(List<MigratableMod> mods, string targetVersion)
        {
            _all = mods;

            var available = mods.Count(m => m.Available);
            var incompatible = mods.Count(m => m.Incompatible);
            var notFound = mods.Count(m => m.NotFound);

            Text = $"Migrate to Minecraft {targetVersion}";
            Size = new Size(860, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9);

            // Summary label
            var summaryParts = new List<string> { $"{mods.Count} mods scanned" };
            if (available > 0) summaryParts.Add($"✓ {available} available for {targetVersion}");
            if (incompatible > 0) summaryParts.Add($"✗ {incompatible} incompatible");
            if (notFound > 0) summaryParts.Add($"❓ {notFound} not identified");
            var lbl = new Label { Text = string.Join("  •  ", summaryParts), Location = new Point(12, 12), Size = new Size(820, 20) };

            lvMods = new ListView
            {
                Location = new Point(12, 36),
                Size = new Size(820, 380),
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9)
            };

            lvMods.Columns.Add("Mod", 160);
            lvMods.Columns.Add("Current File", 170);
            lvMods.Columns.Add("Target Version", 150);
            lvMods.Columns.Add("Dependencies", 140);
            lvMods.Columns.Add("Status", 180);

            foreach (var m in mods)
            {
                var name = !string.IsNullOrEmpty(m.ProjectTitle) ? m.ProjectTitle : m.CurrentFileName;
                string targetCol;
                string depsCol;
                Color rowColor;

                if (m.NotFound)
                {
                    targetCol = "—";
                    depsCol = "";
                    rowColor = Color.FromArgb(240, 240, 240); // light gray
                }
                else if (m.Incompatible)
                {
                    targetCol = "—";
                    depsCol = "";
                    rowColor = Color.FromArgb(255, 255, 210); // light yellow
                }
                else if (m.Available)
                {
                    targetCol = m.TargetVersionNumber;
                    depsCol = m.Dependencies.Count > 0 ? string.Join(", ", m.Dependencies) : "None";
                    rowColor = Color.FromArgb(220, 255, 220); // light green
                }
                else
                {
                    targetCol = "—";
                    depsCol = "";
                    rowColor = Color.White;
                }

                var item = new ListViewItem(new[]
                {
                    name,
                    m.CurrentFileName,
                    targetCol,
                    depsCol,
                    m.StatusMessage
                });
                item.Checked = m.Selected;
                item.BackColor = rowColor;
                item.Tag = m;
                lvMods.Items.Add(item);
            }

            // Buttons row 1: selection helpers
            var btnSelectAll = new Button { Text = "Select All Available", Location = new Point(12, 424), Size = new Size(140, 28) };
            btnSelectAll.Click += (s, e) =>
            {
                foreach (ListViewItem item in lvMods.Items)
                    if (((MigratableMod)item.Tag).Available) item.Checked = true;
            };

            var btnSelectNone = new Button { Text = "Select None", Location = new Point(158, 424), Size = new Size(100, 28) };
            btnSelectNone.Click += (s, e) =>
            {
                foreach (ListViewItem item in lvMods.Items) item.Checked = false;
            };

            // Incompatible handling
            var chkKeep = new CheckBox
            {
                Text = "Keep incompatible mods in folder (don't delete)",
                Location = new Point(280, 427),
                Size = new Size(350, 22),
                Checked = true
            };

            // Buttons row 2: action
            var btnMigrate = new Button
            {
                Text = $"🔄 Migrate to {targetVersion}",
                Location = new Point(620, 482),
                Size = new Size(150, 34),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };
            btnMigrate.Click += (s, e) =>
            {
                KeepIncompatible = chkKeep.Checked;
                SelectedMods = new List<MigratableMod>();
                foreach (ListViewItem item in lvMods.Items)
                {
                    if (item.Checked)
                    {
                        var mod = (MigratableMod)item.Tag;
                        if (mod.Available && !string.IsNullOrEmpty(mod.TargetDownloadUrl))
                            SelectedMods.Add(mod);
                    }
                }
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(780, 482),
                Size = new Size(80, 34),
                DialogResult = DialogResult.Cancel
            };

            // Warning label for incompatible mods
            string warnText = "";
            if (incompatible > 0)
                warnText = $"⚠ {incompatible} mod(s) have no version for {targetVersion} and will {(incompatible > 0 ? "be kept as-is (may cause errors)" : "be removed")}.";
            var lblWarn = new Label
            {
                Text = warnText,
                Location = new Point(12, 460),
                Size = new Size(600, 20),
                ForeColor = Color.OrangeRed
            };

            AcceptButton = btnMigrate;
            CancelButton = btnCancel;
            Controls.AddRange(new Control[] { lbl, lvMods, btnSelectAll, btnSelectNone, chkKeep, lblWarn, btnMigrate, btnCancel });
        }
    }
}

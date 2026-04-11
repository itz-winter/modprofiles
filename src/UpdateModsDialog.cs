using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ModProfileSwitcher.Models;

namespace ModProfileSwitcher
{
    /// <summary>
    /// Shows the list of mods with update status, allowing the user to select which to update.
    /// Similar pattern to ResolvedModsDialog.
    /// </summary>
    public class UpdateModsDialog : Form
    {
        private ListView lvMods;

        public List<UpdateableMod> SelectedUpdates { get; private set; } = new List<UpdateableMod>();

        private readonly List<UpdateableMod> _all;

        public UpdateModsDialog(List<UpdateableMod> mods)
        {
            _all = mods;

            var updatable = mods.Count(m => m.HasUpdate);
            var upToDate = mods.Count(m => !m.HasUpdate && !m.NotFound);
            var notFound = mods.Count(m => m.NotFound);

            Text = "Update Mods";
            Size = new Size(780, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9);

            var summary = $"{mods.Count} mods scanned: ";
            if (updatable > 0) summary += $"⬆ {updatable} update{(updatable == 1 ? "" : "s")} available";
            else summary += "all up to date";
            if (upToDate > 0 && updatable > 0) summary += $", ✓ {upToDate} up to date";
            if (notFound > 0) summary += $", ❓ {notFound} not identified";

            var lbl = new Label { Text = summary, Location = new Point(12, 12), Size = new Size(740, 20) };

            lvMods = new ListView
            {
                Location = new Point(12, 36),
                Size = new Size(740, 370),
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9)
            };

            lvMods.Columns.Add("Mod", 180);
            lvMods.Columns.Add("Current File", 180);
            lvMods.Columns.Add("Latest", 140);
            lvMods.Columns.Add("Status", 220);

            foreach (var m in mods)
            {
                var name = !string.IsNullOrEmpty(m.ProjectTitle) ? m.ProjectTitle : m.CurrentFileName;
                string latestCol;
                Color rowColor;

                if (m.NotFound)
                {
                    latestCol = "—";
                    rowColor = Color.FromArgb(240, 240, 240); // light gray
                }
                else if (m.HasUpdate)
                {
                    latestCol = m.LatestVersion;
                    rowColor = Color.FromArgb(220, 255, 220); // light green
                }
                else
                {
                    latestCol = "—";
                    rowColor = Color.White;
                }

                var item = new ListViewItem(new[]
                {
                    name,
                    m.CurrentFileName,
                    latestCol,
                    m.StatusMessage
                });
                item.Checked = m.Selected;
                item.BackColor = rowColor;
                item.Tag = m;
                lvMods.Items.Add(item);
            }

            // Buttons
            var btnSelectAll = new Button { Text = "Select All Updates", Location = new Point(12, 416), Size = new Size(130, 28) };
            btnSelectAll.Click += (s, e) =>
            {
                foreach (ListViewItem item in lvMods.Items)
                    if (((UpdateableMod)item.Tag).HasUpdate) item.Checked = true;
            };

            var btnSelectNone = new Button { Text = "Select None", Location = new Point(150, 416), Size = new Size(100, 28) };
            btnSelectNone.Click += (s, e) =>
            {
                foreach (ListViewItem item in lvMods.Items) item.Checked = false;
            };

            var btnUpdate = new Button
            {
                Text = $"⬆ Update Selected",
                Location = new Point(530, 446),
                Size = new Size(130, 32),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };
            btnUpdate.Click += (s, e) =>
            {
                SelectedUpdates = new List<UpdateableMod>();
                foreach (ListViewItem item in lvMods.Items)
                {
                    if (item.Checked)
                    {
                        var mod = (UpdateableMod)item.Tag;
                        if (mod.HasUpdate && !string.IsNullOrEmpty(mod.LatestDownloadUrl))
                            SelectedUpdates.Add(mod);
                    }
                }
            };

            var btnCancel = new Button
            {
                Text = "Close",
                Location = new Point(670, 446),
                Size = new Size(80, 32),
                DialogResult = DialogResult.Cancel
            };

            AcceptButton = btnUpdate;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { lbl, lvMods, btnSelectAll, btnSelectNone, btnUpdate, btnCancel });
        }
    }
}

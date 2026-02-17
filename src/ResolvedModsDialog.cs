using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ModProfileSwitcher.Models;

namespace ModProfileSwitcher
{
    /// <summary>
    /// Shows the list of resolved mods with checkboxes so the user can pick which to download.
    /// Version-mismatched and not-found mods are highlighted and unchecked by default.
    /// </summary>
    public class ResolvedModsDialog : Form
    {
        private ListView lvMods;

        public List<ResolvedMod> SelectedMods { get; private set; } = new List<ResolvedMod>();

        private readonly List<ResolvedMod> _all;

        public ResolvedModsDialog(List<ResolvedMod> resolvedMods)
        {
            _all = resolvedMods;

            var compatible = resolvedMods.Count(m => !m.VersionMismatch && !m.NotFound);
            var mismatched = resolvedMods.Count(m => m.VersionMismatch);
            var notFound = resolvedMods.Count(m => m.NotFound);

            Text = "Select Mods to Download";
            Size = new Size(700, 480);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            var summary = $"{resolvedMods.Count} mods resolved: {compatible} compatible";
            if (mismatched > 0) summary += $", ⚠ {mismatched} version mismatch";
            if (notFound > 0) summary += $", ❌ {notFound} not found";

            var lbl = new Label { Text = summary, Location = new Point(12, 12), Size = new Size(660, 20) };

            lvMods = new ListView
            {
                Location = new Point(12, 36),
                Size = new Size(660, 340),
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9)
            };

            lvMods.Columns.Add("Mod", 180);
            lvMods.Columns.Add("File", 200);
            lvMods.Columns.Add("Status", 250);

            foreach (var m in resolvedMods)
            {
                var name = !string.IsNullOrEmpty(m.ProjectTitle) ? m.ProjectTitle : m.ProjectSlug;
                string status;
                Color rowColor;

                if (m.NotFound)
                {
                    status = "❌ Not found / no versions available";
                    rowColor = Color.LightCoral;
                }
                else if (m.VersionMismatch)
                {
                    var versInfo = !string.IsNullOrEmpty(m.ActualGameVersions)
                        ? m.ActualGameVersions : "unknown";
                    status = $"⚠ Mismatch — available for: {versInfo}";
                    rowColor = Color.LightGoldenrodYellow;
                }
                else
                {
                    status = "✓ Compatible";
                    rowColor = Color.White;
                }

                var item = new ListViewItem(new[] { name, m.FileName ?? "(none)", status });
                item.Checked = !m.VersionMismatch && !m.NotFound;
                item.BackColor = rowColor;
                item.Tag = m;
                lvMods.Items.Add(item);
            }

            var btnAll = new Button { Text = "Select All", Location = new Point(12, 386), Size = new Size(90, 28) };
            btnAll.Click += (s, e) => { foreach (ListViewItem item in lvMods.Items) if (!((ResolvedMod)item.Tag).NotFound) item.Checked = true; };

            var btnNone = new Button { Text = "Select None", Location = new Point(110, 386), Size = new Size(90, 28) };
            btnNone.Click += (s, e) => { foreach (ListViewItem item in lvMods.Items) item.Checked = false; };

            var btnCompatible = new Button { Text = "Compatible Only", Location = new Point(210, 386), Size = new Size(120, 28) };
            btnCompatible.Click += (s, e) =>
            {
                foreach (ListViewItem item in lvMods.Items)
                {
                    var mod = (ResolvedMod)item.Tag;
                    item.Checked = !mod.VersionMismatch && !mod.NotFound;
                }
            };

            var btnOk = new Button { Text = "Download", DialogResult = DialogResult.OK, Location = new Point(500, 386), Size = new Size(80, 28) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(590, 386), Size = new Size(80, 28) };
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            btnOk.Click += (s, e) =>
            {
                SelectedMods = new List<ResolvedMod>();
                foreach (ListViewItem item in lvMods.Items)
                {
                    if (item.Checked)
                    {
                        var mod = (ResolvedMod)item.Tag;
                        if (!mod.NotFound && !string.IsNullOrEmpty(mod.DownloadUrl))
                            SelectedMods.Add(mod);
                    }
                }
            };

            Controls.AddRange(new Control[] { lbl, lvMods, btnAll, btnNone, btnCompatible, btnOk, btnCancel });
        }
    }
}

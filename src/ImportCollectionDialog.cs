using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ModProfileSwitcher
{
    /// <summary>
    /// Dialog where the user pastes Modrinth collection text (URLs, slugs, JSON, or a collection page URL).
    /// </summary>
    public class ImportCollectionDialog : Form
    {
        private TextBox txtPaste;
        private ComboBox cboLoader;
        private ComboBox cboMcVersion;

        public string PastedText => txtPaste.Text;
        public string McVersion => cboMcVersion.Text;
        public string Loader => cboLoader.Text;

        public ImportCollectionDialog(string defaultMcVersion, string defaultLoader, List<string> mcVersions = null)
        {
            Text = "Import Modrinth Collection";
            Size = new Size(540, 420);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var lblInfo = new Label
            {
                Text = "Paste a Modrinth collection URL, project URLs (one per line),\nproject slugs, or exported JSON below:",
                Location = new Point(12, 12),
                AutoSize = true
            };

            txtPaste = new TextBox
            {
                Location = new Point(12, 52),
                Size = new Size(500, 240),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9)
            };

            var lblLoader = new Label { Text = "Loader:", Location = new Point(12, 302), AutoSize = true };
            cboLoader = new ComboBox
            {
                Location = new Point(68, 299),
                Size = new Size(110, 23),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboLoader.Items.AddRange(new object[] { "fabric", "forge", "quilt", "neoforge" });
            cboLoader.Text = defaultLoader;
            if (cboLoader.SelectedIndex < 0) cboLoader.SelectedIndex = 0;

            var lblVer = new Label { Text = "MC Version:", Location = new Point(200, 302), AutoSize = true };
            cboMcVersion = new ComboBox
            {
                Location = new Point(280, 299),
                Size = new Size(100, 23),
                DropDownStyle = ComboBoxStyle.DropDown
            };
            if (mcVersions != null && mcVersions.Count > 0)
            {
                foreach (var v in mcVersions)
                    cboMcVersion.Items.Add(v);
            }
            else
            {
                cboMcVersion.Items.Add(defaultMcVersion);
            }
            cboMcVersion.Text = defaultMcVersion;

            var btnOk = new Button
            {
                Text = "Resolve",
                DialogResult = DialogResult.OK,
                Location = new Point(340, 340),
                Size = new Size(80, 30)
            };
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(430, 340),
                Size = new Size(80, 30)
            };

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { lblInfo, txtPaste, lblLoader, cboLoader, lblVer, cboMcVersion, btnOk, btnCancel });
        }
    }
}

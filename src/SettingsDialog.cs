using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using ModProfileSwitcher.Services;

namespace ModProfileSwitcher
{
    /// <summary>
    /// Settings dialog with CurseForge API key management and a built-in guide.
    /// </summary>
    public class SettingsDialog : Form
    {
        private TextBox txtApiKey;
        private Button btnTest;
        private Button btnSave;
        private Button btnCancel;
        private Label lblTestResult;
        private CheckBox chkShowKey;

        public SettingsDialog()
        {
            Text = "Settings";
            Size = new Size(580, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9);

            // ---- CurseForge API Key section ----
            var grpCf = new GroupBox
            {
                Text = "CurseForge API Key",
                Location = new Point(12, 12),
                Size = new Size(540, 140)
            };

            var lblKey = new Label
            {
                Text = "API Key:",
                Location = new Point(12, 28),
                AutoSize = true
            };

            txtApiKey = new TextBox
            {
                Location = new Point(12, 50),
                Size = new Size(440, 23),
                UseSystemPasswordChar = true,
                Text = SettingsManager.CurseForgeApiKey
            };

            chkShowKey = new CheckBox
            {
                Text = "Show",
                Location = new Point(460, 51),
                AutoSize = true
            };
            chkShowKey.CheckedChanged += (s, e) =>
            {
                txtApiKey.UseSystemPasswordChar = !chkShowKey.Checked;
            };

            btnTest = new Button
            {
                Text = "🔑 Test Key",
                Location = new Point(12, 84),
                Size = new Size(100, 30)
            };
            btnTest.Click += BtnTest_Click;

            lblTestResult = new Label
            {
                Text = "",
                Location = new Point(120, 90),
                AutoSize = true
            };

            grpCf.Controls.AddRange(new Control[] { lblKey, txtApiKey, chkShowKey, btnTest, lblTestResult });
            Controls.Add(grpCf);

            // ---- Guide section ----
            var grpGuide = new GroupBox
            {
                Text = "How to Get a CurseForge API Key",
                Location = new Point(12, 160),
                Size = new Size(540, 310)
            };

            var guideText =
                "CurseForge requires a free API key to download mods. Here's how to get one:\n\n" +
                "1. Go to the CurseForge developer console:\n" +
                "   https://console.curseforge.com/\n\n" +
                "2. Sign in with your CurseForge / Overwolf account.\n" +
                "   (Create a free account if you don't have one.)\n\n" +
                "3. Click \"Create API Key\" or go to the API Keys section.\n\n" +
                "4. Fill in the form:\n" +
                "   • Organization Name: anything (e.g., your username)\n" +
                "   • Project Name: \"ModProfileSwitcher\" (or anything)\n" +
                "   • Description: \"Personal mod downloads\"\n\n" +
                "5. Accept the Terms of Service and click Generate.\n\n" +
                "6. Copy the API key (a long string starting with '$2a$10$...')\n" +
                "   and paste it into the field above.\n\n" +
                "The key is stored locally on your machine and never shared.";

            var lblGuide = new Label
            {
                Text = guideText,
                Location = new Point(12, 24),
                Size = new Size(510, 240),
                Font = new Font("Segoe UI", 8.5f)
            };

            var lnkConsole = new LinkLabel
            {
                Text = "🔗 Open CurseForge Developer Console",
                Location = new Point(12, 268),
                AutoSize = true,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            lnkConsole.LinkClicked += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("https://console.curseforge.com/") { UseShellExecute = true }); }
                catch { }
            };

            grpGuide.Controls.AddRange(new Control[] { lblGuide, lnkConsole });
            Controls.Add(grpGuide);

            // ---- Bottom buttons ----
            btnSave = new Button
            {
                Text = "Save",
                Location = new Point(370, 484),
                Size = new Size(85, 32),
                DialogResult = DialogResult.OK
            };
            btnSave.Click += BtnSave_Click;

            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(462, 484),
                Size = new Size(85, 32),
                DialogResult = DialogResult.Cancel
            };

            AcceptButton = btnSave;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { btnSave, btnCancel });
        }

        private async void BtnTest_Click(object sender, EventArgs e)
        {
            var key = txtApiKey.Text.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                lblTestResult.Text = "❌ Enter an API key first.";
                lblTestResult.ForeColor = Color.Red;
                return;
            }

            btnTest.Enabled = false;
            lblTestResult.Text = "Testing…";
            lblTestResult.ForeColor = Color.Gray;

            try
            {
                var svc = new CurseForgeService(key);
                var valid = await svc.TestApiKeyAsync();
                if (valid)
                {
                    lblTestResult.Text = "✅ API key is valid!";
                    lblTestResult.ForeColor = Color.DarkGreen;
                }
                else
                {
                    lblTestResult.Text = "❌ Invalid API key (403 or error).";
                    lblTestResult.ForeColor = Color.Red;
                }
            }
            catch (Exception ex)
            {
                lblTestResult.Text = $"❌ Error: {ex.Message}";
                lblTestResult.ForeColor = Color.Red;
            }
            finally
            {
                btnTest.Enabled = true;
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            SettingsManager.CurseForgeApiKey = txtApiKey.Text.Trim();
        }
    }
}

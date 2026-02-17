using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

#nullable enable

namespace ModProfileSwitcherInstaller
{
    public class InstallerForm : Form
    {
        private readonly TextBox _txtPath;
        private readonly CheckBox _chkDesktop;
        private readonly CheckBox _chkLaunch;
        private readonly Button _btnBrowse;
        private readonly Button _btnInstall;
        private readonly Button _btnCancel;
        private readonly ProgressBar _progressBar;
        private readonly Label _lblStatus;
        private readonly Label _lblTitle;
        private readonly Label _lblPath;

        public InstallerForm()
        {
            Text = $"Install {InstallerEngine.AppName}";
            Size = new Size(520, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // Title
            _lblTitle = new Label
            {
                Text = $"Welcome to {InstallerEngine.AppName} Setup",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(480, 40),
                Location = new Point(16, 12),
                ForeColor = Color.FromArgb(30, 80, 160)
            };
            Controls.Add(_lblTitle);

            var lblVersion = new Label
            {
                Text = $"Version {InstallerEngine.AppVersion}",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.Gray,
                AutoSize = true,
                Location = new Point(18, 50)
            };
            Controls.Add(lblVersion);

            // Install path
            _lblPath = new Label
            {
                Text = "Install location:",
                Location = new Point(16, 85),
                AutoSize = true,
                Font = new Font("Segoe UI", 9)
            };
            Controls.Add(_lblPath);

            _txtPath = new TextBox
            {
                Text = InstallerEngine.DefaultInstallDir,
                Location = new Point(16, 108),
                Size = new Size(390, 24),
                Font = new Font("Segoe UI", 9)
            };
            Controls.Add(_txtPath);

            _btnBrowse = new Button
            {
                Text = "Browse…",
                Location = new Point(414, 107),
                Size = new Size(75, 26)
            };
            _btnBrowse.Click += (s, e) =>
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description = "Choose install location",
                    SelectedPath = _txtPath.Text
                };
                if (dlg.ShowDialog() == DialogResult.OK)
                    _txtPath.Text = dlg.SelectedPath;
            };
            Controls.Add(_btnBrowse);

            // Options
            _chkDesktop = new CheckBox
            {
                Text = "Create Desktop shortcut",
                Location = new Point(18, 148),
                AutoSize = true,
                Checked = true,
                Font = new Font("Segoe UI", 9)
            };
            Controls.Add(_chkDesktop);

            _chkLaunch = new CheckBox
            {
                Text = "Launch after install",
                Location = new Point(18, 174),
                AutoSize = true,
                Checked = true,
                Font = new Font("Segoe UI", 9)
            };
            Controls.Add(_chkLaunch);

            // Progress
            _progressBar = new ProgressBar
            {
                Location = new Point(16, 215),
                Size = new Size(473, 22),
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };
            Controls.Add(_progressBar);

            _lblStatus = new Label
            {
                Text = "",
                Location = new Point(16, 242),
                AutoSize = true,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.DimGray
            };
            Controls.Add(_lblStatus);

            // Buttons
            _btnInstall = new Button
            {
                Text = "Install",
                Size = new Size(100, 34),
                Location = new Point(280, 275),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 120, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnInstall.Click += BtnInstall_Click;
            Controls.Add(_btnInstall);

            _btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(100, 34),
                Location = new Point(390, 275),
                Font = new Font("Segoe UI", 10)
            };
            _btnCancel.Click += (s, e) => Close();
            Controls.Add(_btnCancel);
        }

        private async void BtnInstall_Click(object? sender, EventArgs e)
        {
            _btnInstall.Enabled = false;
            _btnBrowse.Enabled = false;
            _txtPath.ReadOnly = true;
            _progressBar.Visible = true;
            _lblStatus.Text = "Installing…";

            try
            {
                var engine = new InstallerEngine();
                var installDir = _txtPath.Text.Trim();

                // Run on a background thread to keep UI responsive
                await System.Threading.Tasks.Task.Run(() =>
                    engine.Install(installDir, _chkDesktop.Checked, _chkLaunch.Checked));

                _progressBar.Visible = false;
                _lblStatus.ForeColor = Color.DarkGreen;
                _lblStatus.Text = "✅ Installation complete!";

                MessageBox.Show(
                    $"{InstallerEngine.AppName} has been installed to:\n{installDir}\n\n" +
                    "You can uninstall it from Add/Remove Programs.",
                    "Installation Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                Close();
            }
            catch (Exception ex)
            {
                _progressBar.Visible = false;
                _lblStatus.ForeColor = Color.Red;
                _lblStatus.Text = "❌ Installation failed.";

                MessageBox.Show(
                    "Installation failed:\n\n" + ex.Message,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                _btnInstall.Enabled = true;
                _btnBrowse.Enabled = true;
                _txtPath.ReadOnly = false;
            }
        }
    }
}

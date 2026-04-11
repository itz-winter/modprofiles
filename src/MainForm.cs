using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ModProfileSwitcher.Models;
using ModProfileSwitcher.Services;

namespace ModProfileSwitcher
{
    public class MainForm : Form
    {
        // --- Services ---
        private ProfilesManager _profiles;
        private ProfileSwitcher _switcher;
        private readonly ModrinthCollectionResolver _resolver = new ModrinthCollectionResolver();
        private CurseForgeService _curseForge; // initialized when API key is available

        // --- Paths (defaults, configurable via Settings) ---
        private string _minecraftModsPath;
        private string _profilesRoot;
        private string _backupsRoot;

        // --- UI Controls ---
        private ListBox lstProfiles;
        private ListBox lstMods;
        private Button btnNewProfile, btnDeleteProfile, btnRenameProfile, btnDuplicateProfile;
        private Button btnApplyProfile, btnBackupRestore;
        private Button btnAddMod, btnRemoveMod, btnOpenModsFolder;
        private Button btnDownloadMod, btnImportCollection;
        private TextBox txtDownloadUrl;
        private ComboBox cboLoader, cboMcVersion, cboSource;
        private Button btnSettings;
        private ProgressBar progressBar;
        private Label lblStatus;
        private TextBox txtLog;
        private TreeView treeActiveMods;
        private Button btnRefreshActive, btnOpenMinecraftMods, btnImportActiveProfile, btnUpdateMods, btnMigrateMods;

        public MainForm()
        {
            InitPaths();
            InitServices();
            InitUI();
            RefreshProfiles();
            RefreshActiveMods();

            // After the form is shown: load versions + check for mod loaders
            Shown += async (s, e) =>
            {
                await LoadVersionsAsync();
                CheckModLoaders();
            };
        }

        // ===================== Initialization =====================

        private string _minecraftDir; // the .minecraft root

        private void InitPaths()
        {
            // Auto-detect the .minecraft directory
            _minecraftDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft");

            _minecraftModsPath = Path.Combine(_minecraftDir, "mods");

            // Auto-create the mods folder if it doesn't exist
            if (!Directory.Exists(_minecraftModsPath))
            {
                Directory.CreateDirectory(_minecraftModsPath);
            }

            _profilesRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MinecraftModProfiles", "Profiles");
            _backupsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MinecraftModProfiles", "Backups");
        }

        private void InitServices()
        {
            _profiles = new ProfilesManager(_profilesRoot);
            _switcher = new ProfileSwitcher(_profiles, _minecraftModsPath, _backupsRoot);

            // Initialize CurseForge service if API key is available
            if (SettingsManager.HasCurseForgeApiKey)
                _curseForge = new CurseForgeService(SettingsManager.CurseForgeApiKey);

            // Conflict resolver: prompt user when a resource/shader pack already exists
            _switcher.ConflictResolver = (fileName, packType) =>
            {
                var result = MessageBox.Show(
                    $"A {packType} named '{fileName}' already exists in your .minecraft/{(packType == "resource pack" ? "resourcepacks" : "shaderpacks")} folder.\n\nOverwrite it?",
                    $"{packType} Conflict",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                return result == DialogResult.Yes;
            };
        }

        private void InitUI()
        {
            Text = "Minecraft Mod Profile Switcher";
            Size = new Size(960, 820);
            MinimumSize = new Size(800, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9);

            // ---- Left panel: Profiles ----
            var grpProfiles = new GroupBox
            {
                Text = "Profiles",
                Location = new Point(12, 12),
                Size = new Size(220, 280),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            lstProfiles = new ListBox { Location = new Point(8, 24), Size = new Size(204, 180), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom };
            lstProfiles.SelectedIndexChanged += (s, e) => RefreshMods();

            btnNewProfile = new Button { Text = "New", Location = new Point(8, 210), Size = new Size(48, 26) };
            btnNewProfile.Click += BtnNewProfile_Click;
            btnRenameProfile = new Button { Text = "Rename", Location = new Point(58, 210), Size = new Size(48, 26) };
            btnRenameProfile.Click += BtnRenameProfile_Click;
            btnDuplicateProfile = new Button { Text = "Copy", Location = new Point(108, 210), Size = new Size(48, 26) };
            btnDuplicateProfile.Click += BtnDuplicateProfile_Click;
            btnDeleteProfile = new Button { Text = "Delete", Location = new Point(158, 210), Size = new Size(48, 26) };
            btnDeleteProfile.Click += BtnDeleteProfile_Click;

            btnApplyProfile = new Button { Text = "▶ Switch To", Location = new Point(8, 242), Size = new Size(130, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnApplyProfile.Click += BtnApplyProfile_Click;
            btnBackupRestore = new Button { Text = "⏸ Deactivate", Location = new Point(142, 242), Size = new Size(70, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnBackupRestore.Click += BtnRestore_Click;

            grpProfiles.Controls.AddRange(new Control[] { lstProfiles, btnNewProfile, btnRenameProfile, btnDuplicateProfile, btnDeleteProfile, btnApplyProfile, btnBackupRestore });
            Controls.Add(grpProfiles);

            // ---- Center panel: Mods in profile ----
            var grpMods = new GroupBox
            {
                Text = "Mods in Profile",
                Location = new Point(240, 12),
                Size = new Size(320, 280),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            lstMods = new ListBox { Location = new Point(8, 24), Size = new Size(304, 208), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right };

            btnAddMod = MakeButton("Add .jar…", new Point(8, 240), grpMods);
            btnAddMod.Click += BtnAddMod_Click;
            btnRemoveMod = MakeButton("Remove", new Point(88, 240), grpMods);
            btnRemoveMod.Click += BtnRemoveMod_Click;
            btnOpenModsFolder = MakeButton("Open Folder", new Point(168, 240), grpMods);
            btnOpenModsFolder.Click += BtnOpenModsFolder_Click;

            grpMods.Controls.AddRange(new Control[] { lstMods, btnAddMod, btnRemoveMod, btnOpenModsFolder });
            Controls.Add(grpMods);

            // ---- Right panel: Download / Import ----
            var grpDownload = new GroupBox
            {
                Text = "Download / Import",
                Location = new Point(568, 12),
                Size = new Size(370, 300),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            var lblUrl = new Label { Text = "Mod URL:", Location = new Point(8, 28), AutoSize = true };
            txtDownloadUrl = new TextBox { Location = new Point(8, 48), Size = new Size(350, 23), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            var lblLoader = new Label { Text = "Loader:", Location = new Point(8, 80), AutoSize = true };
            cboLoader = new ComboBox { Location = new Point(60, 77), Size = new Size(100, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboLoader.Items.AddRange(new object[] { "fabric", "forge", "quilt", "neoforge" });
            cboLoader.SelectedIndex = 0;

            var lblVer = new Label { Text = "MC Ver:", Location = new Point(170, 80), AutoSize = true };
            cboMcVersion = new ComboBox { Location = new Point(222, 77), Size = new Size(90, 23), DropDownStyle = ComboBoxStyle.DropDown };
            // Versions are loaded asynchronously from the Modrinth API after form is shown
            cboMcVersion.Items.Add("Loading…");
            cboMcVersion.SelectedIndex = 0;

            var lblSource = new Label { Text = "Source:", Location = new Point(8, 114), AutoSize = true };
            cboSource = new ComboBox { Location = new Point(60, 111), Size = new Size(120, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboSource.Items.AddRange(new object[] { "Modrinth", "CurseForge" });
            cboSource.SelectedIndex = 0;

            btnSettings = new Button { Text = "⚙ Settings", Location = new Point(190, 110), Size = new Size(120, 26) };
            btnSettings.Click += BtnSettings_Click;

            btnDownloadMod = new Button { Text = "⬇ Download Mod", Location = new Point(8, 146), Size = new Size(150, 30) };
            btnDownloadMod.Click += BtnDownloadMod_Click;

            btnImportCollection = new Button { Text = "📋 Paste Collection…", Location = new Point(165, 146), Size = new Size(195, 30) };
            btnImportCollection.Click += BtnImportCollection_Click;

            grpDownload.Controls.AddRange(new Control[] { lblUrl, txtDownloadUrl, lblLoader, cboLoader, lblVer, cboMcVersion, lblSource, cboSource, btnSettings, btnDownloadMod, btnImportCollection });
            Controls.Add(grpDownload);

            // ---- Middle panel: Active Mods in .minecraft/mods ----
            var grpActive = new GroupBox
            {
                Text = "Mods Directory (.minecraft/mods)",
                Location = new Point(12, 300),
                Size = new Size(926, 200),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };

            treeActiveMods = new TreeView
            {
                Location = new Point(8, 24),
                Size = new Size(820, 164),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Font = new Font("Consolas", 9),
                ShowRootLines = true,
                ShowPlusMinus = true,
                FullRowSelect = true
            };

            btnRefreshActive = new Button
            {
                Text = "🔄",
                Location = new Point(836, 24),
                Size = new Size(82, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnRefreshActive.Click += (s, e) => RefreshActiveMods();

            btnOpenMinecraftMods = new Button
            {
                Text = "📂 Open",
                Location = new Point(836, 58),
                Size = new Size(82, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnOpenMinecraftMods.Click += (s, e) =>
            {
                if (Directory.Exists(_minecraftModsPath))
                    Process.Start("explorer.exe", _minecraftModsPath);
            };

            btnImportActiveProfile = new Button
            {
                Text = "📝 Name",
                Location = new Point(836, 92),
                Size = new Size(82, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Enabled = true
            };
            btnImportActiveProfile.Click += BtnNameActiveProfile_Click;

            btnUpdateMods = new Button
            {
                Text = "⬆ Update",
                Location = new Point(836, 126),
                Size = new Size(82, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnUpdateMods.Click += BtnUpdateMods_Click;

            btnMigrateMods = new Button
            {
                Text = "🔄 Migrate",
                Location = new Point(836, 160),
                Size = new Size(82, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnMigrateMods.Click += BtnMigrateMods_Click;

            grpActive.Controls.AddRange(new Control[] { treeActiveMods, btnRefreshActive, btnOpenMinecraftMods, btnImportActiveProfile, btnUpdateMods, btnMigrateMods });
            Controls.Add(grpActive);

            // ---- Bottom: progress + log ----
            progressBar = new ProgressBar
            {
                Location = new Point(12, 508),
                Size = new Size(926, 22),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Style = ProgressBarStyle.Continuous
            };
            Controls.Add(progressBar);

            lblStatus = new Label
            {
                Text = "Ready.",
                Location = new Point(12, 534),
                Size = new Size(926, 18),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            Controls.Add(lblStatus);

            txtLog = new TextBox
            {
                Location = new Point(12, 556),
                Size = new Size(926, 210),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 8.5f)
            };
            Controls.Add(txtLog);
        }

        private Button MakeButton(string text, Point location, Control parent)
        {
            var b = new Button { Text = text, Location = location, Size = new Size(66, 26) };
            return b;
        }

        // ===================== Refresh helpers =====================

        private const string ACTIVE_TAG = " ▶ ACTIVE";

        private void RefreshProfiles()
        {
            var sel = lstProfiles.SelectedItem as string;
            lstProfiles.Items.Clear();

            var active = _switcher.ActiveProfileName;

            // Show the active profile first (if any loose jars exist or marker is set)
            if (!string.IsNullOrEmpty(active))
            {
                lstProfiles.Items.Add(active + ACTIVE_TAG);
            }

            // Show all inactive profile subfolders
            foreach (var p in _switcher.ListInactiveProfiles())
            {
                lstProfiles.Items.Add(p);
            }

            // Restore selection
            if (sel != null)
            {
                var cleanSel = sel.Replace(ACTIVE_TAG, "");
                for (int i = 0; i < lstProfiles.Items.Count; i++)
                {
                    var item = lstProfiles.Items[i].ToString().Replace(ACTIVE_TAG, "");
                    if (item == cleanSel) { lstProfiles.SelectedIndex = i; break; }
                }
            }
            if (lstProfiles.SelectedIndex < 0 && lstProfiles.Items.Count > 0)
                lstProfiles.SelectedIndex = 0;

            RefreshMods();
        }

        /// <summary>
        /// Gets the real profile name from the selected list item (strips the ACTIVE tag).
        /// </summary>
        private string SelectedProfileClean
        {
            get
            {
                var sel = lstProfiles.SelectedItem as string;
                return sel?.Replace(ACTIVE_TAG, "");
            }
        }

        /// <summary>
        /// Whether the currently selected profile in the list is the active one.
        /// </summary>
        private bool IsSelectedActive
        {
            get
            {
                var sel = lstProfiles.SelectedItem as string;
                return sel != null && sel.EndsWith(ACTIVE_TAG);
            }
        }

        private void RefreshMods()
        {
            lstMods.Items.Clear();
            var profile = SelectedProfileClean;
            if (profile == null) return;

            List<string> jars;
            List<(string PackType, string FileName)> packs;

            if (IsSelectedActive)
            {
                jars = _switcher.ListActiveJars();
                packs = _switcher.ListActiveProfilePacks();
            }
            else
            {
                jars = _switcher.ListProfileJars(profile);
                packs = _switcher.ListProfilePacks(profile);
            }

            foreach (var m in jars)
                lstMods.Items.Add($"📦 {m}");

            foreach (var (packType, fileName) in packs)
            {
                var icon = packType == "resourcepacks" ? "🎨" : "🌅";
                lstMods.Items.Add($"{icon} {fileName}  [{packType}]");
            }
        }

        /// <summary>
        /// Scans .minecraft/mods and populates the TreeView.
        /// Active profile = loose jars at root. Inactive profiles = subfolders.
        /// </summary>
        private void RefreshActiveMods()
        {
            treeActiveMods.Nodes.Clear();

            if (!Directory.Exists(_minecraftModsPath))
            {
                treeActiveMods.Nodes.Add("(mods folder not found)");
                return;
            }

            var activeProfile = _switcher.ActiveProfileName;

            // --- Active profile (loose jars at root) ---
            var looseFiles = Directory.GetFiles(_minecraftModsPath)
                .Where(f => !Path.GetFileName(f).StartsWith(".")) // skip .active_profile marker
                .OrderBy(f => f).ToArray();
            var looseJars = looseFiles.Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).ToArray();

            var activeLabel = string.IsNullOrEmpty(activeProfile)
                ? $"▶ Active  ({looseJars.Length} mod{(looseJars.Length == 1 ? "" : "s")})"
                : $"▶ {activeProfile}  ({looseJars.Length} mod{(looseJars.Length == 1 ? "" : "s")})  — ACTIVE";
            var activeNode = new TreeNode(activeLabel);
            activeNode.NodeFont = new Font(treeActiveMods.Font, FontStyle.Bold);
            activeNode.ForeColor = Color.DarkGreen;
            foreach (var file in looseFiles)
            {
                var fileName = Path.GetFileName(file);
                var sizeStr = FormatFileSize(file);
                var icon = fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ? "📦" : "📄";
                activeNode.Nodes.Add(new TreeNode($"{icon} {fileName}  ({sizeStr})") { Tag = file });
            }

            // Show active profile's deployed resource/shader packs
            if (!string.IsNullOrEmpty(activeProfile))
            {
                var activePacks = _switcher.ListActiveProfilePacks();
                foreach (var (packType, fileName) in activePacks)
                {
                    var icon = packType == "resourcepacks" ? "🎨" : "🌅";
                    activeNode.Nodes.Add(new TreeNode($"{icon} {fileName}  [{packType}]"));
                }
            }

            treeActiveMods.Nodes.Add(activeNode);

            // --- Inactive profile subfolders ---
            var dirs = Directory.GetDirectories(_minecraftModsPath)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .OrderBy(d => d).ToArray();

            foreach (var dir in dirs)
            {
                var folderName = Path.GetFileName(dir);
                var jarCount = Directory.GetFiles(dir, "*.jar").Length;
                var packs = _switcher.ListProfilePacks(folderName);
                var packInfo = packs.Count > 0 ? $", {packs.Count} pack{(packs.Count == 1 ? "" : "s")}" : "";
                var profileNode = new TreeNode($"⏸ {folderName}  ({jarCount} mod{(jarCount == 1 ? "" : "s")}{packInfo})");
                profileNode.Tag = dir;
                profileNode.NodeFont = new Font(treeActiveMods.Font, FontStyle.Regular);
                profileNode.ForeColor = Color.Gray;

                PopulateTreeChildren(profileNode.Nodes, dir);
                treeActiveMods.Nodes.Add(profileNode);
            }

            if (treeActiveMods.Nodes.Count == 0)
                treeActiveMods.Nodes.Add("(empty — no mods or profiles found)");

            treeActiveMods.ExpandAll();
            Log($"Mods dir: {looseJars.Length} active jar(s), {dirs.Length} inactive profile(s).");
        }

        /// <summary>
        /// Recursively adds files and subfolders inside a profile folder.
        /// </summary>
        private void PopulateTreeChildren(TreeNodeCollection parentNodes, string directory)
        {
            foreach (var dir in Directory.GetDirectories(directory).OrderBy(d => d))
            {
                var folderName = Path.GetFileName(dir);
                var fileCount = CountFiles(dir, "*.*");
                var folderNode = new TreeNode($"📁 {folderName}  ({fileCount} file{(fileCount == 1 ? "" : "s")})");
                folderNode.Tag = dir;
                PopulateTreeChildren(folderNode.Nodes, dir);
                parentNodes.Add(folderNode);
            }

            foreach (var file in Directory.GetFiles(directory).OrderBy(f => f))
            {
                var fileName = Path.GetFileName(file);
                var sizeStr = FormatFileSize(file);
                var icon = fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ? "📦" : "📄";
                var node = new TreeNode($"{icon} {fileName}  ({sizeStr})");
                node.Tag = file;
                parentNodes.Add(node);
            }
        }

        private static string FormatFileSize(string filePath)
        {
            var size = new FileInfo(filePath).Length;
            return size < 1024 * 1024
                ? $"{size / 1024.0:F0} KB"
                : $"{size / (1024.0 * 1024.0):F1} MB";
        }

        /// <summary>
        /// Lets the user name (or rename) the currently active set of loose jars.
        /// This updates the .active_profile marker so they'll be saved properly on switch.
        /// </summary>
        private void BtnNameActiveProfile_Click(object sender, EventArgs e)
        {
            var looseJarCount = _switcher.ActiveJarCount();
            if (looseJarCount == 0)
            {
                MessageBox.Show("No active mods in the mods folder root.", "Name Profile");
                return;
            }

            var current = _switcher.ActiveProfileName ?? "";
            var name = Prompt("Name Active Profile",
                $"Give a name to the {looseJarCount} active mod(s):", current);
            if (string.IsNullOrWhiteSpace(name)) return;

            _switcher.SetActiveProfileName(name);
            Log($"Active profile named '{name}'.");
            RefreshProfiles();
            RefreshActiveMods();
        }

        private static int CountFiles(string dir, string pattern)
        {
            try { return Directory.GetFiles(dir, pattern, SearchOption.AllDirectories).Length; }
            catch { return 0; }
        }

        private static int CountFolders(string dir)
        {
            try { return Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).Length; }
            catch { return 0; }
        }

        /// <summary>
        /// Fetches all Minecraft release versions from the Modrinth API and populates the combo box.
        /// </summary>
        private async Task LoadVersionsAsync()
        {
            try
            {
                Log("Fetching Minecraft versions from Modrinth…");
                var versions = await MinecraftVersionService.GetVersionsAsync();
                cboMcVersion.Items.Clear();
                foreach (var v in versions)
                    cboMcVersion.Items.Add(v);
                if (cboMcVersion.Items.Count > 0)
                    cboMcVersion.SelectedIndex = 0;
                Log($"Loaded {versions.Count} Minecraft versions.");
            }
            catch (Exception ex)
            {
                Log($"Failed to load versions: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the currently loaded version list for use by dialogs.
        /// </summary>
        internal List<string> GetLoadedVersions()
        {
            var list = new List<string>();
            foreach (var item in cboMcVersion.Items)
                list.Add(item.ToString());
            return list;
        }

        /// <summary>
        /// Checks for installed mod loaders. If none are found, shows a warning with install links.
        /// Also auto-selects detected loaders in the Loader dropdown.
        /// </summary>
        private void CheckModLoaders()
        {
            Log("Detecting mod loaders…");
            var detected = ModLoaderDetector.Detect(_minecraftDir);
            var installed = detected.Where(l => l.Installed).ToList();

            foreach (var l in detected)
            {
                if (l.Installed)
                    Log($"  ✓ {l.Name} detected.");
            }

            if (installed.Count > 0)
            {
                // Auto-select the first detected loader in the dropdown
                var firstName = installed[0].Name.ToLowerInvariant();
                for (int i = 0; i < cboLoader.Items.Count; i++)
                {
                    if (cboLoader.Items[i].ToString().Equals(firstName, StringComparison.OrdinalIgnoreCase))
                    {
                        cboLoader.SelectedIndex = i;
                        break;
                    }
                }
                Log($"{installed.Count} mod loader(s) found.");
            }
            else
            {
                Log("⚠ No mod loaders detected!");
                ShowNoLoaderWarning(detected);
            }
        }

        /// <summary>
        /// Shows a warning dialog with links to install mod loaders.
        /// </summary>
        private void ShowNoLoaderWarning(List<ModLoaderDetector.LoaderInfo> loaders)
        {
            var form = new Form
            {
                Text = "No Mod Loader Detected",
                Size = new Size(480, 320),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lbl = new Label
            {
                Text = "⚠ No mod loader was found in your .minecraft folder.\n\n" +
                       "You need to install a mod loader before you can use mods.\n" +
                       "Click any link below to visit the installer page:",
                Location = new Point(16, 16),
                Size = new Size(440, 80)
            };
            form.Controls.Add(lbl);

            int y = 100;
            foreach (var loader in loaders)
            {
                var link = new LinkLabel
                {
                    Text = $"Install {loader.Name}  →  {loader.InstallerUrl}",
                    Tag = loader.InstallerUrl,
                    Location = new Point(16, y),
                    Size = new Size(440, 22),
                    AutoSize = false
                };
                link.LinkClicked += (s, e) =>
                {
                    var url = ((LinkLabel)s).Tag.ToString();
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                    catch { }
                };
                form.Controls.Add(link);
                y += 30;
            }

            var btnOk = new Button
            {
                Text = "OK, I'll install one",
                DialogResult = DialogResult.OK,
                Location = new Point(160, y + 10),
                Size = new Size(160, 32)
            };
            form.AcceptButton = btnOk;
            form.Controls.Add(btnOk);

            form.ShowDialog(this);
        }

        private void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n";
            if (InvokeRequired) { Invoke(new Action(() => { txtLog.AppendText(line); lblStatus.Text = msg; })); }
            else { txtLog.AppendText(line); lblStatus.Text = msg; }
        }

        private string SelectedProfile => SelectedProfileClean;

        // ===================== Profile CRUD =====================

        private void BtnNewProfile_Click(object sender, EventArgs e)
        {
            var name = Prompt("New Profile", "Profile name (subfolder in .minecraft/mods):");
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                _switcher.CreateProfileFolder(name);
                Log($"Created profile folder '{name}'.");
                RefreshProfiles();
                RefreshActiveMods();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void BtnRenameProfile_Click(object sender, EventArgs e)
        {
            var current = SelectedProfileClean;
            if (current == null) return;

            // If renaming the active profile, just update the marker
            if (IsSelectedActive)
            {
                var newName = Prompt("Rename Active Profile", "New name for the active profile:", current);
                if (string.IsNullOrWhiteSpace(newName) || newName == current) return;
                // We can't "rename" a set of loose files, but we can update what name they'll be saved under
                // Deactivate to subfolder with new name, then reactivate
                try
                {
                    _switcher.Deactivate(newName); // stash as newName
                    _switcher.SwitchTo(newName);    // reactivate with new name
                    Log($"Renamed active profile → '{newName}'.");
                    RefreshProfiles();
                    RefreshActiveMods();
                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
                return;
            }

            var rename = Prompt("Rename Profile", "New name:", current);
            if (string.IsNullOrWhiteSpace(rename) || rename == current) return;
            try
            {
                _switcher.RenameProfileFolder(current, rename);
                Log($"Renamed '{current}' → '{rename}'.");
                RefreshProfiles();
                RefreshActiveMods();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void BtnDeleteProfile_Click(object sender, EventArgs e)
        {
            var profile = SelectedProfileClean;
            if (profile == null) return;

            if (IsSelectedActive)
            {
                MessageBox.Show("Cannot delete the active profile. Switch to another profile first.", "Delete");
                return;
            }

            var jars = _switcher.ListProfileJars(profile);
            if (MessageBox.Show($"Delete profile '{profile}' and its {jars.Count} mod(s)?\nThis cannot be undone.", "Confirm Delete", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            try
            {
                _switcher.DeleteProfileFolder(profile);
                Log($"Deleted profile '{profile}'.");
                RefreshProfiles();
                RefreshActiveMods();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void BtnDuplicateProfile_Click(object sender, EventArgs e)
        {
            var profile = SelectedProfileClean;
            if (profile == null) { MessageBox.Show("Select a profile to duplicate."); return; }

            var newName = Prompt("Duplicate Profile", $"Name for the copy of '{profile}':", profile + " - Copy");
            if (string.IsNullOrWhiteSpace(newName)) return;

            try
            {
                _switcher.DuplicateProfile(profile, newName);
                var jarCount = _switcher.ListProfileJars(newName).Count;
                Log($"Duplicated '{profile}' → '{newName}' ({jarCount} mod(s) copied).");
                RefreshProfiles();
                RefreshActiveMods();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // ===================== Switch / Deactivate =====================

        private void BtnApplyProfile_Click(object sender, EventArgs e)
        {
            var target = SelectedProfileClean;
            if (target == null) { MessageBox.Show("Select a profile first."); return; }

            if (IsSelectedActive)
            {
                MessageBox.Show("This profile is already active!", "Switch");
                return;
            }

            try
            {
                // If there are loose jars but no active profile name, ask user to name them
                string currentName = null;
                if (_switcher.ActiveJarCount() > 0 && string.IsNullOrEmpty(_switcher.ActiveProfileName))
                {
                    currentName = Prompt("Name Current Mods",
                        "There are loose mods in your mods folder.\nGive them a profile name so they can be saved:",
                        "unnamed");
                    if (string.IsNullOrWhiteSpace(currentName)) return;
                }

                _switcher.SwitchTo(target, currentName);
                Log($"Switched to profile '{target}'.");
                RefreshProfiles();
                RefreshActiveMods();
                MessageBox.Show($"Profile '{target}' is now active!", "Switched");
            }
            catch (Exception ex)
            {
                Log($"Error switching: {ex.Message}");
                MessageBox.Show("Error: " + ex.Message);
            }
        }

        private void BtnRestore_Click(object sender, EventArgs e)
        {
            // Deactivate: stash all loose jars back into the active profile subfolder
            var active = _switcher.ActiveProfileName;
            if (string.IsNullOrEmpty(active) && _switcher.ActiveJarCount() == 0)
            {
                MessageBox.Show("No active mods to deactivate."); return;
            }

            if (string.IsNullOrEmpty(active))
            {
                active = Prompt("Deactivate Mods",
                    "Name for the current set of mods (they'll be saved in a subfolder):", "unnamed");
                if (string.IsNullOrWhiteSpace(active)) return;
            }

            if (MessageBox.Show($"Deactivate all {_switcher.ActiveJarCount()} active mods?\nThey will be saved as profile '{active}'.",
                "Deactivate", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            try
            {
                _switcher.Deactivate(active);
                Log($"Deactivated. Mods saved as '{active}'.");
                RefreshProfiles();
                RefreshActiveMods();
                MessageBox.Show("All mods deactivated. Mods folder is now clean.");
            }
            catch (Exception ex) { MessageBox.Show("Error: " + ex.Message); }
        }

        // ===================== Mods / Packs =====================

        private void BtnAddMod_Click(object sender, EventArgs e)
        {
            var profile = SelectedProfileClean;
            if (profile == null) { MessageBox.Show("Select a profile first."); return; }
            using var dlg = new OpenFileDialog
            {
                Filter = "Mods (*.jar)|*.jar|Resource/Shader Packs (*.zip)|*.zip|All files (*.*)|*.*",
                Multiselect = true,
                Title = "Add mods or packs"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            var profileDir = IsSelectedActive ? null : Path.Combine(_minecraftModsPath, profile);

            foreach (var f in dlg.FileNames)
            {
                var fileName = Path.GetFileName(f);
                var ext = Path.GetExtension(f).ToLowerInvariant();

                if (ext == ".zip")
                {
                    // Ask user: resource pack or shader pack?
                    var choice = PromptPackType(fileName);
                    if (choice == null) continue; // cancelled

                    string destDir;
                    if (IsSelectedActive)
                    {
                        // Active profile: place directly into .minecraft/resourcepacks or /shaderpacks
                        destDir = Path.Combine(_minecraftDir, choice);
                    }
                    else
                    {
                        // Inactive profile: place into profile subfolder's resourcepacks/ or shaderpacks/
                        destDir = Path.Combine(profileDir, choice);
                    }
                    Directory.CreateDirectory(destDir);
                    var dest = Path.Combine(destDir, fileName);
                    if (File.Exists(dest))
                    {
                        var overwrite = MessageBox.Show($"'{fileName}' already exists. Overwrite?",
                            "Conflict", MessageBoxButtons.YesNo);
                        if (overwrite != DialogResult.Yes) continue;
                    }
                    File.Copy(f, dest, overwrite: true);
                    Log($"Added {choice}: {fileName}");
                }
                else
                {
                    // .jar → mods
                    var destDir2 = IsSelectedActive ? _minecraftModsPath : profileDir;
                    var dest = Path.Combine(destDir2, fileName);
                    File.Copy(f, dest, overwrite: true);
                    Log($"Added mod: {fileName}");
                }
            }
            RefreshMods();
            RefreshActiveMods();
        }

        /// <summary>
        /// Asks the user whether a .zip file is a resource pack or shader pack.
        /// Returns "resourcepacks" or "shaderpacks", or null if cancelled.
        /// </summary>
        private static string PromptPackType(string fileName)
        {
            var form = new Form
            {
                Text = "Pack Type",
                Size = new Size(400, 160),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            var lbl = new Label
            {
                Text = $"What type of pack is '{fileName}'?",
                Location = new Point(12, 16),
                Size = new Size(370, 30)
            };
            var btnRes = new Button { Text = "🎨 Resource Pack", Location = new Point(30, 60), Size = new Size(150, 35), DialogResult = DialogResult.Yes };
            var btnShader = new Button { Text = "🌅 Shader Pack", Location = new Point(200, 60), Size = new Size(150, 35), DialogResult = DialogResult.No };
            form.Controls.AddRange(new Control[] { lbl, btnRes, btnShader });

            var result = form.ShowDialog();
            if (result == DialogResult.Yes) return "resourcepacks";
            if (result == DialogResult.No) return "shaderpacks";
            return null;
        }

        private void BtnRemoveMod_Click(object sender, EventArgs e)
        {
            var profile = SelectedProfileClean;
            if (profile == null || lstMods.SelectedItem == null) return;
            var display = lstMods.SelectedItem.ToString();

            // Parse display format: "📦 filename.jar" or "🎨 filename.zip  [resourcepacks]"
            if (display.Contains("[resourcepacks]") || display.Contains("[shaderpacks]"))
            {
                // It's a resource/shader pack
                var packType = display.Contains("[resourcepacks]") ? "resourcepacks" : "shaderpacks";
                // Extract filename: skip emoji+space, strip "  [packType]"
                var fileName = display.Substring(2).Replace($"  [{packType}]", "").Trim();

                string filePath;
                if (IsSelectedActive)
                {
                    // Active packs are in .minecraft/resourcepacks or /shaderpacks
                    filePath = Path.Combine(_minecraftDir, packType, fileName);
                }
                else
                {
                    filePath = Path.Combine(_minecraftModsPath, profile, packType, fileName);
                }

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Log($"Removed {packType}: {fileName}");
                }
            }
            else
            {
                // It's a .jar mod — strip the emoji prefix "📦 "
                var jar = display.Length > 2 ? display.Substring(2).Trim() : display;
                var sourceDir = IsSelectedActive
                    ? _minecraftModsPath
                    : Path.Combine(_minecraftModsPath, profile);

                var path = Path.Combine(sourceDir, jar);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Log($"Removed mod: {jar}");
                }
            }
            RefreshMods();
            RefreshActiveMods();
        }

        private void BtnOpenModsFolder_Click(object sender, EventArgs e)
        {
            var profile = SelectedProfileClean;
            if (profile == null) return;

            var dir = IsSelectedActive
                ? _minecraftModsPath
                : Path.Combine(_minecraftModsPath, profile);

            if (Directory.Exists(dir))
                Process.Start("explorer.exe", dir);
        }

        // ===================== Download single mod =====================

        /// <summary>
        /// Ensures CurseForge service is ready. Returns false if user hasn't set an API key.
        /// </summary>
        private bool EnsureCurseForge()
        {
            if (_curseForge != null) return true;

            if (!SettingsManager.HasCurseForgeApiKey)
            {
                var result = MessageBox.Show(
                    "CurseForge requires an API key to download mods.\n\n" +
                    "Would you like to open Settings to enter your key?\n" +
                    "(You can get a free key at console.curseforge.com)",
                    "CurseForge API Key Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                {
                    using var dlg = new SettingsDialog();
                    dlg.ShowDialog(this);
                }

                // Check again after dialog
                if (!SettingsManager.HasCurseForgeApiKey)
                    return false;
            }

            _curseForge = new CurseForgeService(SettingsManager.CurseForgeApiKey);
            return true;
        }

        private bool IsCurseForgeSelected => cboSource.SelectedIndex == 1;

        private void BtnSettings_Click(object sender, EventArgs e)
        {
            using var dlg = new SettingsDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                // Refresh CurseForge service with new key
                if (SettingsManager.HasCurseForgeApiKey)
                    _curseForge = new CurseForgeService(SettingsManager.CurseForgeApiKey);
                else
                    _curseForge = null;
                Log("Settings saved.");
            }
        }

        private async void BtnDownloadMod_Click(object sender, EventArgs e)
        {
            var profile = SelectedProfileClean;
            if (profile == null) { MessageBox.Show("Select a profile first."); return; }
            var url = txtDownloadUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) { MessageBox.Show("Enter a URL."); return; }

            var destDir = IsSelectedActive
                ? _minecraftModsPath
                : Path.Combine(_minecraftModsPath, profile);

            btnDownloadMod.Enabled = false;
            progressBar.Value = 0;
            Log($"Downloading {url} …");

            try
            {
                // Auto-detect source from URL
                bool isCurseForge = IsCurseForgeSelected ||
                    url.Contains("curseforge.com/minecraft/mc-mods/");
                bool isModrinth = !isCurseForge &&
                    (url.Contains("modrinth.com/mod/") || url.Contains("modrinth.com/plugin/"));

                if (isCurseForge)
                {
                    // CurseForge download path
                    if (!EnsureCurseForge()) { btnDownloadMod.Enabled = true; return; }

                    Log($"Resolving via CurseForge…");
                    var mod = await _curseForge.ResolveSingleAsync(url, cboMcVersion.Text, cboLoader.Text);
                    if (mod == null || mod.NotFound)
                    {
                        MessageBox.Show("This mod could not be found on CurseForge.", "Not Found");
                        return;
                    }
                    if (mod.VersionMismatch)
                    {
                        var versInfo = !string.IsNullOrEmpty(mod.ActualGameVersions)
                            ? mod.ActualGameVersions : "unknown";
                        var answer = MessageBox.Show(
                            $"'{mod.ProjectTitle}' is not available for {cboMcVersion.Text} ({cboLoader.Text}).\n\n" +
                            $"The closest version supports: {versInfo}\n" +
                            $"File: {mod.FileName}\n\n" +
                            "Download it anyway?",
                            "Version Mismatch",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        if (answer != DialogResult.Yes) return;
                    }
                    if (string.IsNullOrEmpty(mod.DownloadUrl))
                    {
                        MessageBox.Show(
                            "This mod's author has disabled direct downloads via the API.\n" +
                            "You may need to download it manually from the CurseForge website.",
                            "Download Not Available",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                    url = mod.DownloadUrl;
                    Log($"Resolved → {mod.FileName}");

                    var cfFileName = mod.FileName;
                    if (!cfFileName.EndsWith(".jar")) cfFileName += ".jar";
                    var cfDest = Path.Combine(destDir, cfFileName);

                    var cfProgress = new Progress<double>(p =>
                    {
                        progressBar.Value = Math.Min(100, (int)(p * 100));
                    });

                    await Downloader.DownloadFileAsync(url, cfDest, cfProgress);
                    progressBar.Value = 100;
                    Log($"Downloaded → {cfFileName}");
                    RefreshMods();
                    RefreshActiveMods();
                    return;
                }

                // Modrinth download path
                if (isModrinth)
                {
                    var resolved = await _resolver.ResolveAsync(url, cboMcVersion.Text, cboLoader.Text);
                    if (resolved.Count > 0)
                    {
                        var mod = resolved[0];
                        if (mod.NotFound)
                        {
                            MessageBox.Show("This mod could not be found or has no downloadable versions.", "Not Found");
                            return;
                        }
                        if (mod.VersionMismatch)
                        {
                            var versInfo = !string.IsNullOrEmpty(mod.ActualGameVersions)
                                ? mod.ActualGameVersions : "unknown";
                            var answer = MessageBox.Show(
                                $"'{mod.ProjectTitle}' is not available for {cboMcVersion.Text} ({cboLoader.Text}).\n\n" +
                                $"The closest version supports: {versInfo}\n" +
                                $"File: {mod.FileName}\n\n" +
                                "Download it anyway?",
                                "Version Mismatch",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);
                            if (answer != DialogResult.Yes) return;
                        }
                        if (!string.IsNullOrEmpty(mod.DownloadUrl))
                        {
                            url = mod.DownloadUrl;
                            Log($"Resolved → {mod.FileName}");
                        }
                    }
                }

                var fileName = Path.GetFileName(new Uri(url).LocalPath);
                if (!fileName.EndsWith(".jar")) fileName += ".jar";
                var dest = Path.Combine(destDir, fileName);

                var progress = new Progress<double>(p =>
                {
                    progressBar.Value = Math.Min(100, (int)(p * 100));
                });

                await Downloader.DownloadFileAsync(url, dest, progress);
                progressBar.Value = 100;
                Log($"Downloaded → {fileName}");
                RefreshMods();
                RefreshActiveMods();
            }
            catch (Exception ex)
            {
                Log($"Download failed: {ex.Message}");
                MessageBox.Show("Download failed: " + ex.Message);
            }
            finally
            {
                btnDownloadMod.Enabled = true;
            }
        }

        // ===================== Import Collection =====================

        private async void BtnImportCollection_Click(object sender, EventArgs e)
        {
            var profile = SelectedProfileClean;
            if (profile == null) { MessageBox.Show("Select a profile first."); return; }

            var destDir = IsSelectedActive
                ? _minecraftModsPath
                : Path.Combine(_minecraftModsPath, profile);

            bool useCurseForge = IsCurseForgeSelected;

            if (useCurseForge && !EnsureCurseForge())
                return;

            using var dlg = new ImportCollectionDialog(cboMcVersion.Text, cboLoader.Text, GetLoadedVersions(),
                useCurseForge ? "CurseForge" : "Modrinth");
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var pastedText = dlg.PastedText;
            var mcVer = dlg.McVersion;
            var loader = dlg.Loader;

            btnImportCollection.Enabled = false;
            Log($"Resolving {(useCurseForge ? "CurseForge" : "Modrinth")} mods…");

            List<ResolvedMod> resolved;
            try
            {
                if (useCurseForge)
                    resolved = await _curseForge.ResolveAsync(pastedText, mcVer, loader);
                else
                    resolved = await _resolver.ResolveAsync(pastedText, mcVer, loader);
            }
            catch (Exception ex)
            {
                Log($"Resolve error: {ex.Message}");
                MessageBox.Show("Error resolving: " + ex.Message);
                btnImportCollection.Enabled = true;
                return;
            }

            if (resolved.Count == 0)
            {
                MessageBox.Show("No mods were resolved from the pasted text.");
                btnImportCollection.Enabled = true;
                return;
            }

            Log($"Resolved {resolved.Count} mods. Downloading…");

            using var pickDlg = new ResolvedModsDialog(resolved);
            if (pickDlg.ShowDialog(this) != DialogResult.OK) { btnImportCollection.Enabled = true; return; }

            var toDownload = pickDlg.SelectedMods;
            int done = 0;
            foreach (var mod in toDownload)
            {
                try
                {
                    var dest = Path.Combine(destDir, mod.FileName);
                    var progress = new Progress<double>(p =>
                    {
                        var total = (double)done / toDownload.Count + p / toDownload.Count;
                        progressBar.Value = Math.Min(100, (int)(total * 100));
                    });
                    await Downloader.DownloadFileAsync(mod.DownloadUrl, dest, progress);
                    Log($"  ✓ {mod.FileName}");
                }
                catch (Exception ex)
                {
                    Log($"  ✗ {mod.FileName}: {ex.Message}");
                }
                done++;
            }

            progressBar.Value = 100;
            Log($"Collection import done. {done}/{toDownload.Count} downloaded.");
            RefreshMods();
            RefreshActiveMods();
            btnImportCollection.Enabled = true;
        }

        // ===================== Update Mods =====================

        private async void BtnUpdateMods_Click(object sender, EventArgs e)
        {
            // Only works on the active profile's loose jars
            if (_switcher.ActiveJarCount() == 0)
            {
                MessageBox.Show("No active mods to update.\nSwitch to a profile first.", "Update Mods");
                return;
            }

            var source = IsCurseForgeSelected ? "curseforge" : "modrinth";

            // If CurseForge, ensure API key is set
            if (source == "curseforge" && !EnsureCurseForge())
                return;

            btnUpdateMods.Enabled = false;
            progressBar.Value = 0;
            Log($"Checking for updates via {source}…");

            List<UpdateableMod> results;
            try
            {
                var checker = new ModUpdateChecker(
                    source == "curseforge" ? _curseForge : null);

                var progress = new Progress<double>(p =>
                {
                    progressBar.Value = Math.Min(100, (int)(p * 100));
                });

                results = await checker.CheckForUpdatesAsync(
                    _minecraftModsPath,
                    source,
                    cboMcVersion.Text,
                    cboLoader.Text,
                    progress);
            }
            catch (Exception ex)
            {
                Log($"Update check failed: {ex.Message}");
                MessageBox.Show("Error checking for updates:\n" + ex.Message, "Error");
                btnUpdateMods.Enabled = true;
                return;
            }

            progressBar.Value = 100;

            var updateCount = results.Count(r => r.HasUpdate);
            var notFoundCount = results.Count(r => r.NotFound);
            Log($"Scan complete: {updateCount} update(s) available, {notFoundCount} not identified.");

            if (updateCount == 0)
            {
                MessageBox.Show(
                    $"All {results.Count - notFoundCount} identified mods are up to date!" +
                    (notFoundCount > 0 ? $"\n({notFoundCount} mod(s) could not be identified.)" : ""),
                    "Update Mods",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                btnUpdateMods.Enabled = true;
                return;
            }

            // Show selection dialog
            using var dlg = new UpdateModsDialog(results);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedUpdates.Count == 0)
            {
                btnUpdateMods.Enabled = true;
                return;
            }

            var toUpdate = dlg.SelectedUpdates;
            Log($"Updating {toUpdate.Count} mod(s)…");
            progressBar.Value = 0;

            int done = 0;
            int success = 0;
            foreach (var mod in toUpdate)
            {
                try
                {
                    var newDest = Path.Combine(_minecraftModsPath, mod.LatestFileName);

                    var progress2 = new Progress<double>(p =>
                    {
                        var total = (double)done / toUpdate.Count + p / toUpdate.Count;
                        progressBar.Value = Math.Min(100, (int)(total * 100));
                    });

                    await Downloader.DownloadFileAsync(mod.LatestDownloadUrl, newDest, progress2);

                    // Delete old file (if different name)
                    if (mod.LatestFileName != mod.CurrentFileName)
                    {
                        var oldPath = Path.Combine(_minecraftModsPath, mod.CurrentFileName);
                        if (File.Exists(oldPath))
                            File.Delete(oldPath);
                    }

                    Log($"  ✓ {mod.ProjectTitle}: {mod.CurrentFileName} → {mod.LatestFileName}");
                    success++;
                }
                catch (Exception ex)
                {
                    Log($"  ✗ {mod.ProjectTitle}: {ex.Message}");
                }
                done++;
            }

            progressBar.Value = 100;
            Log($"Update complete: {success}/{toUpdate.Count} updated successfully.");
            RefreshMods();
            RefreshActiveMods();

            MessageBox.Show(
                $"{success} of {toUpdate.Count} mod(s) updated successfully!",
                "Update Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            btnUpdateMods.Enabled = true;
        }

        // ===================== Migrate (Upgrade / Downgrade) =====================

        private async void BtnMigrateMods_Click(object sender, EventArgs e)
        {
            if (_switcher.ActiveJarCount() == 0)
            {
                MessageBox.Show("No active mods to migrate.\nSwitch to a profile first.", "Migrate Mods");
                return;
            }

            // Ask for the target MC version
            var versions = GetLoadedVersions();
            if (versions.Count == 0)
            {
                MessageBox.Show("Minecraft version list not loaded yet.\nPlease wait for versions to load.", "Migrate Mods");
                return;
            }

            string targetVersion;
            using (var picker = new Form
            {
                Text = "Migrate Mods – Select Target Version",
                Size = new Size(380, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            })
            {
                var lblPrompt = new Label { Text = "Choose the Minecraft version to migrate to:", Location = new Point(12, 12), AutoSize = true };
                var cboTarget = new ComboBox { Location = new Point(12, 40), Size = new Size(340, 24), DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var v in versions) cboTarget.Items.Add(v);
                if (cboTarget.Items.Count > 0) cboTarget.SelectedIndex = 0;

                var lblLoader = new Label { Text = "Loader:", Location = new Point(12, 72), AutoSize = true };
                var cboTargetLoader = new ComboBox { Location = new Point(60, 70), Size = new Size(120, 24), DropDownStyle = ComboBoxStyle.DropDownList };
                cboTargetLoader.Items.AddRange(new object[] { "fabric", "forge", "neoforge", "quilt" });
                // Pre-select current loader
                for (int i = 0; i < cboTargetLoader.Items.Count; i++)
                {
                    if (cboTargetLoader.Items[i].ToString().Equals(cboLoader.Text, StringComparison.OrdinalIgnoreCase))
                    { cboTargetLoader.SelectedIndex = i; break; }
                }
                if (cboTargetLoader.SelectedIndex < 0) cboTargetLoader.SelectedIndex = 0;

                var ok = new Button { Text = "Continue", DialogResult = DialogResult.OK, Location = new Point(196, 115), Size = new Size(75, 28) };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(277, 115), Size = new Size(75, 28) };
                picker.AcceptButton = ok;
                picker.CancelButton = cancel;
                picker.Controls.AddRange(new Control[] { lblPrompt, cboTarget, lblLoader, cboTargetLoader, ok, cancel });

                if (picker.ShowDialog(this) != DialogResult.OK) return;

                targetVersion = cboTarget.Text;
                // Update the loader combo to match what user picked for migration
                cboLoader.Text = cboTargetLoader.Text;
            }

            if (string.IsNullOrEmpty(targetVersion))
            {
                MessageBox.Show("No target version selected.", "Migrate Mods");
                return;
            }

            var source = IsCurseForgeSelected ? "curseforge" : "modrinth";
            if (source == "curseforge" && !EnsureCurseForge()) return;

            btnMigrateMods.Enabled = false;
            progressBar.Value = 0;
            Log($"Checking migration to {targetVersion} via {source}…");

            List<MigratableMod> results;
            try
            {
                var svc = new VersionMigrationService(source == "curseforge" ? _curseForge : null);
                var progress = new Progress<double>(p =>
                {
                    progressBar.Value = Math.Min(100, (int)(p * 100));
                });

                results = await svc.CheckMigrationAsync(
                    _minecraftModsPath, source, targetVersion, cboLoader.Text, progress);
            }
            catch (Exception ex)
            {
                Log($"Migration check failed: {ex.Message}");
                MessageBox.Show("Error checking migration:\n" + ex.Message, "Error");
                btnMigrateMods.Enabled = true;
                return;
            }

            progressBar.Value = 100;

            var availCount = results.Count(r => r.Available);
            var incompatCount = results.Count(r => r.Incompatible);
            var notFoundCount = results.Count(r => r.NotFound);
            Log($"Migration scan: {availCount} available, {incompatCount} incompatible, {notFoundCount} not identified.");

            if (availCount == 0 && incompatCount == 0)
            {
                MessageBox.Show("Could not identify any mods to migrate.", "Migrate Mods");
                btnMigrateMods.Enabled = true;
                return;
            }

            // Show migration dialog
            using var dlg = new VersionMigrateDialog(results, targetVersion);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedMods.Count == 0)
            {
                btnMigrateMods.Enabled = true;
                return;
            }

            var toMigrate = dlg.SelectedMods;
            bool keepIncompat = dlg.KeepIncompatible;

            Log($"Migrating {toMigrate.Count} mod(s) to {targetVersion}…");
            progressBar.Value = 0;

            int done = 0;
            int success = 0;
            foreach (var mod in toMigrate)
            {
                try
                {
                    var newDest = Path.Combine(_minecraftModsPath, mod.TargetFileName);

                    var progress2 = new Progress<double>(p =>
                    {
                        var total = (double)done / toMigrate.Count + p / toMigrate.Count;
                        progressBar.Value = Math.Min(100, (int)(total * 100));
                    });

                    await Downloader.DownloadFileAsync(mod.TargetDownloadUrl, newDest, progress2);

                    // Remove old file if filename differs
                    if (mod.TargetFileName != mod.CurrentFileName)
                    {
                        var oldPath = Path.Combine(_minecraftModsPath, mod.CurrentFileName);
                        if (File.Exists(oldPath))
                            File.Delete(oldPath);
                    }

                    Log($"  ✓ {mod.ProjectTitle}: {mod.CurrentFileName} → {mod.TargetFileName}");
                    success++;
                }
                catch (Exception ex)
                {
                    Log($"  ✗ {mod.ProjectTitle}: {ex.Message}");
                }
                done++;
            }

            // Handle incompatible mods that weren't migrated
            if (!keepIncompat)
            {
                var incompatible = results.Where(r => r.Incompatible).ToList();
                foreach (var mod in incompatible)
                {
                    try
                    {
                        var path = Path.Combine(_minecraftModsPath, mod.CurrentFileName);
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                            Log($"  🗑 Removed incompatible: {mod.CurrentFileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  ✗ Could not remove {mod.CurrentFileName}: {ex.Message}");
                    }
                }
            }

            progressBar.Value = 100;
            Log($"Migration complete: {success}/{toMigrate.Count} migrated to {targetVersion}.");
            RefreshMods();
            RefreshActiveMods();

            var msg = $"{success} of {toMigrate.Count} mod(s) migrated to {targetVersion}!";
            if (incompatCount > 0)
                msg += $"\n{incompatCount} incompatible mod(s) were {(keepIncompat ? "kept" : "removed")}.";

            MessageBox.Show(msg, "Migration Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            btnMigrateMods.Enabled = true;
        }

        // ===================== Simple prompt dialog =====================

        private static string Prompt(string title, string label, string defaultValue = "")
        {
            var form = new Form { Text = title, Size = new Size(380, 170), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
            var lbl = new Label { Text = label, Location = new Point(12, 12), AutoSize = true };
            var txt = new TextBox { Text = defaultValue, Location = new Point(12, 60), Size = new Size(340, 23) };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(196, 95), Size = new Size(75, 28) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(277, 95), Size = new Size(75, 28) };
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            form.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
            return form.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : null;
        }
    }
}

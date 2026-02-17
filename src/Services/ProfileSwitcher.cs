using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace ModProfileSwitcher.Services
{
    /// <summary>
    /// Switches mod profiles using subfolders inside .minecraft/mods.
    ///
    /// How it works:
    ///   - Active profile  = loose .jar files sitting directly in .minecraft/mods/
    ///   - Inactive profiles = subfolders in .minecraft/mods/ (e.g. mods/1.21.4/)
    ///   - Switching: move current loose jars into a subfolder (deactivate),
    ///                then move the target subfolder's jars to the root (activate).
    ///
    /// Resource / Shader packs:
    ///   - Each profile subfolder may contain resourcepacks/ and shaderpacks/ dirs.
    ///   - On activate: files in those dirs are moved to .minecraft/resourcepacks and
    ///     .minecraft/shaderpacks. If a file with the same name already exists,
    ///     a ConflictResolver callback is invoked.
    ///   - On deactivate: any packs that were moved in are moved back into the profile subfolder.
    /// </summary>
    public class ProfileSwitcher
    {
        private readonly ProfilesManager _profiles;
        private readonly string _minecraftModsPath;
        private readonly string _minecraftDir;
        private readonly string _backupsRoot;

        /// <summary>
        /// Name of the currently active profile (the subfolder the loose jars belong to).
        /// null if the loose jars haven't been assigned to a profile yet.
        /// Stored in a small file: .minecraft/mods/.active_profile
        /// </summary>
        private string _activeProfileName;

        public string ActiveProfileName => _activeProfileName;

        /// <summary>
        /// Callback to resolve conflicts when a resource/shader pack file already exists
        /// in the target directory. Return true to overwrite, false to skip.
        /// Parameters: (fileName, packType "resourcepack" or "shaderpack")
        /// </summary>
        public Func<string, string, bool> ConflictResolver { get; set; }

        /// <summary>
        /// The two pack types we manage, along with their folder names inside .minecraft
        /// and inside each profile subfolder.
        /// </summary>
        private static readonly string[] PackFolders = { "resourcepacks", "shaderpacks" };

        public ProfileSwitcher(ProfilesManager profiles, string minecraftModsPath, string backupsRoot)
        {
            _profiles = profiles;
            _minecraftModsPath = minecraftModsPath;
            _minecraftDir = Directory.GetParent(minecraftModsPath)?.FullName
                ?? Path.GetDirectoryName(minecraftModsPath);
            _backupsRoot = backupsRoot;

            // Read persisted active profile name
            _activeProfileName = ReadActiveMarker();
        }

        /// <summary>
        /// Lists all inactive profile subfolders in .minecraft/mods.
        /// </summary>
        public List<string> ListInactiveProfiles()
        {
            if (!Directory.Exists(_minecraftModsPath)) return new List<string>();
            return Directory.GetDirectories(_minecraftModsPath)
                .Select(Path.GetFileName)
                .Where(n => !n.StartsWith("."))  // skip hidden marker dirs
                .OrderBy(n => n)
                .ToList();
        }

        /// <summary>
        /// Returns the count of loose .jar files currently in the mods root (the active mods).
        /// </summary>
        public int ActiveJarCount()
        {
            return GetJars(_minecraftModsPath).Length;
        }

        /// <summary>
        /// Switches to a different profile:
        ///   1. Deactivate: move current loose .jars into a subfolder (named by the active profile).
        ///      Also stash any resource/shader packs that belong to the old profile.
        ///   2. Activate: move the target subfolder's .jars to the mods root.
        ///      Also deploy resource/shader packs from the target profile.
        ///   3. Update the active marker.
        /// If there is no current active profile name, prompts for one via <paramref name="currentProfileName"/>.
        /// </summary>
        public void SwitchTo(string targetProfileName, string currentProfileName = null)
        {
            var targetDir = Path.Combine(_minecraftModsPath, targetProfileName);
            if (!Directory.Exists(targetDir))
                throw new InvalidOperationException($"Profile folder '{targetProfileName}' not found in mods directory.");

            // --- 1. Deactivate current loose jars ---
            var looseJars = GetJars(_minecraftModsPath);
            if (looseJars.Length > 0)
            {
                // Determine where to stash them
                var stashName = _activeProfileName ?? currentProfileName;
                if (string.IsNullOrEmpty(stashName))
                    throw new InvalidOperationException(
                        "There are loose jars in the mods folder but no active profile name is set.\n" +
                        "Please name the current set of mods first so they can be saved.");

                var stashDir = Path.Combine(_minecraftModsPath, stashName);
                Directory.CreateDirectory(stashDir);

                foreach (var jar in looseJars)
                {
                    var dest = Path.Combine(stashDir, Path.GetFileName(jar));
                    if (File.Exists(dest))
                        File.Delete(dest); // overwrite if same name exists
                    File.Move(jar, dest);
                }

                // Stash resource/shader packs that were deployed by the old profile
                StashPacks(stashDir);
            }

            // --- 2. Activate target profile ---
            var targetJars = GetJars(targetDir);
            foreach (var jar in targetJars)
            {
                File.Move(jar, Path.Combine(_minecraftModsPath, Path.GetFileName(jar)));
            }

            // Deploy resource/shader packs from the target profile
            DeployPacks(targetDir);

            // Remove the now-empty subfolder (or leave it if it has non-jar files)
            if (IsDirectoryEmpty(targetDir))
                Directory.Delete(targetDir, recursive: true);

            // --- 3. Update marker ---
            _activeProfileName = targetProfileName;
            WriteActiveMarker(targetProfileName);
        }

        /// <summary>
        /// Deactivates the current profile: moves loose jars into a subfolder.
        /// Also stashes any resource/shader packs back into the profile.
        /// After this, no profile is active (mods root is empty).
        /// </summary>
        public void Deactivate(string profileName = null)
        {
            var name = profileName ?? _activeProfileName;
            if (string.IsNullOrEmpty(name)) return;

            var looseJars = GetJars(_minecraftModsPath);
            if (looseJars.Length == 0 && !HasTrackedPacks(name))
                return;

            var stashDir = Path.Combine(_minecraftModsPath, name);
            Directory.CreateDirectory(stashDir);
            foreach (var jar in looseJars)
            {
                var dest = Path.Combine(stashDir, Path.GetFileName(jar));
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(jar, dest);
            }

            // Stash resource/shader packs
            StashPacks(stashDir);

            _activeProfileName = null;
            DeleteActiveMarker();
        }

        /// <summary>
        /// Creates a new empty profile subfolder.
        /// </summary>
        public void CreateProfileFolder(string name)
        {
            var dir = Path.Combine(_minecraftModsPath, name);
            if (Directory.Exists(dir))
                throw new InvalidOperationException($"Profile folder '{name}' already exists.");
            Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// Renames a profile subfolder.
        /// </summary>
        public void RenameProfileFolder(string oldName, string newName)
        {
            var oldDir = Path.Combine(_minecraftModsPath, oldName);
            var newDir = Path.Combine(_minecraftModsPath, newName);
            if (!Directory.Exists(oldDir))
                throw new InvalidOperationException($"Profile folder '{oldName}' not found.");
            if (Directory.Exists(newDir))
                throw new InvalidOperationException($"Profile folder '{newName}' already exists.");
            Directory.Move(oldDir, newDir);

            // If this was the active profile, update the marker
            if (_activeProfileName == oldName)
            {
                _activeProfileName = newName;
                WriteActiveMarker(newName);
            }
        }

        /// <summary>
        /// Deletes a profile subfolder and all its contents.
        /// </summary>
        public void DeleteProfileFolder(string name)
        {
            if (_activeProfileName == name)
                throw new InvalidOperationException("Cannot delete the active profile. Deactivate or switch first.");
            var dir = Path.Combine(_minecraftModsPath, name);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }

        /// <summary>
        /// Lists jars in a profile subfolder.
        /// </summary>
        public List<string> ListProfileJars(string profileName)
        {
            var dir = Path.Combine(_minecraftModsPath, profileName);
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir, "*.jar").Select(Path.GetFileName).OrderBy(n => n).ToList();
        }

        /// <summary>
        /// Lists the loose (active) jars at the mods root.
        /// </summary>
        public List<string> ListActiveJars()
        {
            return GetJars(_minecraftModsPath).Select(Path.GetFileName).OrderBy(n => n).ToList();
        }

        /// <summary>
        /// Sets the active profile marker name without moving any files.
        /// </summary>
        public void SetActiveProfileName(string name)
        {
            _activeProfileName = name;
            if (string.IsNullOrEmpty(name))
                DeleteActiveMarker();
            else
                WriteActiveMarker(name);
        }

        /// <summary>
        /// Lists resource/shader pack files stored inside a profile subfolder.
        /// Returns tuples of (packType, fileName).
        /// </summary>
        public List<(string PackType, string FileName)> ListProfilePacks(string profileName)
        {
            var result = new List<(string, string)>();
            var profileDir = Path.Combine(_minecraftModsPath, profileName);
            foreach (var folder in PackFolders)
            {
                var dir = Path.Combine(profileDir, folder);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir).OrderBy(f => f))
                    result.Add((folder, Path.GetFileName(file)));
            }
            return result;
        }

        /// <summary>
        /// Lists resource/shader packs tracked by the active profile
        /// (i.e. currently deployed to .minecraft/resourcepacks and /shaderpacks).
        /// </summary>
        public List<(string PackType, string FileName)> ListActiveProfilePacks()
        {
            if (string.IsNullOrEmpty(_activeProfileName)) return new List<(string, string)>();
            var manifest = ReadPackManifest(_activeProfileName);
            return manifest;
        }

        // ---- Resource / Shader pack switching ----

        /// <summary>
        /// Tracks which pack files were deployed by a profile, so we know which ones
        /// to stash back on deactivation. Stored in: .minecraft/mods/.packs_{profileName}
        /// </summary>
        private string PackManifestPath(string profileName) =>
            Path.Combine(_minecraftModsPath, $".packs_{profileName}");

        private List<(string PackType, string FileName)> ReadPackManifest(string profileName)
        {
            var path = PackManifestPath(profileName);
            var result = new List<(string, string)>();
            if (!File.Exists(path)) return result;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('|');
                if (parts.Length == 2)
                    result.Add((parts[0], parts[1]));
            }
            return result;
        }

        private void WritePackManifest(string profileName, List<(string PackType, string FileName)> packs)
        {
            var path = PackManifestPath(profileName);
            if (packs.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            File.WriteAllLines(path, packs.Select(p => $"{p.PackType}|{p.FileName}"));
        }

        private void DeletePackManifest(string profileName)
        {
            var path = PackManifestPath(profileName);
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>
        /// Whether this profile has any tracked packs deployed.
        /// </summary>
        private bool HasTrackedPacks(string profileName)
        {
            return ReadPackManifest(profileName).Count > 0;
        }

        /// <summary>
        /// Deploys resource/shader packs from a profile subfolder into .minecraft/resourcepacks
        /// and .minecraft/shaderpacks. Records which files were deployed.
        /// </summary>
        private void DeployPacks(string profileDir)
        {
            var profileName = Path.GetFileName(profileDir);
            var deployed = new List<(string PackType, string FileName)>();

            foreach (var folder in PackFolders)
            {
                var srcDir = Path.Combine(profileDir, folder);
                if (!Directory.Exists(srcDir)) continue;

                var destDir = Path.Combine(_minecraftDir, folder);
                Directory.CreateDirectory(destDir);

                foreach (var file in Directory.GetFiles(srcDir))
                {
                    var fileName = Path.GetFileName(file);
                    var destFile = Path.Combine(destDir, fileName);

                    if (File.Exists(destFile))
                    {
                        // Conflict: ask user via callback
                        var packLabel = folder == "resourcepacks" ? "resource pack" : "shader pack";
                        var overwrite = ConflictResolver?.Invoke(fileName, packLabel) ?? true;
                        if (!overwrite)
                            continue; // skip this file
                        File.Delete(destFile);
                    }

                    File.Move(file, destFile);
                    deployed.Add((folder, fileName));
                }

                // Remove the now-empty pack dir inside the profile
                if (IsDirectoryEmpty(srcDir))
                    Directory.Delete(srcDir, recursive: true);
            }

            if (deployed.Count > 0)
                WritePackManifest(profileName, deployed);
        }

        /// <summary>
        /// Stashes resource/shader packs back from .minecraft/resourcepacks and
        /// .minecraft/shaderpacks into the profile subfolder. Uses the pack manifest
        /// to know which files belong to this profile.
        /// </summary>
        private void StashPacks(string profileDir)
        {
            var profileName = Path.GetFileName(profileDir);
            var manifest = ReadPackManifest(profileName);
            if (manifest.Count == 0) return;

            foreach (var (packType, fileName) in manifest)
            {
                var srcFile = Path.Combine(_minecraftDir, packType, fileName);
                if (!File.Exists(srcFile)) continue;

                var destDir = Path.Combine(profileDir, packType);
                Directory.CreateDirectory(destDir);
                var destFile = Path.Combine(destDir, fileName);

                if (File.Exists(destFile)) File.Delete(destFile);
                File.Move(srcFile, destFile);
            }

            DeletePackManifest(profileName);
        }

        private static bool IsDirectoryEmpty(string path)
        {
            if (!Directory.Exists(path)) return true;
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }

        // ---- Backups (kept for safety) ----

        public string BackupCurrentMods()
        {
            var backupDir = Path.Combine(_backupsRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(backupDir);
            foreach (var jar in GetJars(_minecraftModsPath))
                File.Copy(jar, Path.Combine(backupDir, Path.GetFileName(jar)));
            return backupDir;
        }

        public void Restore(string backupFolderPath)
        {
            foreach (var jar in GetJars(_minecraftModsPath))
                File.Delete(jar);
            foreach (var jar in GetJars(backupFolderPath))
                File.Copy(jar, Path.Combine(_minecraftModsPath, Path.GetFileName(jar)), overwrite: true);
        }

        public List<string> ListBackups()
        {
            if (!Directory.Exists(_backupsRoot)) return new List<string>();
            return Directory.GetDirectories(_backupsRoot)
                .OrderByDescending(d => d)
                .Select(Path.GetFileName)
                .ToList();
        }

        // ---- Marker file: .active_profile ----

        private string MarkerPath => Path.Combine(_minecraftModsPath, ".active_profile");

        private string ReadActiveMarker()
        {
            if (File.Exists(MarkerPath))
            {
                var name = File.ReadAllText(MarkerPath).Trim();
                return string.IsNullOrEmpty(name) ? null : name;
            }
            return null;
        }

        private void WriteActiveMarker(string name)
        {
            File.WriteAllText(MarkerPath, name);
        }

        private void DeleteActiveMarker()
        {
            if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
        }

        private static string[] GetJars(string dir)
        {
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            return Directory.GetFiles(dir, "*.jar");
        }
    }
}

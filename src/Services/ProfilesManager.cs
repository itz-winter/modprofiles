using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModProfileSwitcher.Models;
using Newtonsoft.Json;

namespace ModProfileSwitcher.Services
{
    /// <summary>
    /// Manages mod profiles on disk.
    /// Each profile lives in Profiles/{name}/ with a manifest.json and a mods/ subfolder.
    /// </summary>
    public class ProfilesManager
    {
        private readonly string _profilesRoot;

        public ProfilesManager(string profilesRoot)
        {
            _profilesRoot = profilesRoot;
            Directory.CreateDirectory(_profilesRoot);
        }

        public string ProfilesRoot => _profilesRoot;

        // --- CRUD ---

        public List<string> ListProfiles()
        {
            if (!Directory.Exists(_profilesRoot)) return new List<string>();
            return Directory.GetDirectories(_profilesRoot)
                .Select(Path.GetFileName)
                .OrderBy(n => n)
                .ToList();
        }

        public void CreateProfile(string name, string mcVersion = "", string loader = "fabric")
        {
            var dir = ProfileDir(name);
            if (Directory.Exists(dir))
                throw new InvalidOperationException($"Profile '{name}' already exists.");
            Directory.CreateDirectory(ModsDir(name));
            SaveManifest(name, new ModProfile { Name = name, MinecraftVersion = mcVersion, Loader = loader });
        }

        public void DeleteProfile(string name)
        {
            var dir = ProfileDir(name);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }

        public void RenameProfile(string oldName, string newName)
        {
            var oldDir = ProfileDir(oldName);
            var newDir = ProfileDir(newName);
            if (!Directory.Exists(oldDir))
                throw new InvalidOperationException($"Profile '{oldName}' not found.");
            if (Directory.Exists(newDir))
                throw new InvalidOperationException($"Profile '{newName}' already exists.");
            Directory.Move(oldDir, newDir);
            var manifest = LoadManifest(newName);
            if (manifest != null) { manifest.Name = newName; SaveManifest(newName, manifest); }
        }

        // --- Manifest ---

        public ModProfile LoadManifest(string profileName)
        {
            var path = ManifestPath(profileName);
            if (!File.Exists(path)) return new ModProfile { Name = profileName };
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<ModProfile>(json) ?? new ModProfile { Name = profileName };
        }

        public void SaveManifest(string profileName, ModProfile profile)
        {
            var path = ManifestPath(profileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(profile, Formatting.Indented));
        }

        // --- Mods listing ---

        public List<string> ListMods(string profileName)
        {
            var dir = ModsDir(profileName);
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir, "*.jar")
                .Select(Path.GetFileName)
                .OrderBy(n => n)
                .ToList();
        }

        public void AddModFile(string profileName, string sourceJarPath)
        {
            var dir = ModsDir(profileName);
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, Path.GetFileName(sourceJarPath));
            File.Copy(sourceJarPath, dest, overwrite: true);
        }

        public void RemoveMod(string profileName, string jarFileName)
        {
            var path = Path.Combine(ModsDir(profileName), jarFileName);
            if (File.Exists(path)) File.Delete(path);
        }

        // --- Helpers ---

        public string ProfileDir(string name) => Path.Combine(_profilesRoot, name);
        public string ModsDir(string name) => Path.Combine(_profilesRoot, name, "mods");
        private string ManifestPath(string name) => Path.Combine(_profilesRoot, name, "manifest.json");
    }
}

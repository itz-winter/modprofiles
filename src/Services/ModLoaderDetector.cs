using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ModProfileSwitcher.Services
{
    /// <summary>
    /// Detects which mod loaders are installed in a .minecraft directory
    /// by inspecting launcher_profiles.json, the versions folder, and libraries folder.
    /// </summary>
    public static class ModLoaderDetector
    {
        public class LoaderInfo
        {
            public string Name { get; set; }        // e.g. "Fabric", "Forge"
            public bool Installed { get; set; }
            public string InstallerUrl { get; set; } // link to download/install page
        }

        /// <summary>
        /// All known loaders with their download/install page URLs.
        /// </summary>
        public static readonly List<LoaderInfo> KnownLoaders = new List<LoaderInfo>
        {
            new LoaderInfo { Name = "Fabric",   InstallerUrl = "https://fabricmc.net/use/installer/" },
            new LoaderInfo { Name = "Forge",    InstallerUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/" },
            new LoaderInfo { Name = "Quilt",    InstallerUrl = "https://quiltmc.org/en/install/" },
            new LoaderInfo { Name = "NeoForge", InstallerUrl = "https://neoforged.net/" },
        };

        /// <summary>
        /// Scans the .minecraft directory and returns info about which loaders are installed.
        /// </summary>
        public static List<LoaderInfo> Detect(string minecraftDir)
        {
            var results = new List<LoaderInfo>();
            foreach (var loader in KnownLoaders)
            {
                results.Add(new LoaderInfo
                {
                    Name = loader.Name,
                    InstallerUrl = loader.InstallerUrl,
                    Installed = IsLoaderPresent(minecraftDir, loader.Name)
                });
            }
            return results;
        }

        /// <summary>
        /// Returns true if ANY mod loader is detected.
        /// </summary>
        public static bool AnyLoaderInstalled(string minecraftDir)
        {
            return Detect(minecraftDir).Any(l => l.Installed);
        }

        // ---- Detection strategies per loader ----

        private static bool IsLoaderPresent(string mcDir, string loaderName)
        {
            // Strategy 1: check versions/ folder for version directories whose name contains the loader keyword
            if (CheckVersionsFolder(mcDir, loaderName))
                return true;

            // Strategy 2: check launcher_profiles.json for profiles that reference the loader
            if (CheckLauncherProfiles(mcDir, loaderName))
                return true;

            // Strategy 3: check libraries/ folder for a loader-specific subfolder
            if (CheckLibrariesFolder(mcDir, loaderName))
                return true;

            return false;
        }

        private static bool CheckVersionsFolder(string mcDir, string loaderName)
        {
            var versionsDir = Path.Combine(mcDir, "versions");
            if (!Directory.Exists(versionsDir)) return false;

            var keyword = LoaderKeyword(loaderName);
            return Directory.GetDirectories(versionsDir)
                .Any(d => Path.GetFileName(d)
                    .Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static bool CheckLauncherProfiles(string mcDir, string loaderName)
        {
            var profilesFile = Path.Combine(mcDir, "launcher_profiles.json");
            if (!File.Exists(profilesFile)) return false;

            try
            {
                var json = File.ReadAllText(profilesFile);
                var keyword = LoaderKeyword(loaderName);
                // Quick text search — works for all loaders since their name appears in version IDs
                return json.Contains(keyword, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckLibrariesFolder(string mcDir, string loaderName)
        {
            var libDir = Path.Combine(mcDir, "libraries");
            if (!Directory.Exists(libDir)) return false;

            // Each loader stores artifacts under a known group path
            var subPaths = LoaderLibraryPaths(loaderName);
            return subPaths.Any(sub => Directory.Exists(Path.Combine(libDir, sub)));
        }

        private static string LoaderKeyword(string loaderName)
        {
            return loaderName.ToLowerInvariant() switch
            {
                "fabric"   => "fabric",
                "forge"    => "forge",
                "quilt"    => "quilt",
                "neoforge" => "neoforge",
                _          => loaderName.ToLowerInvariant()
            };
        }

        private static string[] LoaderLibraryPaths(string loaderName)
        {
            return loaderName.ToLowerInvariant() switch
            {
                "fabric"   => new[] { Path.Combine("net", "fabricmc") },
                "forge"    => new[] { Path.Combine("net", "minecraftforge") },
                "quilt"    => new[] { Path.Combine("org", "quiltmc") },
                "neoforge" => new[] { Path.Combine("net", "neoforged") },
                _          => Array.Empty<string>()
            };
        }
    }
}

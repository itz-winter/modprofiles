using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ModProfileSwitcher.Services
{
    /// <summary>
    /// Fetches Minecraft game versions from the Modrinth API and filters to releases
    /// that are actually supported by mod loaders.
    /// Results are cached for the lifetime of the app.
    /// </summary>
    public static class MinecraftVersionService
    {
        private static readonly HttpClient Http = new HttpClient();
        private static List<string> _cachedVersions;

        static MinecraftVersionService()
        {
            Http.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0 (github.com)");
        }

        /// <summary>
        /// Returns a list of Minecraft release versions supported by loaders on Modrinth,
        /// ordered newest-first. Uses a cached result after the first call.
        /// </summary>
        public static async Task<List<string>> GetVersionsAsync()
        {
            if (_cachedVersions != null)
                return _cachedVersions;

            try
            {
                // Modrinth tag endpoint returns all game versions with type and major flags
                var json = await Http.GetStringAsync("https://api.modrinth.com/v2/tag/game_version");
                var arr = JArray.Parse(json);

                _cachedVersions = arr
                    .Where(v => v["version_type"]?.ToString() == "release")
                    .Select(v => v["version"]?.ToString())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList(); // already sorted newest-first by the API

                return _cachedVersions;
            }
            catch
            {
                // Fallback if network is unavailable
                _cachedVersions = new List<string>
                {
                    "1.21.4", "1.21.3", "1.21.2", "1.21.1", "1.21",
                    "1.20.6", "1.20.4", "1.20.3", "1.20.2", "1.20.1", "1.20",
                    "1.19.4", "1.19.3", "1.19.2", "1.19.1", "1.19",
                    "1.18.2", "1.18.1", "1.18",
                    "1.17.1", "1.17",
                    "1.16.5", "1.16.4", "1.16.3", "1.16.2", "1.16.1", "1.16"
                };
                return _cachedVersions;
            }
        }

        /// <summary>
        /// Clears the cache so the next call re-fetches from the API.
        /// </summary>
        public static void ClearCache() => _cachedVersions = null;
    }
}

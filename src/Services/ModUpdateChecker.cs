using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ModProfileSwitcher.Models;
using Newtonsoft.Json.Linq;

namespace ModProfileSwitcher.Services
{
    /// <summary>
    /// Checks active profile mods for available updates via Modrinth or CurseForge.
    /// Uses file-hash lookup (SHA-1 for Modrinth, MurmurHash2 for CurseForge) to identify
    /// which project each jar belongs to, then checks for newer versions.
    /// </summary>
    public class ModUpdateChecker
    {
        private readonly HttpClient _modrinthHttp;
        private readonly CurseForgeService _curseForge;

        /// <param name="curseForge">Can be null if CurseForge is not configured.</param>
        public ModUpdateChecker(CurseForgeService curseForge = null)
        {
            _modrinthHttp = new HttpClient();
            _modrinthHttp.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0 (github.com)");
            _curseForge = curseForge;
        }

        /// <summary>
        /// Check all jars in the given directory for updates.
        /// </summary>
        /// <param name="modsDir">Path to the mods directory (root of .minecraft/mods for active profile).</param>
        /// <param name="source">"modrinth" or "curseforge"</param>
        /// <param name="mcVersion">Target Minecraft version filter.</param>
        /// <param name="loader">Target mod loader filter.</param>
        /// <param name="progress">Reports 0.0–1.0 progress.</param>
        public async Task<List<UpdateableMod>> CheckForUpdatesAsync(
            string modsDir,
            string source,
            string mcVersion,
            string loader,
            IProgress<double> progress = null)
        {
            var jarFiles = Directory.GetFiles(modsDir, "*.jar")
                .Where(f => !Path.GetFileName(f).StartsWith("."))
                .ToList();

            if (jarFiles.Count == 0)
                return new List<UpdateableMod>();

            if (source.Equals("curseforge", StringComparison.OrdinalIgnoreCase))
                return await CheckCurseForgeAsync(jarFiles, mcVersion, loader, progress);
            else
                return await CheckModrinthAsync(jarFiles, mcVersion, loader, progress);
        }

        // ======================== Modrinth ========================

        private async Task<List<UpdateableMod>> CheckModrinthAsync(
            List<string> jarFiles, string mcVersion, string loader, IProgress<double> progress)
        {
            var results = new List<UpdateableMod>();

            // Step 1: Compute SHA-1 hashes for all jars
            var hashMap = new Dictionary<string, string>(); // sha1 → filePath
            foreach (var file in jarFiles)
            {
                var hash = ComputeSha1(file);
                hashMap[hash] = file;
            }

            // Step 2: Batch lookup via POST /v2/version_files
            var identified = new Dictionary<string, JObject>(); // sha1 → version object
            try
            {
                var payload = new JObject
                {
                    ["hashes"] = new JArray(hashMap.Keys.ToArray()),
                    ["algorithm"] = "sha1"
                };
                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var resp = await _modrinthHttp.PostAsync("https://api.modrinth.com/v2/version_files", content);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var obj = JObject.Parse(json);
                    foreach (var prop in obj.Properties())
                    {
                        identified[prop.Name] = (JObject)prop.Value;
                    }
                }
            }
            catch { /* batch lookup failed — will fall back to filename guessing */ }

            progress?.Report(0.3);

            // Step 3: For each jar, check for updates
            int done = 0;
            foreach (var file in jarFiles)
            {
                var fileName = Path.GetFileName(file);
                var hash = hashMap.FirstOrDefault(kv => kv.Value == file).Key;
                var mod = new UpdateableMod
                {
                    CurrentFileName = fileName,
                    Source = "modrinth"
                };

                try
                {
                    JObject currentVersion = null;
                    string projectId = null;

                    if (hash != null && identified.TryGetValue(hash, out currentVersion))
                    {
                        projectId = currentVersion["project_id"]?.ToString();
                        mod.CurrentVersion = currentVersion["version_number"]?.ToString() ?? "";
                    }

                    // Fallback: try filename-based slug guessing
                    if (projectId == null)
                    {
                        var guessedSlug = GuessSlugFromFileName(fileName);
                        if (guessedSlug != null)
                        {
                            try
                            {
                                var projJson = await _modrinthHttp.GetStringAsync(
                                    $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(guessedSlug)}");
                                var projObj = JObject.Parse(projJson);
                                projectId = projObj["id"]?.ToString();
                                mod.ProjectTitle = projObj["title"]?.ToString() ?? guessedSlug;
                                mod.ProjectSlug = projObj["slug"]?.ToString() ?? guessedSlug;
                            }
                            catch { /* slug guess failed */ }
                        }
                    }

                    if (projectId == null)
                    {
                        mod.NotFound = true;
                        mod.StatusMessage = "Could not identify mod";
                        results.Add(mod);
                        continue;
                    }

                    mod.ProjectId = projectId;

                    // Fetch project info if we don't have the title yet
                    if (string.IsNullOrEmpty(mod.ProjectTitle))
                    {
                        try
                        {
                            var projJson = await _modrinthHttp.GetStringAsync(
                                $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}");
                            var projObj = JObject.Parse(projJson);
                            mod.ProjectTitle = projObj["title"]?.ToString() ?? projectId;
                            mod.ProjectSlug = projObj["slug"]?.ToString() ?? projectId;
                        }
                        catch
                        {
                            mod.ProjectTitle = projectId;
                            mod.ProjectSlug = projectId;
                        }
                    }

                    // Fetch latest version with filters
                    var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version";
                    var queryParts = new List<string>();
                    if (!string.IsNullOrEmpty(loader))
                        queryParts.Add($"loaders=[\"{loader}\"]");
                    if (!string.IsNullOrEmpty(mcVersion))
                        queryParts.Add($"game_versions=[\"{mcVersion}\"]");
                    if (queryParts.Count > 0)
                        url += "?" + string.Join("&", queryParts);

                    var versionsJson = await _modrinthHttp.GetStringAsync(url);
                    var versions = JArray.Parse(versionsJson);

                    if (versions.Count == 0)
                    {
                        mod.HasUpdate = false;
                        mod.StatusMessage = "✓ Up to date (no versions for this MC version)";
                        results.Add(mod);
                        continue;
                    }

                    var latest = (JObject)versions[0];
                    var latestVersionNumber = latest["version_number"]?.ToString() ?? "";
                    var latestVersionId = latest["id"]?.ToString() ?? "";

                    // Compare: is the latest different from current?
                    var currentVersionId = currentVersion?["id"]?.ToString() ?? "";
                    bool isNewer = !string.IsNullOrEmpty(latestVersionId) &&
                                   latestVersionId != currentVersionId;

                    // Also compare by filename — if hash matched but is the same version
                    var latestFiles = latest["files"] as JArray;
                    JObject latestFile = null;
                    if (latestFiles != null && latestFiles.Count > 0)
                    {
                        foreach (JObject f in latestFiles)
                        {
                            if (f["primary"]?.Value<bool>() == true) { latestFile = f; break; }
                        }
                        latestFile ??= (JObject)latestFiles[0];
                    }

                    var latestFileName = latestFile?["filename"]?.ToString() ?? "";
                    var latestDownloadUrl = latestFile?["url"]?.ToString() ?? "";

                    // If we couldn't identify the current version, compare by filename
                    if (string.IsNullOrEmpty(currentVersionId) && latestFileName == fileName)
                        isNewer = false;

                    mod.LatestVersion = latestVersionNumber;
                    mod.LatestFileName = latestFileName;
                    mod.LatestDownloadUrl = latestDownloadUrl;
                    mod.HasUpdate = isNewer;
                    mod.Selected = isNewer;

                    if (isNewer)
                        mod.StatusMessage = $"⬆ Update: {mod.CurrentVersion} → {latestVersionNumber}";
                    else
                        mod.StatusMessage = "✓ Up to date";
                }
                catch (Exception ex)
                {
                    mod.NotFound = true;
                    mod.StatusMessage = $"Error: {ex.Message}";
                }

                results.Add(mod);
                done++;
                progress?.Report(0.3 + 0.7 * done / jarFiles.Count);
            }

            return results;
        }

        // ======================== CurseForge ========================

        private async Task<List<UpdateableMod>> CheckCurseForgeAsync(
            List<string> jarFiles, string mcVersion, string loader, IProgress<double> progress)
        {
            const int MinecraftGameId = 432;

            if (_curseForge == null)
                throw new InvalidOperationException("CurseForge API key is not configured.");

            var results = new List<UpdateableMod>();

            // CurseForge uses MurmurHash2 fingerprints for file identification
            // POST /v1/fingerprints with array of fingerprints
            var fingerprints = new Dictionary<uint, string>(); // fingerprint → filePath
            foreach (var file in jarFiles)
            {
                var fp = ComputeCurseForgeFingerprint(file);
                fingerprints[fp] = file;
            }

            // Batch fingerprint lookup
            var matchMap = new Dictionary<string, JObject>(); // filePath → match object
            try
            {
                var payload = new JObject
                {
                    ["fingerprints"] = new JArray(fingerprints.Keys.Select(f => (long)f).ToArray())
                };
                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0");
                http.DefaultRequestHeaders.Add("x-api-key", SettingsManager.CurseForgeApiKey);

                var resp = await http.PostAsync($"https://api.curseforge.com/v1/fingerprints/{MinecraftGameId}", content);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var obj = JObject.Parse(json);
                    var exactMatches = obj["data"]?["exactMatches"] as JArray;
                    if (exactMatches != null)
                    {
                        foreach (JObject match in exactMatches)
                        {
                            var fp = match["file"]?["fileFingerprint"]?.Value<uint>() ?? 0;
                            if (fingerprints.TryGetValue(fp, out var path))
                                matchMap[path] = match;
                        }
                    }
                }
            }
            catch { /* fingerprint lookup failed */ }

            progress?.Report(0.3);

            int done = 0;

            foreach (var file in jarFiles)
            {
                var fileName = Path.GetFileName(file);
                var mod = new UpdateableMod
                {
                    CurrentFileName = fileName,
                    Source = "curseforge"
                };

                try
                {
                    int modId = 0;
                    string currentFileId = "";

                    if (matchMap.TryGetValue(file, out var match))
                    {
                        var fileObj = match["file"] as JObject;
                        modId = fileObj?["modId"]?.Value<int>() ?? 0;
                        currentFileId = fileObj?["id"]?.ToString() ?? "";
                        mod.CurrentVersion = fileObj?["displayName"]?.ToString() ?? fileObj?["fileName"]?.ToString() ?? "";
                    }

                    // Fallback: guess slug from filename and search
                    if (modId == 0)
                    {
                        var guessedSlug = GuessSlugFromFileName(fileName);
                        if (guessedSlug != null)
                        {
                            try
                            {
                                var http = new HttpClient();
                                http.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0");
                                http.DefaultRequestHeaders.Add("x-api-key", SettingsManager.CurseForgeApiKey);
                                var searchUrl = $"https://api.curseforge.com/v1/mods/search?gameId={MinecraftGameId}&slug={Uri.EscapeDataString(guessedSlug)}&pageSize=5";
                                var searchJson = await http.GetStringAsync(searchUrl);
                                var searchObj = JObject.Parse(searchJson);
                                var data = searchObj["data"] as JArray;
                                if (data != null && data.Count > 0)
                                {
                                    // Prefer exact slug match
                                    foreach (JObject item in data)
                                    {
                                        if ((item["slug"]?.ToString() ?? "").Equals(guessedSlug, StringComparison.OrdinalIgnoreCase))
                                        {
                                            modId = item["id"]?.Value<int>() ?? 0;
                                            mod.ProjectTitle = item["name"]?.ToString() ?? guessedSlug;
                                            mod.ProjectSlug = item["slug"]?.ToString() ?? guessedSlug;
                                            break;
                                        }
                                    }
                                    if (modId == 0)
                                    {
                                        var first = (JObject)data[0];
                                        modId = first["id"]?.Value<int>() ?? 0;
                                        mod.ProjectTitle = first["name"]?.ToString() ?? guessedSlug;
                                        mod.ProjectSlug = first["slug"]?.ToString() ?? guessedSlug;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    if (modId == 0)
                    {
                        mod.NotFound = true;
                        mod.StatusMessage = "Could not identify mod";
                        results.Add(mod);
                        done++;
                        progress?.Report(0.3 + 0.7 * done / jarFiles.Count);
                        continue;
                    }

                    mod.ProjectId = modId.ToString();

                    // Fetch mod info if we don't have title
                    if (string.IsNullOrEmpty(mod.ProjectTitle))
                    {
                        try
                        {
                            var http = new HttpClient();
                            http.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0");
                            http.DefaultRequestHeaders.Add("x-api-key", SettingsManager.CurseForgeApiKey);
                            var modJson = await http.GetStringAsync($"https://api.curseforge.com/v1/mods/{modId}");
                            var modObj = JObject.Parse(modJson);
                            var modData = modObj["data"] as JObject;
                            if (modData != null)
                            {
                                mod.ProjectTitle = modData["name"]?.ToString() ?? modId.ToString();
                                mod.ProjectSlug = modData["slug"]?.ToString() ?? modId.ToString();
                            }
                        }
                        catch
                        {
                            mod.ProjectTitle = modId.ToString();
                        }
                    }

                    // Fetch latest file with version + loader filter
                    var filesUrl = $"https://api.curseforge.com/v1/mods/{modId}/files?pageSize=10";
                    if (!string.IsNullOrEmpty(mcVersion))
                        filesUrl += $"&gameVersion={Uri.EscapeDataString(mcVersion)}";

                    var loaderTypeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "forge", 1 }, { "fabric", 4 }, { "quilt", 5 }, { "neoforge", 6 }
                    };
                    if (!string.IsNullOrEmpty(loader) && loaderTypeMap.TryGetValue(loader, out var loaderType))
                        filesUrl += $"&modLoaderType={loaderType}";

                    var filesHttp = new HttpClient();
                    filesHttp.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0");
                    filesHttp.DefaultRequestHeaders.Add("x-api-key", SettingsManager.CurseForgeApiKey);
                    var filesJson = await filesHttp.GetStringAsync(filesUrl);
                    var filesObj = JObject.Parse(filesJson);
                    var filesArr = filesObj["data"] as JArray;

                    if (filesArr == null || filesArr.Count == 0)
                    {
                        mod.HasUpdate = false;
                        mod.StatusMessage = "✓ Up to date (no versions for this MC version)";
                        results.Add(mod);
                        done++;
                        progress?.Report(0.3 + 0.7 * done / jarFiles.Count);
                        continue;
                    }

                    // Pick latest (first by date, preferring Release type)
                    JObject latestFile = null;
                    int bestRelease = int.MaxValue;
                    DateTime bestDate = DateTime.MinValue;
                    foreach (JObject f in filesArr)
                    {
                        var rt = f["releaseType"]?.Value<int>() ?? 99;
                        DateTime.TryParse(f["fileDate"]?.ToString(), out var date);
                        if (rt < bestRelease || (rt == bestRelease && date > bestDate))
                        {
                            latestFile = f;
                            bestRelease = rt;
                            bestDate = date;
                        }
                    }
                    latestFile ??= (JObject)filesArr[0];

                    var latestFileId = latestFile["id"]?.ToString() ?? "";
                    var latestFileName2 = latestFile["fileName"]?.ToString() ?? "";
                    var latestDownloadUrl = latestFile["downloadUrl"]?.ToString() ?? "";
                    var latestDisplayName = latestFile["displayName"]?.ToString() ?? latestFileName2;

                    // CDN fallback for mods with disabled API downloads
                    if (string.IsNullOrEmpty(latestDownloadUrl))
                    {
                        var idStr = latestFileId;
                        if (idStr.Length >= 4)
                        {
                            var part1 = idStr.Substring(0, 4);
                            var part2 = idStr.Substring(4).TrimStart('0');
                            if (string.IsNullOrEmpty(part2)) part2 = "0";
                            latestDownloadUrl = $"https://edge.forgecdn.net/files/{part1}/{part2}/{Uri.EscapeDataString(latestFileName2)}";
                        }
                    }

                    bool isNewer = !string.IsNullOrEmpty(latestFileId) && latestFileId != currentFileId;
                    // Also check by filename if we couldn't get current file id
                    if (string.IsNullOrEmpty(currentFileId) && latestFileName2 == fileName)
                        isNewer = false;

                    mod.LatestVersion = latestDisplayName;
                    mod.LatestFileName = latestFileName2;
                    mod.LatestDownloadUrl = latestDownloadUrl;
                    mod.HasUpdate = isNewer;
                    mod.Selected = isNewer;

                    if (isNewer)
                        mod.StatusMessage = $"⬆ Update available: {latestDisplayName}";
                    else
                        mod.StatusMessage = "✓ Up to date";
                }
                catch (Exception ex)
                {
                    mod.NotFound = true;
                    mod.StatusMessage = $"Error: {ex.Message}";
                }

                results.Add(mod);
                done++;
                progress?.Report(0.3 + 0.7 * done / jarFiles.Count);
            }

            return results;
        }

        // ======================== Helpers ========================

        private static string ComputeSha1(string filePath)
        {
            using var sha1 = SHA1.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha1.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// CurseForge fingerprint: MurmurHash2 of the file bytes after stripping
        /// whitespace characters (0x09, 0x0A, 0x0D, 0x20).
        /// </summary>
        private static uint ComputeCurseForgeFingerprint(string filePath)
        {
            var rawBytes = File.ReadAllBytes(filePath);
            // Strip whitespace bytes as CurseForge does
            var filtered = rawBytes.Where(b => b != 9 && b != 10 && b != 13 && b != 32).ToArray();
            return MurmurHash2(filtered, (uint)filtered.Length, 1);
        }

        /// <summary>
        /// MurmurHash2 implementation matching CurseForge's fingerprint algorithm.
        /// </summary>
        private static uint MurmurHash2(byte[] data, uint length, uint seed)
        {
            const uint m = 0x5bd1e995;
            const int r = 24;

            uint h = seed ^ length;
            int currentIndex = 0;

            while (length >= 4)
            {
                uint k = (uint)(data[currentIndex] |
                                (data[currentIndex + 1] << 8) |
                                (data[currentIndex + 2] << 16) |
                                (data[currentIndex + 3] << 24));

                k *= m;
                k ^= k >> r;
                k *= m;

                h *= m;
                h ^= k;

                currentIndex += 4;
                length -= 4;
            }

            switch (length)
            {
                case 3: h ^= (uint)(data[currentIndex + 2] << 16); goto case 2;
                case 2: h ^= (uint)(data[currentIndex + 1] << 8); goto case 1;
                case 1: h ^= data[currentIndex]; h *= m; break;
            }

            h ^= h >> 13;
            h *= m;
            h ^= h >> 15;

            return h;
        }

        /// <summary>
        /// Guess a Modrinth/CurseForge slug from a jar filename.
        /// E.g., "sodium-fabric-0.6.0+mc1.21.4.jar" → "sodium"
        ///        "jei-1.21.1-neoforge-19.21.0.247.jar" → "jei"
        /// </summary>
        private static string GuessSlugFromFileName(string fileName)
        {
            // Remove .jar extension
            var name = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(name)) return null;

            // Common patterns: "modname-loader-version", "modname-version"
            // Strip version-like suffixes: anything after first digit sequence preceded by a separator
            // Also strip common suffixes like -fabric, -forge, -neoforge, -quilt, -mc1.x.x

            // Try splitting on common separators
            var parts = name.Split(new[] { '-', '_', '+' });
            if (parts.Length == 0) return null;

            // Take leading parts that don't look like versions or loaders
            var slugParts = new List<string>();
            var loaderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "fabric", "forge", "neoforge", "quilt", "bukkit", "spigot", "paper" };

            foreach (var part in parts)
            {
                // Stop if this part looks like a version number (starts with digit or is "mc")
                if (part.Length > 0 && char.IsDigit(part[0])) break;
                if (part.StartsWith("mc", StringComparison.OrdinalIgnoreCase) && part.Length > 2 && char.IsDigit(part[2])) break;
                if (part.Equals("v", StringComparison.OrdinalIgnoreCase)) break;

                // Skip loader names
                if (loaderNames.Contains(part)) continue;

                slugParts.Add(part);
            }

            if (slugParts.Count == 0) return null;
            return string.Join("-", slugParts).ToLowerInvariant();
        }
    }
}

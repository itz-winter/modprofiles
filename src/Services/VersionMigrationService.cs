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
    /// Identifies active mods via hash/fingerprint lookup, then checks whether each has
    /// a compatible version for a different (target) Minecraft version and loader.
    /// Reports dependencies and incompatibilities.
    /// </summary>
    public class VersionMigrationService
    {
        private readonly HttpClient _modrinthHttp;
        private readonly CurseForgeService _curseForge;

        public VersionMigrationService(CurseForgeService curseForge = null)
        {
            _modrinthHttp = new HttpClient();
            _modrinthHttp.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0 (github.com)");
            _curseForge = curseForge;
        }

        /// <summary>
        /// Scan all jars in <paramref name="modsDir"/> and check whether each has a version
        /// available for the <paramref name="targetMcVersion"/> and <paramref name="loader"/>.
        /// </summary>
        public async Task<List<MigratableMod>> CheckMigrationAsync(
            string modsDir,
            string source,
            string targetMcVersion,
            string loader,
            IProgress<double> progress = null)
        {
            var jarFiles = Directory.GetFiles(modsDir, "*.jar")
                .Where(f => !Path.GetFileName(f).StartsWith("."))
                .ToList();

            if (jarFiles.Count == 0)
                return new List<MigratableMod>();

            if (source.Equals("curseforge", StringComparison.OrdinalIgnoreCase))
                return await CheckCurseForgeMigrationAsync(jarFiles, targetMcVersion, loader, progress);
            else
                return await CheckModrinthMigrationAsync(jarFiles, targetMcVersion, loader, progress);
        }

        // ======================== Modrinth ========================

        private async Task<List<MigratableMod>> CheckModrinthMigrationAsync(
            List<string> jarFiles, string targetMcVersion, string loader, IProgress<double> progress)
        {
            var results = new List<MigratableMod>();

            // Step 1: Hash all jars → identify via POST /v2/version_files
            var hashMap = new Dictionary<string, string>(); // sha1 → filePath
            foreach (var file in jarFiles)
            {
                var hash = ComputeSha1(file);
                hashMap[hash] = file;
            }

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
                        identified[prop.Name] = (JObject)prop.Value;
                }
            }
            catch { }

            progress?.Report(0.2);

            // Step 2: For each jar, resolve project → look for a version matching targetMcVersion
            int done = 0;
            foreach (var file in jarFiles)
            {
                var fileName = Path.GetFileName(file);
                var hash = hashMap.FirstOrDefault(kv => kv.Value == file).Key;
                var mod = new MigratableMod { CurrentFileName = fileName, Source = "modrinth" };

                try
                {
                    string projectId = null;

                    // Try hash-based identification
                    if (hash != null && identified.TryGetValue(hash, out var currentVersion))
                    {
                        projectId = currentVersion["project_id"]?.ToString();
                        var gameVers = currentVersion["game_versions"] as JArray;
                        if (gameVers != null)
                            mod.CurrentGameVersion = string.Join(", ", gameVers.Select(v => v.ToString()));
                    }

                    // Fallback: slug guessing
                    if (projectId == null)
                    {
                        var slug = GuessSlugFromFileName(fileName);
                        if (slug != null)
                        {
                            try
                            {
                                var projJson = await _modrinthHttp.GetStringAsync(
                                    $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(slug)}");
                                var projObj = JObject.Parse(projJson);
                                projectId = projObj["id"]?.ToString();
                                mod.ProjectTitle = projObj["title"]?.ToString() ?? slug;
                                mod.ProjectSlug = projObj["slug"]?.ToString() ?? slug;
                            }
                            catch { }
                        }
                    }

                    if (projectId == null)
                    {
                        mod.NotFound = true;
                        mod.StatusMessage = "❓ Could not identify mod";
                        results.Add(mod);
                        done++;
                        progress?.Report(0.2 + 0.8 * done / jarFiles.Count);
                        continue;
                    }

                    mod.ProjectId = projectId;

                    // Fetch project info if missing
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

                    // Fetch versions for target MC version + loader
                    var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version";
                    var queryParts = new List<string>();
                    if (!string.IsNullOrEmpty(loader))
                        queryParts.Add($"loaders=[\"{loader}\"]");
                    if (!string.IsNullOrEmpty(targetMcVersion))
                        queryParts.Add($"game_versions=[\"{targetMcVersion}\"]");
                    if (queryParts.Count > 0)
                        url += "?" + string.Join("&", queryParts);

                    var versionsJson = await _modrinthHttp.GetStringAsync(url);
                    var versions = JArray.Parse(versionsJson);

                    if (versions.Count > 0)
                    {
                        var best = (JObject)versions[0];
                        var bestFile = PickPrimaryFile(best["files"] as JArray);

                        mod.Available = true;
                        mod.Selected = true;
                        mod.TargetVersionNumber = best["version_number"]?.ToString() ?? "";
                        mod.TargetFileName = bestFile?["filename"]?.ToString() ?? "";
                        mod.TargetDownloadUrl = bestFile?["url"]?.ToString() ?? "";

                        var gv = best["game_versions"] as JArray;
                        if (gv != null) mod.TargetGameVersions = string.Join(", ", gv.Select(v => v.ToString()));
                        var lo = best["loaders"] as JArray;
                        if (lo != null) mod.TargetLoaders = string.Join(", ", lo.Select(l => l.ToString()));

                        // Check dependencies
                        await ResolveModrinthDependencies(best, mod);

                        mod.StatusMessage = $"✓ Available: {mod.TargetVersionNumber}";
                    }
                    else
                    {
                        mod.Incompatible = true;
                        mod.Selected = false;
                        mod.StatusMessage = $"✗ No version for {targetMcVersion}";
                    }
                }
                catch (Exception ex)
                {
                    mod.NotFound = true;
                    mod.StatusMessage = $"Error: {ex.Message}";
                }

                results.Add(mod);
                done++;
                progress?.Report(0.2 + 0.8 * done / jarFiles.Count);
            }

            return results;
        }

        /// <summary>
        /// Resolves required dependencies from a Modrinth version object.
        /// </summary>
        private async Task ResolveModrinthDependencies(JObject versionObj, MigratableMod mod)
        {
            var deps = versionObj["dependencies"] as JArray;
            if (deps == null || deps.Count == 0) return;

            foreach (JObject dep in deps)
            {
                var depType = dep["dependency_type"]?.ToString();
                if (depType != "required") continue;

                var depProjectId = dep["project_id"]?.ToString();
                if (string.IsNullOrEmpty(depProjectId)) continue;

                try
                {
                    var projJson = await _modrinthHttp.GetStringAsync(
                        $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(depProjectId)}");
                    var projObj = JObject.Parse(projJson);
                    var title = projObj["title"]?.ToString() ?? depProjectId;
                    mod.Dependencies.Add(title);
                }
                catch
                {
                    mod.Dependencies.Add(depProjectId);
                }
            }
        }

        // ======================== CurseForge ========================

        private async Task<List<MigratableMod>> CheckCurseForgeMigrationAsync(
            List<string> jarFiles, string targetMcVersion, string loader, IProgress<double> progress)
        {
            const int MinecraftGameId = 432;

            if (_curseForge == null)
                throw new InvalidOperationException("CurseForge API key is not configured.");

            var results = new List<MigratableMod>();

            // Step 1: Fingerprint all jars
            var fingerprints = new Dictionary<uint, string>(); // fp → filePath
            foreach (var file in jarFiles)
            {
                var fp = ComputeCurseForgeFingerprint(file);
                fingerprints[fp] = file;
            }

            // Batch fingerprint lookup
            var matchMap = new Dictionary<string, JObject>(); // filePath → match
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
            catch { }

            progress?.Report(0.2);

            var loaderTypeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "forge", 1 }, { "fabric", 4 }, { "quilt", 5 }, { "neoforge", 6 }
            };

            int done = 0;
            foreach (var file in jarFiles)
            {
                var fileName = Path.GetFileName(file);
                var mod = new MigratableMod { CurrentFileName = fileName, Source = "curseforge" };

                try
                {
                    int modId = 0;

                    if (matchMap.TryGetValue(file, out var match))
                    {
                        var fileObj = match["file"] as JObject;
                        modId = fileObj?["modId"]?.Value<int>() ?? 0;
                        var gameVers = fileObj?["gameVersions"] as JArray;
                        if (gameVers != null)
                            mod.CurrentGameVersion = string.Join(", ", gameVers.Select(v => v.ToString()));
                    }

                    // Fallback: slug guess → search
                    if (modId == 0)
                    {
                        var slug = GuessSlugFromFileName(fileName);
                        if (slug != null)
                        {
                            try
                            {
                                var http = new HttpClient();
                                http.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0");
                                http.DefaultRequestHeaders.Add("x-api-key", SettingsManager.CurseForgeApiKey);
                                var searchUrl = $"https://api.curseforge.com/v1/mods/search?gameId={MinecraftGameId}&slug={Uri.EscapeDataString(slug)}&pageSize=5";
                                var searchJson = await http.GetStringAsync(searchUrl);
                                var searchObj = JObject.Parse(searchJson);
                                var data = searchObj["data"] as JArray;
                                if (data != null && data.Count > 0)
                                {
                                    foreach (JObject item in data)
                                    {
                                        if ((item["slug"]?.ToString() ?? "").Equals(slug, StringComparison.OrdinalIgnoreCase))
                                        {
                                            modId = item["id"]?.Value<int>() ?? 0;
                                            mod.ProjectTitle = item["name"]?.ToString() ?? slug;
                                            mod.ProjectSlug = item["slug"]?.ToString() ?? slug;
                                            break;
                                        }
                                    }
                                    if (modId == 0)
                                    {
                                        var first = (JObject)data[0];
                                        modId = first["id"]?.Value<int>() ?? 0;
                                        mod.ProjectTitle = first["name"]?.ToString() ?? slug;
                                        mod.ProjectSlug = first["slug"]?.ToString() ?? slug;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    if (modId == 0)
                    {
                        mod.NotFound = true;
                        mod.StatusMessage = "❓ Could not identify mod";
                        results.Add(mod);
                        done++;
                        progress?.Report(0.2 + 0.8 * done / jarFiles.Count);
                        continue;
                    }

                    mod.ProjectId = modId.ToString();

                    // Fetch mod info if missing
                    if (string.IsNullOrEmpty(mod.ProjectTitle))
                    {
                        try
                        {
                            var http = new HttpClient();
                            http.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0");
                            http.DefaultRequestHeaders.Add("x-api-key", SettingsManager.CurseForgeApiKey);
                            var modJson = await http.GetStringAsync($"https://api.curseforge.com/v1/mods/{modId}");
                            var modData = JObject.Parse(modJson)["data"] as JObject;
                            if (modData != null)
                            {
                                mod.ProjectTitle = modData["name"]?.ToString() ?? modId.ToString();
                                mod.ProjectSlug = modData["slug"]?.ToString() ?? modId.ToString();
                            }
                        }
                        catch { mod.ProjectTitle = modId.ToString(); }
                    }

                    // Fetch files for target MC version + loader
                    var filesUrl = $"https://api.curseforge.com/v1/mods/{modId}/files?pageSize=10";
                    if (!string.IsNullOrEmpty(targetMcVersion))
                        filesUrl += $"&gameVersion={Uri.EscapeDataString(targetMcVersion)}";
                    if (!string.IsNullOrEmpty(loader) && loaderTypeMap.TryGetValue(loader, out var loaderType))
                        filesUrl += $"&modLoaderType={loaderType}";

                    var filesHttp = new HttpClient();
                    filesHttp.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0");
                    filesHttp.DefaultRequestHeaders.Add("x-api-key", SettingsManager.CurseForgeApiKey);
                    var filesJson = await filesHttp.GetStringAsync(filesUrl);
                    var filesArr = JObject.Parse(filesJson)["data"] as JArray;

                    if (filesArr != null && filesArr.Count > 0)
                    {
                        // Pick best file (prefer Release type, most recent)
                        JObject bestFile = null;
                        int bestRelease = int.MaxValue;
                        DateTime bestDate = DateTime.MinValue;
                        foreach (JObject f in filesArr)
                        {
                            var rt = f["releaseType"]?.Value<int>() ?? 99;
                            DateTime.TryParse(f["fileDate"]?.ToString(), out var date);
                            if (rt < bestRelease || (rt == bestRelease && date > bestDate))
                            {
                                bestFile = f;
                                bestRelease = rt;
                                bestDate = date;
                            }
                        }
                        bestFile ??= (JObject)filesArr[0];

                        var dlUrl = bestFile["downloadUrl"]?.ToString() ?? "";
                        var targetFileName = bestFile["fileName"]?.ToString() ?? "";

                        // CDN fallback
                        if (string.IsNullOrEmpty(dlUrl))
                        {
                            var idStr = bestFile["id"]?.ToString() ?? "";
                            if (idStr.Length >= 4)
                            {
                                var part1 = idStr.Substring(0, 4);
                                var part2 = idStr.Substring(4).TrimStart('0');
                                if (string.IsNullOrEmpty(part2)) part2 = "0";
                                dlUrl = $"https://edge.forgecdn.net/files/{part1}/{part2}/{Uri.EscapeDataString(targetFileName)}";
                            }
                        }

                        mod.Available = true;
                        mod.Selected = true;
                        mod.TargetFileName = targetFileName;
                        mod.TargetVersionNumber = bestFile["displayName"]?.ToString() ?? targetFileName;
                        mod.TargetDownloadUrl = dlUrl;

                        var gv = bestFile["gameVersions"] as JArray;
                        if (gv != null) mod.TargetGameVersions = string.Join(", ", gv.Select(v => v.ToString()));

                        // Check dependencies
                        ResolveCurseForgeDependencies(bestFile, mod);

                        mod.StatusMessage = $"✓ Available: {mod.TargetVersionNumber}";
                    }
                    else
                    {
                        mod.Incompatible = true;
                        mod.Selected = false;
                        mod.StatusMessage = $"✗ No version for {targetMcVersion}";
                    }
                }
                catch (Exception ex)
                {
                    mod.NotFound = true;
                    mod.StatusMessage = $"Error: {ex.Message}";
                }

                results.Add(mod);
                done++;
                progress?.Report(0.2 + 0.8 * done / jarFiles.Count);
            }

            return results;
        }

        private void ResolveCurseForgeDependencies(JObject fileObj, MigratableMod mod)
        {
            var deps = fileObj["dependencies"] as JArray;
            if (deps == null) return;

            foreach (JObject dep in deps)
            {
                // relationType 3 = required dependency
                var relationType = dep["relationType"]?.Value<int>() ?? 0;
                if (relationType != 3) continue;

                var depModId = dep["modId"]?.Value<int>() ?? 0;
                if (depModId > 0)
                    mod.Dependencies.Add($"CF#{depModId}");
            }
        }

        // ======================== Helpers (shared with ModUpdateChecker) ========================

        private static JObject PickPrimaryFile(JArray files)
        {
            if (files == null || files.Count == 0) return null;
            foreach (JObject f in files)
            {
                if (f["primary"]?.Value<bool>() == true) return f;
            }
            return (JObject)files[0];
        }

        private static string ComputeSha1(string filePath)
        {
            using var sha1 = SHA1.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha1.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static uint ComputeCurseForgeFingerprint(string filePath)
        {
            var rawBytes = File.ReadAllBytes(filePath);
            var filtered = rawBytes.Where(b => b != 9 && b != 10 && b != 13 && b != 32).ToArray();
            return MurmurHash2(filtered, (uint)filtered.Length, 1);
        }

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
                k *= m; k ^= k >> r; k *= m;
                h *= m; h ^= k;
                currentIndex += 4;
                length -= 4;
            }

            switch (length)
            {
                case 3: h ^= (uint)(data[currentIndex + 2] << 16); goto case 2;
                case 2: h ^= (uint)(data[currentIndex + 1] << 8); goto case 1;
                case 1: h ^= data[currentIndex]; h *= m; break;
            }

            h ^= h >> 13; h *= m; h ^= h >> 15;
            return h;
        }

        private static string GuessSlugFromFileName(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(name)) return null;

            var parts = name.Split(new[] { '-', '_', '+' });
            if (parts.Length == 0) return null;

            var slugParts = new List<string>();
            var loaderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "fabric", "forge", "neoforge", "quilt", "bukkit", "spigot", "paper" };

            foreach (var part in parts)
            {
                if (part.Length > 0 && char.IsDigit(part[0])) break;
                if (part.StartsWith("mc", StringComparison.OrdinalIgnoreCase) && part.Length > 2 && char.IsDigit(part[2])) break;
                if (part.Equals("v", StringComparison.OrdinalIgnoreCase)) break;
                if (loaderNames.Contains(part)) continue;
                slugParts.Add(part);
            }

            if (slugParts.Count == 0) return null;
            return string.Join("-", slugParts).ToLowerInvariant();
        }
    }
}

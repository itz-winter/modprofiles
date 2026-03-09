using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ModProfileSwitcher.Models;
using Newtonsoft.Json.Linq;

namespace ModProfileSwitcher.Services
{
    /// <summary>
    /// Resolves CurseForge mod URLs, slugs, and project IDs to download links
    /// using the CurseForge Core API (v1).
    /// Requires a valid API key — obtain one at https://console.curseforge.com/
    /// </summary>
    public class CurseForgeService
    {
        private const string BaseApi = "https://api.curseforge.com/v1";
        private const int MinecraftGameId = 432;

        // CurseForge class IDs for mod loaders
        private static readonly Dictionary<string, int> LoaderTypeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "forge",    1 },
            { "cauldron", 2 },
            { "liteloader", 3 },
            { "fabric",   4 },
            { "quilt",    5 },
            { "neoforge", 6 }
        };

        // Regex patterns for CurseForge URLs
        private static readonly Regex ProjectUrlRx = new Regex(
            @"curseforge\.com/minecraft/mc-mods/(?<slug>[A-Za-z0-9_-]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ProjectIdRx = new Regex(
            @"^(?<id>\d{4,})$",
            RegexOptions.Compiled);

        private readonly HttpClient _http;

        public CurseForgeService(string apiKey)
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0");
            _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        }

        /// <summary>
        /// Test the API key by making a lightweight call.
        /// Returns true if the key is valid.
        /// </summary>
        public async Task<bool> TestApiKeyAsync()
        {
            try
            {
                var resp = await _http.GetAsync($"{BaseApi}/games/{MinecraftGameId}");
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parse pasted text (URLs, slugs, project IDs — one per line) and resolve each
        /// to a downloadable mod file.
        /// </summary>
        public async Task<List<ResolvedMod>> ResolveAsync(string pastedText, string mcVersion = null, string loader = null)
        {
            if (string.IsNullOrWhiteSpace(pastedText))
                return new List<ResolvedMod>();

            var entries = new List<string>(); // slug or numeric id

            // Extract CurseForge URLs
            foreach (Match m in ProjectUrlRx.Matches(pastedText))
            {
                var slug = m.Groups["slug"].Value;
                if (!entries.Contains(slug, StringComparer.OrdinalIgnoreCase))
                    entries.Add(slug);
            }

            // Fallback: treat each non-empty line as a slug or numeric project id
            if (entries.Count == 0)
            {
                foreach (var line in pastedText.Split('\n'))
                {
                    var trimmed = line.Trim().Trim('/');
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    // Extract slug from full URL if present
                    var urlMatch = ProjectUrlRx.Match(trimmed);
                    if (urlMatch.Success)
                    {
                        var slug = urlMatch.Groups["slug"].Value;
                        if (!entries.Contains(slug, StringComparer.OrdinalIgnoreCase))
                            entries.Add(slug);
                    }
                    else if (Regex.IsMatch(trimmed, @"^[A-Za-z0-9_-]+$"))
                    {
                        entries.Add(trimmed);
                    }
                }
            }

            var results = new List<ResolvedMod>();
            foreach (var entry in entries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var resolved = await TryResolveAsync(entry, mcVersion, loader);
                if (resolved != null)
                    results.Add(resolved);
            }

            return results;
        }

        /// <summary>
        /// Resolve a single mod download URL.
        /// </summary>
        public async Task<ResolvedMod> ResolveSingleAsync(string urlOrSlug, string mcVersion, string loader)
        {
            // Extract slug from URL if needed
            var match = ProjectUrlRx.Match(urlOrSlug);
            var entry = match.Success ? match.Groups["slug"].Value : urlOrSlug.Trim().Trim('/');

            return await TryResolveAsync(entry, mcVersion, loader);
        }

        private async Task<ResolvedMod> TryResolveAsync(string slugOrId, string mcVersion, string loader)
        {
            try
            {
                // Step 1: Resolve slug → project ID via search
                int projectId;
                string projectTitle = slugOrId;
                string projectSlug = slugOrId;

                if (int.TryParse(slugOrId, out var numId))
                {
                    projectId = numId;
                    // Fetch project info for the title
                    try
                    {
                        var projJson = await _http.GetStringAsync($"{BaseApi}/mods/{projectId}");
                        var projObj = JObject.Parse(projJson);
                        var data = projObj["data"] as JObject;
                        if (data != null)
                        {
                            projectTitle = data["name"]?.ToString() ?? slugOrId;
                            projectSlug = data["slug"]?.ToString() ?? slugOrId;
                        }
                    }
                    catch { }
                }
                else
                {
                    // Search by slug
                    var searchResult = await SearchModAsync(slugOrId);
                    if (searchResult == null)
                    {
                        return new ResolvedMod
                        {
                            ProjectSlug = slugOrId,
                            ProjectTitle = slugOrId,
                            NotFound = true,
                            Selected = false,
                            Source = "curseforge"
                        };
                    }

                    projectId = searchResult.Value.id;
                    projectTitle = searchResult.Value.name;
                    projectSlug = searchResult.Value.slug;
                }

                // Step 2: Get files with version + loader filter
                var files = await GetModFilesAsync(projectId, mcVersion, loader);

                if (files != null && files.Count > 0)
                {
                    var best = PickBestFile(files);
                    return MakeResolvedMod(best, projectId, projectSlug, projectTitle, versionMismatch: false);
                }

                // Step 3: Fallback — get files without filters
                var allFiles = await GetModFilesAsync(projectId, null, null);
                if (allFiles == null || allFiles.Count == 0)
                {
                    return new ResolvedMod
                    {
                        ProjectId = projectId.ToString(),
                        ProjectSlug = projectSlug,
                        ProjectTitle = projectTitle,
                        NotFound = true,
                        Selected = false,
                        Source = "curseforge"
                    };
                }

                // Version mismatch
                var fallback = PickBestFile(allFiles);
                var mod = MakeResolvedMod(fallback, projectId, projectSlug, projectTitle, versionMismatch: true);
                mod.Selected = false;

                // Record actual game versions
                var gameVersions = fallback["gameVersions"] as JArray;
                if (gameVersions != null)
                    mod.ActualGameVersions = string.Join(", ", gameVersions.Select(v => v.ToString()));

                return mod;
            }
            catch
            {
                return new ResolvedMod
                {
                    ProjectSlug = slugOrId,
                    ProjectTitle = slugOrId,
                    NotFound = true,
                    Selected = false,
                    Source = "curseforge"
                };
            }
        }

        private async Task<(int id, string name, string slug)?> SearchModAsync(string slug)
        {
            try
            {
                var url = $"{BaseApi}/mods/search?gameId={MinecraftGameId}&slug={Uri.EscapeDataString(slug)}&pageSize=5";
                var json = await _http.GetStringAsync(url);
                var obj = JObject.Parse(json);
                var data = obj["data"] as JArray;
                if (data == null || data.Count == 0) return null;

                // Try exact slug match first
                foreach (JObject mod in data)
                {
                    var modSlug = mod["slug"]?.ToString() ?? "";
                    if (modSlug.Equals(slug, StringComparison.OrdinalIgnoreCase))
                    {
                        return (
                            mod["id"]?.Value<int>() ?? 0,
                            mod["name"]?.ToString() ?? slug,
                            modSlug
                        );
                    }
                }

                // Fallback: first result
                var first = (JObject)data[0];
                return (
                    first["id"]?.Value<int>() ?? 0,
                    first["name"]?.ToString() ?? slug,
                    first["slug"]?.ToString() ?? slug
                );
            }
            catch
            {
                return null;
            }
        }

        private async Task<JArray> GetModFilesAsync(int modId, string mcVersion, string loader)
        {
            try
            {
                var url = $"{BaseApi}/mods/{modId}/files?pageSize=50";

                if (!string.IsNullOrEmpty(mcVersion))
                    url += $"&gameVersion={Uri.EscapeDataString(mcVersion)}";

                if (!string.IsNullOrEmpty(loader) && LoaderTypeMap.TryGetValue(loader, out var loaderType))
                    url += $"&modLoaderType={loaderType}";

                var json = await _http.GetStringAsync(url);
                var obj = JObject.Parse(json);
                return obj["data"] as JArray;
            }
            catch
            {
                return null;
            }
        }

        private JObject PickBestFile(JArray files)
        {
            // Prefer release type 1 (Release), then 2 (Beta), then 3 (Alpha)
            // Sort by date descending
            JObject best = null;
            int bestRelease = int.MaxValue;
            DateTime bestDate = DateTime.MinValue;

            foreach (JObject f in files)
            {
                var releaseType = f["releaseType"]?.Value<int>() ?? 99;
                var dateStr = f["fileDate"]?.ToString();
                DateTime.TryParse(dateStr, out var date);

                if (releaseType < bestRelease ||
                    (releaseType == bestRelease && date > bestDate))
                {
                    best = f;
                    bestRelease = releaseType;
                    bestDate = date;
                }
            }

            return best ?? (JObject)files[0];
        }

        private ResolvedMod MakeResolvedMod(JObject file, int projectId, string slug, string title, bool versionMismatch)
        {
            var fileId = file["id"]?.Value<int>() ?? 0;
            var fileName = file["fileName"]?.ToString() ?? "";
            var downloadUrl = file["downloadUrl"]?.ToString() ?? "";

            // Some mods have null downloadUrl (distribution denied by author).
            // In that case, construct the CDN URL from the file ID.
            if (string.IsNullOrEmpty(downloadUrl) && fileId > 0)
            {
                // CurseForge CDN pattern: https://edge.forgecdn.net/files/{first4}/{last3}/{filename}
                var idStr = fileId.ToString();
                if (idStr.Length >= 4)
                {
                    var part1 = idStr.Substring(0, 4);
                    var part2 = idStr.Substring(4).TrimStart('0');
                    if (string.IsNullOrEmpty(part2)) part2 = "0";
                    downloadUrl = $"https://edge.forgecdn.net/files/{part1}/{part2}/{Uri.EscapeDataString(fileName)}";
                }
            }

            return new ResolvedMod
            {
                ProjectId = projectId.ToString(),
                ProjectSlug = slug,
                ProjectTitle = title,
                VersionId = fileId.ToString(),
                FileName = fileName,
                DownloadUrl = downloadUrl,
                VersionMismatch = versionMismatch,
                Selected = !versionMismatch,
                Source = "curseforge"
            };
        }
    }
}

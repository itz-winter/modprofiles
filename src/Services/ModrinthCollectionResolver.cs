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
    /// Parses pasted Modrinth collection text (URLs, slugs, collection page URLs, or JSON)
    /// and resolves each mod to a direct download URL via the Modrinth API.
    /// </summary>
    public class ModrinthCollectionResolver
    {
        private const string BaseApi = "https://api.modrinth.com/v2";
        private const string BaseApiV3 = "https://api.modrinth.com/v3";
        private readonly HttpClient _http = new HttpClient();

        // Matches a Modrinth collection URL like https://modrinth.com/collection/XXXXXX
        private static readonly Regex CollectionUrlRx = new Regex(
            @"modrinth\.com/collection/(?<id>[A-Za-z0-9]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches project page URLs: modrinth.com/mod/slug  or  /plugin/slug  or  /project/slug
        private static readonly Regex ProjectUrlRx = new Regex(
            @"modrinth\.com/(?:mod|plugin|project|datapack|shader|resourcepack)/(?<slug>[A-Za-z0-9_-]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public ModrinthCollectionResolver()
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "ModProfileSwitcher/1.0 (github.com)");
        }

        /// <summary>
        /// Takes pasted text and resolves mods to download links.
        /// Supports: collection URLs, project URLs, bare slugs (one per line), or exported JSON.
        /// </summary>
        public async Task<List<ResolvedMod>> ResolveAsync(string pastedText, string mcVersion = null, string loader = null)
        {
            if (string.IsNullOrWhiteSpace(pastedText))
                return new List<ResolvedMod>();

            var slugs = new List<string>();

            // --- 1. Check for a collection URL ---
            var collMatch = CollectionUrlRx.Match(pastedText);
            if (collMatch.Success)
            {
                var collId = collMatch.Groups["id"].Value;
                var collSlugs = await FetchCollectionProjectIdsAsync(collId);
                slugs.AddRange(collSlugs);
            }

            // --- 2. Extract explicit project URLs ---
            foreach (Match m in ProjectUrlRx.Matches(pastedText))
            {
                var slug = m.Groups["slug"].Value;
                if (!slugs.Contains(slug, StringComparer.OrdinalIgnoreCase))
                    slugs.Add(slug);
            }

            // --- 3. Try JSON array of slugs or objects ---
            if (pastedText.TrimStart().StartsWith("[") || pastedText.TrimStart().StartsWith("{"))
            {
                try
                {
                    var token = JToken.Parse(pastedText);
                    var extracted = ExtractSlugsFromJson(token);
                    foreach (var s in extracted)
                        if (!slugs.Contains(s, StringComparer.OrdinalIgnoreCase))
                            slugs.Add(s);
                }
                catch { /* not valid JSON, continue */ }
            }

            // --- 4. Fallback: treat each non-empty line as a potential slug ---
            if (slugs.Count == 0)
            {
                foreach (var line in pastedText.Split('\n'))
                {
                    var trimmed = line.Trim().Trim('/');
                    if (!string.IsNullOrEmpty(trimmed) && Regex.IsMatch(trimmed, @"^[A-Za-z0-9_-]+$"))
                        slugs.Add(trimmed);
                }
            }

            // --- Resolve each slug ---
            var results = new List<ResolvedMod>();
            foreach (var slug in slugs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var resolved = await TryResolveProjectAsync(slug, mcVersion, loader);
                if (resolved != null)
                    results.Add(resolved);
            }

            return results;
        }

        // --- Collection API ---

        private async Task<List<string>> FetchCollectionProjectIdsAsync(string collectionId)
        {
            var ids = new List<string>();
            try
            {
                // Collections are only available in the v3 API
                var json = await _http.GetStringAsync($"{BaseApiV3}/collection/{collectionId}");
                var obj = JObject.Parse(json);
                var projects = obj["projects"] as JArray;
                if (projects != null)
                {
                    foreach (var p in projects)
                        ids.Add(p.ToString());
                }
            }
            catch { }
            return ids;
        }

        // --- Project → Version → File resolution ---

        private async Task<ResolvedMod> TryResolveProjectAsync(string slugOrId, string mcVersion, string loader)
        {
            try
            {
                // First, fetch project info to get the title
                string projectTitle = slugOrId;
                try
                {
                    var projJson = await _http.GetStringAsync($"{BaseApi}/project/{Uri.EscapeDataString(slugOrId)}");
                    var projObj = JObject.Parse(projJson);
                    projectTitle = projObj["title"]?.ToString() ?? slugOrId;
                }
                catch { /* use slug as fallback */ }

                // Try with the requested version + loader filters
                var url = $"{BaseApi}/project/{Uri.EscapeDataString(slugOrId)}/version";
                var queryParts = new List<string>();
                if (!string.IsNullOrEmpty(loader))
                    queryParts.Add($"loaders=[\"{loader}\"]");
                if (!string.IsNullOrEmpty(mcVersion))
                    queryParts.Add($"game_versions=[\"{mcVersion}\"]");
                if (queryParts.Count > 0)
                    url += "?" + string.Join("&", queryParts);

                var txt = await _http.GetStringAsync(url);
                var arr = JArray.Parse(txt);

                if (arr.Count > 0)
                {
                    // Exact match found
                    var best = (JObject)arr[0];
                    var mod = ParseVersion(best, slugOrId);
                    if (mod != null)
                    {
                        mod.ProjectTitle = projectTitle;
                        mod.VersionMismatch = false;
                    }
                    return mod;
                }

                // No match for the requested version — try without filters
                txt = await _http.GetStringAsync($"{BaseApi}/project/{Uri.EscapeDataString(slugOrId)}/version");
                arr = JArray.Parse(txt);

                if (arr.Count == 0)
                {
                    // No versions at all
                    return new ResolvedMod
                    {
                        ProjectSlug = slugOrId,
                        ProjectTitle = projectTitle,
                        NotFound = true,
                        Selected = false
                    };
                }

                // Got a version, but it doesn't match the requested MC version/loader
                var fallback = (JObject)arr[0];
                var fallbackMod = ParseVersion(fallback, slugOrId);
                if (fallbackMod != null)
                {
                    fallbackMod.ProjectTitle = projectTitle;
                    fallbackMod.VersionMismatch = true;
                    fallbackMod.Selected = false; // uncheck by default

                    // Record what versions/loaders the fallback actually supports
                    var gameVers = fallback["game_versions"] as JArray;
                    if (gameVers != null)
                        fallbackMod.ActualGameVersions = string.Join(", ", gameVers.Select(v => v.ToString()));

                    var loaders = fallback["loaders"] as JArray;
                    if (loaders != null)
                        fallbackMod.ActualLoaders = string.Join(", ", loaders.Select(l => l.ToString()));
                }
                return fallbackMod;
            }
            catch
            {
                return new ResolvedMod
                {
                    ProjectSlug = slugOrId,
                    ProjectTitle = slugOrId,
                    NotFound = true,
                    Selected = false
                };
            }
        }

        private ResolvedMod ParseVersion(JObject versionJson, string slug)
        {
            var versionId = versionJson["id"]?.ToString() ?? "";
            var files = versionJson["files"] as JArray;
            if (files == null || files.Count == 0) return null;

            // Prefer the primary file
            JObject chosen = null;
            foreach (JObject f in files)
            {
                if (f["primary"]?.Value<bool>() == true) { chosen = f; break; }
            }
            chosen ??= (JObject)files[0];

            return new ResolvedMod
            {
                ProjectId = versionJson["project_id"]?.ToString() ?? "",
                ProjectSlug = slug,
                VersionId = versionId,
                FileName = chosen["filename"]?.ToString() ?? "",
                DownloadUrl = chosen["url"]?.ToString() ?? ""
            };
        }

        // --- JSON parsing helpers ---

        private List<string> ExtractSlugsFromJson(JToken token)
        {
            var slugs = new List<string>();
            if (token is JArray arr)
            {
                foreach (var item in arr)
                {
                    if (item.Type == JTokenType.String)
                        slugs.Add(item.ToString());
                    else if (item is JObject obj)
                    {
                        var val = obj["slug"]?.ToString() ?? obj["id"]?.ToString() ?? obj["project_id"]?.ToString();
                        if (!string.IsNullOrEmpty(val)) slugs.Add(val);
                    }
                }
            }
            else if (token is JObject obj)
            {
                // Maybe a manifest-like object with a "mods" array
                var mods = obj["mods"] as JArray;
                if (mods != null)
                    slugs.AddRange(ExtractSlugsFromJson(mods));
            }
            return slugs;
        }
    }
}

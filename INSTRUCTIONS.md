
Minecraft Mod Profile Maker & Switcher
=====================================

Overview
--------
This document describes a simple design and usage instructions for a "Mod Profile Maker & Switcher": a Windows desktop app built with the .NET Framework (WinForms or WPF) that helps you create, manage, switch, and download mod sets (profiles) for Minecraft.

Goals
-----
- Create and manage multiple mod profiles (sets of mod .jar files).
- Switch profiles by moving/copying files into your Minecraft mods folder.
- Download mods from external URLs (supports Modrinth-style links as a convenience).
- Provide an easy UI for creating/importing/exporting profiles and safe backups.

Where mods live (Windows)
-------------------------
Default Minecraft mods folder (for the logged-in user):

`%appdata%\.minecraft\mods`

For multi-version setups you might see subfolders inside `mods` (or you may manage by profile folders in the app). Example path shown in the screenshot: `C:\Users\<user>\AppData\Roaming\.minecraft\mods`.

Profile storage (recommended)
-----------------------------
Keep a dedicated folder for profiles in the app workspace, for example:

`C:\Users\<user>\Documents\MinecraftModProfiles\` (or `f:\programs\Minecraft\modprofiles` as in your workspace)

Structure per profile:

- `Profiles\<profile-name>\mods\` — contains the .jar files for that profile
- `Profiles\<profile-name>\manifest.json` — optional manifest listing mod IDs, sources, and versions

Key UI screens & controls
-------------------------
- Profiles list (left pane): create, rename, delete, import, export.
- Profile detail (main pane): show list of mods in the profile with checkboxes, remove/add buttons, drag-and-drop support to add jars.
- Version selector / Minecraft version tag: label the profile with the target Minecraft version and loader (Forge/Fabric).
- Download panel: input a mod URL or mod ID, choose loader and target mod version, queue downloads.
- Settings: Minecraft mods path, profiles path, max parallel downloads, retry limits.
- Actions: Apply profile (switch), Backup current mods, Restore backup, Open mods folder, Open profile folder.
- Logs & progress: show download progress bars, file copy progress, and a small activity log.

Switching a profile (recommended safe flow)
-----------------------------------------
1. Backup current mods: the app moves `*.jar` from `%appdata%\.minecraft\mods` to `Backups\timestamp` or zips them.
2. Clear the `mods` folder (or move to backup as above).
3. Copy the profile's `mods` folder contents into `%appdata%\.minecraft\mods`.
4. Report success and show any missing/warnings.

This avoids destructive deletes and allows quick restores.

Downloading mods (Modrinth guidance)
-----------------------------------
User-provided pattern to support for Modrinth-style links:

Original pattern (as given): `?loader={LOADER}&version={VERSION}}#download`

Normalized pattern:

`?loader=LOADER&version=VERSION#download`

Notes:
- Replace `LOADER` with the loader string (e.g., `fabric`, `forge`, `quilt`).
- Replace `VERSION` with the Minecraft version or the loader-version filter.
- Remove the curly braces and the explanatory text when constructing the real URL.
- The `#download` fragment is a browser fragment and usually ignored by the server; many download links use a redirect or an API to get the direct file URL. Using Modrinth's official API is more reliable.

Example (concrete):

`https://modrinth.com/mod/example-mod?loader=fabric&version=1.20.4#download`

Recommendation (best practice): use the Modrinth API (https://api.modrinth.com) to find the file object and the direct download URL. If you prefer the simple pattern above, the app can open that link in a web request and follow redirects to retrieve the final .jar.

Security and validation when downloading
---------------------------------------
- Only allow downloads to the app's profile folder or a temp folder; never run executables.
- Validate file extension (`.jar`) and optional size limits.
- Use HTTPS and follow redirects but warn on mixed-content or unknown host.
- Optionally store SHA256 checksums in the profile manifest and validate after download.

C# example: download a file with progress (target: .NET Framework 4.7.2+ or 4.8)
------------------------------------------------------------------------
This is a small example you can reuse in a WinForms/WPF app to download a file and report progress. Prefer using the Modrinth API for production-grade behavior; this sample demonstrates following redirects to the final file.

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

public static class Downloader
{
	private static readonly HttpClient client = new HttpClient();

	public static async Task DownloadFileAsync(string requestUrl, string destinationPath, IProgress<double> progress = null)
	{
		// requestUrl might be a Modrinth 'download' link that redirects to the actual file
		using (var response = await client.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead))
		{
			response.EnsureSuccessStatusCode();

			var contentLength = response.Content.Headers.ContentLength;
			using (var input = await response.Content.ReadAsStreamAsync())
			using (var output = File.Create(destinationPath))
			{
				var buffer = new byte[81920];
				long totalRead = 0;
				int read;
				while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
				{
					await output.WriteAsync(buffer, 0, read);
					totalRead += read;
					if (contentLength.HasValue && progress != null)
					{
						progress.Report((double)totalRead / contentLength.Value);
					}
				}
			}
		}
	}
}
```

Usage sample in a WinForms button click (pseudo):

```csharp
private async void btnDownload_Click(object sender, EventArgs e)
{
	string url = txtUrl.Text.Trim(); // e.g. the normalized Modrinth link
	string dest = Path.Combine(profileModsFolder, Path.GetFileName(new Uri(url).LocalPath));
	var progress = new Progress<double>(p => progressBar.Value = (int)(p * 100));
	try
	{
		await Downloader.DownloadFileAsync(url, dest, progress);
		MessageBox.Show("Download complete");
	}
	catch (Exception ex)
	{
		MessageBox.Show("Download failed: " + ex.Message);
	}
}
```

Notes on redirects and fragments
--------------------------------
- If the Modrinth link uses `#download`, the fragment is not sent to the server. If the link triggers a redirect in the browser, the HttpClient request will follow redirects by default. For more advanced usage, query the Modrinth API to get the file's direct URL.

PowerShell & Windows usage examples
----------------------------------
Open the Minecraft `mods` folder in Explorer from PowerShell:

```powershell
explorer $env:APPDATA\\.minecraft\\mods
```

Copy a profile into the mods folder (example):

```powershell
$profile = 'F:\programs\Minecraft\modprofiles\Profiles\MyProfile\mods'
$dest = "$env:APPDATA\\.minecraft\\mods"
Get-ChildItem -Path $dest -Filter *.jar | Move-Item -Destination "F:\programs\Minecraft\modprofiles\\Backups\\$(Get-Date -Format yyyyMMdd_HHmmss)" -Force
Copy-Item -Path "$profile\\*" -Destination $dest -Recurse -Force
```

This sequence (1) moves current mods to a timestamped backup, and (2) copies the profile mods in place.

Manifest file (optional)
------------------------
Profiles can include a `manifest.json` with structure similar to:

```json
{
  "name": "MyProfile",
  "version": "1.0",
  "minecraftVersion": "1.20.4",
  "loader": "fabric",
  "mods": [
	{ "id": "mod-slug-1", "source": "modrinth", "version": "x.y.z" },
	{ "id": "mod-slug-2", "source": "curseforge", "url": "https://..." }
  ]
}
```

The app can import this manifest to automatically queue downloads and populate the profile `mods` folder.

Edge cases & considerations
--------------------------
- Multiple Minecraft installations/users: allow users to set a custom `mods` path.
- Mod name collisions across versions: include `minecraftVersion` in profile metadata.
- Large downloads and slow connections: add configurable concurrency and retry logic.
- Partial downloads: write to a temp file and rename on success.

Next steps (implementation suggestions)
--------------------------------------
1. Choose UI toolkit: WinForms (fast to build) or WPF (more modern UI). Target .NET Framework 4.7.2 or 4.8 for wide compatibility.
2. Create a project skeleton with a ProfilesManager class, Downloader service, and a small persistence layer for manifests.
3. Implement a safe switch operation with backups and progress reporting.
4. Add Modrinth API integration later (recommended) for robust file discovery and direct download URLs.
5. Add unit tests for file operations and integration tests for small download flows using a local test server.

If you'd like, I can:
- scaffold a WinForms project structure (skeleton code) that implements profile listing, backup/switch, and the Downloader class; or
- create the `manifest.json` schema and a small console utility to test copy/download operations first.

Contact & feedback
------------------
Tell me which of the implementation options you'd prefer (WinForms skeleton, WPF skeleton, or a console prototype), and I'll scaffold the project with code examples and a minimal test. Also tell me whether you'd prefer the app to use the Modrinth API right away or start with the simpler URL-follow approach.

---
Generated on: 2026-02-12


Paste Modrinth collections (feature)
-----------------------------------
Goal
----
Allow users to paste a Modrinth collection (a collection page URL, exported JSON, or a pasted list of project URLs/slugs) into the app and get a resolved list of direct download links for the mods in that collection. This enables quick import of entire mod lists into a profile or a download queue.

User flow
---------
1. User opens the "Import collection" dialog in the app.
2. Paste area: user pastes any of the following:
   - A Modrinth collection page URL (e.g. https://modrinth.com/collection/abcdef)
   - A block of text containing project URLs or slugs (e.g. https://modrinth.com/mod/example-mod or example-mod)
   - A Modrinth exported JSON (if they used an export feature that provides JSON)
3. Options: select target Minecraft version and loader (fabric/forge/quilt) and whether to pick the latest compatible file or prompt per-mod.
4. The app resolves the best matching version/file per mod and shows a list of download links (and the option to queue downloads or add to a profile).

Implementation notes
--------------------
- The app should accept multiple input formats and attempt to extract project slugs or version IDs using regex.
- For reliability use the Modrinth public API (https://api.modrinth.com). The high-level algorithm:
  1. Extract project slugs or version IDs from pasted text.
  2. If you have project slugs, query the project's versions endpoint and pick the best-matching version for the selected loader and Minecraft version.
  3. From the chosen version JSON, read the `files` array and take the direct `url` for the file.
  4. Report the list of {project, version, filename, url} back to the UI.

Assumptions and caution
----------------------
- Modrinth's API rate limits may apply; implement simple retry/backoff and cache common queries during a session.
- The exact API shapes can change; code should tolerate missing fields and fall back to opening the project page in the browser for manual intervention.
- Prefer to query versions and choose the file that matches loader + game_version; if none are found, present options to the user.

C# example: resolve pasted Modrinth content to direct download links
----------------------------------------------------------------
The example below is a practical starting point you can include in the Downloader/service layer. It tries to be resilient: it extracts slugs and version ids from pasted text and queries the Modrinth API for direct file URLs. It uses Newtonsoft.Json (Json.NET) for JSON parsing. This example is written for .NET Framework 4.7.2+.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class ModrinthCollectionResolver
{
	private const string BaseApi = "https://api.modrinth.com/v2";
	private readonly HttpClient _http = new HttpClient();

	// Regex to capture version ids (UUID-like) and project slugs/URLs
	private static readonly Regex VersionIdRx = new Regex(@"[0-9a-fA-F-]{20,}", RegexOptions.Compiled);
	private static readonly Regex SlugRx = new Regex(@"modrinth\.com/(?:project|mod|plugin)/(?<slug>[^/?#\s]+)|(?<=\b)\w[\w-]+(?=\b)", RegexOptions.Compiled);

	public async Task<List<ResolvedMod>> ResolveAsync(string pastedText, string targetMcVersion = null, string loader = null)
	{
		var results = new List<ResolvedMod>();

		// 1) find explicit version ids
		foreach (Match m in VersionIdRx.Matches(pastedText))
		{
			var id = m.Value;
			var r = await TryResolveVersionIdAsync(id);
			if (r != null) results.Add(r);
		}

		// 2) find slugs and try to resolve to a compatible version
		var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Match m in SlugRx.Matches(pastedText))
		{
			var g = m.Groups["slug"];
			if (g.Success)
			{
				slugs.Add(g.Value);
			}
			else
			{
				// fallback: treat the whole match as a slug-like token
				var token = m.Value.Trim();
				if (!token.Contains(".")) slugs.Add(token);
			}
		}

		foreach (var slug in slugs)
		{
			var r = await TryResolveProjectSlugAsync(slug, targetMcVersion, loader);
			if (r != null) results.Add(r);
		}

		return results;
	}

	private async Task<ResolvedMod> TryResolveVersionIdAsync(string versionId)
	{
		try
		{
			var url = $"{BaseApi}/version/{versionId}";
			var txt = await _http.GetStringAsync(url);
			var j = JObject.Parse(txt);
			return ParseResolvedFromVersion(j);
		}
		catch
		{
			return null;
		}
	}

	private async Task<ResolvedMod> TryResolveProjectSlugAsync(string slug, string targetMcVersion, string loader)
	{
		try
		{
			// Query project versions
			var url = $"{BaseApi}/project/{Uri.EscapeDataString(slug)}/version";
			var txt = await _http.GetStringAsync(url);
			var arr = JArray.Parse(txt);

			// prefer version that matches loader + mc version
			JObject best = null;
			foreach (JObject v in arr)
			{
				var loaders = v["loaders"]?.ToObject<string[]>() ?? Array.Empty<string>();
				var gameVersions = v["game_versions"]?.ToObject<string[]>() ?? Array.Empty<string>();

				bool loaderMatch = string.IsNullOrEmpty(loader) || loaders.Contains(loader, StringComparer.OrdinalIgnoreCase);
				bool mcMatch = string.IsNullOrEmpty(targetMcVersion) || gameVersions.Contains(targetMcVersion, StringComparer.OrdinalIgnoreCase);

				if (loaderMatch && mcMatch)
				{
					best = v;
					break;
				}
				// fallback: pick the first if none match exactly
				if (best == null) best = v;
			}

			if (best != null) return ParseResolvedFromVersion(best);
		}
		catch
		{
			// ignore and continue
		}
		return null;
	}

	private ResolvedMod ParseResolvedFromVersion(JObject versionJson)
	{
		// versionJson should contain 'project_id', 'name', 'files' etc.
		var project = versionJson["project_id"]?.ToString() ?? versionJson["project_name"]?.ToString();
		var versionId = versionJson["id"]?.ToString();
		var files = versionJson["files"] as JArray;

		// choose the first file entry that has a url
		if (files != null && files.Count > 0)
		{
			var file = files[0] as JObject;
			var fileUrl = file?["url"]?.ToString();
			var filename = file?["filename"]?.ToString() ?? fileUrl?.Split('/').Last();
			return new ResolvedMod
			{
				ProjectId = project,
				VersionId = versionId,
				FileName = filename,
				DownloadUrl = fileUrl
			};
		}

		return null;
	}
}

public class ResolvedMod
{
	public string ProjectId { get; set; }
	public string VersionId { get; set; }
	public string FileName { get; set; }
	public string DownloadUrl { get; set; }
}
```

How the UI can use this
-----------------------
- Provide a small modal where the user pastes their collection text and chooses the target Minecraft version and loader.
- Call `ResolveAsync` and show a table of resolved mods with columns: Project, Version, Filename, URL, Status (Ready / Needs Selection).
- Allow the user to select which items to download and add to the profile.

If you want, I can now:
- Implement the resolver into a WinForms skeleton with the paste dialog wired up, or
- Create a console utility that accepts pasted text and prints a CSV of download links so you can test resolution quickly.


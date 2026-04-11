using System.Collections.Generic;

namespace ModProfileSwitcher.Models
{
    public class ModProfile
    {
        public string Name { get; set; } = "";
        public string MinecraftVersion { get; set; } = "";
        public string Loader { get; set; } = "fabric";
        public List<ModEntry> Mods { get; set; } = new List<ModEntry>();
    }

    public class ModEntry
    {
        public string Id { get; set; } = "";
        public string Source { get; set; } = "modrinth";
        public string Version { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Url { get; set; } = "";
    }

    public class ResolvedMod
    {
        public string ProjectId { get; set; } = "";
        public string ProjectSlug { get; set; } = "";
        public string ProjectTitle { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string FileName { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public bool Selected { get; set; } = true;

        /// <summary>
        /// Where this mod was resolved from: "modrinth" or "curseforge".
        /// </summary>
        public string Source { get; set; } = "modrinth";

        /// <summary>
        /// True if the mod doesn't support the requested MC version/loader combo
        /// and was resolved to a different version instead.
        /// </summary>
        public bool VersionMismatch { get; set; }

        /// <summary>
        /// The game versions this resolved version actually supports (comma-separated).
        /// Only meaningful when VersionMismatch is true.
        /// </summary>
        public string ActualGameVersions { get; set; } = "";

        /// <summary>
        /// The loader(s) this resolved version actually supports.
        /// </summary>
        public string ActualLoaders { get; set; } = "";

        /// <summary>
        /// True if the project couldn't be resolved at all (no versions found).
        /// </summary>
        public bool NotFound { get; set; }

        public override string ToString() => $"{ProjectSlug} — {FileName}";
    }

    /// <summary>
    /// Represents a mod that may have an update available.
    /// Used by the "Update Mods" feature.
    /// </summary>
    public class UpdateableMod
    {
        /// <summary>The current jar filename on disk.</summary>
        public string CurrentFileName { get; set; } = "";

        /// <summary>Human-readable project name.</summary>
        public string ProjectTitle { get; set; } = "";

        public string ProjectSlug { get; set; } = "";
        public string ProjectId { get; set; } = "";

        /// <summary>The version string of the currently installed file.</summary>
        public string CurrentVersion { get; set; } = "";

        /// <summary>The version string of the latest available file.</summary>
        public string LatestVersion { get; set; } = "";

        /// <summary>Filename of the latest available version.</summary>
        public string LatestFileName { get; set; } = "";

        /// <summary>Direct download URL for the latest version.</summary>
        public string LatestDownloadUrl { get; set; } = "";

        /// <summary>True if a newer version is available.</summary>
        public bool HasUpdate { get; set; }

        /// <summary>True if the mod couldn't be identified on the source platform.</summary>
        public bool NotFound { get; set; }

        /// <summary>User-selected for update (checkbox state).</summary>
        public bool Selected { get; set; }

        /// <summary>Source: "modrinth" or "curseforge".</summary>
        public string Source { get; set; } = "modrinth";

        /// <summary>Status message shown in the dialog.</summary>
        public string StatusMessage { get; set; } = "";
    }

    /// <summary>
    /// Represents a mod being migrated (upgraded/downgraded) to a different MC version.
    /// </summary>
    public class MigratableMod
    {
        public string CurrentFileName { get; set; } = "";
        public string ProjectTitle { get; set; } = "";
        public string ProjectSlug { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string Source { get; set; } = "modrinth";

        /// <summary>The current MC version the installed jar targets.</summary>
        public string CurrentGameVersion { get; set; } = "";

        /// <summary>Filename of the version available for the target MC version.</summary>
        public string TargetFileName { get; set; } = "";
        /// <summary>Version string for the target MC version file.</summary>
        public string TargetVersionNumber { get; set; } = "";
        /// <summary>Download URL for the target version file.</summary>
        public string TargetDownloadUrl { get; set; } = "";
        /// <summary>Game versions the target file supports (comma-separated).</summary>
        public string TargetGameVersions { get; set; } = "";
        /// <summary>Loaders the target file supports (comma-separated).</summary>
        public string TargetLoaders { get; set; } = "";

        /// <summary>True if a compatible version exists for the target MC version.</summary>
        public bool Available { get; set; }
        /// <summary>True if the mod could not be identified on the platform.</summary>
        public bool NotFound { get; set; }
        /// <summary>True if the mod exists on the platform but has no version for the target MC version/loader.</summary>
        public bool Incompatible { get; set; }

        /// <summary>Dependency slugs/titles that are required by this mod's target version.</summary>
        public List<string> Dependencies { get; set; } = new List<string>();

        /// <summary>User-selected for migration (checkbox state).</summary>
        public bool Selected { get; set; }

        /// <summary>Status text shown in the dialog.</summary>
        public string StatusMessage { get; set; } = "";
    }
}

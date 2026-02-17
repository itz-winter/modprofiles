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
}

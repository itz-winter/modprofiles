using System;
using System.IO;
using Newtonsoft.Json;

namespace ModProfileSwitcher.Services
{
    /// <summary>
    /// Persists user settings (e.g., CurseForge API key) to a JSON file in AppData.
    /// </summary>
    public static class SettingsManager
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModProfileSwitcher");

        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

        private static AppSettings _cached;

        public static AppSettings Load()
        {
            if (_cached != null) return _cached;

            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    _cached = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                    return _cached;
                }
            }
            catch { /* corrupt file — reset */ }

            _cached = new AppSettings();
            return _cached;
        }

        public static void Save(AppSettings settings)
        {
            _cached = settings;
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsFile, json);
            }
            catch { /* non-critical */ }
        }

        /// <summary>
        /// Quick accessor for the CurseForge API key.
        /// </summary>
        public static string CurseForgeApiKey
        {
            get => Load().CurseForgeApiKey ?? "";
            set
            {
                var s = Load();
                s.CurseForgeApiKey = value;
                Save(s);
            }
        }

        public static bool HasCurseForgeApiKey =>
            !string.IsNullOrWhiteSpace(Load().CurseForgeApiKey);
    }

    public class AppSettings
    {
        public string CurseForgeApiKey { get; set; } = "";
    }
}

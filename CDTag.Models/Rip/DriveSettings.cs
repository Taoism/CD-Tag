using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CDTag.Model.Rip
{
    /// <summary>Settings remembered for one drive model (Rip > Options).</summary>
    public class DriveSettings
    {
        /// <summary>Read offset typed for this drive; null = AccurateRip's drive database.</summary>
        public int? Offset { get; set; }
        /// <summary>Read speed in "x"; 0 = the drive's maximum.</summary>
        public int Speed { get; set; }
        public bool UseC2 { get; set; }
        public bool Overread { get; set; }
    }

    /// <summary>
    /// Per-drive settings in %LOCALAPPDATA%\CD-Tag\drives.json, keyed "VENDOR - PRODUCT" (as AccurateRip names drives).
    /// </summary>
    public static class DriveSettingsStore
    {
        private static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CD-Tag", "drives.json"); }
        }

        public static string Key(string vendor, string product)
        {
            return ((vendor ?? "").Trim() + " - " + (product ?? "").Trim()).ToUpperInvariant();
        }

        private static Dictionary<string, DriveSettings> LoadAll()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Dictionary<string, DriveSettings>>(File.ReadAllText(FilePath)) ?? new Dictionary<string, DriveSettings>();
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
            }
            return new Dictionary<string, DriveSettings>();
        }

        /// <summary>The saved settings for the drive, or null when none were saved.</summary>
        public static DriveSettings Load(string vendor, string product)
        {
            DriveSettings s;
            return LoadAll().TryGetValue(Key(vendor, product), out s) ? s : null;
        }

        public static void Save(string vendor, string product, DriveSettings settings)
        {
            try
            {
                Dictionary<string, DriveSettings> all = LoadAll();
                all[Key(vendor, product)] = settings;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}

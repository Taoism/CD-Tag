using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace CDTag.Common.Settings
{
    [DataContract]
    public class FavoritesData
    {
        [DataMember]
        public string[] Folders { get; set; }
    }

    /// <summary>Favorite folders of the Tag/Rename browser (favorites.json).</summary>
    public static class FavoritesStore
    {
        private const string FileName = "favorites.json";

        public static List<string> Load()
        {
            FavoritesData data;
            if (SettingsFile.TryLoad(FileName, out data) && data != null && data.Folders != null)
                return data.Folders.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new List<string>();
        }

        public static void Save(IEnumerable<string> folders)
        {
            SettingsFile.Save(FileName, new FavoritesData { Folders = folders.ToArray() });
        }
    }
}

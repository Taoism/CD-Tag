using System;
using System.IO;
using System.Text;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Hash;
using CDTag.Common.Json;
using CDTag.Common.Model;

namespace CDTag.Model.Profile
{
    public partial class UserProfile : ModelBase<UserProfile>
    {
        private static readonly IPathService _pathService;

        static UserProfile()
        {
            _pathService = IoC.Resolve<IPathService>();
        }

        public static UserProfile Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException("path");
            if (!File.Exists(path))
                throw new FileNotFoundException(string.Format("'{0}' not found.", path), path);

            if (LegacyProfile.IsLegacyFile(path))
            {
                // A CD-Tag 2.x profile copied into the Profiles folder: convert it in place,
                // keeping the original as name.cdtag2.ini.
                string name = Path.GetFileNameWithoutExtension(path);
                string directory = Path.GetDirectoryName(path);
                string backup = Path.Combine(directory, name + LegacyProfile.BackupExtension);
                if (!File.Exists(backup))
                    File.Copy(path, backup);
                UserProfile converted = LegacyProfile.Import(backup, directory, name);
                converted.LastModified = File.GetLastWriteTime(path);
                return converted;
            }

            string json = File.ReadAllText(path);
            UserProfile profile = JsonSerializer.ReadObject<UserProfile>(json);
            profile.ProfileName = Path.GetFileNameWithoutExtension(path);
            profile.LastModified = File.GetLastWriteTime(path);

            // Profiles imported from CD-Tag 2.x before naming rules/release lists were converted:
            // convert again from the preserved original, keeping the choices made since.
            string original = Path.Combine(Path.GetDirectoryName(path), profile.ProfileName + LegacyProfile.BackupExtension);
            if (profile.NamingRules == null && File.Exists(original))
            {
                string fileTypes = profile.AudioFileTypes;
                UserProfile upgraded = LegacyProfile.Import(original, Path.GetDirectoryName(path), profile.ProfileName);
                if (upgraded.AudioFileTypes != fileTypes)
                {
                    upgraded.AudioFileTypes = fileTypes;
                    File.WriteAllText(path, JsonSerializer.SerializeObject(upgraded));
                }
                upgraded.LastModified = File.GetLastWriteTime(path);
                upgraded.UpdateModelHash();
                return upgraded;
            }

            profile.UpdateModelHash();

            return profile;
        }

        public static string GetSampleNFO()
        {
            return Properties.Resources.SampleNFO;
        }
    }
}

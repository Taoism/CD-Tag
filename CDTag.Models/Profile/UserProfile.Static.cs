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
                throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException(string.Format("'{0}' not found.", path), path);

            if (LegacyProfile.IsLegacyFile(path))
            {
                // A CD-Tag 2.x profile: read as it is, so CD-Tag 2.x can keep using the same file.
                UserProfile legacy = LegacyProfile.Read(path);
                legacy.LastModified = File.GetLastWriteTime(path);
                legacy.UpdateModelHash();
                return legacy;
            }

            string json = File.ReadAllText(path);
            UserProfile profile = JsonSerializer.ReadObject<UserProfile>(json);
            profile.ProfileName = Path.GetFileNameWithoutExtension(path);
            profile.LastModified = File.GetLastWriteTime(path);

            // Profiles imported from CD-Tag 2.x before naming rules/release lists were converted:
            // convert again from the preserved original, keeping the choices made since.
            string original = Path.Combine(Path.GetDirectoryName(path), profile.ProfileName + LegacyProfile.BackupExtension);
            if ((profile.NamingRules == null || profile.Tagging == null) && File.Exists(original))
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

using CDTag.Common;
using CDTag.Common.Model;

namespace CDTag.Model.Profile
{
    public class NamingFormat : ModelBase<NamingFormat>
    {
        public string Directory
        {
            get { return Get<string>(nameof(Directory)); }
            set { Set(nameof(Directory), value); }
        }

        public string AudioFile
        {
            get { return Get<string>(nameof(AudioFile)); }
            set { Set(nameof(AudioFile), value); }
        }

        public string CUE
        {
            get { return Get<string>(nameof(CUE)); }
            set { Set(nameof(CUE), value); }
        }

        public string Playlist
        {
            get { return Get<string>(nameof(Playlist)); }
            set { Set(nameof(Playlist), value); }
        }

        public string Checksum
        {
            get { return Get<string>(nameof(Checksum)); }
            set { Set(nameof(Checksum), value); }
        }

        public string NFO
        {
            get { return Get<string>(nameof(NFO)); }
            set { Set(nameof(NFO), value); }
        }

        public string Images
        {
            get { return Get<string>(nameof(Images)); }
            set { Set(nameof(Images), value); }
        }

        public string EACLog
        {
            get { return Get<string>(nameof(EACLog)); }
            set { Set(nameof(EACLog), value); }
        }
    }
}

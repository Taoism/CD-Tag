using CDTag.Common;
using CDTag.Common.Model;

namespace CDTag.Model.Profile
{
    public class NamingFormatGroup : ModelBase<NamingFormatGroup>
    {
        public NamingFormatGroup()
        {
            SingleArtist = new NamingFormat();
            VariousArtists = new NamingFormat();
        }

        public NamingFormat SingleArtist
        {
            get { return Get<NamingFormat>(nameof(SingleArtist)); }
            set { Set(nameof(SingleArtist), value); }
        }

        public NamingFormat VariousArtists
        {
            get { return Get<NamingFormat>(nameof(VariousArtists)); }
            set { Set(nameof(VariousArtists), value); }
        }
    }
}

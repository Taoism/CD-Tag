using CDTag.Common;
using CDTag.Common.Model;

namespace CDTag.Model.Profile
{
    public class NFOOptions : ModelBase<NFOOptions>
    {
        public NFOOptions()
        {
            UseAverageBitrate = true;
            DecimalCharacter = ".";
        }

        public bool ShowReleaseScreen
        {
            get { return Get<bool>(nameof(ShowReleaseScreen)); }
            set { Set(nameof(ShowReleaseScreen), value); }
        }

        public string TemplatePath
        {
            get { return Get<string>(nameof(TemplatePath)); }
            set { Set(nameof(TemplatePath), value); }
        }

        public bool UseAverageBitrate
        {
            get { return Get<bool>(nameof(UseAverageBitrate)); }
            set { Set(nameof(UseAverageBitrate), value); }
        }

        public string SingleCDHeader
        {
            get { return Get<string>(nameof(SingleCDHeader)); }
            set { Set(nameof(SingleCDHeader), value); }
        }

        public string MultiCDHeader
        {
            get { return Get<string>(nameof(MultiCDHeader)); }
            set { Set(nameof(MultiCDHeader), value); }
        }

        public string TracklistFooter
        {
            get { return Get<string>(nameof(TracklistFooter)); }
            set { Set(nameof(TracklistFooter), value); }
        }

        public string ReleaseNotesHeader
        {
            get { return Get<string>(nameof(ReleaseNotesHeader)); }
            set { Set(nameof(ReleaseNotesHeader), value); }
        }

        public string ReleaseNotesFooter
        {
            get { return Get<string>(nameof(ReleaseNotesFooter)); }
            set { Set(nameof(ReleaseNotesFooter), value); }
        }

        public string DecimalCharacter
        {
            get { return Get<string>(nameof(DecimalCharacter)); }
            set { Set(nameof(DecimalCharacter), value); }
        }

        public bool AutomaticallyPreviewNFO
        {
            get { return Get<bool>(nameof(AutomaticallyPreviewNFO)); }
            set { Set(nameof(AutomaticallyPreviewNFO), value); }
        }
    }
}

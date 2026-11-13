using CDTag.Common;
using CDTag.Common.Model;

namespace CDTag.Model.Profile.NewProfile
{
    public class FormatItem : ModelBase<FormatItem>
    {
        public string FormatString
        {
            get { return Get<string>(nameof(FormatString)); }
            set { Set(nameof(FormatString), value); }
        }

        public string Result
        {
            get { return Get<string>(nameof(Result)); }
            set { Set(nameof(Result), value); }
        }

        public bool IsSelected
        {
            get { return Get<bool>(nameof(IsSelected)); }
            set { Set(nameof(IsSelected), value); }
        }
    }
}

using System;
using CDTag.Common.Model;
using CDTag.Model.Profile;

namespace CDTag.Model.Tag
{
    /// <summary>
    /// The "additional NFO fields" entered on the first page of Tag Album (CD-Tag 2.x release information):
    /// used in the NFO and, through &lt;ExtInfo&gt;, in folder and file names.
    /// </summary>
    public class ReleaseInfo : ModelBase<ReleaseInfo>
    {
        public ReleaseInfo()
        {
            RipDate = DateTime.Today.ToString("yyyy-MM-dd");
        }

        /// <summary>Starts from the profile's defaults (first entry of each list, ripper/supplier/... values).</summary>
        public static ReleaseInfo FromProfile(UserProfile profile)
        {
            var info = new ReleaseInfo();
            ReleaseInfoOptions options = profile != null ? profile.ReleaseInfo : null;
            if (options == null)
                return info;

            info.ReleaseType = First(options.ReleaseTypes);
            info.Source = First(options.Sources);
            info.Language = First(options.Languages);
            info.LimitedEdition = First(options.LimitedEdition);
            info.WhiteLabel = First(options.WhiteLabel);
            info.CleanExplicit = First(options.CleanExplicit);
            info.Ripper = options.Ripper ?? options.GroupInitials;
            info.Supplier = options.Supplier ?? options.GroupInitials;
            info.Encoder = options.Encoder;
            info.RippingTool = options.RippingTool;
            info.Comment = options.Comment;
            return info;
        }

        private static ReleaseListItem First(ReleaseListItem[] items)
        {
            return items != null && items.Length > 0 ? items[0] : null;
        }

        public string Label { get { return Get<string>(nameof(Label)); } set { Set(nameof(Label), value); } }

        /// <summary>yyyy-MM-dd.</summary>
        public string RipDate { get { return Get<string>(nameof(RipDate)); } set { Set(nameof(RipDate), value); } }

        /// <summary>Street/retail date, yyyy-MM-dd; unknown parts may be 00 (e.g. 2014-00-00).</summary>
        public string RetailDate { get { return Get<string>(nameof(RetailDate)); } set { Set(nameof(RetailDate), value); } }

        public string Comment { get { return Get<string>(nameof(Comment)); } set { Set(nameof(Comment), value); } }
        public string Url { get { return Get<string>(nameof(Url)); } set { Set(nameof(Url), value); } }
        public bool IsOst { get { return Get<bool>(nameof(IsOst)); } set { Set(nameof(IsOst), value); } }
        public bool IsInternal { get { return Get<bool>(nameof(IsInternal)); } set { Set(nameof(IsInternal), value); } }
        public string CatalogNumber { get { return Get<string>(nameof(CatalogNumber)); } set { Set(nameof(CatalogNumber), value); } }
        public string LiveDate { get { return Get<string>(nameof(LiveDate)); } set { Set(nameof(LiveDate), value); } }

        public ReleaseListItem ReleaseType { get { return Get<ReleaseListItem>(nameof(ReleaseType)); } set { Set(nameof(ReleaseType), value); } }
        public ReleaseListItem Source { get { return Get<ReleaseListItem>(nameof(Source)); } set { Set(nameof(Source), value); } }
        /// <summary>WEB FLAC bit depth / sample rate tag ("16BIT", "24BIT-96KHZ"); null = from the files (bit depth only).</summary>
        public string Quality { get { return Get<string>(nameof(Quality)); } set { Set(nameof(Quality), value); } }
        /// <summary>Fix tag for the release name: "" (none), "RERIP", "REPACK", "DIRFIX" or "NFOFIX".</summary>
        public string Fix { get { return Get<string>(nameof(Fix)); } set { Set(nameof(Fix), value); } }
        public ReleaseListItem Language { get { return Get<ReleaseListItem>(nameof(Language)); } set { Set(nameof(Language), value); } }
        public ReleaseListItem LimitedEdition { get { return Get<ReleaseListItem>(nameof(LimitedEdition)); } set { Set(nameof(LimitedEdition), value); } }
        public ReleaseListItem WhiteLabel { get { return Get<ReleaseListItem>(nameof(WhiteLabel)); } set { Set(nameof(WhiteLabel), value); } }
        public ReleaseListItem CleanExplicit { get { return Get<ReleaseListItem>(nameof(CleanExplicit)); } set { Set(nameof(CleanExplicit), value); } }

        /// <summary>Release notes (#Rnotes in the NFO).</summary>
        public string Notes { get { return Get<string>(nameof(Notes)); } set { Set(nameof(Notes), value); } }
        public string LongComment { get { return Get<string>(nameof(LongComment)); } set { Set(nameof(LongComment), value); } }

        public string Ripper { get { return Get<string>(nameof(Ripper)); } set { Set(nameof(Ripper), value); } }
        public string Supplier { get { return Get<string>(nameof(Supplier)); } set { Set(nameof(Supplier), value); } }
        public string Encoder { get { return Get<string>(nameof(Encoder)); } set { Set(nameof(Encoder), value); } }
        public string RippingTool { get { return Get<string>(nameof(RippingTool)); } set { Set(nameof(RippingTool), value); } }

        /// <summary>The four-digit year of <see cref="RetailDate"/>, or null.</summary>
        public string RetailYear
        {
            get
            {
                string date = (RetailDate ?? "").Trim();
                return date.Length >= 4 && char.IsDigit(date[0]) && date.Substring(0, 4) != "0000" ? date.Substring(0, 4) : null;
            }
        }
    }
}

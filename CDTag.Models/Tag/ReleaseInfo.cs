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

        public string Label { get { return Get<string>("Label"); } set { Set("Label", value); } }

        /// <summary>yyyy-MM-dd.</summary>
        public string RipDate { get { return Get<string>("RipDate"); } set { Set("RipDate", value); } }

        /// <summary>Street/retail date, yyyy-MM-dd; unknown parts may be 00 (e.g. 2014-00-00).</summary>
        public string RetailDate { get { return Get<string>("RetailDate"); } set { Set("RetailDate", value); } }

        public string Comment { get { return Get<string>("Comment"); } set { Set("Comment", value); } }
        public string Url { get { return Get<string>("Url"); } set { Set("Url", value); } }
        public bool IsOst { get { return Get<bool>("IsOst"); } set { Set("IsOst", value); } }
        public bool IsInternal { get { return Get<bool>("IsInternal"); } set { Set("IsInternal", value); } }
        public string CatalogNumber { get { return Get<string>("CatalogNumber"); } set { Set("CatalogNumber", value); } }
        public string LiveDate { get { return Get<string>("LiveDate"); } set { Set("LiveDate", value); } }

        public ReleaseListItem ReleaseType { get { return Get<ReleaseListItem>("ReleaseType"); } set { Set("ReleaseType", value); } }
        public ReleaseListItem Source { get { return Get<ReleaseListItem>("Source"); } set { Set("Source", value); } }
        /// <summary>WEB FLAC bit depth / sample rate tag ("16BIT", "24BIT-96KHZ"); null = from the files (bit depth only).</summary>
        public string Quality { get { return Get<string>("Quality"); } set { Set("Quality", value); } }
        /// <summary>Fix tag for the release name: "" (none), "RERIP", "REPACK", "DIRFIX" or "NFOFIX".</summary>
        public string Fix { get { return Get<string>("Fix"); } set { Set("Fix", value); } }
        public ReleaseListItem Language { get { return Get<ReleaseListItem>("Language"); } set { Set("Language", value); } }
        public ReleaseListItem LimitedEdition { get { return Get<ReleaseListItem>("LimitedEdition"); } set { Set("LimitedEdition", value); } }
        public ReleaseListItem WhiteLabel { get { return Get<ReleaseListItem>("WhiteLabel"); } set { Set("WhiteLabel", value); } }
        public ReleaseListItem CleanExplicit { get { return Get<ReleaseListItem>("CleanExplicit"); } set { Set("CleanExplicit", value); } }

        /// <summary>Release notes (#Rnotes in the NFO).</summary>
        public string Notes { get { return Get<string>("Notes"); } set { Set("Notes", value); } }
        public string LongComment { get { return Get<string>("LongComment"); } set { Set("LongComment", value); } }

        public string Ripper { get { return Get<string>("Ripper"); } set { Set("Ripper", value); } }
        public string Supplier { get { return Get<string>("Supplier"); } set { Set("Supplier", value); } }
        public string Encoder { get { return Get<string>("Encoder"); } set { Set("Encoder", value); } }
        public string RippingTool { get { return Get<string>("RippingTool"); } set { Set("RippingTool", value); } }

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

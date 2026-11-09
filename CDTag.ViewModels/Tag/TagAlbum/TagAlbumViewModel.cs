using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Events;
using CDTag.Common.Mvvm;
using CDTag.Model.Lookup;
using CDTag.Model.Profile;
using CDTag.Model.Tag;
using CDTag.Views.Interfaces.Tag.TagAlbum;
using IdSharp.Tagging.ID3v1;

namespace CDTag.ViewModels.Tag.TagAlbum
{
    /// <summary>
    /// Tag Album, CD-Tag 2.x style: page 0 = release information (NFO fields), page 1 = album and tracks with the
    /// folder/file names the profile produces. Preview NFO fills the profile's NFO template.
    /// </summary>
    public class TagAlbumViewModel : ViewModelBase<TagAlbumViewModel>, ITagAlbumViewModel
    {
        public const int ReleasePage = 0;
        public const int AlbumPage = 1;

        private static readonly ReleaseListItem[] DefaultSources = Items("CD", "CDS", "CDM", "CDR", "DVD", "VINYL", "LP", "VLS", "WEB", "WEB-FLAC");
        private static readonly ReleaseListItem[] DefaultTypes = new[] { new ReleaseListItem { Nfo = "Normal release", Dir = "" } }
            .Concat(Items("Advance", "Promo", "Retail", "Remastered", "Reissue", "Bootleg", "Limited Edition", "Deluxe Edition")).ToArray();
        private static readonly ReleaseListItem[] DefaultLanguages = { new ReleaseListItem { Nfo = "English", Dir = "" } };
        private static readonly ReleaseListItem[] NoYes = { new ReleaseListItem { Nfo = "No", Dir = "" }, new ReleaseListItem { Nfo = "Yes", Dir = "" } };
        private static readonly ReleaseListItem[] DefaultCleanExplicit = { new ReleaseListItem { Nfo = "Not designated", Dir = "" }, new ReleaseListItem { Nfo = "Clean", Dir = "" }, new ReleaseListItem { Nfo = "Explicit", Dir = "" } };

        private readonly DelegateCommand _finishCommand;
        private readonly DelegateCommand _previousCommand;
        private readonly DelegateCommand _nextCommand;
        private readonly DelegateCommand _previewNFOCommand;
        private bool _updatingNames;

        public TagAlbumViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            _finishCommand = new DelegateCommand(Finish, () => Album != null && Profile != null && Page == AlbumPage && !IsFinishing);
            _previewNFOCommand = new DelegateCommand(PreviewNFO, () => Album != null && Profile != null);
            _previousCommand = new DelegateCommand(() => Page = ReleasePage, () => Page == AlbumPage && HasReleasePage);
            _nextCommand = new DelegateCommand(() => Page = AlbumPage, () => Page == ReleasePage);
            LookupCommand = new DelegateCommand(Lookup, () => Album != null);

            RippingTools = new ObservableCollection<string>(new[] { "EAC", "XLD", "dBpoweramp", "CUERipper", "whipper", "CUETools", "foobar2000" });
            Encoders = new ObservableCollection<string>(new[] { "LAME 3.100 -V0", "LAME 3.100 -b 320", "FLAC 1.4.3", "FLAC 1.5.0" });

            EnhancedPropertyChanged += TagAlbumViewModel_EnhancedPropertyChanged;
        }

        private static ReleaseListItem[] Items(params string[] values)
        {
            return values.Select(v => new ReleaseListItem { Nfo = v, Dir = v }).ToArray();
        }

        private static string SourceName(ReleaseListItem item)
        {
            return ((item.Dir ?? "").Trim().Length > 0 ? item.Dir : item.Nfo ?? "").Trim();
        }

        /// <summary>
        /// FLAC albums from the web are WEB-FLAC, so plain WEB is dropped from the list when the profile has WEB-FLAC;
        /// MP3 albums from the web are WEB, so WEB-FLAC is dropped for them.
        /// </summary>
        public static ReleaseListItem[] SourcesFor(ReleaseListItem[] sources, Album album)
        {
            if (sources == null || album == null || album.Tracks.Count == 0)
                return sources;
            bool flac = album.Tracks.All(t => t.IsFlac);
            bool hasWebFlac = sources.Any(s => string.Equals(SourceName(s), "WEB-FLAC", StringComparison.OrdinalIgnoreCase));
            if (flac && hasWebFlac)
                return sources.Where(s => !string.Equals(SourceName(s), "WEB", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (!flac)
                return sources.Where(s => !string.Equals(SourceName(s), "WEB-FLAC", StringComparison.OrdinalIgnoreCase)).ToArray();
            return sources;
        }

        private void UpdateHasBitKhz()
        {
            string source = Release != null && Release.Source != null ? SourceName(Release.Source) : null;
            HasBitKhz = SceneTagging && SceneRules.HasBitKhzChoice(Album, source);
            if (!HasBitKhz && Release != null && !string.IsNullOrEmpty(Release.Quality))
                Release.Quality = "";
            // Read from the files: 16BIT, or 24BIT-96KHZ.
            if (HasBitKhz && Release != null && string.IsNullOrEmpty(Release.Quality))
                Release.Quality = SceneRules.FilesQualityTag(Album) ?? "";
        }

        private static readonly System.Text.RegularExpressions.Regex BitKhzPart = new System.Text.RegularExpressions.Regex(
            @"\b\d{2}BIT\b|\d\s*KHZ\b|\b(16|24|32)\s*[/-]\s*(44(\.1)?|48|88(\.2)?|96|176(\.4)?|192)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Fix tags have their own box (Fix), so they are not types.</summary>
        public static readonly string[] FixTags = { "RERIP", "REPACK", "DIRFIX", "NFOFIX" };

        /// <summary>
        /// The Type list without entries that carry a bit depth / sample rate ("16BIT", "24BIT/96KHZ", "Remastered-24BiT/96KhZ", "24/96")
        /// or a fix tag (REPACK, DIRFIX...): Bit/kHz and Fix have their own boxes.
        /// </summary>
        public static ReleaseListItem[] TypesFor(ReleaseListItem[] types)
        {
            if (types == null)
                return null;
            return types.Where(t => !BitKhzPart.IsMatch(t.Nfo ?? "") && !BitKhzPart.IsMatch(t.Dir ?? "")
                                    && !FixTags.Any(f => System.Text.RegularExpressions.Regex.IsMatch((t.Nfo ?? "") + " " + (t.Dir ?? ""), @"(?<![A-Za-z])" + f + @"(?![A-Za-z])",
                                                                                                      System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                        .ToArray();
        }

        /// <summary>Fix choices for the release name (scene ADDITIONAL tags); "" = none.</summary>
        public QualityChoice[] FixChoices { get; } = new[] { new QualityChoice { Value = "", Name = "None" } }
            .Concat(FixTags.Select(f => new QualityChoice { Value = f, Name = f })).ToArray();

        /// <summary>Scene-style tagging ("enabled" in options.json): Bit/kHz and Fix are shown.</summary>
        public bool SceneTagging { get { return CDTag.Common.Settings.AppOptions.Current.IsSceneTagging; } }

        /// <summary>FLAC files from a WEB or Blu-ray source: the Bit/kHz choice applies.</summary>
        public bool HasBitKhz { get { return Get<bool>("HasBitKhz"); } private set { Set("HasBitKhz", value); } }

        /// <summary>Bit/kHz choices (WEB FLAC rules 4.10, Blu-ray): the files' own value first, then 16BIT, 24BIT, 24BIT-44KHZ...</summary>
        public QualityChoice[] QualityChoices { get { return Get<QualityChoice[]>("QualityChoices"); } private set { Set("QualityChoices", value); } }

        /// <summary>Loads the album in <paramref name="path"/> to be tagged with <paramref name="profile"/>.</summary>
        public void Load(string path, UserProfile profile, IEnumerable<string> otherFolders = null)
        {
            Profile = profile;

            ReleaseInfoOptions options = profile != null ? profile.ReleaseInfo : null;
            ReleaseTypes = SceneTagging ? TypesFor((options != null ? options.ReleaseTypes : null) ?? DefaultTypes) : (options != null ? options.ReleaseTypes : null) ?? DefaultTypes;
            Sources = (options != null ? options.Sources : null) ?? DefaultSources;
            Languages = (options != null ? options.Languages : null) ?? DefaultLanguages;
            LimitedEditionChoices = (options != null ? options.LimitedEdition : null) ?? NoYes;
            WhiteLabelChoices = (options != null ? options.WhiteLabel : null) ?? NoYes;
            CleanExplicitChoices = (options != null ? options.CleanExplicit : null) ?? DefaultCleanExplicit;
            IsRipperLocked = options != null && options.LockRipper;
            IsSupplierLocked = options != null && options.LockSupplier;
            IsEncoderLocked = options != null && options.LockEncoder;
            IsCommentLocked = options != null && options.LockComment;

            Album album = new Album(path);
            DiscToc.ApplyLogLengths(album, DiscToc.ForAlbum(album));
            Sources = SourcesFor(Sources, album);
            string filesQuality = SceneRules.FilesQualityTag(album);
            QualityChoices = SceneRules.QualityTags(album).Select(t => new QualityChoice { Value = t, Name = t == filesQuality ? t + " (files)" : t }).ToArray();
            ReleaseInfo release = ReleaseInfo.FromProfile(profile);
            release.Quality = "";
            release.Fix = "";
            release.ReleaseType = release.ReleaseType ?? ReleaseTypes.FirstOrDefault();
            release.Source = release.Source ?? Sources.FirstOrDefault();
            release.Language = release.Language ?? Languages.FirstOrDefault();
            release.LimitedEdition = release.LimitedEdition ?? LimitedEditionChoices.FirstOrDefault();
            release.WhiteLabel = release.WhiteLabel ?? WhiteLabelChoices.FirstOrDefault();
            release.CleanExplicit = release.CleanExplicit ?? CleanExplicitChoices.FirstOrDefault();
            release.RetailDate = ToRetailDate(album.ReleaseDate);
            if (string.IsNullOrEmpty(release.RippingTool))
                release.RippingTool = "EAC";

            Release = release;
            Album = album;
            ShowAllGenres = true;
            OldFolderName = Path.GetFileName(path.TrimEnd('\\'));

            HasReleasePage = profile != null && profile.NFOOptions != null && profile.NFOOptions.ShowReleaseScreen;
            Page = HasReleasePage ? ReleasePage : AlbumPage;

            Release.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "Source")
                    UpdateHasBitKhz();
                UpdateNames();
            };
            UpdateHasBitKhz();
            Album.PropertyChanged += Album_PropertyChanged;
            foreach (AlbumTrack track in Album.Tracks)
                track.PropertyChanged += Track_PropertyChanged;

            SelectedTrackIndex = 0;
            SelectedTrack = Album.Tracks.FirstOrDefault();
            UpdateNames();

            Twin = null;
            TagTwin = false;
            _twinSearch = null;
            if (CDTag.Common.Settings.AppOptions.Current.TagBothFormats && TwinAlbum.FormatOf(album) != null)
                _twinSearch = FindTwinAsync(album, otherFolders != null ? otherFolders.ToList() : null);
        }

        // ------------------------------------------------------------------ FLAC and MP3 together

        private System.Threading.Tasks.Task _twinSearch;

        private async System.Threading.Tasks.Task FindTwinAsync(Album album, List<string> otherFolders)
        {
            Album twin = null;
            try
            {
                twin = await System.Threading.Tasks.Task.Run(() => TwinAlbum.Find(album, otherFolders));
            }
            catch (Exception)
            {
            }
            if (twin == null || Album != album)
                return;
            Twin = twin;
            TagTwin = true;
        }

        /// <summary>The same rip in the other format (Tools > Options: tag the FLAC and MP3 albums together), or null.</summary>
        public Album Twin
        {
            get { return Get<Album>("Twin"); }
            private set
            {
                Set("Twin", value);
                RaisePropertyChanged("HasTwin", null, null);
                RaisePropertyChanged("TwinText", null, null);
                RaisePropertyChanged("TwinFormatName", null, null);
            }
        }

        public bool HasTwin { get { return Twin != null; } }

        /// <summary>Finish also tags <see cref="Twin"/>.</summary>
        public bool TagTwin
        {
            get { return Get<bool>("TagTwin"); }
            set { Set("TagTwin", value); }
        }

        /// <summary>"MP3" or "FLAC": the format of <see cref="Twin"/>.</summary>
        public string TwinFormatName
        {
            get { return Twin != null ? TwinAlbum.FormatName(TwinAlbum.FormatOf(Twin)) : null; }
        }

        /// <summary>"Also tag the MP3 album: Ween-Chocolate And Cheese-2CD-1994".</summary>
        public string TwinText
        {
            get { return Twin != null ? string.Format("Also tag the {0} album: {1}", TwinFormatName, FolderName(Twin)) : null; }
        }

        private static string FolderName(Album album)
        {
            return Path.GetFileName(album.OldDirectoryName.TrimEnd('\\', '/'));
        }

        private static string NameKey(string profileName)
        {
            return System.Text.RegularExpressions.Regex.Replace(profileName ?? "", "flac|mp3", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                         .Trim(' ', '-', '_', '.').ToLowerInvariant();
        }

        /// <summary>
        /// The profile for the other-format album: this profile when it is made for that type too, else the only fitting
        /// profile, else the one named like this one ("GRP" / "GRPFLAC"); otherwise the user picks. Null = cancelled.
        /// </summary>
        private UserProfile PickTwinProfile(string format)
        {
            List<UserProfile> profiles = ProfileSelector.LoadAll(_pathService.ProfileDirectory);
            IList<UserProfile> candidates = ProfileSelector.FindCandidates(profiles, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { format });
            if (candidates.Count == 0)
                return null;

            if (Profile != null)
            {
                UserProfile same = candidates.FirstOrDefault(p => string.Equals(p.ProfileName, Profile.ProfileName, StringComparison.OrdinalIgnoreCase));
                if (same != null && ProfileSelector.GetFileTypes(same).Contains(format))
                    return same;
            }
            if (candidates.Count == 1)
                return candidates[0];
            if (Profile != null)
            {
                List<UserProfile> named = candidates.Where(p => NameKey(p.ProfileName) == NameKey(Profile.ProfileName)).ToList();
                if (named.Count == 1)
                    return named[0];
            }

            var window = IoC.Resolve<CDTag.Views.Interfaces.Profile.ChooseProfile.IChooseProfileWindow>();
            var chooser = (CDTag.ViewModels.Profile.ChooseProfile.IChooseProfileViewModel)window.DataContext;
            chooser.Message = string.Format("Several profiles fit {0} files. Which profile do you want to use for the {0} album?", TwinAlbum.FormatName(format));
            chooser.SetProfiles(candidates, null);
            return ShowWindow(window) == true ? chooser.SelectedProfile : null;
        }

        private static ReleaseListItem Map(ReleaseListItem item, ReleaseListItem[] list)
        {
            if (list == null || list.Length == 0)
                return item;
            if (item == null)
                return list[0];
            return list.FirstOrDefault(i => string.Equals((i.Nfo ?? "").Trim(), (item.Nfo ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? list.FirstOrDefault(i => !string.IsNullOrWhiteSpace(item.Dir) && string.Equals((i.Dir ?? "").Trim(), item.Dir.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? item;
        }

        /// <summary>
        /// Release information for the other-format album: this album's, with the other profile's choices (its list
        /// entries, locked ripper/supplier/comment, its encoder or the one in the files), WEB / WEB-FLAC swapped and
        /// Bit/kHz for FLAC only.
        /// </summary>
        public ReleaseInfo CreateTwinRelease(UserProfile twinProfile, Album twin)
        {
            ReleaseInfo from = Release;
            ReleaseInfo info = ReleaseInfo.FromProfile(twinProfile);
            ReleaseInfoOptions options = twinProfile != null ? twinProfile.ReleaseInfo : null;

            info.Label = from.Label;
            info.RipDate = from.RipDate;
            info.RetailDate = from.RetailDate;
            info.Url = from.Url;
            info.IsOst = from.IsOst;
            info.IsInternal = from.IsInternal;
            info.CatalogNumber = from.CatalogNumber;
            info.LiveDate = from.LiveDate;
            info.Fix = from.Fix;
            info.Notes = from.Notes;
            info.LongComment = from.LongComment;
            info.RippingTool = from.RippingTool;
            if (options == null || !options.LockRipper)
                info.Ripper = from.Ripper;
            if (options == null || !options.LockSupplier)
                info.Supplier = from.Supplier;
            if (options == null || !options.LockComment)
                info.Comment = from.Comment;
            if ((options == null || !options.LockEncoder) && string.IsNullOrWhiteSpace(info.Encoder))
                info.Encoder = TwinAlbum.EncoderOf(twin) ?? "";

            ReleaseListItem[] types = (options != null ? options.ReleaseTypes : null) ?? DefaultTypes;
            info.ReleaseType = Map(from.ReleaseType, SceneTagging ? TypesFor(types) : types);
            info.Language = Map(from.Language, (options != null ? options.Languages : null) ?? DefaultLanguages);
            info.LimitedEdition = Map(from.LimitedEdition, (options != null ? options.LimitedEdition : null) ?? NoYes);
            info.WhiteLabel = Map(from.WhiteLabel, (options != null ? options.WhiteLabel : null) ?? NoYes);
            info.CleanExplicit = Map(from.CleanExplicit, (options != null ? options.CleanExplicit : null) ?? DefaultCleanExplicit);

            // WEB-FLAC is the FLAC name of WEB.
            ReleaseListItem[] sources = SourcesFor((options != null ? options.Sources : null) ?? DefaultSources, twin);
            string source = from.Source != null ? SourceName(from.Source) : null;
            bool flac = TwinAlbum.FormatOf(twin) == "flac";
            if (!flac && string.Equals(source, "WEB-FLAC", StringComparison.OrdinalIgnoreCase))
                source = "WEB";
            else if (flac && string.Equals(source, "WEB", StringComparison.OrdinalIgnoreCase) && sources.Any(s => string.Equals(SourceName(s), "WEB-FLAC", StringComparison.OrdinalIgnoreCase)))
                source = "WEB-FLAC";
            info.Source = sources.FirstOrDefault(s => string.Equals(SourceName(s), source, StringComparison.OrdinalIgnoreCase)) ?? Map(from.Source, sources);

            info.Quality = "";
            if (flac && SceneTagging && SceneRules.HasBitKhzChoice(twin, info.Source != null ? SourceName(info.Source) : null))
                info.Quality = !string.IsNullOrEmpty(from.Quality) ? from.Quality : SceneRules.FilesQualityTag(twin) ?? "";
            return info;
        }

        /// <summary>Tag year ("2014", "2014-05-01") as a 2.x retail date (yyyy-MM-dd, unknown parts 00).</summary>
        private static string ToRetailDate(string tagged)
        {
            string text = (tagged ?? "").Trim();
            if (text.Length >= 10 && text[4] == '-')
                return text.Substring(0, 10);
            if (text.Length >= 7 && text[4] == '-')
                return text.Substring(0, 7) + "-00";
            if (text.Length >= 4)
                return text.Substring(0, 4) + "-00-00";
            return "";
        }

        private void Album_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "NewFolderName")
                UpdateNames();
            if (e.PropertyName == "DiscNames")
                RaisePropertyChanged("DiscName", null, null);
        }

        private void Track_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "NewFileName")
                UpdateNames();
            if (e.PropertyName == "DiscNumber" && sender == SelectedTrack)
                RaiseDiscNameChanged();
        }

        /// <summary>Disc of the selected track (disc names are edited per disc).</summary>
        public int SelectedDisc
        {
            get { return SelectedTrack != null ? DiscNumberOf(SelectedTrack) : 1; }
        }

        /// <summary>Name of the selected track's disc ("McCabe's Guitar Shop, Santa Monica, CA, 10/9/94").</summary>
        public string DiscName
        {
            get { return Album != null ? Album.GetDiscName(SelectedDisc) : null; }
            set
            {
                if (Album == null || Album.GetDiscName(SelectedDisc) == (string.IsNullOrWhiteSpace(value) ? null : value.Trim()))
                    return;
                Album.SetDiscName(SelectedDisc, value);
                RaisePropertyChanged("DiscName", null, null);
            }
        }

        public string DiscNameLabel
        {
            get { return string.Format("Disc {0} name", SelectedDisc); }
        }

        private void RaiseDiscNameChanged()
        {
            RaisePropertyChanged("SelectedDisc", null, null);
            RaisePropertyChanged("DiscName", null, null);
            RaisePropertyChanged("DiscNameLabel", null, null);
        }

        private static int DiscNumberOf(AlbumTrack track)
        {
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(track.DiscNumber ?? "", @"\d+");
            int number;
            return match.Success && int.TryParse(match.Value, out number) && number > 0 ? number : 1;
        }

        /// <summary>Recomputes the new folder name and every new file name from the profile.</summary>
        private void UpdateNames()
        {
            UpdateTrackPreview();
            if (_updatingNames || Album == null || Profile == null)
                return;

            _updatingNames = true;
            try
            {
                var namer = new AlbumNamer(Profile, Album, Release);
                Album.NewFolderName = namer.GetFolderName();
                foreach (AlbumTrack track in Album.Tracks)
                    track.NewFileName = namer.GetTrackFileName(track);
                NfoFileName = namer.GetFileName(NameKind.NFO);
            }
            finally
            {
                _updatingNames = false;
            }
        }

        private void TagAlbumViewModel_EnhancedPropertyChanged(object sender, EnhancedPropertyChangedEventArgs<TagAlbumViewModel> e)
        {
            if (e.IsProperty(p => p.SelectedTrackIndex))
            {
                if (Album != null && SelectedTrackIndex >= 0 && SelectedTrackIndex < Album.Tracks.Count)
                    SelectedTrack = Album.Tracks[SelectedTrackIndex];
            }
            else if (e.IsProperty(p => p.SelectedTrack))
            {
                RaiseDiscNameChanged();
            }
            else if (e.IsProperty(p => p.ShowAllGenres))
            {
                Genres = new ObservableCollection<string>(
                    GenreHelper.GenreByIndex
                        .Take(ShowAllGenres ? GenreHelper.GenreByIndex.Length : 80) // first 80 = original ID3v1 list
                        .Where(g => !string.IsNullOrEmpty(g))
                        .OrderBy(g => g, StringComparer.OrdinalIgnoreCase));
            }
            else if (e.IsProperty(p => p.Page))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>Looks the album up on MusicBrainz and fills album, release info, track titles and cover.</summary>
        private void Lookup()
        {
            ILookupWindow window = IoC.Resolve<ILookupWindow>();
            var lookup = (ILookupViewModel)window.DataContext;
            lookup.Artist = Album.Artist;
            lookup.Album = Album.AlbumTitle;
            lookup.TrackCount = Album.Tracks.Count;
            lookup.DiscCount = new AlbumNamer(Profile ?? new UserProfile(), Album, Release).Discs;
            lookup.Tocs = DiscToc.ForAlbum(Album);

            if (ShowWindow(window) != true || lookup.SelectedDetails == null)
                return;

            ReleaseDetails details = lookup.SelectedDetails;
            int applied = LookupApplier.Apply(details, Album, Release, Sources);
            int expected = details.OnlyDisc > 0
                ? Album.Tracks.Count(t => t.DiscNumber != null && t.DiscNumber.Trim().TrimStart('0') == details.OnlyDisc.ToString())
                : Album.Tracks.Count;
            if (lookup.Cover != null)
            {
                Album.CoverBytes = lookup.Cover;
                Album.Picture = new Picture(lookup.Cover, IdSharp.Tagging.ID3v2.PictureType.CoverFront, "").ImageSource;
            }
            UpdateNames();

            if (applied < expected)
            {
                MessageBox(string.Format("{0} of {1} tracks{3} were matched to the release ({2} tracks). Check the disc and track numbers of the others.",
                                         applied, expected, details.Tracks.Count, details.OnlyDisc > 0 ? " of disc " + details.OnlyDisc : ""),
                           "Look up", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }

        public ICommand LookupCommand { get; private set; }

        /// <summary>Tracks in disc / track order (as listed).</summary>
        private List<AlbumTrack> OrderedTracks()
        {
            return Album.Tracks.ToList();
        }

        // ------------------------------------------------------------------ song name undo

        private readonly Stack<Tuple<string, Dictionary<AlbumTrack, string>>> _titleUndo = new Stack<Tuple<string, Dictionary<AlbumTrack, string>>>();

        /// <summary>"Undo Capitalize Every Word", or null when there is nothing to undo.</summary>
        public string UndoTitlesText
        {
            get { return _titleUndo.Count > 0 ? "Undo " + _titleUndo.Peek().Item1 : null; }
        }

        /// <summary>Puts back the song names from before the last casing / replace / paste.</summary>
        public void UndoTitles()
        {
            if (_titleUndo.Count == 0)
                return;
            foreach (KeyValuePair<AlbumTrack, string> saved in _titleUndo.Pop().Item2)
                saved.Key.Title = saved.Value;
        }

        /// <summary>Runs <paramref name="change"/> on the song names and keeps the old names for Undo when any changed.</summary>
        private void ChangeTitles(string description, Action change)
        {
            Dictionary<AlbumTrack, string> before = Album.Tracks.ToDictionary(t => t, t => t.Title);
            change();
            if (Album.Tracks.Any(t => t.Title != before[t]))
                _titleUndo.Push(Tuple.Create(description, before));
        }

        public void ApplyCasing(TitleCasing casing)
        {
            if (Album == null)
                return;
            string[] names = { "Capitalize Every Word", "UPPERCASE", "lowercase", "Sentence case" };
            ChangeTitles(names[(int)casing], () =>
            {
                foreach (AlbumTrack track in Album.Tracks)
                    track.Title = TitleText.ApplyCasing(track.Title, casing);
            });
        }

        public int SearchReplace(string find, string replace, bool matchCase)
        {
            if (Album == null || string.IsNullOrEmpty(find))
                return 0;
            int result = 0;
            ChangeTitles("Search & Replace", () => result = ReplaceTitles(find, replace, matchCase));
            return result;
        }

        private int ReplaceTitles(string find, string replace, bool matchCase)
        {
            int changed = 0;
            foreach (AlbumTrack track in Album.Tracks)
            {
                string title = TitleText.Replace(track.Title, find, replace, matchCase);
                if (title != track.Title)
                {
                    track.Title = title;
                    changed++;
                }
            }
            return changed;
        }

        public int PasteTracklist(string text)
        {
            if (Album == null)
                return 0;
            List<string> titles = TitleText.ParseTracklist(text);
            List<AlbumTrack> tracks = OrderedTracks();
            ChangeTitles("Paste Tracklist", () =>
            {
                for (int i = 0; i < titles.Count && i < tracks.Count; i++)
                    tracks[i].Title = titles[i];
            });
            return titles.Count;
        }

        public bool IsFinishing
        {
            get { return Get<bool>("IsFinishing"); }
            private set { Set("IsFinishing", value); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
        }

        /// <summary>What Finish is doing ("Tagging 01.flac").</summary>
        public string FinishStatus
        {
            get { return Get<string>("FinishStatus"); }
            private set { Set("FinishStatus", value); }
        }

        /// <summary>The album folder after Finish (renamed), or null when Finish has not run.</summary>
        public string FinishedDirectory { get; private set; }

        private async void Finish()
        {
            // Still looking for the other-format album.
            if (_twinSearch != null && !_twinSearch.IsCompleted)
            {
                IsFinishing = true;
                FinishStatus = "Looking for the same album in the other format...";
                try
                {
                    await _twinSearch;
                }
                catch (Exception)
                {
                }
                finally
                {
                    FinishStatus = null;
                    IsFinishing = false;
                }
            }

            string templatePath = NfoGenerator.ResolveTemplatePath(Profile, _pathService.ProfileDirectory);
            string template = templatePath != null ? NfoGenerator.ReadTemplate(templatePath) : null;
            var finisher = new AlbumFinisher(Profile, Album, Release, template);

            FinishPlan plan;
            try
            {
                plan = finisher.Plan();
            }
            catch (Exception ex)
            {
                MessageBox("Finish could not check the album:" + Environment.NewLine + Environment.NewLine + ex.Message, "Finish",
                           System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            if (!plan.CanRun)
            {
                MessageBox("Nothing was changed. Fix these first:" + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, plan.Problems.Distinct()),
                           "Finish", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // The same album in the other format: same tags and release information, its own profile and maybe its own notes.
            Album twin = TagTwin ? Twin : null;
            AlbumFinisher twinFinisher = null;
            FinishPlan twinPlan = null;
            UserProfile twinProfile = null;
            string format = twin != null ? TwinFormatName : null;
            if (twin != null)
            {
                twinProfile = PickTwinProfile(TwinAlbum.FormatOf(twin));
                if (twinProfile == null)
                    return;

                TwinAlbum.CopyTags(Album, twin);
                ReleaseInfo twinRelease = CreateTwinRelease(twinProfile, twin);

                System.Windows.MessageBoxResult different = MessageBox(
                    string.Format("Are the NFO notes different for the {0} release ({1})?{2}{2}Yes: enter its notes next.{2}No: both releases get the same notes.",
                                  format, FolderName(twin), Environment.NewLine),
                    "Finish", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question);
                if (different == System.Windows.MessageBoxResult.Cancel)
                    return;
                if (different == System.Windows.MessageBoxResult.Yes)
                {
                    IReleaseNotesWindow notesWindow = IoC.Resolve<IReleaseNotesWindow>();
                    var notes = (IReleaseNotesViewModel)notesWindow.DataContext;
                    notes.Title = "NFO notes - " + format + " release";
                    notes.Message = string.Format("NFO notes for the {0} release ({1}). They start as this release's notes:", format, FolderName(twin));
                    notes.Notes = Release.Notes;
                    if (ShowWindow(notesWindow) != true)
                        return;
                    twinRelease.Notes = notes.Notes;
                }

                string twinTemplatePath = NfoGenerator.ResolveTemplatePath(twinProfile, _pathService.ProfileDirectory);
                twinFinisher = new AlbumFinisher(twinProfile, twin, twinRelease, twinTemplatePath != null ? NfoGenerator.ReadTemplate(twinTemplatePath) : null);
                string problem = null;
                try
                {
                    twinPlan = twinFinisher.Plan();
                    if (!twinPlan.CanRun)
                        problem = string.Join(Environment.NewLine, twinPlan.Problems.Distinct());
                }
                catch (Exception ex)
                {
                    problem = ex.Message;
                }
                if (problem != null)
                {
                    if (MessageBox(string.Format("The {0} album cannot be finished:{1}{1}{2}{1}{1}Finish only this album?", format, Environment.NewLine, problem),
                                   "Finish", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
                        return;
                    twin = null;
                }
            }

            string describe = plan.Describe();
            if (twin != null)
                describe += Environment.NewLine + Environment.NewLine + string.Format("Then the {0} album (profile {1}):", format, twinProfile.ProfileName)
                            + Environment.NewLine + twinPlan.Describe();
            if (MessageBox(describe + Environment.NewLine + Environment.NewLine + "Continue?", "Finish",
                           System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            IsFinishing = true;
            try
            {
                FinishedDirectory = await System.Threading.Tasks.Task.Run(() => finisher.Run(plan, status => BeginInvoke(() => FinishStatus = status)));
                FinishStatus = null;
            }
            catch (Exception ex)
            {
                FinishStatus = null;
                IsFinishing = false;
                FinishedDirectory = Directory.Exists(plan.NewDirectory) ? plan.NewDirectory : plan.OldDirectory;
                MessageBox("Finish stopped: " + ex.Message + Environment.NewLine + Environment.NewLine +
                           "Some files may already be tagged or renamed. Check the folder before running Finish again." +
                           (twin != null ? Environment.NewLine + Environment.NewLine + "The " + format + " album was not changed." : ""),
                           "Finish", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            if (twin != null)
            {
                try
                {
                    await System.Threading.Tasks.Task.Run(() => twinFinisher.Run(twinPlan, status => BeginInvoke(() => FinishStatus = format + ": " + status)));
                    FinishStatus = null;
                }
                catch (Exception ex)
                {
                    FinishStatus = null;
                    MessageBox("This album is finished, but the " + format + " album stopped: " + ex.Message + Environment.NewLine + Environment.NewLine +
                               "Some of its files may already be tagged or renamed. Check " + (Directory.Exists(twinPlan.NewDirectory) ? twinPlan.NewDirectory : twinPlan.OldDirectory) +
                               " before running Finish on it again.",
                               "Finish", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }

            IsFinishing = false;
            CloseWindow(true);
        }

        private void PreviewNFO()
        {
            string templatePath = NfoGenerator.ResolveTemplatePath(Profile, _pathService.ProfileDirectory);
            string text;
            if (templatePath == null)
            {
                text = string.Format("The profile \"{0}\" has no NFO template.{1}{1}Select one in the profile (NFO template), or copy it into{1}{2}",
                                     Profile.ProfileName, Environment.NewLine, _pathService.ProfileDirectory);
            }
            else
            {
                text = new NfoGenerator(Profile, Album, Release).Generate(NfoGenerator.ReadTemplate(templatePath));
            }

            IPreviewNfoWindow window = IoC.Resolve<IPreviewNfoWindow>();
            var preview = (IPreviewNfoViewModel)window.DataContext;
            preview.Text = text;
            preview.FileName = NfoFileName;
            ShowWindow(window);
        }

        // ------------------------------------------------------------------ state

        public UserProfile Profile
        {
            get { return Get<UserProfile>("Profile"); }
            private set { Set("Profile", value); }
        }

        public Album Album
        {
            get { return Get<Album>("Album"); }
            private set { Set("Album", value); }
        }

        /// <summary>Release information (first page).</summary>
        public ReleaseInfo Release
        {
            get { return Get<ReleaseInfo>("Release"); }
            private set { Set("Release", value); }
        }

        /// <summary>0 = release information, 1 = album.</summary>
        public int Page
        {
            get { return Get<int>("Page"); }
            set { Set("Page", value); }
        }

        /// <summary>Whether the profile shows the release information page (2.x "Show screen with additional NFO fields").</summary>
        public bool HasReleasePage
        {
            get { return Get<bool>("HasReleasePage"); }
            private set { Set("HasReleasePage", value); }
        }

        public string OldFolderName
        {
            get { return Get<string>("OldFolderName"); }
            private set { Set("OldFolderName", value); }
        }

        /// <summary>The tracks in disc / track order (Track Preview tab).</summary>
        public List<TrackPreviewRow> TrackPreview
        {
            get { return Get<List<TrackPreviewRow>>("TrackPreview"); }
            private set { Set("TrackPreview", value); }
        }

        private void UpdateTrackPreview()
        {
            if (Album == null)
                return;
            bool longTracks = Album.Tracks.Any(t => Math.Round(t.TotalSeconds) >= 600);
            List<TrackPreviewRow> rows = Album.Tracks
                .OrderBy(t => PreviewNumber(t.DiscNumber, 1)).ThenBy(t => PreviewNumber(t.TrackNumber, 0)).ThenBy(t => t.OriginalFileName, StringComparer.OrdinalIgnoreCase)
                .Select(t =>
                {
                    int seconds = (int)Math.Round(t.TotalSeconds);
                    string time = string.Format(longTracks ? "{0:00}:{1:00}" : "{0}:{1:00}", seconds / 60, seconds % 60);
                    return new TrackPreviewRow
                    {
                        Disc = PreviewNumber(t.DiscNumber, 1),
                        Track = PreviewNumber(t.TrackNumber, 0),
                        Artist = t.Artist,
                        Title = t.Title,
                        Time = seconds > 0 ? time : ""
                    };
                }).ToList();

            // Same rows: keep the list (and its scroll position) while typing elsewhere.
            List<TrackPreviewRow> current = TrackPreview;
            if (current == null || !current.SequenceEqual(rows))
                TrackPreview = rows;
        }

        private static int PreviewNumber(string text, int fallback)
        {
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(text ?? "", @"\d+");
            int number;
            return match.Success && int.TryParse(match.Value, out number) ? number : fallback;
        }

        public string NfoFileName
        {
            get { return Get<string>("NfoFileName"); }
            private set { Set("NfoFileName", value); }
        }

        public AlbumTrack SelectedTrack
        {
            get { return Get<AlbumTrack>("SelectedTrack"); }
            set { Set("SelectedTrack", value); }
        }

        public int SelectedTrackIndex
        {
            get { return Get<int>("SelectedTrackIndex"); }
            set { Set("SelectedTrackIndex", value); }
        }

        public bool ShowAllGenres
        {
            get { return Get<bool>("ShowAllGenres"); }
            set { Set("ShowAllGenres", value); }
        }

        public ObservableCollection<string> Genres
        {
            get { return Get<ObservableCollection<string>>("Genres"); }
            private set { Set("Genres", value); }
        }

        // Release info choices
        public ReleaseListItem[] ReleaseTypes { get { return Get<ReleaseListItem[]>("ReleaseTypes"); } private set { Set("ReleaseTypes", value); } }
        public ReleaseListItem[] Sources { get { return Get<ReleaseListItem[]>("Sources"); } private set { Set("Sources", value); } }
        public ReleaseListItem[] Languages { get { return Get<ReleaseListItem[]>("Languages"); } private set { Set("Languages", value); } }
        public ReleaseListItem[] LimitedEditionChoices { get { return Get<ReleaseListItem[]>("LimitedEditionChoices"); } private set { Set("LimitedEditionChoices", value); } }
        public ReleaseListItem[] WhiteLabelChoices { get { return Get<ReleaseListItem[]>("WhiteLabelChoices"); } private set { Set("WhiteLabelChoices", value); } }
        public ReleaseListItem[] CleanExplicitChoices { get { return Get<ReleaseListItem[]>("CleanExplicitChoices"); } private set { Set("CleanExplicitChoices", value); } }
        public ObservableCollection<string> RippingTools { get; private set; }
        public ObservableCollection<string> Encoders { get; private set; }

        public bool IsRipperLocked { get { return Get<bool>("IsRipperLocked"); } private set { Set("IsRipperLocked", value); } }
        public bool IsSupplierLocked { get { return Get<bool>("IsSupplierLocked"); } private set { Set("IsSupplierLocked", value); } }
        public bool IsEncoderLocked { get { return Get<bool>("IsEncoderLocked"); } private set { Set("IsEncoderLocked", value); } }
        public bool IsCommentLocked { get { return Get<bool>("IsCommentLocked"); } private set { Set("IsCommentLocked", value); } }

        /// <summary>No/Yes selectors for OST and Internal (2.x shows them as dropdowns).</summary>
        public int OstIndex
        {
            get { return Release != null && Release.IsOst ? 1 : 0; }
            set { if (Release != null) Release.IsOst = value == 1; }
        }

        public int InternalIndex
        {
            get { return Release != null && Release.IsInternal ? 1 : 0; }
            set { if (Release != null) Release.IsInternal = value == 1; }
        }

        public ICommand FinishCommand
        {
            get { return _finishCommand; }
        }

        public ICommand NextCommand
        {
            get { return _nextCommand; }
        }

        public ICommand PreviewNFOCommand
        {
            get { return _previewNFOCommand; }
        }

        public ICommand PreviousCommand
        {
            get { return _previousCommand; }
        }
    }
}

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
            // Writing tags and renaming is the next step; until then Finish stays disabled.
            _finishCommand = new DelegateCommand(() => { }, () => false);
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

        /// <summary>Loads the album in <paramref name="path"/> to be tagged with <paramref name="profile"/>.</summary>
        public void Load(string path, UserProfile profile)
        {
            Profile = profile;

            ReleaseInfoOptions options = profile != null ? profile.ReleaseInfo : null;
            ReleaseTypes = (options != null ? options.ReleaseTypes : null) ?? DefaultTypes;
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
            ReleaseInfo release = ReleaseInfo.FromProfile(profile);
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

            Release.PropertyChanged += (s, e) => UpdateNames();
            Album.PropertyChanged += Album_PropertyChanged;
            foreach (AlbumTrack track in Album.Tracks)
                track.PropertyChanged += Track_PropertyChanged;

            SelectedTrackIndex = 0;
            SelectedTrack = Album.Tracks.FirstOrDefault();
            UpdateNames();
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
        }

        private void Track_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "NewFileName")
                UpdateNames();
        }

        /// <summary>Recomputes the new folder name and every new file name from the profile.</summary>
        private void UpdateNames()
        {
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

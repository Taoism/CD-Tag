using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;
using CDTag.Model.Lookup;

namespace CDTag.ViewModels.Tag.TagAlbum
{
    public enum LookupSource
    {
        /// <summary>MusicBrainz search by artist / album (or a pasted release link).</summary>
        MusicBrainzSearch,
        /// <summary>MusicBrainz by disc ID / TOC.</summary>
        MusicBrainzDisc,
        /// <summary>CUETools Database by TOC (MusicBrainz, Discogs and freedb data).</summary>
        CueToolsDb
    }

    public class LookupSourceOption
    {
        public LookupSource Value { get; set; }
        public string Name { get; set; }
        public bool NeedsToc { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public interface ILookupViewModel : IViewModelBase
    {
        string Artist { get; set; }
        string Album { get; set; }
        int TrackCount { get; set; }
        int DiscCount { get; set; }
        /// <summary>Disc TOCs of the album (from rip logs / FLAC lengths) for disc-ID lookups.</summary>
        IList<DiscToc> Tocs { get; set; }
        ReleaseDetails SelectedDetails { get; }
        byte[] Cover { get; }

        /// <summary>Starts the first search (called when the window opens).</summary>
        void Start();
    }

    /// <summary>
    /// Looks the album up in MusicBrainz (text search, pasted release link, or disc ID) or the CUETools Database
    /// (MusicBrainz, Discogs and freedb by disc TOC), shows the track list of the selected release, and returns it
    /// with its front cover when the user clicks Apply.
    /// </summary>
    public class LookupViewModel : ViewModelBase, ILookupViewModel
    {
        private static readonly Regex ReleaseId = new Regex(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);

        private readonly MusicBrainzClient _client = new MusicBrainzClient();
        private readonly CueToolsDbClient _ctdb = new CueToolsDbClient();
        /// <summary>Results that came with their full track list (disc-ID lookups), by result ID.</summary>
        private readonly Dictionary<string, ReleaseDetails> _loaded = new Dictionary<string, ReleaseDetails>();
        private CancellationTokenSource _detailsCancellation;
        private int _searchVersion;
        private IList<DiscToc> _tocs = new List<DiscToc>();

        public LookupViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            Results = new ObservableCollection<ReleaseSearchResult>();
            Tracks = new ObservableCollection<ReleaseTrack>();
            Sources = new List<LookupSourceOption>
            {
                new LookupSourceOption { Value = LookupSource.MusicBrainzSearch, Name = "MusicBrainz (search)" },
                new LookupSourceOption { Value = LookupSource.CueToolsDb, Name = "CUETools DB (disc TOC)", NeedsToc = true },
                new LookupSourceOption { Value = LookupSource.MusicBrainzDisc, Name = "MusicBrainz (disc ID)", NeedsToc = true }
            };
            SelectedSource = Sources[0];
            SearchCommand = new DelegateCommand(() => Search(), () => !IsBusy);
            ApplyCommand = new DelegateCommand(Apply, () => SelectedDetails != null && !IsBusy);
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "SelectedResult")
                    LoadSelected();
                else if (e.PropertyName == "SelectedSource" || e.PropertyName == "SelectedToc")
                    OnSourceChanged();
            };
        }

        public string Artist { get { return Get<string>("Artist"); } set { Set("Artist", value); } }
        public string Album { get { return Get<string>("Album"); } set { Set("Album", value); } }

        /// <summary>Optional MusicBrainz release link or ID (loads that release directly).</summary>
        public string ReleaseLink { get { return Get<string>("ReleaseLink"); } set { Set("ReleaseLink", value); } }

        public int TrackCount { get; set; }
        public int DiscCount { get; set; }

        public IList<DiscToc> Tocs
        {
            get { return _tocs; }
            set
            {
                _tocs = value ?? new List<DiscToc>();
                RaisePropertyChanged("Tocs", null, null);
                RaisePropertyChanged("HasManyTocs", null, null);
                SelectedToc = _tocs.FirstOrDefault();
                // A rip with a TOC is best matched by it.
                if (_tocs.Count > 0)
                    SelectedSource = Sources.First(s => s.Value == LookupSource.CueToolsDb);
            }
        }

        public bool HasManyTocs
        {
            get { return _tocs.Count > 1; }
        }

        public DiscToc SelectedToc { get { return Get<DiscToc>("SelectedToc"); } set { Set("SelectedToc", value); } }

        public List<LookupSourceOption> Sources { get; private set; }
        public LookupSourceOption SelectedSource { get { return Get<LookupSourceOption>("SelectedSource"); } set { Set("SelectedSource", value); } }

        /// <summary>True when the artist/album/link boxes are used (text search).</summary>
        public bool IsTextSearch
        {
            get { return SelectedSource == null || !SelectedSource.NeedsToc; }
        }

        /// <summary>"Disc 1: 12 tracks (EAC log)" or why a disc-ID lookup is not possible.</summary>
        public string TocInfo
        {
            get
            {
                if (IsTextSearch)
                    return null;
                if (_tocs.Count == 0)
                    return "No disc TOC: needs the rip log (EAC/XLD) in the album folder, or FLAC files ripped from CD.";
                DiscToc toc = SelectedToc ?? _tocs[0];
                return string.Format("{0} tracks, TOC from {1}", toc.AudioTrackCount, toc.Source);
            }
        }

        public ObservableCollection<ReleaseSearchResult> Results { get; private set; }
        public ObservableCollection<ReleaseTrack> Tracks { get; private set; }

        public ReleaseSearchResult SelectedResult { get { return Get<ReleaseSearchResult>("SelectedResult"); } set { Set("SelectedResult", value); } }
        public ReleaseDetails SelectedDetails { get { return Get<ReleaseDetails>("SelectedDetails"); } private set { Set("SelectedDetails", value); } }
        public byte[] Cover { get; private set; }

        public string Status { get { return Get<string>("Status"); } private set { Set("Status", value); } }
        public bool IsBusy { get { return Get<bool>("IsBusy"); } private set { Set("IsBusy", value); CommandManager.InvalidateRequerySuggested(); } }

        public ICommand SearchCommand { get; private set; }
        public ICommand ApplyCommand { get; private set; }

        public string Summary
        {
            get
            {
                ReleaseDetails d = SelectedDetails;
                if (d == null)
                    return null;
                string discs = d.OnlyDisc > 0 && DiscCount > 1 ? "disc " + d.OnlyDisc + ", " : d.Discs > 1 ? d.Discs + " discs, " : "";
                return string.Format("{0} - {1} ({2}) · {3}{4} tracks · {5} {6} · {7}", d.Artist, d.Title, d.Date ?? "no date", discs,
                                     d.Tracks.Count, d.Label, d.CatalogNumber, d.Source).Trim();
            }
        }

        public void Start()
        {
            Search();
        }

        private bool _started;

        private void OnSourceChanged()
        {
            RaisePropertyChanged("IsTextSearch", null, null);
            RaisePropertyChanged("TocInfo", null, null);
            if (_started)
                Search();
        }

        private async void Search()
        {
            _started = true;
            int version = ++_searchVersion;
            Results.Clear();
            Tracks.Clear();
            _loaded.Clear();
            SelectedDetails = null;
            Cover = null;

            IsBusy = true;
            try
            {
                LookupSource source = SelectedSource != null ? SelectedSource.Value : LookupSource.MusicBrainzSearch;
                if (source == LookupSource.MusicBrainzSearch)
                    await TextSearchAsync(version);
                else
                    await DiscSearchAsync(source, version);
            }
            catch (Exception ex)
            {
                if (version == _searchVersion)
                    Status = "Lookup failed: " + Unwrap(ex).Message;
            }
            finally
            {
                if (version == _searchVersion)
                    IsBusy = false;
            }
        }

        private async Task TextSearchAsync(int version)
        {
            Match id = ReleaseId.Match(ReleaseLink ?? "");
            if (id.Success)
            {
                Status = "Loading release from MusicBrainz...";
                ReleaseDetails details = await _client.GetReleaseAsync(id.Value.ToLowerInvariant());
                if (version != _searchVersion)
                    return;
                ReleaseSearchResult row = details.ToSearchResult(TrackCount);
                _loaded[row.Id] = details;
                Results.Add(row);
                Status = null;
                SelectedResult = row;
                return;
            }

            if (string.IsNullOrWhiteSpace(Artist) && string.IsNullOrWhiteSpace(Album))
            {
                Status = "Enter an artist and/or album, or paste a MusicBrainz release link.";
                return;
            }

            Status = "Searching MusicBrainz...";
            var results = await _client.SearchReleasesAsync(Artist, Album, TrackCount);
            if (version != _searchVersion)
                return;
            foreach (ReleaseSearchResult result in results)
                Results.Add(result);

            int matching = results.Count(r => r.MatchesTrackCount);
            Status = results.Count == 0
                ? "No releases found. Try a shorter title, or paste the release link from musicbrainz.org."
                : string.Format("{0} releases found{1}.", results.Count, matching > 0 ? string.Format(", {0} with {1} tracks (listed first)", matching, TrackCount) : "");
            if (results.Count > 0)
                SelectedResult = results[0];
        }

        private async Task DiscSearchAsync(LookupSource source, int version)
        {
            DiscToc toc = SelectedToc;
            if (toc == null)
            {
                Status = "No disc TOC for this album; use MusicBrainz (search) instead.";
                return;
            }

            string name = source == LookupSource.CueToolsDb ? "CUETools DB" : "MusicBrainz";
            Status = string.Format("Looking up disc {0} in {1}...", toc.Disc, name);
            List<ReleaseDetails> releases = source == LookupSource.CueToolsDb
                ? await _ctdb.LookupAsync(toc)
                : await _client.LookupDiscAsync(toc);
            if (version != _searchVersion)
                return;

            // A MusicBrainz release holds every disc; compare with the whole album. CTDB data is per disc.
            foreach (ReleaseDetails release in releases)
            {
                int wanted = release.OnlyDisc > 0 ? toc.AudioTrackCount : TrackCount;
                ReleaseSearchResult row = release.ToSearchResult(wanted);
                _loaded[row.Id] = release;
                Results.Add(row);
            }

            if (releases.Count == 0)
            {
                Status = string.Format("Disc {0} is not in {1}. Try another source or MusicBrainz (search).", toc.Disc, name);
                return;
            }
            var bySource = releases.GroupBy(r => r.Source).Select(g => g.Count() + " " + g.Key);
            Status = string.Format("{0} matches for disc {1} ({2}).", releases.Count, toc.Disc, string.Join(", ", bySource));
            SelectedResult = Results.FirstOrDefault(r => r.MatchesTrackCount) ?? Results[0];
        }

        private async void LoadSelected()
        {
            ReleaseSearchResult selected = SelectedResult;
            if (selected == null || (SelectedDetails != null && SelectedDetails.Id == selected.Id))
                return;

            if (_detailsCancellation != null)
                _detailsCancellation.Cancel();
            var cancellation = new CancellationTokenSource();
            _detailsCancellation = cancellation;

            Tracks.Clear();
            SelectedDetails = null;
            Cover = null;
            try
            {
                ReleaseDetails details;
                if (!_loaded.TryGetValue(selected.Id, out details))
                {
                    Status = "Loading track list...";
                    details = await _client.GetReleaseAsync(selected.Id, cancellation.Token);
                    if (cancellation.IsCancellationRequested)
                        return;
                    Status = null;
                }
                ShowDetails(details);
                await LoadCoverAsync(details);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!cancellation.IsCancellationRequested)
                    Status = "Could not load the release: " + Unwrap(ex).Message;
            }
        }

        private void ShowDetails(ReleaseDetails details)
        {
            Tracks.Clear();
            foreach (ReleaseTrack track in details.Tracks)
                Tracks.Add(track);
            SelectedDetails = details;
            RaisePropertyChanged("Summary", null, null);
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task LoadCoverAsync(ReleaseDetails details)
        {
            try
            {
                byte[] cover = null;
                if (details.CoverUrl != null)
                    cover = await CueToolsDbClient.GetImageAsync(details.CoverUrl);
                if (cover == null && details.MusicBrainzId != null)
                    cover = await _client.GetFrontCoverAsync(details.MusicBrainzId);
                if (SelectedDetails == details)
                    Cover = cover;
            }
            catch (Exception)
            {
                // No cover is not an error.
            }
        }

        private async void Apply()
        {
            ReleaseDetails details = SelectedDetails;
            if (details == null)
                return;

            if (Cover == null)
            {
                IsBusy = true;
                Status = "Loading cover...";
                await LoadCoverAsync(details);
                IsBusy = false;
            }
            CloseWindow(true);
        }

        private static Exception Unwrap(Exception ex)
        {
            while (ex is AggregateException && ex.InnerException != null)
                ex = ex.InnerException;
            return ex;
        }
    }
}

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
        CueToolsDb,
        /// <summary>Discogs search by artist / album (needs a personal access token), or a pasted Discogs release link.</summary>
        DiscogsSearch
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
                new LookupSourceOption { Value = LookupSource.MusicBrainzDisc, Name = "MusicBrainz (disc ID)", NeedsToc = true },
                new LookupSourceOption { Value = LookupSource.DiscogsSearch, Name = "Discogs (search)" }
            };
            SelectedSource = Sources[0];
            SearchCommand = new DelegateCommand(() => Search(), () => !IsBusy);
            GetDiscogsTokenCommand = new DelegateCommand(() => OpenPage(DiscogsClient.TokenPage));
            SaveDiscogsTokenCommand = new DelegateCommand(SaveDiscogsToken, () => !string.IsNullOrWhiteSpace(DiscogsTokenInput));
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
                var choices = new List<DiscToc>();
                if (_tocs.Count > 1)
                    choices.Add(new AllDiscsToc(_tocs.Count));
                choices.AddRange(_tocs);
                TocChoices = choices;
                RaisePropertyChanged("Tocs", null, null);
                RaisePropertyChanged("TocChoices", null, null);
                RaisePropertyChanged("HasManyTocs", null, null);
                SelectedToc = choices.FirstOrDefault();
                RaisePropertyChanged("TocInfo", null, null);
            }
        }

        /// <summary>The disc list: "All discs" first when the album has several, then each disc.</summary>
        public IList<DiscToc> TocChoices { get; private set; } = new List<DiscToc>();

        public bool HasManyTocs
        {
            get { return _tocs.Count > 1; }
        }

        /// <summary>The "All discs" entry of the disc list: every disc is looked up and the results are joined.</summary>
        public class AllDiscsToc : DiscToc
        {
            private readonly int _discs;

            public AllDiscsToc(int discs)
            {
                _discs = discs;
                Source = "all discs";
            }

            public override string ToString()
            {
                return DisplayName;
            }

            public override string DisplayName
            {
                get { return string.Format("All {0}", _discs); }
            }
        }

        private bool IsAllDiscs
        {
            get { return SelectedToc is AllDiscsToc; }
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
                if (IsAllDiscs)
                {
                    string info = string.Format("{0} tracks on {1} discs, TOCs from {2}", _tocs.Sum(t => t.AudioTrackCount), _tocs.Count,
                                                string.Join(", ", _tocs.Select(t => t.Source).Distinct()));
                    var missing = Enumerable.Range(1, Math.Max(DiscCount, 0)).Where(d => _tocs.All(t => t.Disc != d)).ToList();
                    if (missing.Count > 0)
                        info += string.Format(" (no TOC for disc {0})", string.Join(", ", missing));
                    return info;
                }
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

        // ------------------------------------------------------------------ Discogs token

        private static DiscogsClient Discogs
        {
            get { return new DiscogsClient(CDTag.Common.Settings.AppOptions.Current.DiscogsToken); }
        }

        private bool IsDiscogs
        {
            get { return SelectedSource != null && SelectedSource.Value == LookupSource.DiscogsSearch; }
        }

        /// <summary>Discogs is the source and there is no token yet: the window shows where to get one.</summary>
        public bool NeedsDiscogsToken
        {
            get { return IsDiscogs && !Discogs.HasToken; }
        }

        /// <summary>The token typed or pasted in the Look up window (saved to Tools > Options).</summary>
        public string DiscogsTokenInput
        {
            get { return Get<string>("DiscogsTokenInput"); }
            set { Set("DiscogsTokenInput", value); CommandManager.InvalidateRequerySuggested(); }
        }

        public ICommand GetDiscogsTokenCommand { get; private set; }
        public ICommand SaveDiscogsTokenCommand { get; private set; }

        private void SaveDiscogsToken()
        {
            CDTag.Common.Settings.AppOptionsData options = CDTag.Common.Settings.AppOptions.Current.Clone();
            options.DiscogsToken = DiscogsTokenInput.Trim();
            CDTag.Common.Settings.AppOptions.Save(options);
            DiscogsTokenInput = null;
            RaisePropertyChanged("NeedsDiscogsToken", null, null);
            Search();
        }

        private static void OpenPage(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

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
            RaisePropertyChanged("NeedsDiscogsToken", null, null);
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
                if (source == LookupSource.MusicBrainzSearch || source == LookupSource.DiscogsSearch)
                    await TextSearchAsync(source, version);
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

        private async Task TextSearchAsync(LookupSource source, int version)
        {
            string link = ReleaseLink ?? "";
            string discogsId = DiscogsClient.ReleaseIdFromLink(link);
            if (discogsId == null && source == LookupSource.DiscogsSearch && Regex.IsMatch(link.Trim(), @"^\d+$"))
                discogsId = link.Trim();
            Match id = ReleaseId.Match(link);
            if (discogsId != null || id.Success)
            {
                Status = discogsId != null ? "Loading release from Discogs..." : "Loading release from MusicBrainz...";
                ReleaseDetails details = discogsId != null
                    ? await Discogs.GetReleaseAsync(discogsId)
                    : await _client.GetReleaseAsync(id.Value.ToLowerInvariant());
                if (version != _searchVersion)
                    return;
                ReleaseSearchResult row = details.ToSearchResult(TrackCount);
                _loaded[row.Id] = details;
                Results.Add(row);
                Status = null;
                SelectedResult = row;
                return;
            }

            bool discogs = source == LookupSource.DiscogsSearch;
            if (string.IsNullOrWhiteSpace(Artist) && string.IsNullOrWhiteSpace(Album))
            {
                Status = discogs
                    ? "Enter an artist and/or album, or paste a Discogs release link."
                    : "Enter an artist and/or album, or paste a MusicBrainz or Discogs release link.";
                return;
            }

            Status = discogs ? "Searching Discogs..." : "Searching MusicBrainz...";
            var results = discogs
                ? await Discogs.SearchReleasesAsync(Artist, Album, DiscCount)
                : await _client.SearchReleasesAsync(Artist, Album, TrackCount);
            if (version != _searchVersion)
                return;
            foreach (ReleaseSearchResult result in results)
                Results.Add(result);

            int matching = results.Count(r => r.MatchesTrackCount);
            Status = results.Count == 0
                ? (discogs ? "No releases found. Try a shorter title, or paste the release link from discogs.com."
                           : "No releases found. Try a shorter title, or paste the release link from musicbrainz.org.")
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
            if (IsAllDiscs)
            {
                await AllDiscsSearchAsync(source, name, version);
                return;
            }
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

        /// <summary>Looks up every disc (CD1, CD2, CD3...) and lists the releases they have in common first.</summary>
        private async Task AllDiscsSearchAsync(LookupSource source, string name, int version)
        {
            var perDisc = new List<List<ReleaseDetails>>();
            foreach (DiscToc disc in _tocs)
            {
                Status = string.Format("Looking up disc {0} of {1} in {2}...", disc.Disc, _tocs.Count, name);
                perDisc.Add(source == LookupSource.CueToolsDb
                    ? await _ctdb.LookupAsync(disc)
                    : await _client.LookupDiscAsync(disc));
                if (version != _searchVersion)
                    return;
            }

            int all;
            List<ReleaseDetails> releases;
            if (source == LookupSource.CueToolsDb)
            {
                releases = MultiDiscLookup.CombineCtdb(_tocs, perDisc);
                all = releases.Count(r => r.OnlyDisc == 0 && MultiDiscLookup.DiscsFound(r) == _tocs.Count);
            }
            else
            {
                releases = MultiDiscLookup.CombineMusicBrainz(perDisc, out all);
            }

            foreach (ReleaseDetails release in releases)
            {
                DiscToc only = release.OnlyDisc > 0 ? _tocs.FirstOrDefault(t => t.Disc == release.OnlyDisc) : null;
                ReleaseSearchResult row = release.ToSearchResult(only != null ? only.AudioTrackCount : TrackCount);
                _loaded[row.Id] = release;
                Results.Add(row);
            }

            if (releases.Count == 0)
            {
                Status = string.Format("None of the {0} discs is in {1}. Try another source or MusicBrainz (search).", _tocs.Count, name);
                return;
            }
            Status = all > 0
                ? string.Format("{0} releases match all {1} discs{2}.", all, _tocs.Count,
                                releases.Count > all ? string.Format(" (listed first), {0} only some", releases.Count - all) : "")
                : string.Format("No release matches all {0} discs; {1} match some. Pick one disc in the Disc list to look it up alone.", _tocs.Count, releases.Count);
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
                    details = selected.Id.StartsWith("discogs:", StringComparison.Ordinal)
                        ? await Discogs.GetReleaseAsync(selected.Id.Substring("discogs:".Length), cancellation.Token)
                        : await _client.GetReleaseAsync(selected.Id, cancellation.Token);
                    if (cancellation.IsCancellationRequested)
                        return;
                    _loaded[selected.Id] = details;
                    Status = null;
                }
                ShowDetails(details);
                await FillFromDiscogsAsync(details, cancellation);
                if (cancellation.IsCancellationRequested)
                    return;
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

        private readonly HashSet<ReleaseDetails> _filled = new HashSet<ReleaseDetails>();

        /// <summary>
        /// A CUETools DB Discogs match often has no label, catalog # or cover: they come from the full Discogs release
        /// (the track list stays, its lengths are from the disc's TOC).
        /// </summary>
        private async Task FillFromDiscogsAsync(ReleaseDetails details, CancellationTokenSource cancellation)
        {
            if (details.DiscogsId == null || details.Id.StartsWith("discogs:", StringComparison.Ordinal) || !_filled.Add(details))
                return;
            try
            {
                Status = "Loading label, catalog # and cover from Discogs...";
                ReleaseDetails full = await Discogs.GetReleaseAsync(details.DiscogsId, cancellation.Token);
                if (cancellation.IsCancellationRequested)
                    return;
                if (DiscogsClient.FillMissing(details, full) && SelectedDetails == details)
                {
                    RaisePropertyChanged("Summary", null, null);
                    ReleaseSearchResult row = Results.FirstOrDefault(r => r.Id == details.Id);
                    if (row != null)
                    {
                        // The results grid shows the new label / catalog #.
                        int index = Results.IndexOf(row);
                        ReleaseSearchResult updated = details.ToSearchResult(row.MatchesTrackCount ? row.TrackCount : -1);
                        updated.MatchesTrackCount = row.MatchesTrackCount;
                        Results[index] = updated;
                        SelectedResult = updated;
                    }
                }
                Status = null;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                if (!cancellation.IsCancellationRequested)
                    Status = null; // the CUETools DB data is still usable
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

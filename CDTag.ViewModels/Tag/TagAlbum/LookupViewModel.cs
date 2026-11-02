using System;
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
    public interface ILookupViewModel : IViewModelBase
    {
        string Artist { get; set; }
        string Album { get; set; }
        int TrackCount { get; set; }
        int DiscCount { get; set; }
        ReleaseDetails SelectedDetails { get; }
        byte[] Cover { get; }

        /// <summary>Starts the first search (called when the window opens).</summary>
        void Start();
    }

    /// <summary>
    /// Searches MusicBrainz for the album (or loads a pasted release link/ID), shows the track list of the
    /// selected release, and returns it with its front cover when the user clicks Apply.
    /// </summary>
    public class LookupViewModel : ViewModelBase, ILookupViewModel
    {
        private static readonly Regex ReleaseId = new Regex(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);

        private readonly MusicBrainzClient _client = new MusicBrainzClient();
        private CancellationTokenSource _detailsCancellation;
        private int _searchVersion;

        public LookupViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            Results = new ObservableCollection<ReleaseSearchResult>();
            Tracks = new ObservableCollection<ReleaseTrack>();
            SearchCommand = new DelegateCommand(() => Search(), () => !IsBusy);
            ApplyCommand = new DelegateCommand(Apply, () => SelectedDetails != null && !IsBusy);
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "SelectedResult")
                    LoadSelected();
            };
        }

        public string Artist { get { return Get<string>("Artist"); } set { Set("Artist", value); } }
        public string Album { get { return Get<string>("Album"); } set { Set("Album", value); } }

        /// <summary>Optional MusicBrainz release link or ID (loads that release directly).</summary>
        public string ReleaseLink { get { return Get<string>("ReleaseLink"); } set { Set("ReleaseLink", value); } }

        public int TrackCount { get; set; }
        public int DiscCount { get; set; }

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
                string discs = d.Discs > 1 ? d.Discs + " discs, " : "";
                return string.Format("{0} - {1} ({2}) · {3}{4} tracks · {5} {6}", d.Artist, d.Title, d.Date ?? "no date", discs, d.Tracks.Count, d.Label, d.CatalogNumber).Trim();
            }
        }

        public void Start()
        {
            Search();
        }

        private async void Search()
        {
            int version = ++_searchVersion;
            Results.Clear();
            Tracks.Clear();
            SelectedDetails = null;

            string link = ReleaseLink;
            Match id = ReleaseId.Match(link ?? "");
            IsBusy = true;
            try
            {
                if (id.Success)
                {
                    Status = "Loading release from MusicBrainz...";
                    ReleaseDetails details = await _client.GetReleaseAsync(id.Value.ToLowerInvariant());
                    if (version != _searchVersion)
                        return;
                    var row = new ReleaseSearchResult
                    {
                        Id = details.Id, Artist = details.Artist, Title = details.Title, Date = details.Date, Label = details.Label,
                        CatalogNumber = details.CatalogNumber, TrackCount = details.Tracks.Count, Score = 100,
                        Format = details.Discs > 1 ? details.Discs + "×" + details.Format : details.Format,
                        MatchesTrackCount = details.Tracks.Count == TrackCount
                    };
                    Results.Add(row);
                    ShowDetails(details);
                    Set("SelectedResult", row);
                    Status = null;
                    await LoadCoverAsync(details.Id);
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
            catch (Exception ex)
            {
                Status = "Lookup failed: " + Unwrap(ex).Message;
            }
            finally
            {
                if (version == _searchVersion)
                    IsBusy = false;
            }
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
            Status = "Loading track list...";
            try
            {
                ReleaseDetails details = await _client.GetReleaseAsync(selected.Id, cancellation.Token);
                if (cancellation.IsCancellationRequested)
                    return;
                ShowDetails(details);
                Status = null;
                await LoadCoverAsync(details.Id);
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

        private async Task LoadCoverAsync(string id)
        {
            try
            {
                byte[] cover = await _client.GetFrontCoverAsync(id);
                if (SelectedDetails != null && SelectedDetails.Id == id)
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
                await LoadCoverAsync(details.Id);
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

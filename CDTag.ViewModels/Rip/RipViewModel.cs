using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;
using CDTag.Model.Encoders;
using CDTag.Model.Lookup;
using CDTag.Model.Rip;
using IdSharp.Tagging.ID3v1;

namespace CDTag.ViewModels.Rip
{
    /// <summary>One row of the Rip track list (title and artist can be edited).</summary>
    public class RipTrack : INotifyPropertyChanged
    {
        private string _song, _artist, _status;

        public string Song { get { return _song; } set { _song = value; OnChanged("Song"); } }
        public string Artist { get { return _artist; } set { _artist = value; OnChanged("Artist"); } }
        public int Track { get; set; }
        public string Start { get; set; }
        public string Length { get; set; }
        public string Type { get; set; }
        public bool IsAudio { get; set; }
        public string Status { get { return _status; } set { _status = value; OnChanged("Status"); } }
        private string _pregap, _isrc;
        /// <summary>Pregap (INDEX 00 to INDEX 01), "?" when the drive gave no subchannel data.</summary>
        public string Pregap { get { return _pregap; } set { _pregap = value; OnChanged("Pregap"); } }
        public string Isrc { get { return _isrc; } set { _isrc = value; OnChanged("Isrc"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged(string name) { PropertyChangedEventHandler h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(name)); }
    }

    public class RipFormatItem
    {
        public RipFormat Format { get; set; }
        public EncodePreset Preset { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Rip tab: Find CD reads the disc's TOC and looks it up (MusicBrainz disc ID, CUETools DB); Rip CD rips the audio
    /// tracks (test & copy, offset corrected, AccurateRip checked) with the settings taken from Exact Audio Copy.
    /// </summary>
    public class RipViewModel : ViewModelBase
    {
        private List<CdTrack> _toc;
        private string _driveName, _vendor, _product;
        private CancellationTokenSource _cancellation;

        public RipViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            Drives = new ObservableCollection<string>();
            Tracks = new ObservableCollection<RipTrack>();
            Releases = new ObservableCollection<ReleaseDetails>();
            Genres = new ObservableCollection<string>(GenreHelper.GenreByIndex.Where(g => !string.IsNullOrEmpty(g)).OrderBy(g => g));
            Formats = new List<RipFormatItem>
            {
                new RipFormatItem { Format = RipFormat.Wav, Name = "WAV" },
                new RipFormatItem { Format = RipFormat.Flac, Name = "FLAC (-8, tagged)" },
                new RipFormatItem { Format = RipFormat.Mp3, Preset = EncodePreset.LameV0, Name = "MP3 LAME -V0" },
                new RipFormatItem { Format = RipFormat.Mp3, Preset = EncodePreset.Lame320, Name = "MP3 LAME -b 320" }
            };

            FindCDCommand = new DelegateCommand(() => FindCD(), () => !IsBusy && SelectedDrive != null);
            OptionsCommand = new DelegateCommand(() => ShowSettings = !ShowSettings);
            DriveCommand = new DelegateCommand(RefreshDrives, () => !IsBusy);
            RipCDCommand = new DelegateCommand(RipCD, () => !IsBusy && _toc != null && Tracks.Any(t => t.IsAudio));
            CancelCommand = new DelegateCommand(() => { if (_cancellation != null) _cancellation.Cancel(); }, () => IsBusy);
            BrowseOutputCommand = new DelegateCommand(BrowseOutput, () => !IsBusy);
            LoadEacSettingsCommand = new DelegateCommand(() => LoadSettings(null, null, null), () => !IsBusy);

            LoadSettings(null, null, null);
            RefreshDrives();
            StartDiscWatch();
        }

        public ObservableCollection<string> Drives { get; private set; }
        public ObservableCollection<RipTrack> Tracks { get; private set; }
        public ObservableCollection<ReleaseDetails> Releases { get; private set; }
        public ObservableCollection<string> Genres { get; private set; }
        public List<RipFormatItem> Formats { get; private set; }

        public string SelectedDrive { get { return Get<string>("SelectedDrive"); } set { Set("SelectedDrive", value); } }
        public string Artist { get { return Get<string>("Artist"); } set { Set("Artist", value); } }
        public string Album { get { return Get<string>("Album"); } set { Set("Album", value); } }
        public string Year { get { return Get<string>("Year"); } set { Set("Year", value); } }
        public string Genre { get { return Get<string>("Genre"); } set { Set("Genre", value); } }
        public bool IsVariousArtists { get { return Get<bool>("IsVariousArtists"); } set { Set("IsVariousArtists", value); } }
        public BitmapSource Cover { get { return Get<BitmapSource>("Cover"); } private set { Set("Cover", value); } }
        private byte[] _coverBytes;

        public ReleaseDetails SelectedRelease
        {
            get { return Get<ReleaseDetails>("SelectedRelease"); }
            set
            {
                if (SelectedRelease == value)
                    return;
                Set("SelectedRelease", value);
                if (value != null)
                    ApplyRelease(value);
            }
        }

        public bool HasReleases { get { return Get<bool>("HasReleases"); } private set { Set("HasReleases", value); } }
        public bool ShowSettings { get { return Get<bool>("ShowSettings"); } set { Set("ShowSettings", value); } }

        // ---- settings (from EAC)
        public string OutputFolder { get { return Get<string>("OutputFolder"); } set { Set("OutputFolder", value); } }
        public string Naming { get { return Get<string>("Naming"); } set { Set("Naming", value); } }
        public string VariousNaming { get { return Get<string>("VariousNaming"); } set { Set("VariousNaming", value); } }
        public RipFormatItem SelectedFormat { get { return Get<RipFormatItem>("SelectedFormat"); } set { Set("SelectedFormat", value); } }
        /// <summary>Read offset typed by the user; empty = from AccurateRip's drive database.</summary>
        public string OffsetText { get { return Get<string>("OffsetText"); } set { Set("OffsetText", value); } }
        public string OffsetInfo { get { return Get<string>("OffsetInfo"); } private set { Set("OffsetInfo", value); } }
        public bool TestAndCopy { get { return Get<bool>("TestAndCopy"); } set { Set("TestAndCopy", value); } }
        public bool EjectWhenDone { get { return Get<bool>("EjectWhenDone"); } set { Set("EjectWhenDone", value); } }

        /// <summary>Read speeds offered: 0 = maximum.</summary>
        public List<int> Speeds { get; } = new List<int> { 0, 48, 40, 32, 24, 16, 8, 4 };
        public int SelectedSpeed { get { return Get<int>("SelectedSpeed"); } set { Set("SelectedSpeed", value); } }
        /// <summary>Use the drive's C2 error pointers (from EAC's drive setting).</summary>
        public bool UseC2 { get { return Get<bool>("UseC2"); } set { Set("UseC2", value); } }
        /// <summary>False when the drive refused a C2 read in Find CD (the checkbox is then off and disabled).</summary>
        public bool C2Supported { get { return Get<bool>("C2Supported"); } private set { Set("C2Supported", value); } }
        /// <summary>Overread into the lead-in and lead-out (from EAC's drive setting).</summary>
        public bool Overread { get { return Get<bool>("Overread"); } set { Set("Overread", value); } }
        /// <summary>Copy Image &amp; Create CUE Sheet: one file for the whole disc.</summary>
        public bool RipImage { get { return Get<bool>("RipImage"); } set { Set("RipImage", value); } }
        public string SettingsSource { get { return Get<string>("SettingsSource"); } private set { Set("SettingsSource", value); } }
        private EacSettings _eac;
        private int? _databaseOffset;

        public bool IsBusy { get { return Get<bool>("IsBusy"); } private set { Set("IsBusy", value); } }
        public double Progress { get { return Get<double>("Progress"); } private set { Set("Progress", value); } }
        public string StatusText { get { return Get<string>("StatusText"); } private set { Set("StatusText", value); } }

        public ICommand FindCDCommand { get; private set; }
        public ICommand OptionsCommand { get; private set; }
        public ICommand DriveCommand { get; private set; }
        public ICommand RipCDCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand BrowseOutputCommand { get; private set; }
        public ICommand LoadEacSettingsCommand { get; private set; }

        private char? DriveLetter { get { return string.IsNullOrEmpty(SelectedDrive) ? (char?)null : SelectedDrive[0]; } }

        /// <summary>Lists CD/DVD drives, e.g. "S:\  Audio CD" (volume label shown when a disc is in).</summary>
        public void RefreshDrives()
        {
            string previous = SelectedDrive;
            Drives.Clear();
            foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.CDRom))
            {
                string label = null;
                try
                {
                    if (drive.IsReady)
                        label = drive.VolumeLabel;
                }
                catch (IOException)
                {
                }
                Drives.Add(string.IsNullOrEmpty(label) ? drive.Name : string.Format("{0}  {1}", drive.Name, label));
            }
            SelectedDrive = Drives.FirstOrDefault(d => previous != null && d[0] == previous[0])
                         ?? Drives.FirstOrDefault(d => d.Contains("  ")) ?? Drives.FirstOrDefault();
        }

        /// <summary>Takes the output folder, naming, format and eject setting from EAC (and the drive's offset when given).</summary>
        private void LoadSettings(string vendor, string product, string revision)
        {
            _eac = EacSettings.Load(vendor, product, revision);
            RipSettings s = RipSettings.FromEac(_eac);
            OutputFolder = s.OutputFolder;
            Naming = s.Naming;
            VariousNaming = s.VariousNaming;
            SelectedFormat = Formats.First(f => f.Format == s.Format && (f.Format != RipFormat.Mp3 || f.Preset == EncodePreset.LameV0));
            OffsetText = s.ReadOffset.HasValue ? s.ReadOffset.Value.ToString(CultureInfo.InvariantCulture) : "";
            TestAndCopy = true;
            EjectWhenDone = s.EjectWhenDone;
            SettingsSource = _eac.Found ? "Settings from Exact Audio Copy" + (_eac.DriveKey != null && _eac.DriveOffset.HasValue ? " (drive " + _eac.DriveKey.Trim() + ")" : "") : "Exact Audio Copy not found: default settings";
            UpdateOffsetInfo();
        }

        private void UpdateOffsetInfo()
        {
            OffsetInfo = _databaseOffset.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "Empty = AccurateRip drive database: {0:+0;-0;0}", _databaseOffset.Value)
                : "Empty = AccurateRip drive database (looked up by Find CD)";
        }

        private void BrowseOutput()
        {
            string folder;
            if (_dialogService.ShowFolderDialog("Rip to", OutputFolder, null, out folder) == true && !string.IsNullOrEmpty(folder))
                OutputFolder = folder;
        }

        private async Task FindCD()
        {
            char? letter = DriveLetter;
            if (letter == null)
                return;
            try
            {
                var info = new DriveInfo(letter.Value.ToString());
                _lookedUpDisc = info.IsReady ? letter.Value + "|" + info.VolumeLabel : null;   // the disc watch won't look it up again
            }
            catch (IOException)
            {
            }
            IsBusy = true;
            CommandManager.InvalidateRequerySuggested();
            Tracks.Clear();
            Releases.Clear();
            HasReleases = false;
            Cover = null; _coverBytes = null;
            _toc = null;
            Progress = 0;
            try
            {
                string vendor, product, revision;
                List<CdTrack> toc;
                StatusText = "Reading the disc...";
                _extras = null;
                using (CdDrive drive = await Task.Run(() => CdDrive.Open(letter.Value.ToString())))
                {
                    toc = await Task.Run(() => drive.ReadToc());
                    _driveName = drive.Name;
                    vendor = drive.Vendor; product = drive.Product;
                    string name = drive.Name ?? "";
                    revision = name.StartsWith(vendor + " " + product + " ", StringComparison.Ordinal) ? name.Substring(vendor.Length + product.Length + 2) : "";
                    foreach (CdTrack t in toc)
                    {
                        Tracks.Add(new RipTrack
                        {
                            Track = t.Number, IsAudio = t.IsAudio, Type = t.IsAudio ? "Audio" : "Data",
                            Start = Time(t.StartSector), Length = Time(t.Sectors), Song = t.IsAudio ? "" : "(data track, not ripped)"
                        });
                    }

                    CdTrack firstAudio = toc.FirstOrDefault(t => t.IsAudio);
                    C2Supported = firstAudio != null && await Task.Run(() => drive.SupportsC2(firstAudio.StartSector + 75));

                    // ISRCs, catalog number, CD-Text and pregaps (a few seconds, from the subchannel)
                    var steps = new Progress<string>(m => StatusText = m);
                    try
                    {
                        _extras = await Task.Run(() => GapDetector.Read(drive, toc, steps, CancellationToken.None));
                    }
                    catch (IOException)
                    {
                    }
                }
                _toc = toc;
                if (_extras != null)
                {
                    foreach (RipTrack row in Tracks.Where(t => t.IsAudio))
                    {
                        int gap = _extras.Pregap(row.Track);
                        row.Pregap = _extras.GapsDetected ? Time(gap) : "?";
                        string isrc;
                        row.Isrc = _extras.Isrc.TryGetValue(row.Track, out isrc) ? isrc : "";
                    }
                }
                CommandManager.InvalidateRequerySuggested();

                string typed = OffsetText;
                LoadSettings(vendor, product, revision);
                _vendor = vendor; _product = product;
                // the drive's own settings: saved in CD-Tag for this drive, otherwise EAC's for it
                DriveSettings saved = DriveSettingsStore.Load(vendor, product);
                if (saved != null)
                {
                    OffsetText = saved.Offset.HasValue ? saved.Offset.Value.ToString(CultureInfo.InvariantCulture) : "";
                    SelectedSpeed = Speeds.Contains(saved.Speed) ? saved.Speed : 0;
                    UseC2 = saved.UseC2;
                    Overread = saved.Overread;
                    SettingsSource += "; drive settings saved in CD-Tag";
                }
                if (!C2Supported)
                    UseC2 = false;
                else
                {
                    SelectedSpeed = 0;
                    UseC2 = _eac != null && _eac.UseC2 == true;
                    Overread = _eac != null && _eac.OverreadLeadOut == true;
                }
                if (!string.IsNullOrWhiteSpace(typed))
                    OffsetText = typed;
                StatusText = "Looking up the drive offset and the disc...";
                _databaseOffset = await AccurateRip.FindDriveOffsetAsync(vendor, product);
                UpdateOffsetInfo();

                DiscToc disc = RipJob.ToDiscToc(toc);
                var found = new List<ReleaseDetails>();
                foreach (Func<Task<List<ReleaseDetails>>> source in MetadataSources(disc))
                {
                    try { found.AddRange(await source()); }
                    catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException || ex is IOException || ex is System.Xml.XmlException) { }
                }
                int audioCount = toc.Count(t => t.IsAudio);
                ReleaseDetails cdText = CdTextRelease(_extras, toc);
                if (cdText != null)
                    found.Add(cdText);
                // MusicBrainz also returns discs with a similar TOC: releases with this disc's track count come first
                // (in the order the services ranked them).
                foreach (ReleaseDetails r in found.Where(r => r.Tracks != null && r.Tracks.Count > 0)
                                                  .GroupBy(r => (r.Artist + "|" + r.Title + "|" + r.Date + "|" + r.Source).ToLowerInvariant()).Select(g => g.First())
                                                  .OrderByDescending(r => MediumTracks(r).Count == audioCount))
                    Releases.Add(r);
                HasReleases = Releases.Count > 1;
                StatusText = string.Format(CultureInfo.InvariantCulture, "{0}: {1} audio tracks{2}. {3}", _driveName, audioCount,
                    toc.Any(t => !t.IsAudio) ? " and a data track" : "",
                    Releases.Count == 0 ? "Not found online: type the album details." : Releases.Count == 1 ? "Found 1 release." : string.Format("Found {0} releases: choose one.", Releases.Count));
                if (Releases.Count > 0)
                    SelectedRelease = Releases[0];
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                StatusText = ex.Message;
            }
            finally
            {
                IsBusy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>
        /// The metadata sources in order: EAC's own provider first (CTDB with the plugin's search mode, or freedb = gnudb),
        /// then MusicBrainz (disc ID), CUETools DB and gnudb (only with a gnudb address in Options).
        /// </summary>
        private List<Func<Task<List<ReleaseDetails>>>> MetadataSources(DiscToc disc)
        {
            string ctdbMode = _eac != null ? _eac.CtdbSearch : "Extensive";
            var gnudb = new GnudbClient(CDTag.Common.Settings.AppOptions.Current.GnudbEmail);
            Func<Task<List<ReleaseDetails>>> ctdb = () => new CueToolsDbClient().LookupAsync(disc, CancellationToken.None, ctdbMode);
            Func<Task<List<ReleaseDetails>>> mb = () => new MusicBrainzClient().LookupDiscAsync(disc);
            Func<Task<List<ReleaseDetails>>> gnu = () => gnudb.LookupAsync(disc);
            string provider = _eac != null ? _eac.MetadataProvider : null;
            var sources = provider == "freedb" ? new List<Func<Task<List<ReleaseDetails>>>> { gnu, mb, ctdb }
                        : provider == "CTDB" ? new List<Func<Task<List<ReleaseDetails>>>> { ctdb, mb, gnu }
                        : new List<Func<Task<List<ReleaseDetails>>>> { mb, ctdb, gnu };
            MetadataSourcesText = string.Join(", ", (provider == "freedb" ? new[] { "gnudb", "MusicBrainz", "CUETools DB" }
                                                    : provider == "CTDB" ? new[] { "CUETools DB (" + ctdbMode + ", as in EAC)", "MusicBrainz", "gnudb" }
                                                    : new[] { "MusicBrainz", "CUETools DB", "gnudb" })
                                                   .Where(n => n != "gnudb" || gnudb.IsEnabled));
            return sources;
        }

        /// <summary>"CUETools DB (Extensive, as in EAC), MusicBrainz": the sources Find CD asks, in order.</summary>
        public string MetadataSourcesText { get { return Get<string>("MetadataSourcesText"); } private set { Set("MetadataSourcesText", value); } }

        // ---- automatic Find CD: watch the drive and look the disc up when one is put in
        private System.Windows.Threading.DispatcherTimer _discWatch;
        /// <summary>Drive letter + volume label of the disc Find CD last ran for (automatically or not); null = no disc.</summary>
        private string _lookedUpDisc;

        /// <summary>Every 2 seconds: a new disc in the selected drive is looked up once; taking it out clears the form.</summary>
        private void StartDiscWatch()
        {
            _discWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _discWatch.Tick += async (s, e) =>
            {
                if (IsBusy || DriveLetter == null)
                    return;
                char letter = DriveLetter.Value;
                string disc = null;
                try
                {
                    disc = await Task.Run(() =>
                    {
                        var info = new DriveInfo(letter.ToString());
                        return info.IsReady ? letter + "|" + info.VolumeLabel : null;
                    });
                }
                catch (IOException)
                {
                }
                if (IsBusy || disc == _lookedUpDisc)
                    return;
                _lookedUpDisc = disc;
                if (disc != null)
                {
                    await FindCD();
                }
                else
                {
                    _toc = null;
                    Tracks.Clear(); Releases.Clear(); HasReleases = false; Cover = null; _coverBytes = null;
                    StatusText = "No disc in " + letter + ":";
                    CommandManager.InvalidateRequerySuggested();
                }
            };
            _discWatch.Start();
        }

        private async void ApplyRelease(ReleaseDetails r)
        {
            Artist = r.IsVariousArtists ? "Various Artists" : r.Artist;
            Album = r.Title;
            Year = string.IsNullOrEmpty(r.Date) ? "" : r.Date.Length >= 4 ? r.Date.Substring(0, 4) : r.Date;
            if (!string.IsNullOrWhiteSpace(r.Genre))
                Genre = r.Genre;
            IsVariousArtists = r.IsVariousArtists;
            List<ReleaseTrack> mediumTracks = MediumTracks(r);
            int i = 0;
            foreach (RipTrack row in Tracks.Where(t => t.IsAudio))
            {
                ReleaseTrack t = i < mediumTracks.Count ? mediumTracks[i] : null;
                row.Song = t != null ? t.Title : row.Song;
                row.Artist = t != null && !string.IsNullOrWhiteSpace(t.Artist) && t.Artist != Artist ? t.Artist : "";
                i++;
            }

            Cover = null; _coverBytes = null;
            try
            {
                byte[] bytes = r.MusicBrainzId != null ? await new MusicBrainzClient().GetFrontCoverAsync(r.MusicBrainzId)
                             : r.CoverUrl != null ? await CueToolsDbClient.GetImageAsync(r.CoverUrl) : null;
                if (bytes != null && SelectedRelease == r)
                {
                    var image = new BitmapImage();
                    image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = new MemoryStream(bytes); image.EndInit(); image.Freeze();
                    Cover = image;
                    _coverBytes = bytes;
                }
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException || ex is IOException || ex is NotSupportedException)
            {
            }
        }

        private DiscExtras _extras;

        /// <summary>The disc's own CD-Text as a release (source "CD-Text"), or null when it has none.</summary>
        private static ReleaseDetails CdTextRelease(DiscExtras extras, List<CdTrack> toc)
        {
            CdText text = extras == null ? null : extras.CdText;
            if (text == null || (text.AlbumTitle == null && text.AlbumPerformer == null))
                return null;
            var r = new ReleaseDetails
            {
                Artist = text.AlbumPerformer ?? "", Title = text.AlbumTitle ?? "", Source = "CD-Text", Format = "CD", Discs = 1,
                Tracks = new List<ReleaseTrack>()
            };
            int i = 0;
            foreach (CdTrack t in toc.Where(t => t.IsAudio))
            {
                string performer = text.Performer(t.Number);
                r.Tracks.Add(new ReleaseTrack { Disc = 1, Position = ++i, Title = text.Title(t.Number) ?? "", Artist = performer ?? r.Artist, Length = t.Sectors / 75 });
                if (performer != null && !string.Equals(performer, r.Artist, StringComparison.OrdinalIgnoreCase))
                    r.IsVariousArtists = true;
            }
            return r;
        }

        /// <summary>The tracks of the disc that is in the drive (disc 1, or the disc a disc-ID lookup matched).</summary>
        private static List<ReleaseTrack> MediumTracks(ReleaseDetails r)
        {
            var tracks = r.Tracks.Where(t => r.OnlyDisc == 0 ? t.Disc <= 1 : t.Disc == r.OnlyDisc).OrderBy(t => t.Position).ToList();
            return tracks.Count == 0 ? r.Tracks : tracks;
        }

        private RipAlbum CurrentAlbum()
        {
            var album = new RipAlbum { Artist = Artist, Title = Album, Year = Year, Genre = Genre, IsVariousArtists = IsVariousArtists };
            foreach (RipTrack t in Tracks.Where(t => t.IsAudio))
                album.Tracks.Add(new KeyValuePair<string, string>(t.Song, t.Artist));
            return album;
        }

        private async void RipCD()
        {
            if (string.IsNullOrWhiteSpace(Artist) || string.IsNullOrWhiteSpace(Album))
            {
                MessageBox("Type the artist and album first (or use Find CD).", "Rip CD", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            int offset;
            string offsetSource;
            if (!string.IsNullOrWhiteSpace(OffsetText))
            {
                if (!int.TryParse(OffsetText.Trim().TrimStart('+'), NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
                {
                    MessageBox("The read offset must be a whole number of samples, e.g. 6 or -24.", "Rip CD", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                offsetSource = "set in CD-Tag";
            }
            else if (_databaseOffset.HasValue)
            {
                offset = _databaseOffset.Value;
                offsetSource = "AccurateRip drive database";
            }
            else
            {
                if (MessageBox("This drive's read offset is not in the AccurateRip database and none is set. Rip with offset 0? AccurateRip results will not match if the drive has an offset.",
                               "Rip CD", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                offset = 0;
                offsetSource = "none set";
            }
            if ((SelectedFormat.Format == RipFormat.Flac && AudioEncoder.FlacPath == null) || (SelectedFormat.Format == RipFormat.Mp3 && AudioEncoder.LamePath == null))
            {
                MessageBox((SelectedFormat.Format == RipFormat.Flac ? "flac.exe" : "lame.exe") + " not found. Choose it in Tools > Options.", "Rip CD", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var settings = new RipSettings
            {
                Eac = _eac, OutputFolder = OutputFolder, Naming = Naming, VariousNaming = VariousNaming, UseVariousNaming = _eac == null || _eac.UseVariousNaming,
                Format = SelectedFormat.Format, Mp3Preset = SelectedFormat.Preset, TestAndCopy = TestAndCopy, EjectWhenDone = EjectWhenDone,
                Speed = SelectedSpeed, UseC2 = UseC2, Overread = Overread, Image = RipImage
            };
            if (_vendor != null)
            {
                int typedOffset;
                DriveSettingsStore.Save(_vendor, _product, new DriveSettings
                {
                    Offset = int.TryParse((OffsetText ?? "").Trim().TrimStart('+'), NumberStyles.Integer, CultureInfo.InvariantCulture, out typedOffset) ? typedOffset : (int?)null,
                    Speed = SelectedSpeed, UseC2 = UseC2, Overread = Overread
                });
            }
            var job = new RipJob { Toc = _toc, Album = CurrentAlbum(), Settings = settings, DriveName = _driveName, Offset = offset, OffsetSource = offsetSource, Extras = _extras };
            List<string> paths = job.OutputPaths();
            string existing = paths.FirstOrDefault(File.Exists);
            if (existing != null)
            {
                MessageBox(existing + " already exists. Choose another folder or naming, or move the old files.", "Rip CD", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<RipTrack> rows = Tracks.Where(t => t.IsAudio).ToList();
            foreach (RipTrack row in rows)
                row.Status = "Waiting";
            _cancellation = new CancellationTokenSource();
            IsBusy = true;
            Progress = 0;
            CommandManager.InvalidateRequerySuggested();
            StatusText = "Ripping to " + Path.GetDirectoryName(paths[0]);
            var progress = new Progress<RipJobProgress>(p =>
            {
                rows[p.TrackIndex].Status = p.Status;
                Progress = p.Total;
            });
            char letter = DriveLetter.Value;
            try
            {
                using (CdDrive drive = CdDrive.Open(letter.ToString()))
                {
                    await job.RunAsync(drive, progress, _cancellation.Token);
                    if (_coverBytes != null)
                    {
                        string folderJpg = Path.Combine(Path.GetDirectoryName(paths[0]), "folder.jpg");
                        if (!File.Exists(folderJpg))
                            File.WriteAllBytes(folderJpg, _coverBytes);
                    }
                    for (int i = 0; i < rows.Count && i < job.AccurateRipLines.Count; i++)
                    {
                        RipTrackResult r = job.Results[i];
                        string ar = job.AccurateRipLines[i];
                        string ctdb = i < job.CtdbLines.Count ? job.CtdbLines[i] : "";
                        rows[i].Status = (!r.CrcsMatch ? "CRC mismatch. " : r.Suspicious.Count > 0 ? "Suspicious. " : "OK. ") +
                                         (ar.StartsWith("Accurately", StringComparison.Ordinal) ? "AR " + ar.Substring(ar.IndexOf('(') + 1, ar.IndexOf(')') - ar.IndexOf('(') - 1) : ar.Split(new[] { "  " }, StringSplitOptions.None)[0]) +
                                         (ctdb.Contains("accurately") ? ", CTDB " + ctdb.Substring(ctdb.IndexOf('(') + 1, ctdb.IndexOf(')') - ctdb.IndexOf('(') - 1)
                                          : ctdb.Length > 0 ? ", " + ctdb.Split(new[] { "  " }, StringSplitOptions.None)[0] : "");
                    }
                    if (EjectWhenDone)
                        drive.Eject();
                }
                Progress = 100;
                StatusText = string.Format(CultureInfo.InvariantCulture, "Done: {0} of {1} tracks accurately ripped (AccurateRip){2}{3}. Log: {4}", job.AccurateTracks, rows.Count,
                                           job.CtdbFound ? string.Format(CultureInfo.InvariantCulture, ", {0} match CUETools DB", job.CtdbTracks) : "",
                                           job.HadErrors ? ", there were errors" : "", job.LogPath);
            }
            catch (OperationCanceledException)
            {
                foreach (RipTrack row in rows.Where(r => r.Status != null && !r.Status.StartsWith("OK", StringComparison.Ordinal)))
                    row.Status = "Cancelled";
                StatusText = "Cancelled.";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                StatusText = "Error: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                _cancellation.Dispose();
                _cancellation = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private static string Time(int sectors)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:00}", sectors / 4500, sectors / 75 % 60, sectors % 75);
        }
    }
}

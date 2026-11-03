using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;
using CDTag.Model.Checksum;
using CDTag.Model.Cue;
using CDTag.Model.Encoders;

namespace CDTag.ViewModels.Tools
{
    public class CueFileItem
    {
        public string FullPath { get; set; }
        public string Name { get; set; }
    }

    /// <summary>One output track in the Split CUE list.</summary>
    public class SplitRow : INotifyPropertyChanged
    {
        private string _status;

        public SplitTrack Track { get; set; }
        public string Number { get; set; }
        public string FileName { get; set; }
        public string Start { get; set; }
        public string Length { get; set; }

        public string Status
        {
            get { return _status; }
            set
            {
                _status = value;
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null)
                    handler(this, new PropertyChangedEventArgs("Status"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class SplitFormatItem
    {
        public SplitFormat Format { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Split CUE: splits a CUE image (one WAV or FLAC file) into tracks, as WAV or tagged FLAC, with gaps appended to the previous track.
    /// </summary>
    public class SplitCueViewModel : ViewModelBase, ISplitCueViewModel
    {
        private const string CueFilter = "CUE sheets (*.cue)|*.cue|All files (*.*)|*.*";
        private CueSheet _sheet;
        private string _image;
        private int _sampleRate = 44100;
        private CancellationTokenSource _cancellation;

        public SplitCueViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            CueFiles = new ObservableCollection<CueFileItem>();
            Rows = new ObservableCollection<SplitRow>();
            Formats = new List<SplitFormatItem>
            {
                new SplitFormatItem { Format = SplitFormat.Flac, Name = "FLAC (-8, tagged from the CUE sheet)" },
                new SplitFormatItem { Format = SplitFormat.Wav, Name = "WAV" }
            };
            Set("SelectedFormat", Formats[0]);

            OpenCommand = new DelegateCommand(Open, () => !IsBusy);
            BrowseOutputCommand = new DelegateCommand(BrowseOutput, () => !IsBusy);
            SplitCommand = new DelegateCommand(Split, () => !IsBusy && _image != null && Rows.Count > 0);
            CancelCommand = new DelegateCommand(() => { if (_cancellation != null) _cancellation.Cancel(); }, () => IsBusy);
            CloseCommand = new DelegateCommand(() => { if (_cancellation != null) _cancellation.Cancel(); CloseWindow(true); });
        }

        public ObservableCollection<CueFileItem> CueFiles { get; private set; }
        public ObservableCollection<SplitRow> Rows { get; private set; }
        public List<SplitFormatItem> Formats { get; private set; }

        public bool HasSplit { get; private set; }

        public CueFileItem SelectedCue
        {
            get { return Get<CueFileItem>("SelectedCue"); }
            set
            {
                if (SelectedCue == value)
                    return;
                Set("SelectedCue", value);
                ReadCue();
            }
        }

        public SplitFormatItem SelectedFormat
        {
            get { return Get<SplitFormatItem>("SelectedFormat"); }
            set
            {
                Set("SelectedFormat", value);
                BuildRows();
            }
        }

        public string OutputFolder
        {
            get { return Get<string>("OutputFolder"); }
            set { Set("OutputFolder", value); }
        }

        /// <summary>Audio before track 1 exists: offer to save it as track 00.</summary>
        public bool HasHiddenTrack
        {
            get { return Get<bool>("HasHiddenTrack"); }
            private set { Set("HasHiddenTrack", value); }
        }

        public bool SaveHiddenTrack
        {
            get { return Get<bool>("SaveHiddenTrack"); }
            set
            {
                Set("SaveHiddenTrack", value);
                BuildRows();
            }
        }

        public string HiddenTrackText
        {
            get { return Get<string>("HiddenTrackText"); }
            private set { Set("HiddenTrackText", value); }
        }

        /// <summary>Album, image file and track count, or why the sheet can't be split.</summary>
        public string Info
        {
            get { return Get<string>("Info"); }
            private set { Set("Info", value); }
        }

        public bool IsBusy
        {
            get { return Get<bool>("IsBusy"); }
            private set { Set("IsBusy", value); }
        }

        public double Progress
        {
            get { return Get<double>("Progress"); }
            private set { Set("Progress", value); }
        }

        public string StatusText
        {
            get { return Get<string>("StatusText"); }
            private set { Set("StatusText", value); }
        }

        public ICommand OpenCommand { get; private set; }
        public ICommand BrowseOutputCommand { get; private set; }
        public ICommand SplitCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand CloseCommand { get; private set; }

        public void Load(string folder, string cueFile = null)
        {
            CueFiles.Clear();
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                foreach (string file in Directory.GetFiles(folder, "*.cue").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    CueFiles.Add(new CueFileItem { FullPath = file, Name = Path.GetFileName(file) });
            }
            if (cueFile != null && !CueFiles.Any(f => string.Equals(f.FullPath, cueFile, StringComparison.OrdinalIgnoreCase)))
                CueFiles.Insert(0, new CueFileItem { FullPath = cueFile, Name = Path.GetFileName(cueFile) });

            CueFileItem selected = cueFile != null ? CueFiles.First(f => string.Equals(f.FullPath, cueFile, StringComparison.OrdinalIgnoreCase)) : CueFiles.FirstOrDefault();
            Set<CueFileItem>("SelectedCue", null);
            if (selected != null)
            {
                SelectedCue = selected;
            }
            else
            {
                ReadCue();
                Info = string.IsNullOrEmpty(folder) ? "Open a CUE sheet." : "No .cue files in this folder. Click Open to choose one.";
            }
        }

        private void Open()
        {
            string fileName;
            if (ShowOpenFileDialog("Open CUE sheet", CueFilter, out fileName) == true && !string.IsNullOrEmpty(fileName))
                Load(Path.GetDirectoryName(fileName), fileName);
        }

        private void BrowseOutput()
        {
            string folder;
            if (_dialogService.ShowFolderDialog("Write the tracks to", OutputFolder, null, out folder) == true && !string.IsNullOrEmpty(folder))
                OutputFolder = folder;
        }

        private void ReadCue()
        {
            _sheet = null;
            _image = null;
            Rows.Clear();
            HasHiddenTrack = false;
            StatusText = null;
            Progress = 0;
            if (SelectedCue == null)
            {
                Info = null;
                return;
            }

            try
            {
                _sheet = CueSheet.Load(SelectedCue.FullPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Info = "Cannot read " + SelectedCue.Name + ": " + ex.Message;
                return;
            }
            OutputFolder = Path.GetDirectoryName(SelectedCue.FullPath);

            int audioTracks = _sheet.Tracks.Count(t => t.IsAudio);
            if (audioTracks == 0)
            {
                Info = SelectedCue.Name + " has no audio tracks.";
                return;
            }
            if (!_sheet.IsImage)
            {
                Info = string.Format("{0} lists {1} audio files: the album is already split into tracks.", SelectedCue.Name, _sheet.Files.Count);
                return;
            }

            _image = _sheet.FindImage();
            if (_image == null)
            {
                Info = string.Format("The image file {0} was not found next to the CUE sheet.", _sheet.Files[0]);
                return;
            }
            string extension = Path.GetExtension(_image).ToLowerInvariant();
            if (extension != ".wav" && extension != ".flac")
            {
                Info = Path.GetFileName(_image) + ": only WAV and FLAC images can be split.";
                _image = null;
                return;
            }

            long samples;
            if (!CueSplitter.TryGetLength(_image, out samples, out _sampleRate))
            {
                Info = "Cannot read the audio format of " + Path.GetFileName(_image) + ".";
                _image = null;
                return;
            }

            HasHiddenTrack = _sheet.HiddenTrackFrames > 0;
            HiddenTrackText = HasHiddenTrack ? string.Format("Save the audio before track 1 ({0}) as track 00", CueSheet.FormatTime(_sheet.HiddenTrackFrames)) : null;
            string album = string.Join(" - ", new[] { _sheet.Performer, _sheet.Title }.Where(s => !string.IsNullOrWhiteSpace(s)));
            Info = string.Format("{0}{1} tracks from {2} ({3}).", album.Length > 0 ? album + ": " : "", audioTracks, Path.GetFileName(_image),
                                 CueSheet.FormatTime(samples * CueSheet.FramesPerSecond / Math.Max(1, _sampleRate)));
            _imageSamples = samples;
            BuildRows();
        }

        private long _imageSamples;

        private void BuildRows()
        {
            if (IsBusy)
                return;
            Rows.Clear();
            if (_sheet == null || _image == null || SelectedFormat == null)
                return;
            foreach (SplitTrack track in CueSplitter.Plan(_sheet, _sampleRate, SelectedFormat.Format, HasHiddenTrack && SaveHiddenTrack))
            {
                long end = track.EndSample < 0 ? _imageSamples : track.EndSample;
                Rows.Add(new SplitRow
                {
                    Track = track,
                    Number = track.Number.ToString("00"),
                    FileName = track.FileName,
                    Start = CueSheet.FormatTime(track.StartSample * CueSheet.FramesPerSecond / _sampleRate),
                    Length = CueSheet.FormatTime(Math.Max(0, end - track.StartSample) * CueSheet.FramesPerSecond / _sampleRate)
                });
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private async void Split()
        {
            SplitFormat format = SelectedFormat.Format;
            if (format == SplitFormat.Flac || string.Equals(Path.GetExtension(_image), ".flac", StringComparison.OrdinalIgnoreCase))
            {
                if (AudioEncoder.FlacPath == null)
                {
                    MessageBox("flac.exe not found. Put it in the Encoders folder next to cdtag.exe, or choose it in Tools > Options.", "Split CUE",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            string output = string.IsNullOrWhiteSpace(OutputFolder) ? Path.GetDirectoryName(SelectedCue.FullPath) : OutputFolder.Trim();
            List<SplitRow> rows = Rows.ToList();
            foreach (SplitRow row in rows)
                row.Status = "Waiting";

            _cancellation = new CancellationTokenSource();
            IsBusy = true;
            CommandManager.InvalidateRequerySuggested();
            var progress = new Progress<Tuple<int, int>>(p =>
            {
                if (p.Item1 < 0)
                {
                    StatusText = "Decoding " + Path.GetFileName(_image) + "...";
                    return;
                }
                SplitRow row = rows[p.Item1];
                row.Status = p.Item2 >= 100 ? "Done" : (format == SplitFormat.Flac && p.Item2 >= 50 ? "Encoding " : "Writing ") + p.Item2 + "%";
                Progress = 100.0 * (p.Item1 + p.Item2 / 100.0) / rows.Count;
                StatusText = string.Format("Track {0} of {1}: {2}", p.Item1 + 1, rows.Count, row.FileName);
            });
            try
            {
                await CueSplitter.SplitAsync(_sheet, _image, output, format, HasHiddenTrack && SaveHiddenTrack, progress, _cancellation.Token);
                HasSplit = true;
                Progress = 100;
                StatusText = string.Format("Wrote {0} tracks to {1}.", rows.Count, output);
            }
            catch (OperationCanceledException)
            {
                foreach (SplitRow row in rows)
                    row.Status = "Cancelled";
                StatusText = "Cancelled; nothing was kept.";
                Progress = 0;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is System.ComponentModel.Win32Exception)
            {
                foreach (SplitRow row in rows.Where(r => r.Status != "Done"))
                    row.Status = null;
                StatusText = "Error: " + ex.Message;
                Progress = 0;
            }
            finally
            {
                IsBusy = false;
                _cancellation.Dispose();
                _cancellation = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}

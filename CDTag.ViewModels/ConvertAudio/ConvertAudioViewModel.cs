using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;
using CDTag.Common.Settings;
using CDTag.Model.Encoders;
using CDTag.Views.Interfaces.Options;

namespace CDTag.ViewModels.ConvertAudio
{
    /// <summary>One file in the Convert Audio queue.</summary>
    public class ConvertFile : INotifyPropertyChanged
    {
        private string _status;

        public string FullPath { get; set; }
        public string Name { get { return Path.GetFileName(FullPath); } }
        public string Directory { get { return Path.GetDirectoryName(FullPath); } }
        public long Size { get; set; }

        /// <summary>"Encoding 42%", "Done", "Skipped: ... exists", "Error: ...".</summary>
        public string Status
        {
            get { return _status; }
            set
            {
                if (_status == value)
                    return;
                _status = value;
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null)
                    handler(this, new PropertyChangedEventArgs("Status"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>A Convert Audio preset ("LAME 3.100 -V0 (CD, vinyl...)").</summary>
    public class EncodePresetItem
    {
        public EncodePreset Preset { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Convert Audio tab (CD-Tag 2.x layout): a queue of files encoded to MP3 with LAME (WAV/FLAC sources) or
    /// decoded to WAV (FLAC/MP3 sources). Output goes next to each source; existing files are not overwritten.
    /// </summary>
    public class ConvertAudioViewModel : ViewModelBase
    {
        private const string AudioFilter = "Audio files (*.wav;*.flac;*.mp3)|*.wav;*.flac;*.mp3|All files (*.*)|*.*";
        private CancellationTokenSource _cancellation;

        public ConvertAudioViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            Files = new ObservableCollection<ConvertFile>();
            Presets = new List<EncodePresetItem>
            {
                new EncodePresetItem { Preset = EncodePreset.LameV0, Name = "LAME -V0 (CD, vinyl...)" },
                new EncodePresetItem { Preset = EncodePreset.Lame320, Name = "LAME -b 320 (WEB lossless)" }
            };
            EncodePreset saved;
            SelectedPreset = Enum.TryParse(AppOptions.Current.EncodePreset, out saved) ? Presets.First(p => p.Preset == saved) : Presets[0];

            AddCommand = new DelegateCommand(Add, () => !IsBusy);
            RemoveCommand = new DelegateCommand(Remove, () => !IsBusy && SelectedFile != null);
            ClearCommand = new DelegateCommand(() => Files.Clear(), () => !IsBusy && Files.Count > 0);
            MoveUpCommand = new DelegateCommand(() => Move(-1), () => !IsBusy && SelectedFile != null && Files.IndexOf(SelectedFile) > 0);
            MoveDownCommand = new DelegateCommand(() => Move(1), () => !IsBusy && SelectedFile != null && Files.IndexOf(SelectedFile) < Files.Count - 1);
            OptionsCommand = new DelegateCommand(() => ShowWindow<IOptionsWindow>(), () => !IsBusy);
            EncodeCommand = new DelegateCommand(() => Run(encode: true), () => !IsBusy && Files.Any(f => AudioEncoder.CanEncode(f.FullPath)));
            DecodeCommand = new DelegateCommand(() => Run(encode: false), () => !IsBusy && Files.Any(f => AudioEncoder.CanDecode(f.FullPath)));
            CancelCommand = new DelegateCommand(() => { if (_cancellation != null) _cancellation.Cancel(); }, () => IsBusy);
        }

        public ObservableCollection<ConvertFile> Files { get; private set; }

        public List<EncodePresetItem> Presets { get; private set; }

        public EncodePresetItem SelectedPreset
        {
            get { return Get<EncodePresetItem>("SelectedPreset"); }
            set
            {
                Set("SelectedPreset", value);
                if (value != null && AppOptions.Current.EncodePreset != value.Preset.ToString())
                {
                    AppOptionsData options = AppOptions.Current.Clone();
                    options.EncodePreset = value.Preset.ToString();
                    AppOptions.Save(options);
                }
            }
        }

        public ConvertFile SelectedFile
        {
            get { return Get<ConvertFile>("SelectedFile"); }
            set { Set("SelectedFile", value); }
        }

        public bool IsBusy
        {
            get { return Get<bool>("IsBusy"); }
            private set { Set("IsBusy", value); }
        }

        /// <summary>Status bar text: the encoder in use, or the progress of the queue.</summary>
        public string StatusText
        {
            get { return Get<string>("StatusText"); }
            private set { Set("StatusText", value); }
        }

        public ICommand AddCommand { get; private set; }
        public ICommand RemoveCommand { get; private set; }
        public ICommand ClearCommand { get; private set; }
        public ICommand MoveUpCommand { get; private set; }
        public ICommand MoveDownCommand { get; private set; }
        public ICommand OptionsCommand { get; private set; }
        public ICommand EncodeCommand { get; private set; }
        public ICommand DecodeCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }

        /// <summary>Adds files (from the Add dialog or dropped on the list).</summary>
        public void AddFiles(IEnumerable<string> fileNames)
        {
            foreach (string fileName in fileNames)
            {
                if (System.IO.Directory.Exists(fileName))
                {
                    AddFiles(System.IO.Directory.GetFiles(fileName, "*.*", SearchOption.AllDirectories)
                                 .Where(f => AudioEncoder.CanEncode(f) || AudioEncoder.CanDecode(f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                    continue;
                }
                if (!File.Exists(fileName) || Files.Any(f => string.Equals(f.FullPath, fileName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                long size = 0;
                try
                {
                    size = new FileInfo(fileName).Length;
                }
                catch (IOException)
                {
                }
                Files.Add(new ConvertFile { FullPath = fileName, Size = size });
            }
        }

        private void Add()
        {
            string[] fileNames;
            if (_dialogService.ShowOpenFilesDialog("Add audio files", AudioFilter, null, out fileNames) != true || fileNames == null)
                return;
            AddFiles(fileNames);
        }

        private async void Run(bool encode)
        {
            EncodePreset preset = SelectedPreset != null ? SelectedPreset.Preset : EncodePreset.LameV0;
            List<ConvertFile> queue = Files.Where(f => encode ? AudioEncoder.CanEncode(f.FullPath) : AudioEncoder.CanDecode(f.FullPath)).ToList();

            bool needLame = encode || queue.Any(f => f.FullPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));
            bool needFlac = queue.Any(f => f.FullPath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase));
            var missing = new List<string>();
            if (needLame && AudioEncoder.LamePath == null)
                missing.Add("lame.exe (Encoders\\lame)");
            if (needFlac && AudioEncoder.FlacPath == null)
                missing.Add("flac.exe (Encoders\\flac)");
            if (missing.Count > 0)
            {
                MessageBox(string.Join(" and ", missing) + " not found. Put it in the Encoders folder next to cdtag.exe, or choose it in Tools > Options.",
                           "Convert Audio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            foreach (ConvertFile file in Files)
                file.Status = queue.Contains(file) ? "Waiting" : "Skipped: " + (encode ? "not WAV/FLAC" : "not FLAC/MP3");

            _cancellation = new CancellationTokenSource();
            IsBusy = true;
            CommandManager.InvalidateRequerySuggested();
            int done = 0, failed = 0, skipped = 0;
            try
            {
                for (int i = 0; i < queue.Count; i++)
                {
                    ConvertFile file = queue[i];
                    string target = Path.ChangeExtension(file.FullPath, encode ? ".mp3" : ".wav");
                    if (File.Exists(target))
                    {
                        file.Status = "Skipped: " + Path.GetFileName(target) + " exists";
                        skipped++;
                        continue;
                    }

                    string verb = encode ? "Encoding" : "Decoding";
                    StatusText = string.Format("{0} {1} of {2}: {3}", verb, i + 1, queue.Count, file.Name);
                    file.Status = verb + "...";
                    var progress = new Progress<int>(p => file.Status = string.Format("{0} {1}%", verb, p));
                    try
                    {
                        if (encode)
                            await AudioEncoder.EncodeMp3Async(file.FullPath, target, preset, progress, _cancellation.Token);
                        else
                            await AudioEncoder.DecodeAsync(file.FullPath, target, progress, _cancellation.Token);
                        file.Status = "Done: " + Path.GetFileName(target);
                        done++;
                    }
                    catch (OperationCanceledException)
                    {
                        file.Status = "Cancelled";
                        foreach (ConvertFile rest in queue.Skip(i + 1))
                            rest.Status = "Cancelled";
                        break;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
                    {
                        file.Status = "Error: " + ex.Message;
                        failed++;
                    }
                }
            }
            finally
            {
                IsBusy = false;
                _cancellation.Dispose();
                _cancellation = null;
                CommandManager.InvalidateRequerySuggested();
            }

            StatusText = string.Format("{0}: {1} done{2}{3}.", encode ? AudioEncoder.Describe(preset) : "Decode to WAV", done,
                                       skipped > 0 ? ", " + skipped + " skipped" : "", failed > 0 ? ", " + failed + " failed" : "");
        }

        private void Remove()
        {
            ConvertFile selected = SelectedFile;
            if (selected == null)
                return;

            int index = Files.IndexOf(selected);
            Files.Remove(selected);
            SelectedFile = Files.Count == 0 ? null : Files[Math.Min(index, Files.Count - 1)];
        }

        private void Move(int offset)
        {
            ConvertFile selected = SelectedFile;
            if (selected == null)
                return;

            int index = Files.IndexOf(selected);
            int target = index + offset;
            if (target < 0 || target >= Files.Count)
                return;

            Files.Move(index, target);
            SelectedFile = selected;
        }
    }
}

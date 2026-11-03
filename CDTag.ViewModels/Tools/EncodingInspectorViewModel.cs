using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;
using CDTag.Model.Checksum;
using CDTag.Model.Tag;

namespace CDTag.ViewModels.Tools
{
    public class InspectorFile
    {
        public string FullPath { get; set; }
        public string Name { get; set; }
        public string Folder { get; set; }
    }

    /// <summary>
    /// Encoding Inspector: shows how each MP3/FLAC/WAV file was encoded (LAME tag with its CRCs, FLAC STREAMINFO and encoder, WAV format).
    /// </summary>
    public class EncodingInspectorViewModel : ViewModelBase, IEncodingInspectorViewModel
    {
        private const string AudioFilter = "Audio files (*.mp3;*.flac;*.wav)|*.mp3;*.flac;*.wav|All files (*.*)|*.*";
        private int _version;

        public EncodingInspectorViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            Files = new ObservableCollection<InspectorFile>();
            Items = new ObservableCollection<InspectorItem>();
            AddCommand = new DelegateCommand(Add);
            RemoveCommand = new DelegateCommand(Remove, () => SelectedFile != null);
            ClearCommand = new DelegateCommand(() => { Files.Clear(); SelectedFile = null; }, () => Files.Count > 0);
            CloseCommand = new DelegateCommand(() => CloseWindow(true));
        }

        public ObservableCollection<InspectorFile> Files { get; private set; }
        public ObservableCollection<InspectorItem> Items { get; private set; }

        public InspectorFile SelectedFile
        {
            get { return Get<InspectorFile>("SelectedFile"); }
            set
            {
                if (SelectedFile == value)
                    return;
                Set("SelectedFile", value);
                Inspect();
            }
        }

        public bool IsBusy
        {
            get { return Get<bool>("IsBusy"); }
            private set { Set("IsBusy", value); }
        }

        public string StatusText
        {
            get { return Get<string>("StatusText"); }
            private set { Set("StatusText", value); }
        }

        public ICommand AddCommand { get; private set; }
        public ICommand RemoveCommand { get; private set; }
        public ICommand ClearCommand { get; private set; }
        public ICommand CloseCommand { get; private set; }

        public void AddFiles(IEnumerable<string> paths)
        {
            InspectorFile first = null;
            foreach (string path in paths)
            {
                IEnumerable<string> files;
                if (Directory.Exists(path))
                {
                    try
                    {
                        files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).Where(AudioInspector.CanInspect).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        StatusText = "Error: " + ex.Message;
                        continue;
                    }
                }
                else if (File.Exists(path))
                {
                    files = new[] { path };
                }
                else
                {
                    continue;
                }

                foreach (string file in files)
                {
                    InspectorFile existing = Files.FirstOrDefault(f => string.Equals(f.FullPath, file, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        existing = new InspectorFile { FullPath = file, Name = Path.GetFileName(file), Folder = Path.GetDirectoryName(file) };
                        Files.Add(existing);
                    }
                    if (first == null)
                        first = existing;
                }
            }

            if (first != null)
                SelectedFile = first;
            else if (Files.Count == 0)
                StatusText = "Add MP3, FLAC or WAV files (or drop them on the list).";
            CommandManager.InvalidateRequerySuggested();
        }

        private void Add()
        {
            string[] fileNames;
            if (_dialogService.ShowOpenFilesDialog("Add audio files", AudioFilter, null, out fileNames) == true && fileNames != null)
                AddFiles(fileNames);
        }

        private void Remove()
        {
            InspectorFile selected = SelectedFile;
            if (selected == null)
                return;
            int index = Files.IndexOf(selected);
            Files.Remove(selected);
            SelectedFile = Files.Count == 0 ? null : Files[Math.Min(index, Files.Count - 1)];
        }

        private async void Inspect()
        {
            int version = ++_version;
            Items.Clear();
            InspectorFile file = SelectedFile;
            if (file == null)
            {
                StatusText = null;
                return;
            }

            IsBusy = true;
            StatusText = "Reading " + file.Name + "...";
            List<InspectorItem> items = await Task.Run(() => AudioInspector.Inspect(file.FullPath));
            // Another file was selected while this one was read.
            if (version != _version)
                return;
            IsBusy = false;

            foreach (InspectorItem item in items)
                Items.Add(item);
            int bad = items.Count(i => i.State == LogCheckState.Bad);
            int warnings = items.Count(i => i.State == LogCheckState.Warning);
            StatusText = bad > 0 ? string.Format("{0}: {1} problem{2}.", file.Name, bad, bad == 1 ? "" : "s")
                       : warnings > 0 ? string.Format("{0}: {1} warning{2}.", file.Name, warnings, warnings == 1 ? "" : "s")
                       : file.Name + ": no problems found.";
        }
    }
}

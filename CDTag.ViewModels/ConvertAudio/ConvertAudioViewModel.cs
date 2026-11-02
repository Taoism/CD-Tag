using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;

namespace CDTag.ViewModels.ConvertAudio
{
    /// <summary>One file in the Convert Audio queue.</summary>
    public class ConvertFile
    {
        public string FullPath { get; set; }
        public string Name { get { return Path.GetFileName(FullPath); } }
        public string Directory { get { return Path.GetDirectoryName(FullPath); } }
        public long Size { get; set; }
    }

    /// <summary>
    /// Convert Audio tab (CD-Tag 2.x layout). The file queue works (add, remove, clear, reorder);
    /// encoding and decoding are not implemented yet, so those commands are disabled.
    /// </summary>
    public class ConvertAudioViewModel : ViewModelBase
    {
        private const string AudioFilter = "Audio files (*.wav;*.flac;*.mp3;*.ape;*.wv;*.ogg)|*.wav;*.flac;*.mp3;*.ape;*.wv;*.ogg|All files (*.*)|*.*";

        public ConvertAudioViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            Files = new ObservableCollection<ConvertFile>();

            AddCommand = new DelegateCommand(Add);
            RemoveCommand = new DelegateCommand(Remove, () => SelectedFile != null);
            ClearCommand = new DelegateCommand(() => Files.Clear(), () => Files.Count > 0);
            MoveUpCommand = new DelegateCommand(() => Move(-1), () => SelectedFile != null && Files.IndexOf(SelectedFile) > 0);
            MoveDownCommand = new DelegateCommand(() => Move(1), () => SelectedFile != null && Files.IndexOf(SelectedFile) < Files.Count - 1);

            ICommand notImplemented = new DelegateCommand(() => { }, () => false);
            OptionsCommand = notImplemented;
            EncodeCommand = notImplemented;
            DecodeCommand = notImplemented;
        }

        public ObservableCollection<ConvertFile> Files { get; private set; }

        public ConvertFile SelectedFile
        {
            get { return Get<ConvertFile>("SelectedFile"); }
            set { Set("SelectedFile", value); }
        }

        public ICommand AddCommand { get; private set; }
        public ICommand RemoveCommand { get; private set; }
        public ICommand ClearCommand { get; private set; }
        public ICommand MoveUpCommand { get; private set; }
        public ICommand MoveDownCommand { get; private set; }
        public ICommand OptionsCommand { get; private set; }
        public ICommand EncodeCommand { get; private set; }
        public ICommand DecodeCommand { get; private set; }

        private void Add()
        {
            string[] fileNames;
            if (_dialogService.ShowOpenFilesDialog("Add audio files", AudioFilter, null, out fileNames) != true || fileNames == null)
                return;

            foreach (string fileName in fileNames)
            {
                if (Files.Any(f => string.Equals(f.FullPath, fileName, StringComparison.OrdinalIgnoreCase)))
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

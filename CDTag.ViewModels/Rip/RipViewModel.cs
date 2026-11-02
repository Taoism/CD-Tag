using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;
using IdSharp.Tagging.ID3v1;

namespace CDTag.ViewModels.Rip
{
    /// <summary>One row of the Rip track list.</summary>
    public class RipTrack
    {
        public string Song { get; set; }
        public int Track { get; set; }
        public string Start { get; set; }
        public string Length { get; set; }
        public string Type { get; set; }
    }

    /// <summary>
    /// Rip tab (CD-Tag 2.x layout). Lists the optical drives and holds the album/track form;
    /// reading and ripping discs is not implemented yet, so those commands are disabled.
    /// </summary>
    public class RipViewModel : ViewModelBase
    {
        public RipViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            Drives = new ObservableCollection<string>();
            Tracks = new ObservableCollection<RipTrack>();
            Genres = new ObservableCollection<string>(GenreHelper.GenreByIndex.Where(g => !string.IsNullOrEmpty(g)).OrderBy(g => g));

            DisabledCommand notImplemented = new DisabledCommand();
            FindCDCommand = notImplemented;
            OptionsCommand = notImplemented;
            DriveCommand = notImplemented;
            RipCDCommand = notImplemented;

            RefreshDrives();
        }

        public ObservableCollection<string> Drives { get; private set; }

        public string SelectedDrive
        {
            get { return Get<string>("SelectedDrive"); }
            set { Set("SelectedDrive", value); }
        }

        public string Artist
        {
            get { return Get<string>("Artist"); }
            set { Set("Artist", value); }
        }

        public string Album
        {
            get { return Get<string>("Album"); }
            set { Set("Album", value); }
        }

        public string Year
        {
            get { return Get<string>("Year"); }
            set { Set("Year", value); }
        }

        public string Genre
        {
            get { return Get<string>("Genre"); }
            set { Set("Genre", value); }
        }

        public bool IsVariousArtists
        {
            get { return Get<bool>("IsVariousArtists"); }
            set { Set("IsVariousArtists", value); }
        }

        public ObservableCollection<string> Genres { get; private set; }

        public ObservableCollection<RipTrack> Tracks { get; private set; }

        public ICommand FindCDCommand { get; private set; }
        public ICommand OptionsCommand { get; private set; }
        public ICommand DriveCommand { get; private set; }
        public ICommand RipCDCommand { get; private set; }

        /// <summary>Lists CD/DVD drives, e.g. "E:\ HL-DT-ST BD-RE" (volume label shown when a disc is in).</summary>
        public void RefreshDrives()
        {
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
            SelectedDrive = Drives.FirstOrDefault();
        }

        /// <summary>Command for features that are not implemented yet (always disabled).</summary>
        private sealed class DisabledCommand : ICommand
        {
            public event System.EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) { return false; }
            public void Execute(object parameter) { }
        }
    }
}

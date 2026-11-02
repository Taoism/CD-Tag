using System.Windows.Input;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Events;
using CDTag.Common.Mvvm;
using CDTag.Model.Profile;
using CDTag.Model.Tag;
using CDTag.ViewModels.Events;

namespace CDTag.ViewModels.Tag.TagAlbum
{
    public class TagAlbumViewModel : ViewModelBase<TagAlbumViewModel>, ITagAlbumViewModel
    {
        private readonly DelegateCommand _finishCommand;
        private readonly DelegateCommand _previousCommand;
        private readonly DelegateCommand _previewNFOCommand;

        public TagAlbumViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            _finishCommand = new DelegateCommand(Finish);
            _previewNFOCommand = new DelegateCommand(PreviewNFO);
            _previousCommand = new DelegateCommand(Previous);

            EnhancedPropertyChanged += TagAlbumViewModel_EnhancedPropertyChanged;
        }

        /// <summary>Loads the album in <paramref name="path"/> to be tagged with <paramref name="profile"/>.</summary>
        public void Load(string path, UserProfile profile)
        {
            Profile = profile;
            Album = new Album(path);
        }

        private void TagAlbumViewModel_EnhancedPropertyChanged(object sender, EnhancedPropertyChangedEventArgs<TagAlbumViewModel> e)
        {
            if (e.IsProperty(p => p.SelectedTrackIndex))
            {
                SelectedTrack = Album.Tracks[SelectedTrackIndex];
            }
        }

        private void Finish()
        {
            Album.Finish();

            CloseWindow(true);
        }

        private void PreviewNFO()
        {
        }

        private void Previous()
        {
        }

        public Album Album
        {
            get { return Get<Album>("Album"); }
            private set { Set("Album", value); }
        }

        /// <summary>The profile picked for this album (by file type, or by the user).</summary>
        public UserProfile Profile
        {
            get { return Get<UserProfile>("Profile"); }
            private set { Set("Profile", value); }
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

        public ICommand FinishCommand
        {
            get { return _finishCommand; }
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

using System.Windows.Input;
using CDTag.Common;
using CDTag.Common.Mvvm;
using CDTag.Model.Tag;

namespace CDTag.ViewModels.Tag.TagAlbum
{
    public interface ITagAlbumViewModel : IViewModelBase
    {
        Album Album { get; }
        int SelectedTrackIndex { get; set; }
        AlbumTrack SelectedTrack { get; set; }
        ICommand FinishCommand { get; }
        ICommand PreviewNFOCommand { get; }
        ICommand PreviousCommand { get; }
        ICommand NextCommand { get; }
        ICommand LookupCommand { get; }

        /// <summary>Loads the album in <paramref name="path"/> to be tagged with <paramref name="profile"/>.</summary>
        void Load(string path, CDTag.Model.Profile.UserProfile profile);
    }
}

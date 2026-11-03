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

        /// <summary>The album folder after Finish (it may have been renamed), or null.</summary>
        string FinishedDirectory { get; }

        /// <summary>Changes the casing of every song name.</summary>
        void ApplyCasing(TitleCasing casing);

        /// <summary>Replaces text in every song name; returns how many changed.</summary>
        int SearchReplace(string find, string replace, bool matchCase);

        /// <summary>Song names from a pasted track list, in track order; returns how many lines were found.</summary>
        int PasteTracklist(string text);

        /// <summary>Loads the album in <paramref name="path"/> to be tagged with <paramref name="profile"/>.</summary>
        void Load(string path, CDTag.Model.Profile.UserProfile profile);
    }
}

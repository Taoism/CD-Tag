using System.Windows.Input;
using CDTag.Common;
using CDTag.Common.Mvvm;
using CDTag.FileBrowser.ViewModel;

namespace CDTag.ViewModels.Tag
{
    public interface ITagViewModel : IViewModelBase
    {
        IDirectoryController DirectoryViewModel { get; set; }

        ICommand AboutCommand { get; }
        ICommand TagAlbumCommand { get; }
        ICommand EditTagsCommand { get; }
        ICommand MassTagCommand { get; }
        ICommand ExitCommand { get; }
        ICommand NewProfileCommand { get; }
        ICommand EditProfileCommand { get; }
        ICommand SplitCueSheetCommand { get; }
        ICommand EncodingInspectorCommand { get; }
        ICommand OptionsCommand { get; }
        ICommand CreateChecksumCommand { get; } // TODO: These may become one
        ICommand VerifyChecksumCommand { get; } // TODO: These may become one
        ICommand VerifyEACLogCommand { get; }

        ICommand CheckForUpdatesCommand { get; }
        ICommand CutCommand { get; }
        ICommand CopyCommand { get; }
        ICommand PasteCommand { get; }
        ICommand CopyToFolderCommand { get; }
        ICommand MoveToFolderCommand { get; }
        ICommand RefreshCommand { get; }
        ICommand VerifyEacLogCommand { get; }
        ICommand ShowHelpCommand { get; }
        ICommand HomePageCommand { get; }
        ICommand SupportForumCommand { get; }

        /// <summary>Main window tab: 0 = Rip, 1 = Convert Audio, 2 = Tag/Rename.</summary>
        int SelectedTabIndex { get; set; }
        ICommand ShowRipCommand { get; }
        ICommand ShowConvertAudioCommand { get; }
        ICommand ShowTagRenameCommand { get; }

        System.Collections.ObjectModel.ObservableCollection<string> Favorites { get; }
        ICommand AddFavoriteCommand { get; }
        ICommand RemoveFavoriteCommand { get; }
        ICommand GoToFavoriteCommand { get; }

        bool IsShowStatusBarChecked { get; set; }
        bool IsShowNavigationPaneChecked { get; set; }
    }
}

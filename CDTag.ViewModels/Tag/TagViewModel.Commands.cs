using System.Windows.Input;
using CDTag.Common;
using CDTag.Common.Mvvm;

namespace CDTag.ViewModels.Tag
{
    public partial class TagViewModel : ViewModelBase<ITagViewModel>, ITagViewModel
    {
        public ICommand NewProfileCommand
        {
            get { return Get<ICommand>(nameof(NewProfileCommand)); }
            private set { Set(nameof(NewProfileCommand), value); }
        }

        public ICommand EditProfileCommand
        {
            get { return Get<ICommand>(nameof(EditProfileCommand)); }
            private set { Set(nameof(EditProfileCommand), value); }
        }

        public ICommand AboutCommand
        {
            get { return Get<ICommand>(nameof(AboutCommand)); }
            private set { Set(nameof(AboutCommand), value); }
        }

        public ICommand TagAlbumCommand
        {
            get { return Get<ICommand>(nameof(TagAlbumCommand)); }
            private set { Set(nameof(TagAlbumCommand), value); }
        }

        public ICommand EditTagsCommand
        {
            get { return Get<ICommand>(nameof(EditTagsCommand)); }
            private set { Set(nameof(EditTagsCommand), value); }
        }

        public ICommand CheckForUpdatesCommand
        {
            get { return Get<ICommand>(nameof(CheckForUpdatesCommand)); }
            private set { Set(nameof(CheckForUpdatesCommand), value); }
        }

        public ICommand ExitCommand
        {
            get { return Get<ICommand>(nameof(ExitCommand)); }
            private set { Set(nameof(ExitCommand), value); }
        }

        public ICommand CutCommand
        {
            get { return Get<ICommand>(nameof(CutCommand)); }
            private set { Set(nameof(CutCommand), value); }
        }

        public ICommand CopyCommand
        {
            get { return Get<ICommand>(nameof(CopyCommand)); }
            private set { Set(nameof(CopyCommand), value); }
        }

        public ICommand PasteCommand
        {
            get { return Get<ICommand>(nameof(PasteCommand)); }
            private set { Set(nameof(PasteCommand), value); }
        }

        public ICommand CopyToFolderCommand
        {
            get { return Get<ICommand>(nameof(CopyToFolderCommand)); }
            private set { Set(nameof(CopyToFolderCommand), value); }
        }

        public ICommand MoveToFolderCommand
        {
            get { return Get<ICommand>(nameof(MoveToFolderCommand)); }
            private set { Set(nameof(MoveToFolderCommand), value); }
        }

        public ICommand RefreshCommand
        {
            get { return Get<ICommand>(nameof(RefreshCommand)); }
            private set { Set(nameof(RefreshCommand), value); }
        }

        public ICommand CreateChecksumCommand
        {
            get { return Get<ICommand>(nameof(CreateChecksumCommand)); }
            private set { Set(nameof(CreateChecksumCommand), value); }
        }

        public ICommand VerifyChecksumCommand
        {
            get { return Get<ICommand>(nameof(VerifyChecksumCommand)); }
            private set { Set(nameof(VerifyChecksumCommand), value); }
        }

        public ICommand SplitCueSheetCommand
        {
            get { return Get<ICommand>(nameof(SplitCueSheetCommand)); }
            private set { Set(nameof(SplitCueSheetCommand), value); }
        }

        public ICommand EncodingInspectorCommand
        {
            get { return Get<ICommand>(nameof(EncodingInspectorCommand)); }
            private set { Set(nameof(EncodingInspectorCommand), value); }
        }

        public ICommand MassTagCommand
        {
            get { return Get<ICommand>(nameof(MassTagCommand)); }
            private set { Set(nameof(MassTagCommand), value); }
        }

        public ICommand ShowHelpCommand
        {
            get { return Get<ICommand>(nameof(ShowHelpCommand)); }
            private set { Set(nameof(ShowHelpCommand), value); }
        }

        public ICommand HomePageCommand
        {
            get { return Get<ICommand>(nameof(HomePageCommand)); }
            private set { Set(nameof(HomePageCommand), value); }
        }

        public ICommand SupportForumCommand
        {
            get { return Get<ICommand>(nameof(SupportForumCommand)); }
            private set { Set(nameof(SupportForumCommand), value); }
        }

        public ICommand OptionsCommand
        {
            get { return Get<ICommand>(nameof(OptionsCommand)); }
            private set { Set(nameof(OptionsCommand), value); }
        }

        public ICommand VerifyEACLogCommand
        {
            get { return Get<ICommand>(nameof(VerifyEACLogCommand)); }
            private set { Set(nameof(VerifyEACLogCommand), value); }
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CDTag.Common.Mvvm;
using CDTag.Common.Settings;

namespace CDTag.ViewModels.Tag
{
    /// <summary>Main-window tabs (Rip / Convert Audio / Tag/Rename) and favorite folders.</summary>
    public partial class TagViewModel
    {
        public const int RipTab = 0;
        public const int ConvertAudioTab = 1;
        public const int TagRenameTab = 2;

        /// <summary>Project page (help, home page and issue tracker).</summary>
        public const string ProjectUrl = "https://github.com/Taoism/CD-Tag";

        private ObservableCollection<string> _favorites;

        private static void OpenUrl(string url)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void InitializeMainWindow()
        {
            SelectedTabIndex = TagRenameTab;
            ShowRipCommand = new DelegateCommand(() => SelectedTabIndex = RipTab);
            ShowConvertAudioCommand = new DelegateCommand(() => SelectedTabIndex = ConvertAudioTab);
            ShowTagRenameCommand = new DelegateCommand(() => SelectedTabIndex = TagRenameTab);

            ShowHelpCommand = new DelegateCommand(() => OpenUrl(ProjectUrl + "#readme"));
            HomePageCommand = new DelegateCommand(() => OpenUrl(ProjectUrl));
            SupportForumCommand = new DelegateCommand(() => OpenUrl(ProjectUrl + "/issues"));

            _favorites = new ObservableCollection<string>(FavoritesStore.Load());
            AddFavoriteCommand = new DelegateCommand(AddFavorite, () => CurrentFolder != null && !IsFavorite(CurrentFolder));
            RemoveFavoriteCommand = new DelegateCommand(RemoveFavorite, () => IsFavorite(CurrentFolder));
            GoToFavoriteCommand = new DelegateCommand<string>(GoToFavorite);
        }

        /// <summary>0 = Rip, 1 = Convert Audio, 2 = Tag/Rename.</summary>
        public int SelectedTabIndex
        {
            get { return Get<int>("SelectedTabIndex"); }
            set { Set("SelectedTabIndex", value); }
        }

        public ICommand ShowRipCommand { get; private set; }
        public ICommand ShowConvertAudioCommand { get; private set; }
        public ICommand ShowTagRenameCommand { get; private set; }

        /// <summary>Favorite folders (Favorites menu / toolbar button).</summary>
        public ObservableCollection<string> Favorites
        {
            get { return _favorites; }
        }

        public ICommand AddFavoriteCommand { get; private set; }
        public ICommand RemoveFavoriteCommand { get; private set; }

        /// <summary>Parameter: the folder to open.</summary>
        public ICommand GoToFavoriteCommand { get; private set; }

        private string CurrentFolder
        {
            get { return DirectoryViewModel != null ? DirectoryViewModel.CurrentDirectory : null; }
        }

        private bool IsFavorite(string folder)
        {
            return folder != null && _favorites.Any(f => string.Equals(f.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        }

        private void AddFavorite()
        {
            string folder = CurrentFolder;
            if (folder == null || IsFavorite(folder))
                return;

            _favorites.Add(folder);
            FavoritesStore.Save(_favorites);
        }

        private void RemoveFavorite()
        {
            string folder = CurrentFolder;
            string existing = _favorites.FirstOrDefault(f => string.Equals(f.TrimEnd('\\'), (folder ?? "").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                return;

            _favorites.Remove(existing);
            FavoritesStore.Save(_favorites);
        }

        private void GoToFavorite(string folder)
        {
            if (string.IsNullOrEmpty(folder) || DirectoryViewModel == null)
                return;

            if (!System.IO.Directory.Exists(folder))
            {
                var answer = MessageBox(string.Format("'{0}' no longer exists. Remove it from your favorites?", folder),
                                        "Favorites", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (answer == System.Windows.MessageBoxResult.Yes)
                {
                    _favorites.Remove(folder);
                    FavoritesStore.Save(_favorites);
                }
                return;
            }

            SelectedTabIndex = TagRenameTab;
            DirectoryViewModel.CurrentDirectory = folder;
        }
    }
}

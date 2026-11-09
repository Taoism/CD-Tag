using System;
using CDTag.ViewModels.Tag.TagAlbum;
using System.Collections.Generic;
using System.Linq;
using CDTag.Model.Profile;
using CDTag.Model.Tag;
using CDTag.ViewModels.Profile.ChooseProfile;
using CDTag.Views.Interfaces.Profile.ChooseProfile;
using System.IO;
using System.Reflection;
using System.Windows;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Events;
using CDTag.Common.Mvvm;
using CDTag.FileBrowser.ViewModel;
using CDTag.ViewModels.Events;
using CDTag.Views.Interfaces.About;
using CDTag.Views.Interfaces.Checksum;
using CDTag.Views.Interfaces.Options;
using CDTag.Views.Interfaces.Profile.EditProfile;
using CDTag.Views.Interfaces.Profile.NewProfile;
using CDTag.Views.Interfaces.Tag.EditTag;
using CDTag.Views.Interfaces.Tag.MassTag;
using CDTag.Views.Interfaces.Tag.TagAlbum;
using CDTag.Views.Interfaces.Tools;

namespace CDTag.ViewModels.Tag
{
    public partial class TagViewModel : ViewModelBase<ITagViewModel>, ITagViewModel
    {
        public TagViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            InitializeMainWindow();

            AboutCommand = new DelegateCommand(() => ShowWindow<IAboutWindow>());
            ExitCommand = new DelegateCommand(() => Application.Current.MainWindow.Close());
            TagAlbumCommand = new DelegateCommand(TagAlbum);
            EditTagsCommand = new DelegateCommand(() => ShowWindow<IEditTagWindow>());
            MassTagCommand = new DelegateCommand(ShowMassTag);
            NewProfileCommand = new DelegateCommand(() => ShowWindow<INewProfileWindow>());
            EditProfileCommand = new DelegateCommand(() => ShowWindow<IEditProfileWindow>());
            SplitCueSheetCommand = new DelegateCommand(ShowSplitCue);
            EncodingInspectorCommand = new DelegateCommand(ShowEncodingInspector);
            OptionsCommand = new DelegateCommand(() => ShowWindow<IOptionsWindow>());
            CreateChecksumCommand = new DelegateCommand(() => ShowChecksum(verify: false));
            VerifyChecksumCommand = new DelegateCommand(() => ShowChecksum(verify: true));
            VerifyEACLogCommand = new DelegateCommand(ShowVerifyEacLog);
            RefreshCommand = new DelegateCommand(() => { if (DirectoryViewModel != null) DirectoryViewModel.RefreshExplorer(); });

            EnhancedPropertyChanged += TagViewModel_EnhancedPropertyChanged;

            eventAggregator.Subscribe<GetDirectoryControllerEvent>(OnGetDirectoryController);
        }

        /// <summary>
        /// Opens Tag Album for the folder selected in the file list (or the current folder when no folder
        /// is selected), with every audio file inside it including subfolders (e.g. CD1/CD2). The profile
        /// is picked by file type; if several fit, the user is asked.
        /// </summary>
        private void TagAlbum()
        {
            string directory = GetTagAlbumDirectory();
            if (directory == null)
                return;

            if (Album.IsCollectionFolder(directory))
            {
                MessageBox(string.Format("'{0}' contains several albums.{1}{1}Select one album folder in the file list, then choose Tag Album.", directory, Environment.NewLine),
                           "Tag Album", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            HashSet<string> albumTypes = ProfileSelector.GetAlbumFileTypes(directory);
            if (albumTypes.Count == 0)
            {
                MessageBox(string.Format("There are no MP3 or FLAC files in '{0}' or its subfolders.{1}{1}Select an album folder in the file list (or open it), then choose Tag Album.", directory, Environment.NewLine),
                           "Tag Album", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            List<UserProfile> profiles = ProfileSelector.LoadAll(_pathService.ProfileDirectory);
            IList<UserProfile> candidates = ProfileSelector.FindCandidates(profiles, albumTypes);

            UserProfile chosen;
            if (candidates.Count == 0)
            {
                chosen = null;
            }
            else if (candidates.Count == 1)
            {
                chosen = candidates[0];
            }
            else
            {
                IChooseProfileWindow window = IoC.Resolve<IChooseProfileWindow>();
                var chooser = (IChooseProfileViewModel)window.DataContext;
                string types = albumTypes.Count == 0 ? "these" : string.Join(" and ", albumTypes.OrderBy(t => t).Select(t => t.ToUpperInvariant()));
                chooser.Message = string.Format("Several profiles fit {0} files. Which profile do you want to use for this album?", types);
                chooser.SetProfiles(candidates, ProfileSelector.ActiveProfile);

                if (ShowWindow(window) != true)
                    return;
                chosen = chooser.SelectedProfile;
            }

            ProfileSelector.ActiveProfile = chosen;

            ITagAlbumWindow tagAlbumWindow = IoC.Resolve<ITagAlbumWindow>();
            var tagAlbum = (ITagAlbumViewModel)tagAlbumWindow.DataContext;
            List<string> otherFolders = DirectoryViewModel != null && DirectoryViewModel.FileCollection != null
                ? DirectoryViewModel.FileCollection.Where(f => f.IsSelected && f.IsDirectory).Select(f => f.FullName)
                                     .Where(f => !string.Equals(f, directory, StringComparison.OrdinalIgnoreCase)).ToList()
                : null;
            tagAlbum.Load(directory, chosen, otherFolders);
            ShowWindow(tagAlbumWindow);

            // Show the result: follow the folder when it was renamed while open, otherwise refresh the list.
            if (tagAlbum.FinishedDirectory != null && DirectoryViewModel != null)
            {
                string current = (DirectoryViewModel.CurrentDirectory ?? "").TrimEnd('\\');
                string old = directory.TrimEnd('\\');
                if (string.Equals(current, old, StringComparison.OrdinalIgnoreCase) && Directory.Exists(tagAlbum.FinishedDirectory))
                    DirectoryViewModel.CurrentDirectory = tagAlbum.FinishedDirectory;
                else
                    DirectoryViewModel.RefreshExplorer();
            }
        }

        /// <summary>Create/Verify Checksum for the selected folder (or a selected .sfv/.md5/.sha1 file), otherwise the current folder.</summary>
        private void ShowChecksum(bool verify)
        {
            string checksumFile = GetSelectedFile(f => CDTag.Model.Checksum.ChecksumList.IsChecksumFile(f));
            string folder = checksumFile != null ? Path.GetDirectoryName(checksumFile) : GetTagAlbumDirectory();

            IChecksumWindow window = IoC.Resolve<IChecksumWindow>();
            var checksum = (CDTag.ViewModels.Checksum.IChecksumViewModel)window.DataContext;
            checksum.Load(folder, verify || checksumFile != null, checksumFile);
            ShowWindow(window);
            if (DirectoryViewModel != null)
                DirectoryViewModel.RefreshExplorer();
        }

        /// <summary>Verify EAC Log for the selected .log file, otherwise the logs in the selected or current folder.</summary>
        private void ShowVerifyEacLog()
        {
            string logFile = GetSelectedFile(f => f.EndsWith(".log", StringComparison.OrdinalIgnoreCase));
            string folder = logFile != null ? Path.GetDirectoryName(logFile) : GetTagAlbumDirectory();

            IVerifyEACLogWindow window = IoC.Resolve<IVerifyEACLogWindow>();
            var verify = (CDTag.ViewModels.Checksum.IVerifyEACLogViewModel)window.DataContext;
            verify.Load(folder, logFile);
            ShowWindow(window);
        }

        /// <summary>Encoding Inspector for the selected audio files, otherwise the audio files in the selected or current folder.</summary>
        private void ShowEncodingInspector()
        {
            var paths = new List<string>();
            if (DirectoryViewModel != null && DirectoryViewModel.FileCollection != null)
                paths.AddRange(DirectoryViewModel.FileCollection.Where(f => f.IsSelected && !f.IsDirectory && CDTag.Model.Tag.AudioInspector.CanInspect(f.FullName)).Select(f => f.FullName));
            if (paths.Count == 0)
            {
                string folder = GetTagAlbumDirectory();
                if (folder != null)
                    paths.Add(folder);
            }

            IEncodingInspectorWindow window = IoC.Resolve<IEncodingInspectorWindow>();
            ((CDTag.ViewModels.Tools.IEncodingInspectorViewModel)window.DataContext).AddFiles(paths);
            ShowWindow(window);
        }

        /// <summary>Split CUE for the selected .cue file, otherwise the CUE sheets in the selected or current folder.</summary>
        private void ShowSplitCue()
        {
            string cueFile = GetSelectedFile(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase));
            string folder = cueFile != null ? Path.GetDirectoryName(cueFile) : GetTagAlbumDirectory();

            ISplitCueWindow window = IoC.Resolve<ISplitCueWindow>();
            var split = (CDTag.ViewModels.Tools.ISplitCueViewModel)window.DataContext;
            split.Load(folder, cueFile);
            ShowWindow(window);
            if (split.HasSplit && DirectoryViewModel != null)
                DirectoryViewModel.RefreshExplorer();
        }

        /// <summary>Mass Tag for the selected MP3/FLAC files, otherwise the files in the selected or current folder.</summary>
        private void ShowMassTag()
        {
            var paths = new List<string>();
            if (DirectoryViewModel != null && DirectoryViewModel.FileCollection != null)
                paths.AddRange(DirectoryViewModel.FileCollection.Where(f => f.IsSelected && !f.IsDirectory && MassTagger.CanTag(f.FullName)).Select(f => f.FullName));
            if (paths.Count == 0)
            {
                string folder = GetTagAlbumDirectory();
                if (folder != null)
                    paths.Add(folder);
            }

            IMassTagWindow window = IoC.Resolve<IMassTagWindow>();
            var massTag = (CDTag.ViewModels.Tag.MassTag.IMassTagViewModel)window.DataContext;
            massTag.AddFiles(paths);
            ShowWindow(window);
            if (massTag.HasSaved && DirectoryViewModel != null)
                DirectoryViewModel.RefreshExplorer();
        }

        /// <summary>The first selected file in the file list that matches <paramref name="match"/>, or null.</summary>
        private string GetSelectedFile(Func<string, bool> match)
        {
            if (DirectoryViewModel == null || DirectoryViewModel.FileCollection == null)
                return null;
            var selected = DirectoryViewModel.FileCollection.FirstOrDefault(f => f.IsSelected && !f.IsDirectory && match(f.FullName));
            return selected != null ? selected.FullName : null;
        }

        /// <summary>The first folder selected in the file list, otherwise the current folder.</summary>
        private string GetTagAlbumDirectory()
        {
            if (DirectoryViewModel == null)
                return null;

            if (DirectoryViewModel.FileCollection != null)
            {
                var selectedFolder = DirectoryViewModel.FileCollection.FirstOrDefault(f => f.IsSelected && f.IsDirectory);
                if (selectedFolder != null)
                    return selectedFolder.FullName;
            }

            return DirectoryViewModel.CurrentDirectory;
        }

        private void OnGetDirectoryController(GetDirectoryControllerEvent e)
        {
            e.DirectoryController = DirectoryViewModel;
        }

        private void TagViewModel_EnhancedPropertyChanged(object sender, EnhancedPropertyChangedEventArgs<ITagViewModel> e)
        {
            if (e.IsProperty(p => p.DirectoryViewModel))
            {
                DirectoryViewModel.InitialDirectory = @"C:\";
            }
        }

        public IDirectoryController DirectoryViewModel
        {
            get { return Get<IDirectoryController>(MethodBase.GetCurrentMethod()); }
            set { Set(MethodBase.GetCurrentMethod(), value); }
        }

        public bool IsShowStatusBarChecked
        {
            get { return Get<bool>(MethodBase.GetCurrentMethod()); }
            set { Set(MethodBase.GetCurrentMethod(), value); }
        }

        public bool IsShowNavigationPaneChecked
        {
            get { return Get<bool>(MethodBase.GetCurrentMethod()); }
            set { Set(MethodBase.GetCurrentMethod(), value); }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using CDTag.Model.Profile;
using CDTag.ViewModels.Profile.ChooseProfile;
using CDTag.Views.Interfaces.Profile.ChooseProfile;
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
            AboutCommand = new DelegateCommand(() => ShowWindow<IAboutWindow>());
            ExitCommand = new DelegateCommand(() => Application.Current.MainWindow.Close());
            TagAlbumCommand = new DelegateCommand(TagAlbum);
            EditTagsCommand = new DelegateCommand(() => ShowWindow<IEditTagWindow>());
            MassTagCommand = new DelegateCommand(() => ShowWindow<IMassTagWindow>());
            NewProfileCommand = new DelegateCommand(() => ShowWindow<INewProfileWindow>());
            EditProfileCommand = new DelegateCommand(() => ShowWindow<IEditProfileWindow>());
            SplitCueSheetCommand = new DelegateCommand(() => ShowWindow<ISplitCueWindow>());
            EncodingInspectorCommand = new DelegateCommand(() => ShowWindow<IEncodingInspectorWindow>());
            OptionsCommand = new DelegateCommand(() => ShowWindow<IOptionsWindow>());
            CreateChecksumCommand = new DelegateCommand(() => ShowWindow<IChecksumWindow>());
            VerifyChecksumCommand = new DelegateCommand(() => ShowWindow<IChecksumWindow>());
            VerifyEACLogCommand = new DelegateCommand(() => ShowWindow<IVerifyEACLogWindow>());
            RefreshCommand = new DelegateCommand(() => { if (DirectoryViewModel != null) DirectoryViewModel.RefreshExplorer(); });

            EnhancedPropertyChanged += TagViewModel_EnhancedPropertyChanged;

            eventAggregator.Subscribe<GetDirectoryControllerEvent>(OnGetDirectoryController);
        }

        /// <summary>Picks the profile for the current folder's file types (asking if several fit), then opens Tag Album.</summary>
        private void TagAlbum()
        {
            string directory = DirectoryViewModel != null ? DirectoryViewModel.CurrentDirectory : null;
            HashSet<string> albumTypes = ProfileSelector.GetAlbumFileTypes(directory);

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
            ShowWindow<ITagAlbumWindow>();
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

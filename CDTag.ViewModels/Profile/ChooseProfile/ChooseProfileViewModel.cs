using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;
using CDTag.Model.Profile;

namespace CDTag.ViewModels.Profile.ChooseProfile
{
    public interface IChooseProfileViewModel : IViewModelBase
    {
        string Message { get; set; }
        ObservableCollection<UserProfile> Profiles { get; }
        UserProfile SelectedProfile { get; set; }
        ICommand OKCommand { get; }

        /// <summary>Fills the list; <paramref name="preferred"/> is pre-selected when present.</summary>
        void SetProfiles(IEnumerable<UserProfile> profiles, UserProfile preferred);
    }

    public class ChooseProfileViewModel : ViewModelBase, IChooseProfileViewModel
    {
        public ChooseProfileViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            Profiles = new ObservableCollection<UserProfile>();
            OKCommand = new DelegateCommand(() => CloseWindow(true), () => SelectedProfile != null);
        }

        public string Message
        {
            get { return Get<string>(nameof(Message)); }
            set { Set(nameof(Message), value); }
        }

        public ObservableCollection<UserProfile> Profiles { get; private set; }

        public UserProfile SelectedProfile
        {
            get { return Get<UserProfile>(nameof(SelectedProfile)); }
            set { Set(nameof(SelectedProfile), value); }
        }

        public ICommand OKCommand { get; private set; }

        public void SetProfiles(IEnumerable<UserProfile> profiles, UserProfile preferred)
        {
            Profiles.Clear();
            foreach (UserProfile profile in profiles)
                Profiles.Add(profile);

            SelectedProfile = (preferred != null ? Profiles.FirstOrDefault(p => p.ProfileName == preferred.ProfileName) : null)
                              ?? Profiles.FirstOrDefault();
        }
    }
}

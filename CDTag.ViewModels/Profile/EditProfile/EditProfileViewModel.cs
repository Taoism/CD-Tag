using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Events;
using CDTag.Common.Mvvm;
using CDTag.Model.Profile;
using CDTag.Views.Interfaces.Profile.NewProfile;

namespace CDTag.ViewModels.Profile.EditProfile
{
    public partial class EditProfileViewModel : ViewModelBase<EditProfileViewModel>, IEditProfileViewModel
    {
        private readonly DelegateCommand _newProfileCommand;
        private readonly DelegateCommand _renameProfileCommand;
        private readonly DelegateCommand _copyProfileCommand;
        private readonly DelegateCommand _deleteProfileCommand;
        private readonly DelegateCommand _importProfileCommand;
        private readonly DelegateCommand _exportProfileCommand;

        private readonly ObservableCollection<UserProfile> _profiles;

        public EditProfileViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            _newProfileCommand = new DelegateCommand(NewProfile);
            _renameProfileCommand = new DelegateCommand(RenameProfile);
            _copyProfileCommand = new DelegateCommand(CopyProfile);
            _deleteProfileCommand = new DelegateCommand(DeleteProfile);
            _importProfileCommand = new DelegateCommand(ImportProfile);
            _exportProfileCommand = new DelegateCommand(ExportProfile, () => Profile != null);

            _profiles = new ObservableCollection<UserProfile>();
            _profiles.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (UserProfile added in e.NewItems)
                        added.PropertyChanged += Profile_PropertyChanged;
                }
                if (e.OldItems != null)
                {
                    foreach (UserProfile removed in e.OldItems)
                        removed.PropertyChanged -= Profile_PropertyChanged;
                }
            };

            string[] files = Directory.GetFiles(_pathService.ProfileDirectory, "*.cfg").OrderBy(p => p.ToLower()).ToArray();
            foreach (string file in files)
            {
                try
                {
                    _profiles.Add(UserProfile.Load(file));
                }
                catch (Exception ex)
                {
                    // TODO
                    Debug.WriteLine(ex);
                }
            }

            if (_profiles.Count == 0)
            {
                CreateDefaultProfile();
            }

            EnhancedPropertyChanged += EditProfileViewModel_EnhancedPropertyChanged;

            Profile = _profiles.First();
            InitializeEditor();
            UpdateWindowTitle();
        }

        private void EditProfileViewModel_EnhancedPropertyChanged(object sender, EnhancedPropertyChangedEventArgs<EditProfileViewModel> e)
        {
            if (e.IsProperty(p => p.Profile))
            {
                UserProfile oldValue = e.OldValue as UserProfile;
                if (oldValue != null)
                {
                    oldValue.EnhancedPropertyChanged -= Profile_EnhancedPropertyChanged;
                }

                UserProfile newValue = e.NewValue as UserProfile;
                if (newValue != null)
                {
                    newValue.EnhancedPropertyChanged -= Profile_EnhancedPropertyChanged;
                    newValue.EnhancedPropertyChanged += Profile_EnhancedPropertyChanged;
                    newValue.CheckHasChanges();
                }
                RefreshEditors();

                UpdateWindowTitle();
            }
        }

        /// <summary>The file-type choice in the list is saved straight away.</summary>
        private void Profile_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "AudioFileTypes")
                return;

            var profile = (UserProfile)sender;
            if (profile.AudioFileTypes == "")
                profile.AudioFileTypes = null;
            else
                profile.Save();
        }

        private void Profile_EnhancedPropertyChanged(object sender, EnhancedPropertyChangedEventArgs<UserProfile> e)
        {
            if (e.IsProperty(p => p.HasChanges))
            {
                UpdateWindowTitle();
            }
        }

        private void UpdateWindowTitle()
        {
            var profile = Profile;
            if (profile == null)
            {
                WindowTitle = "Edit Profile";
            }
            else
            {
                WindowTitle = string.Format("Edit Profile - {0}{1}", profile.ProfileName, profile.HasChanges ? "*" : "");
            }
        }

        public string WindowTitle
        {
            get { return Get<string>("WindowTitle"); }
            set { Set("WindowTitle", value); }
        }

        public string Header
        {
            get { return Get<string>("Header"); }
            set { Set("Header", value); }
        }

        public ICommand NewProfileCommand
        {
            get { return _newProfileCommand; }
        }

        public ICommand RenameProfileCommand
        {
            get { return _renameProfileCommand; }
        }

        public ICommand CopyProfileCommand
        {
            get { return _copyProfileCommand; }
        }

        public ICommand DeleteProfileCommand
        {
            get { return _deleteProfileCommand; }
        }

        public ICommand ImportProfileCommand
        {
            get { return _importProfileCommand; }
        }

        public ICommand ExportProfileCommand
        {
            get { return _exportProfileCommand; }
        }

        /// <summary>Imports CD-Tag 2.x profiles (.cfg, several at once) with their NFO templates.</summary>
        private void ImportProfile()
        {
            string[] fileNames;
            bool? result = _dialogService.ShowOpenFilesDialog("Import CD-Tag 2.x profiles",
                "CD-Tag 2.x profiles (*.cfg;*.txt)|*.cfg;*.txt|All files (*.*)|*.*", null, out fileNames);
            if (result != true || fileNames == null || fileNames.Length == 0)
                return;

            string[] notLegacy = fileNames.Where(f => !LegacyProfile.IsLegacyFile(f)).Select(Path.GetFileName).ToArray();
            string[] legacy = fileNames.Where(LegacyProfile.IsLegacyFile).ToArray();
            if (legacy.Length == 0)
            {
                MessageBox(string.Format("{0} {1} not a CD-Tag 2.x profile.", string.Join(", ", notLegacy), notLegacy.Length == 1 ? "is" : "are"),
                           "Import profiles", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string[] replacing = legacy.Select(Path.GetFileNameWithoutExtension)
                                       .Where(n => _profiles.Any(p => string.Equals(p.ProfileName, n, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (replacing.Length > 0)
            {
                var answer = MessageBox(string.Format("{0} already {1}. Replace {2} with the 2.x version?", string.Join(", ", replacing),
                                                      replacing.Length == 1 ? "exists" : "exist", replacing.Length == 1 ? "it" : "them"),
                                        "Import profiles", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                    return;
            }

            var imported = new List<UserProfile>();
            var problems = new List<string>(notLegacy.Select(n => n + ": not a CD-Tag 2.x profile"));
            foreach (string fileName in legacy)
            {
                string profileName = Path.GetFileNameWithoutExtension(fileName);
                try
                {
                    UserProfile profile = LegacyProfile.Import(fileName, _pathService.ProfileDirectory, profileName);
                    UserProfile existing = _profiles.FirstOrDefault(p => string.Equals(p.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                        _profiles[_profiles.IndexOf(existing)] = profile;
                    else
                        _profiles.Add(profile);
                    imported.Add(profile);
                    if (profile.NFOOptions.TemplatePath == null)
                        problems.Add(profileName + ": its NFO template was not found next to the profile");
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    problems.Add(profileName + ": " + ex.Message);
                }
            }

            if (imported.Count > 0)
                Profile = imported[0];

            string message = string.Format("Imported {0} profile{1}{2}.", imported.Count, imported.Count == 1 ? "" : "s",
                                           imported.Count > 0 ? ": " + string.Join(", ", imported.Select(p => p.ProfileName)) : "");
            if (problems.Count > 0)
                message += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, problems);
            MessageBox(message, "Import profiles", MessageBoxButton.OK, problems.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        /// <summary>Writes the selected profile as a CD-Tag 2.x profile (.cfg + .nfo template) to a folder.</summary>
        private void ExportProfile()
        {
            UserProfile profile = Profile;
            if (profile == null)
                return;
            if (profile.HasChanges)
            {
                var save = MessageBox(string.Format("Save the changes to {0} before exporting?", profile.ProfileName), "Export profile",
                                      MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (save == MessageBoxResult.Cancel)
                    return;
                if (save == MessageBoxResult.Yes)
                    profile.Save();
            }

            string folder;
            if (_dialogService.ShowFolderDialog(string.Format("Export {0} as a CD-Tag 2.x profile to", profile.ProfileName), null, null, out folder) != true
                || string.IsNullOrEmpty(folder))
                return;

            if (string.Equals(Path.GetFullPath(folder).TrimEnd('\\'), Path.GetFullPath(_pathService.ProfileDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox("That is CD-Tag's own Profiles folder. Choose the folder of the old CD-Tag (or another folder).",
                           "Export profile", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string cfg = Path.Combine(folder, profile.ProfileName + ".cfg");
            if (File.Exists(cfg))
            {
                var answer = MessageBox(string.Format("{0} already exists in that folder. Replace it?", Path.GetFileName(cfg)), "Export profile",
                                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                List<string> written = LegacyProfileExport.Export(profile, _pathService.ProfileDirectory, folder);
                MessageBox(string.Format("Exported {0} for CD-Tag 2.x:{1}{2}", profile.ProfileName, Environment.NewLine,
                                         string.Join(Environment.NewLine, written)),
                           "Export profile", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox(ex.Message, "Export profile", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void NewProfile()
        {
            var currentProfile = Profile;
            if (currentProfile != null && currentProfile.HasChanges)
            {
                var msgBoxResult = MessageBox(string.Format("Do you want to save changes to {0}?", currentProfile.ProfileName), "Save changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (msgBoxResult == MessageBoxResult.Yes)
                {
                    currentProfile.Save();
                }
                else if (msgBoxResult != MessageBoxResult.No)
                {
                    return;
                }
            }

            bool? result = ShowWindow<INewProfileWindow>();

            if (result == true)
            {
                // TODO: What if the user overwrites an existing profile?

                UserProfile newProfile = null;

                string[] files = Directory.GetFiles(_pathService.ProfileDirectory, "*.cfg").OrderBy(p => p.ToLower()).ToArray();
                foreach (string file in files)
                {
                    bool exists = false;
                    foreach (var profile in Profiles)
                    {
                        if (string.Compare(profile.GetProfileFileName(), file, ignoreCase: true) == 0)
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (exists)
                    {
                        continue;
                    }

                    try
                    {
                        var profile = UserProfile.Load(file);
                        _profiles.Add(profile);

                        if (newProfile == null)
                            newProfile = profile;
                    }
                    catch (Exception ex)
                    {
                        // TODO
                        Debug.WriteLine(ex);
                    }
                }

                if (newProfile != null)
                {
                    Profile = newProfile;
                }
            }
        }

        private void DeleteProfile()
        {
            var profile = Profile;
            if (profile == null)
                return;

            string messageBoxText = string.Format("Do you want to delete {0}?", profile.ProfileName);
            if (_profiles.Count == 1)
            {
                messageBoxText += string.Format("{0}{0}If you delete this profile a Default profile will be created.", Environment.NewLine);
            }

            var result = MessageBox(messageBoxText, "Delete profile", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;

            File.Delete(profile.GetProfileFileName());

            Profile = null;

            int index = _profiles.IndexOf(profile);
            _profiles.RemoveAt(index);

            if (_profiles.Count > 0)
            {
                if (index >= _profiles.Count)
                    index = _profiles.Count - 1;

                Profile = _profiles[index];
            }
            else
            {
                Profile = CreateDefaultProfile();
            }
        }

        private UserProfile CreateDefaultProfile()
        {
            UserProfile profile = new UserProfile();
            profile.ProfileName = "Default";
            profile.Save();

            _profiles.Add(profile);

            return profile;
        }

        public ObservableCollection<UserProfile> Profiles
        {
            get { return _profiles; }
        }

        public UserProfile Profile
        {
            get { return Get<UserProfile>("Profile"); }
            set { Set("Profile", value); }
        }
    }
}

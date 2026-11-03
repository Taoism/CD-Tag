using System.IO;
using System.Linq;
using System.Windows;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Profile;
using CDTag.ViewModels.Profile.EditProfile;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Profile.EditProfile
{
    [TestFixture]
    public class EditProfileViewModelTests
    {
        private const string _profileName = "unittests";
        private const string _profileName2 = "unittests2";
        private const string _profileName3 = "unittests3";
        private string _defaultProfile;
        private string _unitTestsProfile;
        private string _unitTestsProfile2;
        private string _unitTestsProfile3;
        private string _unitTestsNFO;

        [OneTimeSetUp]
        public void Setup()
        {
            IoC.ClearAllRegistrations();

            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());

            string profileDirectory = IoC.Resolve<IPathService>().ProfileDirectory;
            _defaultProfile = Path.Combine(profileDirectory, "Default.cfg");
            _unitTestsProfile = Path.Combine(profileDirectory, _profileName + ".cfg");
            _unitTestsProfile2 = Path.Combine(profileDirectory, _profileName2 + ".cfg");
            _unitTestsProfile3 = Path.Combine(profileDirectory, _profileName3 + ".cfg");
            _unitTestsNFO = Path.Combine(profileDirectory, _profileName + ".nfo");

            DeleteUnitTestsProfile();
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            DeleteUnitTestsProfile();
        }

        private void DeleteUnitTestsProfile()
        {
            if (File.Exists(_defaultProfile))
                File.Delete(_defaultProfile);
            if (File.Exists(_unitTestsProfile))
                File.Delete(_unitTestsProfile);
            if (File.Exists(_unitTestsProfile2))
                File.Delete(_unitTestsProfile2);
            if (File.Exists(_unitTestsProfile3))
                File.Delete(_unitTestsProfile3);
            if (File.Exists(_unitTestsNFO))
                File.Delete(_unitTestsNFO);
        }

        [Test]
        public void ConstructorTest()
        {
            // Arrange/Act
            DeleteUnitTestsProfile();

            EditProfileViewModel editProfileViewModel = IoC.Resolve<EditProfileViewModel>();

            // Assert
            Assert.That(editProfileViewModel, Is.Not.Null, "editProfileViewModel");
            Assert.That(editProfileViewModel.CopyProfileCommand, Is.Not.Null, "editProfileViewModel.CopyProfileCommand");
            Assert.That(editProfileViewModel.DeleteProfileCommand, Is.Not.Null, "editProfileViewModel.DeleteProfileCommand");
            Assert.That(editProfileViewModel.NewProfileCommand, Is.Not.Null, "editProfileViewModel.NewProfileCommand");
            Assert.That(editProfileViewModel.RenameProfileCommand, Is.Not.Null, "editProfileViewModel.RenameProfileCommand");
            Assert.That(editProfileViewModel.Profiles, Is.Not.Null, "editProfileViewModel.Profiles");
            Assert.That(editProfileViewModel.Profiles.Count, Is.EqualTo(1), "editProfileViewModel.Profiles.Count");
            Assert.That(editProfileViewModel.Profile, Is.Not.Null, "editProfileViewModel.Profile");
            Assert.That(editProfileViewModel.Profile.ProfileName, Is.EqualTo("Default"), "editProfileViewModel.Profile");
            Assert.That(editProfileViewModel.Header, Is.EqualTo("Select Profile"), "editProfileViewModel.Header");
            Assert.That(editProfileViewModel.WindowTitle, Is.EqualTo("Edit Profile - Default"), "editProfileViewModel.WindowTitle");
            Assert.That(File.Exists(_defaultProfile), Is.True, "File.Exists(_defaultProfile)");
        }

        [Test]
        public void DeleteProfileTest()
        {
            // Arrange
            DeleteUnitTestsProfile();

            UserProfile profile = new UserProfile();
            profile.ProfileName = _profileName;
            profile.Save();

            profile = new UserProfile();
            profile.ProfileName = _profileName2;
            profile.Save();

            profile = new UserProfile();
            profile.ProfileName = _profileName3;
            profile.Save();

            EditProfileViewModel editProfileViewModel = IoC.Resolve<EditProfileViewModel>();
            bool confirmationShown = false;
            editProfileViewModel.ShowMessageBox += (sender, args) => { confirmationShown = true; args.Data.Result = MessageBoxResult.Yes; }; // delete confirmation
            var profiles = editProfileViewModel.Profiles;

            Assert.That(profiles.Count, Is.EqualTo(3), "profiles.Count");
            Assert.That(editProfileViewModel.WindowTitle, Is.EqualTo("Edit Profile - " + _profileName), "editProfileViewModel.WindowTitle");

            // Act - Delete 2nd of 3 profiles
            var deleteProfile = profiles[1];
            editProfileViewModel.Profile = deleteProfile;
            editProfileViewModel.DeleteProfileCommand.Execute(null);

            // Assert
            Assert.That(confirmationShown, Is.True, "confirmationShown");
            Assert.That(editProfileViewModel.Profiles, Is.EqualTo(profiles), "editProfileViewModel.Profiles");
            Assert.That(editProfileViewModel.Profiles.Count, Is.EqualTo(2), "editProfileViewModel.Profiles.Count");
            Assert.That(editProfileViewModel.Profile, Is.Not.Null, "editProfileViewModel.Profile");
            Assert.That(editProfileViewModel.Profile, Is.EqualTo(profiles[1]), "editProfileViewModel.Profile");
            Assert.That(editProfileViewModel.Profiles.Contains(deleteProfile), Is.False, "editProfileViewModel.Profiles.Contains(deleteProfile)");
            Assert.That(editProfileViewModel.WindowTitle, Is.EqualTo("Edit Profile - " + _profileName3), "editProfileViewModel.WindowTitle");
            Assert.That(File.Exists(_defaultProfile), Is.False, "File.Exists(_defaultProfile)");
            Assert.That(File.Exists(_unitTestsProfile), Is.True, "File.Exists(_unitTestsProfile)");
            Assert.That(File.Exists(_unitTestsProfile2), Is.False, "File.Exists(_unitTestsProfile2)");
            Assert.That(File.Exists(_unitTestsProfile3), Is.True, "File.Exists(_unitTestsProfile3)");

            // Arrange
            confirmationShown = false;

            // Act - Delete 2nd of 2 profiles
            deleteProfile = profiles[1];
            editProfileViewModel.Profile = deleteProfile;
            editProfileViewModel.DeleteProfileCommand.Execute(null);

            // Assert
            Assert.That(confirmationShown, Is.True, "confirmationShown");
            Assert.That(editProfileViewModel.Profiles, Is.EqualTo(profiles), "editProfileViewModel.Profiles");
            Assert.That(editProfileViewModel.Profiles.Count, Is.EqualTo(1), "editProfileViewModel.Profiles.Count");
            Assert.That(editProfileViewModel.Profile, Is.Not.Null, "editProfileViewModel.Profile");
            Assert.That(editProfileViewModel.Profile, Is.EqualTo(profiles[0]), "editProfileViewModel.Profile");
            Assert.That(editProfileViewModel.Profiles.Contains(deleteProfile), Is.False, "editProfileViewModel.Profiles.Contains(deleteProfile)");
            Assert.That(editProfileViewModel.WindowTitle, Is.EqualTo("Edit Profile - " + _profileName), "editProfileViewModel.WindowTitle");
            Assert.That(File.Exists(_defaultProfile), Is.False, "File.Exists(_defaultProfile)");
            Assert.That(File.Exists(_unitTestsProfile), Is.True, "File.Exists(_unitTestsProfile)");
            Assert.That(File.Exists(_unitTestsProfile2), Is.False, "File.Exists(_unitTestsProfile2)");
            Assert.That(File.Exists(_unitTestsProfile3), Is.False, "File.Exists(_unitTestsProfile3)");

            // Arrange
            confirmationShown = false;

            // Act - Delete last remaining profile in list
            deleteProfile = profiles[0];
            editProfileViewModel.Profile = deleteProfile;
            editProfileViewModel.DeleteProfileCommand.Execute(null);

            // Assert
            Assert.That(confirmationShown, Is.True, "confirmationShown");
            Assert.That(editProfileViewModel.Profiles, Is.EqualTo(profiles), "editProfileViewModel.Profiles");
            Assert.That(editProfileViewModel.Profiles.Count, Is.EqualTo(1), "editProfileViewModel.Profiles.Count");
            Assert.That(editProfileViewModel.Profile, Is.Not.Null, "editProfileViewModel.Profile");
            Assert.That(editProfileViewModel.Profiles.Contains(deleteProfile), Is.False, "editProfileViewModel.Profiles.Contains(deleteProfile)");
            Assert.That(editProfileViewModel.Profile.ProfileName, Is.EqualTo("Default"), "editProfileViewModel.Profile");
            Assert.That(editProfileViewModel.WindowTitle, Is.EqualTo("Edit Profile - Default"), "editProfileViewModel.WindowTitle");
            Assert.That(File.Exists(_defaultProfile), Is.True, "File.Exists(_defaultProfile)");
            Assert.That(File.Exists(_unitTestsProfile), Is.False, "File.Exists(_unitTestsProfile)");
            Assert.That(File.Exists(_unitTestsProfile2), Is.False, "File.Exists(_unitTestsProfile2)");
            Assert.That(File.Exists(_unitTestsProfile3), Is.False, "File.Exists(_unitTestsProfile3)");
        }

        [Test]
        public void DeleteProfileCancelDialogTest()
        {
            // Arrange
            DeleteUnitTestsProfile();

            Assert.That(File.Exists(_unitTestsProfile), Is.False, "File.Exists(_unitTestsProfile)");

            UserProfile profile = new UserProfile();
            profile.ProfileName = _profileName;
            profile.Save();

            Assert.That(File.Exists(_unitTestsProfile), Is.True, "File.Exists(_unitTestsProfile)");

            EditProfileViewModel editProfileViewModel = IoC.Resolve<EditProfileViewModel>();
            bool confirmationShown = false;
            editProfileViewModel.ShowMessageBox += (sender, args) => { confirmationShown = true; args.Data.Result = MessageBoxResult.No; }; // delete confirmation

            // Assert - initial state
            Assert.That(editProfileViewModel.Profiles, Is.Not.Null, "editProfileViewModel.Profiles");
            Assert.That(editProfileViewModel.Profile, Is.Not.Null, "editProfileViewModel.Profile");
            Assert.That(editProfileViewModel.Profile.ProfileName, Is.EqualTo(_profileName), "editProfileViewModel.Profile.ProfileName");
            Assert.That(File.Exists(_unitTestsProfile), Is.True, "File.Exists(_unitTestsProfile)");
            Assert.That(File.Exists(_defaultProfile), Is.False, "File.Exists(_defaultProfile)");

            // Act
            editProfileViewModel.DeleteProfileCommand.Execute(null);

            // Assert
            Assert.That(confirmationShown, Is.True, "confirmationShown");
            Assert.That(editProfileViewModel.Profiles, Is.Not.Null, "editProfileViewModel.Profiles");
            Assert.That(editProfileViewModel.Profile, Is.Not.Null, "editProfileViewModel.Profile");
            Assert.That(editProfileViewModel.Profile.ProfileName, Is.EqualTo(_profileName), "editProfileViewModel.Profile.ProfileName");
            Assert.That(File.Exists(_unitTestsProfile), Is.True, "File.Exists(_unitTestsProfile)");
            Assert.That(File.Exists(_defaultProfile), Is.False, "File.Exists(_defaultProfile)");
        }

        [Test]
        public void DeleteProfileNullTest()
        {
            // Arrange
            DeleteUnitTestsProfile();

            EditProfileViewModel editProfileViewModel = IoC.Resolve<EditProfileViewModel>();
            bool confirmationShown = false;
            editProfileViewModel.ShowMessageBox += (sender, args) => { confirmationShown = true; args.Data.Result = MessageBoxResult.Yes; }; // delete confirmation

            // Act
            editProfileViewModel.Profile = null;
            editProfileViewModel.DeleteProfileCommand.Execute(null);

            // Assert
            Assert.That(confirmationShown, Is.False, "confirmationShown");
            Assert.That(editProfileViewModel.Profiles, Is.Not.Null, "editProfileViewModel.Profiles");
            Assert.That(editProfileViewModel.Profile, Is.Null, "editProfileViewModel.Profile");
            //Assert.That(editProfileViewModel.Profile.ProfileName, Is.EqualTo(_profileName), "editProfileViewModel.Profile.ProfileName");
            Assert.That(File.Exists(_unitTestsProfile), Is.False, "File.Exists(_unitTestsProfile)");
            Assert.That(File.Exists(_defaultProfile), Is.True, "File.Exists(_defaultProfile)");
        }

        [Test]
        public void NewProfileSaveChangesToCurentYesTest()
        {
            // Arrange
            DeleteUnitTestsProfile();

            EditProfileViewModel editProfileViewModel = IoC.Resolve<EditProfileViewModel>();

            // Act

            // Assert
            Assert.Ignore("Not implemented (upstream placeholder test)");
        }

        [Test]
        public void NewProfileSaveChangesToCurentNoTest()
        {
            // Arrange
            DeleteUnitTestsProfile();

            EditProfileViewModel editProfileViewModel = IoC.Resolve<EditProfileViewModel>();

            // Act

            // Assert
            Assert.Ignore("Not implemented (upstream placeholder test)");
        }

        [Test]
        public void NewProfileSaveChangesToCurentCancelTest()
        {
            // Arrange
            DeleteUnitTestsProfile();

            EditProfileViewModel editProfileViewModel = IoC.Resolve<EditProfileViewModel>();

            // Act

            // Assert
            Assert.Ignore("Not implemented (upstream placeholder test)");
        }

        [Test]
        public void NewProfileCancelTest()
        {
            // Arrange
            DeleteUnitTestsProfile();

            EditProfileViewModel editProfileViewModel = IoC.Resolve<EditProfileViewModel>();

            // Act

            // Assert
            Assert.Ignore("Not implemented (upstream placeholder test)");
        }

        [Test]
        public void NewProfileTest()
        {
            // Arrange
            DeleteUnitTestsProfile();

            EditProfileViewModel editProfileViewModel = IoC.Resolve<EditProfileViewModel>();

            // Act

            // Assert
            Assert.Ignore("Not implemented (upstream placeholder test)");
        }

        [Test]
        public void NewProfileOverwriteTest()
        {
            // Arrange
            DeleteUnitTestsProfile();

            EditProfileViewModel editProfileViewModel = IoC.Resolve<EditProfileViewModel>();

            // Act

            // Assert
            Assert.Ignore("Not implemented (upstream placeholder test)");
        }

        [Test]
        public void OkSavesEditedSettingsAndCloses()
        {
            DeleteUnitTestsProfile();
            new UserProfile { ProfileName = _profileName }.Save();
            EditProfileViewModel vm = IoC.Resolve<EditProfileViewModel>();
            bool? closed = null;
            vm.CloseWindow = r => closed = r;

            vm.SelectedPage = 1;
            vm.SelectedSlot = vm.NamingSlots[2];
            vm.NamingFormat.Directory = "<Artist>-<Album>-<Discs>CD-<Year>-GRP";
            vm.Profile.Finish.CreateSFV = false;
            vm.Tagging.LongTrackNumbers = true;
            vm.SourcesText = "CD\r\nWEB-FLAC\r\nNormal | ";
            vm.ReplacementsText = " & => _and_\r\n";
            vm.DeleteFilesText = "*.sfv\r\n*.m3u";
            vm.RefreshChanges();
            Assert.That(vm.WindowTitle, Does.EndWith("*"), "unsaved changes show in the title");

            vm.OKCommand.Execute(null);

            Assert.That(closed, Is.True);
            UserProfile saved = UserProfile.Load(_unitTestsProfile);
            Assert.That(saved.FileNaming.MultiCD.SingleArtist.Directory, Is.EqualTo("<Artist>-<Album>-<Discs>CD-<Year>-GRP"));
            Assert.That(saved.Finish.CreateSFV, Is.False);
            Assert.That(saved.Tagging.LongTrackNumbers, Is.True);
            Assert.That(saved.ReleaseInfo.Sources.Select(s => s.Nfo + "/" + s.Dir), Is.EqualTo(new[] { "CD/CD", "WEB-FLAC/WEB-FLAC", "Normal/" }));
            Assert.That(saved.NamingRules.Replacements[0].Find, Is.EqualTo(" &"));
            Assert.That(saved.NamingRules.Replacements[0].Replace, Is.EqualTo("_and_"));
            Assert.That(saved.DeleteFiles, Is.EqualTo(new[] { "*.sfv", "*.m3u" }));
        }

        [Test]
        public void CancelAsksBeforeDroppingChanges()
        {
            DeleteUnitTestsProfile();
            new UserProfile { ProfileName = _profileName }.Save();
            EditProfileViewModel vm = IoC.Resolve<EditProfileViewModel>();
            bool? closed = null;
            vm.CloseWindow = r => closed = r;
            string asked = null;
            vm.ShowMessageBox += (s, e) => { asked = e.Data.MessageBoxText; e.Data.Result = MessageBoxResult.No; };

            vm.Profile.Finish.CreateSFV = !vm.Profile.Finish.CreateSFV;
            vm.CancelCommand.Execute(null);

            Assert.That(asked, Does.Contain(_profileName));
            Assert.That(closed, Is.False);
            Assert.That(UserProfile.Load(_unitTestsProfile).Finish.CreateSFV, Is.EqualTo(new UserProfile().Finish.CreateSFV), "not saved");
        }

        [Test]
        public void RenameAndCopy()
        {
            DeleteUnitTestsProfile();
            string profileDirectory = IoC.Resolve<IPathService>().ProfileDirectory;
            var profile = new UserProfile { ProfileName = _profileName };
            profile.NFOOptions.TemplatePath = _profileName + ".nfo";
            profile.Save();
            File.WriteAllText(_unitTestsNFO, "nfo");
            EditProfileViewModel vm = IoC.Resolve<EditProfileViewModel>();

            vm.NewProfileName = _profileName2;
            vm.RenameProfileCommand.Execute(null);

            Assert.That(vm.Profile.ProfileName, Is.EqualTo(_profileName2));
            Assert.That(File.Exists(_unitTestsProfile), Is.False);
            Assert.That(File.Exists(_unitTestsProfile2), Is.True);
            Assert.That(File.Exists(Path.Combine(profileDirectory, _profileName2 + ".nfo")), Is.True, "the template follows the profile");
            Assert.That(UserProfile.Load(_unitTestsProfile2).NFOOptions.TemplatePath, Is.EqualTo(_profileName2 + ".nfo"));

            string copyFile = Path.Combine(profileDirectory, _profileName2 + " copy.cfg");
            try
            {
                vm.CopyProfileCommand.Execute(null);
                Assert.That(vm.Profile.ProfileName, Is.EqualTo(_profileName2 + " copy"));
                Assert.That(File.Exists(copyFile), Is.True);
                Assert.That(vm.Profiles.Count, Is.EqualTo(2));
            }
            finally
            {
                File.Delete(copyFile);
                File.Delete(Path.Combine(profileDirectory, _profileName2 + " copy.nfo"));
                File.Delete(Path.Combine(profileDirectory, _profileName2 + ".nfo"));
            }
        }
    }
}

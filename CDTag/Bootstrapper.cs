using System;
using System.Linq;
using System.Windows;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Common.Events;
using CDTag.FileBrowser.ViewModel;
using CDTag.View;
using CDTag.ViewModel.Options;
using CDTag.ViewModels.About;
using CDTag.ViewModels.Checksum;
using CDTag.ViewModels.Profile.EditProfile;
using CDTag.ViewModels.Profile.NewProfile;
using CDTag.ViewModels.Tag;
using CDTag.ViewModels.Tag.EditTag;
using CDTag.ViewModels.Tag.MassTag;
using CDTag.ViewModels.Tag.TagAlbum;
using CDTag.ViewModels.Tools;
using CDTag.Views;
using CDTag.Views.About;
using CDTag.Views.Checksum;
using CDTag.Views.Interfaces.About;
using CDTag.Views.Interfaces.Checksum;
using CDTag.Views.Interfaces.Options;
using CDTag.Views.Interfaces.Profile.EditProfile;
using CDTag.Views.Interfaces.Profile.NewProfile;
using CDTag.Views.Interfaces.Tag.EditTag;
using CDTag.Views.Interfaces.Tag.MassTag;
using CDTag.Views.Interfaces.Tag.TagAlbum;
using CDTag.Views.Interfaces.Tools;
using CDTag.Views.Options;
using CDTag.Views.Profile.EditProfile;
using CDTag.Views.Profile.NewProfile;
using CDTag.Views.Tag.EditTag;
using CDTag.Views.Tag.MassTag;
using CDTag.Views.Tag.TagAlbum;
using CDTag.Views.Tools;
using CDTag.ViewModels.Profile.ChooseProfile;
using CDTag.Views.Interfaces.Profile.ChooseProfile;
using CDTag.Views.Profile.ChooseProfile;
using CDTag.Views.Theming;

namespace CDTag
{
    public class Bootstrapper
    {
        public void Run()
        {
            ConfigureContainer();
            ThemeManager.Instance.Initialize(Application.Current);
            CreateShell();
        }

        protected void ConfigureContainer()
        {
            // Instances
            IoC.RegisterInstance<IDispatcher>(new ApplicationDispatcher());
            IoC.RegisterInstance<IDialogService>(new DialogService());
            IoC.RegisterInstance<IPathService>(new PathService());
            InstallDefaults();
            IoC.RegisterInstance<IThemeService>(ThemeManager.Instance);
            IoC.RegisterInstance<ITagViewModel>(IoC.Resolve<TagViewModel>());
            // One instance per tab, shared by the tab view and its menu.
            IoC.RegisterInstance<CDTag.ViewModels.Rip.RipViewModel>(IoC.Resolve<CDTag.ViewModels.Rip.RipViewModel>());
            IoC.RegisterInstance<CDTag.ViewModels.ConvertAudio.ConvertAudioViewModel>(IoC.Resolve<CDTag.ViewModels.ConvertAudio.ConvertAudioViewModel>());

            // Views
            IoC.RegisterType<IAboutWindow, AboutWindow>();
            IoC.RegisterType<IEditTagWindow, ID3v2Window>();
            IoC.RegisterType<ITagAlbumWindow, TagAlbumWindow>();
            IoC.RegisterType<IMassTagWindow, MassTagWindow>();
            IoC.RegisterType<IEditProfileWindow, EditProfileWindow>();
            IoC.RegisterType<INewProfileWindow, NewProfileWindow>();
            IoC.RegisterType<IChooseProfileWindow, ChooseProfileWindow>();
            IoC.RegisterType<CDTag.Views.Interfaces.Tag.TagAlbum.IPreviewNfoWindow, CDTag.Views.Tag.TagAlbum.PreviewNfoWindow>();
            IoC.RegisterType<CDTag.Views.Interfaces.Tag.TagAlbum.ILookupWindow, CDTag.Views.Tag.TagAlbum.LookupWindow>();
            IoC.RegisterType<CDTag.Views.Interfaces.Tag.TagAlbum.IReleaseNotesWindow, CDTag.Views.Tag.TagAlbum.ReleaseNotesWindow>();
            IoC.RegisterType<ISplitCueWindow, SplitCueWindow>();
            IoC.RegisterType<IEncodingInspectorWindow, EncodingInspectorWindow>();
            IoC.RegisterType<IOptionsWindow, OptionsWindow>();
            IoC.RegisterType<IChecksumWindow, ChecksumWindow>();
            IoC.RegisterType<IVerifyEACLogWindow, VerifyEACLogWindow>();

            // View models
            IoC.RegisterType<IDirectoryController, DirectoryController>();
            IoC.RegisterType<IAboutViewModel, AboutViewModel>();
            IoC.RegisterType<IID3v2ViewModel, ID3v2ViewModel>();
            IoC.RegisterType<ITagAlbumViewModel, TagAlbumViewModel>();
            IoC.RegisterType<IMassTagViewModel, MassTagViewModel>();
            IoC.RegisterType<IEditProfileViewModel, EditProfileViewModel>();
            IoC.RegisterType<INewProfileViewModel, NewProfileViewModel>();
            IoC.RegisterType<IChooseProfileViewModel, ChooseProfileViewModel>();
            IoC.RegisterType<CDTag.ViewModels.Tag.TagAlbum.IPreviewNfoViewModel, CDTag.ViewModels.Tag.TagAlbum.PreviewNfoViewModel>();
            IoC.RegisterType<CDTag.ViewModels.Tag.TagAlbum.ILookupViewModel, CDTag.ViewModels.Tag.TagAlbum.LookupViewModel>();
            IoC.RegisterType<CDTag.ViewModels.Tag.TagAlbum.IReleaseNotesViewModel, CDTag.ViewModels.Tag.TagAlbum.ReleaseNotesViewModel>();
            IoC.RegisterType<ISplitCueViewModel, SplitCueViewModel>();
            IoC.RegisterType<IEncodingInspectorViewModel, EncodingInspectorViewModel>();
            IoC.RegisterType<IOptionsViewModel, OptionsViewModel>();
            IoC.RegisterType<IChecksumViewModel, ChecksumViewModel>();
            IoC.RegisterType<IVerifyEACLogViewModel, VerifyEACLogViewModel>();

            // Events
            IoC.Resolve<IEventAggregator>().Subscribe<MessageBoxEvent>(ShowMessageBox);
        }

        private static void ShowMessageBox(MessageBoxEvent messageBox)
        {
            var result = CDTag.Common.ThemedMessageBox.Show(messageBox.Owner as Window, messageBox.MessageBoxText, messageBox.Caption,
                                                            messageBox.MessageBoxButton, messageBox.MessageBoxImage);

            messageBox.Result = result;
        }

        protected DependencyObject CreateShell()
        {
            MainWindow shell = IoC.Resolve<MainWindow>();
            shell.Show();
            CheckSceneRules(shell);
            CheckForUpdates(shell);

            return shell;
        }

        /// <summary>
        /// Looks for updated scene rules (scenerules.org) in the background on every start; tells the user when a
        /// rule file is new or has changed. Offline or failing: silent.
        /// </summary>
        /// <summary>
        /// First run: copies the blank NFO template in Defaults (next to cdtag.exe) to the Profiles folder when it isn't
        /// there yet. Existing files are never replaced.
        /// </summary>
        private static void InstallDefaults()
        {
            string defaults = System.IO.Path.Combine(AppContext.BaseDirectory, "Defaults");
            if (!System.IO.Directory.Exists(defaults))
                return;
            IPathService paths = IoC.Resolve<IPathService>();
            // A folder that already has profiles (e.g. a CD-Tag 2.x folder) is left as it is
            if (PathService.HasProfiles(paths.ProfileDirectory))
                return;
            CopyIfMissing(System.IO.Path.Combine(defaults, "blank.nfo"), System.IO.Path.Combine(paths.ProfileDirectory, "blank.nfo"));
        }

        private static void CopyIfMissing(string source, string target)
        {
            try
            {
                if (System.IO.File.Exists(source) && !System.IO.File.Exists(target))
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                    System.IO.File.Copy(source, target);
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                // Not essential: built-in defaults are used.
            }
        }

        /// <summary>Looks for a newer CD-Tag on GitHub (Options > Updates) and asks whether to install it. Offline: silent.</summary>
        private static void CheckForUpdates(Window shell)
        {
            CDTag.Model.Update.UpdateChecker.CleanUp();
            if (!CDTag.Common.Settings.AppOptions.Current.IsCheckForUpdates)
                return;
            // After the window is up, so the question has an owner on the right monitor.
            shell.Dispatcher.BeginInvoke(new Action(async () => await CDTag.ViewModel.Update.UpdatePrompt.CheckAsync(shell, quiet: true)),
                                         System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private static void CheckSceneRules(Window shell)
        {
            if (!CDTag.Common.Settings.AppOptions.Current.IsSceneTagging)
                return; // scene-style tagging is off ("enabled": "false" in options.json)
            string directory = System.IO.Path.Combine(IoC.Resolve<IPathService>().LocalApplicationDirectory, "Rules");
            var updater = new CDTag.Model.Tag.SceneRulesUpdater(directory);
            try
            {
                updater.ApplyGenres(); // last downloaded rules, until the check finishes
            }
            catch (Exception)
            {
            }

            // No rules yet (first start, or the Rules folder was emptied): download them without telling the user.
            bool silent = !updater.HasRules;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var changes = await updater.CheckAsync();
                    if (silent || changes.Count == 0)
                        return;
                    string text = "The scene rules on scenerules.org have changed:" + Environment.NewLine + Environment.NewLine +
                                  string.Join(Environment.NewLine, changes.Select(c => "- " + c)) + Environment.NewLine + Environment.NewLine +
                                  "The new rule files are saved in" + Environment.NewLine + directory;
                    shell.Dispatcher.BeginInvoke(new Action(() =>
                        CDTag.Common.ThemedMessageBox.Show(shell, text, "Scene rules", MessageBoxButton.OK, MessageBoxImage.Information)));
                }
                catch (Exception)
                {
                    // No connection or the site changed: try again next start.
                }
            });
        }
    }
}

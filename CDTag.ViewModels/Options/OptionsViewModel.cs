using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;
using CDTag.Common.Settings;

namespace CDTag.ViewModel.Options
{
    /// <summary>Tools > Options. Edits a copy of the options; OK applies and saves them.</summary>
    public class OptionsViewModel : ViewModelBase, IOptionsViewModel
    {
        private readonly IThemeService _themeService;

        public OptionsViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            _themeService = IoC.Resolve<IThemeService>();

            AppOptionsData options = AppOptions.Current;
            Theme = _themeService.ThemeName;
            IsStartupSpecific = options.StartupFolderMode == AppOptionsData.StartupSpecific;
            IsStartupLastUsed = !IsStartupSpecific;
            StartupFolder = options.StartupFolder;
            ShowHiddenFiles = options.ShowHiddenFiles;
            NewTagID3v2Version = options.NewTagID3v2Version == "ID3v24" ? "ID3v24" : "ID3v23";
            TagBothFormats = options.TagBothFormats;
            KeepRipLogs = options.KeepRipLogs;
            CheckForUpdates = options.IsCheckForUpdates;
            CurrentVersionText = "This is CD-Tag " + CDTag.Model.Update.UpdateChecker.CurrentVersion +
                                 ". When a newer version is on GitHub, CD-Tag asks whether to install it; your options and profiles are kept.";
            CheckForUpdatesNowCommand = new DelegateCommand(CheckForUpdatesNow);
            LamePath = options.LamePath;
            FlacPath = options.FlacPath;
            GnudbEmail = options.GnudbEmail;
            DiscogsToken = options.DiscogsToken;
            GetDiscogsTokenCommand = new DelegateCommand(() => OpenPage(CDTag.Model.Lookup.DiscogsClient.TokenPage));

            BrowseStartupFolderCommand = new DelegateCommand(BrowseStartupFolder);
            BrowseLameCommand = new DelegateCommand(() => BrowseExe("lame.exe", "LamePath"));
            BrowseFlacCommand = new DelegateCommand(() => BrowseExe("flac.exe", "FlacPath"));
            OKCommand = new DelegateCommand(Apply);
        }

        public string Theme
        {
            get { return Get<string>("Theme"); }
            set { Set("Theme", value); }
        }

        public bool IsStartupLastUsed
        {
            get { return Get<bool>("IsStartupLastUsed"); }
            set { Set("IsStartupLastUsed", value); }
        }

        public bool IsStartupSpecific
        {
            get { return Get<bool>("IsStartupSpecific"); }
            set { Set("IsStartupSpecific", value); }
        }

        public string StartupFolder
        {
            get { return Get<string>("StartupFolder"); }
            set { Set("StartupFolder", value); }
        }

        public bool ShowHiddenFiles
        {
            get { return Get<bool>("ShowHiddenFiles"); }
            set { Set("ShowHiddenFiles", value); }
        }

        /// <summary>Tag Album also tags the same album in the other format (FLAC and MP3 folders next to each other).</summary>
        /// <summary>Finish keeps (and renames) the rip logs; off = they are deleted.</summary>
        public bool KeepRipLogs
        {
            get { return Get<bool>("KeepRipLogs"); }
            set { Set("KeepRipLogs", value); }
        }

        /// <summary>Look for a newer CD-Tag on GitHub when CD-Tag starts.</summary>
        public bool CheckForUpdates
        {
            get { return Get<bool>("CheckForUpdates"); }
            set { Set("CheckForUpdates", value); }
        }

        /// <summary>"This is CD-Tag 3.2.7."</summary>
        public string CurrentVersionText
        {
            get { return Get<string>("CurrentVersionText"); }
            private set { Set("CurrentVersionText", value); }
        }

        /// <summary>Checks GitHub now and asks whether to install a newer version.</summary>
        public ICommand CheckForUpdatesNowCommand { get; private set; }

        private async void CheckForUpdatesNow()
        {
            System.Windows.Window owner = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
                                          ?? System.Windows.Application.Current.MainWindow;
            await CDTag.ViewModel.Update.UpdatePrompt.CheckAsync(owner, quiet: false);
        }

        public bool TagBothFormats
        {
            get { return Get<bool>("TagBothFormats"); }
            set { Set("TagBothFormats", value); }
        }

        public string NewTagID3v2Version
        {
            get { return Get<string>("NewTagID3v2Version"); }
            set { Set("NewTagID3v2Version", value); }
        }

        /// <summary>lame.exe; empty uses Encoders\mp3\lame.exe next to cdtag.exe when it is there, otherwise the built-in LAME.</summary>
        public string LamePath
        {
            get { return Get<string>("LamePath"); }
            set
            {
                Set("LamePath", value);
                LameInfo = EncoderInfo(value, CDTag.Model.Encoders.AudioEncoder.UserLameFolder, "lame", "lame.exe");
            }
        }

        /// <summary>Address registered with gnudb.org; empty = gnudb is not asked (Rip > Find CD).</summary>
        public string GnudbEmail
        {
            get { return Get<string>("GnudbEmail"); }
            set { Set("GnudbEmail", value); }
        }

        /// <summary>Discogs personal access token; empty = Discogs search is off (release links still work).</summary>
        public string DiscogsToken
        {
            get { return Get<string>("DiscogsToken"); }
            set { Set("DiscogsToken", value); }
        }

        /// <summary>Opens discogs.com's developer settings, where the personal access token is made.</summary>
        public ICommand GetDiscogsTokenCommand { get; private set; }

        private static void OpenPage(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        public string FlacPath
        {
            get { return Get<string>("FlacPath"); }
            set
            {
                Set("FlacPath", value);
                FlacInfo = EncoderInfo(value, CDTag.Model.Encoders.AudioEncoder.UserFlacFolder, "flac", "flac.exe");
            }
        }

        /// <summary>Which lame.exe is used (chosen, yours in Encoders\mp3, or built-in) and its version.</summary>
        public string LameInfo
        {
            get { return Get<string>("LameInfo"); }
            private set { Set("LameInfo", value); }
        }

        public string FlacInfo
        {
            get { return Get<string>("FlacInfo"); }
            private set { Set("FlacInfo", value); }
        }

        public ICommand BrowseLameCommand { get; private set; }
        public ICommand BrowseFlacCommand { get; private set; }

        /// <summary>"Using yours: Encoders\mp3\lame.exe (LAME 3.100)", "Using the built-in flac 1.5.0", or why none is found.</summary>
        private static string EncoderInfo(string configured, string userFolder, string folder, string exe)
        {
            string path;
            string where;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                path = configured.Trim();
                if (!File.Exists(path))
                    return "File not found.";
                where = null;
            }
            else if ((path = CDTag.Model.Encoders.AudioEncoder.UserPath(userFolder, exe)) != null)
            {
                where = string.Format(@"yours: Encoders\{0}\{1}", userFolder, exe);
            }
            else if ((path = CDTag.Model.Encoders.AudioEncoder.BundledPath(folder, exe)) != null)
            {
                where = "the built-in";
            }
            else
            {
                return string.Format(@"Not found. Put {0} in Encoders\{1} next to cdtag.exe, or choose it.", exe, userFolder);
            }

            string version = CDTag.Model.Encoders.AudioEncoder.GetVersion(path);
            if (where == null)
                return version ?? "Unknown version";
            return where == "the built-in"
                ? "Using the built-in " + (version ?? exe)
                : string.Format("Using {0}{1}", where, version != null ? " (" + version + ")" : "");
        }

        private void BrowseExe(string exe, string property)
        {
            string[] files;
            if (_dialogService.ShowOpenFilesDialog("Choose " + exe, exe + "|" + exe + "|Programs (*.exe)|*.exe", null, out files) == true && files != null && files.Length > 0)
            {
                if (property == "LamePath")
                    LamePath = files[0];
                else
                    FlacPath = files[0];
            }
        }

        public ICommand BrowseStartupFolderCommand { get; private set; }

        public ICommand OKCommand { get; private set; }

        private void BrowseStartupFolder()
        {
            string folder;
            if (_dialogService.ShowFolderDialog("Choose the folder to open at startup", StartupFolder, null, out folder) == true)
            {
                StartupFolder = folder;
                IsStartupSpecific = true;
                IsStartupLastUsed = false;
            }
        }

        private void Apply()
        {
            if (IsStartupSpecific && (string.IsNullOrWhiteSpace(StartupFolder) || !Directory.Exists(StartupFolder)))
            {
                MessageBox("The startup folder does not exist. Choose an existing folder, or select \"The last folder used\".",
                           "Options", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppOptionsData options = AppOptions.Current.Clone();
            options.StartupFolderMode = IsStartupSpecific ? AppOptionsData.StartupSpecific : AppOptionsData.StartupLastUsed;
            options.StartupFolder = string.IsNullOrWhiteSpace(StartupFolder) ? null : StartupFolder.Trim();
            options.ShowHiddenFiles = ShowHiddenFiles;
            options.NewTagID3v2Version = NewTagID3v2Version;
            options.TagBothFormats = TagBothFormats;
            options.KeepRipLogs = KeepRipLogs;
            options.CheckForUpdates = CheckForUpdates ? "true" : "false";
            options.LamePath = string.IsNullOrWhiteSpace(LamePath) ? null : LamePath.Trim();
            options.FlacPath = string.IsNullOrWhiteSpace(FlacPath) ? null : FlacPath.Trim();
            options.GnudbEmail = string.IsNullOrWhiteSpace(GnudbEmail) ? null : GnudbEmail.Trim();
            options.DiscogsToken = string.IsNullOrWhiteSpace(DiscogsToken) ? null : DiscogsToken.Trim();
            AppOptions.Save(options);

            if (!string.IsNullOrEmpty(Theme))
                _themeService.ThemeName = Theme;

            CloseWindow(true);
        }
    }
}

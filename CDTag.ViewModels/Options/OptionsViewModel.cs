using System;
using System.IO;
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
            LamePath = options.LamePath;
            FlacPath = options.FlacPath;

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

        public string NewTagID3v2Version
        {
            get { return Get<string>("NewTagID3v2Version"); }
            set { Set("NewTagID3v2Version", value); }
        }

        /// <summary>lame.exe; empty uses Encoders\lame next to cdtag.exe.</summary>
        public string LamePath
        {
            get { return Get<string>("LamePath"); }
            set
            {
                Set("LamePath", value);
                LameInfo = EncoderInfo(value, "lame", "lame.exe");
            }
        }

        public string FlacPath
        {
            get { return Get<string>("FlacPath"); }
            set
            {
                Set("FlacPath", value);
                FlacInfo = EncoderInfo(value, "flac", "flac.exe");
            }
        }

        /// <summary>"Using ...\Encoders\lame\lame.exe (LAME 3.100 (64-bit))" or "Not found".</summary>
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

        private static string EncoderInfo(string configured, string folder, string exe)
        {
            string path = !string.IsNullOrWhiteSpace(configured) ? configured.Trim() : Path.Combine(AppContext.BaseDirectory, "Encoders", folder, exe);
            if (!File.Exists(path))
                return string.IsNullOrWhiteSpace(configured) ? string.Format("Not found in Encoders\\{0}. Choose {1}.", folder, exe) : "File not found.";
            string version = CDTag.Model.Encoders.AudioEncoder.GetVersion(path);
            return string.IsNullOrWhiteSpace(configured)
                ? string.Format("Using Encoders\\{0}\\{1}{2}", folder, exe, version != null ? " (" + version + ")" : "")
                : version ?? "Unknown version";
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
            options.LamePath = string.IsNullOrWhiteSpace(LamePath) ? null : LamePath.Trim();
            options.FlacPath = string.IsNullOrWhiteSpace(FlacPath) ? null : FlacPath.Trim();
            AppOptions.Save(options);

            if (!string.IsNullOrEmpty(Theme))
                _themeService.ThemeName = Theme;

            CloseWindow(true);
        }
    }
}

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

            BrowseStartupFolderCommand = new DelegateCommand(BrowseStartupFolder);
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
            AppOptions.Save(options);

            if (!string.IsNullOrEmpty(Theme))
                _themeService.ThemeName = Theme;

            CloseWindow(true);
        }
    }
}

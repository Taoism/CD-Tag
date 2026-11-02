using System.Windows.Input;
using CDTag.Common.Mvvm;

namespace CDTag.ViewModel.Options
{
    public interface IOptionsViewModel : IViewModelBase
    {
        /// <summary>"System", "Light" or "Dark".</summary>
        string Theme { get; set; }

        bool IsStartupLastUsed { get; set; }
        bool IsStartupSpecific { get; set; }
        string StartupFolder { get; set; }

        bool ShowHiddenFiles { get; set; }

        /// <summary>"ID3v23" or "ID3v24".</summary>
        string NewTagID3v2Version { get; set; }

        ICommand BrowseStartupFolderCommand { get; }
        ICommand OKCommand { get; }
    }
}

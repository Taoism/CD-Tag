using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;

namespace CDTag.ViewModels.Tag.TagAlbum
{
    public interface IReleaseNotesViewModel : IViewModelBase
    {
        /// <summary>"NFO notes - MP3 release".</summary>
        string Title { get; set; }
        /// <summary>What the notes are for ("Notes for the MP3 release (Ween-Album-2CD-1994-GRP):").</summary>
        string Message { get; set; }
        string Notes { get; set; }
        ICommand OKCommand { get; }
    }

    public class ReleaseNotesViewModel : ViewModelBase, IReleaseNotesViewModel
    {
        public ReleaseNotesViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            OKCommand = new DelegateCommand(() => CloseWindow(true));
        }

        public string Title
        {
            get { return Get<string>("Title"); }
            set { Set("Title", value); }
        }

        public string Message
        {
            get { return Get<string>("Message"); }
            set { Set("Message", value); }
        }

        public string Notes
        {
            get { return Get<string>("Notes"); }
            set { Set("Notes", value); }
        }

        public ICommand OKCommand { get; private set; }
    }
}

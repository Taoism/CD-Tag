using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;

namespace CDTag.ViewModels.Tag.TagAlbum
{
    public interface IPreviewNfoViewModel : IViewModelBase
    {
        string Text { get; set; }
        string FileName { get; set; }
    }

    public class PreviewNfoViewModel : ViewModelBase, IPreviewNfoViewModel
    {
        public PreviewNfoViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
        }

        /// <summary>The generated NFO.</summary>
        public string Text
        {
            get { return Get<string>(nameof(Text)); }
            set { Set(nameof(Text), value); }
        }

        /// <summary>The name the NFO will be saved under (shown in the title).</summary>
        public string FileName
        {
            get { return Get<string>(nameof(FileName)); }
            set { Set(nameof(FileName), value); }
        }
    }
}

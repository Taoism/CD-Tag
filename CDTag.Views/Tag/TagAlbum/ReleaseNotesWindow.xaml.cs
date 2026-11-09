using CDTag.ViewModels.Tag.TagAlbum;
using CDTag.Views.Interfaces.Tag.TagAlbum;

namespace CDTag.Views.Tag.TagAlbum
{
    public partial class ReleaseNotesWindow : WindowViewBase, IReleaseNotesWindow
    {
        public ReleaseNotesWindow(IReleaseNotesViewModel viewModel)
            : base(viewModel)
        {
            InitializeComponent();
        }
    }
}

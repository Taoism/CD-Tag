using CDTag.ViewModels.Tag.TagAlbum;
using CDTag.Views.Interfaces.Tag.TagAlbum;

namespace CDTag.Views.Tag.TagAlbum
{
    public partial class LookupWindow : WindowViewBase, ILookupWindow
    {
        public LookupWindow(ILookupViewModel viewModel)
            : base(viewModel)
        {
            InitializeComponent();
            Loaded += (s, e) => viewModel.Start();
        }
    }
}

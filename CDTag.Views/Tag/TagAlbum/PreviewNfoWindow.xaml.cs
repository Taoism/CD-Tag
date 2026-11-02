using System.Windows;
using CDTag.ViewModels.Tag.TagAlbum;
using CDTag.Views.Interfaces.Tag.TagAlbum;

namespace CDTag.Views.Tag.TagAlbum
{
    public partial class PreviewNfoWindow : WindowViewBase, IPreviewNfoWindow
    {
        private readonly IPreviewNfoViewModel _viewModel;

        public PreviewNfoWindow(IPreviewNfoViewModel viewModel)
            : base(viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_viewModel.Text))
                Clipboard.SetText(_viewModel.Text);
        }
    }
}

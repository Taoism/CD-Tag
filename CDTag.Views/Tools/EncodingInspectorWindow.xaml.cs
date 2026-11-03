using System.Windows;
using CDTag.ViewModels.Tools;
using CDTag.Views.Interfaces.Tools;

namespace CDTag.Views.Tools
{
    /// <summary>
    /// Interaction logic for EncodingInspectorWindow.xaml
    /// </summary>
    public partial class EncodingInspectorWindow : WindowViewBase, IEncodingInspectorWindow
    {
        private readonly IEncodingInspectorViewModel _viewModel;

        public EncodingInspectorWindow(IEncodingInspectorViewModel viewModel)
            : base(viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
        }

        private void FileList_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void FileList_Drop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null)
                _viewModel.AddFiles(files);
            e.Handled = true;
        }
    }
}

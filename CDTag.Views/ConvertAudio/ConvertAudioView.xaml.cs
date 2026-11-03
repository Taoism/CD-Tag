using System.Windows;
using System.Windows.Controls;
using CDTag.Common;
using CDTag.ViewModels.ConvertAudio;

namespace CDTag.Views.ConvertAudio
{
    /// <summary>Convert Audio tab (CD-Tag 2.x layout).</summary>
    public partial class ConvertAudioView : UserControl
    {
        public ConvertAudioView()
        {
            InitializeComponent();
            DataContext = IoC.Resolve<ConvertAudioViewModel>();
        }

        private void Queue_DragOver(object sender, DragEventArgs e)
        {
            var viewModel = DataContext as ConvertAudioViewModel;
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && viewModel != null && !viewModel.IsBusy ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Queue_Drop(object sender, DragEventArgs e)
        {
            var viewModel = DataContext as ConvertAudioViewModel;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (viewModel != null && files != null && !viewModel.IsBusy)
                viewModel.AddFiles(files);
            e.Handled = true;
        }
    }
}

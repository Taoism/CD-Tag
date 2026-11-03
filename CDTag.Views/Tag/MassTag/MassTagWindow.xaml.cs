using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CDTag.ViewModels.Tag.MassTag;
using CDTag.Views.Interfaces.Tag.MassTag;

namespace CDTag.Views.Tag.MassTag
{
    /// <summary>
    /// Interaction logic for MassTagWindow.xaml
    /// </summary>
    public partial class MassTagWindow : WindowViewBase, IMassTagWindow
    {
        private readonly IMassTagViewModel _viewModel;

        public MassTagWindow(IMassTagViewModel viewModel)
            : base(viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
        }

        private void RowsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel.SelectedRows = RowsGrid.SelectedItems.Cast<object>().ToList();
        }

        private void RowsGrid_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void RowsGrid_Drop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null)
                _viewModel.AddFiles(files);
            e.Handled = true;
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Events;
using CDTag.ViewModels.ConvertAudio;
using IdSharp.Common.Events;

namespace CDTag.Views.ConvertAudio
{
    /// <summary>Convert Audio tab (CD-Tag 2.x layout).</summary>
    public partial class ConvertAudioView : UserControl
    {
        private readonly ConvertAudioViewModel _viewModel;

        public ConvertAudioView()
        {
            InitializeComponent();
            _viewModel = IoC.Resolve<ConvertAudioViewModel>();
            DataContext = _viewModel;

            // The tab is not a window, so it shows the view model's dialogs and message boxes itself.
            _viewModel.ShowDialogWindow += ViewModel_ShowDialogWindow;
            _viewModel.ShowMessageBox += ViewModel_ShowMessageBox;
        }

        private void ViewModel_ShowDialogWindow(object sender, DataEventArgs<ShowWindowEvent> e)
        {
            e.Data.Result = IoC.Resolve<IDialogService>().ShowWindow(e.Data.IWindow, Window.GetWindow(this));
        }

        private void ViewModel_ShowMessageBox(object sender, DataEventArgs<MessageBoxEvent> e)
        {
            MessageBoxEvent messageBoxEvent = e.Data;
            messageBoxEvent.Owner = Window.GetWindow(this) as CDTag.Views.Interfaces.IWindow;
            _viewModel.EventAggregator.Publish(messageBoxEvent);
        }

        private void Queue_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !_viewModel.IsBusy ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Queue_Drop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && !_viewModel.IsBusy)
                _viewModel.AddFiles(files);
            e.Handled = true;
        }
    }
}

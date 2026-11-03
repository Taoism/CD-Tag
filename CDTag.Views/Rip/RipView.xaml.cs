using System.Windows;
using System.Windows.Controls;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Events;
using CDTag.ViewModels.Rip;
using IdSharp.Common.Events;

namespace CDTag.Views.Rip
{
    /// <summary>Rip tab (CD-Tag 2.x layout).</summary>
    public partial class RipView : UserControl
    {
        private readonly RipViewModel _viewModel;

        public RipView()
        {
            InitializeComponent();
            _viewModel = IoC.Resolve<RipViewModel>();
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
    }
}

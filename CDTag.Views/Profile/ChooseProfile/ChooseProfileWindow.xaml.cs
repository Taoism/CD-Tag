using System.Windows.Input;
using CDTag.ViewModels.Profile.ChooseProfile;
using CDTag.Views.Interfaces.Profile.ChooseProfile;

namespace CDTag.Views.Profile.ChooseProfile
{
    public partial class ChooseProfileWindow : WindowViewBase, IChooseProfileWindow
    {
        private readonly IChooseProfileViewModel _viewModel;

        public ChooseProfileWindow(IChooseProfileViewModel viewModel)
            : base(viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
        }

        private void ProfileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.SelectedProfile != null && _viewModel.OKCommand.CanExecute(null))
                _viewModel.OKCommand.Execute(null);
        }
    }
}

using CDTag.Common;
using CDTag.ViewModels.ConvertAudio;
using CDTag.ViewModels.Rip;
using CDTag.ViewModels.Tag;

namespace CDTag.Views.Tag
{
    public partial class TagMenu : ViewBase
    {
        public TagMenu()
            : this(IoC.Resolve<ITagViewModel>())
        {
        }

        public TagMenu(ITagViewModel viewModel)
            : base(viewModel)
        {
            InitializeComponent();

            // The Rip / Encode menus act on the tab view models (shared instances, see Bootstrapper).
            RipMenu.DataContext = IoC.Resolve<RipViewModel>();
            EncodeMenu.DataContext = IoC.Resolve<ConvertAudioViewModel>();

            // Visibility of those two still follows the selected tab of the main view model.
            RipMenu.SetBinding(VisibilityProperty, TabVisibility(viewModel, "0"));
            EncodeMenu.SetBinding(VisibilityProperty, TabVisibility(viewModel, "1"));

            FavoritesMenuItem.SubmenuOpened += (s, e) =>
            {
                if (e.OriginalSource == FavoritesMenuItem)
                    FavoritesMenu.Fill(FavoritesMenuItem, viewModel);
            };
        }

        private System.Windows.Data.Binding TabVisibility(ITagViewModel viewModel, string tabIndex)
        {
            return new System.Windows.Data.Binding("SelectedTabIndex")
            {
                Source = viewModel,
                Converter = (System.Windows.Data.IValueConverter)FindResource("IsEqualToVisibilityConverter"),
                ConverterParameter = tabIndex
            };
        }
    }
}

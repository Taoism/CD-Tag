using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CDTag.Common;
using CDTag.ViewModels.Tag;

namespace CDTag.Views.Tag
{
    public partial class TagToolbar : ViewBase
    {
        private readonly ITagViewModel _viewModel;

        public TagToolbar()
            : this(IoC.Resolve<ITagViewModel>())
        {
        }

        public TagToolbar(ITagViewModel viewModel)
            : base(viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
        }

        private void FavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = FavoritesButton,
                Placement = PlacementMode.Bottom
            };
            FavoritesMenu.Fill(menu, _viewModel);
            menu.IsOpen = true;
        }
    }
}

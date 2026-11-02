using System.Windows.Controls;
using CDTag.ViewModels.Tag;

namespace CDTag.Views.Tag
{
    /// <summary>Builds the Favorites items (shared by the Favorites menu and the toolbar drop-down).</summary>
    internal static class FavoritesMenu
    {
        public static void Fill(ItemsControl target, ITagViewModel viewModel)
        {
            target.Items.Clear();

            target.Items.Add(new MenuItem { Header = "_Add current folder", Command = viewModel.AddFavoriteCommand });
            target.Items.Add(new MenuItem { Header = "_Remove current folder", Command = viewModel.RemoveFavoriteCommand });

            if (viewModel.Favorites.Count == 0)
                return;

            target.Items.Add(new Separator());
            foreach (string folder in viewModel.Favorites)
            {
                target.Items.Add(new MenuItem
                {
                    // Headers are plain text so "_" in folder names is not treated as an access key.
                    Header = new TextBlock { Text = folder },
                    Command = viewModel.GoToFavoriteCommand,
                    CommandParameter = folder,
                    ToolTip = folder
                });
            }
        }
    }
}

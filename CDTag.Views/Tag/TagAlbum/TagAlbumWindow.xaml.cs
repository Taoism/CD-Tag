using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CDTag.Model.Tag;
using CDTag.ViewModels.Tag.TagAlbum;
using CDTag.Views.Interfaces.Tag.TagAlbum;

namespace CDTag.Views.Tag.TagAlbum
{
    public partial class TagAlbumWindow : WindowViewBase, ITagAlbumWindow
    {
        private readonly ITagAlbumViewModel _viewModel;
        private Album _album;

        public TagAlbumWindow(ITagAlbumViewModel viewModel)
            : base(viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();

            // DataGrid columns are not in the visual tree, so the Artist column (various artists only) is toggled here.
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            AttachAlbum(_viewModel.Album);
            TracksDataGrid.PreviewKeyDown += TracksDataGrid_PreviewKeyDown;
        }

        // ------------------------------------------------------------------ Up / Down between tracks

        /// <summary>Up / Down in a track's box (song name, artist, track, disc) moves to the same box of the track above / below.</summary>
        private void TracksDataGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if ((e.Key != System.Windows.Input.Key.Up && e.Key != System.Windows.Input.Key.Down) || System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None)
                return;
            var box = System.Windows.Input.Keyboard.FocusedElement as TextBox;
            if (box == null)
                return;
            var cell = FindAncestor<DataGridCell>(box);
            var row = FindAncestor<DataGridRow>(box);
            if (cell == null || row == null)
                return;

            int index = TracksDataGrid.ItemContainerGenerator.IndexFromContainer(row) + (e.Key == System.Windows.Input.Key.Up ? -1 : 1);
            e.Handled = true;
            if (index < 0 || index >= TracksDataGrid.Items.Count)
                return;

            DataGridColumn column = cell.Column;
            object item = TracksDataGrid.Items[index];
            TracksDataGrid.SelectedIndex = index;
            TracksDataGrid.ScrollIntoView(item, column);
            TracksDataGrid.UpdateLayout();
            var next = TracksDataGrid.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
            FrameworkElement content = next != null ? column.GetCellContent(next) : null;
            TextBox target = content != null ? FindDescendant<TextBox>(content) : null;
            if (target != null)
            {
                target.Focus();
                target.CaretIndex = target.Text != null ? target.Text.Length : 0;
            }
        }

        private static T FindAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            while (element != null && !(element is T))
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            return element as T;
        }

        private static T FindDescendant<T>(DependencyObject element) where T : DependencyObject
        {
            if (element is T)
                return (T)element;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
            {
                T found = FindDescendant<T>(System.Windows.Media.VisualTreeHelper.GetChild(element, i));
                if (found != null)
                    return found;
            }
            return null;
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Album")
                AttachAlbum(_viewModel.Album);
        }

        private void AttachAlbum(Album album)
        {
            if (_album != null)
                _album.PropertyChanged -= Album_PropertyChanged;
            _album = album;
            if (_album != null)
                _album.PropertyChanged += Album_PropertyChanged;
            UpdateArtistColumn();
        }

        private void Album_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsVariousArtists")
                UpdateArtistColumn();
        }

        private void UpdateArtistColumn()
        {
            ArtistColumn.Visibility = _album != null && _album.IsVariousArtists ? Visibility.Visible : Visibility.Collapsed;
            PreviewArtistColumn.Visibility = ArtistColumn.Visibility;
        }

        // ------------------------------------------------------------------ song name menu

        private void SongNameMenu_Opened(object sender, RoutedEventArgs e)
        {
            var menu = (ContextMenu)sender;
            MenuItem paste = menu.Items.OfType<MenuItem>().FirstOrDefault(m => "_Paste Tracklist".Equals(m.Header));
            if (paste != null)
                paste.IsEnabled = ClipboardText() != null;

            MenuItem undo = menu.Items.OfType<MenuItem>().FirstOrDefault(m => "UndoTitles".Equals(m.Tag));
            if (undo != null)
            {
                string text = _viewModel.UndoTitlesText;
                undo.Header = text != null ? "_" + text : "_Undo";
                undo.IsEnabled = text != null;
            }
        }

        private void UndoTitles_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.UndoTitles();
        }

        private void Casing_Click(object sender, RoutedEventArgs e)
        {
            TitleCasing casing;
            if (Enum.TryParse((string)((MenuItem)sender).Tag, out casing))
                _viewModel.ApplyCasing(casing);
        }

        private void SearchReplace_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SearchReplaceWindow { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            int changed = _viewModel.SearchReplace(dialog.Find, dialog.ReplaceWith, dialog.MatchCase);
            if (changed == 0)
                CDTag.Common.ThemedMessageBox.Show(this, string.Format("\"{0}\" was not found in the song names.", dialog.Find), "Search & Replace", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PasteTracklist_Click(object sender, RoutedEventArgs e)
        {
            string text = ClipboardText();
            if (text == null)
                return;
            int lines = _viewModel.PasteTracklist(text);
            int tracks = _viewModel.Album.Tracks.Count;
            if (lines != tracks)
            {
                CDTag.Common.ThemedMessageBox.Show(this, string.Format("The track list has {0} song names for {1} tracks; the first {2} were filled in order.",
                                                    lines, tracks, Math.Min(lines, tracks)),
                                "Paste Tracklist", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static string ClipboardText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null; // clipboard busy
            }
        }
    }
}

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
        }

        // ------------------------------------------------------------------ song name menu

        private void SongNameMenu_Opened(object sender, RoutedEventArgs e)
        {
            var menu = (ContextMenu)sender;
            MenuItem paste = menu.Items.OfType<MenuItem>().FirstOrDefault(m => "_Paste Tracklist".Equals(m.Header));
            if (paste != null)
                paste.IsEnabled = ClipboardText() != null;
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
                MessageBox.Show(this, string.Format("\"{0}\" was not found in the song names.", dialog.Find), "Search & Replace", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show(this, string.Format("The track list has {0} song names for {1} tracks; the first {2} were filled in order.",
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

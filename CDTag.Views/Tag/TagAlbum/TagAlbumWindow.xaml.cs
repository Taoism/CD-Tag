using System.ComponentModel;
using System.Windows;
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
    }
}

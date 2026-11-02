using System;
using System.IO;
using System.Linq;
using CDTag.Common;
using CDTag.Common.Model;
using IdSharp.AudioInfo;
using IdSharp.Tagging.ID3v1;
using IdSharp.Tagging.ID3v2;
using IdSharp.Tagging.ID3v2.Frames;
using IdSharp.Tagging.VorbisComment;

namespace CDTag.Model.Tag
{
    public class AlbumTrack : ModelBase<AlbumTrack>
    {
        private readonly string _path;

        public AlbumTrack()
        {
        }

        public AlbumTrack(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException("path");

            _path = path;

            if (string.Equals(Path.GetExtension(path), ".flac", StringComparison.OrdinalIgnoreCase))
            {
                ReadFlacTags(path);
            }
            else if (ID3v2Tag.DoesTagExist(path))
            {
                ID3v2Tag id3v2 = new ID3v2Tag(path);
                DiscNumber = id3v2.DiscNumber;
                TrackNumber = id3v2.TrackNumber;
                Artist = id3v2.Artist;
                Title = id3v2.Title;
                ReleaseDate = id3v2.Year;
                Album = id3v2.Album;
                Genre = id3v2.Genre;
                if (id3v2.PictureList != null && id3v2.PictureList.Count == 1)
                {
                    Picture = id3v2.PictureList[0];
                }
            }

            if (!IsFlac && ID3v1Tag.DoesTagExist(path))
            {
                ID3v1Tag id3v1 = new ID3v1Tag(path);
                if (string.IsNullOrWhiteSpace(TrackNumber))
                    TrackNumber = string.Format("{0}", id3v1.TrackNumber);
                if (string.IsNullOrWhiteSpace(Artist))
                    Artist = id3v1.Artist;
                if (string.IsNullOrWhiteSpace(Title))
                    Title = id3v1.Title;
                if (string.IsNullOrWhiteSpace(ReleaseDate))
                    ReleaseDate = id3v1.Year;
                if (string.IsNullOrWhiteSpace(Album))
                    Album = id3v1.Album;
                if (string.IsNullOrWhiteSpace(Genre))
                    Genre = GenreHelper.GenreByIndex[id3v1.GenreIndex];
            }

            IAudioFile audioFile = AudioFile.Create(_path, throwExceptionIfUnknown: true);
            Bitrate = audioFile.Bitrate;
            TotalSeconds = audioFile.TotalSeconds;

            // TODO: APE, Lyrics3

            // TODO: When no tags, try to guess from path and file names
            // TODO: Parse Tracks for TotalTracks
            // TODO: Parse Disc for TotalDiscs

            // Parse track # from TrackNumber including total tracks
            if (!string.IsNullOrWhiteSpace(TrackNumber))
            {
                if (TrackNumber.Contains('/'))
                {
                    TrackNumber = TrackNumber.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    // TODO: Set total tracks?
                }
            }

            // Parse disc # from DiscNumber including total discs
            if (!string.IsNullOrWhiteSpace(DiscNumber))
            {
                if (DiscNumber.Contains('/'))
                {
                    DiscNumber = DiscNumber.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    // TODO: Set total discs?
                }
            }
            else
            {
                DiscNumber = "1";
            }
        }

        /// <summary>Reads Vorbis comments and the embedded cover from a FLAC file.</summary>
        private void ReadFlacTags(string path)
        {
            IsFlac = true;

            VorbisComment vorbis = new VorbisComment(path);
            Artist = vorbis.Artist;
            Title = vorbis.Title;
            Album = vorbis.Album;
            Genre = vorbis.Genre;
            ReleaseDate = vorbis.Year;
            TrackNumber = vorbis.TrackNumber;
            DiscNumber = vorbis.Items.GetValue("DISCNUMBER");

            // Prefer the front cover; otherwise take the first picture.
            foreach (byte[] block in vorbis.GetPictureBlocks())
            {
                Picture picture = Tag.Picture.FromFlacPictureBlock(block);
                if (picture == null || picture.ImageSource == null)
                    continue;
                if (CoverPicture == null || picture.PictureType == PictureType.CoverFront)
                    CoverPicture = picture;
                if (picture.PictureType == PictureType.CoverFront)
                    break;
            }
        }

        /// <summary>True for FLAC files (tags come from Vorbis comments).</summary>
        public bool IsFlac { get; private set; }

        /// <summary>Embedded cover of a FLAC file (MP3 covers are in <see cref="Picture"/>).</summary>
        public Picture CoverPicture
        {
            get { return Get<Picture>("CoverPicture"); }
            private set { Set("CoverPicture", value); }
        }

        public IAttachedPicture Picture
        {
            get { return Get<IAttachedPicture>("Picture"); }
            set { Set("Picture", value); }
        }

        public decimal Bitrate
        {
            get { return Get<decimal>("Bitrate"); }
            private set { Set("Bitrate", value); }
        }

        public decimal TotalSeconds
        {
            get { return Get<decimal>("TotalSeconds"); }
            private set { Set("TotalSeconds", value); }
        }

        public string OriginalFileName
        {
            get { return _path; }
        }

        public string DiscNumber
        {
            get { return Get<string>("DiscNumber"); }
            set { Set("DiscNumber", value); }
        }

        public string TrackNumber
        {
            get { return Get<string>("TrackNumber"); }
            set { Set("TrackNumber", value); }
        }

        public string Artist
        {
            get { return Get<string>("Artist"); }
            set { Set("Artist", value); }
        }

        public string Title
        {
            get { return Get<string>("Title"); }
            set { Set("Title", value); }
        }

        public string ReleaseDate
        {
            get { return Get<string>("ReleaseDate"); }
            set { Set("ReleaseDate", value); }
        }

        public string Album
        {
            get { return Get<string>("Album"); }
            set { Set("Album", value); }
        }

        public string Genre
        {
            get { return Get<string>("Genre"); }
            set { Set("Genre", value); }
        }
    }
}

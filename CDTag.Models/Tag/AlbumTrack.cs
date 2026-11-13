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
                throw new ArgumentNullException(nameof(path));

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
            Frequency = audioFile.Frequency;
            Channels = audioFile.Channels;
            ChannelMode = IsFlac ? (Channels == 1 ? "Single Channel" : "Stereo") : ReadMpegChannelMode(path) ?? (Channels == 1 ? "Single Channel" : "Stereo");
            try
            {
                FileSize = new System.IO.FileInfo(path).Length;
            }
            catch (System.IO.IOException)
            {
            }

            // TODO: APE, Lyrics3

            // TODO: When no tags, try to guess from path and file names
            // TODO: Parse Tracks for TotalTracks
            // TODO: Parse Disc for TotalDiscs

            // Parse track # from TrackNumber including total tracks
            if (!string.IsNullOrWhiteSpace(TrackNumber))
            {
                if (TrackNumber.Contains('/'))
                {
                    TrackNumber = TrackNumber.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
                    // TODO: Set total tracks?
                }
            }

            // Parse disc # from DiscNumber including total discs
            if (!string.IsNullOrWhiteSpace(DiscNumber))
            {
                if (DiscNumber.Contains('/'))
                {
                    DiscNumber = DiscNumber.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
                    // TODO: Set total discs?
                }
            }
            else
            {
                DiscNumber = "1";
                IsDiscNumberGuessed = true;
            }
        }

        /// <summary>True when the disc number did not come from a tag (it may be replaced by one from the file name).</summary>
        public bool IsDiscNumberGuessed { get; set; }

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
            DiscSubtitle = vorbis.Items.GetValue("DISCSUBTITLE");

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

        /// <summary>Disc name from the tags (FLAC DISCSUBTITLE), or null.</summary>
        public string DiscSubtitle { get; set; }

        private int? _bitsPerSample;

        /// <summary>Bits per sample of a FLAC file (read once), 0 when unknown or not FLAC.</summary>
        public int BitsPerSample
        {
            get
            {
                if (_bitsPerSample == null)
                {
                    CDTag.Model.Lookup.FlacStreamInfo info = IsFlac && _path != null ? CDTag.Model.Lookup.FlacStreamInfo.Read(_path) : null;
                    _bitsPerSample = info != null ? info.BitsPerSample : 0;
                }
                return _bitsPerSample.Value;
            }
        }

        /// <summary>True for FLAC files (tags come from Vorbis comments).</summary>
        public bool IsFlac { get; private set; }

        /// <summary>Creates a track from known values without reading a file (previews, tests).</summary>
        public static AlbumTrack FromValues(string path, string discNumber, string trackNumber, string artist, string title,
                                            decimal bitrate = 0, decimal totalSeconds = 0, long fileSize = 0)
        {
            var track = new AlbumTrack(path, readFile: false);
            track.DiscNumber = discNumber;
            track.TrackNumber = trackNumber;
            track.Artist = artist;
            track.Title = title;
            track.Bitrate = bitrate;
            track.TotalSeconds = totalSeconds;
            track.IsFlac = string.Equals(System.IO.Path.GetExtension(path), ".flac", StringComparison.OrdinalIgnoreCase);
            track.Frequency = 44100;
            track.Channels = 2;
            track.ChannelMode = "Stereo";
            track.FileSize = fileSize;
            return track;
        }

        private AlbumTrack(string path, bool readFile)
        {
            _path = path;
        }

        /// <summary>Sample rate in Hz (e.g. 44100).</summary>
        public int Frequency { get; private set; }

        public int Channels { get; private set; }

        /// <summary>"Stereo", "Joint Stereo", "Dual Channel" or "Single Channel".</summary>
        public string ChannelMode { get; private set; }

        /// <summary>File size in bytes.</summary>
        public long FileSize { get; private set; }

        /// <summary>Channel mode from the first MPEG audio frame header (after any ID3v2 tag), or null.</summary>
        private static string ReadMpegChannelMode(string path)
        {
            try
            {
                using (var stream = System.IO.File.OpenRead(path))
                {
                    long start = ID3v2Tag.DoesTagExist(path) ? ID3v2Tag.GetTagSize(path) : 0;
                    stream.Position = start;
                    var buffer = new byte[64 * 1024];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    for (int i = 0; i + 3 < read; i++)
                    {
                        // Frame sync (11 bits), valid version/layer/bitrate/sample-rate fields
                        if (buffer[i] != 0xFF || (buffer[i + 1] & 0xE0) != 0xE0)
                            continue;
                        if ((buffer[i + 1] & 0x18) == 0x08 || (buffer[i + 1] & 0x06) == 0 || (buffer[i + 2] & 0xF0) == 0xF0 || (buffer[i + 2] & 0x0C) == 0x0C)
                            continue;
                        switch (buffer[i + 3] >> 6)
                        {
                            case 0: return "Stereo";
                            case 1: return "Joint Stereo";
                            case 2: return "Dual Channel";
                            default: return "Single Channel";
                        }
                    }
                }
            }
            catch (System.IO.IOException)
            {
            }
            return null;
        }

        /// <summary>The name the profile gives this file (preview; set by Tag Album).</summary>
        public string NewFileName
        {
            get { return Get<string>(nameof(NewFileName)); }
            set { Set(nameof(NewFileName), value); }
        }

        /// <summary>Embedded cover of a FLAC file (MP3 covers are in <see cref="Picture"/>).</summary>
        public Picture CoverPicture
        {
            get { return Get<Picture>(nameof(CoverPicture)); }
            private set { Set(nameof(CoverPicture), value); }
        }

        public IAttachedPicture Picture
        {
            get { return Get<IAttachedPicture>(nameof(Picture)); }
            set { Set(nameof(Picture), value); }
        }

        public decimal Bitrate
        {
            get { return Get<decimal>(nameof(Bitrate)); }
            private set { Set(nameof(Bitrate), value); }
        }

        /// <summary>True when <see cref="TotalSeconds"/> is the exact CD length from the rip log.</summary>
        public bool IsLengthFromLog { get; private set; }

        /// <summary>Uses the exact CD length (from the rip log's TOC) instead of the length estimated from the file.</summary>
        public void SetCdLength(decimal seconds)
        {
            TotalSeconds = seconds;
            IsLengthFromLog = true;
        }

        public decimal TotalSeconds
        {
            get { return Get<decimal>(nameof(TotalSeconds)); }
            private set { Set(nameof(TotalSeconds), value); }
        }

        public string OriginalFileName
        {
            get { return _path; }
        }

        public string DiscNumber
        {
            get { return Get<string>(nameof(DiscNumber)); }
            set { Set(nameof(DiscNumber), value); }
        }

        public string TrackNumber
        {
            get { return Get<string>(nameof(TrackNumber)); }
            set { Set(nameof(TrackNumber), value); }
        }

        public string Artist
        {
            get { return Get<string>(nameof(Artist)); }
            set { Set(nameof(Artist), value); }
        }

        public string Title
        {
            get { return Get<string>(nameof(Title)); }
            set { Set(nameof(Title), value); }
        }

        public string ReleaseDate
        {
            get { return Get<string>(nameof(ReleaseDate)); }
            set { Set(nameof(ReleaseDate), value); }
        }

        public string Album
        {
            get { return Get<string>(nameof(Album)); }
            set { Set(nameof(Album), value); }
        }

        public string Genre
        {
            get { return Get<string>(nameof(Genre)); }
            set { Set(nameof(Genre), value); }
        }
    }
}

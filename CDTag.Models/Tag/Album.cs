using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using CDTag.Common;
using CDTag.Common.Model;
using IdSharp.Tagging.ID3v2;

namespace CDTag.Model.Tag
{
    public class Album : ModelBase<Album>
    {
        private readonly string _path;
        private readonly ObservableCollection<AlbumTrack> _tracks;

        public Album(string path)
            : this(path, FillFromNames(path, GetTracksFromPath(path)))
        {
        }

        /// <summary>
        /// Untagged rips: fills empty track fields from the file names ("0101 Koko.flac" = disc 1, track 1, "Koko")
        /// and empty artist/album/year from the folder name ("Ween-Boognish Provides-14CD-2026-FLAC").
        /// Values from tags are never replaced.
        /// </summary>
        public static List<AlbumTrack> FillFromNames(string path, List<AlbumTrack> tracks)
        {
            FolderNameInfo folder = NameGuesser.FromFolderName(Path.GetFileName((path ?? "").TrimEnd('\\', '/')));
            int discSubfolders = tracks.Select(t => Path.GetDirectoryName(t.OriginalFileName))
                                       .Where(d => IsDiscFolderName(Path.GetFileName(d)))
                                       .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int discCount = Math.Max(folder.Discs, discSubfolders);

            foreach (AlbumTrack track in tracks)
            {
                FileNameInfo file = NameGuesser.FromFileName(track.OriginalFileName, discCount, folder.Artist);

                if (string.IsNullOrWhiteSpace(track.Title))
                    track.Title = file.Title;
                if (string.IsNullOrWhiteSpace(track.TrackNumber) || track.TrackNumber.Trim() == "0")
                    track.TrackNumber = file.Track > 0 ? file.Track.ToString() : track.TrackNumber;
                if (track.IsDiscNumberGuessed)
                {
                    int disc = file.Disc;
                    if (disc == 0)
                    {
                        // CD1 / Disc 2 subfolders
                        System.Text.RegularExpressions.Match number = System.Text.RegularExpressions.Regex.Match(
                            Path.GetFileName(Path.GetDirectoryName(track.OriginalFileName)) ?? "", @"(\d+)");
                        if (IsDiscFolderName(Path.GetFileName(Path.GetDirectoryName(track.OriginalFileName))) && number.Success)
                            disc = int.Parse(number.Value);
                    }
                    if (disc > 0)
                        track.DiscNumber = disc.ToString();
                }
                if (string.IsNullOrWhiteSpace(track.Artist))
                    track.Artist = file.Artist ?? folder.Artist;
                if (string.IsNullOrWhiteSpace(track.Album))
                    track.Album = folder.Album;
                if (string.IsNullOrWhiteSpace(track.ReleaseDate))
                    track.ReleaseDate = folder.Year;
            }
            return tracks;
        }

        public Album(string path, ICollection<AlbumTrack> tracks)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException("path");
            if (tracks == null)
                throw new ArgumentNullException("tracks");
            if (tracks.Count == 0)
                throw new ArgumentException("tracks.Count == 0", "tracks");

            _path = path;
            _tracks = new ObservableCollection<AlbumTrack>(tracks);

            SetAlbumPropertiesFromTracks();
        }

        /// <summary>Audio file types Tag Album loads.</summary>
        public static readonly string[] SupportedExtensions = { ".mp3", ".flac" };

        /// <summary>
        /// Every supported audio file in <paramref name="path"/> and its subfolders (multi-disc rips keep
        /// discs in CD1/CD2/... folders), ordered by path so discs and tracks stay in sequence.
        /// Folders hidden by the file-browser rules (Tools > Options) are skipped.
        /// </summary>
        public static IEnumerable<string> GetAudioFiles(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return Enumerable.Empty<string>();

            var files = new List<string>();
            var pending = new Stack<KeyValuePair<string, int>>();
            pending.Push(new KeyValuePair<string, int>(path, 0));
            while (pending.Count > 0)
            {
                KeyValuePair<string, int> item = pending.Pop();
                string directory = item.Key;
                try
                {
                    files.AddRange(GetAudioFilesInFolder(directory));

                    if (item.Value >= MaxSubfolderDepth)
                        continue;

                    foreach (string subdirectory in Directory.EnumerateDirectories(directory))
                    {
                        if (CDTag.Common.Settings.AppOptions.IsVisible(new DirectoryInfo(subdirectory)))
                            pending.Push(new KeyValuePair<string, int>(subdirectory, item.Value + 1));
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }

            return files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>How many folder levels below the album folder are searched (Album\CD1\... = 1).</summary>
        private const int MaxSubfolderDepth = 2;

        private static readonly System.Text.RegularExpressions.Regex DiscFolderPattern = new System.Text.RegularExpressions.Regex(
            @"^\s*((cd|disc|disk|dvd|side)\s*[-_#.]?\s*[0-9a-z]{1,2}\b.*|\d{1,2})\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static IEnumerable<string> GetAudioFilesInFolder(string directory)
        {
            return Directory.EnumerateFiles(directory)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>True for folder names like "CD1", "Disc 2", "Disk_1", "Side A", "02".</summary>
        public static bool IsDiscFolderName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && DiscFolderPattern.IsMatch(name);
        }

        /// <summary>
        /// True when <paramref name="path"/> looks like a folder of albums rather than one album:
        /// no audio files of its own, and more than one non-disc subfolder that contains audio.
        /// </summary>
        public static bool IsCollectionFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            try
            {
                if (GetAudioFilesInFolder(path).Any())
                    return false;

                int albumLikeSubfolders = 0;
                foreach (string subdirectory in Directory.EnumerateDirectories(path))
                {
                    if (!CDTag.Common.Settings.AppOptions.IsVisible(new DirectoryInfo(subdirectory)))
                        continue;
                    if (IsDiscFolderName(Path.GetFileName(subdirectory)))
                        continue;
                    if (GetAudioFiles(subdirectory).Any() && ++albumLikeSubfolders > 1)
                        return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
            return false;
        }

        private static List<AlbumTrack> GetTracksFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException("path");

            string[] files = GetAudioFiles(path).ToArray();
            if (files.Length == 0)
                throw new Exception(string.Format("No audio files found in '{0}'", path)); // TODO: Localize

            var tracks = new List<AlbumTrack>();
            foreach (string file in files)
            {
                AlbumTrack track = new AlbumTrack(file);
                tracks.Add(track);
            }

            return tracks;
        }

        public ImageSource Picture
        {
            get { return Get<ImageSource>("Picture"); }
            set { Set("Picture", value); }
        }

        private void SetAlbumPropertiesFromTracks()
        {
            AlbumTrack first = _tracks[0];

            AlbumTitle = first.Album;
            Genre = first.Genre;
            Artist = first.Artist;
            ReleaseDate = first.ReleaseDate;
            if (first.Picture != null)
            {
                Picture = new Picture(first.Picture).ImageSource;
            }
            else if (first.CoverPicture != null)
            {
                Picture = first.CoverPicture.ImageSource;
            }

            for (int i = 1; i < _tracks.Count; i++)
            {
                AlbumTrack track = _tracks[i];

                if (AlbumTitle != track.Album)
                    AlbumTitle = null;
                if (Genre != track.Genre)
                    Genre = null;
                if (Artist != track.Artist)
                {
                    IsVariousArtists = true;
                    Artist = null;
                }
                if (ReleaseDate != track.ReleaseDate)
                    ReleaseDate = null;
            }

            // TODO: Use Album Artist from tracks
            // TODO: Use TCMP from tracks
        }

        public ObservableCollection<AlbumTrack> Tracks
        {
            get { return _tracks; }
        }

        public string OldDirectoryName
        {
            get { return _path; }
        }

        public string Artist
        {
            get { return Get<string>("Artist"); }
            set { Set("Artist", value); }
        }

        public string AlbumTitle
        {
            get { return Get<string>("AlbumTitle"); }
            set { Set("AlbumTitle", value); }
        }

        public string Genre
        {
            get { return Get<string>("Genre"); }
            set { Set("Genre", value); }
        }

        public string ReleaseDate
        {
            get { return Get<string>("ReleaseDate"); }
            set { Set("ReleaseDate", value); }
        }

        public bool IsVariousArtists
        {
            get { return Get<bool>("IsVariousArtists"); }
            set { Set("IsVariousArtists", value); }
        }

        /// <summary>Cover image bytes chosen in Tag Album (e.g. from an online lookup); null = keep the existing cover.</summary>
        public byte[] CoverBytes { get; set; }

        /// <summary>The folder name the profile gives this album (preview; set by Tag Album).</summary>
        public string NewFolderName
        {
            get { return Get<string>("NewFolderName"); }
            set { Set("NewFolderName", value); }
        }

        public void Finish()
        {
            foreach (AlbumTrack track in Tracks)
            {
                string ext = Path.GetExtension(track.OriginalFileName);
                // Rename in place: tracks may live in disc subfolders (CD1, CD2, ...).
                string trackDirectory = Path.GetDirectoryName(track.OriginalFileName);

                if (!string.IsNullOrWhiteSpace(track.TrackNumber) &&
                    !string.IsNullOrWhiteSpace(track.Artist) &&
                    !string.IsNullOrWhiteSpace(track.Title))
                {
                    string newFileName = string.Format("{0:00} - {1} - {2}{3}", int.Parse(track.TrackNumber), track.Artist, track.Title, ext);
                    newFileName = Path.Combine(trackDirectory, newFileName);
                    if (string.Compare(track.OriginalFileName, newFileName, ignoreCase: false) == 0)
                    {
                        newFileName = string.Format("{0:00} - {1}{2}", int.Parse(track.TrackNumber), track.Title, ext);
                        newFileName = Path.Combine(trackDirectory, newFileName);
                    }

                    File.Move(track.OriginalFileName, newFileName);
                }
            }
        }
    }
}

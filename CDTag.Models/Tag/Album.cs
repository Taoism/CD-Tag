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

            // CD1 / CD2 folders whose files are all tagged with the same disc: the folders say which disc.
            var discFolders = tracks.GroupBy(t => Path.GetDirectoryName(t.OriginalFileName), StringComparer.OrdinalIgnoreCase)
                .Where(g => IsDiscFolderName(Path.GetFileName(g.Key)))
                .ToList();
            if (discFolders.Count > 1)
            {
                var discsPerFolder = discFolders.Select(g => g.Select(t => (t.DiscNumber ?? "").Trim()).Distinct().ToList()).ToList();
                bool distinct = discsPerFolder.All(d => d.Count == 1) && discsPerFolder.Select(d => d[0]).Distinct().Count() == discFolders.Count;
                if (!distinct)
                {
                    foreach (var group in discFolders)
                    {
                        System.Text.RegularExpressions.Match number = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(group.Key), @"(\d+)");
                        if (!number.Success)
                            continue;
                        foreach (AlbumTrack track in group)
                            track.DiscNumber = int.Parse(number.Value).ToString();
                    }
                }
            }

            // Disc / track order (paths sort CD10 before CD2).
            return tracks.OrderBy(t => SortNumber(t.DiscNumber, 1)).ThenBy(t => SortNumber(t.TrackNumber, 0))
                         .ThenBy(t => t.OriginalFileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static int SortNumber(string text, int fallback)
        {
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(text ?? "", @"\d+");
            int number;
            return match.Success && int.TryParse(match.Value, out number) ? number : fallback;
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
            var damaged = new List<string>();
            foreach (string file in files)
            {
                try
                {
                    tracks.Add(new AlbumTrack(file));
                }
                catch (InvalidDataException)
                {
                    // Keep going so every damaged file is named at once, not one per try.
                    damaged.Add(DescribeDamagedFile(path, file));
                }
            }

            if (damaged.Count != 0)
            {
                throw new InvalidDataException(string.Format("{0} no audio that can be read: {1}. Download or rip {2} again.",
                    damaged.Count == 1 ? "This file has" : damaged.Count + " files have", string.Join("; ", damaged),
                    damaged.Count == 1 ? "it" : "them"));
            }

            return tracks;
        }

        /// <summary>"CD1\108 Title.mp3 (only zero bytes: an unfinished download or copy)" or "... (no audio frames found)".</summary>
        public static string DescribeDamagedFile(string albumPath, string file)
        {
            string name = Path.GetRelativePath(albumPath, file);
            string reason;
            try
            {
                reason = IsAllZero(file) ? "only zero bytes: an unfinished download or copy" : "no audio frames found";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                reason = ex.Message;
            }
            return string.Format("{0} ({1})", name, reason);
        }

        /// <summary>True when the file is empty or every byte is 0.</summary>
        public static bool IsAllZero(string file)
        {
            var buffer = new byte[1 << 16];
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length))
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (Array.FindIndex(buffer, 0, read, b => b != 0) >= 0)
                        return false;
                }
            }
            return true;
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
            foreach (AlbumTrack track in _tracks)
            {
                int disc;
                if (!string.IsNullOrWhiteSpace(track.DiscSubtitle) && int.TryParse((track.DiscNumber ?? "").Trim(), out disc) && !_discNames.ContainsKey(disc))
                    _discNames[disc] = track.DiscSubtitle.Trim();
            }
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

        private readonly Dictionary<int, string> _discNames = new Dictionary<int, string>();

        /// <summary>Disc names/subtitles by disc number ("McCabe's Guitar Shop, Santa Monica, CA, 10/9/94"); null when none.</summary>
        public string GetDiscName(int disc)
        {
            string name;
            return _discNames.TryGetValue(disc, out name) && !string.IsNullOrWhiteSpace(name) ? name : null;
        }

        public void SetDiscName(int disc, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                _discNames.Remove(disc);
            else
                _discNames[disc] = name.Trim();
            RaisePropertyChanged("DiscNames", null, null);
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

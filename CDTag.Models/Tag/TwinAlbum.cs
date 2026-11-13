using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CDTag.Model.Lookup;

namespace CDTag.Model.Tag
{
    /// <summary>
    /// The same rip in the other format: a FLAC album and its MP3 encode (or the other way round) in folders
    /// next to each other. Same discs, same number of tracks, and every track the same length.
    /// </summary>
    public static partial class TwinAlbum
    {
        /// <summary>Longest length difference (seconds) for two tracks to count as the same track.</summary>
        public const double MaxLengthDifference = 2.0;

        /// <summary>At most this many folders next to the album are looked at.</summary>
        public const int MaxSiblings = 400;

        /// <summary>"flac", "mp3", or null when the album mixes both.</summary>
        public static string FormatOf(Album album)
        {
            if (album == null || album.Tracks.Count == 0)
                return null;
            if (album.Tracks.All(t => t.IsFlac))
                return "flac";
            if (album.Tracks.All(t => string.Equals(Path.GetExtension(t.OriginalFileName), ".mp3", StringComparison.OrdinalIgnoreCase)))
                return "mp3";
            return null;
        }

        /// <summary>"FLAC" / "MP3" for messages.</summary>
        public static string FormatName(string format)
        {
            return (format ?? "").ToUpperInvariant();
        }

        /// <summary>
        /// Finds the other-format album: first among <paramref name="preferred"/> (other folders selected in the file
        /// list), then among the folders next to the album, most similar names first. Null when there is none.
        /// </summary>
        public static Album Find(Album album, IEnumerable<string> preferred = null)
        {
            string format = FormatOf(album);
            if (format == null)
                return null;
            string otherExtension = format == "flac" ? ".mp3" : ".flac";
            string own = album.OldDirectoryName.TrimEnd('\\', '/');

            var candidates = new List<string>();
            if (preferred != null)
                candidates.AddRange(preferred.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.TrimEnd('\\', '/')));

            string parent = Path.GetDirectoryName(own);
            if (!string.IsNullOrEmpty(parent))
            {
                string name = Path.GetFileName(own);
                try
                {
                    candidates.AddRange(Directory.GetDirectories(parent)
                        .Select(d => d.TrimEnd('\\', '/'))
                        .OrderByDescending(d => Similarity(name, Path.GetFileName(d)))
                        .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
                        .Take(MaxSiblings));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            Album best = null;
            double bestScore = double.MaxValue;
            foreach (string folder in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(folder, own, StringComparison.OrdinalIgnoreCase) || IsInside(folder, own) || IsInside(own, folder))
                    continue;

                List<string> files;
                try
                {
                    files = Album.GetAudioFiles(folder).ToList();
                }
                catch (Exception)
                {
                    continue;
                }
                if (files.Count != album.Tracks.Count || !files.All(f => string.Equals(Path.GetExtension(f), otherExtension, StringComparison.OrdinalIgnoreCase)))
                    continue;

                Album other;
                try
                {
                    other = new Album(folder);
                    DiscToc.ApplyLogLengths(other, DiscToc.ForAlbum(other));
                }
                catch (Exception)
                {
                    continue;
                }

                double score = Difference(album, other);
                if (score < bestScore)
                {
                    best = other;
                    bestScore = score;
                }
            }
            return best;
        }

        private static bool IsInside(string folder, string parent)
        {
            return folder.StartsWith(parent.TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Length of the common start of two folder names, ignoring case ("Ween-Album-2CD-FLAC" / "Ween-Album-2CD").</summary>
        private static int Similarity(string a, string b)
        {
            a = (a ?? "").ToLowerInvariant();
            b = (b ?? "").ToLowerInvariant();
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i])
                i++;
            return i;
        }

        /// <summary>True when <paramref name="other"/> is the same rip as <paramref name="album"/> in another format.</summary>
        public static bool IsSameAlbum(Album album, Album other)
        {
            return !double.IsInfinity(Difference(album, other));
        }

        /// <summary>
        /// Total length difference in seconds of the paired tracks (disc / track order), or infinity when the albums
        /// are not the same: other track count, other discs, a track more than 2 seconds off, or too few known lengths.
        /// </summary>
        public static double Difference(Album album, Album other)
        {
            if (album == null || other == null || album.Tracks.Count != other.Tracks.Count || album.Tracks.Count == 0)
                return double.PositiveInfinity;
            string a = FormatOf(album), b = FormatOf(other);
            if (a == null || b == null || a == b)
                return double.PositiveInfinity;

            List<AlbumTrack> left = Ordered(album), right = Ordered(other);
            if (!DiscLayout(left).SequenceEqual(DiscLayout(right)))
                return double.PositiveInfinity;

            double total = 0;
            int known = 0;
            for (int i = 0; i < left.Count; i++)
            {
                double x = (double)left[i].TotalSeconds, y = (double)right[i].TotalSeconds;
                if (x <= 0 || y <= 0)
                    continue;
                double difference = Math.Abs(x - y);
                if (difference > MaxLengthDifference)
                    return double.PositiveInfinity;
                total += difference;
                known++;
            }
            // Lengths are what tells two rips apart: most of them must be known.
            if (known * 2 < left.Count)
                return double.PositiveInfinity;
            return total;
        }

        /// <summary>Tracks per disc, in disc order.</summary>
        private static List<int> DiscLayout(List<AlbumTrack> tracks)
        {
            return tracks.GroupBy(t => DiscNumberOf(t)).OrderBy(g => g.Key).Select(g => g.Count()).ToList();
        }

        /// <summary>The tracks in disc / track order (as Tag Album lists them).</summary>
        public static List<AlbumTrack> Ordered(Album album)
        {
            return album.Tracks.OrderBy(t => DiscNumberOf(t)).ThenBy(t => Number(t.TrackNumber, 0))
                        .ThenBy(t => t.OriginalFileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static int DiscNumberOf(AlbumTrack track)
        {
            int number = Number(track.DiscNumber, 1);
            return number > 0 ? number : 1;
        }

        private static int Number(string text, int fallback)
        {
            Match match = Digits().Match(text ?? "");
            int number;
            return match.Success && int.TryParse(match.Value, out number) ? number : fallback;
        }

        /// <summary>
        /// Gives <paramref name="target"/> the album and track tags of <paramref name="source"/> (tracks paired in
        /// disc / track order), its disc names and the chosen cover. A cover only in the source files is copied when
        /// the target files have none.
        /// </summary>
        public static void CopyTags(Album source, Album target)
        {
            target.Artist = source.Artist;
            target.AlbumTitle = source.AlbumTitle;
            target.Genre = source.Genre;
            target.ReleaseDate = source.ReleaseDate;
            target.IsVariousArtists = source.IsVariousArtists;

            List<AlbumTrack> from = Ordered(source), to = Ordered(target);
            for (int i = 0; i < from.Count && i < to.Count; i++)
            {
                to[i].DiscNumber = from[i].DiscNumber;
                to[i].TrackNumber = from[i].TrackNumber;
                to[i].Artist = from[i].Artist;
                to[i].Title = from[i].Title;
                to[i].Album = from[i].Album;
                to[i].Genre = from[i].Genre;
                to[i].ReleaseDate = from[i].ReleaseDate;
            }

            int discs = Math.Max(from.Select(DiscNumberOf).DefaultIfEmpty(1).Max(), to.Select(DiscNumberOf).DefaultIfEmpty(1).Max());
            for (int disc = 1; disc <= discs; disc++)
                target.SetDiscName(disc, source.GetDiscName(disc));

            if (source.CoverBytes != null)
                target.CoverBytes = source.CoverBytes;
            else if (EmbeddedCover(target) == null)
                target.CoverBytes = EmbeddedCover(source);
        }

        /// <summary>The cover in the first track's tags, or null.</summary>
        private static byte[] EmbeddedCover(Album album)
        {
            AlbumTrack first = album.Tracks.FirstOrDefault();
            if (first == null)
                return null;
            if (first.Picture != null && first.Picture.PictureData != null && first.Picture.PictureData.Length > 0)
                return first.Picture.PictureData;
            if (first.CoverPicture != null && first.CoverPicture.PictureBytes != null && first.CoverPicture.PictureBytes.Length > 0)
                return first.CoverPicture.PictureBytes;
            return null;
        }

        /// <summary>
        /// The encoder read from the files: "LAME 3.100 -V0", "LAME 3.100 -b 320", "LAME 3.99", "FLAC 1.5.0"; null when unknown.
        /// </summary>
        public static string EncoderOf(Album album)
        {
            AlbumTrack first = album != null ? album.Tracks.FirstOrDefault() : null;
            if (first == null)
                return null;
            try
            {
                if (first.IsFlac)
                {
                    string vendor = FlacVendor(first.OriginalFileName);
                    Match version = LibFlacVendorVersion().Match(vendor ?? "");
                    return version.Success ? "FLAC " + version.Groups[1].Value : null;
                }

                LameHeader lame = LameHeader.Read(first.OriginalFileName);
                if (lame == null || string.IsNullOrEmpty(lame.Version))
                    return null;
                return "LAME " + lame.Version + (lame.IsSceneV0 ? " -V0" : lame.IsScene320 ? " -b 320" : "");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The vendor string of a FLAC file's Vorbis comment ("reference libFLAC 1.5.0 20250211"), or null.</summary>
        public static string FlacVendor(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var reader = new BinaryReader(stream);
                // An ID3v2 tag in front of fLaC.
                byte[] magic = reader.ReadBytes(4);
                if (magic.Length == 4 && magic[0] == 'I' && magic[1] == 'D' && magic[2] == '3')
                {
                    byte[] header = reader.ReadBytes(6);
                    if (header.Length < 6)
                        return null;
                    int size = (header[2] << 21) | (header[3] << 14) | (header[4] << 7) | header[5];
                    stream.Position = 10 + size;
                    magic = reader.ReadBytes(4);
                }
                if (magic.Length < 4 || Encoding.ASCII.GetString(magic) != "fLaC")
                    return null;

                while (stream.Position + 4 <= stream.Length)
                {
                    byte[] block = reader.ReadBytes(4);
                    bool last = (block[0] & 0x80) != 0;
                    int type = block[0] & 0x7F;
                    int length = (block[1] << 16) | (block[2] << 8) | block[3];
                    if (type == 4 && length >= 4)
                    {
                        int vendorLength = reader.ReadInt32();
                        if (vendorLength < 0 || vendorLength > length - 4)
                            return null;
                        return Encoding.UTF8.GetString(reader.ReadBytes(vendorLength));
                    }
                    stream.Position += length;
                    if (last)
                        break;
                }
            }
            return null;
        }

        [GeneratedRegex(@"\d+", RegexOptions.None, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex Digits();

        [GeneratedRegex(@"libFLAC\s+(\d+\.\d+(\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex LibFlacVendorVersion();
    }
}

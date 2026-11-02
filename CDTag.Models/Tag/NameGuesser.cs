using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CDTag.Model.Tag
{
    /// <summary>What could be read from a folder name such as "Ween-Boognish Provides-14CD-2026-FLAC".</summary>
    public class FolderNameInfo
    {
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Year { get; set; }
        public int Discs { get; set; }
    }

    /// <summary>What could be read from a file name such as "0101 Koko.flac".</summary>
    public class FileNameInfo
    {
        public int Disc { get; set; }
        public int Track { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
    }

    /// <summary>
    /// Fills in album and track information for untagged rips from folder and file names:
    /// folders "Artist-Album-14CD-2026[-FLAC]" (or "Artist - Album (2026)") and
    /// files "0101 Title", "101 Title" (multi-disc), "01 Title", "01 - Title", "1-01 Title", "01-Artist-Title".
    /// </summary>
    public static class NameGuesser
    {
        private static readonly Regex YearPart = new Regex(@"^\(?((?:19|20)\d{2})\)?$");
        private static readonly Regex DiscCountPart = new Regex(@"^(\d{1,2})\s?CDS?$", RegexOptions.IgnoreCase);

        // Release/format markers that end scene-style folder names and are not part of the album title.
        private static readonly HashSet<string> Markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FLAC", "MP3", "WEB", "WEB-FLAC", "CD", "CDS", "CDM", "CDR", "VINYL", "LP", "VLS", "EP", "DVD", "BLURAY", "SACD",
            "320", "V0", "V2", "16BIT", "24BIT", "24BIT-44KHZ", "24BIT-48KHZ", "24BIT-96KHZ", "24BIT-192KHZ", "WAV", "ALAC", "AAC",
            "PROPER", "REPACK", "DIRFIX", "NFOFIX", "RETAIL", "PROMO", "ADVANCE", "BOOTLEG", "REMASTERED", "REISSUE", "LIMITED", "DELUXE"
        };

        public static FolderNameInfo FromFolderName(string folderName)
        {
            var info = new FolderNameInfo();
            if (string.IsNullOrWhiteSpace(folderName))
                return info;

            string name = folderName.Trim();

            // "Artist - Album (2026) [FLAC]" style: take bracketed year, drop bracketed markers.
            Match bracketYear = Regex.Match(name, @"[\(\[]((?:19|20)\d{2})[\)\]]");
            if (bracketYear.Success)
            {
                info.Year = bracketYear.Groups[1].Value;
                name = name.Remove(bracketYear.Index, bracketYear.Length);
            }
            name = Regex.Replace(name, @"\[[^\]]*\]", " ").Trim();

            // Prefer " - " as the separator when present (artist/album may contain plain hyphens).
            List<string> parts = name.Contains(" - ")
                ? name.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList()
                : name.Split('-').Select(p => p.Trim()).ToList();
            parts = parts.Where(p => p.Length > 0).ToList();

            // Peel markers, year and disc count off the end.
            for (int i = parts.Count - 1; i >= 1; i--)
            {
                string part = parts[i];
                Match year = YearPart.Match(part);
                Match discs = DiscCountPart.Match(part);
                bool groupTagAtEnd = i == parts.Count - 1 && i >= 3 && Regex.IsMatch(part, @"^[A-Z0-9]{2,8}$");
                if (year.Success && info.Year == null)
                    info.Year = year.Groups[1].Value;
                else if (discs.Success && info.Discs == 0)
                    info.Discs = int.Parse(discs.Groups[1].Value, CultureInfo.InvariantCulture);
                else if (!Markers.Contains(part) && !groupTagAtEnd)
                    break;
                parts.RemoveAt(i);
            }

            if (parts.Count >= 2)
            {
                info.Artist = Tidy(parts[0]);
                info.Album = Tidy(string.Join("-", parts.Skip(1)));
            }
            else if (parts.Count == 1)
            {
                info.Album = Tidy(parts[0]);
            }
            return info;
        }

        public static FileNameInfo FromFileName(string fileName, int discCount, string albumArtist = null)
        {
            var info = new FileNameInfo();
            string name = Path.GetFileNameWithoutExtension(fileName ?? "").Trim();
            if (name.Length == 0)
                return info;

            // "1-01 Title" / "1.01 Title": disc separated from track
            Match match = Regex.Match(name, @"^(\d{1,2})[-.](\d{2,3})(?:\s*[-_.]\s*|\s+)(.*)$");
            if (match.Success)
            {
                info.Disc = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                info.Track = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                name = match.Groups[3].Value;
            }
            else
            {
                match = Regex.Match(name, @"^(\d{1,4})(?:\s*[-_.]\s*|\s+|$)(.*)$");
                if (match.Success)
                {
                    string digits = match.Groups[1].Value;
                    if (digits.Length == 4)
                    {
                        // "0101" = disc 01, track 01
                        info.Disc = int.Parse(digits.Substring(0, 2), CultureInfo.InvariantCulture);
                        info.Track = int.Parse(digits.Substring(2), CultureInfo.InvariantCulture);
                    }
                    else if (digits.Length == 3 && discCount > 1)
                    {
                        // "101" on a multi-disc set = disc 1, track 01
                        info.Disc = int.Parse(digits.Substring(0, 1), CultureInfo.InvariantCulture);
                        info.Track = int.Parse(digits.Substring(1), CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        info.Track = int.Parse(digits, CultureInfo.InvariantCulture);
                    }
                    name = match.Groups[2].Value;
                }
            }

            name = name.Trim(' ', '-', '_', '.');

            // "Artist - Title" / "artist-title": split off a leading artist
            string[] split = name.Contains(" - ") ? name.Split(new[] { " - " }, 2, StringSplitOptions.None) : null;
            if (split == null && !name.Contains(" ") && name.Count(c => c == '-') == 1)
                split = name.Split('-');
            if (split != null && split.Length == 2 && split[0].Trim().Length > 0 && split[1].Trim().Length > 0)
            {
                string artist = Tidy(split[0]);
                if (albumArtist == null || string.Equals(Normalize(artist), Normalize(albumArtist), StringComparison.OrdinalIgnoreCase) || name.Contains(" - "))
                {
                    info.Artist = artist;
                    name = split[1];
                }
            }

            info.Title = Tidy(name);
            return info;
        }

        /// <summary>Underscores to spaces (when the name has no spaces), collapse whitespace.</summary>
        private static string Tidy(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            string result = text.Contains(" ") ? text : text.Replace('_', ' ');
            result = Regex.Replace(result, @"\s+", " ").Trim();
            return result.Length == 0 ? null : result;
        }

        private static string Normalize(string text)
        {
            return Regex.Replace(text ?? "", @"[\s_\-.]", "");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CDTag.Model.Profile;

namespace CDTag.Model.Tag
{
    /// <summary>
    /// Fills a CD-Tag 2.x NFO template (DOS code page 437 text with #Tokens) for an album.
    /// Lines containing track tokens (#N, #Song, #Time, ...) are repeated per track; a #Rnotes line is
    /// repeated per wrapped line of the release notes. Replacements keep later columns aligned.
    /// </summary>
    public class NfoGenerator
    {
        private const int LineWidth = 78;

        private static readonly string[] Tokens = new[]
        {
            "#HH:MM:SS", "#H:MM:SS", "#MM:SS", "#M:SS", "#CleanExplicit", "#RippingTool", "#WhiteLabel", "#ReleaseNC", "#Release",
            "#Language", "#Supplier", "#Internal", "#Encoder", "#Catnum", "#Rnotes", "#Ripper", "#Folder", "#Source", "#Artist",
            "#MBmegs", "#MBMBR", "#MBMB", "#SizeMB", "#Discs", "#Album", "#Genre", "#Rdate", "#Sdate", "#Label", "#LDate", "#Comment",
            "#LtdEd", "#Size", "#Mode", "#Song", "#Time", "#Type", "#Year", "#Disc", "#KHz", "#OST", "#url", "#Hh", "#Hz", "#Br",
            "#MB", "#TT", "#N.", "#N)", "#N-", "#N", "#0", "#H", "#M", "#S"
        }.OrderByDescending(t => t.Length).ToArray();

        private static readonly string[] TrackTokens = { "#N", "#0", "#Song", "#Time" };

        private readonly UserProfile _profile;
        private readonly Album _album;
        private readonly ReleaseInfo _release;
        private readonly AlbumNamer _namer;
        private readonly List<AlbumTrack> _tracks;

        public NfoGenerator(UserProfile profile, Album album, ReleaseInfo release)
        {
            if (profile == null)
                throw new ArgumentNullException("profile");
            if (album == null)
                throw new ArgumentNullException("album");

            _profile = profile;
            _album = album;
            _release = release ?? new ReleaseInfo();
            _namer = new AlbumNamer(profile, album, _release);
            _tracks = album.Tracks
                .OrderBy(t => Number(t.DiscNumber, 1))
                .ThenBy(t => Number(t.TrackNumber, 0))
                .ToList();
        }

        /// <summary>Reads a template (or NFO) written in the DOS code page.</summary>
        public static string ReadTemplate(string path)
        {
            return File.ReadAllText(path, LegacyProfile.DosEncoding);
        }

        /// <summary>Writes an NFO in the DOS code page, as NFO viewers expect.</summary>
        public static void WriteNfo(string path, string text)
        {
            File.WriteAllText(path, text, LegacyProfile.DosEncoding);
        }

        /// <summary>Full path of the profile's NFO template (relative paths are in the profile folder), or null.</summary>
        public static string ResolveTemplatePath(UserProfile profile, string profileDirectory)
        {
            string template = profile != null && profile.NFOOptions != null ? profile.NFOOptions.TemplatePath : null;
            if (string.IsNullOrWhiteSpace(template))
                return null;
            string path = Path.IsPathRooted(template) ? template : Path.Combine(profileDirectory ?? "", template);
            return File.Exists(path) ? path : null;
        }

        public string Generate(string template)
        {
            if (template == null)
                throw new ArgumentNullException("template");

            var output = new List<string>();
            foreach (string line in template.Replace("\r\n", "\n").Split('\n'))
            {
                if (ContainsToken(line, "#Rnotes"))
                    output.AddRange(NotesLines(line));
                else if (TrackTokens.Any(t => ContainsToken(line, t)))
                    output.AddRange(TrackLines(line));
                else
                    output.Add(Replace(line, null));
            }
            return string.Join("\r\n", output);
        }

        // ------------------------------------------------------------------ track list

        private IEnumerable<string> TrackLines(string line)
        {
            int discs = _namer.Discs;
            string header = discs > 1 ? _profile.NFOOptions.MultiCDHeader : _profile.NFOOptions.SingleCDHeader;
            int currentDisc = -1;

            foreach (AlbumTrack track in _tracks)
            {
                int disc = Number(track.DiscNumber, 1);
                if (disc != currentDisc)
                {
                    currentDisc = disc;
                    if (!string.IsNullOrEmpty(header))
                    {
                        string text = header.Replace("#Discs", discs.ToString(CultureInfo.InvariantCulture))
                                            .Replace("#Disc", disc.ToString(CultureInfo.InvariantCulture));
                        foreach (string headerLine in SplitLines(text))
                            yield return headerLine;
                    }
                }
                yield return Replace(line, track);
            }

            if (!string.IsNullOrEmpty(_profile.NFOOptions.TracklistFooter))
            {
                foreach (string footerLine in SplitLines(_profile.NFOOptions.TracklistFooter))
                    yield return footerLine;
            }
        }

        // ------------------------------------------------------------------ release notes

        private IEnumerable<string> NotesLines(string line)
        {
            string notes = (_release.Notes ?? "").Trim();
            if (notes.Length == 0)
            {
                yield return Replace(line, null);
                yield break;
            }

            int column = IndexOfToken(line, "#Rnotes");
            string prefix = line.Substring(0, column);
            string rest = line.Substring(column + "#Rnotes".Length);
            string trimmed = rest.TrimEnd();
            int suffixStart = trimmed.Length;
            while (suffixStart > 0 && trimmed[suffixStart - 1] != ' ')
                suffixStart--;
            string suffix = trimmed.Length > 0 && suffixStart > 0 ? rest.Substring(suffixStart) : "";
            int width = suffix.Length > 0 ? "#Rnotes".Length + suffixStart : Math.Max(30, LineWidth - column);

            if (!string.IsNullOrEmpty(_profile.NFOOptions.ReleaseNotesHeader))
                foreach (string headerLine in SplitLines(_profile.NFOOptions.ReleaseNotesHeader))
                    yield return headerLine;

            foreach (string wrapped in Wrap(notes, width))
                yield return prefix + (suffix.Length > 0 ? wrapped.PadRight(width) + suffix : wrapped);

            if (!string.IsNullOrEmpty(_profile.NFOOptions.ReleaseNotesFooter))
                foreach (string footerLine in SplitLines(_profile.NFOOptions.ReleaseNotesFooter))
                    yield return footerLine;
        }

        private static IEnumerable<string> Wrap(string text, int width)
        {
            foreach (string paragraph in SplitLines(text))
            {
                string remaining = paragraph.TrimEnd();
                if (remaining.Length == 0)
                {
                    yield return "";
                    continue;
                }
                while (remaining.Length > width)
                {
                    int cut = remaining.LastIndexOf(' ', width);
                    if (cut <= 0)
                        cut = width;
                    yield return remaining.Substring(0, cut).TrimEnd();
                    remaining = remaining.Substring(cut).TrimStart();
                }
                yield return remaining;
            }
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Split('\n');
        }

        // ------------------------------------------------------------------ token replacement

        private string Replace(string line, AlbumTrack track)
        {
            var sb = new StringBuilder(line.Length + 16);
            int i = 0;
            while (i < line.Length)
            {
                string token = line[i] == '#' ? MatchToken(line, i) : null;
                if (token == null)
                {
                    sb.Append(line[i]);
                    i++;
                    continue;
                }

                string value = Value(token, track, sb.Length) ?? "";
                int after = i + token.Length;
                int spaces = 0;
                while (after + spaces < line.Length && line[after + spaces] == ' ')
                    spaces++;
                bool alignmentGap = spaces >= 2 && after + spaces < line.Length;

                sb.Append(value);
                if (alignmentGap)
                {
                    // A column gap in the template: the next field keeps its original column (at least one space).
                    int column = after + spaces;
                    sb.Append(' ', Math.Max(1, column - sb.Length));
                    i = column;
                }
                else
                {
                    i = after;
                }
            }
            return sb.ToString();
        }

        private static string MatchToken(string line, int index)
        {
            foreach (string token in Tokens)
            {
                if (index + token.Length <= line.Length && string.Compare(line, index, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    // "#N" must not swallow the start of another word (e.g. "#Name")
                    int end = index + token.Length;
                    if (char.IsLetter(token[token.Length - 1]) && end < line.Length && char.IsLetter(line[end]) && token.Length <= 2)
                        continue;
                    return token;
                }
            }
            return null;
        }

        private static bool ContainsToken(string line, string token)
        {
            return IndexOfToken(line, token) >= 0;
        }

        private static int IndexOfToken(string line, string token)
        {
            for (int i = line.IndexOf('#'); i >= 0; i = line.IndexOf('#', i + 1))
            {
                string match = MatchToken(line, i);
                if (match != null && (match.Equals(token, StringComparison.OrdinalIgnoreCase) ||
                                      (token == "#N" && match.StartsWith("#N", StringComparison.OrdinalIgnoreCase))))
                    return i;
            }
            return -1;
        }

        private string Value(string token, AlbumTrack track, int column)
        {
            string decimalSeparator = string.IsNullOrEmpty(_profile.NFOOptions.DecimalCharacter) ? "." : _profile.NFOOptions.DecimalCharacter;
            int totalSeconds = (int)Math.Round(_tracks.Sum(t => t.TotalSeconds));
            AlbumTrack first = _tracks.FirstOrDefault();
            double megabytes = _tracks.Sum(t => t.FileSize) / 1048576.0;
            string size = megabytes.ToString("0.00", CultureInfo.InvariantCulture).Replace(".", decimalSeparator);

            switch (token.ToUpperInvariant())
            {
                // per track
                case "#N": return TrackNumber(track, "");
                case "#N.": return TrackNumber(track, ".");
                case "#N)": return TrackNumber(track, ")");
                case "#N-": return TrackNumber(track, "-");
                case "#0": return track == null ? "" : Number(track.TrackNumber, 0).ToString("00", CultureInfo.InvariantCulture);
                case "#SONG":
                    if (track == null)
                        return "";
                    return _album.IsVariousArtists && !string.IsNullOrWhiteSpace(track.Artist) ? track.Artist + " - " + track.Title : track.Title;
                case "#TIME": return track == null ? "" : MinutesSeconds((int)Math.Round(track.TotalSeconds));
                case "#DISC": return (track != null ? Number(track.DiscNumber, 1) : 1).ToString(CultureInfo.InvariantCulture);
                case "#DISCS": return _namer.Discs.ToString(CultureInfo.InvariantCulture);

                // album
                case "#ARTIST": return string.IsNullOrWhiteSpace(_album.Artist) && _album.IsVariousArtists ? "Various Artists" : _album.Artist;
                case "#ALBUM": return _album.AlbumTitle;
                case "#GENRE": return _album.Genre;
                case "#YEAR": return _namer.Year;
                case "#FOLDER": return _namer.GetFolderName();
                case "#TT": return _tracks.Count.ToString(CultureInfo.InvariantCulture);
                case "#RELEASE":
                    string release = ReleaseTitle();
                    return new string(' ', Math.Max(0, (LineWidth - release.Length) / 2 - column)) + release;
                case "#RELEASENC": return ReleaseTitle();

                // audio
                case "#BR":
                    if (!_profile.NFOOptions.UseAverageBitrate)
                        return "VBR";
                    decimal bitrate = AlbumNamer.AverageBitrate(_album);
                    return bitrate > 0 ? Math.Round(bitrate).ToString(CultureInfo.InvariantCulture) : "";
                case "#HZ": return first != null && first.Frequency > 0 ? first.Frequency.ToString(CultureInfo.InvariantCulture) : "";
                case "#KHZ":
                    return first != null && first.Frequency > 0
                        ? (first.Frequency / 1000.0).ToString("0.#", CultureInfo.InvariantCulture).Replace(".", decimalSeparator)
                        : "";
                case "#MODE": return first != null ? first.ChannelMode : "";
                case "#SIZE":
                case "#MB": return size;
                case "#SIZEMB":
                case "#MBMB":
                case "#MBMBR": return size + "MB";
                case "#MBMEGS": return size + " megs";

                // play time
                case "#MM:SS": return MinutesSeconds(totalSeconds);
                case "#M:SS": return MinutesSeconds(totalSeconds);
                case "#H:MM:SS": return string.Format("{0}:{1:00}:{2:00}", totalSeconds / 3600, totalSeconds / 60 % 60, totalSeconds % 60);
                case "#HH:MM:SS": return string.Format("{0:00}:{1:00}:{2:00}", totalSeconds / 3600, totalSeconds / 60 % 60, totalSeconds % 60);
                case "#H": return totalSeconds >= 3600 ? (totalSeconds / 3600) + " " : "";
                case "#HH": return totalSeconds >= 3600 ? (totalSeconds / 3600) + "h " : "";
                case "#M": return (totalSeconds / 60 % 60).ToString(CultureInfo.InvariantCulture);
                case "#S": return (totalSeconds % 60).ToString("00", CultureInfo.InvariantCulture);

                // release information
                case "#LABEL": return _release.Label;
                case "#CATNUM": return _release.CatalogNumber;
                case "#RDATE": return FormatDate(_release.RipDate);
                case "#SDATE": return FormatDate(_release.RetailDate);
                case "#LDATE": return _release.LiveDate;
                case "#URL": return _release.Url;
                case "#COMMENT": return _release.Comment;
                case "#RNOTES": return _release.Notes;
                case "#RIPPER": return _release.Ripper;
                case "#SUPPLIER": return _release.Supplier;
                case "#ENCODER": return _release.Encoder;
                case "#RIPPINGTOOL": return _release.RippingTool;
                case "#TYPE": return Nfo(_release.ReleaseType);
                case "#SOURCE": return Nfo(_release.Source);
                case "#LANGUAGE": return Nfo(_release.Language);
                case "#CLEANEXPLICIT": return Nfo(_release.CleanExplicit);
                case "#WHITELABEL": return Nfo(_release.WhiteLabel) ?? "No";
                case "#LTDED": return Nfo(_release.LimitedEdition) ?? "No";
                case "#INTERNAL": return _release.IsInternal ? "Yes" : "No";
                case "#OST": return _release.IsOst ? "Yes" : "No";
            }
            return "";
        }

        private string ReleaseTitle()
        {
            string artist = string.IsNullOrWhiteSpace(_album.Artist) && _album.IsVariousArtists ? "Various Artists" : _album.Artist;
            return string.Format("{0} - {1}", artist, _album.AlbumTitle);
        }

        private string TrackNumber(AlbumTrack track, string suffix)
        {
            if (track == null)
                return "";
            int width = _tracks.Select(t => Number(t.TrackNumber, 0)).DefaultIfEmpty(0).Max().ToString(CultureInfo.InvariantCulture).Length;
            return Number(track.TrackNumber, 0).ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(1, width)) + suffix;
        }

        private static string MinutesSeconds(int seconds)
        {
            return string.Format("{0}:{1:00}", seconds / 60, seconds % 60);
        }

        /// <summary>Formats yyyy-MM-dd using the profile's 2.x date format (e.g. MM-DD-YYYY); unknown parts stay 00.</summary>
        private string FormatDate(string date)
        {
            if (string.IsNullOrWhiteSpace(date))
                return "";
            Match match = Regex.Match(date.Trim(), @"^(\d{4})-(\d{1,2})-(\d{1,2})$");
            string format = _profile.ReleaseInfo != null ? _profile.ReleaseInfo.NfoDateFormat : null;
            if (!match.Success || string.IsNullOrWhiteSpace(format))
                return date.Trim();
            return format.ToUpperInvariant()
                .Replace("YYYY", match.Groups[1].Value)
                .Replace("MM", match.Groups[2].Value.PadLeft(2, '0'))
                .Replace("DD", match.Groups[3].Value.PadLeft(2, '0'));
        }

        private static string Nfo(ReleaseListItem item)
        {
            return item != null ? item.Nfo : null;
        }

        private static int Number(string text, int fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
                return fallback;
            Match match = Regex.Match(text, @"\d+");
            int number;
            return match.Success && int.TryParse(match.Value, out number) ? number : fallback;
        }
    }
}

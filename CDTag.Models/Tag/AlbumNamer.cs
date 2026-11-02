using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CDTag.Model.Profile;

namespace CDTag.Model.Tag
{
    /// <summary>The names a profile can produce for an album.</summary>
    public enum NameKind
    {
        Directory,
        AudioFile,
        CUE,
        Playlist,
        Checksum,
        NFO,
        Images,
        EACLog
    }

    /// <summary>
    /// Builds folder and file names from a profile's naming formats, following CD-Tag 2.x:
    /// tokens (&lt;Artist&gt;, &lt;Album&gt;, &lt;Year&gt;, &lt;Track&gt;, &lt;Disc&gt;, &lt;ExtInfo&gt;, ...),
    /// case regions (&lt;SC&gt; custom/smart case, &lt;LC&gt; lower, &lt;UC&gt; upper), the profile's
    /// find/replace list, character restrictions and length limits (&lt;Suffix&gt; protects what follows).
    /// </summary>
    public class AlbumNamer
    {
        private const char SuffixMark = '\u0001';
        private static readonly Regex TokenPattern = new Regex(@"<(/?)([A-Za-z]+[0-9]?)>", RegexOptions.CultureInvariant);
        private static readonly string Punctuation = "'.,!?()&";

        private readonly UserProfile _profile;
        private readonly Album _album;
        private readonly ReleaseInfo _release;
        private readonly NamingRules _rules;

        public AlbumNamer(UserProfile profile, Album album, ReleaseInfo release)
        {
            if (profile == null)
                throw new ArgumentNullException("profile");
            if (album == null)
                throw new ArgumentNullException("album");

            _profile = profile;
            _album = album;
            _release = release ?? new ReleaseInfo();
            _rules = profile.NamingRules ?? new NamingRules
            {
                Replacements = new ReplacePair[0],
                WordSeparators = new[] { " ", "_" },
                FieldSeparators = new[] { "-" }
            };
        }

        /// <summary>Number of discs (highest disc number among the tracks, at least 1).</summary>
        public int Discs
        {
            get { return Math.Max(1, _album.Tracks.Select(t => ParseNumber(t.DiscNumber)).DefaultIfEmpty(1).Max()); }
        }

        /// <summary>The naming formats used for this album (single/multi CD or vinyl, single artist or VA).</summary>
        public NamingFormat Format
        {
            get
            {
                FileNaming naming = _profile.FileNaming;
                NamingFormatGroup group = IsVinyl ? naming.Vinyl : (Discs > 1 ? naming.MultiCD : naming.SingleCD);
                if (group == null)
                    return null;
                return _album.IsVariousArtists ? group.VariousArtists : group.SingleArtist;
            }
        }

        private bool IsVinyl
        {
            get
            {
                string source = _release.Source != null ? (_release.Source.Nfo ?? "") : "";
                return Regex.IsMatch(source, @"VINYL|\bLP\b|VLS|INCH", RegexOptions.IgnoreCase);
            }
        }

        /// <summary>The album year: retail date year, else the year from the tags.</summary>
        public string Year
        {
            get
            {
                string year = _release.RetailYear;
                if (year != null)
                    return year;
                string tagged = (_album.ReleaseDate ?? "").Trim();
                return tagged.Length >= 4 ? tagged.Substring(0, 4) : tagged;
            }
        }

        /// <summary>New folder name, or null when the profile has no directory format.</summary>
        public string GetFolderName()
        {
            NamingFormat format = Format;
            return format == null ? null : Build(format.Directory, null, null, _profile.FileNaming.DirectoryCharacterLimit, null);
        }

        /// <summary>New file name (with the original extension) for a track, or null.</summary>
        public string GetTrackFileName(AlbumTrack track)
        {
            NamingFormat format = Format;
            if (format == null || track == null)
                return null;
            string extension = System.IO.Path.GetExtension(track.OriginalFileName ?? "").ToLowerInvariant();
            return Build(format.AudioFile, track, null, _profile.FileNaming.FileCharacterLimit, extension);
        }

        /// <summary>Name for a cue sheet, playlist, checksum file, NFO, image or log (with extension), or null.</summary>
        public string GetFileName(NameKind kind, string imageText = null, string extension = null)
        {
            NamingFormat format = Format;
            if (format == null)
                return null;

            string pattern;
            string defaultExtension;
            switch (kind)
            {
                case NameKind.Directory: return GetFolderName();
                case NameKind.CUE: pattern = format.CUE; defaultExtension = ".cue"; break;
                case NameKind.Playlist: pattern = format.Playlist; defaultExtension = ".m3u"; break;
                case NameKind.Checksum: pattern = format.Checksum; defaultExtension = ".sfv"; break;
                case NameKind.NFO: pattern = format.NFO; defaultExtension = ".nfo"; break;
                case NameKind.Images: pattern = format.Images; defaultExtension = ".jpg"; break;
                case NameKind.EACLog: pattern = format.EACLog; defaultExtension = ".log"; break;
                default: throw new ArgumentOutOfRangeException("kind");
            }
            return Build(pattern, null, imageText, _profile.FileNaming.FileCharacterLimit, extension ?? defaultExtension);
        }

        private string Build(string pattern, AlbumTrack track, string imageText, int? limit, string extension)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return null;

            string name = Expand(pattern, track, imageText);
            name = Clean(name);

            int suffixAt = name.IndexOf(SuffixMark);
            string head = suffixAt >= 0 ? name.Substring(0, suffixAt) : name;
            string tail = suffixAt >= 0 ? name.Substring(suffixAt + 1).Replace(SuffixMark.ToString(), "") : "";

            int max = (limit ?? 0) > 0 ? limit.Value - (extension ?? "").Length : int.MaxValue;
            if (head.Length + tail.Length > max)
                head = head.Substring(0, Math.Max(0, max - tail.Length)).TrimEnd('_', '-', ' ', '.');

            string result = (head + tail).Trim();
            return result.Length == 0 ? null : result + (extension ?? "");
        }

        // ------------------------------------------------------------------ expansion

        /// <summary>Expands tokens and applies case regions (exposed for tests).</summary>
        public string Expand(string pattern, AlbumTrack track, string imageText)
        {
            var output = new StringBuilder();
            var region = new StringBuilder();
            string mode = null;
            int position = 0;

            foreach (Match match in TokenPattern.Matches(pattern))
            {
                region.Append(pattern, position, match.Index - position);
                position = match.Index + match.Length;

                bool closing = match.Groups[1].Value == "/";
                string name = match.Groups[2].Value;
                string upper = name.ToUpperInvariant();

                if (upper == "SC" || upper == "LC" || upper == "UC")
                {
                    output.Append(ApplyCase(region.ToString(), mode));
                    region.Clear();
                    mode = closing ? null : upper;
                    continue;
                }

                if (upper == "SUFFIX")
                {
                    region.Append(SuffixMark);
                    continue;
                }

                region.Append(CleanValue(Value(name, track, imageText)));
            }

            region.Append(pattern, position, pattern.Length - position);
            output.Append(ApplyCase(region.ToString(), mode));
            return output.ToString();
        }

        private string Value(string token, AlbumTrack track, string imageText)
        {
            switch (token.ToUpperInvariant())
            {
                case "ARTIST":
                    if (track != null && _album.IsVariousArtists)
                        return track.Artist;
                    return string.IsNullOrWhiteSpace(_album.Artist) ? (_album.IsVariousArtists ? "VA" : null) : _album.Artist;
                case "ALBUM": return _album.AlbumTitle;
                case "YEAR": return Year;
                case "GENRE": return _album.Genre;
                case "SONG":
                case "TITLE": return track != null ? track.Title : null;
                case "TRACK": return track != null ? FormatTrack(track.TrackNumber) : null;
                case "TRACKS": return _album.Tracks.Count.ToString(CultureInfo.InvariantCulture);
                case "DISC": return track != null ? FormatDisc(track.DiscNumber) : null;
                case "DISCS": return Discs.ToString(CultureInfo.InvariantCulture);
                case "PROFILE": return _profile.ProfileName;
                case "IMAGETEXT": return imageText;
                case "BITRATE":
                    decimal bitrate = track != null ? track.Bitrate : AverageBitrate(_album);
                    return bitrate > 0 ? Math.Round(bitrate).ToString(CultureInfo.InvariantCulture) : null;
                case "SIDE": return null;
            }

            if (token.StartsWith("ExtInfo", StringComparison.OrdinalIgnoreCase))
            {
                string number = token.Substring("ExtInfo".Length);
                int index = number.Length == 0 ? 0 : ParseNumber(number) - 1;
                return ExtInfo(index);
            }

            return null; // unknown token: dropped
        }

        private string ExtInfo(int index)
        {
            if (_profile.ExtInfo == null || index < 0 || index >= _profile.ExtInfo.Length)
                return null;

            ExtInfoDefinition definition = _profile.ExtInfo[index];
            if (definition == null || definition.Items == null)
                return null;

            var values = new List<string>();
            foreach (ExtInfoItem item in definition.Items)
            {
                string value = ExtInfoFieldValue(item.Field);
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                values.Add((string.IsNullOrEmpty(item.Format) ? "<Value>" : item.Format).Replace("<Value>", value.Trim()));
            }

            if (values.Count == 0)
                return null;
            return (definition.Begin ?? "") + string.Join(definition.FieldSeparator ?? "", values) + (definition.End ?? "");
        }

        private string ExtInfoFieldValue(string field)
        {
            string key = Regex.Replace((field ?? "").ToLowerInvariant(), "[^a-z0-9#]", "");
            switch (key)
            {
                case "releasetype":
                case "type": return Dir(_release.ReleaseType);
                case "source": return Dir(_release.Source);
                case "language2letter":
                case "language": return Dir(_release.Language);
                case "limitededition": return Dir(_release.LimitedEdition);
                case "whitelabel": return Dir(_release.WhiteLabel);
                case "cleanexplicit": return Dir(_release.CleanExplicit);
                case "recordlabel":
                case "label": return _release.Label;
                case "catalog#":
                case "catalognumber":
                case "catalog": return _release.CatalogNumber;
                case "livedate": return _release.LiveDate;
                case "internal": return _release.IsInternal ? "INT" : null;
                case "ost": return _release.IsOst ? "OST" : null;
                default: return null;
            }
        }

        private static string Dir(ReleaseListItem item)
        {
            return item != null ? item.Dir : null;
        }

        private string FormatTrack(string trackNumber)
        {
            int number = ParseNumber(trackNumber);
            if (number <= 0)
                return null;
            int highest = _album.Tracks.Select(t => ParseNumber(t.TrackNumber)).DefaultIfEmpty(0).Max();
            int width = Math.Max(2, highest.ToString(CultureInfo.InvariantCulture).Length);
            return number.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        }

        private string FormatDisc(string discNumber)
        {
            int number = Math.Max(1, ParseNumber(discNumber));
            return number.ToString(CultureInfo.InvariantCulture).PadLeft(Discs.ToString(CultureInfo.InvariantCulture).Length, '0');
        }

        public static decimal AverageBitrate(Album album)
        {
            decimal seconds = album.Tracks.Sum(t => t.TotalSeconds);
            if (seconds <= 0)
                return album.Tracks.Select(t => t.Bitrate).DefaultIfEmpty(0).Average();
            return album.Tracks.Sum(t => t.Bitrate * t.TotalSeconds) / seconds;
        }

        private static int ParseNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;
            Match match = Regex.Match(text, @"\d+");
            int number;
            return match.Success && int.TryParse(match.Value, out number) ? number : 0;
        }

        // ------------------------------------------------------------------ cleaning

        /// <summary>Field values: transliterate when asked, then the find/replace list.</summary>
        private string CleanValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            if (_profile.FileNaming.UseLatinCharactersOnly || _profile.FileNaming.UseStandardCharactersOnly)
                value = ToLatin(value);
            return ApplyReplacements(value.Trim());
        }

        /// <summary>The whole name: replacements again (literal parts), removals, allowed characters, tidy-up.</summary>
        private string Clean(string name)
        {
            name = ApplyReplacements(name);

            if (_rules.RemoveStrings != null)
            {
                foreach (string remove in _rules.RemoveStrings.Where(r => !string.IsNullOrEmpty(r)))
                    name = name.Replace(remove, "");
            }

            if (_profile.FileNaming.UseUnderscores)
                name = name.Replace(' ', '_');

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c == SuffixMark)
                {
                    sb.Append(c);
                    continue;
                }
                if ("\\/:*?\"<>|".IndexOf(c) >= 0 || char.IsControl(c))
                    continue;
                if (_profile.FileNaming.UseStandardCharactersOnly && !IsStandard(c))
                    continue;
                sb.Append(c);
            }
            name = sb.ToString();

            // Collapse runs of the same separator ("__", "--") and trim them from the ends.
            name = Regex.Replace(name, @"([_\-. ])\1+", "$1");
            name = name.Trim('_', '-', ' ', '.');
            return name;
        }

        private string ApplyReplacements(string text)
        {
            if (_rules.Replacements == null || _rules.Replacements.Length == 0 || string.IsNullOrEmpty(text))
                return text;

            // Pairs that replace spaces (" " -> "_") run after the others, so word-based pairs such as
            // "Volume 2" -> "Vol 2" still match; then the others run again to tidy up ("__" -> "_").
            ReplacePair[] wordPairs = _rules.Replacements.Where(p => !string.IsNullOrEmpty(p.Find) && p.Find.Trim().Length > 0).ToArray();
            ReplacePair[] spacePairs = _rules.Replacements.Where(p => !string.IsNullOrEmpty(p.Find) && p.Find.Trim().Length == 0).ToArray();

            text = ReplaceAll(text, wordPairs);
            text = ReplaceAll(text, spacePairs);
            for (int pass = 0; pass < 3; pass++)
            {
                string before = text;
                text = ReplaceAll(text, wordPairs);
                if (text == before)
                    break;
            }
            return text;
        }

        private static string ReplaceAll(string text, IEnumerable<ReplacePair> pairs)
        {
            foreach (ReplacePair pair in pairs)
                text = text.Replace(pair.Find, pair.Replace ?? "");
            return text;
        }

        private static bool IsStandard(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '(' || c == ')' || c == ' ';
        }

        /// <summary>Removes accents and converts common letters to A-Z (é -> e, ß -> ss, æ -> ae, ø -> o).</summary>
        public static string ToLatin(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (char c in value.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;
                switch (c)
                {
                    case 'ß': sb.Append("ss"); break;
                    case 'æ': sb.Append("ae"); break;
                    case 'Æ': sb.Append("AE"); break;
                    case 'œ': sb.Append("oe"); break;
                    case 'Œ': sb.Append("OE"); break;
                    case 'ø': sb.Append('o'); break;
                    case 'Ø': sb.Append('O'); break;
                    case 'đ': sb.Append('d'); break;
                    case 'Đ': sb.Append('D'); break;
                    case 'ł': sb.Append('l'); break;
                    case 'Ł': sb.Append('L'); break;
                    case 'þ': sb.Append("th"); break;
                    case 'Þ': sb.Append("Th"); break;
                    case '‘':
                    case '’': sb.Append('\''); break;
                    case '“':
                    case '”': sb.Append('"'); break;
                    case '–':
                    case '—': sb.Append('-'); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // ------------------------------------------------------------------ casing

        private string ApplyCase(string text, string mode)
        {
            switch (mode)
            {
                case "LC": return text.ToLowerInvariant();
                case "UC": return text.ToUpperInvariant();
                case "SC": return SmartCase(text);
                default: return text;
            }
        }

        /// <summary>
        /// CD-Tag 2.x custom case: capitalise each word; keep lower-case-list words lower unless they start the text
        /// or follow a field separator; optionally leave ALL-CAPS words and words with non-letters unchanged.
        /// </summary>
        public string SmartCase(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var wordSeparators = new HashSet<char>((_rules.WordSeparators ?? new string[0]).Where(s => s.Length == 1).Select(s => s[0]));
            var fieldSeparators = new HashSet<char>((_rules.FieldSeparators ?? new string[0]).Where(s => s.Length == 1).Select(s => s[0]));
            wordSeparators.Add(' ');
            var lowercase = new HashSet<string>(_rules.LowercaseWords ?? new string[0], StringComparer.OrdinalIgnoreCase);

            var result = new StringBuilder(text.Length);
            var word = new StringBuilder();
            bool firstWord = true;
            bool afterFieldSeparator = true;

            Action flush = () =>
            {
                if (word.Length == 0)
                    return;
                result.Append(CaseWord(word.ToString(), firstWord || afterFieldSeparator, lowercase));
                word.Clear();
                firstWord = false;
                afterFieldSeparator = false;
            };

            foreach (char c in text)
            {
                if (c == SuffixMark || wordSeparators.Contains(c) || fieldSeparators.Contains(c))
                {
                    flush();
                    if (fieldSeparators.Contains(c))
                        afterFieldSeparator = true;
                    result.Append(c);
                }
                else
                {
                    word.Append(c);
                }
            }
            flush();
            return result.ToString();
        }

        private string CaseWord(string word, bool startsField, HashSet<string> lowercase)
        {
            string letters = new string(word.Where(char.IsLetter).ToArray());
            if (letters.Length == 0)
                return word;

            if (_profile.FileNaming.DontChangeCaseWordsWithAllCaps && letters.Length > 1 && letters.All(char.IsUpper))
                return word;

            if (_profile.FileNaming.DontChangeCaseWordsWithNonLetter && word.Any(c => !char.IsLetter(c) && Punctuation.IndexOf(c) < 0))
                return word;

            if (!startsField && lowercase.Contains(word.Trim(Punctuation.ToCharArray())))
                return word.ToLowerInvariant();

            var chars = word.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsLetter(chars[i]))
                {
                    chars[i] = char.ToUpperInvariant(chars[i]);
                    break;
                }
            }
            return new string(chars);
        }
    }
}

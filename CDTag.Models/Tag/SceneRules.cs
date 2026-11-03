using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CDTag.Model.Lookup;

namespace CDTag.Model.Tag
{
    public enum SceneRuleSet
    {
        None,
        /// <summary>Official FLAC Standard Rules v3.0 (2016).</summary>
        Flac2016,
        /// <summary>Official MP3 Release Rules 4.1 (2021).</summary>
        Mp3_2021,
        /// <summary>WEB FLAC rules 1.0 (2023).</summary>
        WebFlac2023
    }

    /// <summary>What a release will look like after Finish, for the rules check.</summary>
    public class SceneRelease
    {
        public SceneRelease()
        {
            Files = new List<string>();
        }

        /// <summary>Release (folder) name.</summary>
        public string Name { get; set; }
        /// <summary>Every file after Finish, relative to the release folder ("CD1\101-a-b.flac").</summary>
        public List<string> Files { get; set; }
        public Album Album { get; set; }
        /// <summary>Source from the release page ("CD", "VINYL", "WEB"...).</summary>
        public string Source { get; set; }
        public int Discs { get; set; }
        public string Year { get; set; }
        /// <summary>Shop/service URL from the release page (WEB FLAC 7.2, MP3 13.2).</summary>
        public string Url { get; set; }
    }

    /// <summary>
    /// Checks a release against the scene rules that can be checked from the files (names, tags, audio format,
    /// required files). Every finding names the rule it comes from.
    /// </summary>
    public static class SceneRules
    {
        /// <summary>MP3 rules 15.2.</summary>
        public const int MaxMp3PictureBytes = 2097152;

        private static readonly Regex ValidName = new Regex(@"^[A-Za-z0-9_.\-()]+$", RegexOptions.CultureInvariant);

        private static readonly string[] FlacFileTypes = { ".cue", ".flac", ".jpg", ".log", ".m3u", ".nfo", ".sfv" };
        private static readonly string[] WebFlacFileTypes = { ".flac", ".jpg", ".m3u", ".nfo", ".pdf", ".sfv" };

        /// <summary>FLAC rules 6.5: the allowed GENRE values.</summary>
        private static string[] _flacGenres;

        /// <summary>The allowed FLAC genres: from the downloaded rules when available, else the built-in list.</summary>
        public static string[] FlacGenres
        {
            get { return _flacGenres ?? BuiltInFlacGenres; }
        }

        /// <summary>Replaces the genre list (read from the current FLAC rules by <see cref="SceneRulesUpdater"/>).</summary>
        public static void SetFlacGenres(IEnumerable<string> genres)
        {
            string[] list = (genres ?? Enumerable.Empty<string>()).Where(g => !string.IsNullOrWhiteSpace(g)).ToArray();
            _flacGenres = list.Length > 0 ? list : BuiltInFlacGenres;
        }

        private static readonly string[] BuiltInFlacGenres =
        {
            "A Cappella", "Acid", "Acid Jazz", "Acid Punk", "Acoustic", "AlternRock", "Alternative", "Ambient", "Anime", "Avantgarde",
            "Ballad", "Bass", "Beat", "Bebop", "Big Band", "Black Metal", "Bluegrass", "Blues", "Booty Bass", "BritPop", "Cabaret",
            "Celtic", "Chamber Music", "Chanson", "Chorus", "Christian Gangsta Rap", "Christian Rap", "Christian Rock", "Classic Rock",
            "Classical", "Club", "Club-House", "Comedy", "Contemporary Christian", "Country", "Crossover", "Cult", "Dance", "Dance Hall",
            "Darkwave", "Death Metal", "Disco", "Dream", "Drum & Bass", "Drum Solo", "Duet", "Easy Listening", "Electronic", "Ethnic",
            "Euro-House", "Euro-Techno", "Eurodance", "Fast Fusion", "Folk", "Folk-Rock", "Folklore", "Freestyle", "Funk", "Fusion",
            "Game", "Gangsta Rap", "Goa", "Gospel", "Gothic", "Gothic Rock", "Grunge", "Hard Rock", "Hardcore", "Heavy Metal",
            "Hip-Hop", "House", "Humour", "Indie", "Industrial", "Instrumental", "Instrumental Pop", "Instrumental Rock", "Jazz",
            "Jazz+Funk", "JPop", "Jungle", "Latin", "Lo-Fi", "Meditative", "Merengue", "Metal", "Musical", "National Folk",
            "Native American", "Negerpunk", "New Age", "New Wave", "Noise", "Oldies", "Opera", "Other", "Polka", "Polsk Punk", "Pop",
            "Pop-Folk", "Pop/Funk", "Porn Groove", "Power Ballad", "Pranks", "Primus", "Progressive Rock", "Psychedelic",
            "Psychadelic", "Psychedelic Rock", "Punk", "Punk Rock", "R&B", "Rap", "Rave", "Reggae", "Retro", "Revival", "Rhythmic Soul",
            "Rock", "Rock & Roll", "Salsa", "Samba", "Satire", "Showtunes", "Ska", "Slow Jam", "Slow Rock", "Sonata", "Soul",
            "Sound Clip", "Soundtrack", "Southern Rock", "Space", "Speech", "Swing", "Symphonic Rock", "Symphony", "Synthpop", "Tango",
            "Techno", "Techno-Industrial", "Terror", "Thrash Metal", "Top 40", "Trailer", "Trance", "Tribal", "Trip-Hop", "Vocal",
            "Unknown"
        };

        /// <summary>Common online genres (MusicBrainz, Discogs) and the closest allowed genre.</summary>
        private static readonly Dictionary<string, string> GenreAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Alternative Rock", "AlternRock" }, { "Alt Rock", "AlternRock" }, { "Hip Hop", "Hip-Hop" }, { "Rap Rock", "Rap" },
            { "Indie Rock", "Indie" }, { "Indie Pop", "Indie" }, { "Drum and Bass", "Drum & Bass" }, { "Rhythm and Blues", "R&B" },
            { "Rock and Roll", "Rock & Roll" }, { "Rock N Roll", "Rock & Roll" }, { "Electronica", "Electronic" },
            { "Psychedelic Pop", "Psychedelic" }, { "Experimental", "Avantgarde" }, { "Experimental Rock", "Avantgarde" },
            { "Progressive Metal", "Metal" }, { "Thrash", "Thrash Metal" }, { "Death", "Death Metal" }, { "Synth-Pop", "Synthpop" },
            { "Synth Pop", "Synthpop" }, { "Trip Hop", "Trip-Hop" }, { "Lo-Fi Rock", "Lo-Fi" }, { "Lofi", "Lo-Fi" },
            { "Comedy Rock", "Comedy" }, { "Hardcore Punk", "Hardcore" }, { "Post-Punk", "Punk" }, { "Pop Rock", "Rock" },
            { "Soundtracks", "Soundtrack" }, { "Stage & Screen", "Soundtrack" }, { "Folk Rock", "Folk-Rock" }, { "Country Rock", "Country" }
        };

        /// <summary>The allowed genre for <paramref name="genre"/> ("Alternative Rock" -> "AlternRock"), or null.</summary>
        public static string ToAllowedGenre(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre))
                return null;
            string exact = FlacGenres.FirstOrDefault(g => string.Equals(g, genre.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
            string alias;
            return GenreAliases.TryGetValue(genre.Trim(), out alias) ? alias : null;
        }

        /// <summary>
        /// The rules for the album: FLAC from a WEB source = WEB FLAC rules, other FLAC = FLAC rules,
        /// MP3 = MP3 rules.
        /// </summary>
        public static SceneRuleSet RuleSetFor(Album album, string source = null)
        {
            if (album == null || album.Tracks.Count == 0)
                return SceneRuleSet.None;
            if (album.Tracks.All(t => t.IsFlac))
                return IsWeb(source) ? SceneRuleSet.WebFlac2023 : SceneRuleSet.Flac2016;
            if (album.Tracks.All(t => string.Equals(Path.GetExtension(t.OriginalFileName), ".mp3", StringComparison.OrdinalIgnoreCase)))
                return SceneRuleSet.Mp3_2021;
            return SceneRuleSet.None;
        }

        /// <summary>True for WEB sources ("WEB", "WEB-FLAC").</summary>
        public static bool IsWeb(string source)
        {
            return (source ?? "").Trim().StartsWith("WEB", StringComparison.OrdinalIgnoreCase);
        }

        public static string Describe(SceneRuleSet rules)
        {
            switch (rules)
            {
                case SceneRuleSet.Flac2016: return "FLAC rules v3.0";
                case SceneRuleSet.WebFlac2023: return "WEB FLAC rules 1.0";
                case SceneRuleSet.Mp3_2021: return "MP3 rules 4.1";
                default: return null;
            }
        }

        /// <summary>The bit depth tag WEB FLAC release names need ("16BIT"), from the files; null when unknown.</summary>
        public static string BitDepthTag(Album album)
        {
            if (album == null)
                return null;
            int bits = album.Tracks.Where(t => t.IsFlac).Select(t => t.BitsPerSample).DefaultIfEmpty(0).Max();
            return bits > 0 ? bits.ToString(CultureInfo.InvariantCulture) + "BIT" : null;
        }

        /// <summary>The sample rate tag of the files as WEB FLAC rules 4.10.2 write it (44.1 kHz = "44KHZ"); null when unknown.</summary>
        public static string SampleRateTag(Album album)
        {
            if (album == null)
                return null;
            int rate = album.Tracks.Where(t => t.IsFlac).Select(t => t.Frequency).DefaultIfEmpty(0).Max();
            return rate > 0 ? (rate / 1000).ToString(CultureInfo.InvariantCulture) + "KHZ" : null;
        }

        /// <summary>
        /// The bit depth / sample rate tags a WEB FLAC release name can have (rules 4.10): ##BIT is required, ##KHZ optional.
        /// The files' own values come first.
        /// </summary>
        public static List<string> QualityTags(Album album)
        {
            string bits = BitDepthTag(album);
            string rate = SampleRateTag(album);
            var tags = new List<string>();
            if (bits != null)
            {
                tags.Add(bits);
                if (rate != null && bits != "16BIT")
                    tags.Add(bits + "-" + rate);
            }
            // 16 bit is only 16BIT (no kHz); 24 bit can carry the sample rate.
            tags.Add("16BIT");
            tags.Add("24BIT");
            foreach (string k in new[] { "44KHZ", "48KHZ", "88KHZ", "96KHZ", "176KHZ", "192KHZ" })
                tags.Add("24BIT-" + k);
            return tags.Distinct().ToList();
        }

        /// <summary>BLURAY / HDDVD sources (2BLURAY...): bits and sample rate are kept from the disc (FLAC rules 3.4.2).</summary>
        public static bool IsBluray(string source)
        {
            return Regex.IsMatch((source ?? "").Trim(), @"^\d*(BLURAY|HDDVD)", RegexOptions.IgnoreCase);
        }

        /// <summary>FLAC files from a WEB or Blu-ray source: the Bit/kHz choice applies.</summary>
        public static bool HasBitKhzChoice(Album album, string source)
        {
            return album != null && album.Tracks.Count > 0 && album.Tracks.All(t => t.IsFlac) && (IsWeb(source) || IsBluray(source));
        }

        public static List<string> Check(SceneRuleSet rules, SceneRelease release)
        {
            var issues = new List<string>();
            if (rules == SceneRuleSet.None || release == null)
                return issues;

            bool mp3 = rules == SceneRuleSet.Mp3_2021;
            bool webFlac = rules == SceneRuleSet.WebFlac2023;
            bool flac = !mp3; // both FLAC rule sets
            string prefix = mp3 ? "MP3 " : webFlac ? "WEB FLAC " : "FLAC ";
            // Rule numbers per rule set: FLAC v3.0 / WEB FLAC 1.0 / MP3 4.1
            Func<string, string, string, string> rule = (f, w, m) => prefix + (mp3 ? m : webFlac ? w : f);

            string name = release.Name ?? "";
            string source = (release.Source ?? "").Trim().ToUpperInvariant();
            bool web = IsWeb(source);
            Album album = release.Album;
            int nameLimit = webFlac ? 255 : 128;

            // ---- Release name
            if (!ValidName.IsMatch(name))
                issues.Add(rule("1.5", "1.6", "5.2") + ": the release name has characters other than a-z A-Z 0-9 _ . - ( ).");
            if (flac && name.Length > nameLimit)
                issues.Add(string.Format("{0}: the release name is {1} characters ({2} at most).", rule("1.5", "1.6", ""), name.Length, nameLimit));
            if (flac)
            {
                if (!Regex.IsMatch(name, @"-FLAC-(\d{4}|\d{2,3}[xX]{1,2})-[A-Za-z0-9_]+$"))
                    issues.Add(rule("4.1", "4.1", "") + ": the release name must end with -FLAC-YEAR-GROUP (FLAC in capitals).");
            }
            if (webFlac)
            {
                if (Regex.IsMatch(name, @"-(WEBFLAC|WEB_FLAC)-", RegexOptions.IgnoreCase))
                    issues.Add("WEB FLAC 4.12.1: WEBFLAC and WEB_FLAC are not allowed; use -WEB-FLAC-.");
                if (!Regex.IsMatch(name, @"-WEB-FLAC-"))
                    issues.Add("WEB FLAC 4.4: the source tag must be WEB (in capitals): -WEB-FLAC-.");
                Match bit = Regex.Match(name, @"-(\d{2})BIT(-(\d{2,3})KHZ)?-WEB-FLAC-");
                if (!bit.Success)
                {
                    issues.Add("WEB FLAC 4.10.1: the release name needs the bit depth before the source (-16BIT-WEB-FLAC-).");
                }

                if (name.Contains("_-_"))
                    issues.Add("WEB FLAC 4.12.2: _-_ is not allowed in the release name.");
                if (album != null && album.Tracks.Count == 1 && !Regex.IsMatch(name, @"-(Single|SINGLE|EP)-") && !Regex.IsMatch(name, @"_(Single|EP)-"))
                    issues.Add("WEB FLAC 4.7: a single track release needs the -Single- type tag (albums and EPs with one track are exempt).");
            }
            else if (flac)
            {
                if (!Regex.IsMatch(name, @"-(\d+[xX]?)?(\d+INCH_)?(BLURAY|HDDVD|CDS|CDM|CDEP|MCD|CDREP|CDRS|CDR|CD|DVDA|DVD|VINYL|VLS|MLP|LP|EP|INCH_VINYL|FLEXI)-FLAC-"))
                    issues.Add("FLAC 4.4: the release name needs a source tag in capitals before -FLAC- (CD, 2CD, VINYL...).");
            }
            else
            {
                if (!Regex.IsMatch(name, @"-(\d{4}|\d{3}x|\d{2}xx)-[A-Za-z0-9_]+$"))
                    issues.Add("MP3 3.1: the release name must end with -YEAR-GROUP.");
                if (release.Discs > 1 && !Regex.IsMatch(name, @"-" + release.Discs + @"[A-Z]+-"))
                    issues.Add(string.Format("MP3 3.6: a {0}-disc release needs a multi-disc tag (-{0}CD-).", release.Discs));

                bool webInName = Regex.IsMatch(name, @"-WEB-");
                if (web && !webInName)
                    issues.Add("MP3 13.2.1: a WEB release needs the -WEB- source tag in the release name.");
                if (!web && source.Length > 0 && webInName)
                    issues.Add(string.Format("MP3 13: the release name says WEB but the source is {0} (one source tag only).", source));
                if (web && album != null && album.Tracks.Count == 1 && album.Tracks[0].TotalSeconds <= 25 * 60 && !Regex.IsMatch(name, @"-SINGLE-"))
                    issues.Add("MP3 3.9: a one-track WEB release needs the -SINGLE- tag after the title (unless it is longer than 25 minutes).");
            }
            // ##BIT / ##KHZ in a FLAC release name must match the files (WEB FLAC 4.10; Blu-ray keeps the disc's bits and rate, FLAC 3.4.2).
            Match quality = flac ? Regex.Match(name, @"-(\d{2})BIT(-(\d{2,3})KHZ)?-") : Match.Empty;
            if (quality.Success && quality.Groups[1].Value == "16" && quality.Groups[3].Success)
                issues.Add(string.Format("{0}: 16BIT has no kHz tag; use -16BIT- instead of -16BIT-{1}KHZ-.", rule("3.4.2", "4.10.2", ""), quality.Groups[3].Value));
            if (quality.Success && album != null)
            {
                string actual = BitDepthTag(album);
                if (actual != null && actual != quality.Groups[1].Value + "BIT")
                    issues.Add(string.Format("{0}: the release name says {1}BIT but the files are {2}.", rule("3.4.2", "4.10.1", ""), quality.Groups[1].Value, actual));
                string rate = SampleRateTag(album);
                if (quality.Groups[3].Success && rate != null && rate != quality.Groups[3].Value + "KHZ")
                    issues.Add(string.Format("{0}: the release name says {1}KHZ but the files are {2}.", rule("3.4.2", "4.10.2", ""), quality.Groups[3].Value, rate));
            }
            if (album != null && album.IsVariousArtists && !name.StartsWith("VA-", StringComparison.Ordinal))
                issues.Add(rule("4.2.1", "4.2.1", "3.7") + ": a various artists release name must start with VA-.");
            if (!string.IsNullOrEmpty(release.Year) && !Regex.IsMatch(release.Year, @"^\d{4}$"))
                issues.Add(rule("6.2", "6.2", "3.10") + ": the year must have four digits (" + release.Year + ").");

            // ---- Files
            string[] allowedTypes = webFlac ? WebFlacFileTypes : FlacFileTypes;
            string audioExtension = mp3 ? ".mp3" : ".flac";
            foreach (string file in release.Files)
            {
                string fileName = Path.GetFileName(file);
                string extension = Path.GetExtension(fileName).ToLowerInvariant();
                if (!ValidName.IsMatch(fileName))
                    issues.Add(string.Format("{0}: {1} has characters other than a-z A-Z 0-9 _ . - ( ).", rule("1.5", "1.6", "5.2"), fileName));
                if (flac && fileName.Length > nameLimit)
                    issues.Add(string.Format("{0}: {1} is longer than {2} characters.", rule("1.5", "1.6", ""), fileName, nameLimit));
                if (mp3 && name.Length + 1 + file.Length > 255)
                    issues.Add(string.Format("MP3 5.1: release name + {0} is longer than 255 characters.", fileName));
                if (flac && fileName != fileName.ToLowerInvariant())
                    issues.Add(string.Format("{0}: {1} must be lowercase.", rule("5.2", "5.2", ""), fileName));
                if (flac && !allowedTypes.Contains(extension))
                    issues.Add(string.Format("{0}: {1} is not an allowed file type ({2}).", rule("1.1.3", "1.2.3", ""), fileName,
                                             string.Join(", ", allowedTypes.Select(t => t.TrimStart('.')))));
                if (webFlac && fileName.Contains("_-_"))
                    issues.Add(string.Format("WEB FLAC 5.6.1: _-_ is not allowed in file names ({0}).", fileName));
                if (extension == audioExtension && !Regex.IsMatch(fileName, @"^([0-9]+|[a-zA-Z][0-9]+)-"))
                    issues.Add(string.Format("{0}: {1} must start with the track number.", rule("5.1", "5.1", "4.2"), fileName));
            }
            if (!release.Files.Any(f => f.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase)))
                issues.Add(rule("1.1", "1.2", "2.1") + ": the release needs an SFV.");
            if (!release.Files.Any(f => f.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase)))
                issues.Add(rule("1.1", "1.2", "2.1") + ": the release needs an NFO.");
            if (!web && !release.Files.Any(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
                issues.Add(rule("1.1 / 9.1", "", "2.2 / 17.1") + ": a physical release needs a JPG proof photo (medium with cover).");
            if (web && string.IsNullOrWhiteSpace(release.Url))
                issues.Add(rule("", "7.2", "13.2") + ": the NFO must have the full URL of the release at the shop/service (fill in URL on the release page).");

            if (album == null)
                return issues;

            // ---- Tags
            if (flac && !string.IsNullOrWhiteSpace(album.Genre) && ToAllowedGenre(album.Genre) != album.Genre.Trim())
            {
                string suggestion = ToAllowedGenre(album.Genre);
                issues.Add(string.Format("{0}: genre \"{1}\" is not in the allowed list{2}.", rule("6.5", "6.6", ""), album.Genre,
                                         suggestion != null ? " (use " + suggestion + ")" : ""));
            }
            if (string.IsNullOrWhiteSpace(album.Genre))
                issues.Add(rule("6.2", "6.2", "15.1") + ": the genre is empty.");
            if (mp3 && album.CoverBytes != null && album.CoverBytes.Length > MaxMp3PictureBytes)
                issues.Add("MP3 15.2: the cover is larger than 2 MB, so it is not embedded.");

            // ---- Audio format
            if (rules == SceneRuleSet.Flac2016)
            {
                bool cd = source.Length == 0 || Regex.IsMatch(source, @"^\d*CD");
                foreach (AlbumTrack track in album.Tracks)
                {
                    FlacStreamInfo info = FlacStreamInfo.Read(track.OriginalFileName);
                    if (info == null)
                        continue;
                    if (cd && (info.SampleRate != 44100 || info.BitsPerSample != 16 || info.Channels != 2))
                    {
                        issues.Add(string.Format("FLAC 3.2: CD releases are 44.1 kHz / 16 bit / stereo; {0} is {1:0.#} kHz / {2} bit / {3} ch.",
                                                 Path.GetFileName(track.OriginalFileName), info.SampleRate / 1000.0, info.BitsPerSample, info.Channels));
                        break;
                    }
                }
            }
            else if (mp3)
            {
                AlbumTrack wrong = album.Tracks.FirstOrDefault(t => t.Frequency != 0 && t.Frequency != 44100 && t.Frequency != 48000);
                if (wrong != null)
                    issues.Add(string.Format("MP3 10.3: {0} is {1} Hz (44.1 or 48 kHz only).", Path.GetFileName(wrong.OriginalFileName), wrong.Frequency));

                // WEB music: 320 kbps CBR (bought as MP3, or encoded from a lossless download with LAME -b 320).
                AlbumTrack low = web ? album.Tracks.FirstOrDefault(t => t.Bitrate > 0 && t.Bitrate < 319) : null;
                if (low != null)
                    issues.Add(string.Format("MP3 10.1 / 10.2.1: WEB music is 320 kbps CBR; {0} is {1:0} kbps.", Path.GetFileName(low.OriginalFileName), low.Bitrate));
                if (album.Tracks.Select(t => Math.Round(t.Bitrate)).Where(b => b > 0).Distinct().Count() > 1 && web)
                    issues.Add("MP3 13: all tracks of a WEB release must have the same bitrate.");

                // Encoder: LAME 3.100 -V0 (non-WEB) or -b 320 (lossless WEB). MP3CD/FLASH MP3s are released as they are.
                if (!Regex.IsMatch(source, "MP3CD|FLASH"))
                    CheckLame(album, web, issues);
            }

            return issues;
        }

        private static void CheckLame(Album album, bool web, List<string> issues)
        {
            // Only files on disk can be checked (not tracks built from values).
            var headers = album.Tracks
                .Where(t => File.Exists(t.OriginalFileName))
                .Select(t => new { Name = Path.GetFileName(t.OriginalFileName), Header = LameHeader.Read(t.OriginalFileName) })
                .ToList();
            if (headers.Count == 0)
                return;

            if (web)
            {
                // Bought as MP3: released unmodified, any encoder. Encoded by the group (LAME 3.100): -b 320 only.
                var wrong = headers.Where(h => h.Header != null && h.Header.IsLame3100 && !h.Header.IsScene320).ToList();
                if (wrong.Count > 0)
                    issues.Add(string.Format("MP3 10.2.1: lossless WEB sources are encoded with LAME 3.100 -b 320; {0} is {1}{2}.",
                                             wrong[0].Name, wrong[0].Header.Settings, More(wrong.Count)));
                if (headers.Select(h => h.Header == null ? "" : h.Header.Settings).Distinct().Count() > 1)
                    issues.Add("MP3 13: all tracks of a WEB release must come from the same encoder and settings.");
                return;
            }

            var notV0 = headers.Where(h => h.Header == null || !h.Header.IsSceneV0).ToList();
            if (notV0.Count > 0)
                issues.Add(string.Format("MP3 10.2.2: encode with LAME 3.100 -V0; {0} is {1}{2}.", notV0[0].Name,
                                         notV0[0].Header == null ? "not from LAME (no LAME header)" : notV0[0].Header.Settings, More(notV0.Count)));
        }

        private static string More(int count)
        {
            return count > 1 ? string.Format(" ({0} tracks)", count) : "";
        }
    }
}

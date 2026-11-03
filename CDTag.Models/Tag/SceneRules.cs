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
                else if (album != null)
                {
                    string actual = BitDepthTag(album);
                    if (actual != null && actual != bit.Groups[1].Value + "BIT")
                        issues.Add(string.Format("WEB FLAC 4.10.1: the release name says {0}BIT but the files are {1}.", bit.Groups[1].Value, actual));
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
            }

            return issues;
        }
    }
}

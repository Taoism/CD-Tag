using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CDTag.Model.Lookup;

namespace CDTag.Model.Tag
{
    /// <summary>A rule file found on scenerules.org.</summary>
    public class SceneRuleFile
    {
        /// <summary>"FLAC", "MP3", "WEBFLAC".</summary>
        public string Kind { get; set; }
        public int Year { get; set; }
        /// <summary>"2016_FLAC.nfo".</summary>
        public string FileName { get; set; }
        public string Sha256 { get; set; }
    }

    /// <summary>What changed since the last check.</summary>
    public class SceneRulesChange
    {
        public SceneRuleFile File { get; set; }
        /// <summary>"new", "changed" or "newer" (a newer rule set than the one CD-Tag checks).</summary>
        public string What { get; set; }

        public override string ToString()
        {
            switch (What)
            {
                case "changed": return string.Format("{0} was updated on scenerules.org.", File.FileName);
                case "newer": return string.Format("{0} is newer than the {1} rules CD-Tag checks ({2}); review Finish's findings by hand.",
                                                   File.FileName, File.Kind, SceneRulesUpdater.BuiltInYear(File.Kind));
                default: return string.Format("{0} is new on scenerules.org.", File.FileName);
            }
        }
    }

    /// <summary>
    /// Keeps a local copy of the newest scene rules (scenerules.org) in the Rules folder and reports when they change.
    /// The FLAC genre list for the rules check is read from the downloaded FLAC rules.
    /// </summary>
    public partial class SceneRulesUpdater
    {
        private const string SiteUrl = "https://scenerules.org/";
        private const string ManifestName = "rules.json";
        private static readonly JsonSerializerOptions IndentedJson = new JsonSerializerOptions { WriteIndented = true };

        private readonly string _directory;
        private readonly HttpClient _http;

        public SceneRulesUpdater(string directory, HttpClient http = null)
        {
            _directory = directory;
            _http = http ?? CreateClient();
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(MusicBrainzClient.UserAgent);
            return client;
        }

        /// <summary>True when the Rules folder has a manifest and every rule file it lists.</summary>
        public bool HasRules
        {
            get
            {
                Dictionary<string, SceneRuleFile> known = LoadManifest();
                return known.Count > 0 && known.Values.All(f => f.FileName != null && File.Exists(Path.Combine(_directory, f.FileName)));
            }
        }

        /// <summary>The year of the rules the checks in <see cref="SceneRules"/> follow.</summary>
        public static int BuiltInYear(string kind)
        {
            switch (kind)
            {
                case "FLAC": return 2016;
                case "MP3": return 2021;
                case "WEBFLAC": return 2023;
                default: return 0;
            }
        }

        /// <summary>Downloads the newest rule files and returns what changed since the last check.</summary>
        public async Task<List<SceneRulesChange>> CheckAsync(CancellationToken cancellation = default(CancellationToken))
        {
            string index = await _http.GetStringAsync(SiteUrl, cancellation).ConfigureAwait(false);
            List<SceneRuleFile> latest = FindLatest(index);

            Directory.CreateDirectory(_directory);
            Dictionary<string, SceneRuleFile> known = LoadManifest();
            var notified = new HashSet<string>(known.Values.Where(v => v.Sha256 != null).Select(v => v.FileName), StringComparer.OrdinalIgnoreCase);
            bool firstRun = known.Count == 0;
            var changes = new List<SceneRulesChange>();

            foreach (SceneRuleFile file in latest)
            {
                byte[] bytes = await _http.GetByteArrayAsync(SiteUrl + "nfo/" + file.FileName, cancellation).ConfigureAwait(false);
                file.Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                await File.WriteAllBytesAsync(Path.Combine(_directory, file.FileName), bytes, CancellationToken.None).ConfigureAwait(false);

                SceneRuleFile before;
                known.TryGetValue(file.Kind, out before);
                if (before == null || !string.Equals(before.FileName, file.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!firstRun)
                        changes.Add(new SceneRulesChange { File = file, What = "new" });
                    int builtIn = BuiltInYear(file.Kind);
                    if (builtIn > 0 && file.Year > builtIn)
                        changes.Add(new SceneRulesChange { File = file, What = "newer" });
                }
                else if (!string.Equals(before.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add(new SceneRulesChange { File = file, What = "changed" });
                }
                known[file.Kind] = file;
            }

            SaveManifest(known);
            ApplyGenres();
            return changes;
        }

        /// <summary>The newest rule file of each kind linked from the scenerules.org index page.</summary>
        public static List<SceneRuleFile> FindLatest(string indexHtml)
        {
            return RuleLink().Matches(indexHtml ?? "")
                .Select(m => new SceneRuleFile
                {
                    Kind = m.Groups["kind"].Value,
                    Year = int.Parse(m.Groups["year"].Value, CultureInfo.InvariantCulture),
                    FileName = m.Groups["year"].Value + "_" + m.Groups["kind"].Value + ".nfo"
                })
                .GroupBy(f => f.Kind)
                .Select(g => g.OrderByDescending(f => f.Year).First())
                .OrderBy(f => f.Kind)
                .ToList();
        }

        /// <summary>Uses the genre list of the downloaded FLAC rules (when it is the rule set CD-Tag checks).</summary>
        public void ApplyGenres()
        {
            string path = Path.Combine(_directory, BuiltInYear("FLAC") + "_FLAC.nfo");
            if (!File.Exists(path))
                return;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            List<string> genres = ParseFlacGenres(File.ReadAllText(path, Encoding.GetEncoding(437)));
            if (genres.Count > 50)
                SceneRules.SetFlacGenres(genres);
        }

        /// <summary>The genres listed in rule 6.5 of the FLAC rules NFO.</summary>
        public static List<string> ParseFlacGenres(string nfo)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(nfo))
                return result;
            int start = nfo.IndexOf("genres listed:", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return result;
            int end = nfo.IndexOf("NOTE:", start, StringComparison.OrdinalIgnoreCase);
            string block = nfo.Substring(start + "genres listed:".Length, (end > start ? end : nfo.Length) - start - "genres listed:".Length);

            // Box-drawn lines: "|      A Cappella, Acid, ...      |"
            var text = new StringBuilder();
            foreach (string line in block.Replace("\r\n", "\n").Split('\n'))
            {
                string content = line.Trim().Trim('|').Trim();
                if (content.StartsWith('+'))
                    break;
                text.Append(' ').Append(content);
            }
            foreach (string genre in text.ToString().Split(','))
            {
                string name = Whitespace().Replace(genre, " ").Trim().TrimEnd('.');
                if (name.Length > 0)
                    result.Add(name);
            }
            return result;
        }

        private Dictionary<string, SceneRuleFile> LoadManifest()
        {
            string path = Path.Combine(_directory, ManifestName);
            try
            {
                if (File.Exists(path))
                {
                    var files = JsonSerializer.Deserialize<List<SceneRuleFile>>(File.ReadAllText(path));
                    if (files != null)
                        return files.Where(f => f.Kind != null).ToDictionary(f => f.Kind);
                }
            }
            catch (JsonException)
            {
            }
            return new Dictionary<string, SceneRuleFile>();
        }

        private void SaveManifest(Dictionary<string, SceneRuleFile> files)
        {
            string json = JsonSerializer.Serialize(files.Values.OrderBy(f => f.Kind).ToList(), IndentedJson);
            File.WriteAllText(Path.Combine(_directory, ManifestName), json);
        }

        [GeneratedRegex(@"nfo/(?<year>\d{4})_(?<kind>MP3|FLAC|WEBFLAC)\.nfo", RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex RuleLink();

        [GeneratedRegex(@"\s+", RegexOptions.None, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex Whitespace();
    }
}

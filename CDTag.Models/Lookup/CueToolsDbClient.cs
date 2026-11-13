using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CDTag.Model.Lookup
{
    /// <summary>
    /// CUETools Database (db.cue.tools, over HTTPS; db.cuetools.net only has a certificate for cue.tools) metadata lookup by disc TOC — the service behind CUETools / CUERipper and
    /// EAC's CTDB plugin. One request returns matching releases from MusicBrainz, Discogs and freedb.
    /// </summary>
    public class CueToolsDbClient
    {
        private const string LookupUrl = "https://db.cue.tools/lookup2.php?version=3&ctdb=0&fuzzy=1&metadata=";
        private static readonly XNamespace Ns = "http://db.cuetools.net/ns/mmd-1.0#";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(MusicBrainzClient.UserAgent);
            return client;
        }

        /// <summary>
        /// Looks the disc up. <paramref name="metadataSearch"/> is the CTDB plugin's search mode: "fast", "default" or "extensive"
        /// (all databases, fuzzy too; EAC's CTDB plugin setting MetadataSearch).
        /// </summary>
        public async Task<List<ReleaseDetails>> LookupAsync(DiscToc toc, string metadataSearch = "extensive", CancellationToken cancellation = default(CancellationToken))
        {
            ArgumentNullException.ThrowIfNull(toc);
            string mode = (metadataSearch ?? "extensive").Trim().ToLowerInvariant();
            if (mode != "fast" && mode != "default" && mode != "extensive")
                mode = "extensive";

            using (HttpResponseMessage response = await Http.GetAsync(LookupUrl + mode + "&toc=" + toc.CtdbToc, cancellation).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new List<ReleaseDetails>();
                response.EnsureSuccessStatusCode();
                string xml = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                return Parse(xml, toc);
            }
        }

        /// <summary>
        /// The rips of this disc stored in CTDB (for verifying a rip): per entry the number of submissions and a CRC per track.
        /// Empty when the disc is not in CTDB; null when CTDB can't be reached.
        /// </summary>
        public async Task<List<CtdbEntry>> LookupRipsAsync(DiscToc toc, CancellationToken cancellation = default(CancellationToken))
        {
            try
            {
                string url = "https://db.cue.tools/lookup2.php?version=3&ctdb=1&fuzzy=0&metadata=none&toc=" + toc.CtdbToc;
                using (HttpResponseMessage response = await Http.GetAsync(url, cancellation).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        return new List<CtdbEntry>();
                    if (!response.IsSuccessStatusCode)
                        return null;
                    return ParseRips(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested || ex is System.Xml.XmlException)
            {
                return null;
            }
        }

        public static List<CtdbEntry> ParseRips(string xml)
        {
            var result = new List<CtdbEntry>();
            XDocument doc = XDocument.Parse(xml);
            foreach (XElement e in doc.Descendants(Ns + "entry"))
            {
                int confidence;
                int.TryParse((string)e.Attribute("confidence"), NumberStyles.Integer, CultureInfo.InvariantCulture, out confidence);
                var crcs = new List<uint>();
                foreach (string part in ((string)e.Attribute("trackcrcs") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    uint crc;
                    if (uint.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out crc))
                        crcs.Add(crc);
                }
                result.Add(new CtdbEntry { Confidence = confidence, TrackCrcs = crcs, Id = (string)e.Attribute("id") });
            }
            return result;
        }

        /// <summary>Downloads a cover image; null when it is not available.</summary>
        public static async Task<byte[]> GetImageAsync(string url, CancellationToken cancellation = default(CancellationToken))
        {
            using (HttpResponseMessage response = await Http.GetAsync(url, cancellation).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    return null;
                return await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
            }
        }

        // ------------------------------------------------------------------ parsing (public for tests)

        /// <summary>Releases in a lookup2.php response; track numbers and lengths come from <paramref name="toc"/>.</summary>
        public static List<ReleaseDetails> Parse(string xml, DiscToc toc)
        {
            var results = new List<Tuple<ReleaseDetails, int>>();
            if (string.IsNullOrWhiteSpace(xml) || !xml.TrimStart().StartsWith('<'))
                return new List<ReleaseDetails>(); // "Not Found"

            XDocument document = XDocument.Parse(xml);
            int index = 0;
            foreach (XElement meta in document.Root.Elements(Ns + "metadata"))
            {
                string source = SourceName((string)meta.Attribute("source"));
                string id = (string)meta.Attribute("id");
                XElement label = meta.Elements(Ns + "label").FirstOrDefault();
                XElement release = meta.Elements(Ns + "release").FirstOrDefault();
                string artist = Clean((string)meta.Attribute("artist"));

                var details = new ReleaseDetails
                {
                    Id = source + ":" + id + ":" + (index++).ToString(CultureInfo.InvariantCulture),
                    Source = source,
                    Artist = artist,
                    Title = Clean((string)meta.Attribute("album")),
                    Date = Clean(release != null ? (string)release.Attribute("date") : null) ?? Clean((string)meta.Attribute("year")),
                    Country = Clean(release != null ? (string)release.Attribute("country") : null),
                    Label = Clean(label != null ? (string)label.Attribute("name") : null),
                    CatalogNumber = Clean(label != null ? (string)label.Attribute("catno") : null),
                    Genre = Clean((string)meta.Attribute("genre")),
                    Format = "CD",
                    Discs = Math.Max(1, ParseInt((string)meta.Attribute("disccount"))),
                    MusicBrainzId = source == "MusicBrainz" ? id : null,
                    DiscogsId = source == "Discogs" && id != null && id.All(char.IsDigit) ? id : null,
                    CoverUrl = CoverUrl(meta),
                    OnlyDisc = toc != null ? toc.Disc : 0,
                    Tracks = new List<ReleaseTrack>()
                };
                string discName = Clean((string)meta.Attribute("discname"));
                if (discName != null)
                    details.DiscNames[toc != null && toc.Disc > 0 ? toc.Disc : 1] = discName;
                if (details.Genre != null)
                    details.Genre = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(details.Genre);

                int position = 0;
                foreach (XElement track in meta.Elements(Ns + "track"))
                {
                    details.Tracks.Add(new ReleaseTrack
                    {
                        Disc = toc != null && toc.Disc > 0 ? toc.Disc : 1,
                        Position = position + 1,
                        Title = Clean((string)track.Attribute("name")),
                        Artist = Clean((string)track.Attribute("artist")) ?? artist,
                        Length = toc != null && position < toc.AudioTrackCount ? toc.TrackSeconds(position) : 0
                    });
                    position++;
                }

                details.IsVariousArtists = string.Equals(artist, "Various Artists", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(artist, "Various", StringComparison.OrdinalIgnoreCase) ||
                                           details.Tracks.Select(t => t.Artist).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
                // MusicBrainz entries have no relevance: they are exact TOC matches.
                string relevance = (string)meta.Attribute("relevance");
                results.Add(Tuple.Create(details, relevance == null ? 100 : ParseInt(relevance)));
            }

            // MusicBrainz first, then Discogs, then freedb; best relevance first (OrderBy is stable).
            return results.OrderBy(r => SourceOrder(r.Item1.Source)).ThenByDescending(r => r.Item2).Select(r => r.Item1).ToList();
        }

        private static string CoverUrl(XElement meta)
        {
            XElement cover = meta.Elements(Ns + "coverart").FirstOrDefault(c => (string)c.Attribute("primary") == "1")
                             ?? meta.Elements(Ns + "coverart").FirstOrDefault();
            if (cover == null)
                return null;
            // Cover Art Archive thumbnails: "-250.jpg" -> the 500 px version.
            string small = (string)cover.Attribute("uri150");
            if (small != null && small.Contains("coverartarchive.org") && small.EndsWith("-250.jpg", StringComparison.OrdinalIgnoreCase))
                return Secure(string.Concat(small.AsSpan(0, small.Length - "-250.jpg".Length), "-500.jpg"));
            return Secure((string)cover.Attribute("uri") ?? small);
        }

        /// <summary>
        /// Cover links in CTDB answers may be http:// (Cover Art Archive, Discogs); both serve the same images over https,
        /// so the image can't be swapped on the way. Anything that isn't http(s) is dropped.
        /// </summary>
        public static string Secure(string url)
        {
            Uri uri;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri))
                return null;
            if (uri.Scheme == Uri.UriSchemeHttps)
                return uri.AbsoluteUri;
            if (uri.Scheme != Uri.UriSchemeHttp)
                return null;
            var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 };
            return builder.Uri.AbsoluteUri;
        }

        private static string SourceName(string source)
        {
            switch ((source ?? "").ToLowerInvariant())
            {
                case "musicbrainz": return "MusicBrainz";
                case "discogs": return "Discogs";
                case "freedb": return "freedb";
                default: return source;
            }
        }

        private static int SourceOrder(string source)
        {
            switch (source)
            {
                case "MusicBrainz": return 0;
                case "Discogs": return 1;
                case "freedb": return 2;
                default: return 3;
            }
        }

        private static string Clean(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static int ParseInt(string text)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }
    }
}

namespace CDTag.Model.Lookup
{
    /// <summary>One rip of a disc stored in the CUETools database: how many submitted it, and its CRC per track.</summary>
    public class CtdbEntry
    {
        public string Id { get; set; }
        public int Confidence { get; set; }
        public System.Collections.Generic.List<uint> TrackCrcs { get; set; }
    }
}

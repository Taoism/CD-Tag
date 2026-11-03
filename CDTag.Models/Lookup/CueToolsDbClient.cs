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
    /// CUETools Database (db.cuetools.net) metadata lookup by disc TOC — the service behind CUETools / CUERipper and
    /// EAC's CTDB plugin. One request returns matching releases from MusicBrainz, Discogs and freedb.
    /// </summary>
    public class CueToolsDbClient
    {
        private const string LookupUrl = "http://db.cuetools.net/lookup2.php?version=3&ctdb=0&metadata=extensive&fuzzy=1&toc=";
        private static readonly XNamespace Ns = "http://db.cuetools.net/ns/mmd-1.0#";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(MusicBrainzClient.UserAgent);
            return client;
        }

        public async Task<List<ReleaseDetails>> LookupAsync(DiscToc toc, CancellationToken cancellation = default(CancellationToken))
        {
            if (toc == null)
                throw new ArgumentNullException("toc");

            using (HttpResponseMessage response = await Http.GetAsync(LookupUrl + toc.CtdbToc, cancellation).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new List<ReleaseDetails>();
                response.EnsureSuccessStatusCode();
                string xml = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return Parse(xml, toc);
            }
        }

        /// <summary>Downloads a cover image; null when it is not available.</summary>
        public static async Task<byte[]> GetImageAsync(string url, CancellationToken cancellation = default(CancellationToken))
        {
            using (HttpResponseMessage response = await Http.GetAsync(url, cancellation).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    return null;
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        // ------------------------------------------------------------------ parsing (public for tests)

        /// <summary>Releases in a lookup2.php response; track numbers and lengths come from <paramref name="toc"/>.</summary>
        public static List<ReleaseDetails> Parse(string xml, DiscToc toc)
        {
            var results = new List<Tuple<ReleaseDetails, int>>();
            if (string.IsNullOrWhiteSpace(xml) || !xml.TrimStart().StartsWith("<", StringComparison.Ordinal))
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
                    CoverUrl = CoverUrl(meta),
                    OnlyDisc = toc != null ? toc.Disc : 0,
                    Tracks = new List<ReleaseTrack>()
                };
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
                return small.Substring(0, small.Length - "-250.jpg".Length) + "-500.jpg";
            return (string)cover.Attribute("uri") ?? small;
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

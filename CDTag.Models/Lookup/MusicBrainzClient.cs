using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CDTag.Model.Lookup
{
    /// <summary>A release found by a search (summary row).</summary>
    public class ReleaseSearchResult
    {
        public string Id { get; set; }
        public int Score { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
        public string Date { get; set; }
        public string Country { get; set; }
        /// <summary>e.g. "2×CD", "Digital Media", "CD + DVD".</summary>
        public string Format { get; set; }
        public int TrackCount { get; set; }
        public string Label { get; set; }
        public string CatalogNumber { get; set; }
        public string Disambiguation { get; set; }
        public bool MatchesTrackCount { get; set; }
        /// <summary>Where the data comes from: "MusicBrainz", "Discogs", "freedb".</summary>
        public string Source { get; set; }
    }

    public class ReleaseTrack
    {
        public int Disc { get; set; }
        public int Position { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        /// <summary>Length in seconds (0 when unknown).</summary>
        public int Length { get; set; }

        public string LengthText
        {
            get { return Length > 0 ? string.Format("{0}:{1:00}", Length / 60, Length % 60) : ""; }
        }
    }

    /// <summary>Full release details used to fill Tag Album.</summary>
    public class ReleaseDetails
    {
        public string Id { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
        public string Date { get; set; }
        public string Label { get; set; }
        public string CatalogNumber { get; set; }
        public string Genre { get; set; }
        /// <summary>Format of the first medium: "CD", "Digital Media", "12\" Vinyl", ...</summary>
        public string Format { get; set; }
        public int Discs { get; set; }
        public bool IsVariousArtists { get; set; }
        public List<ReleaseTrack> Tracks { get; set; }
        public string Country { get; set; }
        /// <summary>"MusicBrainz", "Discogs", "freedb".</summary>
        public string Source { get; set; }
        /// <summary>MusicBrainz release ID (for the Cover Art Archive); null for other sources.</summary>
        public string MusicBrainzId { get; set; }
        /// <summary>Cover image to download instead of the Cover Art Archive front cover.</summary>
        public string CoverUrl { get; set; }
        /// <summary>When the data covers one disc of the album only (disc-ID lookups): that disc; otherwise 0.</summary>
        public int OnlyDisc { get; set; }
        /// <summary>Disc names (MusicBrainz medium titles, CTDB disc names) by disc number.</summary>
        public Dictionary<int, string> DiscNames { get; set; } = new Dictionary<int, string>();

        public ReleaseSearchResult ToSearchResult(int trackCount)
        {
            return new ReleaseSearchResult
            {
                Id = Id, Artist = Artist, Title = Title, Date = Date, Country = Country, Label = Label, CatalogNumber = CatalogNumber,
                TrackCount = Tracks.Count, Score = 100, Source = Source,
                Format = Discs > 1 ? Discs + "×" + Format : Format,
                MatchesTrackCount = Tracks.Count == trackCount
            };
        }
    }

    /// <summary>
    /// Minimal MusicBrainz web service client (https://musicbrainz.org/doc/MusicBrainz_API) plus the
    /// Cover Art Archive. Requests are rate limited to one per second as MusicBrainz asks.
    /// </summary>
    public class MusicBrainzClient
    {
        private const string BaseUrl = "https://musicbrainz.org/ws/2/";
        private const string CoverArtUrl = "https://coverartarchive.org/release/";
        public const string UserAgent = "CD-Tag/2.0 ( https://github.com/Taoism/CD-Tag )";

        private static readonly HttpClient Http = CreateClient();
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static DateTime _lastRequest = DateTime.MinValue;

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }

        /// <summary>Searches releases by artist and album title. <paramref name="trackCount"/> is used to rank matches.</summary>
        public async Task<List<ReleaseSearchResult>> SearchReleasesAsync(string artist, string album, int trackCount, CancellationToken cancellation = default(CancellationToken))
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(album))
                query.Add("release:\"" + Escape(album) + "\"");
            if (!string.IsNullOrWhiteSpace(artist))
                query.Add("artist:\"" + Escape(artist) + "\"");
            if (query.Count == 0)
                return new List<ReleaseSearchResult>();

            string url = BaseUrl + "release?fmt=json&limit=40&query=" + Uri.EscapeDataString(string.Join(" AND ", query));
            string json = await GetStringAsync(url, cancellation).ConfigureAwait(false);
            return ParseSearch(json, trackCount);
        }

        public async Task<ReleaseDetails> GetReleaseAsync(string id, CancellationToken cancellation = default(CancellationToken))
        {
            string url = BaseUrl + "release/" + Uri.EscapeDataString(id) + "?fmt=json&inc=recordings+artist-credits+labels+genres+release-groups+media";
            string json = await GetStringAsync(url, cancellation).ConfigureAwait(false);
            return ParseRelease(json);
        }

        /// <summary>
        /// Releases containing the disc <paramref name="toc"/> (exact disc ID, otherwise MusicBrainz' fuzzy TOC match),
        /// with full track lists.
        /// </summary>
        public async Task<List<ReleaseDetails>> LookupDiscAsync(DiscToc toc, CancellationToken cancellation = default(CancellationToken))
        {
            string url = BaseUrl + "discid/" + Uri.EscapeDataString(toc.MusicBrainzDiscId) + "?toc=" + toc.MusicBrainzToc +
                         "&cdstubs=no&fmt=json&inc=recordings+artist-credits+labels+genres+release-groups";
            string json;
            try
            {
                json = await GetStringAsync(url, cancellation).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return new List<ReleaseDetails>();
                throw;
            }
            return ParseDiscLookup(json);
        }

        public static List<ReleaseDetails> ParseDiscLookup(string json)
        {
            var results = new List<ReleaseDetails>();
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement releases;
                if (document.RootElement.TryGetProperty("releases", out releases) && releases.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement release in releases.EnumerateArray())
                        results.Add(ParseRelease(release));
                }
            }
            return results;
        }

        /// <summary>Front cover (500 px) from the Cover Art Archive, or null when there is none.</summary>
        public async Task<byte[]> GetFrontCoverAsync(string id, CancellationToken cancellation = default(CancellationToken))
        {
            await ThrottleAsync(cancellation).ConfigureAwait(false);
            try
            {
                using (HttpResponseMessage response = await Http.GetAsync(CoverArtUrl + Uri.EscapeDataString(id) + "/front-500", cancellation).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        return null;
                    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                Gate.Release();
            }
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken cancellation)
        {
            // MusicBrainz answers 503 when its per-IP rate limit is hit; back off and retry a few times.
            for (int attempt = 1; ; attempt++)
            {
                await ThrottleAsync(cancellation).ConfigureAwait(false);
                bool retry;
                try
                {
                    using (HttpResponseMessage response = await Http.GetAsync(url, cancellation).ConfigureAwait(false))
                    {
                        retry = (int)response.StatusCode == 503 && attempt < 4;
                        if (!retry)
                        {
                            if ((int)response.StatusCode == 503)
                                throw new InvalidOperationException("MusicBrainz is busy right now (rate limited). Please try again in a minute.");
                            response.EnsureSuccessStatusCode();
                            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    Gate.Release();
                }
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellation).ConfigureAwait(false);
            }
        }

        /// <summary>Waits so requests are at least 1.1 s apart; the caller must release <see cref="Gate"/>.</summary>
        private static async Task ThrottleAsync(CancellationToken cancellation)
        {
            await Gate.WaitAsync(cancellation).ConfigureAwait(false);
            TimeSpan wait = _lastRequest.AddMilliseconds(1100) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellation).ConfigureAwait(false);
            _lastRequest = DateTime.UtcNow;
        }

        /// <summary>Escapes Lucene special characters in a phrase.</summary>
        private static string Escape(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text.Trim())
            {
                if ("+-&|!(){}[]^\"~*?:\\/".IndexOf(c) >= 0)
                    sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ parsing (public for tests)

        public static List<ReleaseSearchResult> ParseSearch(string json, int trackCount)
        {
            var results = new List<ReleaseSearchResult>();
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement releases;
                if (!document.RootElement.TryGetProperty("releases", out releases))
                    return results;

                foreach (JsonElement release in releases.EnumerateArray())
                {
                    var result = new ReleaseSearchResult
                    {
                        Id = Str(release, "id"),
                        Score = Int(release, "score"),
                        Title = Str(release, "title"),
                        Artist = ArtistCredit(release),
                        Date = Str(release, "date"),
                        Country = Str(release, "country"),
                        TrackCount = Int(release, "track-count"),
                        Disambiguation = Str(release, "disambiguation"),
                        Source = "MusicBrainz"
                    };

                    JsonElement media;
                    if (release.TryGetProperty("media", out media) && media.ValueKind == JsonValueKind.Array)
                        result.Format = DescribeFormats(media.EnumerateArray().Select(m => Str(m, "format") ?? "?").ToList());

                    JsonElement labels;
                    if (release.TryGetProperty("label-info", out labels) && labels.ValueKind == JsonValueKind.Array)
                    {
                        JsonElement first = labels.EnumerateArray().FirstOrDefault();
                        if (first.ValueKind == JsonValueKind.Object)
                        {
                            result.CatalogNumber = Str(first, "catalog-number");
                            JsonElement label;
                            if (first.TryGetProperty("label", out label) && label.ValueKind == JsonValueKind.Object)
                                result.Label = Str(label, "name");
                        }
                    }

                    result.MatchesTrackCount = trackCount > 0 && result.TrackCount == trackCount;
                    results.Add(result);
                }
            }

            return results.OrderByDescending(r => r.MatchesTrackCount).ThenByDescending(r => r.Score).ToList();
        }

        public static ReleaseDetails ParseRelease(string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
                return ParseRelease(document.RootElement);
        }

        private static ReleaseDetails ParseRelease(JsonElement release)
        {
            {
                var details = new ReleaseDetails
                {
                    Id = Str(release, "id"),
                    Title = Str(release, "title"),
                    Artist = ArtistCredit(release),
                    Date = Str(release, "date"),
                    Country = Str(release, "country"),
                    Source = "MusicBrainz",
                    Tracks = new List<ReleaseTrack>()
                };
                details.MusicBrainzId = details.Id;

                JsonElement labels;
                if (release.TryGetProperty("label-info", out labels) && labels.ValueKind == JsonValueKind.Array)
                {
                    JsonElement first = labels.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object)
                    {
                        details.CatalogNumber = Str(first, "catalog-number");
                        JsonElement label;
                        if (first.TryGetProperty("label", out label) && label.ValueKind == JsonValueKind.Object)
                            details.Label = Str(label, "name");
                    }
                }

                // Genres: release first, then release group; most voted wins.
                details.Genre = TopGenre(release);
                JsonElement group;
                if (details.Genre == null && release.TryGetProperty("release-group", out group) && group.ValueKind == JsonValueKind.Object)
                    details.Genre = TopGenre(group);

                JsonElement media;
                if (release.TryGetProperty("media", out media) && media.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement medium in media.EnumerateArray())
                    {
                        int disc = Int(medium, "position");
                        if (details.Format == null)
                            details.Format = Str(medium, "format");
                        details.Discs++;
                        string discName = Str(medium, "title");
                        if (discName != null)
                            details.DiscNames[disc > 0 ? disc : details.Discs] = discName;

                        JsonElement tracks;
                        if (!medium.TryGetProperty("tracks", out tracks) || tracks.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (JsonElement track in tracks.EnumerateArray())
                        {
                            int length = Int(track, "length");
                            details.Tracks.Add(new ReleaseTrack
                            {
                                Disc = disc > 0 ? disc : details.Discs,
                                Position = Int(track, "position"),
                                Title = Str(track, "title"),
                                Artist = ArtistCredit(track) ?? details.Artist,
                                Length = (int)Math.Round(length / 1000.0)
                            });
                        }
                    }
                }

                details.IsVariousArtists = string.Equals(details.Artist, "Various Artists", StringComparison.OrdinalIgnoreCase) ||
                                           details.Tracks.Select(t => t.Artist).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
                return details;
            }
        }

        private static string TopGenre(JsonElement element)
        {
            JsonElement genres;
            if (!element.TryGetProperty("genres", out genres) || genres.ValueKind != JsonValueKind.Array)
                return null;
            JsonElement best = genres.EnumerateArray().OrderByDescending(g => Int(g, "count")).FirstOrDefault();
            string name = best.ValueKind == JsonValueKind.Object ? Str(best, "name") : null;
            return name == null ? null : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
        }

        /// <summary>"Ween", "Ween feat. X" — joined artist credit names.</summary>
        private static string ArtistCredit(JsonElement element)
        {
            JsonElement credits;
            if (!element.TryGetProperty("artist-credit", out credits) || credits.ValueKind != JsonValueKind.Array)
                return null;
            var sb = new StringBuilder();
            foreach (JsonElement credit in credits.EnumerateArray())
            {
                string name = Str(credit, "name");
                if (name == null)
                {
                    JsonElement artist;
                    if (credit.TryGetProperty("artist", out artist))
                        name = Str(artist, "name");
                }
                sb.Append(name).Append(Str(credit, "joinphrase") ?? "");
            }
            string result = sb.ToString().Trim();
            return result.Length == 0 ? null : result;
        }

        private static string DescribeFormats(List<string> formats)
        {
            if (formats.Count == 0)
                return null;
            return string.Join(" + ", formats.GroupBy(f => f).Select(g => g.Count() > 1 ? g.Count() + "×" + g.Key : g.Key));
        }

        private static string Str(JsonElement element, string name)
        {
            JsonElement value;
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.String)
                return null;
            string text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static int Int(JsonElement element, string name)
        {
            JsonElement value;
            int number;
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out value))
                return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number))
                return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
                return number;
            return 0;
        }
    }
}

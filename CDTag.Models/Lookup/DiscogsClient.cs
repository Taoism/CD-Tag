using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CDTag.Model.Lookup
{
    /// <summary>
    /// Discogs API (https://www.discogs.com/developers): release by ID or link, and search. Discogs documents search as
    /// needing a personal access token; in November 2026 it also answered without one (25 requests a minute instead of 60),
    /// so the token is used when there is one and a refusal without one points the user to discogs.com.
    /// </summary>
    public partial class DiscogsClient
    {
        private const string BaseUrl = "https://api.discogs.com/";
        /// <summary>Where a Discogs user makes a personal access token.</summary>
        public const string TokenPage = "https://www.discogs.com/settings/developers";
        public const string NoTokenMessage =
            "Discogs wants a personal access token for searching: click \"Get a token\" (discogs.com > Settings > Developers > Generate new token), paste it and click Save.";

        private static readonly HttpClient Http = CreateClient();
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static DateTime _lastRequest = DateTime.MinValue;

        private readonly string _token;

        public DiscogsClient(string token)
        {
            _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        }

        public bool HasToken
        {
            get { return _token != null; }
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(MusicBrainzClient.UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.discogs.v2.discogs+json");
            return client;
        }

        /// <summary>The release ID in "https://www.discogs.com/release/123-Artist-Title", "[r123]" or "r123"; null otherwise.</summary>
        public static string ReleaseIdFromLink(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            Match match = ReleaseLink().Match(text);
            if (!match.Success)
                match = ReleaseCode().Match(text);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>Releases by artist and title (needs a token). <paramref name="discs"/> ranks CDs with that many discs first.</summary>
        public async Task<List<ReleaseSearchResult>> SearchReleasesAsync(string artist, string album, int discs, CancellationToken cancellation = default(CancellationToken))
        {
            var query = new List<string> { "type=release", "per_page=50" };
            if (!string.IsNullOrWhiteSpace(artist))
                query.Add("artist=" + Uri.EscapeDataString(artist.Trim()));
            if (!string.IsNullOrWhiteSpace(album))
                query.Add("release_title=" + Uri.EscapeDataString(album.Trim()));
            string json = await GetStringAsync(BaseUrl + "database/search?" + string.Join("&", query), cancellation).ConfigureAwait(false);
            return ParseSearch(json, discs);
        }

        public async Task<ReleaseDetails> GetReleaseAsync(string id, CancellationToken cancellation = default(CancellationToken))
        {
            string json = await GetStringAsync(BaseUrl + "releases/" + Uri.EscapeDataString(id), cancellation).ConfigureAwait(false);
            return ParseRelease(json);
        }

        private async Task<string> GetStringAsync(string url, CancellationToken cancellation)
        {
            // 60 requests a minute with a token, 25 without.
            int spacing = HasToken ? 1100 : 2500;
            for (int attempt = 1; ; attempt++)
            {
                await Gate.WaitAsync(cancellation).ConfigureAwait(false);
                bool retry;
                try
                {
                    TimeSpan wait = _lastRequest.AddMilliseconds(spacing) - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, cancellation).ConfigureAwait(false);
                    _lastRequest = DateTime.UtcNow;

                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        if (HasToken)
                            request.Headers.TryAddWithoutValidation("Authorization", "Discogs token=" + _token);
                        using (HttpResponseMessage response = await Http.SendAsync(request, cancellation).ConfigureAwait(false))
                        {
                            int status = (int)response.StatusCode;
                            retry = status == 429 && attempt < 4;
                            if (!retry)
                            {
                                if (status == 401 && !HasToken)
                                    throw new InvalidOperationException(NoTokenMessage);
                                if (status == 401)
                                    throw new InvalidOperationException("Discogs did not accept the personal access token. Check it in Tools > Options, or make a new one at discogs.com > Settings > Developers.");
                                if (status == 429)
                                    throw new InvalidOperationException("Discogs is busy right now (rate limited). Please try again in a minute.");
                                if (status == 404)
                                    throw new InvalidOperationException("Discogs has no such release.");
                                response.EnsureSuccessStatusCode();
                                return await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                            }
                        }
                    }
                }
                finally
                {
                    Gate.Release();
                }
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), cancellation).ConfigureAwait(false);
            }
        }

        // ------------------------------------------------------------------ parsing (public for tests)

        public static List<ReleaseSearchResult> ParseSearch(string json, int discs)
        {
            var results = new List<Tuple<ReleaseSearchResult, int, int>>();
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement items;
                if (!document.RootElement.TryGetProperty("results", out items) || items.ValueKind != JsonValueKind.Array)
                    return new List<ReleaseSearchResult>();

                foreach (JsonElement item in items.EnumerateArray())
                {
                    if (Str(item, "type") != null && Str(item, "type") != "release")
                        continue;
                    string title = Str(item, "title") ?? "";
                    string artist = null;
                    int dash = title.IndexOf(" - ", StringComparison.Ordinal);
                    if (dash > 0)
                    {
                        artist = CleanArtist(title.Substring(0, dash));
                        title = title.Substring(dash + 3);
                    }
                    List<string> formats = Strings(item, "format");
                    int quantity = Int(item, "format_quantity");
                    string format = formats.FirstOrDefault();
                    List<string> labels = Strings(item, "label");

                    var result = new ReleaseSearchResult
                    {
                        Id = "discogs:" + Int(item, "id").ToString(CultureInfo.InvariantCulture),
                        Artist = artist,
                        Title = title,
                        Date = Str(item, "year"),
                        Country = Str(item, "country"),
                        Format = format == null ? null : (quantity > 1 ? quantity + "×" : "") + FormatName(format),
                        Label = labels.FirstOrDefault(),
                        CatalogNumber = Str(item, "catno"),
                        Source = "Discogs",
                        Disambiguation = string.Join(", ", formats.Skip(1))
                    };
                    bool cd = format != null && format.Contains("CD", StringComparison.OrdinalIgnoreCase);
                    int rank = (cd ? 2 : 0) + (discs > 0 && Math.Max(quantity, 1) == discs ? 1 : 0);
                    results.Add(Tuple.Create(result, rank, results.Count));
                }
            }
            return results.OrderByDescending(r => r.Item2).ThenBy(r => r.Item3).Select(r => r.Item1).ToList();
        }

        public static ReleaseDetails ParseRelease(string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement release = document.RootElement;
                string id = Int(release, "id").ToString(CultureInfo.InvariantCulture);
                var details = new ReleaseDetails
                {
                    Id = "discogs:" + id,
                    DiscogsId = id,
                    Source = "Discogs",
                    Artist = Artists(release, "artists"),
                    Title = Str(release, "title"),
                    Date = Date(Str(release, "released")) ?? (Int(release, "year") > 0 ? Int(release, "year").ToString(CultureInfo.InvariantCulture) : null),
                    Country = Str(release, "country"),
                    Tracks = new List<ReleaseTrack>()
                };

                JsonElement labels;
                if (release.TryGetProperty("labels", out labels) && labels.ValueKind == JsonValueKind.Array)
                {
                    JsonElement first = labels.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object)
                    {
                        details.Label = CleanArtist(Str(first, "name"));
                        string catno = Str(first, "catno");
                        details.CatalogNumber = catno == null || catno.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : catno;
                    }
                }

                int quantity = 0;
                JsonElement formats;
                if (release.TryGetProperty("formats", out formats) && formats.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement format in formats.EnumerateArray())
                    {
                        string name = Str(format, "name");
                        if (details.Format == null)
                            details.Format = FormatName(name);
                        int qty;
                        if (FormatName(name) == details.Format && int.TryParse(Str(format, "qty"), NumberStyles.Integer, CultureInfo.InvariantCulture, out qty))
                            quantity += qty;
                    }
                }

                List<string> styles = Strings(release, "styles");
                List<string> genres = Strings(release, "genres");
                details.Genre = PickGenre(styles, genres);

                JsonElement images;
                if (release.TryGetProperty("images", out images) && images.ValueKind == JsonValueKind.Array)
                {
                    JsonElement cover = images.EnumerateArray().FirstOrDefault(i => Str(i, "type") == "primary");
                    if (cover.ValueKind != JsonValueKind.Object)
                        cover = images.EnumerateArray().FirstOrDefault();
                    if (cover.ValueKind == JsonValueKind.Object)
                        details.CoverUrl = Str(cover, "uri") ?? Str(cover, "uri150");
                }

                JsonElement tracklist;
                if (release.TryGetProperty("tracklist", out tracklist) && tracklist.ValueKind == JsonValueKind.Array)
                    AddTracks(details, tracklist);

                details.Discs = Math.Max(Math.Max(quantity, 1), details.Tracks.Count == 0 ? 1 : details.Tracks.Max(t => t.Disc));
                details.IsVariousArtists = string.Equals(details.Artist, "Various Artists", StringComparison.OrdinalIgnoreCase) ||
                                           details.Tracks.Select(t => t.Artist).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
                return details;
            }
        }

        /// <summary>
        /// Discogs positions: "1-03", "CD2-1", "2.5" (disc and track), "7" or "07" (track; a restart at 1 means the next disc),
        /// "A1" / empty (numbered in order). Headings are skipped; an index track with its own numbered parts gives those parts,
        /// otherwise one track.
        /// </summary>
        private static void AddTracks(ReleaseDetails details, JsonElement tracklist)
        {
            int disc = 1;
            int last = 0;
            bool explicitDiscs = false;
            foreach (JsonElement item in tracklist.EnumerateArray())
            {
                string type = Str(item, "type_") ?? "track";
                if (type == "heading")
                    continue;

                var parts = new List<JsonElement>();
                JsonElement subTracks;
                if (type == "index" && item.TryGetProperty("sub_tracks", out subTracks) && subTracks.ValueKind == JsonValueKind.Array)
                {
                    var subs = subTracks.EnumerateArray().ToList();
                    bool separate = subs.Count > 1 && subs.All(s => NumberedPosition().IsMatch(Str(s, "position") ?? ""));
                    if (separate)
                        parts.AddRange(subs);
                }
                bool wholeIndex = parts.Count == 0 && type == "index";
                if (parts.Count == 0)
                    parts.Add(item);

                foreach (JsonElement part in parts)
                {
                    string position = Str(part, "position") ?? "";
                    if (wholeIndex && position.Length == 0 && item.TryGetProperty("sub_tracks", out subTracks) && subTracks.ValueKind == JsonValueKind.Array)
                        position = subTracks.EnumerateArray().Select(s => Str(s, "position")).FirstOrDefault(p => p != null) ?? "";

                    int partDisc, partTrack;
                    ParsePosition(position, out partDisc, out partTrack);
                    if (partDisc > 0)
                    {
                        if (partDisc != disc)
                            last = 0;
                        disc = partDisc;
                        explicitDiscs = true;
                    }
                    else if (partTrack == 1 && last > 0 && !explicitDiscs && !SubTrackLetterAtEnd().IsMatch(position))
                    {
                        disc++;
                        last = 0;
                    }
                    int number = partTrack > 0 ? partTrack : last + 1;

                    string title = Str(part, "title");
                    ReleaseTrack existing = details.Tracks.FirstOrDefault(t => t.Disc == disc && t.Position == number);
                    if (existing != null)
                    {
                        // "3a" / "3b": parts of one CD track
                        existing.Title = existing.Title + " / " + title;
                        existing.Length += Duration(Str(part, "duration"));
                        continue;
                    }

                    details.Tracks.Add(new ReleaseTrack
                    {
                        Disc = disc,
                        Position = number,
                        Title = title,
                        Artist = Artists(part, "artists") ?? Artists(item, "artists") ?? details.Artist,
                        Length = Duration(Str(part, "duration")) > 0 ? Duration(Str(part, "duration")) : Duration(Str(item, "duration"))
                    });
                    last = number;
                }
            }
        }

        /// <summary>
        /// Fills what a CUETools DB Discogs entry lacks (label, catalog #, country, full date, genre, cover, format) from the
        /// full Discogs release; its tracks (with lengths from the disc's TOC) stay. Returns true when something was added.
        /// </summary>
        public static bool FillMissing(ReleaseDetails target, ReleaseDetails discogs)
        {
            if (target == null || discogs == null)
                return false;
            bool changed = false;
            Func<string, string, string> fill = (current, value) =>
            {
                if (!string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(value))
                    return current;
                changed = true;
                return value;
            };
            target.Label = fill(target.Label, discogs.Label);
            target.CatalogNumber = fill(target.CatalogNumber, discogs.CatalogNumber);
            target.Country = fill(target.Country, discogs.Country);
            target.Genre = fill(target.Genre, discogs.Genre);
            target.CoverUrl = fill(target.CoverUrl, discogs.CoverUrl);
            if (discogs.Date != null && (target.Date == null || (discogs.Date.StartsWith(target.Date, StringComparison.Ordinal) && discogs.Date.Length > target.Date.Length)))
            {
                target.Date = discogs.Date;
                changed = true;
            }
            if (discogs.Format != null && !string.Equals(target.Format, discogs.Format, StringComparison.OrdinalIgnoreCase))
            {
                target.Format = discogs.Format;
                changed = true;
            }
            return changed;
        }

        public static void ParsePosition(string position, out int disc, out int track)
        {
            disc = 0;
            track = 0;
            string p = (position ?? "").Trim();
            Match match = DiscAndTrackPosition().Match(p);
            if (match.Success)
            {
                disc = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                track = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                return;
            }
            match = TrackPosition().Match(p);
            if (match.Success)
                track = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>The first style or genre that is an ID3 genre ("Techno", "Rock"), else the first genre.</summary>
        private static string PickGenre(List<string> styles, List<string> genres)
        {
            foreach (string name in styles.Concat(genres))
            {
                string known = IdSharp.Tagging.ID3v1.GenreHelper.GenreByIndex.FirstOrDefault(g => string.Equals(g, name, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(known))
                    return known;
            }
            return genres.FirstOrDefault();
        }

        private static string FormatName(string name)
        {
            if (name == null)
                return null;
            if (name.Equals("File", StringComparison.OrdinalIgnoreCase))
                return "Digital Media";
            return name;
        }

        /// <summary>"1994-05-16", "1994-05-00", "1994-00-00" as "1994-05-16", "1994-05", "1994".</summary>
        private static string Date(string released)
        {
            if (string.IsNullOrWhiteSpace(released))
                return null;
            string[] parts = released.Trim().Split('-');
            if (parts[0].Length != 4 || parts[0] == "0000")
                return null;
            var kept = new List<string> { parts[0] };
            for (int i = 1; i < parts.Length && parts[i] != "00" && parts[i] != "0"; i++)
                kept.Add(parts[i]);
            return string.Join("-", kept);
        }

        private static int Duration(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;
            int seconds = 0;
            foreach (string part in text.Trim().Split(':'))
            {
                int value;
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    return 0;
                seconds = seconds * 60 + value;
            }
            return seconds;
        }

        /// <summary>"Artist A &amp; Artist B" from a Discogs artists array (name variations and "(2)" numbers handled).</summary>
        private static string Artists(JsonElement element, string property)
        {
            JsonElement artists;
            if (!element.TryGetProperty(property, out artists) || artists.ValueKind != JsonValueKind.Array)
                return null;
            var list = artists.EnumerateArray().ToList();
            if (list.Count == 0)
                return null;
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                string name = CleanArtist(Str(list[i], "anv") ?? Str(list[i], "name"));
                if (string.Equals(name, "Various", StringComparison.OrdinalIgnoreCase))
                    name = "Various Artists";
                text.Append(name);
                if (i < list.Count - 1)
                {
                    string join = (Str(list[i], "join") ?? ",").Trim();
                    text.Append(join == "," || join.Length == 0 ? ", " : " " + join + " ");
                }
            }
            return text.ToString().Trim();
        }

        private static string CleanArtist(string name)
        {
            return name == null ? null : ArtistNumber().Replace(name.Trim(), "").TrimEnd('*');
        }

        private static string Str(JsonElement element, string property)
        {
            JsonElement value;
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out value))
                return null;
            string text = value.ValueKind == JsonValueKind.String ? value.GetString()
                        : value.ValueKind == JsonValueKind.Number ? value.GetRawText()
                        : null;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static int Int(JsonElement element, string property)
        {
            int value;
            return int.TryParse(Str(element, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private static List<string> Strings(JsonElement element, string property)
        {
            JsonElement value;
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out value) || value.ValueKind != JsonValueKind.Array)
                return new List<string>();
            return value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString().Trim()).Where(v => v.Length > 0).ToList();
        }

        [GeneratedRegex(@"discogs\.com/(?:[^/\s]+/)*release/(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex ReleaseLink();

        [GeneratedRegex(@"\s+\(\d+\)$", RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex ArtistNumber();

        [GeneratedRegex(@"^(?:(?:CD|DVD|Dis[ck])\s*)?(\d+)\s*[-.:]\s*(\d+)[a-z]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex DiscAndTrackPosition();

        [GeneratedRegex(@"^\s*(?:(?:CD|DVD|Dis[ck])?\s*\d+\s*[-.]\s*)?\d+\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex NumberedPosition();

        [GeneratedRegex(@"^\s*\[?r(\d+)\]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex ReleaseCode();

        [GeneratedRegex("[b-z]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex SubTrackLetterAtEnd();

        [GeneratedRegex(@"^(\d+)[a-z]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex TrackPosition();
    }
}

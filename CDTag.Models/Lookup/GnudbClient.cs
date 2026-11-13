using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CDTag.Model.Lookup
{
    /// <summary>
    /// gnudb.org, the freedb successor (CDDB protocol over HTTP). gnudb only answers registered programs, identified by the
    /// address their user registered: without <see cref="Email"/> nothing is looked up.
    /// </summary>
    public class GnudbClient
    {
        private const string Url = "https://gnudb.gnudb.org/~cddb/cddb.cgi";
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(MusicBrainzClient.UserAgent);
            return client;
        }

        public GnudbClient(string email)
        {
            Email = (email ?? "").Trim();
        }

        public string Email { get; private set; }
        public bool IsEnabled { get { return Email.Contains('@'); } }

        /// <summary>The freedb disc ID of all tracks (data tracks included, as freedb counts them).</summary>
        public static uint DiscId(DiscToc toc)
        {
            int n = 0;
            foreach (int s in toc.Offsets)
            {
                for (int t = (s + 150) / 75; t > 0; t /= 10)
                    n += t % 10;
            }
            int total = (toc.LeadOut + 150) / 75 - (toc.Offsets[0] + 150) / 75;
            return (uint)(((n % 0xFF) << 24) | (total << 8) | toc.Offsets.Count);
        }

        public async Task<List<ReleaseDetails>> LookupAsync(DiscToc toc, CancellationToken cancellation = default(CancellationToken))
        {
            var results = new List<ReleaseDetails>();
            if (!IsEnabled || toc == null || toc.Offsets.Count == 0)
                return results;

            string query = string.Format(CultureInfo.InvariantCulture, "cddb query {0:x8} {1} {2} {3}", DiscId(toc), toc.Offsets.Count,
                                         string.Join(" ", toc.Offsets.Select(o => (o + 150).ToString(CultureInfo.InvariantCulture))), (toc.LeadOut + 150) / 75);
            string answer = await CommandAsync(query, cancellation).ConfigureAwait(false);
            var matches = new List<Tuple<string, string>>();   // category, disc id
            string[] lines = answer.Replace("\r", "").Split('\n');
            if (lines[0].StartsWith("200 ", StringComparison.Ordinal))
            {
                string[] p = lines[0].Split(' ');
                if (p.Length >= 3) matches.Add(Tuple.Create(p[1], p[2]));
            }
            else if (lines[0].StartsWith("210", StringComparison.Ordinal) || lines[0].StartsWith("211", StringComparison.Ordinal))
            {
                foreach (string line in lines.Skip(1).TakeWhile(l => l != "."))
                {
                    string[] p = line.Split(' ');
                    if (p.Length >= 2) matches.Add(Tuple.Create(p[0], p[1]));
                }
            }

            foreach (var m in matches.Take(5))
            {
                string entry = await CommandAsync("cddb read " + m.Item1 + " " + m.Item2, cancellation).ConfigureAwait(false);
                ReleaseDetails r = ParseEntry(entry, toc);
                if (r != null)
                    results.Add(r);
            }
            return results;
        }

        private async Task<string> CommandAsync(string command, CancellationToken cancellation)
        {
            string user = Uri.EscapeDataString(Email.Split('@')[0]);
            string host = Uri.EscapeDataString(Email.Contains('@') ? Email.Substring(Email.IndexOf('@') + 1) : "localhost");
            string url = string.Format(CultureInfo.InvariantCulture, "{0}?cmd={1}&hello={2}+{3}+CD-Tag+3&proto=6", Url, command.Replace(' ', '+'), user, host);
            byte[] data = await Http.GetByteArrayAsync(url, cancellation).ConfigureAwait(false);
            return Encoding.UTF8.GetString(data);
        }

        /// <summary>An xmcd entry: DTITLE=Artist / Album, DYEAR, DGENRE, TTITLEn=Title (or Artist / Title); lines may repeat.</summary>
        public static ReleaseDetails ParseEntry(string text, DiscToc toc)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith("210", StringComparison.Ordinal))
                return null;
            var fields = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith('#') || !raw.Contains('='))
                    continue;
                int eq = raw.IndexOf('=');
                string key = raw.Substring(0, eq);
                StringBuilder sb;
                if (!fields.TryGetValue(key, out sb))
                    fields[key] = sb = new StringBuilder();
                sb.Append(raw.AsSpan(eq + 1));
            }
            Func<string, string> get = k => fields.TryGetValue(k, out StringBuilder field) ? field.ToString().Trim() : "";

            string dtitle = get("DTITLE");
            int slash = dtitle.IndexOf(" / ", StringComparison.Ordinal);
            string artist = slash >= 0 ? dtitle.Substring(0, slash).Trim() : dtitle;
            string album = slash >= 0 ? dtitle.Substring(slash + 3).Trim() : dtitle;
            bool various = artist.Equals("Various", StringComparison.OrdinalIgnoreCase) || artist.Equals("Various Artists", StringComparison.OrdinalIgnoreCase);
            var r = new ReleaseDetails
            {
                Artist = various ? "Various Artists" : artist, Title = album, Date = get("DYEAR"), Genre = get("DGENRE"),
                Source = "gnudb", Discs = 1, Format = "CD", IsVariousArtists = various, Tracks = new List<ReleaseTrack>()
            };
            int audio = toc.AudioTrackCount;
            for (int i = 0; i < audio; i++)
            {
                string t = get("TTITLE" + i.ToString(CultureInfo.InvariantCulture));
                string trackArtist = null;
                int s = t.IndexOf(" / ", StringComparison.Ordinal);
                if (s >= 0)
                {
                    trackArtist = t.Substring(0, s).Trim();
                    t = t.Substring(s + 3).Trim();
                    r.IsVariousArtists = true;
                }
                r.Tracks.Add(new ReleaseTrack { Disc = 1, Position = i + 1, Title = t, Artist = trackArtist ?? r.Artist, Length = toc.TrackSeconds(i) });
            }
            if (r.IsVariousArtists) r.Artist = various ? "Various Artists" : r.Artist;
            return r;
        }
    }
}

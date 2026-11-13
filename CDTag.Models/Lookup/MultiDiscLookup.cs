using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CDTag.Model.Lookup
{
    /// <summary>
    /// Combines the disc-ID lookups of every disc of an album (CD1, CD2, CD3...) into whole-album results.
    /// </summary>
    public static partial class MultiDiscLookup
    {
        /// <summary>"Album (Disc 2)", "Album CD2", "Album - CD 2/3" as "Album".</summary>
        public static string StripDiscNumber(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return title;
            string stripped = DiscSuffix().Replace(title, "").Trim();
            return stripped.Length > 0 ? stripped : title.Trim();
        }

        /// <summary>
        /// CUETools DB data is per disc: the entries for the same release on each disc (same MusicBrainz release, or the
        /// same source, artist and album title) are joined into one release with every disc's tracks.
        /// Releases found for every disc are listed first. <paramref name="perDisc"/> holds the results of each TOC in
        /// <paramref name="tocs"/>, in the same order.
        /// </summary>
        public static List<ReleaseDetails> CombineCtdb(IList<DiscToc> tocs, IList<List<ReleaseDetails>> perDisc)
        {
            var groups = new List<KeyValuePair<string, List<ReleaseDetails>>>();
            var byKey = new Dictionary<string, List<ReleaseDetails>>(StringComparer.Ordinal);
            for (int i = 0; i < perDisc.Count; i++)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (ReleaseDetails release in perDisc[i] ?? new List<ReleaseDetails>())
                {
                    string key = CtdbKey(release);
                    if (!seen.Add(key))
                        continue; // the best entry of this disc only
                    List<ReleaseDetails> parts;
                    if (!byKey.TryGetValue(key, out parts))
                    {
                        parts = new List<ReleaseDetails>();
                        byKey[key] = parts;
                        groups.Add(new KeyValuePair<string, List<ReleaseDetails>>(key, parts));
                    }
                    parts.Add(release);
                }
            }

            int discs = tocs.Count;
            return groups
                .Select((g, index) => new { Release = Join(g.Key, g.Value, discs), Discs = g.Value.Count, Index = index })
                .OrderByDescending(r => r.Discs)
                .ThenBy(r => r.Index)
                .Select(r => r.Release)
                .ToList();
        }

        /// <summary>
        /// A MusicBrainz release holds every disc; the releases found by the most discs (all, when the TOCs are right)
        /// come first, each listed once.
        /// </summary>
        public static List<ReleaseDetails> CombineMusicBrainz(IList<List<ReleaseDetails>> perDisc)
        {
            int all;
            return CombineMusicBrainz(perDisc, out all);
        }

        /// <summary><paramref name="foundByAll"/>: how many releases every disc was found in.</summary>
        public static List<ReleaseDetails> CombineMusicBrainz(IList<List<ReleaseDetails>> perDisc, out int foundByAll)
        {
            var found = new List<Tuple<ReleaseDetails, int, int>>();
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (List<ReleaseDetails> disc in perDisc)
            {
                foreach (ReleaseDetails release in (disc ?? new List<ReleaseDetails>()).GroupBy(r => r.Id).Select(g => g.First()))
                {
                    int at;
                    if (index.TryGetValue(release.Id, out at))
                    {
                        found[at] = Tuple.Create(found[at].Item1, found[at].Item2 + 1, found[at].Item3);
                    }
                    else
                    {
                        index[release.Id] = found.Count;
                        found.Add(Tuple.Create(release, 1, found.Count));
                    }
                }
            }
            foundByAll = found.Count(f => f.Item2 == perDisc.Count);
            return found.OrderByDescending(f => f.Item2).ThenBy(f => f.Item3).Select(f => f.Item1).ToList();
        }

        /// <summary>How many discs of a CUETools DB result have tracks (a combined result covers several).</summary>
        public static int DiscsFound(ReleaseDetails release)
        {
            return release == null || release.Tracks == null ? 0 : release.Tracks.Select(t => t.Disc).Distinct().Count();
        }

        private static string CtdbKey(ReleaseDetails release)
        {
            if (!string.IsNullOrEmpty(release.MusicBrainzId))
                return "mb|" + release.MusicBrainzId.ToLowerInvariant();
            if (!string.IsNullOrEmpty(release.DiscogsId))
                return "discogs|" + release.DiscogsId;
            return (release.Source ?? "") + "|" + Normalize(release.Artist) + "|" + Normalize(StripDiscNumber(release.Title));
        }

        private static string Normalize(string text)
        {
            return new string((text ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static ReleaseDetails Join(string key, List<ReleaseDetails> parts, int albumDiscs)
        {
            if (parts.Count == 1)
            {
                // Found for one disc only: stays a one-disc result. Ids are only unique per disc.
                parts[0].Id = "disc" + parts[0].OnlyDisc + ":" + parts[0].Id;
                return parts[0];
            }

            ReleaseDetails first = parts[0];
            var joined = new ReleaseDetails
            {
                Id = "all:" + key,
                Source = first.Source,
                Artist = first.Artist,
                Title = StripDiscNumber(first.Title),
                Date = parts.Select(p => p.Date).FirstOrDefault(v => v != null),
                Country = parts.Select(p => p.Country).FirstOrDefault(v => v != null),
                Label = parts.Select(p => p.Label).FirstOrDefault(v => v != null),
                CatalogNumber = parts.Select(p => p.CatalogNumber).FirstOrDefault(v => v != null),
                Genre = parts.Select(p => p.Genre).FirstOrDefault(v => v != null),
                Format = first.Format,
                Discs = Math.Max(albumDiscs, parts.Max(p => p.Discs)),
                MusicBrainzId = first.MusicBrainzId,
                DiscogsId = parts.Select(p => p.DiscogsId).FirstOrDefault(v => v != null),
                CoverUrl = parts.Select(p => p.CoverUrl).FirstOrDefault(v => v != null),
                OnlyDisc = 0,
                Tracks = parts.SelectMany(p => p.Tracks).OrderBy(t => t.Disc).ThenBy(t => t.Position).ToList()
            };
            foreach (ReleaseDetails part in parts)
            {
                foreach (KeyValuePair<int, string> name in part.DiscNames)
                    joined.DiscNames[name.Key] = name.Value;
            }
            joined.IsVariousArtists = parts.Any(p => p.IsVariousArtists);
            return joined;
        }

        [GeneratedRegex(@"\s*(?:[-:,/]\s*)?[\(\[]?\s*(?:cd|disc|disk)\s*[-_#.]?\s*\d{1,2}(?:\s*(?:of|/)\s*\d{1,2})?\s*[\)\]]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex DiscSuffix();
    }
}

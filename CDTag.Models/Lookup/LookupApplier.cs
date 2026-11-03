using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CDTag.Model.Profile;
using CDTag.Model.Tag;

namespace CDTag.Model.Lookup
{
    /// <summary>Copies a looked-up release into Tag Album's album, tracks and release information.</summary>
    public static class LookupApplier
    {
        /// <summary>
        /// Applies <paramref name="details"/>. Tracks are matched by disc and track number; if that fails for any
        /// track but the track counts are equal, they are matched in order (and renumbered).
        /// When the release covers one disc only (<see cref="ReleaseDetails.OnlyDisc"/>), just that disc's tracks are changed.
        /// Returns how many tracks got a title.
        /// </summary>
        public static int Apply(ReleaseDetails details, Album album, ReleaseInfo release, ReleaseListItem[] sources)
        {
            if (details == null)
                throw new ArgumentNullException("details");
            if (album == null)
                throw new ArgumentNullException("album");

            album.IsVariousArtists = details.IsVariousArtists;
            album.Artist = details.IsVariousArtists && string.Equals(details.Artist, "Various Artists", StringComparison.OrdinalIgnoreCase)
                ? "Various Artists"
                : Plain(details.Artist);
            album.AlbumTitle = Plain(details.Title);
            if (!string.IsNullOrWhiteSpace(details.Genre))
                album.Genre = details.Genre;

            if (release != null)
            {
                string date = ToRetailDate(details.Date);
                if (date != null)
                    release.RetailDate = date;
                if (!string.IsNullOrWhiteSpace(details.Label))
                    release.Label = details.Label;
                if (!string.IsNullOrWhiteSpace(details.CatalogNumber))
                    release.CatalogNumber = details.CatalogNumber;
                ReleaseListItem source = MatchSource(details.Format, sources);
                if (source != null)
                    release.Source = source;
            }

            List<AlbumTrack> tracks = album.Tracks
                .Where(t => details.OnlyDisc <= 0 || Number(t.DiscNumber, 1) == details.OnlyDisc)
                .OrderBy(t => Number(t.DiscNumber, 1)).ThenBy(t => Number(t.TrackNumber, 0)).ThenBy(t => t.OriginalFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var byPosition = details.Tracks.GroupBy(t => Tuple.Create(t.Disc, t.Position)).ToDictionary(g => g.Key, g => g.First());

            bool allMatch = tracks.All(t => byPosition.ContainsKey(Tuple.Create(Number(t.DiscNumber, 1), Number(t.TrackNumber, 0))));
            bool inOrder = !allMatch && tracks.Count == details.Tracks.Count;

            int applied = 0;
            for (int i = 0; i < tracks.Count; i++)
            {
                AlbumTrack track = tracks[i];
                ReleaseTrack source;
                if (inOrder)
                {
                    source = details.Tracks[i];
                    track.DiscNumber = source.Disc.ToString(CultureInfo.InvariantCulture);
                    track.TrackNumber = source.Position.ToString(CultureInfo.InvariantCulture);
                }
                else if (!byPosition.TryGetValue(Tuple.Create(Number(track.DiscNumber, 1), Number(track.TrackNumber, 0)), out source))
                {
                    continue;
                }

                track.Title = Plain(source.Title);
                track.Artist = details.IsVariousArtists ? Plain(source.Artist) : album.Artist;
                track.Album = album.AlbumTitle;
                applied++;
            }
            return applied;
        }

        /// <summary>"2026-10-14" / "2026-10" / "2026" as yyyy-MM-dd with 00 for unknown parts.</summary>
        public static string ToRetailDate(string date)
        {
            if (string.IsNullOrWhiteSpace(date))
                return null;
            string[] parts = date.Trim().Split('-');
            if (parts[0].Length != 4)
                return null;
            string month = parts.Length > 1 ? parts[1].PadLeft(2, '0') : "00";
            string day = parts.Length > 2 ? parts[2].PadLeft(2, '0') : "00";
            return parts[0] + "-" + month + "-" + day;
        }

        /// <summary>Typographic quotes and dashes from MusicBrainz as plain ASCII (as usual in tags and NFOs).</summary>
        public static string Plain(string text)
        {
            if (text == null)
                return null;
            return text.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"')
                       .Replace('‐', '-').Replace('‑', '-').Replace('–', '-').Replace('—', '-').Replace('…', '.');
        }

        private static ReleaseListItem MatchSource(string format, ReleaseListItem[] sources)
        {
            if (string.IsNullOrWhiteSpace(format) || sources == null || sources.Length == 0)
                return null;

            string wanted;
            string f = format.ToUpperInvariant();
            if (f.Contains("VINYL"))
                wanted = "VINYL";
            else if (f.Contains("DIGITAL"))
                wanted = "WEB";
            else if (f.Contains("SACD"))
                wanted = "SACD";
            else if (f.Contains("BLU-RAY"))
                wanted = "BLURAY";
            else if (f.Contains("DVD"))
                wanted = "DVD";
            else if (f.Contains("CD"))
                wanted = "CD";
            else
                return null;

            return sources.FirstOrDefault(s => string.Equals(s.Nfo, wanted, StringComparison.OrdinalIgnoreCase))
                ?? sources.FirstOrDefault(s => (s.Nfo ?? "").StartsWith(wanted, StringComparison.OrdinalIgnoreCase));
        }

        private static int Number(string text, int fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
                return fallback;
            var match = System.Text.RegularExpressions.Regex.Match(text, @"\d+");
            int number;
            return match.Success && int.TryParse(match.Value, out number) ? number : fallback;
        }
    }
}

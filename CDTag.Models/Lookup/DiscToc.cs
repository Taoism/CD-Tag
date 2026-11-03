using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CDTag.Model.Tag;

namespace CDTag.Model.Lookup
{
    /// <summary>
    /// The table of contents of one CD (track start sectors and lead-out), as used by disc-ID lookups
    /// (CUETools DB, MusicBrainz). Read from the rip log (EAC, XLD, CUERipper) or rebuilt from exact FLAC lengths.
    /// </summary>
    public class DiscToc
    {
        /// <summary>CD sectors (frames) per second.</summary>
        public const int SectorsPerSecond = 75;
        public const int SamplesPerSector = 588;
        /// <summary>Sectors between the last audio track and a data track of an enhanced CD.</summary>
        private const int DataTrackGap = 11400;

        public DiscToc()
        {
            Offsets = new List<int>();
            IsAudio = new List<bool>();
        }

        /// <summary>Disc number in the album (1-based).</summary>
        public int Disc { get; set; }

        /// <summary>Start sector of each track (track 1 usually 0).</summary>
        public List<int> Offsets { get; private set; }

        /// <summary>False for a data track (enhanced CD).</summary>
        public List<bool> IsAudio { get; private set; }

        /// <summary>Sector after the last track.</summary>
        public int LeadOut { get; set; }

        /// <summary>Where the TOC came from, e.g. "EAC log" or "FLAC lengths".</summary>
        public string Source { get; set; }

        public int AudioTrackCount
        {
            get { return IsAudio.Count(a => a); }
        }

        /// <summary>Last sector of the audio session (the lead-out MusicBrainz uses for enhanced CDs).</summary>
        private int AudioLeadOut
        {
            get
            {
                int lastAudio = IsAudio.LastIndexOf(true);
                return lastAudio == IsAudio.Count - 1 ? LeadOut : Offsets[lastAudio + 1] - DataTrackGap;
            }
        }

        /// <summary>Exact length of audio track <paramref name="index"/> (0-based) in seconds.</summary>
        public decimal TrackLength(int index)
        {
            int end = index + 1 < Offsets.Count ? Offsets[index + 1] : LeadOut;
            if (index + 1 < Offsets.Count && !IsAudio[index + 1])
                end = AudioLeadOut;
            return (end - Offsets[index]) / (decimal)SectorsPerSecond;
        }

        /// <summary>
        /// MP3 rips with a log: each track gets its exact CD length from the log's TOC (MP3 lengths are estimates).
        /// Only discs whose log has as many audio tracks as the disc are changed. Returns how many tracks changed.
        /// </summary>
        public static int ApplyLogLengths(Album album, IEnumerable<DiscToc> tocs)
        {
            if (album == null || tocs == null)
                return 0;
            int changed = 0;
            foreach (DiscToc toc in tocs.Where(t => t.Source != null && t.Source.EndsWith("log", StringComparison.OrdinalIgnoreCase)))
            {
                List<AlbumTrack> tracks = album.Tracks.Where(t => Number(t.DiscNumber, 1) == toc.Disc)
                    .OrderBy(t => Number(t.TrackNumber, 0)).ThenBy(t => t.OriginalFileName, StringComparer.OrdinalIgnoreCase).ToList();
                if (tracks.Count != toc.AudioTrackCount || tracks.Any(t => t.IsFlac))
                    continue;
                for (int i = 0; i < tracks.Count; i++)
                {
                    tracks[i].SetCdLength(toc.TrackLength(i));
                    changed++;
                }
            }
            return changed;
        }

        /// <summary>Length of audio track <paramref name="index"/> (0-based) in seconds.</summary>
        public int TrackSeconds(int index)
        {
            int end = index + 1 < Offsets.Count ? Offsets[index + 1] : LeadOut;
            if (index + 1 < Offsets.Count && !IsAudio[index + 1])
                end = AudioLeadOut;
            return (int)Math.Round((end - Offsets[index]) / (double)SectorsPerSecond);
        }

        /// <summary>CUETools DB form: "0:22617:...:267075"; data tracks are prefixed with '-'.</summary>
        public string CtdbToc
        {
            get
            {
                var parts = Offsets.Select((o, i) => (IsAudio[i] ? "" : "-") + o.ToString(CultureInfo.InvariantCulture));
                return string.Join(":", parts) + ":" + LeadOut.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>MusicBrainz form: "1+12+267225+150+22767+..." (audio tracks, +150 sector lead-in).</summary>
        public string MusicBrainzToc
        {
            get
            {
                int count = AudioTrackCount;
                var values = new List<int> { 1, count, AudioLeadOut + 150 };
                values.AddRange(Offsets.Where((o, i) => IsAudio[i]).Select(o => o + 150));
                return string.Join("+", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>The MusicBrainz disc ID (SHA-1 of the TOC, base64 with "._-").</summary>
        public string MusicBrainzDiscId
        {
            get
            {
                var audio = Offsets.Where((o, i) => IsAudio[i]).ToList();
                var sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}{1:X2}{2:X8}", 1, audio.Count, AudioLeadOut + 150);
                for (int i = 0; i < 99; i++)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0:X8}", i < audio.Count ? audio[i] + 150 : 0);
                byte[] hash = SHA1.HashData(Encoding.ASCII.GetBytes(sb.ToString()));
                return Convert.ToBase64String(hash).Replace('+', '.').Replace('/', '_').Replace('=', '-');
            }
        }

        public override string ToString()
        {
            return string.Format("Disc {0}: {1} tracks ({2})", Disc, AudioTrackCount, Source);
        }

        // ------------------------------------------------------------------ rip logs

        private static readonly Regex TocRow = new Regex(
            @"^\s*(\d{1,2})\s*\|\s*[\d:.]+\s*\|\s*[\d:.]+\s*\|\s*(\d+)\s*\|\s*(\d+)\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>Reads the first TOC table of an EAC / XLD / CUERipper log; null when there is none.</summary>
        public static DiscToc FromLog(string text, string source = "rip log")
        {
            if (string.IsNullOrEmpty(text))
                return null;

            var toc = new DiscToc { Source = source };
            int expected = 1;
            int lastEnd = -1;
            foreach (Match row in TocRow.Matches(text))
            {
                int track = int.Parse(row.Groups[1].Value, CultureInfo.InvariantCulture);
                if (track != expected)
                {
                    if (toc.Offsets.Count > 0)
                        break; // a second table (log of another rip)
                    if (track != 1)
                        continue;
                }
                int start = int.Parse(row.Groups[2].Value, CultureInfo.InvariantCulture);
                int end = int.Parse(row.Groups[3].Value, CultureInfo.InvariantCulture);

                // Enhanced CD: the data track starts 11400 sectors after the audio session.
                bool isData = lastEnd >= 0 && start - (lastEnd + 1) == DataTrackGap;
                toc.Offsets.Add(start);
                toc.IsAudio.Add(!isData);
                lastEnd = end;
                expected = track + 1;
            }

            if (toc.Offsets.Count == 0)
                return null;
            toc.LeadOut = lastEnd + 1;
            return toc;
        }

        /// <summary>Reads a log file; EAC writes UTF-16, others UTF-8 or ANSI.</summary>
        public static DiscToc FromLogFile(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                string text;
                if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                    text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
                else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                    text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
                else
                    text = Encoding.UTF8.GetString(bytes);

                string name = text.Contains("Exact Audio Copy") ? "EAC log"
                            : text.Contains("X Lossless Decoder") ? "XLD log"
                            : text.Contains("CUERipper") ? "CUERipper log"
                            : "rip log";
                return FromLog(text, name);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ FLAC lengths

        /// <summary>
        /// Rebuilds the TOC from the exact sample counts of a disc's FLAC tracks (CD rips are whole sectors).
        /// Track 1 is assumed to start at sector 0 (lookups are fuzzy, so pressings with an offset still match).
        /// Null when a file is not CD audio.
        /// </summary>
        public static DiscToc FromFlacFiles(IList<string> files)
        {
            if (files == null || files.Count == 0)
                return null;

            var toc = new DiscToc { Source = "FLAC lengths" };
            long position = 0;
            foreach (string file in files)
            {
                FlacStreamInfo info = FlacStreamInfo.Read(file);
                if (info == null || info.SampleRate != 44100 || info.Channels != 2 || info.TotalSamples <= 0 ||
                    info.TotalSamples % SamplesPerSector != 0)
                {
                    return null;
                }
                toc.Offsets.Add((int)position);
                toc.IsAudio.Add(true);
                position += info.TotalSamples / SamplesPerSector;
            }
            toc.LeadOut = (int)position;
            return toc;
        }

        // ------------------------------------------------------------------ album

        private static readonly Regex DiscInName = new Regex(@"(?:cd|disc|disk)\s*[-_#.]?\s*(\d{1,2})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// One TOC per disc of <paramref name="album"/>: from the rip logs in the album folder (and disc subfolders),
        /// otherwise from FLAC lengths. Discs without either are left out.
        /// </summary>
        public static List<DiscToc> ForAlbum(Album album)
        {
            var result = new List<DiscToc>();
            if (album == null)
                return result;

            var discs = album.Tracks
                .GroupBy(t => Number(t.DiscNumber, 1))
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.OrderBy(t => Number(t.TrackNumber, 0)).ThenBy(t => t.OriginalFileName, StringComparer.OrdinalIgnoreCase).ToList());

            // Logs
            List<string> logs = FindLogs(album.OldDirectoryName);
            var parsed = logs.Select(l => new { Path = l, Toc = FromLogFile(l) }).Where(l => l.Toc != null).ToList();
            var unnumbered = new List<DiscToc>();
            foreach (var log in parsed)
            {
                int disc = DiscNumberOf(log.Path, album.OldDirectoryName);
                if (disc > 0 && discs.ContainsKey(disc) && result.All(r => r.Disc != disc))
                {
                    log.Toc.Disc = disc;
                    result.Add(log.Toc);
                }
                else if (disc == 0)
                {
                    unnumbered.Add(log.Toc);
                }
            }
            // Logs without a disc number: match by track count, in disc order.
            foreach (DiscToc toc in unnumbered)
            {
                int disc = discs.Keys.FirstOrDefault(d => result.All(r => r.Disc != d) && discs[d].Count == toc.AudioTrackCount);
                if (disc == 0 && discs.Count == 1 && result.Count == 0)
                    disc = discs.Keys.First();
                if (disc > 0)
                {
                    toc.Disc = disc;
                    result.Add(toc);
                }
            }

            // FLAC lengths for the rest
            foreach (KeyValuePair<int, List<AlbumTrack>> disc in discs)
            {
                if (result.Any(r => r.Disc == disc.Key) || !disc.Value.All(t => t.IsFlac))
                    continue;
                DiscToc toc = FromFlacFiles(disc.Value.Select(t => t.OriginalFileName).ToList());
                if (toc != null)
                {
                    toc.Disc = disc.Key;
                    result.Add(toc);
                }
            }

            return result.OrderBy(r => r.Disc).ToList();
        }

        private static List<string> FindLogs(string folder)
        {
            var logs = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return logs;
            try
            {
                logs.AddRange(Directory.EnumerateFiles(folder, "*.log"));
                foreach (string sub in Directory.EnumerateDirectories(folder))
                {
                    if (Album.IsDiscFolderName(Path.GetFileName(sub)))
                        logs.AddRange(Directory.EnumerateFiles(sub, "*.log"));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return logs.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Disc number from "CD2\x.log" or "x (Disc 2).log"; 0 when the name has none.</summary>
        private static int DiscNumberOf(string logPath, string albumFolder)
        {
            string folder = Path.GetDirectoryName(logPath);
            if (!string.Equals(folder?.TrimEnd('\\'), albumFolder?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                Match number = Regex.Match(Path.GetFileName(folder) ?? "", @"(\d+)");
                if (number.Success)
                    return int.Parse(number.Value, CultureInfo.InvariantCulture);
            }
            Match match = DiscInName.Match(Path.GetFileNameWithoutExtension(logPath));
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        private static int Number(string text, int fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
                return fallback;
            Match match = Regex.Match(text, @"\d+");
            int number;
            return match.Success && int.TryParse(match.Value, out number) ? number : fallback;
        }
    }

    /// <summary>The FLAC STREAMINFO values needed for a TOC.</summary>
    public class FlacStreamInfo
    {
        public int SampleRate { get; private set; }
        public int Channels { get; private set; }
        public int BitsPerSample { get; private set; }
        public long TotalSamples { get; private set; }

        public static FlacStreamInfo Read(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var header = new byte[10];
                    if (stream.Read(header, 0, 10) < 10)
                        return null;

                    // Skip an ID3v2 tag some tools put in front.
                    if (header[0] == 'I' && header[1] == 'D' && header[2] == '3')
                    {
                        int size = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
                        stream.Position = 10 + size;
                        if (stream.Read(header, 0, 4) < 4)
                            return null;
                    }
                    else
                    {
                        stream.Position = 4;
                    }
                    if (header[0] != 'f' || header[1] != 'L' || header[2] != 'a' || header[3] != 'C')
                        return null;

                    var block = new byte[4 + 34];
                    if (stream.Read(block, 0, block.Length) < block.Length || (block[0] & 0x7F) != 0)
                        return null; // STREAMINFO is always the first block

                    byte[] s = block.Skip(4).ToArray();
                    return new FlacStreamInfo
                    {
                        SampleRate = (s[10] << 12) | (s[11] << 4) | (s[12] >> 4),
                        Channels = ((s[12] >> 1) & 0x07) + 1,
                        BitsPerSample = (((s[12] & 0x01) << 4) | (s[13] >> 4)) + 1,
                        TotalSamples = ((long)(s[13] & 0x0F) << 32) | ((long)s[14] << 24) | ((long)s[15] << 16) | ((long)s[16] << 8) | s[17]
                    };
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}

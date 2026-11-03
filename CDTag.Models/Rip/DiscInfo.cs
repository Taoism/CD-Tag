using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace CDTag.Model.Rip
{
    /// <summary>CD-Text block 0: titles and performers, index 0 = the album, 1..99 = tracks.</summary>
    public class CdText
    {
        public Dictionary<int, string> Titles { get; private set; } = new Dictionary<int, string>();
        public Dictionary<int, string> Performers { get; private set; } = new Dictionary<int, string>();

        public string AlbumTitle { get { return Get(Titles, 0); } }
        public string AlbumPerformer { get { return Get(Performers, 0); } }
        public string Title(int track) { return Get(Titles, track); }
        public string Performer(int track) { return Get(Performers, track); }
        public bool IsEmpty { get { return Titles.Values.All(string.IsNullOrWhiteSpace) && Performers.Values.All(string.IsNullOrWhiteSpace); } }

        private static string Get(Dictionary<int, string> d, int i)
        {
            string v;
            return d.TryGetValue(i, out v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        }

        /// <summary>
        /// Parses CD-Text packs (18 bytes: pack type, track, sequence, block/position, 12 text bytes, CRC). Text for consecutive
        /// tracks is NUL-separated and runs across packs; a tab means "same as the previous track". ISO-8859-1, block 0 only.
        /// </summary>
        public static CdText Parse(byte[] data, int offset, int count)
        {
            var result = new CdText();
            var text = new Dictionary<int, List<byte>>();   // pack type -> bytes in order
            var firstTrack = new Dictionary<int, int>();
            for (int o = offset; o + 18 <= offset + count; o += 18)
            {
                int type = data[o];
                int block = (data[o + 3] >> 4) & 0x07;
                bool unicode = (data[o + 3] & 0x80) != 0;
                if (block != 0 || unicode || (type != 0x80 && type != 0x81))
                    continue;
                List<byte> bytes;
                if (!text.TryGetValue(type, out bytes))
                {
                    text[type] = bytes = new List<byte>();
                    firstTrack[type] = data[o + 1] & 0x7F;
                }
                for (int i = 0; i < 12; i++)
                    bytes.Add(data[o + 4 + i]);
            }
            Encoding latin1 = Encoding.Latin1;
            foreach (var pair in text)
            {
                Dictionary<int, string> target = pair.Key == 0x80 ? result.Titles : result.Performers;
                int track = firstTrack[pair.Key];
                string previous = null;
                foreach (string part in latin1.GetString(pair.Value.ToArray()).Split('\0'))
                {
                    if (track > 99) break;
                    string value = part == "\t" ? previous : part;
                    if (!target.ContainsKey(track))
                        target[track] = value;
                    previous = value;
                    track++;
                }
            }
            return result.IsEmpty ? null : result;
        }
    }

    /// <summary>
    /// One sector's Q subchannel frame. ADR 1 = position (track and index), ADR 2 = the disc's catalog number,
    /// ADR 3 = the track's ISRC.
    /// </summary>
    public class QPosition
    {
        public int Adr { get; set; }
        public int Track { get; set; }
        /// <summary>Index (ADR 1 only; -1 otherwise, so a non-position frame never looks like a gap).</summary>
        public int Index { get; set; } = -1;
        public string Isrc { get; set; }
        public string CatalogNumber { get; set; }

        /// <summary>
        /// Q from raw P-W subcode (96 bytes from <paramref name="offset"/>, bit 6 of each byte is Q). Null when its CRC-16 is wrong.
        /// </summary>
        public static QPosition FromRawSubcode(byte[] buffer, int offset)
        {
            var q = new byte[12];
            for (int i = 0; i < 96; i++)
            {
                if ((buffer[offset + i] & 0x40) != 0)
                    q[i / 8] |= (byte)(0x80 >> (i % 8));
            }
            int crc = Crc16Ccitt(q, 0, 10) ^ 0xFFFF;
            if (crc != ((q[10] << 8) | q[11]))
                return null;
            int adr = q[0] & 0x0F;
            var p = new QPosition { Adr = adr };
            if (adr == 1)
            {
                p.Track = Bcd(q[1]);
                p.Index = Bcd(q[2]);
            }
            else if (adr == 2)
            {
                // 13 BCD digits in Q[1..7]
                var sb = new StringBuilder();
                for (int i = 0; i < 13; i++)
                    sb.Append((char)('0' + ((i % 2 == 0 ? q[1 + i / 2] >> 4 : q[1 + i / 2]) & 0x0F)));
                string mcn = sb.ToString();
                if (mcn.All(char.IsDigit) && mcn.Any(c => c != '0'))
                    p.CatalogNumber = mcn;
            }
            else if (adr == 3)
            {
                // ISRC: 5 six-bit characters (0-9 = digits, 17-42 = A-Z), 2 zero bits, then 7 BCD digits
                ulong bits = 0;
                for (int i = 1; i <= 8; i++)
                    bits = (bits << 8) | q[i];
                var sb = new StringBuilder();
                for (int c = 0; c < 5; c++)
                {
                    int code = (int)((bits >> (58 - c * 6)) & 0x3F);
                    sb.Append(code <= 9 ? (char)('0' + code) : code >= 17 && code <= 42 ? (char)('A' + code - 17) : '?');
                }
                for (int d = 0; d < 7; d++)
                    sb.Append((char)('0' + (int)((bits >> (28 - d * 4)) & 0x0F)));
                string isrc = sb.ToString();
                if (!isrc.Contains('?') && isrc.Substring(5).All(char.IsDigit))
                    p.Isrc = isrc;
            }
            return p;
        }

        private static int Bcd(byte b)
        {
            return (b >> 4) * 10 + (b & 0x0F);
        }

        private static int Crc16Ccitt(byte[] data, int offset, int count)
        {
            int crc = 0;
            for (int i = offset; i < offset + count; i++)
            {
                crc ^= data[i] << 8;
                for (int k = 0; k < 8; k++)
                    crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
            }
            return crc;
        }
    }

    /// <summary>What Find CD reads from the disc besides the TOC.</summary>
    public class DiscExtras
    {
        public string CatalogNumber { get; set; }
        public CdText CdText { get; set; }
        /// <summary>ISRC per track number.</summary>
        public Dictionary<int, string> Isrc { get; private set; } = new Dictionary<int, string>();
        /// <summary>Pregap (INDEX 00 to INDEX 01) in sectors per track number; tracks without a gap are left out.</summary>
        public Dictionary<int, int> Pregaps { get; private set; } = new Dictionary<int, int>();
        /// <summary>False when the drive gave no usable subchannel data (gaps unknown).</summary>
        public bool GapsDetected { get; set; }

        public int Pregap(int track)
        {
            int g;
            return Pregaps.TryGetValue(track, out g) ? g : 0;
        }
    }

    /// <summary>
    /// Finds each track's pregap (where INDEX 00 starts) from the Q subchannel, like EAC's gap detection: the sector before a
    /// track start is checked, and when it is INDEX 00 of that track, the start of the gap is found by binary search.
    /// Q data can be unreliable, so each sector is read up to 3 times and positions with a bad CRC are retried.
    /// </summary>
    public static class GapDetector
    {
        public static DiscExtras Read(CdDrive drive, List<CdTrack> toc, IProgress<string> progress, CancellationToken cancellation)
        {
            var extras = new DiscExtras();
            List<CdTrack> audio = toc.Where(t => t.IsAudio).ToList();
            progress?.Report("Reading CD-Text...");
            extras.CdText = drive.ReadCdText();
            if (audio.Count == 0)
                return extras;

            bool subchannel = drive.ReadQRange(audio[0].StartSector + 75, 1) != null;
            if (!subchannel)
            {
                // No raw subchannel: ask the drive (slow, a few seconds per track); gaps stay unknown.
                progress?.Report("Reading the catalog number and ISRCs...");
                extras.CatalogNumber = drive.ReadCatalogNumber();
                foreach (CdTrack t in audio)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string isrc = drive.ReadIsrc(t.Number);
                    if (isrc != null)
                        extras.Isrc[t.Number] = isrc;
                }
                return extras;
            }

            // ISRC (ADR 3) and catalog number (ADR 2) frames come at least once per 100 sectors: read 104 sectors
            // two seconds into every track.
            foreach (CdTrack t in audio)
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report(string.Format(CultureInfo.InvariantCulture, "Reading ISRCs: track {0} of {1}", t.Number, audio.Count));
                int from = t.StartSector + Math.Min(150, Math.Max(0, t.Sectors - 120));
                for (int k = 0; k < 4 && from + CdDrive.MaxSectorsPerRead <= t.EndSector; k++, from += CdDrive.MaxSectorsPerRead)
                {
                    QPosition[] frames = drive.ReadQRange(from, CdDrive.MaxSectorsPerRead);
                    if (frames == null) continue;
                    foreach (QPosition f in frames.Where(f => f != null))
                    {
                        if (f.Isrc != null && !extras.Isrc.ContainsKey(t.Number)) extras.Isrc[t.Number] = f.Isrc;
                        if (f.CatalogNumber != null && extras.CatalogNumber == null) extras.CatalogNumber = f.CatalogNumber;
                    }
                }
            }
            extras.GapsDetected = true;
            for (int i = 1; i < audio.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report(string.Format(CultureInfo.InvariantCulture, "Detecting gaps: track {0} of {1}", audio[i].Number, audio.Count));
                int gap = FindGap(drive, audio[i], audio[i - 1].StartSector);
                if (gap > 0)
                    extras.Pregaps[audio[i].Number] = gap;
            }
            return extras;
        }

        /// <summary>
        /// Walks back from the track start, 26 sectors per read, while the sectors are INDEX 00 of this track. Sectors with a bad
        /// Q CRC are skipped. Gaps longer than 2 minutes are not followed (they are very rare).
        /// </summary>
        private static int FindGap(CdDrive drive, CdTrack track, int previousStart)
        {
            int gapStart = track.StartSector;
            int limit = Math.Max(previousStart + 1, track.StartSector - 2 * 60 * 75);
            for (int end = track.StartSector; end > limit; end -= CdDrive.MaxSectorsPerRead)
            {
                int first = Math.Max(limit, end - CdDrive.MaxSectorsPerRead);
                QPosition[] q = null;
                for (int attempt = 0; attempt < 3 && q == null; attempt++)
                    q = drive.ReadQRange(first, end - first);
                if (q == null)
                    break;
                for (int j = q.Length - 1; j >= 0; j--)
                {
                    if (q[j] == null || q[j].Adr != 1)
                        continue;   // bad CRC, or an ISRC / catalog number frame (no position)
                    if (q[j].Track == track.Number && q[j].Index == 0)
                        gapStart = first + j;
                    else
                        return track.StartSector - gapStart;   // reached the previous track's music
                }
            }
            return track.StartSector - gapStart;
        }

        private static QPosition Q(CdDrive drive, int lba)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                QPosition q = drive.ReadQ(lba);
                if (q != null)
                    return q;
            }
            return null;
        }

        public static string Time(int sectors)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:00}", sectors / 4500, sectors / 75 % 60, sectors % 75);
        }
    }
}

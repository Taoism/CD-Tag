using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CDTag.Model.Rip
{
    /// <summary>One pressing's results for a track in the AccurateRip database.</summary>
    public class AccurateRipEntry
    {
        public int Confidence { get; set; }
        public uint Crc { get; set; }
        /// <summary>CRC of the 450th frame (used to find the pressing offset; not checked here).</summary>
        public uint Frame450Crc { get; set; }
    }

    /// <summary>
    /// AccurateRip: disc IDs, the database lookup (dBAR files from accuraterip.com), the per-track CRCs (v1 and v2)
    /// and the drive read offset database (DriveOffsets.bin). Only audio tracks of the first session count.
    /// </summary>
    public static class AccurateRip
    {
        private const string Host = "https://www.accuraterip.com/accuraterip/";
        /// <summary>The first and last 5 sectors of the disc are not checked (drives can't read them exactly).</summary>
        public const int SkipSamples = 5 * CdDrive.SamplesPerSector;

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CD-Tag/3.2");
            return client;
        }

        // ------------------------------------------------------------------ disc IDs

        /// <summary>Start sectors of the audio tracks and the audio lead-out (LBA, track 1 usually 0).</summary>
        public static void AudioLayout(IList<CdTrack> toc, out List<int> starts, out int leadOut)
        {
            starts = toc.Where(t => t.IsAudio).Select(t => t.StartSector).ToList();
            leadOut = CdDrive.AudioLeadOut(toc);
        }

        public static uint Id1(IList<int> starts, int leadOut)
        {
            uint id = 0;
            foreach (int s in starts)
                id += (uint)s;
            return id + (uint)leadOut;
        }

        public static uint Id2(IList<int> starts, int leadOut)
        {
            uint id = 0;
            for (int i = 0; i < starts.Count; i++)
                id += (uint)Math.Max(starts[i], 1) * (uint)(i + 1);
            return id + (uint)leadOut * (uint)(starts.Count + 1);
        }

        /// <summary>The freedb disc ID (from the audio tracks), as AccurateRip uses it.</summary>
        public static uint FreedbId(IList<int> starts, int leadOut)
        {
            int n = 0;
            foreach (int s in starts)
            {
                int seconds = (s + 150) / 75;
                while (seconds > 0)
                {
                    n += seconds % 10;
                    seconds /= 10;
                }
            }
            int total = (leadOut + 150) / 75 - (starts[0] + 150) / 75;
            return (uint)(((n % 0xFF) << 24) | (total << 8) | starts.Count);
        }

        public static string DatabaseUrl(IList<int> starts, int leadOut)
        {
            uint id1 = Id1(starts, leadOut), id2 = Id2(starts, leadOut), cddb = FreedbId(starts, leadOut);
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:x}/{2:x}/{3:x}/dBAR-{4:000}-{5:x8}-{6:x8}-{7:x8}.bin",
                                 Host, id1 & 0xF, (id1 >> 4) & 0xF, (id1 >> 8) & 0xF, starts.Count, id1, id2, cddb);
        }

        /// <summary>
        /// Downloads the AccurateRip results: per track, one entry per pressing. Empty when the disc is not in the
        /// database (404); null when the database can't be reached.
        /// </summary>
        public static async Task<List<List<AccurateRipEntry>>> LookupAsync(IList<int> starts, int leadOut, CancellationToken cancellation = default(CancellationToken))
        {
            try
            {
                using (HttpResponseMessage response = await Http.GetAsync(DatabaseUrl(starts, leadOut), cancellation).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        return Enumerable.Range(0, starts.Count).Select(_ => new List<AccurateRipEntry>()).ToList();
                    if (!response.IsSuccessStatusCode)
                        return null;
                    byte[] data = await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
                    return Parse(data, starts.Count);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
            {
                return null;
            }
        }

        /// <summary>dBAR file: blocks of [track count, id1, id2, cddb id] followed by [confidence, CRC, frame 450 CRC] per track.</summary>
        public static List<List<AccurateRipEntry>> Parse(byte[] data, int trackCount)
        {
            var result = Enumerable.Range(0, trackCount).Select(_ => new List<AccurateRipEntry>()).ToList();
            int o = 0;
            while (o + 13 <= data.Length)
            {
                int count = data[o];
                o += 13;
                for (int t = 0; t < count && o + 9 <= data.Length; t++, o += 9)
                {
                    if (t >= trackCount || count != trackCount)
                        continue;
                    result[t].Add(new AccurateRipEntry
                    {
                        Confidence = data[o],
                        Crc = BitConverter.ToUInt32(data, o + 1),
                        Frame450Crc = BitConverter.ToUInt32(data, o + 5)
                    });
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ CRCs

        /// <summary>
        /// Running AccurateRip v1 and v2 CRC of one track. Feed the track's samples in order (16-bit stereo, little-endian bytes);
        /// the first track skips its first 5 sectors and the last track its last 5.
        /// </summary>
        public sealed class TrackCrc
        {
            private readonly long _checkFrom, _checkTo;
            private long _position;   // 1-based sample position within the track
            private uint _v1, _v2;

            public TrackCrc(long trackSamples, bool firstTrack, bool lastTrack)
            {
                _checkFrom = firstTrack ? SkipSamples : 1;
                _checkTo = lastTrack ? trackSamples - SkipSamples : trackSamples;
            }

            public uint V1 { get { return _v1; } }
            public uint V2 { get { return _v2; } }

            public void Add(byte[] buffer, int offset, int count)
            {
                for (int i = offset; i + 3 < offset + count; i += 4)
                {
                    _position++;
                    if (_position < _checkFrom || _position > _checkTo)
                        continue;
                    uint sample = (uint)(buffer[i] | (buffer[i + 1] << 8) | (buffer[i + 2] << 16) | (buffer[i + 3] << 24));
                    uint pos = (uint)_position;
                    _v1 += sample * pos;
                    ulong product = (ulong)sample * pos;
                    _v2 += (uint)(product & 0xFFFFFFFF) + (uint)(product >> 32);
                }
            }
        }

        /// <summary>"Accurately ripped (confidence 12) [ABCD1234] (AR v2)", or why not.</summary>
        public static string Describe(List<AccurateRipEntry> entries, uint v1, uint v2, out bool accurate, out int confidence)
        {
            accurate = false;
            confidence = 0;
            if (entries == null)
                return "AccurateRip not reachable";
            if (entries.Count == 0)
                return "Track not present in AccurateRip database";
            int c2 = entries.Where(e => e.Crc == v2).Sum(e => e.Confidence);
            int c1 = entries.Where(e => e.Crc == v1).Sum(e => e.Confidence);
            if (c2 > 0 || c1 > 0)
            {
                accurate = true;
                bool useV2 = c2 >= c1;
                confidence = useV2 ? c2 : c1;
                return string.Format(CultureInfo.InvariantCulture, "Accurately ripped (confidence {0})  [{1:X8}]  (AR {2})", confidence, useV2 ? v2 : v1, useV2 ? "v2" : "v1");
            }
            int total = entries.Max(e => e.Confidence);
            return string.Format(CultureInfo.InvariantCulture, "Cannot be verified as accurate (confidence {0})  [{1:X8}], AccurateRip returned [{2:X8}]",
                                 total, v2, entries.OrderByDescending(e => e.Confidence).First().Crc);
        }

        // ------------------------------------------------------------------ drive offsets

        /// <summary>
        /// The drive's read offset from AccurateRip's DriveOffsets.bin (cached in %LOCALAPPDATA%\CD-Tag, refreshed after 30 days),
        /// or null when the drive is not listed. Records: offset (int16), "VENDOR - PRODUCT" (32 bytes), submissions (int32), 31 bytes.
        /// </summary>
        public static async Task<int?> FindDriveOffsetAsync(string vendor, string product, CancellationToken cancellation = default(CancellationToken))
        {
            byte[] data = await LoadDriveOffsetsAsync(cancellation).ConfigureAwait(false);
            return data == null ? (int?)null : FindDriveOffset(data, vendor, product);
        }

        public static int? FindDriveOffset(byte[] data, string vendor, string product)
        {
            string want = Normalize((vendor ?? "").Trim() + " - " + (product ?? "").Trim());
            for (int o = 0; o + 69 <= data.Length; o += 69)
            {
                string name = Encoding.ASCII.GetString(data, o + 2, 32).TrimEnd('\0', ' ');
                if (Normalize(name) == want)
                    return BitConverter.ToInt16(data, o);
            }
            return null;
        }

        private static string Normalize(string name)
        {
            return string.Join(" ", name.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static async Task<byte[]> LoadDriveOffsetsAsync(CancellationToken cancellation)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CD-Tag");
            string path = Path.Combine(folder, "DriveOffsets.bin");
            bool fresh = File.Exists(path) && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddDays(-30);
            if (!fresh)
            {
                try
                {
                    byte[] data = await Http.GetByteArrayAsync(Host + "DriveOffsets.bin", cancellation).ConfigureAwait(false);
                    if (data.Length > 69 && data.Length % 69 == 0)
                    {
                        Directory.CreateDirectory(folder);
                        await File.WriteAllBytesAsync(path, data, CancellationToken.None).ConfigureAwait(false);
                        return data;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellation.IsCancellationRequested || ex is IOException)
                {
                }
            }
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellation).ConfigureAwait(false) : null;
        }
    }
}

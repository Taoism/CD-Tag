using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using CDTag.Model.Tag;

namespace CDTag.Model.Rip
{
    /// <summary>What happened to one track during a rip.</summary>
    public class RipTrackResult
    {
        public int Number { get; set; }
        public string WavPath { get; set; }
        public long Samples { get; set; }
        public uint TestCrc { get; set; }
        public uint CopyCrc { get; set; }
        public bool Tested { get; set; }
        public uint ArV1 { get; set; }
        public uint ArV2 { get; set; }
        public double Peak { get; set; }
        /// <summary>Times (m:ss) of blocks that could not be read the same twice.</summary>
        public List<string> Suspicious { get; private set; }
        public int Retries { get; set; }
        /// <summary>Sectors the drive flagged with C2 errors (C2 mode only).</summary>
        public int C2Errors { get; set; }

        public RipTrackResult()
        {
            Suspicious = new List<string>();
        }

        public bool CrcsMatch { get { return !Tested || TestCrc == CopyCrc; } }
    }

    /// <summary>Progress of a rip: track index (0-based), pass ("Test", "Copy"), percent of the track.</summary>
    public class RipProgress
    {
        public int TrackIndex { get; set; }
        public string Pass { get; set; }
        public int Percent { get; set; }
        public double Speed { get; set; }
    }

    /// <summary>
    /// Reads audio tracks from a CD like EAC's secure mode with "test & copy": every track is read twice (cache defeated
    /// by the distance between the passes); blocks whose CRC differs between the passes are re-read (after reading
    /// elsewhere to empty the drive cache) until one version is read twice. The drive's read offset is corrected;
    /// samples outside the readable area are silence ("fill up missing offset samples with silence").
    /// </summary>
    public sealed class CdRipper
    {
        private const int MaxRereads = 16;

        private readonly CdDrive _drive;
        private readonly List<CdTrack> _toc;
        private readonly int _offset;
        private readonly int _leadOut;
        private readonly int _firstAudio;

        public CdRipper(CdDrive drive, List<CdTrack> toc, int readOffset)
        {
            _drive = drive;
            _toc = toc;
            _offset = readOffset;
            _leadOut = CdDrive.AudioLeadOut(toc);
            _firstAudio = toc.Where(t => t.IsAudio).Select(t => t.StartSector).DefaultIfEmpty(0).First();
        }

        public bool TestAndCopy { get; set; } = true;

        /// <summary>Use the drive's C2 error pointers: flagged blocks are re-read (EAC "Make use of C2 pointers").</summary>
        public bool UseC2 { get; set; }

        /// <summary>Try to read into the lead-in/lead-out where the offset needs samples outside the audio area.</summary>
        public bool Overread { get; set; }

        private bool _overreadFailed;

        /// <summary>True when the drive refused to read past the audio area (the missing samples were silence).</summary>
        public bool OverreadFailed { get { return _overreadFailed; } }

        /// <summary>Sectors per read: 26, or 24 with C2 data (the transfer must stay below 64 KB).</summary>
        private int Block { get { return UseC2 ? CdDrive.MaxSectorsPerC2Read : CdDrive.MaxSectorsPerRead; } }

        /// <summary>Rips the audio track at <paramref name="index"/> (into the audio-track list) to <paramref name="wavPath"/>.</summary>
        public RipTrackResult RipTrack(int index, string wavPath, IProgress<RipProgress> progress, CancellationToken cancellation)
        {
            List<CdTrack> audio = _toc.Where(t => t.IsAudio).ToList();
            CdTrack track = audio[index];
            bool first = index == 0, last = index == audio.Count - 1;
            long firstSample = (long)track.StartSector * CdDrive.SamplesPerSector;
            long samples = (long)track.Sectors * CdDrive.SamplesPerSector;
            var result = new RipTrackResult { Number = track.Number, WavPath = wavPath, Samples = samples, Tested = TestAndCopy };

            Dictionary<int, uint> testBlocks = null;
            if (TestAndCopy)
            {
                testBlocks = new Dictionary<int, uint>();
                var testCrc = new Crc32Running();
                ReadRange(firstSample, samples, null, testBlocks, (b, o, c) => testCrc.Add(b, o, c), index, "Test", progress, cancellation, result);
                result.TestCrc = testCrc.Value;
            }

            var copyCrc = new Crc32Running();
            var ar = new AccurateRip.TrackCrc(samples, first, last);
            int peak = 0;
            string temp = wavPath + ".~cdtag";
            try
            {
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    var format = new Cue.WavFormat { Channels = 2, SampleRate = 44100, BitsPerSample = 16, BlockAlign = 4 };
                    byte[] header = format.Header(samples * 4);
                    output.Write(header, 0, header.Length);
                    ReadRange(firstSample, samples, testBlocks, null, (b, o, c) =>
                    {
                        output.Write(b, o, c);
                        copyCrc.Add(b, o, c);
                        ar.Add(b, o, c);
                        for (int i = o; i + 1 < o + c; i += 2)
                        {
                            int v = Math.Abs((int)(short)(b[i] | (b[i + 1] << 8)));
                            if (v > peak) peak = v;
                        }
                    }, index, "Copy", progress, cancellation, result);
                }
                File.Move(temp, wavPath, true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            result.CopyCrc = copyCrc.Value;
            result.ArV1 = ar.V1;
            result.ArV2 = ar.V2;
            result.Peak = Math.Min(100.0, peak * 100.0 / 32767);
            return result;
        }

        /// <summary>
        /// Delivers samples [firstSample, firstSample + count) of the disc, offset-corrected, to <paramref name="sink"/>.
        /// Test pass: records each block's CRC in <paramref name="record"/>. Copy pass: compares with <paramref name="compare"/>
        /// and re-reads blocks that differ.
        /// </summary>
        private void ReadRange(long firstSample, long count, Dictionary<int, uint> compare, Dictionary<int, uint> record,
                               Action<byte[], int, int> sink, int index, string pass, IProgress<RipProgress> progress,
                               CancellationToken cancellation, RipTrackResult result)
        {
            long rawFirst = firstSample + _offset;           // where the wanted samples are in the drive's (uncorrected) data
            long rawEnd = rawFirst + count;
            int firstSector = (int)Math.Floor(rawFirst / (double)CdDrive.SamplesPerSector);
            int endSector = (int)Math.Ceiling(rawEnd / (double)CdDrive.SamplesPerSector);
            var buffer = new byte[Block * CdDrive.SectorBytes];
            var started = DateTime.UtcNow;
            int lastPercent = -1;

            for (int sector = firstSector; sector < endSector; sector += Block)
            {
                cancellation.ThrowIfCancellationRequested();
                int n = Math.Min(Block, endSector - sector);
                ReadBlock(sector, n, buffer, compare, record, result);

                // slice of this block inside the wanted range
                long blockFirst = (long)sector * CdDrive.SamplesPerSector;
                long from = Math.Max(blockFirst, rawFirst), to = Math.Min(blockFirst + (long)n * CdDrive.SamplesPerSector, rawEnd);
                if (to > from)
                    sink(buffer, (int)(from - blockFirst) * 4, (int)(to - from) * 4);

                int percent = (int)((sector + n - firstSector) * 100L / Math.Max(1, endSector - firstSector));
                if (progress != null && percent != lastPercent)
                {
                    lastPercent = percent;
                    double seconds = Math.Max(0.001, (DateTime.UtcNow - started).TotalSeconds);
                    progress.Report(new RipProgress { TrackIndex = index, Pass = pass, Percent = percent, Speed = (sector + n - firstSector) / 75.0 / seconds });
                }
            }
        }

        /// <summary>One read, with C2 pointers when <see cref="UseC2"/> (a C2-flagged block counts as a failed read).</summary>
        private bool Read(int first, int count, byte[] buffer, int at, RipTrackResult result)
        {
            if (!UseC2)
                return _drive.ReadSectors(first, count, buffer, at);
            int c2;
            if (!_drive.ReadSectorsC2(first, count, buffer, at, out c2))
                return false;
            if (c2 > 0 && result != null)
                result.C2Errors += c2;
            return c2 == 0;
        }

        /// <summary>
        /// Reads one block. Sectors outside the audio area are silence, unless <see cref="Overread"/> is on and the drive can
        /// read them (into the lead-in before track 1 / the lead-out after the last track).
        /// </summary>
        private void ReadBlock(int sector, int n, byte[] buffer, Dictionary<int, uint> compare, Dictionary<int, uint> record, RipTrackResult result)
        {
            int readFirst = Math.Max(sector, _firstAudio), readEnd = Math.Min(sector + n, _leadOut);
            Array.Clear(buffer, 0, n * CdDrive.SectorBytes);
            bool alreadyRead = false;
            if (Overread && (readFirst != sector || readEnd != sector + n) && !_overreadFailed)
            {
                int first = Math.Max(sector, -150), end = sector + n;   // -150..-1: track 1's pregap in the lead-in
                if (end > first && Read(first, end - first, buffer, (first - sector) * CdDrive.SectorBytes, null))
                {
                    readFirst = first;
                    readEnd = end;
                    alreadyRead = true;
                }
                else
                {
                    _overreadFailed = true;   // the drive can't: silence, and don't ask again
                    Array.Clear(buffer, 0, n * CdDrive.SectorBytes);
                }
            }
            if (readEnd <= readFirst)
            {
                record?.Add(sector, 0);
                return;
            }
            int at = (readFirst - sector) * CdDrive.SectorBytes;
            int count = readEnd - readFirst;
            bool ok = alreadyRead || Read(readFirst, count, buffer, at, result);
            uint crc = Crc32Running.Compute(buffer, 0, n * CdDrive.SectorBytes);

            if (record != null)
            {
                // test pass: a read error is retried in the copy pass
                record[sector] = ok ? crc : ~crc;
                return;
            }
            uint expected;
            if (ok && (compare == null || (compare.TryGetValue(sector, out expected) && expected == crc)))
                return;

            // Error recovery: re-read until one version comes back twice.
            var seen = new Dictionary<uint, int>();
            if (ok) seen[crc] = 1;
            if (compare != null && compare.TryGetValue(sector, out expected)) seen[expected] = seen.TryGetValue(expected, out int c0) ? c0 + 1 : 1;
            byte[] best = null;
            for (int attempt = 0; attempt < MaxRereads; attempt++)
            {
                result.Retries++;
                DefeatCache(sector);
                Array.Clear(buffer, 0, n * CdDrive.SectorBytes);
                if (!Read(readFirst, count, buffer, at, null))
                    continue;   // read error or C2-flagged: try again
                crc = Crc32Running.Compute(buffer, 0, n * CdDrive.SectorBytes);
                int times = seen.TryGetValue(crc, out int c) ? c + 1 : 1;
                seen[crc] = times;
                best = (byte[])buffer.Clone();
                if (times >= 2)
                    return;   // read the same twice: keep it
            }
            if (best != null)
                Buffer.BlockCopy(best, 0, buffer, 0, n * CdDrive.SectorBytes);
            long secondsIntoTrack = Math.Max(0, (long)(sector - _toc.Last(t => t.StartSector <= Math.Max(sector, _firstAudio)).StartSector)) / 75;
            result.Suspicious.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", secondsIntoTrack / 60, secondsIntoTrack % 60));
        }

        /// <summary>Reads about 1 MB far from <paramref name="sector"/> so the drive's cache no longer holds it.</summary>
        private void DefeatCache(int sector)
        {
            var scratch = new byte[Block * CdDrive.SectorBytes];
            int far = sector < (_firstAudio + _leadOut) / 2 ? _leadOut - 20 * Block : _firstAudio;
            for (int i = 0; i < 18; i++)
                _drive.ReadSectors(Math.Max(_firstAudio, far + i * Block), Block, scratch);
        }
    }

    /// <summary>CRC-32 (IEEE), the "Test CRC"/"Copy CRC" of EAC logs.</summary>
    public sealed class Crc32Running
    {
        private static readonly uint[] Table = CreateTable();
        private uint _crc = 0xFFFFFFFFu;

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        public uint Value { get { return _crc ^ 0xFFFFFFFFu; } }

        public void Add(byte[] data, int offset, int count)
        {
            uint crc = _crc;
            for (int i = offset; i < offset + count; i++)
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            _crc = crc;
        }

        public static uint Compute(byte[] data, int offset, int count)
        {
            var c = new Crc32Running();
            c.Add(data, offset, count);
            return c.Value;
        }
    }
}

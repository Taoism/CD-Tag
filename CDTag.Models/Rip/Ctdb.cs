using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CDTag.Model.Lookup;

namespace CDTag.Model.Rip
{
    /// <summary>
    /// Verifying a rip against the CUETools database (CTDB). A track's CTDB CRC is a CRC-32 of its samples, except that the
    /// disc's first 10 sectors (5,880 samples) and its last 10 sectors plus the remainder of the disc length modulo
    /// 5,880 samples are left out (CTDB works in 5,880-sample strides). Checked against CTDB entries of a real disc.
    /// </summary>
    public static class Ctdb
    {
        public const int Stride = 10 * CdDrive.SamplesPerSector;

        /// <summary>Running CTDB CRC of one track: feed the track's bytes in order.</summary>
        public sealed class TrackCrc
        {
            private readonly long _from, _to;   // sample positions (0-based) included: [from, to)
            private long _position;
            private readonly Crc32Running _crc = new Crc32Running();

            public TrackCrc(long trackSamples, bool firstTrack, bool lastTrack, long discSamples)
            {
                _from = firstTrack ? Stride : 0;
                _to = lastTrack ? trackSamples - (Stride + discSamples % Stride) : trackSamples;
            }

            public uint Value { get { return _crc.Value; } }

            public void Add(byte[] buffer, int offset, int count)
            {
                long samples = count / 4;
                long start = Math.Max(_position, _from), end = Math.Min(_position + samples, _to);
                if (end > start)
                    _crc.Add(buffer, offset + (int)(start - _position) * 4, (int)(end - start) * 4);
                _position += samples;
            }
        }

        /// <summary>
        /// "Accurately ripped (confidence 98/101) [F16A1ACF]" when entries with this track CRC were submitted, otherwise why not.
        /// </summary>
        public static string Describe(List<CtdbEntry> entries, int trackIndex, uint crc, out bool accurate, out int confidence)
        {
            accurate = false;
            confidence = 0;
            if (entries == null)
                return "CTDB not reachable";
            if (entries.Count == 0)
                return "Disc not present in CTDB";
            int total = entries.Sum(e => e.Confidence);
            confidence = entries.Where(e => trackIndex < e.TrackCrcs.Count && e.TrackCrcs[trackIndex] == crc).Sum(e => e.Confidence);
            accurate = confidence > 0;
            return accurate
                ? string.Format(CultureInfo.InvariantCulture, "CTDB: accurately ripped (confidence {0}/{1})  [{2:X8}]", confidence, total, crc)
                : string.Format(CultureInfo.InvariantCulture, "CTDB: differs from all {0} submissions  [{1:X8}]", total, crc);
        }
    }
}

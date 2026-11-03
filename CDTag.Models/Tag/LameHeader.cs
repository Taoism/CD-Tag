using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CDTag.Model.Tag
{
    /// <summary>
    /// The LAME info tag of an MP3 (written in the Xing/Info frame): encoder version, VBR method and preset.
    /// IdSharp's reader keeps only 4 characters of the version, so "3.100" reads as "3.10"; this reads all 9.
    /// </summary>
    public class LameHeader
    {
        /// <summary>"LAME3.100", "LAME3.99r".</summary>
        public string Encoder { get; private set; }
        /// <summary>"3.100", "3.99".</summary>
        public string Version { get; private set; }
        /// <summary>1 CBR, 2 ABR, 3-6 VBR, 8 CBR 2-pass, 9 ABR 2-pass.</summary>
        public int Method { get; private set; }
        /// <summary>Preset as stored: 8-320 = bitrate, 410-500 = V9-V0, 0 = not stored.</summary>
        public int Preset { get; private set; }
        /// <summary>Bitrate byte (CBR: the bitrate, VBR: the minimum bitrate; 255 = 255 or more).</summary>
        public int Bitrate { get; private set; }

        /// <summary>Info tag revision (0 for LAME 3.90 and later, 15 reserved).</summary>
        public int Revision { get; private set; }
        /// <summary>Lowpass filter in Hz (0 = unknown).</summary>
        public int Lowpass { get; private set; }
        /// <summary>Peak signal amplitude (1.0 = full scale), when stored.</summary>
        public float? Peak { get; private set; }
        /// <summary>Track (radio) replay gain in dB, when stored.</summary>
        public double? RadioGain { get; private set; }
        /// <summary>Album (audiophile) replay gain in dB, when stored.</summary>
        public double? AudiophileGain { get; private set; }
        /// <summary>Bits: 1 nspsytune, 2 nssafejoint, 4 nogap (next), 8 nogap (previous).</summary>
        public int Flags { get; private set; }
        public int AthType { get; private set; }
        /// <summary>Samples LAME added at the start (usually 576).</summary>
        public int EncoderDelay { get; private set; }
        /// <summary>Samples LAME added at the end to fill the last frame.</summary>
        public int Padding { get; private set; }
        public int NoiseShaping { get; private set; }
        /// <summary>0 mono, 1 stereo, 2 dual, 3 joint, 4 forced, 5 auto, 6 intensity, 7 other.</summary>
        public int StereoMode { get; private set; }
        public bool Unwise { get; private set; }
        /// <summary>0 32 kHz or less, 1 44.1 kHz, 2 48 kHz, 3 above 48 kHz.</summary>
        public int SourceFrequency { get; private set; }
        public int Mp3Gain { get; private set; }
        public int Surround { get; private set; }
        /// <summary>Bytes from the start of the first frame to the end of the last (no ID3 tags).</summary>
        public uint MusicLength { get; private set; }
        /// <summary>CRC-16 of the audio frames after the info frame.</summary>
        public int MusicCrc { get; private set; }
        /// <summary>CRC-16 of the first 190 bytes of the info frame.</summary>
        public int TagCrc { get; private set; }
        public bool TagCrcValid { get; private set; }
        /// <summary>Frame and byte counts and VBR quality from the Xing/Info header (0 when absent).</summary>
        public uint Frames { get; private set; }
        public uint Bytes { get; private set; }
        public int XingQuality { get; private set; }
        /// <summary>Offset of the info frame in the data given to <see cref="Parse"/>.</summary>
        public int FrameOffset { get; private set; }

        public string MethodName
        {
            get
            {
                switch (Method)
                {
                    case 1: return "CBR";
                    case 2: return "ABR";
                    case 3: return "VBR (old/rh)";
                    case 4: return "VBR (mtrh)";
                    case 5: return "VBR (mt)";
                    case 6: return "VBR";
                    case 8: return "CBR (2 pass)";
                    case 9: return "ABR (2 pass)";
                    default: return "Unknown (" + Method.ToString(CultureInfo.InvariantCulture) + ")";
                }
            }
        }

        public string StereoModeName
        {
            get
            {
                string[] names = { "Mono", "Stereo", "Dual channel", "Joint stereo", "Forced joint stereo", "Auto", "Intensity stereo", "Other" };
                return names[StereoMode & 7];
            }
        }

        public string SourceFrequencyName
        {
            get
            {
                string[] names = { "32 kHz or less", "44.1 kHz", "48 kHz", "Above 48 kHz" };
                return names[SourceFrequency & 3];
            }
        }

        public bool IsCbr { get { return Method == 1 || Method == 8; } }
        public bool IsVbr { get { return Method >= 3 && Method <= 6; } }

        /// <summary>The -V level (0-9) for a VBR preset, otherwise -1.</summary>
        public int VbrQuality { get { return Preset >= 410 && Preset <= 500 && Preset % 10 == 0 ? (500 - Preset) / 10 : -1; } }

        public bool IsLame3100 { get { return Version == "3.100"; } }
        /// <summary>LAME 3.100 -V0 (scene standard for non-WEB sources).</summary>
        public bool IsSceneV0 { get { return IsLame3100 && IsVbr && Preset == 500; } }
        /// <summary>LAME 3.100 -b 320 (scene standard for lossless WEB sources).</summary>
        public bool IsScene320 { get { return IsLame3100 && IsCbr && Preset == 320; } }

        /// <summary>"LAME 3.100 -V0", "LAME 3.100 -b 320", "LAME 3.99 --abr 192".</summary>
        public string Settings
        {
            get
            {
                string name = "LAME " + (Version ?? "?");
                if (VbrQuality >= 0)
                    return name + " -V" + VbrQuality.ToString(CultureInfo.InvariantCulture);
                if (Preset >= 8 && Preset <= 320)
                    return name + (IsCbr ? " -b " : Method == 2 || Method == 9 ? " --abr " : " ") + Preset.ToString(CultureInfo.InvariantCulture);
                if (IsCbr && Bitrate > 0)
                    return name + " -b " + Bitrate.ToString(CultureInfo.InvariantCulture);
                return name + (IsVbr ? " VBR" : "");
            }
        }

        public override string ToString()
        {
            return Settings;
        }

        private static readonly Regex VersionPattern = new Regex(@"^LAME(\d+\.\d+)", RegexOptions.CultureInvariant);

        /// <summary>Reads the LAME tag; null when the file has none (or can't be read).</summary>
        public static LameHeader Read(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = SkipId3v2(stream);
                    var buffer = new byte[4096];
                    stream.Position = start;
                    int read = stream.Read(buffer, 0, buffer.Length);
                    return Parse(buffer, read);
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

        /// <summary>Parses the LAME tag in the first frame of <paramref name="data"/> (audio data after any ID3v2 tag).</summary>
        public static LameHeader Parse(byte[] data, int length)
        {
            // First frame sync
            int sync = -1;
            for (int i = 0; i + 1 < length && i < 2048; i++)
            {
                if (data[i] == 0xFF && (data[i + 1] & 0xE0) == 0xE0)
                {
                    sync = i;
                    break;
                }
            }
            if (sync < 0)
                return null;

            // Xing/Info header within the first frame (after the side info), then "LAME"
            int xing = IndexOf(data, length, sync, Math.Min(length, sync + 64), "Xing");
            if (xing < 0)
                xing = IndexOf(data, length, sync, Math.Min(length, sync + 64), "Info");
            int lame = IndexOf(data, length, xing >= 0 ? xing : sync, Math.Min(length, (xing >= 0 ? xing : sync) + 200), "LAME");
            if (lame < 0 || lame + 36 > length)
                return null;

            string encoder = Encoding.ASCII.GetString(data, lame, 9);
            int end = 4;
            while (end < 9 && IsVersionChar(encoder[end]))
                end++;
            encoder = encoder.Substring(0, end);
            Match version = VersionPattern.Match(encoder);

            var header = new LameHeader
            {
                Encoder = encoder,
                Version = version.Success ? version.Groups[1].Value : null,
                Method = data[lame + 9] & 0x0F,
                Bitrate = data[lame + 20],
                Preset = ((data[lame + 26] & 0x07) << 8) | data[lame + 27]
            };

            // The rest of the LAME tag (revision 0/1 layout).
            header.Revision = data[lame + 9] >> 4;
            header.Lowpass = data[lame + 10] * 100;
            uint peak = ReadUInt32(data, lame + 11);
            header.Peak = peak == 0 ? (float?)null : BitConverter.Int32BitsToSingle((int)peak);
            header.RadioGain = ReplayGain(data, lame + 15);
            header.AudiophileGain = ReplayGain(data, lame + 17);
            header.Flags = data[lame + 19] >> 4;
            header.AthType = data[lame + 19] & 0x0F;
            header.EncoderDelay = (data[lame + 21] << 4) | (data[lame + 22] >> 4);
            header.Padding = ((data[lame + 22] & 0x0F) << 8) | data[lame + 23];
            header.NoiseShaping = data[lame + 24] & 0x03;
            header.StereoMode = (data[lame + 24] >> 2) & 0x07;
            header.Unwise = (data[lame + 24] & 0x20) != 0;
            header.SourceFrequency = data[lame + 24] >> 6;
            header.Mp3Gain = (sbyte)data[lame + 25];
            header.Surround = (data[lame + 26] >> 3) & 0x07;
            header.MusicLength = ReadUInt32(data, lame + 28);
            header.MusicCrc = (data[lame + 32] << 8) | data[lame + 33];
            header.TagCrc = (data[lame + 34] << 8) | data[lame + 35];
            header.TagCrcValid = Crc16.Compute(data, sync, lame + 34 - sync) == header.TagCrc;
            header.FrameOffset = sync;

            if (xing >= 0)
            {
                int flags = (int)ReadUInt32(data, xing + 4);
                int position = xing + 8;
                if ((flags & 1) != 0 && position + 4 <= length)
                {
                    header.Frames = ReadUInt32(data, position);
                    position += 4;
                }
                if ((flags & 2) != 0 && position + 4 <= length)
                {
                    header.Bytes = ReadUInt32(data, position);
                    position += 4;
                }
                if ((flags & 4) != 0)
                    position += 100;
                if ((flags & 8) != 0 && position + 4 <= length)
                    header.XingQuality = (int)ReadUInt32(data, position);
            }
            return header;
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        /// <summary>Replay gain field: 3 bits name (1 radio, 2 audiophile), 3 bits originator, sign bit, 9 bits value in 0.1 dB.</summary>
        private static double? ReplayGain(byte[] data, int offset)
        {
            int value = (data[offset] << 8) | data[offset + 1];
            if ((value >> 13) == 0)
                return null;
            double gain = (value & 0x1FF) / 10.0;
            return (value & 0x200) != 0 ? -gain : gain;
        }

        private static bool IsVersionChar(char c)
        {
            return (c >= '0' && c <= '9') || c == '.' || (c >= 'a' && c <= 'z');
        }

        private static int IndexOf(byte[] data, int length, int from, int to, string text)
        {
            for (int i = Math.Max(0, from); i + text.Length <= length && i < to; i++)
            {
                int j = 0;
                while (j < text.Length && data[i + j] == text[j])
                    j++;
                if (j == text.Length)
                    return i;
            }
            return -1;
        }

        private static long SkipId3v2(Stream stream)
        {
            var header = new byte[10];
            if (stream.Read(header, 0, 10) != 10 || header[0] != 'I' || header[1] != 'D' || header[2] != '3')
                return 0;
            long size = ((header[6] & 0x7F) << 21) | ((header[7] & 0x7F) << 14) | ((header[8] & 0x7F) << 7) | (header[9] & 0x7F);
            return 10 + size + ((header[5] & 0x10) != 0 ? 10 : 0);
        }
    }
}

namespace CDTag.Model.Tag
{
    /// <summary>CRC-16 (polynomial 0x8005, reflected, initial value 0) used by the LAME info tag.</summary>
    public static class Crc16
    {
        private static readonly ushort[] Table = CreateTable();

        private static ushort[] CreateTable()
        {
            var table = new ushort[256];
            for (int i = 0; i < 256; i++)
            {
                int c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? (c >> 1) ^ 0xA001 : c >> 1;
                table[i] = (ushort)c;
            }
            return table;
        }

        public static int Compute(byte[] data, int offset, int count, int crc = 0)
        {
            for (int i = offset; i < offset + count; i++)
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc & 0xFFFF;
        }
    }
}

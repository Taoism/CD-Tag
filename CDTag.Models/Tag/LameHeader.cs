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

            return new LameHeader
            {
                Encoder = encoder,
                Version = version.Success ? version.Groups[1].Value : null,
                Method = data[lame + 9] & 0x0F,
                Bitrate = data[lame + 20],
                Preset = ((data[lame + 26] & 0x07) << 8) | data[lame + 27]
            };
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CDTag.Model.Checksum;

namespace CDTag.Model.Tag
{
    /// <summary>One line of the Encoding Inspector: a value in a section ("MPEG", "LAME tag", "FLAC"...).</summary>
    public class InspectorItem
    {
        public string Section { get; set; }
        /// <summary>The section name on its first line only (for a list without grouping).</summary>
        public string SectionLabel { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
        public LogCheckState State { get; set; }
    }

    /// <summary>
    /// Encoding Inspector: what is inside an MP3 (MPEG header, Xing/LAME info tag with CRC checks, tags),
    /// a FLAC file (STREAMINFO, encoder, metadata blocks) or a WAV file.
    /// </summary>
    public static class AudioInspector
    {
        public static readonly string[] Extensions = { ".mp3", ".flac", ".wav" };

        private static readonly int[,] Mp3Bitrates =
        {
            // MPEG-1 Layer III, MPEG-2/2.5 Layer III
            { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 },
            { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 }
        };

        private static readonly int[,] Mp3SampleRates =
        {
            { 44100, 48000, 32000 }, // MPEG-1
            { 22050, 24000, 16000 }, // MPEG-2
            { 11025, 12000, 8000 }   // MPEG-2.5
        };

        public static bool CanInspect(string path)
        {
            return Extensions.Contains(Path.GetExtension(path) ?? "", StringComparer.OrdinalIgnoreCase);
        }

        public static List<InspectorItem> Inspect(string path)
        {
            var items = new List<InspectorItem>();
            var info = new FileInfo(path);
            Add(items, "File", "Name", info.Name);
            Add(items, "File", "Size", string.Format(CultureInfo.InvariantCulture, "{0:#,0} bytes", info.Length));

            try
            {
                switch (Path.GetExtension(path).ToLowerInvariant())
                {
                    case ".mp3":
                        InspectMp3(path, info.Length, items);
                        break;
                    case ".flac":
                        InspectFlac(path, info.Length, items);
                        break;
                    case ".wav":
                        InspectWav(path, items);
                        break;
                    default:
                        Add(items, "File", "Type", "Not an MP3, FLAC or WAV file", LogCheckState.Warning);
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is IndexOutOfRangeException)
            {
                Add(items, "File", "Error", ex.Message, LogCheckState.Bad);
            }

            // Keep sections together, in the order they first appear.
            List<string> order = items.Select(i => i.Section).Distinct().ToList();
            items = items.OrderBy(i => order.IndexOf(i.Section)).ToList();
            for (int i = 0; i < items.Count; i++)
                items[i].SectionLabel = i == 0 || items[i - 1].Section != items[i].Section ? items[i].Section : null;
            return items;
        }

        // ------------------------------------------------------------------ MP3

        private static void InspectMp3(string path, long fileLength, List<InspectorItem> items)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var head = new byte[10];
                long audioStart = 0;
                int read = stream.Read(head, 0, 10);
                if (read == 10 && head[0] == 'I' && head[1] == 'D' && head[2] == '3')
                {
                    long size = ((head[6] & 0x7F) << 21) | ((head[7] & 0x7F) << 14) | ((head[8] & 0x7F) << 7) | (head[9] & 0x7F);
                    audioStart = 10 + size + ((head[5] & 0x10) != 0 ? 10 : 0);
                    Add(items, "Tags", "ID3v2", string.Format(CultureInfo.InvariantCulture, "ID3v2.{0}.{1}, {2:#,0} bytes", head[3], head[4], audioStart));
                }

                var buffer = new byte[16384];
                stream.Position = audioStart;
                int length = stream.Read(buffer, 0, buffer.Length);
                int sync = FindFrame(buffer, length);
                if (sync < 0)
                {
                    Add(items, "MPEG", "Frame", "No MPEG audio frame found", LogCheckState.Bad);
                    return;
                }
                if (sync > 0)
                    Add(items, "MPEG", "Junk before audio", string.Format(CultureInfo.InvariantCulture, "{0:#,0} bytes", sync), LogCheckState.Warning);

                int b1 = buffer[sync + 1], b2 = buffer[sync + 2], b3 = buffer[sync + 3];
                int versionBits = (b1 >> 3) & 3;           // 3 MPEG-1, 2 MPEG-2, 0 MPEG-2.5
                int layerBits = (b1 >> 1) & 3;             // 1 Layer III
                int versionRow = versionBits == 3 ? 0 : versionBits == 2 ? 1 : 2;
                int sampleRate = Mp3SampleRates[versionRow, (b2 >> 2) & 3];
                int bitrate = Mp3Bitrates[versionRow == 0 ? 0 : 1, b2 >> 4];
                int channelMode = b3 >> 6;
                int samplesPerFrame = versionRow == 0 ? 1152 : 576;
                string[] channelNames = { "Stereo", "Joint stereo", "Dual channel", "Mono" };

                Add(items, "MPEG", "Format", string.Format("MPEG-{0} Layer {1}", versionRow == 0 ? "1" : versionRow == 1 ? "2" : "2.5", layerBits == 1 ? "III" : layerBits == 2 ? "II" : "I"),
                    versionRow == 0 && layerBits == 1 ? LogCheckState.Info : LogCheckState.Warning);
                Add(items, "MPEG", "Sample rate", string.Format(CultureInfo.InvariantCulture, "{0:#,0} Hz", sampleRate), sampleRate == 44100 ? LogCheckState.Info : LogCheckState.Warning);
                Add(items, "MPEG", "Channel mode", channelNames[channelMode]);
                Add(items, "MPEG", "CRC protection", (b1 & 1) == 0 ? "Yes" : "No");

                LameHeader lame = LameHeader.Parse(buffer, length);
                bool hasXing = IndexOf(buffer, length, sync, sync + 64, "Xing") >= 0 || IndexOf(buffer, length, sync, sync + 64, "Info") >= 0;
                long audioBytes = fileLength - audioStart - Id3v1AndApeSize(stream, fileLength);
                if (lame != null && lame.Frames > 0)
                {
                    double seconds = (double)lame.Frames * samplesPerFrame / sampleRate;
                    if (lame.EncoderDelay + lame.Padding < lame.Frames * samplesPerFrame)
                        seconds = ((double)lame.Frames * samplesPerFrame - lame.EncoderDelay - lame.Padding) / sampleRate;
                    Add(items, "MPEG", "Length", Duration(seconds));
                    Add(items, "MPEG", "Frames", lame.Frames.ToString("#,0", CultureInfo.InvariantCulture) + " (plus the info frame)");
                    Add(items, "MPEG", "Average bitrate", string.Format(CultureInfo.InvariantCulture, "{0:0.0} kbps", (lame.Bytes > 0 ? lame.Bytes : audioBytes) * 8.0 / seconds / 1000));
                }
                else if (bitrate > 0)
                {
                    Add(items, "MPEG", "Bitrate", bitrate + " kbps (first frame)");
                    Add(items, "MPEG", "Length", Duration(audioBytes * 8.0 / (bitrate * 1000.0)) + " (estimated)");
                }

                if (!hasXing)
                    Add(items, "Info tag", "Xing/Info header", "None", LogCheckState.Warning);
                if (lame == null)
                {
                    Add(items, "LAME tag", "LAME tag", "None (not encoded with LAME, or the tag was removed)", LogCheckState.Warning);
                }
                else
                {
                    AddLame(items, lame);
                    CheckMusicCrc(stream, audioStart, sync, lame, versionRow, sampleRate, b2, items);
                }

                AddTrailingTags(stream, fileLength, items);
            }
        }

        private static void AddLame(List<InspectorItem> items, LameHeader lame)
        {
            const string s = "LAME tag";
            Add(items, s, "Encoder", lame.Encoder, lame.IsLame3100 ? LogCheckState.Good : LogCheckState.Info);
            Add(items, s, "Settings", lame.Settings, lame.IsSceneV0 || lame.IsScene320 ? LogCheckState.Good : LogCheckState.Info);
            Add(items, s, "Bitrate method", lame.MethodName);
            if (lame.Preset > 0)
                Add(items, s, "Preset", PresetName(lame.Preset));
            Add(items, s, lame.IsCbr ? "Bitrate" : lame.IsVbr ? "Minimum bitrate" : "Target bitrate", lame.Bitrate == 255 ? "255+ kbps" : lame.Bitrate + " kbps");
            if (lame.XingQuality > 0)
                Add(items, s, "Quality", lame.XingQuality.ToString(CultureInfo.InvariantCulture) + (lame.XingQuality <= 100 ? string.Format(" (-V{0} -q{1})", (100 - lame.XingQuality) / 10, (100 - lame.XingQuality) % 10) : ""));
            Add(items, s, "Lowpass", lame.Lowpass > 0 ? string.Format(CultureInfo.InvariantCulture, "{0:#,0} Hz", lame.Lowpass) : "Unknown");
            Add(items, s, "Stereo mode", lame.StereoModeName);
            Add(items, s, "Source sample rate", lame.SourceFrequencyName);
            Add(items, s, "Encoder delay / padding", string.Format(CultureInfo.InvariantCulture, "{0} / {1} samples", lame.EncoderDelay, lame.Padding));
            Add(items, s, "ATH type", lame.AthType.ToString(CultureInfo.InvariantCulture));
            Add(items, s, "Noise shaping", lame.NoiseShaping.ToString(CultureInfo.InvariantCulture));
            var flags = new List<string>();
            if ((lame.Flags & 1) != 0) flags.Add("nspsytune");
            if ((lame.Flags & 2) != 0) flags.Add("nssafejoint");
            if ((lame.Flags & 4) != 0) flags.Add("nogap (next)");
            if ((lame.Flags & 8) != 0) flags.Add("nogap (previous)");
            Add(items, s, "Flags", flags.Count == 0 ? "None" : string.Join(", ", flags));
            if (lame.Unwise)
                Add(items, s, "Unwise settings", "Yes (expert options outside the presets)", LogCheckState.Warning);
            if (lame.Mp3Gain != 0)
                Add(items, s, "MP3 gain", string.Format(CultureInfo.InvariantCulture, "{0:+0;-0} ({1:+0.0;-0.0} dB)", lame.Mp3Gain, lame.Mp3Gain * 1.5), LogCheckState.Warning);
            if (lame.Peak != null)
                Add(items, s, "Peak", string.Format(CultureInfo.InvariantCulture, "{0:0.000000}", lame.Peak));
            if (lame.RadioGain != null)
                Add(items, s, "Replay gain (track)", string.Format(CultureInfo.InvariantCulture, "{0:+0.0;-0.0} dB", lame.RadioGain));
            if (lame.AudiophileGain != null)
                Add(items, s, "Replay gain (album)", string.Format(CultureInfo.InvariantCulture, "{0:+0.0;-0.0} dB", lame.AudiophileGain));
            Add(items, s, "Music length", string.Format(CultureInfo.InvariantCulture, "{0:#,0} bytes", lame.MusicLength));
            Add(items, s, "Info tag CRC", string.Format("{0:X4} ({1})", lame.TagCrc, lame.TagCrcValid ? "OK" : "does not match"), lame.TagCrcValid ? LogCheckState.Good : LogCheckState.Bad);
        }

        /// <summary>The music CRC covers the frames after the info frame, up to the music length.</summary>
        private static void CheckMusicCrc(Stream stream, long audioStart, int sync, LameHeader lame, int versionRow, int sampleRate, int b2, List<InspectorItem> items)
        {
            int bitrate = Mp3Bitrates[versionRow == 0 ? 0 : 1, b2 >> 4];
            int padding = (b2 >> 1) & 1;
            if (bitrate == 0 || lame.MusicLength == 0)
            {
                Add(items, "LAME tag", "Music CRC", string.Format("{0:X4} (not checked)", lame.MusicCrc));
                return;
            }
            int frameSize = (versionRow == 0 ? 144000 : 72000) * bitrate / sampleRate + padding;
            long start = audioStart + sync + frameSize;
            long end = audioStart + sync + lame.MusicLength;
            if (end > stream.Length || end <= start)
            {
                Add(items, "LAME tag", "Music CRC", string.Format("{0:X4} (the file is shorter than the music length: truncated?)", lame.MusicCrc), LogCheckState.Bad);
                return;
            }

            stream.Position = start;
            var buffer = new byte[1 << 16];
            int crc = 0;
            long left = end - start;
            while (left > 0)
            {
                int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (read <= 0)
                    break;
                crc = Crc16.Compute(buffer, 0, read, crc);
                left -= read;
            }
            bool ok = left == 0 && crc == lame.MusicCrc;
            Add(items, "LAME tag", "Music CRC", string.Format("{0:X4} ({1})", lame.MusicCrc, ok ? "OK" : "does not match: the audio was changed or damaged"), ok ? LogCheckState.Good : LogCheckState.Bad);
        }

        private static string PresetName(int preset)
        {
            if (preset >= 410 && preset <= 500 && preset % 10 == 0)
                return "-V" + ((500 - preset) / 10).ToString(CultureInfo.InvariantCulture);
            switch (preset)
            {
                case 1000: return "--preset r3mix";
                case 1001: return "--preset standard";
                case 1002: return "--preset extreme";
                case 1003: return "--preset insane";
                case 1004: return "--preset fast standard";
                case 1005: return "--preset fast extreme";
                case 1006: return "--preset medium";
                case 1007: return "--preset fast medium";
            }
            return preset <= 320 ? preset.ToString(CultureInfo.InvariantCulture) + " kbps" : preset.ToString(CultureInfo.InvariantCulture);
        }

        private static long Id3v1AndApeSize(Stream stream, long fileLength)
        {
            long size = 0;
            var tag = new byte[32];
            if (fileLength >= 128)
            {
                stream.Position = fileLength - 128;
                stream.Read(tag, 0, 3);
                if (tag[0] == 'T' && tag[1] == 'A' && tag[2] == 'G')
                    size = 128;
            }
            if (fileLength - size >= 32)
            {
                stream.Position = fileLength - size - 32;
                stream.Read(tag, 0, 32);
                if (Encoding.ASCII.GetString(tag, 0, 8) == "APETAGEX")
                    size += BitConverter.ToInt32(tag, 12) + ((tag[23] & 0x80) != 0 ? 32 : 0);
            }
            return size;
        }

        private static void AddTrailingTags(Stream stream, long fileLength, List<InspectorItem> items)
        {
            var tag = new byte[32];
            bool id3v1 = false;
            if (fileLength >= 128)
            {
                stream.Position = fileLength - 128;
                stream.Read(tag, 0, 3);
                id3v1 = tag[0] == 'T' && tag[1] == 'A' && tag[2] == 'G';
            }
            Add(items, "Tags", "ID3v1", id3v1 ? "Yes" : "No");
            long apeEnd = fileLength - (id3v1 ? 128 : 0);
            if (apeEnd >= 32)
            {
                stream.Position = apeEnd - 32;
                stream.Read(tag, 0, 32);
                if (Encoding.ASCII.GetString(tag, 0, 8) == "APETAGEX")
                    Add(items, "Tags", "APE", string.Format(CultureInfo.InvariantCulture, "APEv{0}, {1:#,0} bytes", BitConverter.ToInt32(tag, 8) / 1000, BitConverter.ToInt32(tag, 12)), LogCheckState.Warning);
            }
        }

        private static int FindFrame(byte[] data, int length)
        {
            for (int i = 0; i + 3 < length; i++)
            {
                if (data[i] != 0xFF || (data[i + 1] & 0xE0) != 0xE0)
                    continue;
                int version = (data[i + 1] >> 3) & 3, layer = (data[i + 1] >> 1) & 3, bitrate = data[i + 2] >> 4, rate = (data[i + 2] >> 2) & 3;
                if (version != 1 && layer != 0 && bitrate != 15 && rate != 3)
                    return i;
            }
            return -1;
        }

        // ------------------------------------------------------------------ FLAC

        private static readonly Regex FlacVendor = new Regex(@"libFLAC (?<version>\d+\.\d+(?:\.\d+)?)");

        private static void InspectFlac(string path, long fileLength, List<InspectorItem> items)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream))
            {
                byte[] magic = reader.ReadBytes(4);
                if (magic.Length == 4 && magic[0] == 'I' && magic[1] == 'D' && magic[2] == '3')
                {
                    byte[] rest = reader.ReadBytes(6);
                    long size = ((rest[2] & 0x7F) << 21) | ((rest[3] & 0x7F) << 14) | ((rest[4] & 0x7F) << 7) | (rest[5] & 0x7F);
                    Add(items, "Tags", "ID3v2", string.Format(CultureInfo.InvariantCulture, "{0:#,0} bytes before the FLAC data (not allowed in FLAC)", size + 10), LogCheckState.Bad);
                    stream.Position = 10 + size;
                    magic = reader.ReadBytes(4);
                }
                if (magic.Length != 4 || Encoding.ASCII.GetString(magic) != "fLaC")
                {
                    Add(items, "FLAC", "Format", "Not a FLAC stream (no fLaC marker)", LogCheckState.Bad);
                    return;
                }

                long samples = 0;
                int sampleRate = 0, channels = 0, bits = 0;
                int pictures = 0, comments = 0, seekPoints = 0;
                long paddingBytes = 0, pictureBytes = 0;
                var blockNames = new List<string>();
                bool last = false;
                while (!last && stream.Position + 4 <= fileLength)
                {
                    int header = reader.ReadByte();
                    last = (header & 0x80) != 0;
                    int type = header & 0x7F;
                    byte[] lengthBytes = reader.ReadBytes(3);
                    int length = (lengthBytes[0] << 16) | (lengthBytes[1] << 8) | lengthBytes[2];
                    long next = stream.Position + length;
                    switch (type)
                    {
                        case 0:
                            byte[] info = reader.ReadBytes(34);
                            int minBlock = (info[0] << 8) | info[1], maxBlock = (info[2] << 8) | info[3];
                            int minFrame = (info[4] << 16) | (info[5] << 8) | info[6], maxFrame = (info[7] << 16) | (info[8] << 8) | info[9];
                            sampleRate = (info[10] << 12) | (info[11] << 4) | (info[12] >> 4);
                            channels = ((info[12] >> 1) & 7) + 1;
                            bits = (((info[12] & 1) << 4) | (info[13] >> 4)) + 1;
                            samples = ((long)(info[13] & 0x0F) << 32) | ((long)info[14] << 24) | ((long)info[15] << 16) | ((long)info[16] << 8) | info[17];
                            string md5 = Convert.ToHexString(info, 18, 16).ToLowerInvariant();
                            Add(items, "FLAC", "Sample rate", string.Format(CultureInfo.InvariantCulture, "{0:#,0} Hz", sampleRate));
                            Add(items, "FLAC", "Bits per sample", bits.ToString(CultureInfo.InvariantCulture));
                            Add(items, "FLAC", "Channels", channels.ToString(CultureInfo.InvariantCulture));
                            Add(items, "FLAC", "Samples", samples.ToString("#,0", CultureInfo.InvariantCulture));
                            if (sampleRate > 0)
                                Add(items, "FLAC", "Length", Duration((double)samples / sampleRate));
                            Add(items, "FLAC", "Block size", minBlock == maxBlock ? minBlock.ToString(CultureInfo.InvariantCulture) : minBlock + "-" + maxBlock);
                            if (maxFrame > 0)
                                Add(items, "FLAC", "Frame size", string.Format(CultureInfo.InvariantCulture, "{0:#,0}-{1:#,0} bytes", minFrame, maxFrame));
                            bool noMd5 = md5.All(c => c == '0');
                            Add(items, "FLAC", "Audio MD5", noMd5 ? "Not stored" : md5, noMd5 ? LogCheckState.Warning : LogCheckState.Info);
                            blockNames.Add("STREAMINFO");
                            break;
                        case 1:
                            paddingBytes += length;
                            blockNames.Add("PADDING");
                            break;
                        case 2:
                            blockNames.Add("APPLICATION");
                            break;
                        case 3:
                            seekPoints += length / 18;
                            blockNames.Add("SEEKTABLE");
                            break;
                        case 4:
                            byte[] block = reader.ReadBytes(length);
                            int vendorLength = BitConverter.ToInt32(block, 0);
                            string vendor = Encoding.UTF8.GetString(block, 4, vendorLength);
                            comments = BitConverter.ToInt32(block, 4 + vendorLength);
                            Match version = FlacVendor.Match(vendor);
                            if (version.Success)
                                Add(items, "FLAC", "Encoder", "FLAC " + version.Groups["version"].Value);
                            else
                                Add(items, "FLAC", "Encoder", (vendor.StartsWith("Lavf", StringComparison.Ordinal) ? "FFmpeg (" + vendor + ")" : vendor) + ", not the reference encoder (libFLAC)",
                                    LogCheckState.Warning);
                            Add(items, "FLAC", "Vendor string", vendor);
                            blockNames.Add("VORBIS_COMMENT");
                            break;
                        case 5:
                            blockNames.Add("CUESHEET");
                            break;
                        case 6:
                            pictures++;
                            pictureBytes += length;
                            blockNames.Add("PICTURE");
                            break;
                        default:
                            blockNames.Add("type " + type.ToString(CultureInfo.InvariantCulture));
                            break;
                    }
                    stream.Position = next;
                }

                if (samples > 0 && channels > 0 && bits > 0)
                {
                    double raw = samples * channels * (bits / 8.0);
                    long audio = fileLength - stream.Position;
                    Add(items, "FLAC", "Compression", string.Format(CultureInfo.InvariantCulture, "{0:0.0} % of WAV size, {1:0} kbps", audio * 100.0 / raw, audio * 8.0 / ((double)samples / sampleRate) / 1000));
                }
                Add(items, "Metadata", "Blocks", string.Join(", ", blockNames.GroupBy(n => n).Select(g => g.Count() > 1 ? g.Key + " x" + g.Count() : g.Key)));
                Add(items, "Metadata", "Tags", comments.ToString(CultureInfo.InvariantCulture) + " Vorbis comments");
                Add(items, "Metadata", "Pictures", pictures == 0 ? "None" : string.Format(CultureInfo.InvariantCulture, "{0} ({1:#,0} bytes)", pictures, pictureBytes));
                Add(items, "Metadata", "Padding", string.Format(CultureInfo.InvariantCulture, "{0:#,0} bytes", paddingBytes));
                if (seekPoints > 0)
                    Add(items, "Metadata", "Seek points", seekPoints.ToString(CultureInfo.InvariantCulture));
            }
        }

        // ------------------------------------------------------------------ WAV

        private static void InspectWav(string path, List<InspectorItem> items)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
                {
                    Add(items, "WAV", "Format", "Not a RIFF file", LogCheckState.Bad);
                    return;
                }
                reader.ReadInt32();
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
                {
                    Add(items, "WAV", "Format", "Not a WAVE file", LogCheckState.Bad);
                    return;
                }

                int channels = 0, rate = 0, bits = 0, byteRate = 0;
                var chunks = new List<string>();
                while (stream.Position + 8 <= stream.Length)
                {
                    string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    uint size = reader.ReadUInt32();
                    long next = stream.Position + size + (size & 1);
                    chunks.Add(id.Trim());
                    if (id == "fmt ")
                    {
                        int format = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        rate = reader.ReadInt32();
                        byteRate = reader.ReadInt32();
                        reader.ReadUInt16();
                        bits = reader.ReadUInt16();
                        Add(items, "WAV", "Format", format == 1 ? "PCM" : format == 3 ? "IEEE float" : format == 0xFFFE ? "Extensible" : "0x" + format.ToString("X4"));
                        Add(items, "WAV", "Sample rate", string.Format(CultureInfo.InvariantCulture, "{0:#,0} Hz", rate));
                        Add(items, "WAV", "Bits per sample", bits.ToString(CultureInfo.InvariantCulture));
                        Add(items, "WAV", "Channels", channels.ToString(CultureInfo.InvariantCulture));
                    }
                    else if (id == "data")
                    {
                        Add(items, "WAV", "Audio data", string.Format(CultureInfo.InvariantCulture, "{0:#,0} bytes", size));
                        if (byteRate > 0)
                            Add(items, "WAV", "Length", Duration((double)size / byteRate));
                        if (channels > 0 && bits > 0)
                        {
                            long frames = size / (channels * bits / 8);
                            Add(items, "WAV", "Samples", frames.ToString("#,0", CultureInfo.InvariantCulture) +
                                (rate == 44100 && frames % 588 != 0 ? " (not whole CD sectors)" : ""), rate == 44100 && frames % 588 != 0 ? LogCheckState.Warning : LogCheckState.Info);
                        }
                    }
                    stream.Position = next;
                }
                Add(items, "WAV", "Chunks", string.Join(", ", chunks));
            }
        }

        // ------------------------------------------------------------------ helpers

        private static string Duration(double seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:mm\\:ss\\.fff}", (int)span.TotalHours, span)
                : span.ToString("m\\:ss\\.fff", CultureInfo.InvariantCulture);
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

        private static void Add(List<InspectorItem> items, string section, string name, string value, LogCheckState state = LogCheckState.Info)
        {
            items.Add(new InspectorItem { Section = section, Name = name, Value = value, State = state });
        }
    }
}

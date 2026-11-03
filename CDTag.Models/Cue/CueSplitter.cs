using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CDTag.Model.Encoders;

namespace CDTag.Model.Cue
{
    public enum SplitFormat
    {
        Wav,
        Flac
    }

    /// <summary>One file Split CUE will write: a range of the image, in samples.</summary>
    public class SplitTrack
    {
        /// <summary>Track number; 0 for hidden track one audio.</summary>
        public int Number { get; set; }
        public string Title { get; set; }
        public string Performer { get; set; }
        public long StartSample { get; set; }
        /// <summary>Exclusive; -1 = to the end of the image.</summary>
        public long EndSample { get; set; }
        public string FileName { get; set; }
    }

    /// <summary>PCM layout of a WAV file and where its audio data is.</summary>
    public class WavFormat
    {
        public int FormatTag { get; set; }
        public int Channels { get; set; }
        public int SampleRate { get; set; }
        public int BitsPerSample { get; set; }
        public int BlockAlign { get; set; }
        public long DataOffset { get; set; }
        public long DataLength { get; set; }

        public long Samples { get { return BlockAlign > 0 ? DataLength / BlockAlign : 0; } }

        public static WavFormat Read(Stream stream)
        {
            var reader = new BinaryReader(stream);
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
                throw new InvalidDataException("Not a RIFF file.");
            reader.ReadUInt32();
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
                throw new InvalidDataException("Not a WAVE file.");

            var format = new WavFormat();
            while (stream.Position + 8 <= stream.Length)
            {
                string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                long size = reader.ReadUInt32();
                long next = stream.Position + size + (size & 1);
                if (id == "fmt ")
                {
                    format.FormatTag = reader.ReadUInt16();
                    format.Channels = reader.ReadUInt16();
                    format.SampleRate = reader.ReadInt32();
                    reader.ReadInt32();
                    format.BlockAlign = reader.ReadUInt16();
                    format.BitsPerSample = reader.ReadUInt16();
                }
                else if (id == "data")
                {
                    format.DataOffset = stream.Position;
                    // Some writers put 0 or 0xFFFFFFFF when streaming: use the rest of the file.
                    format.DataLength = size == 0 || size == 0xFFFFFFFF || stream.Position + size > stream.Length ? stream.Length - stream.Position : size;
                    break;
                }
                stream.Position = next;
            }
            if (format.DataOffset == 0 || format.BlockAlign == 0)
                throw new InvalidDataException("The WAV file has no fmt or data chunk.");
            if (format.FormatTag != 1 && format.FormatTag != 0xFFFE)
                throw new InvalidDataException("Only PCM WAV files can be split.");
            return format;
        }

        /// <summary>A 44-byte PCM WAV header for <paramref name="dataLength"/> bytes of audio.</summary>
        public byte[] Header(long dataLength)
        {
            var header = new MemoryStream();
            var writer = new BinaryWriter(header);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write((uint)(36 + dataLength));
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((ushort)1);
            writer.Write((ushort)Channels);
            writer.Write(SampleRate);
            writer.Write(SampleRate * BlockAlign);
            writer.Write((ushort)BlockAlign);
            writer.Write((ushort)BitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write((uint)dataLength);
            return header.ToArray();
        }
    }

    /// <summary>
    /// Splits a CUE image (one WAV or FLAC file) into tracks. Tracks start at INDEX 01, so each pregap stays at the end of the
    /// previous track (EAC "gaps appended to previous track"); audio before track 1 can be written as track 00.
    /// </summary>
    public static class CueSplitter
    {
        /// <summary>The tracks to write, with file names like "01 - Title.flac" ("01 - Performer - Title" when a track has its own performer).</summary>
        public static List<SplitTrack> Plan(CueSheet sheet, int sampleRate, SplitFormat format, bool hiddenTrack)
        {
            var tracks = new List<SplitTrack>();
            List<CueTrack> audio = sheet.Tracks.Where(t => t.IsAudio).OrderBy(t => t.Index01).ToList();
            int width = Math.Max(2, audio.Count == 0 ? 2 : audio.Max(t => t.Number).ToString(CultureInfo.InvariantCulture).Length);
            string extension = format == SplitFormat.Flac ? ".flac" : ".wav";

            if (hiddenTrack && audio.Count > 0 && audio[0].Index01 > 0)
            {
                tracks.Add(new SplitTrack
                {
                    Number = 0,
                    Title = "Hidden track",
                    Performer = sheet.Performer,
                    StartSample = 0,
                    EndSample = ToSamples(audio[0].Index01, sampleRate),
                    FileName = new string('0', width) + " - Hidden track" + extension
                });
            }

            for (int i = 0; i < audio.Count; i++)
            {
                CueTrack track = audio[i];
                string performer = string.IsNullOrWhiteSpace(track.Performer) ? sheet.Performer : track.Performer;
                bool ownPerformer = !string.IsNullOrWhiteSpace(track.Performer) && !string.Equals(track.Performer, sheet.Performer, StringComparison.OrdinalIgnoreCase);
                string title = string.IsNullOrWhiteSpace(track.Title) ? "Track " + track.Number.ToString(CultureInfo.InvariantCulture) : track.Title;
                string name = track.Number.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0') + " - " + (ownPerformer ? track.Performer + " - " : "") + title;
                tracks.Add(new SplitTrack
                {
                    Number = track.Number,
                    Title = track.Title,
                    Performer = performer,
                    StartSample = ToSamples(track.Index01, sampleRate),
                    EndSample = i + 1 < audio.Count ? ToSamples(audio[i + 1].Index01, sampleRate) : -1,
                    FileName = SafeFileName(name) + extension
                });
            }
            return tracks;
        }

        /// <summary>Length (samples) and sample rate of a WAV or FLAC image; false when it can't be read.</summary>
        public static bool TryGetLength(string image, out long samples, out int sampleRate)
        {
            samples = 0;
            sampleRate = 0;
            try
            {
                using (var stream = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (string.Equals(Path.GetExtension(image), ".flac", StringComparison.OrdinalIgnoreCase))
                    {
                        var head = new byte[42];
                        if (stream.Read(head, 0, 42) != 42 || Encoding.ASCII.GetString(head, 0, 4) != "fLaC")
                            return false;
                        // STREAMINFO is always the first block: 4 bytes block header, then 34 bytes.
                        sampleRate = (head[18] << 12) | (head[19] << 4) | (head[20] >> 4);
                        samples = ((long)(head[21] & 0x0F) << 32) | ((long)head[22] << 24) | ((long)head[23] << 16) | ((long)head[24] << 8) | head[25];
                        return sampleRate > 0;
                    }
                    WavFormat format = WavFormat.Read(stream);
                    samples = format.Samples;
                    sampleRate = format.SampleRate;
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                return false;
            }
        }

        public static long ToSamples(long frames, int sampleRate)
        {
            return frames * sampleRate / CueSheet.FramesPerSecond;
        }

        public static string SafeFileName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                if (c == ':' )
                    sb.Append(" -");
                else if (c == '/' || c == '\\' || c == '|')
                    sb.Append('-');
                else if (c == '?' || c == '*' || c == '"' || c == '<' || c == '>' || c < ' ')
                    continue;
                else
                    sb.Append(c);
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim().TrimEnd('.');
        }

        /// <summary>Vorbis comments for a split track.</summary>
        public static List<KeyValuePair<string, string>> Tags(CueSheet sheet, SplitTrack track, int trackCount)
        {
            var tags = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("ARTIST", track.Performer),
                new KeyValuePair<string, string>("TITLE", track.Title),
                new KeyValuePair<string, string>("ALBUM", sheet.Title),
                new KeyValuePair<string, string>("DATE", sheet.Date),
                new KeyValuePair<string, string>("GENRE", sheet.Genre),
                new KeyValuePair<string, string>("TRACKNUMBER", track.Number > 0 ? track.Number.ToString(CultureInfo.InvariantCulture) : null),
                new KeyValuePair<string, string>("TRACKTOTAL", trackCount.ToString(CultureInfo.InvariantCulture))
            };
            if (!string.IsNullOrWhiteSpace(sheet.Performer) && !string.Equals(sheet.Performer, track.Performer, StringComparison.OrdinalIgnoreCase))
                tags.Add(new KeyValuePair<string, string>("ALBUMARTIST", sheet.Performer));
            CueTrack cue = sheet.Tracks.FirstOrDefault(t => t.Number == track.Number);
            if (cue != null && !string.IsNullOrWhiteSpace(cue.Isrc))
                tags.Add(new KeyValuePair<string, string>("ISRC", cue.Isrc));
            return tags;
        }

        /// <summary>
        /// Writes the tracks into <paramref name="outputFolder"/>. A FLAC image is decoded to a temporary WAV first. Existing files are
        /// not overwritten (an IOException is thrown before anything is written). <paramref name="progress"/> gets (track index, percent).
        /// </summary>
        public static async Task SplitAsync(CueSheet sheet, string image, string outputFolder, SplitFormat format, bool hiddenTrack,
                                            IProgress<Tuple<int, int>> progress, CancellationToken cancellation)
        {
            string tempImage = null;
            var written = new List<string>();
            bool finished = false;
            try
            {
                string wav = image;
                if (string.Equals(Path.GetExtension(image), ".flac", StringComparison.OrdinalIgnoreCase))
                {
                    tempImage = Path.Combine(Path.GetTempPath(), "cdtag-split-" + Guid.NewGuid().ToString("N") + ".wav");
                    progress?.Report(Tuple.Create(-1, 0));
                    await AudioEncoder.DecodeAsync(image, tempImage, null, cancellation).ConfigureAwait(false);
                    wav = tempImage;
                }

                using (var stream = new FileStream(wav, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    WavFormat pcm = WavFormat.Read(stream);
                    List<SplitTrack> tracks = Plan(sheet, pcm.SampleRate, format, hiddenTrack);
                    if (tracks.Count == 0)
                        throw new InvalidDataException("The CUE sheet has no audio tracks.");
                    string exists = tracks.Select(t => Path.Combine(outputFolder, t.FileName)).FirstOrDefault(File.Exists);
                    if (exists != null)
                        throw new IOException(Path.GetFileName(exists) + " already exists in " + outputFolder + ".");
                    if (tracks.Last().StartSample >= pcm.Samples)
                        throw new InvalidDataException("The audio file is shorter than the CUE sheet says.");
                    Directory.CreateDirectory(outputFolder);

                    int trackCount = tracks.Count(t => t.Number > 0);
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        SplitTrack track = tracks[i];
                        long end = track.EndSample < 0 || track.EndSample > pcm.Samples ? pcm.Samples : track.EndSample;
                        string target = Path.Combine(outputFolder, track.FileName);
                        string trackWav = format == SplitFormat.Wav ? target + ".~cdtag" : Path.Combine(Path.GetTempPath(), "cdtag-track-" + Guid.NewGuid().ToString("N") + ".wav");
                        int index = i;
                        try
                        {
                            await CopyRangeAsync(stream, pcm, track.StartSample, end, trackWav,
                                                 new Progress<int>(p => progress?.Report(Tuple.Create(index, format == SplitFormat.Wav ? p : p / 2))), cancellation).ConfigureAwait(false);
                            if (format == SplitFormat.Wav)
                            {
                                File.Move(trackWav, target);
                            }
                            else
                            {
                                await AudioEncoder.EncodeFlacAsync(trackWav, target, Tags(sheet, track, trackCount),
                                                                   new Progress<int>(p => progress?.Report(Tuple.Create(index, 50 + p / 2))), cancellation).ConfigureAwait(false);
                            }
                            written.Add(target);
                        }
                        finally
                        {
                            TryDelete(trackWav);
                        }
                        progress?.Report(Tuple.Create(index, 100));
                    }
                }
                finished = true;
            }
            finally
            {
                TryDelete(tempImage);
                // Cancelled or failed: do not leave half a split behind.
                if (!finished)
                    written.ForEach(TryDelete);
            }
        }

        private static async Task CopyRangeAsync(Stream source, WavFormat pcm, long startSample, long endSample, string target, IProgress<int> progress, CancellationToken cancellation)
        {
            long length = (endSample - startSample) * pcm.BlockAlign;
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
            {
                byte[] header = pcm.Header(length);
                await output.WriteAsync(header, 0, header.Length, cancellation).ConfigureAwait(false);
                source.Position = pcm.DataOffset + startSample * pcm.BlockAlign;
                var buffer = new byte[1 << 18];
                long left = length;
                while (left > 0)
                {
                    int read = await source.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, left), cancellation).ConfigureAwait(false);
                    if (read <= 0)
                        throw new EndOfStreamException("The audio file ended early.");
                    await output.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false);
                    left -= read;
                    progress?.Report((int)(100 * (length - left) / Math.Max(1, length)));
                }
            }
        }

        private static void TryDelete(string path)
        {
            if (path == null)
                return;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

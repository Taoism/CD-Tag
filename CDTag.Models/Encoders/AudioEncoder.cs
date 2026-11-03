using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CDTag.Common.Settings;
using CDTag.Model.Tag;
using IdSharp.Tagging.ID3v2;

namespace CDTag.Model.Encoders
{
    public enum EncodePreset
    {
        /// <summary>LAME -V0 (scene MP3 rules 10.2.2: CD, vinyl and other non-WEB sources).</summary>
        LameV0,
        /// <summary>LAME -b 320 (scene MP3 rules 10.2.1: lossless WEB sources).</summary>
        Lame320
    }

    /// <summary>
    /// Encodes WAV/FLAC to MP3 with lame.exe and decodes FLAC/MP3 to WAV, using the encoders in the Encoders folder
    /// next to cdtag.exe (or the paths set in Options). Tags of the source are copied to the MP3 (ID3v2).
    /// </summary>
    public static class AudioEncoder
    {
        private static readonly Regex Percent = new Regex(@"\(\s*(\d{1,3})%\)", RegexOptions.CultureInvariant);

        /// <summary>Folder (under Encoders next to cdtag.exe) for your own lame.exe.</summary>
        public const string UserLameFolder = "mp3";
        /// <summary>Folder (under Encoders next to cdtag.exe) for your own flac.exe.</summary>
        public const string UserFlacFolder = "flac";

        /// <summary>lame.exe: the Options path, then Encoders\mp3 next to cdtag.exe (yours), then the built-in LAME, then PATH. Null when not found.</summary>
        public static string LamePath
        {
            get { return Find(AppOptions.Current.LamePath, UserLameFolder, "lame", "lame.exe"); }
        }

        /// <summary>flac.exe: the Options path, then Encoders\flac next to cdtag.exe (yours), then the built-in FLAC, then PATH. Null when not found.</summary>
        public static string FlacPath
        {
            get { return Find(AppOptions.Current.FlacPath, UserFlacFolder, "flac", "flac.exe"); }
        }

        /// <summary>The folder with cdtag.exe (the starter, for a release build: the folder above bin\).</summary>
        public static string ProgramDirectory
        {
            get
            {
                string baseDirectory = AppContext.BaseDirectory.TrimEnd('\\', '/');
                return string.Equals(Path.GetFileName(baseDirectory), "bin", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(baseDirectory)
                    : baseDirectory;
            }
        }

        /// <summary>Your own encoder: Encoders\mp3\lame.exe or Encoders\flac\flac.exe next to cdtag.exe. Null when not there.</summary>
        public static string UserPath(string userFolder, string exe)
        {
            string path = Path.Combine(ProgramDirectory, "Encoders", userFolder, exe);
            return File.Exists(path) ? path : null;
        }

        private static string Find(string configured, string userFolder, string folder, string exe)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;
            string user = UserPath(userFolder, exe);
            if (user != null)
                return user;
            string bundled = BundledPath(folder, exe);
            if (bundled != null)
                return bundled;
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim('"'), exe);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException)
                {
                }
            }
            return null;
        }

        /// <summary>
        /// The encoder shipped with CD-Tag (LAME 3.100, FLAC 1.5.0): bin\Encoders\&lt;folder&gt;\&lt;exe&gt; under the folder with cdtag.exe.
        /// Null when it is not there.
        /// </summary>
        public static string BundledPath(string folder, string exe)
        {
            string path = Path.Combine(ProgramDirectory, "bin", "Encoders", folder, exe);
            return File.Exists(path) ? path : null;
        }

        /// <summary>"LAME 3.100" / "flac 1.3.1" from the exe's --version output; null when it can't be run.</summary>
        public static string GetVersion(string exe)
        {
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return null;
            try
            {
                var info = new ProcessStartInfo(exe, "--version")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit(5000);
                    Match lame = Regex.Match(output, @"LAME (?:\d+bits )?version (\S+)");
                    if (lame.Success)
                        return "LAME " + lame.Groups[1].Value + (output.Contains("64bits") ? " (64-bit)" : "");
                    Match flac = Regex.Match(output, @"flac (\S+)");
                    return flac.Success ? "flac " + flac.Groups[1].Value : output.Split('\n').FirstOrDefault()?.Trim();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                return null;
            }
        }

        public static string[] LameArguments(EncodePreset preset)
        {
            return preset == EncodePreset.Lame320 ? new[] { "-b", "320" } : new[] { "-V0" };
        }

        public static string Describe(EncodePreset preset)
        {
            return preset == EncodePreset.Lame320 ? "LAME 3.100 -b 320" : "LAME 3.100 -V0";
        }

        public static bool CanEncode(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".wav" || extension == ".flac";
        }

        public static bool CanDecode(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".flac" || extension == ".mp3";
        }

        /// <summary>
        /// Encodes <paramref name="source"/> (WAV or FLAC) to <paramref name="target"/> with LAME. FLAC is decoded to a
        /// temporary WAV first. Writes to a temporary name and renames at the end, so a cancelled encode leaves nothing behind.
        /// </summary>
        public static async Task EncodeMp3Async(string source, string target, EncodePreset preset, IProgress<int> progress, CancellationToken cancellation)
        {
            string lame = LamePath;
            if (lame == null)
                throw new FileNotFoundException("lame.exe was not found. Put it in the Encoders\\lame folder or set it in Tools > Options.");

            string wav = source;
            string tempWav = null;
            string tempMp3 = target + ".~cdtag";
            try
            {
                if (string.Equals(Path.GetExtension(source), ".flac", StringComparison.OrdinalIgnoreCase))
                {
                    tempWav = Path.Combine(Path.GetTempPath(), "cdtag-" + Guid.NewGuid().ToString("N") + ".wav");
                    await DecodeFlacAsync(source, tempWav, cancellation).ConfigureAwait(false);
                    wav = tempWav;
                }

                var arguments = new List<string>(LameArguments(preset)) { "--nohist", wav, tempMp3 };
                await RunAsync(lame, arguments, progress, cancellation).ConfigureAwait(false);
                if (!File.Exists(tempMp3))
                    throw new IOException("LAME did not write " + Path.GetFileName(target) + ".");

                CopyTags(source, tempMp3);
                File.Move(tempMp3, target, true);
                progress?.Report(100);
            }
            finally
            {
                TryDelete(tempWav);
                TryDelete(tempMp3);
            }
        }

        /// <summary>Decodes FLAC (flac.exe) or MP3 (lame.exe --decode) to a WAV.</summary>
        public static async Task DecodeAsync(string source, string target, IProgress<int> progress, CancellationToken cancellation)
        {
            string temp = target + ".~cdtag";
            try
            {
                if (string.Equals(Path.GetExtension(source), ".flac", StringComparison.OrdinalIgnoreCase))
                {
                    await DecodeFlacAsync(source, temp, cancellation).ConfigureAwait(false);
                }
                else
                {
                    string lame = LamePath;
                    if (lame == null)
                        throw new FileNotFoundException("lame.exe was not found. Put it in the Encoders\\lame folder or set it in Tools > Options.");
                    await RunAsync(lame, new List<string> { "--decode", "--nohist", source, temp }, progress, cancellation).ConfigureAwait(false);
                }
                File.Move(temp, target, true);
                progress?.Report(100);
            }
            finally
            {
                TryDelete(temp);
            }
        }

        private static readonly Dictionary<string, bool> FlacThreadSupport = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Threads for one FLAC encode: flac 1.5.0 and later encode in several threads (--threads); older versions don't know
        /// the option, so they get 1. Up to 8 threads (more gains little for one CD track).
        /// </summary>
        public static int FlacThreads(string flac)
        {
            bool supported;
            lock (FlacThreadSupport)
            {
                if (!FlacThreadSupport.TryGetValue(flac, out supported))
                {
                    Match m = Regex.Match(GetVersion(flac) ?? "", @"flac (\d+)\.(\d+)");
                    supported = m.Success && (int.Parse(m.Groups[1].Value) > 1 || (int.Parse(m.Groups[1].Value) == 1 && int.Parse(m.Groups[2].Value) >= 5));
                    FlacThreadSupport[flac] = supported;
                }
            }
            return supported ? Math.Clamp(Environment.ProcessorCount, 1, 8) : 1;
        }

        /// <summary>
        /// Encodes a WAV to FLAC (-8, with an audio MD5) and writes <paramref name="tags"/> as Vorbis comments ("ARTIST", "TITLE"...).
        /// Writes to a temporary name and renames at the end.
        /// </summary>
        public static async Task EncodeFlacAsync(string wav, string target, IEnumerable<KeyValuePair<string, string>> tags, IProgress<int> progress, CancellationToken cancellation)
        {
            string flac = FlacPath;
            if (flac == null)
                throw new FileNotFoundException("flac.exe was not found. Put it in the Encoders\\flac folder or set it in Tools > Options.");

            string temp = target + ".~cdtag";
            try
            {
                var arguments = new List<string> { "-8", "-f" };
                int threads = FlacThreads(flac);
                if (threads > 1)
                    arguments.Add("--threads=" + threads.ToString(System.Globalization.CultureInfo.InvariantCulture));
                arguments.AddRange(new[] { "-o", temp, wav });
                await RunAsync(flac, arguments, progress, cancellation).ConfigureAwait(false);
                if (!File.Exists(temp))
                    throw new IOException("flac.exe did not write " + Path.GetFileName(target) + ".");
                // Tags are written here rather than with flac -T: flac.exe gets its command line in the ANSI code page,
                // which broke characters such as ’ (U+2019). Vorbis comments are UTF-8.
                if (tags != null)
                {
                    var vorbis = new IdSharp.Tagging.VorbisComment.VorbisComment(temp);
                    foreach (KeyValuePair<string, string> tag in tags.Where(t => !string.IsNullOrWhiteSpace(t.Value)))
                        vorbis.Items.SetValue(tag.Key, tag.Value.Trim());
                    vorbis.Save(temp);
                }
                File.Move(temp, target, true);
                progress?.Report(100);
            }
            finally
            {
                TryDelete(temp);
            }
        }

        private static Task DecodeFlacAsync(string source, string wav, CancellationToken cancellation)
        {
            string flac = FlacPath;
            if (flac == null)
                throw new FileNotFoundException("flac.exe was not found. Put it in the Encoders\\flac folder or set it in Tools > Options.");
            return RunAsync(flac, new List<string> { "-d", "-s", "-f", "-o", wav, source }, null, cancellation);
        }

        /// <summary>Copies the source's artist/title/album/year/genre/track/disc to an ID3v2 tag on the MP3.</summary>
        public static void CopyTags(string source, string mp3)
        {
            if (string.Equals(Path.GetExtension(source), ".wav", StringComparison.OrdinalIgnoreCase))
                return;
            AlbumTrack track;
            try
            {
                track = new AlbumTrack(source);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is NotSupportedException)
            {
                return;
            }
            if (new[] { track.Artist, track.Title, track.Album, track.ReleaseDate, track.Genre, track.TrackNumber }.All(string.IsNullOrWhiteSpace))
                return;

            var id3v2 = new ID3v2Tag(mp3);
            id3v2.Artist = track.Artist;
            id3v2.Title = track.Title;
            id3v2.Album = track.Album;
            id3v2.Year = track.ReleaseDate;
            id3v2.Genre = track.Genre;
            id3v2.TrackNumber = track.TrackNumber;
            id3v2.DiscNumber = track.DiscNumber;
            id3v2.Save(mp3);
        }

        private static async Task RunAsync(string exe, IList<string> arguments, IProgress<int> progress, CancellationToken cancellation)
        {
            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(exe)
            };
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);

            using (var process = new Process { StartInfo = info })
            {
                process.Start();
                var errors = new StringBuilder();
                Task output = process.StandardOutput.ReadToEndAsync();
                Task error = ReadProgressAsync(process.StandardError, progress, errors);
                try
                {
                    await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        process.Kill(true);
                        process.WaitForExit(5000);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    throw;
                }
                await Task.WhenAll(output, error).ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    string message = errors.ToString().Trim();
                    throw new IOException(string.Format("{0} failed (exit code {1}){2}", Path.GetFileName(exe), process.ExitCode,
                                                        message.Length > 0 ? ": " + message.Split('\n').Last().Trim() : "."));
                }
            }
        }

        /// <summary>LAME redraws "1234/5678 (22%)" lines with \r; reports the percentage.</summary>
        private static async Task ReadProgressAsync(StreamReader reader, IProgress<int> progress, StringBuilder errors)
        {
            var buffer = new char[1024];
            var line = new StringBuilder();
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    char c = buffer[i];
                    if (c == '\r' || c == '\n')
                    {
                        string text = line.ToString();
                        Match match = Percent.Match(text);
                        if (match.Success)
                            progress?.Report(Math.Min(99, int.Parse(match.Groups[1].Value)));
                        else if (text.Trim().Length > 0 && errors.Length < 4000)
                            errors.AppendLine(text);
                        line.Clear();
                    }
                    else
                    {
                        line.Append(c);
                    }
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

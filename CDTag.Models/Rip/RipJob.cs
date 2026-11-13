using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CDTag.Model.Encoders;
using CDTag.Model.Lookup;
using CDTag.Model.Tag;

namespace CDTag.Model.Rip
{
    public enum RipFormat
    {
        Wav,
        Flac,
        Mp3
    }

    /// <summary>Rip settings (Rip > Options), started from EAC's settings.</summary>
    public class RipSettings
    {
        public string OutputFolder { get; set; }
        public string Naming { get; set; }
        public string VariousNaming { get; set; }
        public bool UseVariousNaming { get; set; }
        public RipFormat Format { get; set; }
        public EncodePreset Mp3Preset { get; set; }
        /// <summary>Read offset in samples; null = from AccurateRip's drive database.</summary>
        public int? ReadOffset { get; set; }
        public bool TestAndCopy { get; set; } = true;
        /// <summary>Copy Image &amp; Create CUE Sheet: one file for the whole disc instead of one per track.</summary>
        public bool Image { get; set; }
        /// <summary>Read speed in "x"; 0 = maximum.</summary>
        public int Speed { get; set; }
        public bool UseC2 { get; set; }
        public bool Overread { get; set; }
        public bool EjectWhenDone { get; set; }
        public bool WriteLog { get; set; } = true;
        public bool WriteCue { get; set; } = true;
        public EacSettings Eac { get; set; }

        public static RipSettings FromEac(EacSettings eac)
        {
            RipFormat format = eac.Format == "flac" ? RipFormat.Flac : eac.Format == "mp3" ? RipFormat.Mp3 : RipFormat.Wav;
            return new RipSettings
            {
                Eac = eac,
                OutputFolder = eac.OutputFolder,
                Naming = eac.Naming,
                VariousNaming = eac.VariousNaming,
                UseVariousNaming = eac.UseVariousNaming,
                Format = format,
                Mp3Preset = EncodePreset.LameV0,
                ReadOffset = eac.DriveOffset.HasValue && eac.DriveOffset.Value != 0 ? eac.DriveOffset : null,
                EjectWhenDone = eac.EjectWhenDone
            };
        }

        public string Extension
        {
            get { return Format == RipFormat.Flac ? ".flac" : Format == RipFormat.Mp3 ? ".mp3" : ".wav"; }
        }
    }

    /// <summary>Album and track fields from the Rip tab.</summary>
    public class RipAlbum
    {
        public string Artist { get; set; }
        public string Title { get; set; }
        public string Year { get; set; }
        public string Genre { get; set; }
        public bool IsVariousArtists { get; set; }
        /// <summary>Per audio track: title and artist (artist empty = the album artist).</summary>
        public List<KeyValuePair<string, string>> Tracks { get; set; } = new List<KeyValuePair<string, string>>();

        public string TrackTitle(int i) { return i < Tracks.Count && !string.IsNullOrWhiteSpace(Tracks[i].Key) ? Tracks[i].Key.Trim() : "Track " + (i + 1).ToString("00", CultureInfo.InvariantCulture); }
        public string TrackArtist(int i) { return i < Tracks.Count && !string.IsNullOrWhiteSpace(Tracks[i].Value) ? Tracks[i].Value.Trim() : (Artist ?? "").Trim(); }
        public string AlbumArtist { get { return IsVariousArtists ? "Various Artists" : (Artist ?? "").Trim(); } }
    }

    /// <summary>Progress of the whole rip for the Rip tab: track index, status text.</summary>
    public class RipJobProgress
    {
        public int TrackIndex { get; set; }
        public string Status { get; set; }
        /// <summary>0-100 for the whole disc.</summary>
        public double Total { get; set; }
    }

    /// <summary>
    /// A whole rip: works out the file names (EAC naming scheme), rips every audio track with <see cref="CdRipper"/>,
    /// checks AccurateRip, encodes (FLAC/MP3 with tags) and writes a log and a CUE sheet.
    /// </summary>
    public class RipJob
    {
        /// <summary>"CD-Tag 3.1 extraction log" (the program version).</summary>
        public static readonly string LogTitle = "CD-Tag " + VersionText() + " extraction log";

        private static string VersionText()
        {
            Version v = typeof(RipJob).Assembly.GetName().Version;
            return v == null ? "" : v.Major.ToString(CultureInfo.InvariantCulture) + "." + v.Minor.ToString(CultureInfo.InvariantCulture);
        }

        public List<CdTrack> Toc { get; set; }
        public RipAlbum Album { get; set; }
        public RipSettings Settings { get; set; }
        public string DriveName { get; set; }
        public int Offset { get; set; }
        public string OffsetSource { get; set; }
        /// <summary>ISRCs, catalog number, CD-Text and pregaps read by Find CD (null when not read).</summary>
        public DiscExtras Extras { get; set; }

        public List<RipTrackResult> Results { get; private set; } = new List<RipTrackResult>();
        public List<string> AccurateRipLines { get; private set; } = new List<string>();
        /// <summary>Per track: the CUETools DB result ("CTDB: accurately ripped (confidence 98/101)").</summary>
        public List<string> CtdbLines { get; private set; } = new List<string>();
        public int CtdbTracks { get; private set; }
        public bool CtdbFound { get; private set; }
        public string LogPath { get; private set; }
        public int AccurateTracks { get; private set; }
        public bool HadErrors { get; private set; }
        /// <summary>C2 was asked for but the drive doesn't return C2 data (ripped without it).</summary>
        public bool C2Unsupported { get; private set; }

        private List<CdTrack> Audio { get { return Toc.Where(t => t.IsAudio).ToList(); } }

        /// <summary>Full output paths of the tracks (with the extension of the output format).</summary>
        public List<string> TrackPaths()
        {
            EacSettings eac = Settings.Eac ?? EacSettings.Defaults();
            string scheme = Album.IsVariousArtists && Settings.UseVariousNaming ? Settings.VariousNaming : Settings.Naming;
            var paths = new List<string>();
            for (int i = 0; i < Audio.Count; i++)
            {
                string relative = eac.TrackPath(scheme, Album.AlbumArtist, Album.Title, Album.Year, Album.Genre, Audio[i].Number,
                                                Album.TrackArtist(i), Album.TrackTitle(i));
                if (relative.Length == 0)
                    relative = Audio[i].Number.ToString("00", CultureInfo.InvariantCulture);
                paths.Add(Path.Combine(Settings.OutputFolder ?? "", relative) + Settings.Extension);
            }
            return paths;
        }

        public async Task RunAsync(CdDrive drive, IProgress<RipJobProgress> progress, CancellationToken cancellation)
        {
            if (Settings.Image)
            {
                await RunImageAsync(drive, progress, cancellation).ConfigureAwait(false);
                return;
            }
            List<CdTrack> audio = Audio;
            List<string> paths = TrackPaths();
            foreach (string folder in paths.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase))
                Directory.CreateDirectory(folder);

            List<int> starts; int leadOut;
            AccurateRip.AudioLayout(Toc, out starts, out leadOut);
            Task<List<List<AccurateRipEntry>>> arTask = AccurateRip.LookupAsync(starts, leadOut, cancellation);
            Task<List<CDTag.Model.Lookup.CtdbEntry>> ctdbTask = new CDTag.Model.Lookup.CueToolsDbClient().LookupRipsAsync(ToDiscToc(Toc), cancellation);

            drive.SetReadSpeed(Settings.Speed);
            List<CdTrack> audioTracks = Audio;
            C2Unsupported = Settings.UseC2 && audioTracks.Count > 0 && !drive.SupportsC2(audioTracks[0].StartSector + 75);
            var ripper = new CdRipper(drive, Toc, Offset) { TestAndCopy = Settings.TestAndCopy, UseC2 = Settings.UseC2 && !C2Unsupported, Overread = Settings.Overread };
            long totalSectors = audio.Sum(t => (long)t.Sectors) * (Settings.TestAndCopy ? 2 : 1);
            long doneSectors = 0;
            List<List<AccurateRipEntry>> ar = null;

            for (int i = 0; i < audio.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                string target = paths[i];
                string wav = Settings.Format == RipFormat.Wav ? target : Path.ChangeExtension(target, ".~rip.wav");
                int index = i;
                long before = doneSectors;
                var trackProgress = new Progress<RipProgress>(p =>
                {
                    long passBase = before + (p.Pass == "Copy" && Settings.TestAndCopy ? audio[index].Sectors : 0);
                    double total = 100.0 * (passBase + audio[index].Sectors * p.Percent / 100.0) / Math.Max(1, totalSectors);
                    progress?.Report(new RipJobProgress
                    {
                        TrackIndex = index, Total = total,
                        Status = string.Format(CultureInfo.InvariantCulture, "{0} {1}% ({2:0.0}x)", p.Pass == "Test" ? "Testing" : "Copying", p.Percent, p.Speed)
                    });
                });

                RipTrackResult r = await Task.Run(() => ripper.RipTrack(index, wav, trackProgress, cancellation), cancellation).ConfigureAwait(false);
                Results.Add(r);
                doneSectors += audio[i].Sectors * (Settings.TestAndCopy ? 2 : 1);

                if (ar == null && arTask.IsCompleted)
                    ar = arTask.Result;
                string arText = "";
                if (ar != null)
                {
                    bool ok; int confidence;
                    arText = AccurateRip.Describe(ar[i], r.ArV1, r.ArV2, out ok, out confidence);
                }

                if (Settings.Format != RipFormat.Wav)
                {
                    progress?.Report(new RipJobProgress { TrackIndex = i, Total = 100.0 * doneSectors / Math.Max(1, totalSectors), Status = "Encoding..." });
                    try
                    {
                        await EncodeAsync(i, wav, target, cancellation).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (File.Exists(wav))
                            File.Delete(wav);
                    }
                }
                bool errors = !r.CrcsMatch || r.Suspicious.Count > 0;
                HadErrors |= errors;
                progress?.Report(new RipJobProgress
                {
                    TrackIndex = i, Total = 100.0 * doneSectors / Math.Max(1, totalSectors),
                    Status = errors ? (r.Suspicious.Count > 0 ? "Suspicious positions" : "CRC mismatch") : arText.StartsWith("Accurately", StringComparison.Ordinal) ? "OK, accurate" : "OK"
                });
            }

            await FinishAsync(arTask, ctdbTask, paths, CueSheetText(paths)).ConfigureAwait(false);
        }

        /// <summary>The AccurateRip and CTDB results, then the CUE sheet and the log.</summary>
        private async Task FinishAsync(Task<List<List<AccurateRipEntry>>> arTask, Task<List<CDTag.Model.Lookup.CtdbEntry>> ctdbTask, List<string> paths, string cueText)
        {
            List<List<AccurateRipEntry>> ar = await arTask.ConfigureAwait(false);
            AccurateRipLines.Clear();
            AccurateTracks = 0;
            for (int i = 0; i < Results.Count; i++)
            {
                bool ok; int confidence;
                string text = AccurateRip.Describe(ar == null ? null : ar[i], Results[i].ArV1, Results[i].ArV2, out ok, out confidence);
                if (ok) AccurateTracks++;
                AccurateRipLines.Add(text);
            }

            List<CDTag.Model.Lookup.CtdbEntry> ctdb = await ctdbTask.ConfigureAwait(false);
            CtdbLines.Clear();
            CtdbTracks = 0;
            for (int i = 0; i < Results.Count; i++)
            {
                bool ok; int confidence;
                CtdbLines.Add(Ctdb.Describe(ctdb, i, Results[i].CtdbCrc, out ok, out confidence));
                if (ok) CtdbTracks++;
            }
            CtdbFound = ctdb != null && ctdb.Count > 0;

            string baseName = BaseName();
            if (Settings.WriteCue)
                await File.WriteAllTextAsync(baseName + ".cue", cueText, new UTF8Encoding(true)).ConfigureAwait(false);
            if (Settings.WriteLog)
            {
                LogPath = baseName + ".log";
                await File.WriteAllTextAsync(LogPath, LogText(paths), new UTF8Encoding(true)).ConfigureAwait(false);
            }
        }

        /// <summary>"…\Album folder\Artist - Album" (no extension): the image, CUE sheet and log file names.</summary>
        private string BaseName()
        {
            string albumFolder = Path.GetDirectoryName(TrackPaths()[0]);
            return Path.Combine(albumFolder, (Settings.Eac ?? EacSettings.Defaults()).Clean(Album.AlbumArtist + " - " + Album.Title).Trim());
        }

        /// <summary>The whole-disc image ("Artist - Album.flac" in the album folder).</summary>
        public string ImagePath()
        {
            return BaseName() + Settings.Extension;
        }

        /// <summary>The files a rip will write: the image, or one file per track.</summary>
        public List<string> OutputPaths()
        {
            return Settings.Image ? new List<string> { ImagePath() } : TrackPaths();
        }

        /// <summary>
        /// Copy Image &amp; Create CUE Sheet: the whole disc (from sector 0, so audio before track 1 is included) as one file,
        /// read track by track with test &amp; copy so each track still gets its CRCs, AccurateRip and CTDB results.
        /// </summary>
        private async Task RunImageAsync(CdDrive drive, IProgress<RipJobProgress> progress, CancellationToken cancellation)
        {
            List<CdTrack> audio = Audio;
            string image = ImagePath();
            Directory.CreateDirectory(Path.GetDirectoryName(image));

            List<int> starts; int leadOut;
            AccurateRip.AudioLayout(Toc, out starts, out leadOut);
            Task<List<List<AccurateRipEntry>>> arTask = AccurateRip.LookupAsync(starts, leadOut, cancellation);
            Task<List<CDTag.Model.Lookup.CtdbEntry>> ctdbTask = new CDTag.Model.Lookup.CueToolsDbClient().LookupRipsAsync(ToDiscToc(Toc), cancellation);

            drive.SetReadSpeed(Settings.Speed);
            C2Unsupported = Settings.UseC2 && audio.Count > 0 && !drive.SupportsC2(audio[0].StartSector + 75);
            var ripper = new CdRipper(drive, Toc, Offset) { TestAndCopy = Settings.TestAndCopy, UseC2 = Settings.UseC2 && !C2Unsupported, Overread = Settings.Overread };
            long totalSectors = audio.Sum(t => (long)t.Sectors) * (Settings.TestAndCopy ? 2 : 1);
            long doneSectors = 0;

            string wav = Settings.Format == RipFormat.Wav ? image + ".~cdtag" : Path.ChangeExtension(image, ".~rip.wav");
            try
            {
                using (var output = new FileStream(wav, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    byte[] header = CdRipper.CdFormat.Header((long)leadOut * CdDrive.SamplesPerSector * 4);
                    await output.WriteAsync(header, cancellation).ConfigureAwait(false);
                    await Task.Run(() => ripper.RipBeforeFirstTrack(output, cancellation), cancellation).ConfigureAwait(false);
                    for (int i = 0; i < audio.Count; i++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int index = i;
                        long before = doneSectors;
                        var trackProgress = new Progress<RipProgress>(p =>
                        {
                            long passBase = before + (p.Pass == "Copy" && Settings.TestAndCopy ? audio[index].Sectors : 0);
                            progress?.Report(new RipJobProgress
                            {
                                TrackIndex = index, Total = 100.0 * (passBase + audio[index].Sectors * p.Percent / 100.0) / Math.Max(1, totalSectors),
                                Status = string.Format(CultureInfo.InvariantCulture, "{0} {1}% ({2:0.0}x)", p.Pass == "Test" ? "Testing" : "Copying", p.Percent, p.Speed)
                            });
                        });
                        RipTrackResult r = await Task.Run(() => ripper.RipTrackTo(index, output, trackProgress, cancellation), cancellation).ConfigureAwait(false);
                        r.WavPath = image;
                        Results.Add(r);
                        doneSectors += audio[i].Sectors * (Settings.TestAndCopy ? 2 : 1);
                        bool errors = !r.CrcsMatch || r.Suspicious.Count > 0;
                        HadErrors |= errors;
                        progress?.Report(new RipJobProgress
                        {
                            TrackIndex = i, Total = 100.0 * doneSectors / Math.Max(1, totalSectors),
                            Status = errors ? (r.Suspicious.Count > 0 ? "Suspicious positions" : "CRC mismatch") : "OK"
                        });
                    }
                }

                if (Settings.Format == RipFormat.Wav)
                {
                    File.Move(wav, image, true);
                }
                else
                {
                    progress?.Report(new RipJobProgress { TrackIndex = audio.Count - 1, Total = 100, Status = "Encoding the image..." });
                    await EncodeImageAsync(wav, image, cancellation).ConfigureAwait(false);
                }
            }
            finally
            {
                if (File.Exists(wav))
                    File.Delete(wav);
            }

            await FinishAsync(arTask, ctdbTask, audio.Select(_ => image).ToList(), ImageCueText(image)).ConfigureAwait(false);
        }

        /// <summary>The image gets album tags only (track titles are in the CUE sheet).</summary>
        private async Task EncodeImageAsync(string wav, string target, CancellationToken cancellation)
        {
            if (Settings.Format == RipFormat.Flac)
            {
                var tags = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("ARTIST", Album.AlbumArtist),
                    new KeyValuePair<string, string>("ALBUM", Album.Title),
                    new KeyValuePair<string, string>("DATE", Album.Year),
                    new KeyValuePair<string, string>("GENRE", Album.Genre)
                };
                if (Extras != null && Extras.CatalogNumber != null)
                    tags.Add(new KeyValuePair<string, string>("CATALOGNUMBER", Extras.CatalogNumber));
                await AudioEncoder.EncodeFlacAsync(wav, target, tags, null, cancellation).ConfigureAwait(false);
                return;
            }
            await AudioEncoder.EncodeMp3Async(wav, target, Settings.Mp3Preset, null, cancellation).ConfigureAwait(false);
            var values = new TagValues();
            values[TagField.Artist] = Album.AlbumArtist;
            values[TagField.Album] = Album.Title;
            values[TagField.Year] = Album.Year;
            values[TagField.Genre] = Album.Genre;
            MassTagger.Write(target, values, TagValues.Fields.Where(f => values[f].Length > 0).ToList());
        }

        /// <summary>
        /// The CUE sheet of an image: one FILE, every track with INDEX 01 at its start and INDEX 00 where its pregap begins
        /// (track 1: INDEX 00 at 00:00:00 when there is audio before it).
        /// </summary>
        public string ImageCueText(string imagePath)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(Album.Genre)) sb.AppendFormat("REM GENRE \"{0}\"\r\n", Album.Genre.Trim());
            if (!string.IsNullOrWhiteSpace(Album.Year)) sb.AppendFormat("REM DATE {0}\r\n", Album.Year.Trim());
            sb.AppendFormat("REM COMMENT \"{0}\"\r\n", LogTitle.Replace(" extraction log", ""));
            if (Extras != null && Extras.CatalogNumber != null) sb.AppendFormat("CATALOG {0}\r\n", Extras.CatalogNumber);
            sb.AppendFormat("PERFORMER \"{0}\"\r\n", Album.AlbumArtist);
            sb.AppendFormat("TITLE \"{0}\"\r\n", (Album.Title ?? "").Trim());
            sb.AppendFormat("FILE \"{0}\" {1}\r\n", Path.GetFileName(imagePath), Settings.Format == RipFormat.Mp3 ? "MP3" : "WAVE");
            List<CdTrack> audio = Audio;
            for (int i = 0; i < audio.Count; i++)
            {
                sb.AppendFormat("  TRACK {0:00} AUDIO\r\n", audio[i].Number);
                sb.AppendFormat("    TITLE \"{0}\"\r\n", Album.TrackTitle(i));
                sb.AppendFormat("    PERFORMER \"{0}\"\r\n", Album.TrackArtist(i));
                string isrc = IsrcOf(audio[i].Number);
                if (isrc != null) sb.AppendFormat("    ISRC {0}\r\n", isrc);
                if (audio[i].PreEmphasis) sb.Append("    FLAGS PRE\r\n");
                int gap = i == 0 ? audio[0].StartSector : (Extras == null ? 0 : Extras.Pregap(audio[i].Number));
                if (gap > 0)
                    sb.AppendFormat("    INDEX 00 {0}\r\n", CueTime(audio[i].StartSector - gap));
                sb.AppendFormat("    INDEX 01 {0}\r\n", CueTime(audio[i].StartSector));
            }
            return sb.ToString();
        }

        private async Task EncodeAsync(int i, string wav, string target, CancellationToken cancellation)
        {
            var tags = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("ARTIST", Album.TrackArtist(i)),
                new KeyValuePair<string, string>("TITLE", Album.TrackTitle(i)),
                new KeyValuePair<string, string>("ALBUM", Album.Title),
                new KeyValuePair<string, string>("ALBUMARTIST", Album.AlbumArtist),
                new KeyValuePair<string, string>("DATE", Album.Year),
                new KeyValuePair<string, string>("GENRE", Album.Genre),
                new KeyValuePair<string, string>("TRACKNUMBER", Audio[i].Number.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("TOTALTRACKS", Audio.Count.ToString(CultureInfo.InvariantCulture))
            };
            string isrc = IsrcOf(Audio[i].Number);
            if (isrc != null) tags.Add(new KeyValuePair<string, string>("ISRC", isrc));
            if (Extras != null && Extras.CatalogNumber != null) tags.Add(new KeyValuePair<string, string>("CATALOGNUMBER", Extras.CatalogNumber));
            if (Settings.Format == RipFormat.Flac)
            {
                await AudioEncoder.EncodeFlacAsync(wav, target, tags, null, cancellation).ConfigureAwait(false);
                return;
            }
            await AudioEncoder.EncodeMp3Async(wav, target, Settings.Mp3Preset, null, cancellation).ConfigureAwait(false);
            var values = new TagValues();
            values[TagField.Artist] = Album.TrackArtist(i);
            values[TagField.Title] = Album.TrackTitle(i);
            values[TagField.Album] = Album.Title;
            values[TagField.AlbumArtist] = Album.IsVariousArtists ? Album.AlbumArtist : "";
            values[TagField.Year] = Album.Year;
            values[TagField.Genre] = Album.Genre;
            values[TagField.TrackNumber] = Audio[i].Number.ToString(CultureInfo.InvariantCulture) + "/" + Audio.Count.ToString(CultureInfo.InvariantCulture);
            MassTagger.Write(target, values, TagValues.Fields.Where(f => values[f].Length > 0).ToList());
        }

        private static string Time(long sectors)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:00}", sectors / 4500, sectors / 75 % 60, sectors % 75);
        }

        /// <summary>A CUE sheet for the track files (gaps appended to the previous track, as ripped).</summary>
        private string IsrcOf(int track)
        {
            string isrc;
            return Extras != null && Extras.Isrc.TryGetValue(track, out isrc) ? isrc : null;
        }

        private static string CueTime(int sectors)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", sectors / 4500, sectors / 75 % 60, sectors % 75);
        }

        /// <summary>
        /// A CUE sheet for the track files, like EAC's for "gaps appended to previous track": a track's pregap (INDEX 00) is
        /// in the previous track's file, its INDEX 01 at the start of its own. With CATALOG and ISRC when the disc has them.
        /// </summary>
        public string CueSheetText(List<string> paths)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(Album.Genre)) sb.AppendFormat("REM GENRE \"{0}\"\r\n", Album.Genre.Trim());
            if (!string.IsNullOrWhiteSpace(Album.Year)) sb.AppendFormat("REM DATE {0}\r\n", Album.Year.Trim());
            sb.AppendFormat("REM COMMENT \"{0}\"\r\n", LogTitle.Replace(" extraction log", ""));
            if (Extras != null && Extras.CatalogNumber != null) sb.AppendFormat("CATALOG {0}\r\n", Extras.CatalogNumber);
            sb.AppendFormat("PERFORMER \"{0}\"\r\n", Album.AlbumArtist);
            sb.AppendFormat("TITLE \"{0}\"\r\n", (Album.Title ?? "").Trim());
            List<CdTrack> audio = Audio;
            string type = Settings.Format == RipFormat.Mp3 ? "MP3" : "WAVE";
            for (int i = 0; i < audio.Count; i++)
            {
                int gap = Extras == null ? 0 : Extras.Pregap(audio[i].Number);
                // the track's header: in the previous file when it has a pregap there
                if (gap == 0 || i == 0)
                    sb.AppendFormat("FILE \"{0}\" {1}\r\n", Path.GetFileName(paths[i]), type);
                sb.AppendFormat("  TRACK {0:00} AUDIO\r\n", audio[i].Number);
                sb.AppendFormat("    TITLE \"{0}\"\r\n", Album.TrackTitle(i));
                sb.AppendFormat("    PERFORMER \"{0}\"\r\n", Album.TrackArtist(i));
                string isrc = IsrcOf(audio[i].Number);
                if (isrc != null) sb.AppendFormat("    ISRC {0}\r\n", isrc);
                if (audio[i].PreEmphasis) sb.Append("    FLAGS PRE\r\n");
                if (gap > 0 && i > 0)
                {
                    sb.AppendFormat("    INDEX 00 {0}\r\n", CueTime(audio[i - 1].Sectors - gap));
                    sb.AppendFormat("FILE \"{0}\" {1}\r\n", Path.GetFileName(paths[i]), type);
                }
                sb.Append("    INDEX 01 00:00:00\r\n");
            }
            return sb.ToString();
        }

        /// <summary>A plain text log in the layout of an EAC log (it is CD-Tag's own, with no EAC checksum).</summary>
        public string LogText(List<string> paths)
        {
            var sb = new StringBuilder();
            sb.AppendLine(LogTitle);
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "Extraction logfile from {0:d. MMMM yyyy, HH:mm}\r\n\r\n", DateTime.Now);
            sb.AppendFormat("{0} / {1}\r\n\r\n", Album.AlbumArtist, Album.Title);
            sb.AppendFormat("Used drive  : {0}\r\n\r\n", DriveName);
            sb.AppendFormat("Read mode               : {0}\r\n", Settings.TestAndCopy ? "Secure (test & copy, up to 16 re-reads of mismatching blocks)" : "Secure (copy only)");
            sb.AppendLine("Defeat audio cache      : Yes (between passes and before every re-read)");
            sb.AppendLine("Make use of C2 pointers : " + (Settings.UseC2 && !C2Unsupported ? "Yes" : Settings.UseC2 ? "No (the drive does not return C2 data)" : "No"));
            sb.AppendLine("Read speed              : " + (Settings.Speed <= 0 ? "Maximum" : Settings.Speed.ToString(CultureInfo.InvariantCulture) + "x"));
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "Read offset correction                      : {0} ({1})\r\n", Offset, OffsetSource);
            sb.AppendLine("Overread into Lead-In and Lead-Out          : " + (Settings.Overread ? "Yes" : "No"));
            sb.AppendLine("Fill up missing offset samples with silence : Yes");
            sb.AppendLine(Settings.Image ? "Gap handling                                : Image (gaps in place, INDEX 00 in the CUE sheet)"
                        : Extras != null && Extras.GapsDetected ? "Gap handling                                : Appended to previous track"
                        : "Gap handling                                : Not detected, thus appended to previous track");
            sb.AppendFormat("Output format                               : {0}\r\n", Settings.Format == RipFormat.Flac ? (AudioEncoder.GetVersion(AudioEncoder.FlacPath) ?? "flac") + " -8" : Settings.Format == RipFormat.Mp3 ? AudioEncoder.Describe(Settings.Mp3Preset) : "WAV");
            sb.AppendLine("Sample format                               : 44.100 Hz; 16 Bit; Stereo");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("TOC of the extracted CD");
            sb.AppendLine();
            sb.AppendLine("     Track |   Start  |  Length  | Start sector | End sector ");
            sb.AppendLine("    ---------------------------------------------------------");
            foreach (CdTrack t in Toc)
                sb.AppendFormat(CultureInfo.InvariantCulture, "      {0,3}  | {1,8} | {2,8} |    {3,7}   |   {4,7}   {5}\r\n",
                                t.Number, Time(t.StartSector), Time(t.Sectors), t.StartSector, t.EndSector - 1, t.IsAudio ? "" : "(data)");
            sb.AppendLine();
            for (int i = 0; i < Results.Count; i++)
            {
                RipTrackResult r = Results[i];
                sb.AppendLine();
                sb.AppendFormat("Track {0,2}\r\n\r\n", r.Number);
                sb.AppendFormat("     Filename {0}\r\n\r\n", paths[i]);
                int pregap = Extras == null ? 0 : Extras.Pregap(r.Number);
                if (pregap > 0) sb.AppendFormat("     Pre-gap length  {0}\r\n\r\n", GapDetector.Time(pregap));
                sb.AppendFormat(CultureInfo.InvariantCulture, "     Peak level {0:0.0} %\r\n", r.Peak);
                string trackIsrc = IsrcOf(r.Number);
                if (trackIsrc != null) sb.AppendFormat("     ISRC {0}\r\n", trackIsrc);
                if (r.Tested) sb.AppendFormat("     Test CRC {0:X8}\r\n", r.TestCrc);
                sb.AppendFormat("     Copy CRC {0:X8}\r\n", r.CopyCrc);
                if (r.C2Errors > 0) sb.AppendFormat("     C2 errors reported by the drive in {0} sectors (re-read)\r\n", r.C2Errors);
                if (i < AccurateRipLines.Count) sb.AppendFormat("     {0}\r\n", AccurateRipLines[i]);
                if (i < CtdbLines.Count) sb.AppendFormat("     {0}\r\n", CtdbLines[i]);
                foreach (string s in r.Suspicious.Distinct())
                    sb.AppendFormat("     Suspicious position {0}\r\n", s);
                sb.AppendLine(r.CrcsMatch && r.Suspicious.Count == 0 ? "     Copy OK" : "     Copy finished");
            }
            sb.AppendLine();
            sb.AppendLine(HadErrors ? "There were errors" : "No errors occurred");
            sb.AppendLine();
            sb.AppendLine(AccurateTracks == Results.Count ? "All tracks accurately ripped"
                        : string.Format(CultureInfo.InvariantCulture, "{0} of {1} tracks accurately ripped", AccurateTracks, Results.Count));
            if (CtdbFound)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "CUETools DB: {0} of {1} tracks match", CtdbTracks, Results.Count));
            sb.AppendLine();
            sb.AppendLine("End of status report");
            return sb.ToString();
        }

        /// <summary>The disc's TOC as Tag Album's lookup type (MusicBrainz disc ID, CTDB).</summary>
        public static DiscToc ToDiscToc(List<CdTrack> toc)
        {
            var d = new DiscToc { Source = "CD", Disc = 1 };
            foreach (CdTrack t in toc)
            {
                d.Offsets.Add(t.StartSector);
                d.IsAudio.Add(t.IsAudio);
            }
            d.LeadOut = toc.Last().EndSector;
            return d;
        }
    }
}

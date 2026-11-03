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
        public const string LogTitle = "CD-Tag 3.0 extraction log";

        public List<CdTrack> Toc { get; set; }
        public RipAlbum Album { get; set; }
        public RipSettings Settings { get; set; }
        public string DriveName { get; set; }
        public int Offset { get; set; }
        public string OffsetSource { get; set; }

        public List<RipTrackResult> Results { get; private set; } = new List<RipTrackResult>();
        public List<string> AccurateRipLines { get; private set; } = new List<string>();
        public string LogPath { get; private set; }
        public int AccurateTracks { get; private set; }
        public bool HadErrors { get; private set; }

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
            List<CdTrack> audio = Audio;
            List<string> paths = TrackPaths();
            foreach (string folder in paths.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase))
                Directory.CreateDirectory(folder);

            List<int> starts; int leadOut;
            AccurateRip.AudioLayout(Toc, out starts, out leadOut);
            Task<List<List<AccurateRipEntry>>> arTask = AccurateRip.LookupAsync(starts, leadOut, cancellation);

            var ripper = new CdRipper(drive, Toc, Offset) { TestAndCopy = Settings.TestAndCopy };
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

            ar = await arTask.ConfigureAwait(false);
            AccurateRipLines.Clear();
            AccurateTracks = 0;
            for (int i = 0; i < Results.Count; i++)
            {
                bool ok; int confidence;
                string text = AccurateRip.Describe(ar == null ? null : ar[i], Results[i].ArV1, Results[i].ArV2, out ok, out confidence);
                if (ok) AccurateTracks++;
                AccurateRipLines.Add(text);
            }

            string albumFolder = Path.GetDirectoryName(paths[0]);
            string baseName = Path.Combine(albumFolder, (Settings.Eac ?? EacSettings.Defaults()).Clean(Album.AlbumArtist + " - " + Album.Title).Trim());
            if (Settings.WriteCue)
                File.WriteAllText(baseName + ".cue", CueSheetText(paths), new UTF8Encoding(true));
            if (Settings.WriteLog)
            {
                LogPath = baseName + ".log";
                File.WriteAllText(LogPath, LogText(paths), new UTF8Encoding(true));
            }
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
        public string CueSheetText(List<string> paths)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(Album.Genre)) sb.AppendFormat("REM GENRE \"{0}\"\r\n", Album.Genre.Trim());
            if (!string.IsNullOrWhiteSpace(Album.Year)) sb.AppendFormat("REM DATE {0}\r\n", Album.Year.Trim());
            sb.AppendFormat("REM COMMENT \"{0}\"\r\n", LogTitle.Replace(" extraction log", ""));
            sb.AppendFormat("PERFORMER \"{0}\"\r\n", Album.AlbumArtist);
            sb.AppendFormat("TITLE \"{0}\"\r\n", (Album.Title ?? "").Trim());
            List<CdTrack> audio = Audio;
            for (int i = 0; i < audio.Count; i++)
            {
                string type = Settings.Format == RipFormat.Mp3 ? "MP3" : "WAVE";
                sb.AppendFormat("FILE \"{0}\" {1}\r\n", Path.GetFileName(paths[i]), type);
                sb.AppendFormat("  TRACK {0:00} AUDIO\r\n", audio[i].Number);
                sb.AppendFormat("    TITLE \"{0}\"\r\n", Album.TrackTitle(i));
                sb.AppendFormat("    PERFORMER \"{0}\"\r\n", Album.TrackArtist(i));
                if (audio[i].PreEmphasis) sb.Append("    FLAGS PRE\r\n");
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
            sb.AppendLine("Make use of C2 pointers : No");
            sb.AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "Read offset correction                      : {0} ({1})\r\n", Offset, OffsetSource);
            sb.AppendLine("Fill up missing offset samples with silence : Yes");
            sb.AppendLine("Gap handling                                : Appended to previous track");
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
                sb.AppendFormat(CultureInfo.InvariantCulture, "     Peak level {0:0.0} %\r\n", r.Peak);
                if (r.Tested) sb.AppendFormat("     Test CRC {0:X8}\r\n", r.TestCrc);
                sb.AppendFormat("     Copy CRC {0:X8}\r\n", r.CopyCrc);
                if (i < AccurateRipLines.Count) sb.AppendFormat("     {0}\r\n", AccurateRipLines[i]);
                foreach (string s in r.Suspicious.Distinct())
                    sb.AppendFormat("     Suspicious position {0}\r\n", s);
                sb.AppendLine(r.CrcsMatch && r.Suspicious.Count == 0 ? "     Copy OK" : "     Copy finished");
            }
            sb.AppendLine();
            sb.AppendLine(HadErrors ? "There were errors" : "No errors occurred");
            sb.AppendLine();
            sb.AppendLine(AccurateTracks == Results.Count ? "All tracks accurately ripped"
                        : string.Format(CultureInfo.InvariantCulture, "{0} of {1} tracks accurately ripped", AccurateTracks, Results.Count));
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

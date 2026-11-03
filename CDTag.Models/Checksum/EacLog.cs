using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CDTag.Model.Checksum
{
    /// <summary>Result of one check of an EAC log.</summary>
    public enum LogCheckState
    {
        Info,
        Good,
        Warning,
        Bad
    }

    public class LogCheck
    {
        public LogCheckState State { get; set; }
        public string Name { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>One extracted track (or the whole range of a range rip) in an EAC log.</summary>
    public class EacLogTrack
    {
        /// <summary>Track number; 0 for a range rip.</summary>
        public int Number { get; set; }
        public string FileName { get; set; }
        public double? Peak { get; set; }
        public double? Quality { get; set; }
        public string TestCrc { get; set; }
        public string CopyCrc { get; set; }
        public bool CopyOk { get; set; }
        public List<string> Errors { get; private set; }

        /// <summary>Accurately ripped / Cannot be verified / Not present (null when not checked).</summary>
        public string AccurateRip { get; set; }
        public bool? AccuratelyRipped { get; set; }

        public EacLogTrack()
        {
            Errors = new List<string>();
        }
    }

    /// <summary>
    /// An Exact Audio Copy extraction log: settings, tracks, CRCs, AccurateRip results and the log checksum.
    /// Only English logs are checked in detail; the checksum works for any language.
    /// </summary>
    public class EacLog
    {
        private static readonly Regex ChecksumLine = new Regex(@"^==== Log checksum (?<sum>[0-9A-Fa-f]+) ====[ \t]*\r?$", RegexOptions.Multiline);
        private static readonly Regex SettingLine = new Regex(@"^\s*(?<key>[^:\r\n]+?)\s*:\s(?<value>.*)$");
        private static readonly Regex TrackHeader = new Regex(@"^Track\s+(?<n>\d+)\s*$");
        private static readonly Regex Value = new Regex(@"^\s+(?<key>Filename|Peak level|Track quality|Range quality|Test CRC|Copy CRC)\s+(?<value>.+?)\s*$");

        public EacLog()
        {
            Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Tracks = new List<EacLogTrack>();
        }

        /// <summary>The whole log, including its checksum line.</summary>
        public string Text { get; private set; }

        /// <summary>The text the checksum covers (everything before the checksum line).</summary>
        public string SignedText { get; private set; }

        /// <summary>The checksum in the log, or null when it has none.</summary>
        public string ListedChecksum { get; private set; }

        public string Version { get; private set; }
        public string Album { get; private set; }
        public bool IsEnglish { get; private set; }
        public bool IsRange { get; private set; }
        public bool NoErrors { get; private set; }
        public bool AllAccurate { get; private set; }

        /// <summary>"Key : Value" settings above the TOC (Read mode, Defeat audio cache, ...).</summary>
        public Dictionary<string, string> Settings { get; private set; }

        public List<EacLogTrack> Tracks { get; private set; }

        /// <summary>The checksum is present and matches the text.</summary>
        public bool? ChecksumValid
        {
            get { return ListedChecksum == null ? (bool?)null : string.Equals(EacLogChecksum.Compute(SignedText), ListedChecksum, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>EAC writes logs in UTF-16LE with a BOM; older or edited logs may be UTF-8 or ANSI.</summary>
        public static string ReadText(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            return ChecksumList.ReadText(bytes);
        }

        /// <summary>The logs in one file (EAC appends a new log, with its own checksum, when a disc is ripped again).</summary>
        public static List<EacLog> Split(string text)
        {
            var logs = new List<EacLog>();
            int start = 0;
            foreach (Match match in ChecksumLine.Matches(text))
            {
                int end = match.Index + match.Length;
                logs.Add(Parse(text.Substring(start, end - start), text.Substring(start, match.Index - start), match.Groups["sum"].Value.ToUpperInvariant()));
                start = end;
            }
            string rest = text.Substring(start);
            if (rest.Trim().Length > 0 && (logs.Count == 0 || rest.Contains("EAC extraction logfile")))
                logs.Add(Parse(rest, rest, null));
            return logs;
        }

        private static EacLog Parse(string text, string signed, string checksum)
        {
            var log = new EacLog { Text = text, SignedText = signed, ListedChecksum = checksum };
            string[] lines = text.Replace("\r", "").Split('\n');

            log.IsEnglish = text.Contains("EAC extraction logfile");
            Match version = Regex.Match(text, @"Exact Audio Copy V\S+(?: from [^\r\n]+)?");
            log.Version = version.Success ? version.Value.Trim() : null;
            if (!log.IsEnglish)
                return log;

            log.IsRange = text.Contains("\nRange status and errors") || text.Contains("\nSelected range");
            log.NoErrors = text.Contains("\nNo errors occurred") || text.Contains("\nNo errors occured");
            log.AllAccurate = text.Contains("All tracks accurately ripped") || Regex.IsMatch(text, @"All\s+\d+\s+tracks accurately ripped");

            int i = 0;
            // Header: album line after the logfile line, then settings until the TOC.
            for (; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("EAC extraction logfile", StringComparison.Ordinal))
                {
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        if (lines[j].Trim().Length > 0)
                        {
                            log.Album = lines[j].Trim();
                            i = j;
                            break;
                        }
                    }
                    continue;
                }
                if (lines[i].StartsWith("TOC of the extracted CD", StringComparison.Ordinal) || lines[i].StartsWith("Range status", StringComparison.Ordinal) || TrackHeader.IsMatch(lines[i]))
                    break;
                Match setting = SettingLine.Match(lines[i]);
                if (setting.Success && !log.Settings.ContainsKey(setting.Groups["key"].Value))
                    log.Settings[setting.Groups["key"].Value] = setting.Groups["value"].Value.Trim();
            }

            // Tracks (track mode) or the selected range.
            EacLogTrack track = null;
            for (; i < lines.Length; i++)
            {
                string line = lines[i];
                Match header = TrackHeader.Match(line);
                if (header.Success)
                {
                    track = new EacLogTrack { Number = int.Parse(header.Groups["n"].Value, CultureInfo.InvariantCulture) };
                    log.Tracks.Add(track);
                    continue;
                }
                if (line.StartsWith("Selected range", StringComparison.Ordinal))
                {
                    track = new EacLogTrack { Number = 0 };
                    log.Tracks.Add(track);
                    continue;
                }
                if (line.StartsWith("No errors occur", StringComparison.Ordinal) || line.StartsWith("There were errors", StringComparison.Ordinal) ||
                    line.StartsWith("AccurateRip summary", StringComparison.Ordinal) || line.StartsWith("End of status report", StringComparison.Ordinal))
                {
                    track = null;
                    if (line.StartsWith("AccurateRip summary", StringComparison.Ordinal))
                        ParseAccurateRipSummary(log, lines, i + 1);
                    continue;
                }
                if (track == null)
                    continue;

                Match value = Value.Match(line);
                string trimmed = line.Trim();
                if (value.Success)
                {
                    string v = value.Groups["value"].Value;
                    switch (value.Groups["key"].Value)
                    {
                        case "Filename":
                            track.FileName = v;
                            break;
                        case "Peak level":
                            track.Peak = Percent(v);
                            break;
                        case "Track quality":
                        case "Range quality":
                            track.Quality = Percent(v);
                            break;
                        case "Test CRC":
                            track.TestCrc = v.ToUpperInvariant();
                            break;
                        case "Copy CRC":
                            track.CopyCrc = v.ToUpperInvariant();
                            break;
                    }
                }
                else if (trimmed == "Copy OK" || trimmed == "Copy finished")
                {
                    track.CopyOk = true;
                }
                else if (trimmed.StartsWith("Suspicious position", StringComparison.Ordinal) || trimmed.StartsWith("Missing samples", StringComparison.Ordinal) ||
                         trimmed.StartsWith("Timing problem", StringComparison.Ordinal) || trimmed.StartsWith("Too many samples", StringComparison.Ordinal) ||
                         trimmed.StartsWith("Copy aborted", StringComparison.Ordinal))
                {
                    track.Errors.Add(trimmed);
                }
                else if (trimmed.StartsWith("Accurately ripped", StringComparison.Ordinal))
                {
                    track.AccurateRip = trimmed;
                    track.AccuratelyRipped = true;
                }
                else if (trimmed.StartsWith("Cannot be verified", StringComparison.Ordinal))
                {
                    track.AccurateRip = trimmed;
                    track.AccuratelyRipped = false;
                }
                else if (trimmed.StartsWith("Track not present in AccurateRip", StringComparison.Ordinal))
                {
                    track.AccurateRip = trimmed;
                }
            }
            return log;
        }

        /// <summary>Range rips list AccurateRip per track after the range ("Track  1  accurately ripped (confidence 2)  [...]").</summary>
        private static void ParseAccurateRipSummary(EacLog log, string[] lines, int start)
        {
            var summary = new Regex(@"^Track\s+(?<n>\d+)\s+(?<status>.+?)\s*$");
            for (int i = start; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("End of status report", StringComparison.Ordinal) || line.StartsWith("All tracks", StringComparison.Ordinal) ||
                    line.StartsWith("No tracks", StringComparison.Ordinal) || line.StartsWith("----", StringComparison.Ordinal))
                    break;
                Match match = summary.Match(line);
                if (!match.Success)
                    continue;
                int number = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
                EacLogTrack track = log.Tracks.FirstOrDefault(t => t.Number == number);
                if (track == null)
                {
                    if (!log.IsRange)
                        continue;
                    track = new EacLogTrack { Number = number };
                    log.Tracks.Add(track);
                }
                string status = match.Groups["status"].Value;
                track.AccurateRip = status;
                if (status.StartsWith("accurately ripped", StringComparison.OrdinalIgnoreCase))
                    track.AccuratelyRipped = true;
                else if (status.StartsWith("cannot be verified", StringComparison.OrdinalIgnoreCase))
                    track.AccuratelyRipped = false;
            }
        }

        private static double? Percent(string text)
        {
            Match match = Regex.Match(text, @"\d+(?:[.,]\d+)?");
            double value;
            return match.Success && double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : (double?)null;
        }

        private string Setting(string key)
        {
            string value;
            return Settings.TryGetValue(key, out value) ? value : null;
        }

        /// <summary>Checks of the rip, in the order shown: checksum, settings, then the tracks.</summary>
        public List<LogCheck> Evaluate()
        {
            var checks = new List<LogCheck>();
            bool? valid = ChecksumValid;
            checks.Add(valid == null
                ? new LogCheck { State = LogCheckState.Warning, Name = "Log checksum", Detail = "No checksum (EAC before 0.99, or the checksum option was off)." }
                : valid == true
                    ? new LogCheck { State = LogCheckState.Good, Name = "Log checksum", Detail = "OK: the log has not been edited." }
                    : new LogCheck { State = LogCheckState.Bad, Name = "Log checksum", Detail = "Does not match: the log was edited after EAC wrote it." });

            checks.Add(new LogCheck { State = LogCheckState.Info, Name = "Version", Detail = Version ?? "Unknown (EAC before 0.99)" });
            if (!IsEnglish)
            {
                checks.Add(new LogCheck { State = LogCheckState.Warning, Name = "Language", Detail = "Not an English log; only the checksum was checked." });
                return checks;
            }
            if (Album != null)
                checks.Add(new LogCheck { State = LogCheckState.Info, Name = "Album", Detail = Album });
            string drive = Setting("Used drive");
            if (drive != null)
                checks.Add(new LogCheck { State = LogCheckState.Info, Name = "Drive", Detail = Regex.Replace(drive, @"\s+Adapter:.*$", "") });

            AddSetting(checks, "Read mode", v => v.StartsWith("Secure", StringComparison.OrdinalIgnoreCase) ? LogCheckState.Good : LogCheckState.Bad, "Secure mode is required.");
            AddSetting(checks, "Utilize accurate stream", YesGood(LogCheckState.Warning), null);
            AddSetting(checks, "Defeat audio cache", YesGood(LogCheckState.Bad), "The drive cache must be defeated.");
            AddSetting(checks, "Make use of C2 pointers", v => v.StartsWith("No", StringComparison.OrdinalIgnoreCase) ? LogCheckState.Good : LogCheckState.Warning, "C2 pointers can hide read errors.");
            AddSetting(checks, "Read offset correction", v => v.Trim() == "0" ? LogCheckState.Warning : LogCheckState.Good, "0 is right only for drives with no offset.");
            AddSetting(checks, "Fill up missing offset samples with silence", YesGood(LogCheckState.Warning), null);
            AddSetting(checks, "Delete leading and trailing silent blocks", v => v.StartsWith("No", StringComparison.OrdinalIgnoreCase) ? LogCheckState.Good : LogCheckState.Bad, "Silent blocks must be kept.");
            AddSetting(checks, "Null samples used in CRC calculations", YesGood(LogCheckState.Warning), null);
            if (!IsRange)
                AddSetting(checks, "Gap handling", v => v.StartsWith("Appended to previous", StringComparison.OrdinalIgnoreCase) ? LogCheckState.Good
                                                       : v.StartsWith("Not detected", StringComparison.OrdinalIgnoreCase) ? LogCheckState.Bad : LogCheckState.Warning,
                           "Gaps should be detected and appended to the previous track.");
            AddSetting(checks, "Sample format", v => v.Contains("44.100") && v.Contains("16 Bit") ? LogCheckState.Good : LogCheckState.Warning, null);
            string format = Setting("Used output format");
            if (format != null)
                checks.Add(new LogCheck { State = LogCheckState.Info, Name = "Output format", Detail = format });

            checks.Add(new LogCheck { State = LogCheckState.Info, Name = "Rip type", Detail = IsRange ? "Range (one image file)" : Tracks.Count + " tracks" });

            foreach (EacLogTrack track in Tracks.Where(t => t.FileName != null || t.TestCrc != null || t.CopyCrc != null))
                checks.Add(CheckTrack(track));

            int accurate = Tracks.Count(t => t.AccuratelyRipped == true);
            int notAccurate = Tracks.Count(t => t.AccuratelyRipped == false);
            int withResult = Tracks.Count(t => t.AccurateRip != null);
            if (withResult == 0)
                checks.Add(new LogCheck { State = LogCheckState.Warning, Name = "AccurateRip", Detail = "Not used, or the disc is not in the database." });
            else
                checks.Add(new LogCheck
                {
                    State = notAccurate > 0 ? LogCheckState.Bad : accurate == withResult ? LogCheckState.Good : LogCheckState.Warning,
                    Name = "AccurateRip",
                    Detail = string.Format("{0} of {1} tracks accurately ripped{2}.", accurate, withResult, notAccurate > 0 ? ", " + notAccurate + " could not be verified" : "")
                });

            checks.Add(NoErrors
                ? new LogCheck { State = LogCheckState.Good, Name = "Errors", Detail = "No errors occurred." }
                : new LogCheck { State = LogCheckState.Bad, Name = "Errors", Detail = "There were errors." });
            return checks;
        }

        private static Func<string, LogCheckState> YesGood(LogCheckState otherwise)
        {
            return v => v.StartsWith("Yes", StringComparison.OrdinalIgnoreCase) ? LogCheckState.Good : otherwise;
        }

        private void AddSetting(List<LogCheck> checks, string key, Func<string, LogCheckState> rate, string hint)
        {
            string value = Setting(key);
            if (value == null)
                return;
            LogCheckState state = rate(value);
            checks.Add(new LogCheck { State = state, Name = key, Detail = state == LogCheckState.Good || hint == null ? value : value + " (" + hint + ")" });
        }

        private static LogCheck CheckTrack(EacLogTrack track)
        {
            var notes = new List<string>();
            LogCheckState state = LogCheckState.Good;
            if (track.TestCrc == null)
            {
                notes.Add("no test CRC (not test and copy)");
                state = LogCheckState.Warning;
            }
            else if (track.CopyCrc != null && track.TestCrc != track.CopyCrc)
            {
                notes.Add("test CRC " + track.TestCrc + " differs from copy CRC " + track.CopyCrc);
                state = LogCheckState.Bad;
            }
            else
            {
                notes.Add("CRC " + track.CopyCrc);
            }
            if (track.Errors.Count > 0)
            {
                notes.Add(string.Join("; ", track.Errors.Distinct().Take(3)) + (track.Errors.Count > 3 ? " (+" + (track.Errors.Count - 3) + " more)" : ""));
                state = LogCheckState.Bad;
            }
            else if (!track.CopyOk)
            {
                notes.Add("no \"Copy OK\"");
                state = LogCheckState.Bad;
            }
            if (track.Quality != null && track.Quality < 100 && state == LogCheckState.Good)
                state = LogCheckState.Warning;
            if (track.Quality != null)
                notes.Add(string.Format(CultureInfo.InvariantCulture, "quality {0:0.0} %", track.Quality));
            if (track.Peak != null)
                notes.Add(string.Format(CultureInfo.InvariantCulture, "peak {0:0.0} %", track.Peak));
            if (track.Number > 0 && track.AccurateRip != null)
                notes.Add(track.AccurateRip);

            return new LogCheck { State = state, Name = track.Number == 0 ? "Range" : "Track " + track.Number, Detail = string.Join(", ", notes) };
        }
    }
}

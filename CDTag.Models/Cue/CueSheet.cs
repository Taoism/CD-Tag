using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CDTag.Model.Checksum;

namespace CDTag.Model.Cue
{
    /// <summary>One TRACK of a CUE sheet. Times are in CD frames (1/75 second).</summary>
    public class CueTrack
    {
        public int Number { get; set; }
        public string Title { get; set; }
        public string Performer { get; set; }
        public string Isrc { get; set; }
        /// <summary>The FILE the track's INDEX 01 is in.</summary>
        public string File { get; set; }
        /// <summary>INDEX 00 (start of the pregap), or null.</summary>
        public long? Index00 { get; set; }
        /// <summary>INDEX 01 (start of the track).</summary>
        public long Index01 { get; set; }
        public bool IsAudio { get; set; }
    }

    /// <summary>A CUE sheet (EAC, foobar2000, CUETools...): album fields, files and tracks.</summary>
    public class CueSheet
    {
        public const int FramesPerSecond = 75;

        private static readonly Regex Command = new Regex(@"^\s*(?<cmd>\S+)\s*(?<args>.*?)\s*$");
        private static readonly Regex Time = new Regex(@"^(?<m>\d+):(?<s>\d{1,2}):(?<f>\d{1,2})$");

        public CueSheet()
        {
            Files = new List<string>();
            Tracks = new List<CueTrack>();
            Remarks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string Path { get; private set; }
        public string Title { get; set; }
        public string Performer { get; set; }
        public string Catalog { get; set; }
        /// <summary>REM lines: GENRE, DATE, DISCID, COMMENT...</summary>
        public Dictionary<string, string> Remarks { get; private set; }
        public List<string> Files { get; private set; }
        public List<CueTrack> Tracks { get; private set; }

        public string Genre { get { return Remark("GENRE"); } }
        public string Date { get { return Remark("DATE"); } }

        /// <summary>One FILE for every track: an image that can be split.</summary>
        public bool IsImage { get { return Files.Count == 1 && Tracks.Count > 0; } }

        /// <summary>Audio before INDEX 01 of track 1 (hidden track one audio), in frames.</summary>
        public long HiddenTrackFrames { get { return Tracks.Count > 0 ? Tracks[0].Index01 : 0; } }

        public string Remark(string key)
        {
            string value;
            return Remarks.TryGetValue(key, out value) ? value : null;
        }

        public static CueSheet Load(string path)
        {
            CueSheet sheet = Parse(ChecksumList.ReadText(System.IO.File.ReadAllBytes(path)));
            sheet.Path = path;
            return sheet;
        }

        public static CueSheet Parse(string text)
        {
            var sheet = new CueSheet();
            string file = null;
            CueTrack track = null;
            foreach (string rawLine in text.Replace("\r", "").Split('\n'))
            {
                Match match = Command.Match(rawLine);
                if (!match.Success)
                    continue;
                string cmd = match.Groups["cmd"].Value.ToUpperInvariant();
                string args = match.Groups["args"].Value;
                switch (cmd)
                {
                    case "REM":
                        int space = args.IndexOf(' ');
                        if (space > 0 && track == null)
                            sheet.Remarks[args.Substring(0, space)] = Unquote(args.Substring(space + 1));
                        break;
                    case "TITLE":
                        if (track != null)
                            track.Title = Unquote(args);
                        else
                            sheet.Title = Unquote(args);
                        break;
                    case "PERFORMER":
                        if (track != null)
                            track.Performer = Unquote(args);
                        else
                            sheet.Performer = Unquote(args);
                        break;
                    case "CATALOG":
                        sheet.Catalog = args.Trim();
                        break;
                    case "FILE":
                        file = FileName(args);
                        if (!sheet.Files.Contains(file, StringComparer.OrdinalIgnoreCase))
                            sheet.Files.Add(file);
                        break;
                    case "TRACK":
                        string[] parts = args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        int number;
                        if (parts.Length == 0 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                            break;
                        track = new CueTrack { Number = number, File = file, IsAudio = parts.Length < 2 || parts[1].Equals("AUDIO", StringComparison.OrdinalIgnoreCase) };
                        sheet.Tracks.Add(track);
                        break;
                    case "ISRC":
                        if (track != null)
                            track.Isrc = args.Trim();
                        break;
                    case "INDEX":
                        if (track == null)
                            break;
                        string[] index = args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        long frames;
                        if (index.Length < 2 || !TryParseTime(index[1], out frames))
                            break;
                        if (index[0] == "00" || index[0] == "0")
                        {
                            track.Index00 = frames;
                        }
                        else if (index[0] == "01" || index[0] == "1")
                        {
                            track.Index01 = frames;
                            track.File = file;
                        }
                        break;
                }
            }
            return sheet;
        }

        public static bool TryParseTime(string text, out long frames)
        {
            frames = 0;
            Match match = Time.Match(text.Trim());
            if (!match.Success)
                return false;
            frames = (long.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture)) * FramesPerSecond
                     + int.Parse(match.Groups["f"].Value, CultureInfo.InvariantCulture);
            return true;
        }

        public static string FormatTime(long frames)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:00}", frames / (60 * FramesPerSecond), frames / FramesPerSecond % 60, frames % FramesPerSecond);
        }

        /// <summary>The audio file of the image: the FILE name, or the same name with another extension (.wav/.flac) next to the cue.</summary>
        public string FindImage()
        {
            if (Files.Count == 0 || Path == null)
                return null;
            string folder = System.IO.Path.GetDirectoryName(Path);
            string named = System.IO.Path.Combine(folder, Files[0]);
            if (System.IO.File.Exists(named))
                return named;
            foreach (string extension in new[] { ".flac", ".wav" })
            {
                string other = System.IO.Path.ChangeExtension(named, extension);
                if (System.IO.File.Exists(other))
                    return other;
                other = System.IO.Path.ChangeExtension(Path, extension);
                if (System.IO.File.Exists(other))
                    return other;
            }
            return null;
        }

        private static string FileName(string args)
        {
            args = args.Trim();
            if (args.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = args.IndexOf('"', 1);
                return end > 0 ? args.Substring(1, end - 1) : args.Substring(1);
            }
            int space = args.LastIndexOf(' ');
            return space > 0 ? args.Substring(0, space) : args;
        }

        private static string Unquote(string value)
        {
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return value.Substring(1, value.Length - 2);
            return value;
        }
    }
}

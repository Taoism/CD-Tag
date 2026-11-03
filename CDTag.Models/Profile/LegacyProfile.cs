using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CDTag.Common.Json;

namespace CDTag.Model.Profile
{
    /// <summary>
    /// Reads profiles written by CD-Tag 2.x (INI-style .cfg files with [CONFIG], [NAME0]..[NAME5],
    /// [NAMEOPT], [NFO_OPTIONS], [TAG_PROCESS], ...) and converts them to <see cref="UserProfile"/>.
    /// </summary>
    /// <remarks>
    /// Naming formats are copied verbatim (they use the 2.x token language, e.g. &lt;SC&gt;, &lt;LC&gt;,
    /// &lt;ExtInfo&gt;, &lt;Discs&gt;). Settings without a counterpart in the new model (release info,
    /// tagging rules, word lists, ...) are not converted; <see cref="Import"/> keeps a copy of the
    /// original file next to the new profile so nothing is lost.
    /// </remarks>
    public static class LegacyProfile
    {
        /// <summary>Extension of the preserved original 2.x profile.</summary>
        public const string BackupExtension = ".cdtag2.ini";

        // [NAMEn] -> naming slot
        private static readonly string[] NameSections = { "NAME0", "NAME1", "NAME2", "NAME3", "NAME4", "NAME5" };

        /// <summary>CD-Tag 2.x wrote its profiles in the DOS code page (437), e.g. byte F8 is the degree sign.</summary>
        public static Encoding DosEncoding
        {
            get
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(437);
            }
        }

        /// <summary>True when the file looks like a CD-Tag 2.x profile rather than a JSON profile.</summary>
        public static bool IsLegacyFile(string path)
        {
            if (!File.Exists(path))
                return false;

            using (var reader = new StreamReader(path, DosEncoding, detectEncodingFromByteOrderMarks: true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0)
                        continue;
                    return line.StartsWith("[", StringComparison.Ordinal);
                }
            }
            return false;
        }

        /// <summary>Parses a 2.x profile into a new profile named <paramref name="profileName"/>.</summary>
        /// <param name="nfoTemplatePath">Receives the full path of the referenced NFO template if it exists next to the profile, otherwise null.</param>
        public static UserProfile Convert(string path, string profileName, out string nfoTemplatePath)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException("path");

            var ini = ReadIni(File.ReadAllLines(path, DosEncoding));
            if (!ini.ContainsKey("CONFIG") && !ini.ContainsKey("NAME0"))
                throw new InvalidDataException(string.Format("'{0}' is not a CD-Tag 2.x profile.", Path.GetFileName(path)));

            var profile = new UserProfile { ProfileName = profileName };

            // Naming formats
            NamingFormat[] slots =
            {
                profile.FileNaming.SingleCD.SingleArtist, profile.FileNaming.SingleCD.VariousArtists,
                profile.FileNaming.MultiCD.SingleArtist, profile.FileNaming.MultiCD.VariousArtists,
                profile.FileNaming.Vinyl.SingleArtist, profile.FileNaming.Vinyl.VariousArtists
            };
            for (int i = 0; i < NameSections.Length; i++)
            {
                Dictionary<string, string> section;
                if (!ini.TryGetValue(NameSections[i], out section))
                    continue;

                NamingFormat format = slots[i];
                format.Directory = Get(section, "DIR");
                format.AudioFile = Get(section, "MP3");
                format.CUE = Get(section, "CUE");
                format.Playlist = Get(section, "M3U");
                format.Checksum = Get(section, "SFV");
                format.NFO = Get(section, "NFO");
                format.Images = Get(section, "JPG");
                format.EACLog = Get(section, "LOG");
            }

            // CD-Tag 2.x profiles were made per format; FLAC ones say so in their name or folder format.
            bool isFlac = (profileName ?? "").IndexOf("FLAC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          (profile.FileNaming.SingleCD.SingleArtist.Directory ?? "").IndexOf("FLAC", StringComparison.OrdinalIgnoreCase) >= 0;
            profile.AudioFileTypes = isFlac ? "flac" : "mp3";

            // Naming options
            Dictionary<string, string> nameOpt = Section(ini, "NAMEOPT");
            profile.FileNaming.FileCharacterLimit = GetInt(nameOpt, "CharLimit");
            profile.FileNaming.DirectoryCharacterLimit = GetInt(nameOpt, "DirCharLimit");
            profile.FileNaming.UseStandardCharactersOnly = GetBool(nameOpt, "StdOnly");
            profile.FileNaming.UseLatinCharactersOnly = GetBool(nameOpt, "Foreign");

            Dictionary<string, string> smartCase = Section(ini, "SMARTCASE");
            profile.FileNaming.DontChangeCaseWordsWithAllCaps = GetBool(smartCase, "Caps");
            profile.FileNaming.DontChangeCaseWordsWithNonLetter = GetBool(smartCase, "Alpha");

            // NFO options
            Dictionary<string, string> nfo = Section(ini, "NFO_OPTIONS");
            profile.NFOOptions.ShowReleaseScreen = GetBool(nfo, "ShowReleaseScreen");
            profile.NFOOptions.UseAverageBitrate = GetBool(nfo, "ShowAvgVBR", profile.NFOOptions.UseAverageBitrate);
            profile.NFOOptions.SingleCDHeader = Text(Get(nfo, "SingleCDHeader"));
            profile.NFOOptions.MultiCDHeader = Text(Get(nfo, "MultiCDHeader"));
            profile.NFOOptions.TracklistFooter = Text(Get(nfo, "TracklistFooter"));
            profile.NFOOptions.ReleaseNotesHeader = Text(Get(nfo, "RnotesHeader"));
            profile.NFOOptions.ReleaseNotesFooter = Text(Get(nfo, "RnotesFooter"));
            profile.NFOOptions.AutomaticallyPreviewNFO = GetBool(nfo, "AutoPreviewNFO");
            string decimalCharacter = Get(nfo, "DecChar");
            if (!string.IsNullOrEmpty(decimalCharacter))
                profile.NFOOptions.DecimalCharacter = decimalCharacter;

            nfoTemplatePath = null;
            string template = Get(nfo, "Template");
            if (!string.IsNullOrWhiteSpace(template))
            {
                string candidate = Path.IsPathRooted(template) ? template : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), template);
                if (File.Exists(candidate))
                    nfoTemplatePath = candidate;
            }

            // What to do when tagging an album
            Dictionary<string, string> process = Section(ini, "TAG_PROCESS");
            profile.Finish.RenameDirectory = GetBool(process, "RenameDIR");
            profile.Finish.RenameAudioFiles = GetBool(process, "RenameMP3");
            profile.Finish.RenameCUEFiles = GetBool(process, "RenameCUE");
            profile.Finish.RenameImageFiles = GetBool(process, "RenameJPG");
            profile.Finish.RenameEACLogFiles = GetBool(process, "RenameLOG");
            profile.Finish.CreatePlaylist = GetBool(process, "MakeM3U");
            profile.Finish.CreateSFV = GetBool(process, "MakeSFV");
            profile.Finish.DeleteOldFiles = GetBool(process, "DeleteOld");
            profile.Finish.DeleteEmptySubdirectories = GetBool(process, "RemoveEmptyDirs");
            if (GetBool(process, "RenameOrMakeNFO"))
                profile.Finish.NFO = FinishNFO.RenameIfExistsElseCreate;
            else if (GetBool(process, "RenameNFO"))
                profile.Finish.NFO = FinishNFO.RenameExisting;
            else if (GetBool(process, "MakeNFO"))
                profile.Finish.NFO = FinishNFO.CreateNew;
            else
                profile.Finish.NFO = FinishNFO.None;

            ConvertNamingRules(ini, profile);
            ConvertExtInfo(ini, profile);
            ConvertReleaseInfo(ini, profile);
            profile.DeleteFiles = List(ini, "DELETE_LIST").Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
            profile.Tagging = ConvertTagging(ini);

            return profile;
        }

        private static TaggingOptions ConvertTagging(Dictionary<string, Dictionary<string, string>> ini)
        {
            Dictionary<string, string> tagging = Section(ini, "TAGGING");
            Dictionary<string, string> id3v2 = Section(ini, "TAG_ID3V2");
            Dictionary<string, string> checksums = Section(ini, "SFVMD5");
            var options = new TaggingOptions();
            options.Id3v2 = Action(Get(tagging, "ID3V2"), options.Id3v2);
            options.Id3v1 = Action(Get(tagging, "ID3V1"), options.Id3v1);
            options.FlacId3v2 = Action(Get(tagging, "FLAC_ID3V2"), options.FlacId3v2);
            options.FlacId3v1 = Action(Get(tagging, "FLAC_ID3V1"), options.FlacId3v1);
            options.LongTrackNumbers = GetBool(tagging, "LongTRCK", options.LongTrackNumbers);
            options.UseId3v1Comment = GetBool(tagging, "UseID3v1Comment");
            options.SeparatePlaylistPerDisc = GetBool(tagging, "SeparateM3U", options.SeparatePlaylistPerDisc);
            options.NoId3v2Comment = GetBool(id3v2, "NoWriteID3v2Comments");
            options.ChecksumHideGeneratedBy = GetBool(checksums, "HideGeneratedBy");
            options.ChecksumHideFileInfo = GetBool(checksums, "HideFileInfo");
            return options;
        }

        private static TagAction Action(string value, TagAction fallback)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "writealways": return TagAction.WriteAlways;
                case "writeifexists": return TagAction.WriteIfExists;
                case "remove": return TagAction.Remove;
                case "leave": case "none": case "ignore": return TagAction.Leave;
                default: return fallback;
            }
        }

        /// <summary>
        /// Converts <paramref name="sourcePath"/> and saves it in <paramref name="profileDirectory"/> as
        /// <paramref name="profileName"/>.cfg, together with its NFO template (as name.nfo) and a copy of the
        /// original file (name<see cref="BackupExtension"/>).
        /// </summary>
        public static UserProfile Import(string sourcePath, string profileDirectory, string profileName)
        {
            string nfoTemplatePath;
            UserProfile profile = Convert(sourcePath, profileName, out nfoTemplatePath);

            string backupPath = Path.Combine(profileDirectory, profileName + BackupExtension);
            if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(backupPath), StringComparison.OrdinalIgnoreCase))
                File.Copy(sourcePath, backupPath, overwrite: true);

            if (nfoTemplatePath != null)
            {
                string nfoFileName = profileName + ".nfo";
                string target = Path.Combine(profileDirectory, nfoFileName);
                if (!string.Equals(Path.GetFullPath(nfoTemplatePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    File.Copy(nfoTemplatePath, target, overwrite: true);
                profile.NFOOptions.TemplatePath = nfoFileName;
            }

            string message;
            if (!profile.ValidateProfileName(out message))
                throw new InvalidDataException(message);

            File.WriteAllText(Path.Combine(profileDirectory, profileName + ".cfg"), JsonSerializer.SerializeObject(profile));
            profile.LastModified = DateTime.Now;
            profile.UpdateModelHash();
            return profile;
        }

        private static void ConvertNamingRules(Dictionary<string, Dictionary<string, string>> ini, UserProfile profile)
        {
            List<string> find = List(ini, "ORIG_LIST", keepEmpty: true);
            List<string> replace = List(ini, "REPLACE_LIST", keepEmpty: true);
            var pairs = new List<ReplacePair>();
            for (int i = 0; i < find.Count; i++)
            {
                if (string.IsNullOrEmpty(find[i]))
                    continue;
                pairs.Add(new ReplacePair { Find = find[i], Replace = i < replace.Count ? replace[i] ?? "" : "" });
            }

            profile.NamingRules = new NamingRules
            {
                Replacements = pairs.ToArray(),
                RemoveStrings = List(ini, "REMOVE_LIST").Where(v => !string.IsNullOrEmpty(v)).ToArray(),
                LowercaseWords = List(ini, "LOWERCASE_LIST").Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray(),
                WordSeparators = List(ini, "WORDSEP_LIST").Where(v => !string.IsNullOrEmpty(v)).ToArray(),
                FieldSeparators = List(ini, "FIELDSEP_LIST").Where(v => !string.IsNullOrEmpty(v)).ToArray()
            };
        }

        private static void ConvertExtInfo(Dictionary<string, Dictionary<string, string>> ini, UserProfile profile)
        {
            var definitions = new List<ExtInfoDefinition>();
            for (int n = 1; n <= 8; n++)
            {
                Dictionary<string, string> options;
                if (!ini.TryGetValue(n == 1 ? "OPTIONS" : "Options" + n, out options))
                    options = new Dictionary<string, string>();

                string suffix = n == 1 ? "" : n.ToString();
                List<string> fields = List(ini, "EXTDIRVAL_LIST" + suffix, keepEmpty: true);
                List<string> formats = List(ini, "EXTDIRDIR_LIST" + suffix, keepEmpty: true);
                var items = new List<ExtInfoItem>();
                for (int i = 0; i < fields.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(fields[i]))
                        continue;
                    items.Add(new ExtInfoItem { Field = fields[i].Trim(), Format = i < formats.Count && !string.IsNullOrEmpty(formats[i]) ? formats[i] : "<Value>" });
                }

                definitions.Add(new ExtInfoDefinition
                {
                    Begin = Get(options, "Begin") ?? "",
                    FieldSeparator = Get(options, "FieldSep") ?? "",
                    End = Get(options, "End") ?? "",
                    Items = items.ToArray()
                });
            }
            profile.ExtInfo = definitions.ToArray();
        }

        private static void ConvertReleaseInfo(Dictionary<string, Dictionary<string, string>> ini, UserProfile profile)
        {
            Dictionary<string, string> release = Section(ini, "RELEASE_INFO");
            Dictionary<string, string> nfo = Section(ini, "NFO_OPTIONS");
            profile.ReleaseInfo = new ReleaseInfoOptions
            {
                ReleaseTypes = Pairs(ini, "RELEASETYPENFO_LIST", "RELEASETYPEDIR_LIST"),
                Sources = Pairs(ini, "SOURCENFO_LIST", "SOURCEDIR_LIST"),
                Languages = Pairs(ini, "LANGRYNFO_LIST", "LANGRYDIR_LIST"),
                LimitedEdition = Pairs(ini, "LTDEDNFO_LIST", "LTDEDDIR_LIST"),
                WhiteLabel = Pairs(ini, "WHITELABELNFO_LIST", "WHITELABELDIR_LIST"),
                CleanExplicit = Pairs(ini, "CLEANEXPLICITNFO_LIST", "CLEANEXPLICITDIR_LIST"),
                GroupInitials = Get(release, "GroupInitials"),
                Ripper = Get(release, "Ripper"),
                Supplier = Get(release, "Supplier"),
                Encoder = Get(release, "Encoder"),
                RippingTool = Get(release, "Grabber"),
                Comment = Get(release, "ID3Comment"),
                LockRipper = GetBool(release, "LockRipper"),
                LockSupplier = GetBool(release, "LockSupplier"),
                LockEncoder = GetBool(release, "LockEncoder"),
                LockComment = GetBool(release, "LockID3Comment"),
                NfoDateFormat = Get(nfo, "DateFormat")
            };
        }

        /// <summary>Values of a numbered list section ("1=...", "2=...") in numeric order.</summary>
        private static List<string> List(Dictionary<string, Dictionary<string, string>> ini, string sectionName, bool keepEmpty = false)
        {
            Dictionary<string, string> section;
            var result = new List<string>();
            if (!ini.TryGetValue(sectionName, out section))
                return result;

            int number;
            int max = section.Keys.Where(k => int.TryParse(k, out number)).Select(int.Parse).DefaultIfEmpty(0).Max();
            for (int i = 1; i <= max; i++)
            {
                string value;
                section.TryGetValue(i.ToString(), out value);
                if (keepEmpty || !string.IsNullOrEmpty(value))
                    result.Add(value ?? "");
            }
            return result;
        }

        private static ReleaseListItem[] Pairs(Dictionary<string, Dictionary<string, string>> ini, string nfoSection, string dirSection)
        {
            List<string> nfo = List(ini, nfoSection, keepEmpty: true);
            List<string> dir = List(ini, dirSection, keepEmpty: true);
            var result = new List<ReleaseListItem>();
            for (int i = 0; i < nfo.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(nfo[i]))
                    continue;
                result.Add(new ReleaseListItem { Nfo = nfo[i], Dir = i < dir.Count ? dir[i] ?? "" : "" });
            }
            return result.Count == 0 ? null : result.ToArray();
        }


        private static Dictionary<string, Dictionary<string, string>> ReadIni(IEnumerable<string> lines)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> current = null;
            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimStart();
                if (line.Length == 0 || line[0] == ';')
                    continue;

                if (line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    string name = end > 1 ? line.Substring(1, end - 1).Trim() : line.Substring(1).Trim();
                    if (!result.TryGetValue(name, out current))
                    {
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        result.Add(name, current);
                    }
                    continue;
                }

                int equals = rawLine.IndexOf('=');
                if (current == null || equals <= 0)
                    continue;

                string key = rawLine.Substring(0, equals).Trim();
                string value = rawLine.Substring(equals + 1); // keep trailing spaces (NFO footers rely on them)
                if (!current.ContainsKey(key))
                    current.Add(key, value);
            }
            return result;
        }

        private static Dictionary<string, string> Section(Dictionary<string, Dictionary<string, string>> ini, string name)
        {
            Dictionary<string, string> section;
            return ini.TryGetValue(name, out section) ? section : new Dictionary<string, string>();
        }

        private static string Get(Dictionary<string, string> section, string key)
        {
            string value;
            if (!section.TryGetValue(key, out value) || string.IsNullOrEmpty(value))
                return null;
            return value;
        }

        private static bool GetBool(Dictionary<string, string> section, string key, bool defaultValue = false)
        {
            string value = Get(section, key);
            if (value == null)
                return defaultValue;
            value = value.Trim();
            return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static int? GetInt(Dictionary<string, string> section, string key)
        {
            int result;
            string value = Get(section, key);
            return value != null && int.TryParse(value.Trim(), out result) && result > 0 ? result : (int?)null;
        }

        /// <summary>2.x stores line breaks in NFO header/footer text as "&amp;nl;".</summary>
        private static string Text(string value)
        {
            return value == null ? null : value.Replace("&nl;", Environment.NewLine);
        }
    }
}

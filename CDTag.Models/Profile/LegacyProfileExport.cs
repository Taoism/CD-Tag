using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CDTag.Model.Profile
{
    /// <summary>
    /// Writes a profile back as a CD-Tag 2.x profile (.cfg, DOS code page) with its NFO template.
    /// </summary>
    /// <remarks>
    /// A profile imported from 2.x keeps its original file (name<see cref="LegacyProfile.BackupExtension"/>). The export starts
    /// from that file, so 2.x settings this version doesn't use (APE tags, word lists, ...) come back unchanged, and only the
    /// settings that differ from the original are rewritten. Profiles made in this version are written from scratch.
    /// </remarks>
    public static class LegacyProfileExport
    {
        private static readonly string[] NameSections = { "NAME0", "NAME1", "NAME2", "NAME3", "NAME4", "NAME5" };

        /// <summary>
        /// Writes <paramref name="profile"/> to <paramref name="directory"/> as &lt;name&gt;.cfg, plus &lt;name&gt;.nfo when the
        /// profile has an NFO template. Returns the paths written.
        /// </summary>
        /// <param name="profileDirectory">Folder of the profile (where its template and 2.x original are), or null.</param>
        /// <param name="originalPath">2.x file to start from; by default the profile's own .cfg when it is a 2.x profile,
        /// else its preserved original (name<see cref="LegacyProfile.BackupExtension"/>).</param>
        public static List<string> Export(UserProfile profile, string profileDirectory, string directory, string originalPath = null)
        {
            if (profile == null)
                throw new ArgumentNullException("profile");
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentNullException("directory");

            string name = profile.ProfileName;
            Directory.CreateDirectory(directory);
            var written = new List<string>();

            // NFO template next to the .cfg: kept by name when it is already there (2.x profiles can share one),
            // otherwise copied as <name>.nfo
            string templateName = null;
            string template = TemplateFile(profile, profileDirectory);
            bool inPlace = profileDirectory != null &&
                           string.Equals(Path.GetFullPath(profileDirectory).TrimEnd('\\'), Path.GetFullPath(directory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            if (template == null && inPlace && profile.NFOOptions != null && !string.IsNullOrWhiteSpace(profile.NFOOptions.TemplatePath))
            {
                // Saving in place: a template that isn't there (yet) keeps its name
                templateName = profile.NFOOptions.TemplatePath;
            }
            else if (template != null)
            {
                if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(template)), Path.GetFullPath(directory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    templateName = Path.GetFileName(template);
                }
                else if (inPlace)
                {
                    // Saving in place: a template somewhere else stays where it is
                    templateName = Path.GetFullPath(template);
                }
                else
                {
                    templateName = name + ".nfo";
                    string target = Path.Combine(directory, templateName);
                    File.Copy(template, target, overwrite: true);
                    written.Add(target);
                }
            }

            // Start from the 2.x original when there is one
            string original = originalPath;
            if (original == null && profileDirectory != null)
            {
                original = new[] { Path.Combine(profileDirectory, name + ".cfg"), Path.Combine(profileDirectory, name + LegacyProfile.BackupExtension) }
                    .FirstOrDefault(LegacyProfile.IsLegacyFile);
            }
            IniDocument ini;
            UserProfile before = null;
            if (original != null && File.Exists(original))
            {
                ini = IniDocument.Parse(File.ReadAllLines(original, LegacyProfile.DosEncoding));
                string ignored;
                before = LegacyProfile.Convert(original, name, out ignored);
            }
            else
            {
                ini = new IniDocument();
                ini.Set("CONFIG", "Revision", "13");
            }

            Dictionary<string, string> now = Flatten(profile);
            Dictionary<string, string> then = before != null ? Flatten(before) : null;
            foreach (KeyValuePair<string, string> value in now)
            {
                string old;
                if (then != null && then.TryGetValue(value.Key, out old) && old == value.Value)
                    continue;
                int dot = value.Key.IndexOf('|');
                ini.Set(value.Key.Substring(0, dot), value.Key.Substring(dot + 1), value.Value);
            }

            Dictionary<string, List<string>> lists = Lists(profile);
            Dictionary<string, List<string>> oldLists = before != null ? Lists(before) : null;
            foreach (KeyValuePair<string, List<string>> list in lists)
            {
                List<string> old;
                if (oldLists != null && oldLists.TryGetValue(list.Key, out old) && old.SequenceEqual(list.Value))
                    continue;
                ini.SetList(list.Key, list.Value);
            }

            // Without a template file to copy, a 2.x original keeps the template name it had.
            if (templateName != null || before == null)
                ini.Set("NFO_OPTIONS", "Template", templateName ?? "");
            ini.Set("CONFIG", "LastUpdated", DateTime.Now.ToString("yyyy-MM-dd h:mm tt", CultureInfo.InvariantCulture));

            string cfg = Path.Combine(directory, name + ".cfg");
            File.WriteAllText(cfg, ini.ToString(), LegacyProfile.DosEncoding);
            written.Insert(0, cfg);
            return written;
        }

        private static string TemplateFile(UserProfile profile, string profileDirectory)
        {
            string template = profile.NFOOptions != null ? profile.NFOOptions.TemplatePath : null;
            if (string.IsNullOrWhiteSpace(template))
                return null;
            string path = Path.IsPathRooted(template) || profileDirectory == null ? template : Path.Combine(profileDirectory, template);
            return File.Exists(path) ? path : null;
        }

        /// <summary>Single values as "SECTION|Key" = text, in 2.x notation.</summary>
        private static Dictionary<string, string> Flatten(UserProfile p)
        {
            var v = new Dictionary<string, string>();
            Action<string, string, string> set = (section, key, value) => v[section + "|" + key] = value ?? "";
            Func<bool, string> b = x => x ? "1" : "0";

            NamingFormat[] slots =
            {
                p.FileNaming.SingleCD.SingleArtist, p.FileNaming.SingleCD.VariousArtists,
                p.FileNaming.MultiCD.SingleArtist, p.FileNaming.MultiCD.VariousArtists,
                p.FileNaming.Vinyl.SingleArtist, p.FileNaming.Vinyl.VariousArtists
            };
            for (int i = 0; i < slots.Length; i++)
            {
                NamingFormat f = slots[i] ?? new NamingFormat();
                set(NameSections[i], "DIR", f.Directory);
                set(NameSections[i], "MP3", f.AudioFile);
                set(NameSections[i], "CUE", f.CUE);
                set(NameSections[i], "M3U", f.Playlist);
                set(NameSections[i], "SFV", f.Checksum);
                set(NameSections[i], "NFO", f.NFO);
                set(NameSections[i], "JPG", f.Images);
                set(NameSections[i], "LOG", f.EACLog);
            }

            set("NAMEOPT", "CharLimit", Number(p.FileNaming.FileCharacterLimit));
            set("NAMEOPT", "DirCharLimit", Number(p.FileNaming.DirectoryCharacterLimit));
            set("NAMEOPT", "StdOnly", b(p.FileNaming.UseStandardCharactersOnly));
            set("NAMEOPT", "Foreign", b(p.FileNaming.UseLatinCharactersOnly));
            set("SMARTCASE", "Caps", b(p.FileNaming.DontChangeCaseWordsWithAllCaps));
            set("SMARTCASE", "Alpha", b(p.FileNaming.DontChangeCaseWordsWithNonLetter));

            NFOOptions nfo = p.NFOOptions;
            set("NFO_OPTIONS", "ShowReleaseScreen", b(nfo.ShowReleaseScreen));
            set("NFO_OPTIONS", "ShowAvgVBR", b(nfo.UseAverageBitrate));
            set("NFO_OPTIONS", "SingleCDHeader", Text(nfo.SingleCDHeader));
            set("NFO_OPTIONS", "MultiCDHeader", Text(nfo.MultiCDHeader));
            set("NFO_OPTIONS", "TracklistFooter", Text(nfo.TracklistFooter));
            set("NFO_OPTIONS", "RnotesHeader", Text(nfo.ReleaseNotesHeader));
            set("NFO_OPTIONS", "RnotesFooter", Text(nfo.ReleaseNotesFooter));
            set("NFO_OPTIONS", "AutoPreviewNFO", b(nfo.AutomaticallyPreviewNFO));
            set("NFO_OPTIONS", "DecChar", nfo.DecimalCharacter);

            Finish finish = p.Finish;
            set("TAG_PROCESS", "RenameDIR", b(finish.RenameDirectory));
            set("TAG_PROCESS", "RenameMP3", b(finish.RenameAudioFiles));
            set("TAG_PROCESS", "RenameCUE", b(finish.RenameCUEFiles));
            set("TAG_PROCESS", "RenameJPG", b(finish.RenameImageFiles));
            set("TAG_PROCESS", "RenameLOG", b(finish.RenameEACLogFiles));
            set("TAG_PROCESS", "MakeM3U", b(finish.CreatePlaylist));
            set("TAG_PROCESS", "MakeSFV", b(finish.CreateSFV));
            set("TAG_PROCESS", "DeleteOld", b(finish.DeleteOldFiles));
            set("TAG_PROCESS", "RemoveEmptyDirs", b(finish.DeleteEmptySubdirectories));
            set("TAG_PROCESS", "MakeNFO", b(finish.NFO == FinishNFO.CreateNew));
            set("TAG_PROCESS", "RenameNFO", b(finish.NFO == FinishNFO.RenameExisting));
            set("TAG_PROCESS", "RenameOrMakeNFO", b(finish.NFO == FinishNFO.RenameIfExistsElseCreate));

            TaggingOptions tagging = p.Tagging ?? new TaggingOptions();
            set("TAGGING", "ID3V2", Action(tagging.Id3v2));
            set("TAGGING", "ID3V1", Action(tagging.Id3v1));
            set("TAGGING", "FLAC_ID3V2", Action(tagging.FlacId3v2));
            set("TAGGING", "FLAC_ID3V1", Action(tagging.FlacId3v1));
            set("TAGGING", "LongTRCK", b(tagging.LongTrackNumbers));
            set("TAGGING", "UseID3v1Comment", b(tagging.UseId3v1Comment));
            set("TAGGING", "SeparateM3U", b(tagging.SeparatePlaylistPerDisc));
            set("TAG_ID3V2", "NoWriteID3v2Comments", b(tagging.NoId3v2Comment));
            set("SFVMD5", "HideGeneratedBy", b(tagging.ChecksumHideGeneratedBy));
            set("SFVMD5", "HideFileInfo", b(tagging.ChecksumHideFileInfo));

            ReleaseInfoOptions release = p.ReleaseInfo;
            if (release != null)
            {
                set("RELEASE_INFO", "GroupInitials", release.GroupInitials);
                set("RELEASE_INFO", "Ripper", release.Ripper);
                set("RELEASE_INFO", "Supplier", release.Supplier);
                set("RELEASE_INFO", "Encoder", release.Encoder);
                set("RELEASE_INFO", "Grabber", release.RippingTool);
                set("RELEASE_INFO", "ID3Comment", release.Comment);
                set("RELEASE_INFO", "LockRipper", b(release.LockRipper));
                set("RELEASE_INFO", "LockSupplier", b(release.LockSupplier));
                set("RELEASE_INFO", "LockEncoder", b(release.LockEncoder));
                set("RELEASE_INFO", "LockID3Comment", b(release.LockComment));
                set("NFO_OPTIONS", "DateFormat", release.NfoDateFormat);
            }

            // Settings 2.x doesn't have
            string extra = LegacyProfile.ExtraSection;
            set(extra, "FileTypes", p.AudioFileTypes);
            set(extra, "Underscores", b(p.FileNaming.UseUnderscores));
            set(extra, "CueInM3U", b(finish.WriteCUEFilesToPlaylist));
            set(extra, "MakeMD5", b(finish.CreateMD5));
            set(extra, "MakeSHA1", b(finish.CreateSHA1));
            set(extra, "FlattenDiscs", b(finish.MoveOutOfDiscFolders));
            set(extra, "EmbedFlacCover", b(tagging.EmbedCoverInFlac));
            set(extra, "SkipSceneRules", b(tagging.SkipSceneRules));

            ExtInfoDefinition[] ext = p.ExtInfo ?? new ExtInfoDefinition[0];
            for (int n = 1; n <= ext.Length && n <= 8; n++)
            {
                string section = n == 1 ? "OPTIONS" : "Options" + n;
                set(section, "Begin", ext[n - 1].Begin);
                set(section, "FieldSep", ext[n - 1].FieldSeparator);
                set(section, "End", ext[n - 1].End);
            }
            return v;
        }

        /// <summary>Numbered list sections ("1=...", "2=...").</summary>
        private static Dictionary<string, List<string>> Lists(UserProfile p)
        {
            var lists = new Dictionary<string, List<string>>();
            NamingRules rules = p.NamingRules;
            if (rules != null)
            {
                ReplacePair[] pairs = rules.Replacements ?? new ReplacePair[0];
                lists["ORIG_LIST"] = pairs.Select(r => r.Find ?? "").ToList();
                lists["REPLACE_LIST"] = pairs.Select(r => r.Replace ?? "").ToList();
                lists["REMOVE_LIST"] = (rules.RemoveStrings ?? new string[0]).ToList();
                lists["LOWERCASE_LIST"] = (rules.LowercaseWords ?? new string[0]).ToList();
                lists["WORDSEP_LIST"] = (rules.WordSeparators ?? new string[0]).ToList();
                lists["FIELDSEP_LIST"] = (rules.FieldSeparators ?? new string[0]).ToList();
            }
            lists["DELETE_LIST"] = (p.DeleteFiles ?? new string[0]).ToList();

            ReleaseInfoOptions release = p.ReleaseInfo;
            if (release != null)
            {
                AddPairs(lists, "RELEASETYPENFO_LIST", "RELEASETYPEDIR_LIST", release.ReleaseTypes);
                AddPairs(lists, "SOURCENFO_LIST", "SOURCEDIR_LIST", release.Sources);
                AddPairs(lists, "LANGRYNFO_LIST", "LANGRYDIR_LIST", release.Languages);
                AddPairs(lists, "LTDEDNFO_LIST", "LTDEDDIR_LIST", release.LimitedEdition);
                AddPairs(lists, "WHITELABELNFO_LIST", "WHITELABELDIR_LIST", release.WhiteLabel);
                AddPairs(lists, "CLEANEXPLICITNFO_LIST", "CLEANEXPLICITDIR_LIST", release.CleanExplicit);
            }

            ExtInfoDefinition[] ext = p.ExtInfo ?? new ExtInfoDefinition[0];
            for (int n = 1; n <= ext.Length && n <= 8; n++)
            {
                string suffix = n == 1 ? "" : n.ToString(CultureInfo.InvariantCulture);
                ExtInfoItem[] items = ext[n - 1].Items ?? new ExtInfoItem[0];
                lists["EXTDIRVAL_LIST" + suffix] = items.Select(i => i.Field ?? "").ToList();
                lists["EXTDIRDIR_LIST" + suffix] = items.Select(i => i.Format == "<Value>" ? "" : i.Format ?? "").ToList();
            }
            return lists;
        }

        private static void AddPairs(Dictionary<string, List<string>> lists, string nfoSection, string dirSection, ReleaseListItem[] items)
        {
            if (items == null)
                return;
            lists[nfoSection] = items.Select(i => i.Nfo ?? "").ToList();
            lists[dirSection] = items.Select(i => i.Dir ?? "").ToList();
        }

        private static string Number(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";
        }

        /// <summary>2.x stores line breaks in NFO header/footer text as "&amp;nl;".</summary>
        private static string Text(string value)
        {
            return value == null ? "" : value.Replace("\r\n", "&nl;").Replace("\n", "&nl;");
        }

        private static string Action(TagAction action)
        {
            switch (action)
            {
                case TagAction.WriteAlways: return "WriteAlways";
                case TagAction.WriteIfExists: return "WriteIfExists";
                case TagAction.Remove: return "Remove";
                default: return "Leave";
            }
        }

        /// <summary>An INI file that keeps the order of its sections and keys.</summary>
        private class IniDocument
        {
            private readonly List<KeyValuePair<string, List<KeyValuePair<string, string>>>> _sections =
                new List<KeyValuePair<string, List<KeyValuePair<string, string>>>>();

            public static IniDocument Parse(IEnumerable<string> lines)
            {
                var doc = new IniDocument();
                List<KeyValuePair<string, string>> current = null;
                foreach (string raw in lines)
                {
                    string line = raw.TrimStart();
                    if (line.Length == 0 || line[0] == ';')
                        continue;
                    if (line[0] == '[')
                    {
                        int end = line.IndexOf(']');
                        current = doc.Section(end > 1 ? line.Substring(1, end - 1).Trim() : line.Substring(1).Trim());
                        continue;
                    }
                    int equals = raw.IndexOf('=');
                    if (current != null && equals > 0)
                        current.Add(new KeyValuePair<string, string>(raw.Substring(0, equals).Trim(), raw.Substring(equals + 1)));
                }
                return doc;
            }

            private List<KeyValuePair<string, string>> Section(string name)
            {
                foreach (var section in _sections)
                {
                    if (string.Equals(section.Key, name, StringComparison.OrdinalIgnoreCase))
                        return section.Value;
                }
                var entries = new List<KeyValuePair<string, string>>();
                _sections.Add(new KeyValuePair<string, List<KeyValuePair<string, string>>>(name, entries));
                return entries;
            }

            public void Set(string section, string key, string value)
            {
                List<KeyValuePair<string, string>> entries = Section(section);
                int index = entries.FindIndex(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
                var entry = new KeyValuePair<string, string>(index >= 0 ? entries[index].Key : key, value ?? "");
                if (index >= 0)
                    entries[index] = entry;
                else
                    entries.Add(entry);
            }

            public void SetList(string section, IList<string> values)
            {
                List<KeyValuePair<string, string>> entries = Section(section);
                entries.Clear();
                for (int i = 0; i < values.Count; i++)
                    entries.Add(new KeyValuePair<string, string>((i + 1).ToString(CultureInfo.InvariantCulture), values[i] ?? ""));
            }

            public override string ToString()
            {
                var text = new System.Text.StringBuilder();
                foreach (var section in _sections)
                {
                    text.Append('[').Append(section.Key).Append("]\r\n");
                    foreach (var entry in section.Value)
                        text.Append(entry.Key).Append('=').Append(entry.Value).Append("\r\n");
                    text.Append("\r\n"); // one blank line between sections, as 2.x writes them
                }
                return text.ToString();
            }
        }
    }
}

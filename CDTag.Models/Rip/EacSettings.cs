using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CDTag.Model.Rip
{
    /// <summary>
    /// Exact Audio Copy's settings (HKCU\Software\AWSoftware\EACU): output folder, file naming, character replacements,
    /// output format, eject, and per drive the read offset, C2 and AccurateRip use. Null fields = not set in EAC.
    /// </summary>
    public partial class EacSettings
    {
        private const string Root = @"Software\AWSoftware\EACU";

        public bool Found { get; private set; }
        public string OutputFolder { get; set; }
        public string Naming { get; set; }
        public string VariousNaming { get; set; }
        public bool UseVariousNaming { get; set; }
        /// <summary>Character replacements for names: EAC's rule "\-" replaces '\' with "-".</summary>
        public List<KeyValuePair<char, string>> Replacements { get; private set; }
        public bool EjectWhenDone { get; set; }
        /// <summary>"wav", "flac" or "mp3" (from EAC's external encoder, or WAV when it uses its internal WAV writer).</summary>
        public string Format { get; set; }

        /// <summary>EAC's metadata provider: "CTDB", "freedb", "GD3", or null when unknown.</summary>
        public string MetadataProvider { get; set; }
        /// <summary>The CTDB plugin's search mode ("Fast", "Default", "Extensive"); "Extensive" when not set.</summary>
        public string CtdbSearch { get; set; } = "Extensive";

        // Drive (when found)
        public string DriveKey { get; set; }
        public int? DriveOffset { get; set; }
        public bool? UseC2 { get; set; }
        public bool? UseAccurateRip { get; set; }
        public bool? OverreadLeadOut { get; set; }

        public EacSettings()
        {
            Replacements = new List<KeyValuePair<char, string>>();
        }

        /// <summary>Defaults that match EAC's own when EAC is not installed.</summary>
        public static EacSettings Defaults()
        {
            var s = new EacSettings
            {
                OutputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
                Naming = @"%albumartist% - %albumtitle%\%tracknr2% - %title%",
                VariousNaming = @"%albumtitle%\%tracknr2% - %artist% - %title%",
                UseVariousNaming = true,
                EjectWhenDone = false,
                Format = "flac"
            };
            foreach (string rule in new[] { "\"''", "\\-", "/,", ":-", "*x", "?", "<[", ">]", "|!" })
                s.Replacements.Add(new KeyValuePair<char, string>(rule[0], rule.Substring(1)));
            return s;
        }

        /// <summary>Reads EAC's settings; the drive's settings come from the key "vendor + product + ' ' + revision".</summary>
        public static EacSettings Load(string vendor = null, string product = null, string revision = null)
        {
            EacSettings s = Defaults();
            using (RegistryKey ext = Registry.CurrentUser.OpenSubKey(Root + @"\Extraction Options"))
            using (RegistryKey comp = Registry.CurrentUser.OpenSubKey(Root + @"\Compression Options"))
            {
                if (ext == null)
                    return s;
                s.Found = true;
                s.OutputFolder = Text(ext, "DirectorySpecification") ?? s.OutputFolder;
                s.Naming = Text(ext, "FileNamingConvention") ?? s.Naming;
                s.VariousNaming = Text(ext, "VariousFileNamingConvention") ?? s.VariousNaming;
                s.UseVariousNaming = Flag(ext, "UseVariousFileNamingConvention") ?? s.UseVariousNaming;
                s.EjectWhenDone = Flag(ext, "EjectWhenDone") ?? false;

                var rules = new List<KeyValuePair<char, string>>();
                for (int i = 0; i < 32; i++)
                {
                    string rule = Text(ext, "ReplacementRule" + i.ToString(CultureInfo.InvariantCulture));
                    if (rule == null)
                        break;
                    if (rule.Length > 0)
                        rules.Add(new KeyValuePair<char, string>(rule[0], rule.Substring(1)));
                }
                if (rules.Count > 0)
                {
                    s.Replacements.Clear();
                    s.Replacements.AddRange(rules);
                }

                s.Format = "wav";
                if (comp != null && Flag(comp, "UseExternalEncoder") == true)
                {
                    string extension = (Text(comp, "ExternalEncoderExtension") ?? "").Trim('.', ' ').ToLowerInvariant();
                    if (extension == "flac" || extension == "mp3")
                        s.Format = extension;
                }
            }

            using (RegistryKey internet = Registry.CurrentUser.OpenSubKey(Root + @"\Internet Options"))
                s.MetadataProvider = ProviderName(internet == null ? null : Text(internet, "MetadataPluginUsed"));
            using (RegistryKey ctdb = Registry.CurrentUser.OpenSubKey(@"Software\CUETools\EACPugin"))   // sic: the plugin's own key
                s.CtdbSearch = (ctdb == null ? null : Text(ctdb, "MetadataSearch")) ?? s.CtdbSearch;

            if (vendor != null && product != null)
            {
                s.DriveKey = vendor + product + " " + (revision ?? "");
                using (RegistryKey drives = Registry.CurrentUser.OpenSubKey(Root + @"\Drive Options"))
                {
                    RegistryKey drive = drives == null ? null : drives.OpenSubKey(s.DriveKey.Trim());
                    if (drive == null && drives != null)
                    {
                        // firmware updated since: match vendor + product only
                        string prefix = (vendor + product).Replace(" ", "");
                        string name = drives.GetSubKeyNames().FirstOrDefault(n => n.Replace(" ", "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                        if (name != null)
                            drive = drives.OpenSubKey(name);
                    }
                    if (drive != null)
                    {
                        using (drive)
                        {
                            int offset = Int(drive, "SampleOffset") ?? 0;
                            if (Flag(drive, "UseCombinedSampleOffset") == true)
                                offset = Int(drive, "CombinedSampleOffset") ?? offset;
                            s.DriveOffset = offset;
                            s.UseC2 = Flag(drive, "UseC2Correction");
                            s.UseAccurateRip = Flag(drive, "UseAccurateRip");
                            s.OverreadLeadOut = Flag(drive, "OverReadLeadOut");
                        }
                    }
                }
            }
            return s;
        }

        /// <summary>The metadata plugin EAC uses, from its ID: the plugin DLL in the EAC folder that contains the ID.</summary>
        private static string ProviderName(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
                return null;
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Exact Audio Copy");
            if (!Directory.Exists(folder))
                return null;
            byte[] ascii = Encoding.ASCII.GetBytes(pluginId.ToUpperInvariant());
            byte[] wide = Encoding.Unicode.GetBytes(pluginId.ToUpperInvariant());
            foreach (string dll in Directory.GetFiles(folder, "*.dll"))
            {
                string name = Path.GetFileName(dll).ToLowerInvariant();
                if (!(name.Contains("ctdb") || name.Contains("freedb") || name.Contains("gd3")))
                    continue;
                byte[] data;
                try { data = File.ReadAllBytes(dll); }
                catch (IOException) { continue; }
                byte[] upper = data.Select(b => b >= (byte)'a' && b <= (byte)'z' ? (byte)(b - 32) : b).ToArray();
                if (IndexOf(upper, ascii) >= 0 || IndexOf(upper, wide) >= 0)
                    return name.Contains("ctdb") ? "CTDB" : name.Contains("freedb") ? "freedb" : "GD3";
            }
            return null;
        }

        private static int IndexOf(byte[] data, byte[] pattern)
        {
            for (int i = 0; i + pattern.Length <= data.Length; i++)
            {
                int j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return i;
            }
            return -1;
        }

        private static string Text(RegistryKey key, string name)
        {
            object v = key.GetValue(name);
            return v as string;
        }

        private static bool? Flag(RegistryKey key, string name)
        {
            object v = key.GetValue(name);
            var bytes = v as byte[];
            if (bytes != null && bytes.Length > 0)
                return bytes[0] != 0;
            if (v is int)
                return (int)v != 0;
            return null;
        }

        private static int? Int(RegistryKey key, string name)
        {
            object v = key.GetValue(name);
            var bytes = v as byte[];
            if (bytes != null && bytes.Length >= 4)
                return BitConverter.ToInt32(bytes, 0);
            if (v is int)
                return (int)v;
            return null;
        }

        // ------------------------------------------------------------------ file names

        /// <summary>
        /// A track's path (without extension) from an EAC naming scheme: %albumartist% %albumtitle% %year% %genre%
        /// %tracknr1% %tracknr2% %artist% %title% (and the older %albuminterpret% %cdtitle%); "\" makes folders.
        /// Each value has the replacement rules applied; empty folder names are dropped.
        /// </summary>
        public string TrackPath(string scheme, string albumArtist, string album, string year, string genre, int track, string artist, string title)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "albumartist", albumArtist }, { "albuminterpret", albumArtist }, { "albumtitle", album }, { "cdtitle", album },
                { "year", year }, { "genre", genre },
                { "tracknr1", track.ToString(CultureInfo.InvariantCulture) }, { "tracknr2", track.ToString("00", CultureInfo.InvariantCulture) },
                { "tracknr3", track.ToString("000", CultureInfo.InvariantCulture) },
                { "artist", artist }, { "title", title }
            };
            string text = PercentVariable().Replace(scheme ?? "", m =>
            {
                string v;
                return values.TryGetValue(m.Groups[1].Value, out v) ? Clean(v) : m.Value;
            });
            var parts = text.Split('\\').Select(p => p.Trim().TrimEnd('.').Trim()).Where(p => p.Length > 0).ToList();
            return string.Join("\\", parts);
        }

        /// <summary>A name part with EAC's replacement rules and then any other invalid file name characters removed.</summary>
        public string Clean(string value)
        {
            var sb = new StringBuilder();
            foreach (char c in value ?? "")
            {
                KeyValuePair<char, string> rule = Replacements.FirstOrDefault(r => r.Key == c);
                if (rule.Key == c && rule.Value != null)
                    sb.Append(rule.Value);
                else if (c < ' ' || Path.GetInvalidFileNameChars().Contains(c))
                    continue;
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        [GeneratedRegex(@"%(\w+)%", RegexOptions.None, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial Regex PercentVariable();
    }
}

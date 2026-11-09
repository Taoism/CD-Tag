using System;
using System.IO;
using System.Runtime.Serialization;

namespace CDTag.Common.Settings
{
    /// <summary>Values edited in Tools > Options (stored in options.json).</summary>
    [DataContract]
    public class AppOptionsData
    {
        public const string StartupLastUsed = "LastUsed";
        public const string StartupSpecific = "Specific";

        public AppOptionsData()
        {
            StartupFolderMode = StartupLastUsed;
            NewTagID3v2Version = "ID3v23";
            SceneTaggingEnabled = "true";
        }

        /// <summary><see cref="StartupLastUsed"/> or <see cref="StartupSpecific"/>.</summary>
        [DataMember]
        public string StartupFolderMode { get; set; }

        /// <summary>Folder opened at startup when <see cref="StartupFolderMode"/> is <see cref="StartupSpecific"/>.</summary>
        [DataMember]
        public string StartupFolder { get; set; }

        /// <summary>Show items with the Hidden attribute (protected operating system items stay hidden).</summary>
        [DataMember]
        public bool ShowHiddenFiles { get; set; }

        /// <summary>ID3v2 version written to files that have no ID3v2 tag yet ("ID3v23" or "ID3v24").</summary>
        [DataMember]
        public string NewTagID3v2Version { get; set; }

        /// <summary>lame.exe; empty = Encoders\lame\lame.exe next to cdtag.exe, then PATH.</summary>
        [DataMember]
        public string LamePath { get; set; }

        /// <summary>flac.exe (decodes FLAC for MP3 encoding); empty = Encoders\flac\flac.exe next to cdtag.exe, then PATH.</summary>
        [DataMember]
        public string FlacPath { get; set; }

        /// <summary>Convert Audio's last preset ("LameV0" or "Lame320").</summary>
        [DataMember]
        public string EncodePreset { get; set; }

        /// <summary>
        /// The address registered with gnudb.org for its CD database (it refuses lookups without one). Empty = gnudb is not used.
        /// </summary>
        [DataMember]
        public string GnudbEmail { get; set; }

        /// <summary>
        /// Discogs personal access token (discogs.com > Settings > Developers). Needed for the Discogs search and covers;
        /// release links work without it.
        /// </summary>
        [DataMember]
        public string DiscogsToken { get; set; }

        /// <summary>
        /// Tag Album also tags the same rip in the other format (the FLAC album and its MP3 encode in folders next to
        /// each other): Finish gives both the same tags and release information, each with its own profile.
        /// </summary>
        [DataMember]
        public bool TagBothFormats { get; set; }

        /// <summary>Finish keeps the rip logs (.log) and renames them; off = Finish deletes them.</summary>
        [DataMember]
        public bool KeepRipLogs { get; set; }

        /// <summary>
        /// Scene-style tagging, "true" or "false" ("enabled" in options.json). On: Finish checks the scene rules, release names
        /// get the bit depth / fix tags, Tag Album shows Bit/kHz and Fix, genres follow the scene list, and the rules are
        /// checked for updates at startup. Missing = on.
        /// </summary>
        [DataMember(Name = "enabled")]
        public string SceneTaggingEnabled { get; set; }

        /// <summary>True unless <see cref="SceneTaggingEnabled"/> is "false".</summary>
        [System.Xml.Serialization.XmlIgnore]
        public bool IsSceneTagging
        {
            get { return !string.Equals((SceneTaggingEnabled ?? "").Trim(), "false", StringComparison.OrdinalIgnoreCase); }
        }

        public AppOptionsData Clone()
        {
            return (AppOptionsData)MemberwiseClone();
        }
    }

    /// <summary>Application-wide options, loaded on first use.</summary>
    public static class AppOptions
    {
        private const string FileName = "options.json";

        /// <summary>
        /// options.json next to the program (the folder with cdtag.exe; for a release build that is the folder above bin\),
        /// or null. When it exists, options are read from and saved to it instead of %LOCALAPPDATA%\CD-Tag.
        /// </summary>
        public static string ProgramOptionsFile
        {
            get
            {
                string baseDirectory = AppContext.BaseDirectory.TrimEnd('\\', '/');
                string programDirectory = string.Equals(Path.GetFileName(baseDirectory), "bin", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(baseDirectory)
                    : baseDirectory;
                string path = programDirectory != null ? Path.Combine(programDirectory, FileName) : null;
                return path != null && File.Exists(path) ? path : null;
            }
        }

        /// <summary>The options file in use (a full path next to the program, or the name in %LOCALAPPDATA%\CD-Tag).</summary>
        private static string Location
        {
            get { return ProgramOptionsFile ?? FileName; }
        }
        private static AppOptionsData _current;

        /// <summary>Raised after <see cref="Save"/> stored new options.</summary>
        public static event EventHandler Changed;

        /// <summary>The current options (never null).</summary>
        public static AppOptionsData Current
        {
            get
            {
                if (_current == null)
                {
                    AppOptionsData loaded;
                    _current = SettingsFile.TryLoad(Location, out loaded) && loaded != null ? loaded : new AppOptionsData();
                }
                return _current;
            }
        }

        /// <summary>Stores <paramref name="options"/> as the current options and notifies listeners.</summary>
        public static void Save(AppOptionsData options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            _current = options.Clone();
            SettingsFile.Save(Location, _current);

            EventHandler handler = Changed;
            if (handler != null)
                handler(null, EventArgs.Empty);
        }

        /// <summary>
        /// Whether a file or folder should be listed in the file browser. Hidden items are shown only
        /// when <see cref="AppOptionsData.ShowHiddenFiles"/> is on; protected operating system items
        /// (Hidden + System, e.g. "System Volume Information") are always skipped, as in Explorer.
        /// </summary>
        public static bool IsVisible(FileSystemInfo info)
        {
            if (info == null)
                return false;

            FileAttributes attributes;
            try
            {
                attributes = info.Attributes;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }

            if ((attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
                return true;
            if ((attributes & FileAttributes.System) == FileAttributes.System)
                return false;
            return Current.ShowHiddenFiles;
        }
    }
}

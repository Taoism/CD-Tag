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

        public AppOptionsData Clone()
        {
            return (AppOptionsData)MemberwiseClone();
        }
    }

    /// <summary>Application-wide options, loaded on first use.</summary>
    public static class AppOptions
    {
        private const string FileName = "options.json";
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
                    _current = SettingsFile.TryLoad(FileName, out loaded) && loaded != null ? loaded : new AppOptionsData();
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
            SettingsFile.Save(FileName, _current);

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

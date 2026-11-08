using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CDTag.Common.Mvvm;
using CDTag.Model.Profile;

namespace CDTag.ViewModels.Profile.EditProfile
{
    /// <summary>A naming slot (2.x NAME0..NAME5) shown in the File naming page.</summary>
    public class NamingSlot
    {
        public string Name { get; set; }
        public Func<UserProfile, NamingFormat> Get { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>The settings pages of Edit Profile, the OK/Apply/Cancel buttons, Rename and Copy.</summary>
    public partial class EditProfileViewModel
    {
        public const int ProfilesPage = 0;

        private static readonly string[] PageNames = { "Profiles", "File naming", "Naming rules", "Release info", "NFO", "Finish", "Tagging" };

        public string[] Pages { get { return PageNames; } }

        /// <summary>Index into <see cref="Pages"/>.</summary>
        public int SelectedPage
        {
            get { return Get<int>("SelectedPage"); }
            set
            {
                Set("SelectedPage", value);
                Header = value >= 0 && value < PageNames.Length ? (value == ProfilesPage ? "Select Profile" : PageNames[value]) : "";
                RefreshChanges();
            }
        }

        public NamingSlot[] NamingSlots { get; } =
        {
            new NamingSlot { Name = "Single CD, single artist", Get = p => Group(p, 0).SingleArtist },
            new NamingSlot { Name = "Single CD, various artists", Get = p => Group(p, 0).VariousArtists },
            new NamingSlot { Name = "Multi-CD, single artist", Get = p => Group(p, 1).SingleArtist },
            new NamingSlot { Name = "Multi-CD, various artists", Get = p => Group(p, 1).VariousArtists },
            new NamingSlot { Name = "Vinyl, single artist", Get = p => Group(p, 2).SingleArtist },
            new NamingSlot { Name = "Vinyl, various artists", Get = p => Group(p, 2).VariousArtists }
        };

        /// <summary>The naming group (0 single CD, 1 multi-CD, 2 vinyl) of a profile, created when missing.</summary>
        private static NamingFormatGroup Group(UserProfile profile, int which)
        {
            FileNaming naming = profile.FileNaming;
            NamingFormatGroup group = which == 0 ? naming.SingleCD : which == 1 ? naming.MultiCD : naming.Vinyl;
            if (group == null)
            {
                group = new NamingFormatGroup();
                if (which == 0) naming.SingleCD = group;
                else if (which == 1) naming.MultiCD = group;
                else naming.Vinyl = group;
            }
            if (group.SingleArtist == null)
                group.SingleArtist = new NamingFormat();
            if (group.VariousArtists == null)
                group.VariousArtists = new NamingFormat();
            return group;
        }

        public NamingSlot SelectedSlot
        {
            get { return Get<NamingSlot>("SelectedSlot") ?? NamingSlots[0]; }
            set
            {
                Set("SelectedSlot", value);
                RaiseEditorChanged("NamingFormat");
            }
        }

        /// <summary>The naming format of the selected slot of the selected profile.</summary>
        public NamingFormat NamingFormat
        {
            get { return Profile != null ? SelectedSlot.Get(Profile) : null; }
        }

        /// <summary>Scene-style tagging (options.json): the scene rules option is shown.</summary>
        public bool SceneTagging { get { return CDTag.Common.Settings.AppOptions.Current.IsSceneTagging; } }

        /// <summary>The naming tokens (the scene ones only with scene-style tagging).</summary>
        public string TokenHint
        {
            get
            {
                return "Tokens: <Artist> <Album> <Year> <Track> <Disc> <Discs> <Song> <Source> " + (SceneTagging ? "<Fix> <BitDepth> " : "") +
                       "<ExtInfo>..<ExtInfo8> <ImageText>; <LC>..</LC> lowercase, <SC>..</SC> smart case.";
            }
        }

        public TagAction[] TagActions { get; } = { TagAction.WriteAlways, TagAction.WriteIfExists, TagAction.Remove, TagAction.Leave };
        public FinishNFO[] FinishNFOChoices { get; } = (FinishNFO[])Enum.GetValues(typeof(FinishNFO));

        /// <summary>Tagging options of the selected profile (created when missing).</summary>
        public TaggingOptions Tagging
        {
            get
            {
                if (Profile == null)
                    return null;
                return Profile.Tagging ?? (Profile.Tagging = new TaggingOptions());
            }
        }

        /// <summary>Release info options of the selected profile (created when missing).</summary>
        public ReleaseInfoOptions ReleaseInfo
        {
            get
            {
                if (Profile == null)
                    return null;
                return Profile.ReleaseInfo ?? (Profile.ReleaseInfo = new ReleaseInfoOptions());
            }
        }

        private NamingRules Rules
        {
            get { return Profile.NamingRules ?? (Profile.NamingRules = new NamingRules()); }
        }

        // ---- Lists edited as text, one entry per line

        public string DeleteFilesText
        {
            get { return Lines(Profile != null ? Profile.DeleteFiles : null); }
            set { if (Profile != null) Profile.DeleteFiles = ParseLines(value); }
        }

        public string RemoveStringsText
        {
            get { return Profile != null && Profile.NamingRules != null ? Lines(Profile.NamingRules.RemoveStrings) : ""; }
            set { if (Profile != null) Rules.RemoveStrings = ParseLines(value); }
        }

        public string LowercaseWordsText
        {
            get { return Profile != null && Profile.NamingRules != null ? Lines(Profile.NamingRules.LowercaseWords) : ""; }
            set { if (Profile != null) Rules.LowercaseWords = ParseLines(value).Select(v => v.Trim()).Where(v => v.Length > 0).ToArray(); }
        }

        /// <summary>"find => replace" per line.</summary>
        public string ReplacementsText
        {
            get
            {
                if (Profile == null || Profile.NamingRules == null || Profile.NamingRules.Replacements == null)
                    return "";
                return string.Join(Environment.NewLine, Profile.NamingRules.Replacements.Select(r => r.Find + " => " + r.Replace));
            }
            set
            {
                if (Profile == null)
                    return;
                Rules.Replacements = SplitLines(value).Where(l => l.Length > 0).Select(l =>
                {
                    int arrow = l.IndexOf(" => ", StringComparison.Ordinal);
                    return arrow < 0 ? new ReplacePair { Find = l, Replace = "" } : new ReplacePair { Find = l.Substring(0, arrow), Replace = l.Substring(arrow + 4) };
                }).ToArray();
            }
        }

        public string ReleaseTypesText
        {
            get { return Profile != null && Profile.ReleaseInfo != null ? ItemsText(Profile.ReleaseInfo.ReleaseTypes) : ""; }
            set { if (Profile != null) ReleaseInfo.ReleaseTypes = ParseItems(value); }
        }

        public string SourcesText
        {
            get { return Profile != null && Profile.ReleaseInfo != null ? ItemsText(Profile.ReleaseInfo.Sources) : ""; }
            set { if (Profile != null) ReleaseInfo.Sources = ParseItems(value); }
        }

        public string LanguagesText
        {
            get { return Profile != null && Profile.ReleaseInfo != null ? ItemsText(Profile.ReleaseInfo.Languages) : ""; }
            set { if (Profile != null) ReleaseInfo.Languages = ParseItems(value); }
        }

        private static readonly string[] EditorProperties =
        {
            "NamingFormat", "Tagging", "ReleaseInfo", "DeleteFilesText", "RemoveStringsText", "LowercaseWordsText", "ReplacementsText",
            "ReleaseTypesText", "SourcesText", "LanguagesText", "NewProfileName"
        };

        private void RaiseEditorChanged(string name)
        {
            RaisePropertyChanged(name, null, null);
        }

        /// <summary>Rebinds the settings pages to the selected profile.</summary>
        private void RefreshEditors()
        {
            NewProfileName = Profile != null ? Profile.ProfileName : null;
            foreach (string name in EditorProperties)
                RaiseEditorChanged(name);
        }

        private static string Lines(string[] values)
        {
            return values == null ? "" : string.Join(Environment.NewLine, values);
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Split('\n');
        }

        private static string[] ParseLines(string text)
        {
            return SplitLines(text).Where(l => l.Length > 0).ToArray();
        }

        /// <summary>"Text" when the NFO and folder text are the same, otherwise "NFO text | folder text".</summary>
        private static string ItemsText(ReleaseListItem[] items)
        {
            if (items == null)
                return "";
            return string.Join(Environment.NewLine, items.Select(i => i.Nfo == i.Dir ? i.Nfo : i.Nfo + " | " + i.Dir));
        }

        private static ReleaseListItem[] ParseItems(string text)
        {
            ReleaseListItem[] items = SplitLines(text).Where(l => l.Trim().Length > 0).Select(l =>
            {
                int bar = l.IndexOf('|');
                if (bar < 0)
                    return new ReleaseListItem { Nfo = l.Trim(), Dir = l.Trim() };
                return new ReleaseListItem { Nfo = l.Substring(0, bar).Trim(), Dir = l.Substring(bar + 1).Trim() };
            }).ToArray();
            return items.Length == 0 ? null : items;
        }

        // ---- Saving

        public ICommand OKCommand { get; private set; }
        public ICommand ApplyCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }

        private void InitializeEditor()
        {
            OKCommand = new DelegateCommand(() => { if (SaveChanges()) CloseWindow(true); });
            ApplyCommand = new DelegateCommand(() => SaveChanges());
            CancelCommand = new DelegateCommand(Cancel);
            SelectedPage = ProfilesPage;
        }

        /// <summary>Updates HasChanges (title asterisk) of the selected profile.</summary>
        public void RefreshChanges()
        {
            if (Profile != null)
                Profile.CheckHasChanges();
            UpdateWindowTitle();
        }

        private List<UserProfile> ChangedProfiles()
        {
            foreach (UserProfile profile in _profiles)
                profile.CheckHasChanges();
            return _profiles.Where(p => p.HasChanges).ToList();
        }

        /// <summary>Saves every changed profile; false when one can't be saved.</summary>
        private bool SaveChanges()
        {
            foreach (UserProfile profile in ChangedProfiles())
            {
                string message;
                if (!profile.ValidateProfileName(out message))
                {
                    MessageBox(message, "Edit Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                try
                {
                    profile.Save();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    MessageBox(string.Format("{0} could not be saved: {1}", profile.ProfileName, ex.Message), "Edit Profile", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            UpdateWindowTitle();
            return true;
        }

        private void Cancel()
        {
            List<UserProfile> changed = ChangedProfiles();
            if (changed.Count > 0)
            {
                var answer = MessageBox(string.Format("Save the changes to {0}?", string.Join(", ", changed.Select(p => p.ProfileName))), "Edit Profile",
                                        MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel)
                    return;
                if (answer == MessageBoxResult.Yes && !SaveChanges())
                    return;
            }
            CloseWindow(false);
        }

        // ---- Rename / Copy

        /// <summary>Name typed on the Profiles page for Rename.</summary>
        public string NewProfileName
        {
            get { return Get<string>("NewProfileName"); }
            set { Set("NewProfileName", value); }
        }

        private void RenameProfile()
        {
            UserProfile profile = Profile;
            string newName = (NewProfileName ?? "").Trim();
            if (profile == null || newName.Length == 0 || newName == profile.ProfileName)
            {
                MessageBox("Type the new name in the Name box, then press Rename.", "Rename profile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_profiles.Any(p => p != profile && string.Equals(p.ProfileName, newName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox(string.Format("A profile named {0} already exists.", newName), "Rename profile", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string oldName = profile.ProfileName;
            string oldFile = profile.GetProfileFileName();
            profile.ProfileName = newName;
            string message;
            if (!profile.ValidateProfileName(out message))
            {
                profile.ProfileName = oldName;
                MessageBox(message, "Rename profile", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string directory = _pathService.ProfileDirectory;
            // Files named after the profile: its NFO template and the 2.x original
            if (profile.NFOOptions != null && string.Equals(profile.NFOOptions.TemplatePath, oldName + ".nfo", StringComparison.OrdinalIgnoreCase))
            {
                MoveIfFree(Path.Combine(directory, oldName + ".nfo"), Path.Combine(directory, newName + ".nfo"));
                profile.NFOOptions.TemplatePath = newName + ".nfo";
            }
            MoveIfFree(Path.Combine(directory, oldName + LegacyProfile.BackupExtension), Path.Combine(directory, newName + LegacyProfile.BackupExtension));
            // The .cfg itself too: saving starts from it, keeping the 2.x settings this version doesn't edit
            MoveIfFree(oldFile, profile.GetProfileFileName());

            profile.Save();
            if (!string.Equals(oldFile, profile.GetProfileFileName(), StringComparison.OrdinalIgnoreCase) && File.Exists(oldFile))
                File.Delete(oldFile);
            UpdateWindowTitle();
        }

        private static void MoveIfFree(string source, string target)
        {
            if (File.Exists(source) && !File.Exists(target))
                File.Move(source, target);
        }

        private void CopyProfile()
        {
            UserProfile profile = Profile;
            if (profile == null)
                return;
            string name = profile.ProfileName + " copy";
            for (int n = 2; _profiles.Any(p => string.Equals(p.ProfileName, name, StringComparison.OrdinalIgnoreCase)); n++)
                name = profile.ProfileName + " copy " + n;

            // Copy of the saved profile (unsaved edits stay with the original)
            UserProfile copy = UserProfile.Load(profile.GetProfileFileName());
            copy.ProfileName = name;
            string directory = _pathService.ProfileDirectory;
            if (copy.NFOOptions != null && !string.IsNullOrEmpty(copy.NFOOptions.TemplatePath) && File.Exists(Path.Combine(directory, copy.NFOOptions.TemplatePath))
                && string.Equals(copy.NFOOptions.TemplatePath, profile.ProfileName + ".nfo", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(Path.Combine(directory, copy.NFOOptions.TemplatePath), Path.Combine(directory, name + ".nfo"), true);
                copy.NFOOptions.TemplatePath = name + ".nfo";
            }
            if (LegacyProfile.IsLegacyFile(profile.GetProfileFileName()))
                File.Copy(profile.GetProfileFileName(), copy.GetProfileFileName(), false);
            copy.Save();
            _profiles.Add(copy);
            Profile = copy;
        }
    }
}

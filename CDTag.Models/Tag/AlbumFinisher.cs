using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using CDTag.Model.Checksum;
using CDTag.Model.Profile;
using IdSharp.Tagging.APEv2;
using IdSharp.Tagging.ID3v1;
using IdSharp.Tagging.ID3v2;
using IdSharp.Tagging.ID3v2.Frames;
using IdSharp.Tagging.VorbisComment;

namespace CDTag.Model.Tag
{
    /// <summary>A file move done by Finish (full paths).</summary>
    public class FileMove
    {
        public string Source { get; set; }
        public string Target { get; set; }
    }

    /// <summary>A file Finish creates (full path) with its content.</summary>
    public class FileCreate
    {
        public string Path { get; set; }
        public byte[] Content { get; set; }
        /// <summary>Content needs the final audio files (checksums), so it is built when the file is written.</summary>
        public Func<byte[]> Build { get; set; }
    }

    /// <summary>Everything Finish will do, checked before anything is touched.</summary>
    public class FinishPlan
    {
        public FinishPlan()
        {
            Problems = new List<string>();
            Warnings = new List<string>();
            Deletes = new List<string>();
            Moves = new List<FileMove>();
            Creates = new List<FileCreate>();
            RemovedFolders = new List<string>();
        }

        /// <summary>CD1 / CD2 / ... folders whose files move into the album folder; deleted afterwards.</summary>
        public List<string> RemovedFolders { get; private set; }

        /// <summary>Scene rules findings (Finish can still run).</summary>
        public List<string> RuleIssues { get; private set; } = new List<string>();

        /// <summary>"FLAC rules v3.0", or null when no rules were checked.</summary>
        public string RuleSetName { get; set; }

        /// <summary>Reasons Finish cannot run.</summary>
        public List<string> Problems { get; private set; }
        public List<string> Warnings { get; private set; }
        public List<string> Deletes { get; private set; }
        /// <summary>Audio files first, then cue sheets, images, logs and the NFO.</summary>
        public List<FileMove> Moves { get; private set; }
        public List<FileCreate> Creates { get; private set; }
        public int TaggedFiles { get; set; }
        /// <summary>The album folder after renaming (same as before when it is not renamed).</summary>
        public string NewDirectory { get; set; }
        public string OldDirectory { get; set; }

        public bool CanRun
        {
            get { return Problems.Count == 0; }
        }

        /// <summary>A short summary for the confirmation dialog.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("Write tags to {0} files.", TaggedFiles).AppendLine();
            int renamed = Moves.Count(m => !string.Equals(m.Source, m.Target, StringComparison.Ordinal));
            if (renamed > 0)
                sb.AppendFormat("Rename {0} files.", renamed).AppendLine();
            if (RemovedFolders.Count > 0)
                sb.AppendFormat("Move the files out of {0} into the album folder and delete {1}.",
                                string.Join(", ", RemovedFolders.Select(System.IO.Path.GetFileName)), RemovedFolders.Count == 1 ? "it" : "those folders").AppendLine();
            foreach (FileCreate create in Creates)
                sb.AppendFormat("Create {0}", Relative(create.Path)).AppendLine();
            if (Deletes.Count > 0)
            {
                sb.AppendFormat("Delete {0} files:", Deletes.Count).AppendLine();
                foreach (string delete in Deletes.Take(12))
                    sb.Append("    ").AppendLine(Relative(delete));
                if (Deletes.Count > 12)
                    sb.AppendFormat("    ... and {0} more", Deletes.Count - 12).AppendLine();
            }
            if (!string.Equals(OldDirectory, NewDirectory, StringComparison.Ordinal))
                sb.AppendFormat("Rename the folder to {0}", System.IO.Path.GetFileName(NewDirectory)).AppendLine();
            foreach (string warning in Warnings)
                sb.AppendLine().Append("Note: ").Append(warning);
            if (RuleSetName != null)
            {
                sb.AppendLine().AppendLine();
                if (RuleIssues.Count == 0)
                {
                    sb.AppendFormat("Scene rules ({0}): no problems found.", RuleSetName);
                }
                else
                {
                    sb.AppendFormat("Scene rules ({0}), {1} problem{2}:", RuleSetName, RuleIssues.Count, RuleIssues.Count == 1 ? "" : "s").AppendLine();
                    foreach (string issue in RuleIssues.Take(15))
                        sb.Append("    ").AppendLine(issue);
                    if (RuleIssues.Count > 15)
                        sb.AppendFormat("    ... and {0} more", RuleIssues.Count - 15).AppendLine();
                }
            }
            return sb.ToString().TrimEnd();
        }

        private string Relative(string path)
        {
            string root = OldDirectory.TrimEnd('\\') + "\\";
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length) : path;
        }
    }

    /// <summary>
    /// Tag Album's Finish (CD-Tag 2.x "process"): writes tags and the cover, deletes the profile's delete list,
    /// renames files and the folder, and creates the NFO, playlists and checksum files.
    /// </summary>
    public partial class AlbumFinisher
    {
        private static readonly string[] ImageExtensions =
        {
            ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".tif", ".tiff", ".webp", ".avif", ".heic", ".heif",
            ".jxl", ".jp2", ".j2k", ".ico", ".tga", ".psd", ".svg", ".pcx"
        };

        private readonly UserProfile _profile;
        private readonly Album _album;
        private readonly ReleaseInfo _release;
        private readonly string _nfoTemplate;
        private readonly AlbumNamer _namer;
        private readonly TaggingOptions _tagging;
        private readonly Finish _finish;
        private HashSet<string> _flattened = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <param name="nfoTemplate">The NFO template text, or null when the profile has none.</param>
        public AlbumFinisher(UserProfile profile, Album album, ReleaseInfo release, string nfoTemplate)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(album);

            _profile = profile;
            _album = album;
            _release = release ?? new ReleaseInfo();
            _nfoTemplate = nfoTemplate;
            _namer = new AlbumNamer(profile, album, _release);
            _tagging = profile.Tagging ?? new TaggingOptions();
            _finish = profile.Finish ?? new Finish();
            KeepLogs = CDTag.Common.Settings.AppOptions.Current.KeepRipLogs;
        }

        /// <summary>Keep and rename the rip logs (.log); false = delete them (Tools > Options).</summary>
        public bool KeepLogs { get; set; }

        /// <summary>Source tag from the release page ("CD", "WEB"...).</summary>
        private string SourceTag
        {
            get { return _release.Source != null ? (string.IsNullOrWhiteSpace(_release.Source.Dir) ? _release.Source.Nfo : _release.Source.Dir) : null; }
        }

        private string Folder
        {
            get { return _album.OldDirectoryName.TrimEnd('\\'); }
        }

        // ------------------------------------------------------------------ plan

        public FinishPlan Plan()
        {
            var plan = new FinishPlan { OldDirectory = Folder, NewDirectory = Folder, TaggedFiles = _album.Tracks.Count };
            List<AlbumTrack> tracks = OrderedTracks();
            var audio = new HashSet<string>(tracks.Select(t => t.OriginalFileName), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> discByFolder = DiscFolders(tracks);

            // CD1 / CD2 / ... folders (directly in the album folder, one disc each) whose files move up
            _flattened = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_finish.MoveOutOfDiscFolders)
            {
                foreach (string folder in discByFolder.Keys)
                {
                    if (string.Equals(Path.GetDirectoryName(folder), Folder, StringComparison.OrdinalIgnoreCase) && Album.IsDiscFolderName(Path.GetFileName(folder)))
                        _flattened.Add(folder);
                }
            }
            plan.RemovedFolders.AddRange(_flattened.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));

            foreach (AlbumTrack track in tracks)
            {
                string name = Path.GetFileName(track.OriginalFileName);
                if (Number(track.TrackNumber) <= 0)
                    plan.Problems.Add(string.Format("{0} has no track number.", name));
                if (string.IsNullOrWhiteSpace(track.Title))
                    plan.Problems.Add(string.Format("{0} has no song name.", name));
                if (!File.Exists(track.OriginalFileName))
                    plan.Problems.Add(string.Format("{0} no longer exists.", name));
                else if ((File.GetAttributes(track.OriginalFileName) & FileAttributes.ReadOnly) != 0)
                    plan.Problems.Add(string.Format("{0} is read-only.", name));
            }
            if (string.IsNullOrWhiteSpace(_album.Artist) && !_album.IsVariousArtists)
                plan.Problems.Add("The album has no artist.");
            if (string.IsNullOrWhiteSpace(_album.AlbumTitle))
                plan.Problems.Add("The album has no title.");

            // Delete list (never the album's own audio files)
            var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_finish.DeleteOldFiles && _profile.DeleteFiles != null)
            {
                foreach (string file in AlbumFiles())
                {
                    string name = Path.GetFileName(file);
                    if (audio.Contains(file))
                        continue;
                    if (_profile.DeleteFiles.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern.Trim(), name, ignoreCase: true)))
                    {
                        plan.Deletes.Add(file);
                        deleted.Add(file);
                    }
                }
            }

            // Rip logs: deleted unless kept in Tools > Options.
            if (!KeepLogs)
            {
                foreach (string file in AlbumFiles().Where(f => IsLog(f) && !deleted.Contains(f)))
                {
                    plan.Deletes.Add(file);
                    deleted.Add(file);
                }
            }

            // Audio files
            if (_finish.RenameAudioFiles)
            {
                foreach (AlbumTrack track in tracks)
                {
                    string newName = _namer.GetTrackFileName(track);
                    if (newName == null)
                    {
                        plan.Problems.Add("The profile has no audio file name format.");
                        break;
                    }
                    plan.Moves.Add(new FileMove { Source = track.OriginalFileName, Target = Path.Combine(FolderAfter(track.OriginalFileName), newName) });
                }
            }

            List<string> remaining = AlbumFiles().Where(f => !deleted.Contains(f) && !audio.Contains(f)).ToList();

            // Cue sheets, logs: one per disc folder (or per disc when the name says which disc)
            if (_finish.RenameCUEFiles)
                PlanPerDisc(plan, remaining, ".cue", NameKind.CUE, discByFolder);
            if (_finish.RenameEACLogFiles)
            {
                PlanPerDisc(plan, remaining, ".log", NameKind.EACLog, discByFolder);
                PlanPerDisc(plan, remaining, ".txt", NameKind.EACLog, discByFolder, AuCdtectEnding, "-aucdtect");
            }

            // Images
            if (_finish.RenameImageFiles)
            {
                foreach (string image in remaining.Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
                {
                    string target = _namer.GetFileName(NameKind.Images, ImageText(image), Path.GetExtension(image).ToLowerInvariant());
                    if (target != null)
                        plan.Moves.Add(new FileMove { Source = image, Target = Path.Combine(FolderAfter(image), target) });
                }
            }

            PlanArtFolders(plan, tracks, deleted);

            // NFO
            string nfoName = _namer.GetFileName(NameKind.NFO);
            List<string> nfos = remaining.Where(f => Path.GetExtension(f).Equals(".nfo", StringComparison.OrdinalIgnoreCase)).ToList();
            bool rename = _finish.NFO == FinishNFO.RenameExisting || (_finish.NFO == FinishNFO.RenameIfExistsElseCreate && nfos.Count > 0);
            bool create = _finish.NFO == FinishNFO.CreateNew || (_finish.NFO == FinishNFO.RenameIfExistsElseCreate && nfos.Count == 0);
            if (nfoName != null && rename)
            {
                if (nfos.Count == 1)
                    plan.Moves.Add(new FileMove { Source = nfos[0], Target = Path.Combine(Folder, nfoName) });
                else if (nfos.Count > 1)
                    plan.Warnings.Add("There are several NFO files; none was renamed.");
            }
            if (nfoName != null && create)
            {
                if (string.IsNullOrEmpty(_nfoTemplate))
                {
                    plan.Warnings.Add("The profile has no NFO template, so no NFO is created.");
                }
                else
                {
                    string text = new NfoGenerator(_profile, _album, _release).Generate(_nfoTemplate);
                    plan.Creates.Add(new FileCreate { Path = Path.Combine(Folder, nfoName), Content = LegacyProfile.DosEncoding.GetBytes(text) });
                }
            }

            PlanFlattening(plan, deleted);
            PlanPlaylistsAndChecksums(plan, tracks, remaining);

            // Folder
            if (_finish.RenameDirectory)
            {
                string folderName = _namer.GetFolderName();
                if (folderName == null)
                {
                    plan.Problems.Add("The profile has no folder name format.");
                }
                else
                {
                    string parent = Path.GetDirectoryName(Folder);
                    string target = parent == null ? Folder : Path.Combine(parent, folderName);
                    if (!string.Equals(target, Folder, StringComparison.OrdinalIgnoreCase) && (Directory.Exists(target) || File.Exists(target)))
                        plan.Problems.Add(string.Format("A folder named {0} already exists.", folderName));
                    plan.NewDirectory = target;
                }
            }

            CheckTargets(plan, deleted);
            CheckSceneRules(plan, deleted);
            return plan;
        }

        private void CheckSceneRules(FinishPlan plan, HashSet<string> deleted)
        {
            SceneRuleSet rules = _tagging.SkipSceneRules || !CDTag.Common.Settings.AppOptions.Current.IsSceneTagging ? SceneRuleSet.None : SceneRules.RuleSetFor(_album, SourceTag);
            if (rules == SceneRuleSet.None)
                return;

            // The files as they will be after Finish, relative to the release folder.
            var moves = plan.Moves.ToDictionary(m => m.Source, m => m.Target, StringComparer.OrdinalIgnoreCase);
            var files = AlbumFiles().Concat(_artFiles).Where(f => !deleted.Contains(f)).Select(f => { string target; return moves.TryGetValue(f, out target) ? target : f; })
                                    .Concat(plan.Creates.Select(c => c.Path))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .Select(f => RelativeTo(Folder, f))
                                    .ToList();
            var release = new SceneRelease
            {
                Name = Path.GetFileName(plan.NewDirectory),
                Files = files,
                Album = _album,
                Source = SourceTag,
                Url = _release.Url,
                Discs = _namer.Discs,
                Year = _namer.Year
            };
            plan.RuleSetName = SceneRules.Describe(rules);
            plan.RuleIssues.AddRange(SceneRules.Check(rules, release));
        }

        private static readonly string[] JunkFiles = { "thumbs.db", "desktop.ini", ".ds_store" };

        private List<string> _artFiles = new List<string>();

        /// <summary>
        /// Art folders (Artwork, Scans, Covers... in the album folder or a disc folder, any folder without tracks that holds
        /// only images): the images go into the album folder with the image names (000- up to 9 discs, 0000- from 10) and the
        /// folder is deleted. A folder with other files in it stays as it is.
        /// </summary>
        private void PlanArtFolders(FinishPlan plan, List<AlbumTrack> tracks, HashSet<string> deleted)
        {
            _artFiles = new List<string>();
            if (!Directory.Exists(Folder))
                return;
            var trackFolders = new HashSet<string>(tracks.Select(t => Path.GetDirectoryName(t.OriginalFileName)), StringComparer.OrdinalIgnoreCase);
            var parents = new List<string> { Folder };
            parents.AddRange(trackFolders.Where(f => !string.Equals(f, Folder, StringComparison.OrdinalIgnoreCase)
                                                     && f.StartsWith(Folder + "\\", StringComparison.OrdinalIgnoreCase)));

            var artFolders = new List<string>();
            foreach (string parent in parents.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (string sub in Directory.GetDirectories(parent).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    if (trackFolders.Any(t => string.Equals(t, sub, StringComparison.OrdinalIgnoreCase) || t.StartsWith(sub + "\\", StringComparison.OrdinalIgnoreCase)))
                        continue; // holds tracks: a disc folder
                    string[] files = Directory.GetFiles(sub, "*", SearchOption.AllDirectories);
                    if (!files.Any(IsImage))
                        continue;
                    string other = files.FirstOrDefault(f => !IsImage(f) && !IsJunk(f));
                    if (other != null)
                    {
                        plan.Warnings.Add(string.Format("{0} has files that are not images ({1}), so it stays as it is.", RelativeTo(Folder, sub), Path.GetFileName(other)));
                        continue;
                    }
                    artFolders.Add(sub);
                }
            }
            if (artFolders.Count == 0)
                return;

            var moves = new List<KeyValuePair<FileMove, string>>(); // move, image text
            foreach (string folder in artFolders)
            {
                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    _artFiles.Add(file);
                    if (!IsImage(file))
                    {
                        plan.Deletes.Add(file);
                        deleted.Add(file);
                        continue;
                    }
                    string text = ImageText(file);
                    string name = _finish.RenameImageFiles ? _namer.GetFileName(NameKind.Images, text, Path.GetExtension(file).ToLowerInvariant()) : null;
                    moves.Add(new KeyValuePair<FileMove, string>(new FileMove { Source = file, Target = Path.Combine(Folder, name ?? Path.GetFileName(file)) }, text));
                }
                plan.RemovedFolders.Add(folder);
            }

            // The same name twice (front.jpg in Artwork\CD1 and Artwork\CD2, or next to the album's own folder.jpg):
            // the folder goes into the image text ("artwork_cd2_front").
            var taken = new HashSet<string>(plan.Moves.Select(m => m.Target), StringComparer.OrdinalIgnoreCase);
            foreach (string file in AlbumFiles().Where(f => !deleted.Contains(f) && plan.Moves.All(m => !string.Equals(m.Source, f, StringComparison.OrdinalIgnoreCase))))
                taken.Add(file);
            var counts = moves.GroupBy(m => m.Key.Target, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var item in moves)
            {
                FileMove move = item.Key;
                if (counts[move.Target] > 1 || taken.Contains(move.Target))
                {
                    string where = NonAlphanumeric().Replace(RelativeTo(Folder, Path.GetDirectoryName(move.Source)).ToLowerInvariant(), "_").Trim('_');
                    string text = where + "_" + item.Value;
                    string name = _finish.RenameImageFiles ? _namer.GetFileName(NameKind.Images, text, Path.GetExtension(move.Source).ToLowerInvariant()) : null;
                    move.Target = Path.Combine(Folder, name ?? (where + "_" + Path.GetFileName(move.Source)));
                }
                plan.Moves.Add(move);
            }
        }

        private static bool IsImage(string file)
        {
            return ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant());
        }

        private bool IsJunk(string file)
        {
            string name = Path.GetFileName(file);
            if (JunkFiles.Contains(name.ToLowerInvariant()))
                return true;
            return _finish.DeleteOldFiles && _profile.DeleteFiles != null
                   && _profile.DeleteFiles.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern.Trim(), name, ignoreCase: true));
        }

        /// <summary>auCDtect Task Manager (y-soft.org) reports: a rip log, "Folder.auCDtect.txt".</summary>
        public const string AuCdtectEnding = ".auCDtect.txt";

        private static bool IsLog(string file)
        {
            return Path.GetExtension(file).Equals(".log", StringComparison.OrdinalIgnoreCase)
                   || file.EndsWith(AuCdtectEnding, StringComparison.OrdinalIgnoreCase);
        }

        /// <param name="ending">Files to rename: names ending in this (default: <paramref name="extension"/>).</param>
        /// <param name="suffix">Added to the new name before the extension ("-aucdtect").</param>
        private void PlanPerDisc(FinishPlan plan, List<string> remaining, string extension, NameKind kind, Dictionary<string, int> discByFolder,
                                 string ending = null, string suffix = "")
        {
            var files = ending != null
                ? remaining.Where(f => f.EndsWith(ending, StringComparison.OrdinalIgnoreCase)).ToList()
                : remaining.Where(f => Path.GetExtension(f).Equals(extension, StringComparison.OrdinalIgnoreCase)).ToList();
            bool multi = _namer.Discs > 1;
            var planned = new List<KeyValuePair<FileMove, int>>();
            foreach (var group in files.GroupBy(f => Path.GetDirectoryName(f), StringComparer.OrdinalIgnoreCase))
            {
                List<string> inFolder = group.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                int folderDisc;
                bool discFolder = discByFolder.TryGetValue(group.Key, out folderDisc) && folderDisc > 0;
                for (int i = 0; i < inFolder.Count; i++)
                {
                    int disc = discFolder ? folderDisc : multi ? DiscFromName(inFolder[i], i + 1) : 0;
                    if (inFolder.Count > 1 && !multi)
                    {
                        plan.Warnings.Add(string.Format("There are several {0} files; they were not renamed.", extension));
                        break;
                    }
                    string target = _namer.GetFileName(kind, null, extension, disc);
                    if (target != null)
                    {
                        var move = new FileMove { Source = inFolder[i], Target = Path.Combine(FolderAfter(inFolder[i]), target) };
                        plan.Moves.Add(move);
                        planned.Add(new KeyValuePair<FileMove, int>(move, disc));
                    }
                }
            }

            // Moved out of CD1 / CD2 / ... with a name format without <Disc>: the discs would share one name
            foreach (var same in planned.Where(p => _flattened.Contains(Path.GetDirectoryName(p.Key.Source)))
                                        .GroupBy(p => p.Key.Target, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(p => p.Value).Distinct().Count() > 1))
            {
                foreach (var item in same)
                {
                    string target = item.Key.Target;
                    item.Key.Target = Path.Combine(Path.GetDirectoryName(target),
                        Path.GetFileNameWithoutExtension(target) + "-cd" + item.Value.ToString(CultureInfo.InvariantCulture) + Path.GetExtension(target));
                }
                plan.Warnings.Add(string.Format("The {0} name format has no <Disc>, so \"-cd1\", \"-cd2\"... is added to {1}.", kind == NameKind.CUE ? "CUE" : "log",
                                                Path.GetFileName(same.Key)));
            }

            if (suffix.Length > 0)
            {
                foreach (var item in planned)
                {
                    string target = item.Key.Target;
                    item.Key.Target = Path.Combine(Path.GetDirectoryName(target), Path.GetFileNameWithoutExtension(target) + suffix + Path.GetExtension(target));
                }
            }
        }

        private void PlanPlaylistsAndChecksums(FinishPlan plan, List<AlbumTrack> tracks, List<string> remaining)
        {
            // Final names of the audio files
            Func<AlbumTrack, string> finalPath = t =>
            {
                FileMove move = plan.Moves.FirstOrDefault(m => string.Equals(m.Source, t.OriginalFileName, StringComparison.OrdinalIgnoreCase));
                return move != null ? move.Target : t.OriginalFileName;
            };

            // Tracks live in the album folder or in disc folders; playlists/checksums go next to them.
            var folders = tracks.GroupBy(t => FolderAfter(t.OriginalFileName), StringComparer.OrdinalIgnoreCase).ToList();

            if (_finish.CreatePlaylist)
            {
                var cues = plan.Moves.Where(m => m.Target.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)).Select(m => m.Target)
                    .Concat(remaining.Where(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) && plan.Moves.All(m => !string.Equals(m.Source, f, StringComparison.OrdinalIgnoreCase))))
                    .ToList();
                bool perDisc = _namer.Discs > 1 && _tagging.SeparatePlaylistPerDisc;
                var groups = perDisc
                    ? tracks.GroupBy(t => Number(t.DiscNumber, 1)).Select(g => new { Disc = g.Key, Tracks = g.ToList() }).ToList()
                    : folders.Select(g => new { Disc = folders.Count > 1 ? DiscOf(g.ToList()) : 0, Tracks = g.ToList() }).ToList();

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool collision = false;
                var playlists = new List<FileCreate>();
                foreach (var group in groups)
                {
                    string folder = FolderAfter(group.Tracks[0].OriginalFileName);
                    string name = _namer.GetFileName(NameKind.Playlist, null, null, group.Disc);
                    if (name == null)
                        break;
                    string path = Path.Combine(folder, name);
                    if (!names.Add(path))
                        collision = true;
                    List<string> lines = new List<string>();
                    if (_finish.WriteCUEFilesToPlaylist)
                        lines.AddRange(cues.Where(c => string.Equals(Path.GetDirectoryName(c), folder, StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName));
                    lines.AddRange(group.Tracks.Select(t => RelativeTo(folder, finalPath(t))));
                    playlists.Add(new FileCreate { Path = path, Content = Playlist(lines) });
                }
                if (collision)
                {
                    // The playlist name has no <Disc>: one playlist for the whole album.
                    string name = _namer.GetFileName(NameKind.Playlist);
                    playlists.Clear();
                    playlists.Add(new FileCreate { Path = Path.Combine(Folder, name), Content = Playlist(tracks.Select(t => RelativeTo(Folder, finalPath(t)))) });
                }
                else if (playlists.Count > 1)
                {
                    // Master playlist of the whole album in disc order, next to the disc playlists: 000- (0000- from 10 discs).
                    string master = _namer.GetFileName(NameKind.Playlist, null, null, AlbumNamer.AllDiscs);
                    if (master != null && !names.Contains(Path.Combine(Folder, master)))
                        playlists.Insert(0, new FileCreate { Path = Path.Combine(Folder, master), Content = Playlist(tracks.Select(t => RelativeTo(Folder, finalPath(t)))) });
                }
                plan.Creates.AddRange(playlists);
            }

            var checksumKinds = new List<Tuple<string, Func<IList<string>, string, byte[]>>>();
            if (_finish.CreateSFV)
                checksumKinds.Add(Tuple.Create<string, Func<IList<string>, string, byte[]>>(".sfv", Sfv));
            if (_finish.CreateMD5)
                checksumKinds.Add(Tuple.Create<string, Func<IList<string>, string, byte[]>>(".md5", (files, folder) => HashList(files, folder, ChecksumKind.Md5)));
            if (_finish.CreateSHA1)
                checksumKinds.Add(Tuple.Create<string, Func<IList<string>, string, byte[]>>(".sha1", (files, folder) => HashList(files, folder, ChecksumKind.Sha1)));

            foreach (var kind in checksumKinds)
            {
                foreach (var group in folders)
                {
                    string folder = group.Key;
                    int disc = folders.Count > 1 ? DiscOf(group.ToList()) : 0;
                    string name = _namer.GetFileName(NameKind.Checksum, null, kind.Item1, disc);
                    if (name == null)
                        break;
                    List<AlbumTrack> groupTracks = group.ToList();
                    var build = kind.Item2;
                    plan.Creates.Add(new FileCreate
                    {
                        Path = Path.Combine(folder, name),
                        Build = () => build(groupTracks.Select(finalPath).ToList(), folder)
                    });
                }
            }
        }

        /// <summary>Folder <paramref name="file"/> ends up in: the album folder for files in CD1 / CD2 / ... folders that move up.</summary>
        private string FolderAfter(string file)
        {
            string folder = Path.GetDirectoryName(file);
            return _flattened.Contains(folder) ? Folder : folder;
        }

        /// <summary>
        /// Files in the CD1 / CD2 / ... folders that nothing else moves or deletes go into the album folder as they are.
        /// A file that would land on an identical file (the same cover in every disc folder) is deleted instead.
        /// </summary>
        private void PlanFlattening(FinishPlan plan, HashSet<string> deleted)
        {
            if (_flattened.Count == 0)
                return;

            var sources = new HashSet<string>(plan.Moves.Select(m => m.Source), StringComparer.OrdinalIgnoreCase);
            foreach (string folder in _flattened.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                foreach (string file in Directory.GetFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    if (!sources.Contains(file) && !deleted.Contains(file))
                        plan.Moves.Add(new FileMove { Source = file, Target = Path.Combine(Folder, Path.GetFileName(file)) });
                }
                if (Directory.GetDirectories(folder).Any(d => !plan.RemovedFolders.Contains(d, StringComparer.OrdinalIgnoreCase)))
                    plan.Warnings.Add(string.Format("{0} has subfolders; they stay where they are, so {0} is not deleted.", Path.GetFileName(folder)));
            }

            // Duplicates: identical files from several disc folders, or identical to a file already in the album folder
            var keep = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // target -> source kept
            var movingAway = new HashSet<string>(plan.Moves.Select(m => m.Source), StringComparer.OrdinalIgnoreCase);
            foreach (FileMove move in plan.Moves.ToList())
            {
                if (!_flattened.Contains(Path.GetDirectoryName(move.Source)))
                {
                    keep[move.Target] = move.Source;
                    continue;
                }
                string kept;
                bool duplicate = keep.TryGetValue(move.Target, out kept)
                    ? SameContent(kept, move.Source)
                    : File.Exists(move.Target) && !movingAway.Contains(move.Target) && !deleted.Contains(move.Target) && SameContent(move.Target, move.Source);
                if (duplicate)
                {
                    plan.Moves.Remove(move);
                    plan.Deletes.Add(move.Source);
                    deleted.Add(move.Source);
                }
                else if (!keep.ContainsKey(move.Target))
                {
                    keep[move.Target] = move.Source;
                }
            }

            // Different files with one name in several disc folders (Folder.auCDtect.txt per disc): each keeps its disc in
            // the name ("Folder.auCDtect-cd2.txt"), so nothing is lost.
            var targets = plan.Moves.GroupBy(m => m.Target, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (FileMove move in plan.Moves.Where(m => _flattened.Contains(Path.GetDirectoryName(m.Source))).ToList())
            {
                bool clash = targets[move.Target] > 1
                             || (File.Exists(move.Target) && !movingAway.Contains(move.Target) && !deleted.Contains(move.Target));
                if (!clash)
                    continue;
                string disc = DiscTag(Path.GetFileName(Path.GetDirectoryName(move.Source)));
                string name = Path.GetFileNameWithoutExtension(move.Target) + "-" + disc + Path.GetExtension(move.Target);
                move.Target = Path.Combine(Folder, name);
            }
        }

        /// <summary>"cd2" for CD2 / Disc 2 / Disk_2 folders; otherwise the folder name in lowercase with underscores.</summary>
        private static string DiscTag(string folderName)
        {
            var number = Digits().Match(folderName ?? "");
            if (number.Success)
                return "cd" + int.Parse(number.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            return NonAlphanumeric().Replace((folderName ?? "disc").Trim().ToLowerInvariant(), "_");
        }

        private static bool SameContent(string a, string b)
        {
            var infoA = new FileInfo(a);
            var infoB = new FileInfo(b);
            if (!infoA.Exists || !infoB.Exists || infoA.Length != infoB.Length)
                return false;
            return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }

        /// <summary>Two moves to one name, or a move onto a file that stays: stop before touching anything.</summary>
        private static void CheckTargets(FinishPlan plan, HashSet<string> deleted)
        {
            var sources = new HashSet<string>(plan.Moves.Select(m => m.Source), StringComparer.OrdinalIgnoreCase);
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FileMove move in plan.Moves)
            {
                if (!targets.Add(move.Target))
                    plan.Problems.Add(string.Format("Two files would be named {0}.", Path.GetFileName(move.Target)));
                else if (File.Exists(move.Target) && !sources.Contains(move.Target) && !deleted.Contains(move.Target))
                    plan.Problems.Add(string.Format("{0} already exists.", Path.GetFileName(move.Target)));
                if (move.Target.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || Path.GetFileName(move.Target).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    plan.Problems.Add(string.Format("{0} is not a valid file name.", Path.GetFileName(move.Target)));
            }
            foreach (FileCreate create in plan.Creates)
            {
                if (targets.Contains(create.Path))
                    plan.Problems.Add(string.Format("{0} would be created and also be a renamed file.", Path.GetFileName(create.Path)));
            }
        }

        // ------------------------------------------------------------------ run

        /// <summary>Carries out <paramref name="plan"/>; returns the album folder afterwards.</summary>
        public string Run(FinishPlan plan, Action<string> progress = null)
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (!plan.CanRun)
                throw new InvalidOperationException(string.Join(Environment.NewLine, plan.Problems));
            progress = progress ?? (s => { });

            foreach (string file in plan.Deletes)
            {
                progress("Deleting " + Path.GetFileName(file));
                if (File.Exists(file))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
            }

            foreach (AlbumTrack track in OrderedTracks())
            {
                progress("Tagging " + Path.GetFileName(track.OriginalFileName));
                WriteTags(track);
            }

            // Two steps so names can be swapped between files.
            var pending = plan.Moves.Where(m => !string.Equals(m.Source, m.Target, StringComparison.Ordinal)).ToList();
            var temporary = new List<FileMove>();
            foreach (FileMove move in pending)
            {
                string temp = Path.Combine(Path.GetDirectoryName(move.Source), string.Concat("~cdtag", Guid.NewGuid().ToString("N").AsSpan(0, 8), Path.GetExtension(move.Source)));
                File.Move(move.Source, temp);
                temporary.Add(new FileMove { Source = temp, Target = move.Target });
            }
            foreach (FileMove move in temporary)
            {
                progress("Renaming " + Path.GetFileName(move.Target));
                File.Move(move.Source, move.Target);
            }

            foreach (FileCreate create in plan.Creates)
            {
                progress("Creating " + Path.GetFileName(create.Path));
                File.WriteAllBytes(create.Path, create.Content ?? create.Build());
            }

            if (_finish.DeleteEmptySubdirectories)
                DeleteEmptyFolders(Folder);

            // The emptied CD1 / CD2 / ... folders
            foreach (string folder in plan.RemovedFolders)
            {
                if (!Directory.Exists(folder))
                    continue;
                DeleteEmptyFolders(folder);
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    progress("Deleting " + Path.GetFileName(folder));
                    Directory.Delete(folder);
                }
            }

            string result = Folder;
            if (!string.Equals(plan.NewDirectory, Folder, StringComparison.Ordinal))
            {
                progress("Renaming the folder");
                if (string.Equals(plan.NewDirectory, Folder, StringComparison.OrdinalIgnoreCase))
                {
                    // Only the case changes.
                    string temp = Folder + "~cdtag";
                    Directory.Move(Folder, temp);
                    Directory.Move(temp, plan.NewDirectory);
                }
                else
                {
                    Directory.Move(Folder, plan.NewDirectory);
                }
                result = plan.NewDirectory;
            }
            return result;
        }

        private static void DeleteEmptyFolders(string folder)
        {
            foreach (string sub in Directory.GetDirectories(folder))
            {
                DeleteEmptyFolders(sub);
                if (!Directory.EnumerateFileSystemEntries(sub).Any())
                    Directory.Delete(sub);
            }
        }

        // ------------------------------------------------------------------ tags

        private void WriteTags(AlbumTrack track)
        {
            string path = track.OriginalFileName;
            int disc = Number(track.DiscNumber, 1);
            int discs = _namer.Discs;
            int tracksOnDisc = _album.Tracks.Count(t => Number(t.DiscNumber, 1) == disc);
            string artist = _album.IsVariousArtists ? track.Artist : (_album.Artist ?? track.Artist);
            string comment = _release.Comment;
            string year = _namer.Year;

            if (track.IsFlac)
            {
                var vorbis = new VorbisComment(path);
                NameValueList items = vorbis.Items;
                // FLAC rules 6.1: every field name once.
                foreach (var duplicate in items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList())
                {
                    foreach (NameValueItem extra in duplicate.Skip(1).ToList())
                        items.Remove(extra);
                }
                Set(items, "ARTIST", artist);
                Set(items, "TITLE", track.Title);
                Set(items, "ALBUM", _album.AlbumTitle);
                Set(items, "DATE", year);
                Remove(items, "YEAR");
                Set(items, "GENRE", _album.Genre);
                Set(items, "TRACKNUMBER", Number(track.TrackNumber).ToString(CultureInfo.InvariantCulture));
                // FLAC rules 6.2.1 field names: TOTALTRACKS, DISCNUMBER, TOTALDISCS (also for one disc).
                Set(items, "TOTALTRACKS", tracksOnDisc.ToString(CultureInfo.InvariantCulture));
                Remove(items, "TRACKTOTAL");
                Set(items, "DISCNUMBER", disc.ToString(CultureInfo.InvariantCulture));
                Set(items, "TOTALDISCS", discs.ToString(CultureInfo.InvariantCulture));
                Remove(items, "DISCTOTAL");
                Set(items, "DISCSUBTITLE", _album.GetDiscName(disc));
                Set(items, "ALBUMARTIST", _album.IsVariousArtists ? "Various Artists" : null);
                Set(items, "COMPILATION", _album.IsVariousArtists ? "1" : null);
                if (!string.IsNullOrWhiteSpace(comment))
                    Set(items, "COMMENT", comment);

                if (_tagging.EmbedCoverInFlac)
                {
                    if (_album.CoverBytes != null)
                    {
                        // Replace the front cover, keep other pictures.
                        var blocks = vorbis.GetPictureBlocks().Where(b => b.Length < 4 || ReadInt32BigEndian(b, 0) != (int)PictureType.CoverFront).ToList();
                        blocks.Insert(0, FlacPictureBlock(_album.CoverBytes));
                        vorbis.SetPictureBlocks(blocks);
                    }
                }
                else if (!SceneRules.IsWeb(SourceTag))
                {
                    // FLAC rules 6.3 (CD, vinyl...): the picture block MUST be empty. WEB FLAC has no such rule,
                    // so artwork from the shop stays.
                    vorbis.SetPictureBlocks(Array.Empty<byte[]>());
                }
                vorbis.Save(path);

                if (_tagging.FlacId3v2 == TagAction.Remove)
                {
                    ID3v2Tag.RemoveTag(path);
                    APEv2Tag.RemoveTag(path);
                }
                if (_tagging.FlacId3v1 == TagAction.Remove)
                    ID3v1Tag.RemoveTag(path);
                return;
            }

            // MP3
            string trackText = _tagging.LongTrackNumbers
                ? string.Format(CultureInfo.InvariantCulture, "{0}/{1}", Number(track.TrackNumber), tracksOnDisc)
                : Number(track.TrackNumber).ToString(CultureInfo.InvariantCulture);
            string discText = discs > 1 ? (_tagging.LongTrackNumbers ? string.Format(CultureInfo.InvariantCulture, "{0}/{1}", disc, discs) : disc.ToString(CultureInfo.InvariantCulture)) : null;

            bool hasId3v2 = ID3v2Tag.DoesTagExist(path);
            if (_tagging.Id3v2 == TagAction.Remove)
            {
                ID3v2Tag.RemoveTag(path);
            }
            else if (_tagging.Id3v2 == TagAction.WriteAlways || (_tagging.Id3v2 == TagAction.WriteIfExists && hasId3v2))
            {
                var id3v2 = new ID3v2Tag(path);
                id3v2.Artist = artist;
                id3v2.Title = track.Title;
                id3v2.Album = _album.AlbumTitle;
                id3v2.Year = year;
                id3v2.Genre = _album.Genre;
                id3v2.TrackNumber = trackText;
                id3v2.DiscNumber = discText;
                id3v2.AlbumArtist = _album.IsVariousArtists ? "Various Artists" : null;
                id3v2.IsPartOfCompilation = _album.IsVariousArtists;
                if (!_tagging.NoId3v2Comment && !string.IsNullOrWhiteSpace(comment))
                {
                    id3v2.CommentsList.Clear();
                    IComments frame = id3v2.CommentsList.AddNew();
                    frame.Description = "";
                    frame.Value = comment;
                }
                // MP3 rules 15.2: at most 2 MB of pictures per file.
                if (_album.CoverBytes != null && _album.CoverBytes.Length <= SceneRules.MaxMp3PictureBytes)
                {
                    foreach (IAttachedPicture old in id3v2.PictureList.Where(p => p.PictureType == PictureType.CoverFront).ToList())
                        id3v2.PictureList.Remove(old);
                    IAttachedPicture picture = id3v2.PictureList.AddNew();
                    picture.PictureType = PictureType.CoverFront;
                    picture.MimeType = MimeType(_album.CoverBytes);
                    picture.Description = "";
                    picture.PictureData = _album.CoverBytes;
                }
                id3v2.Save(path);
            }

            bool hasId3v1 = ID3v1Tag.DoesTagExist(path);
            if (_tagging.Id3v1 == TagAction.Remove)
            {
                ID3v1Tag.RemoveTag(path);
            }
            else if (_tagging.Id3v1 == TagAction.WriteAlways || (_tagging.Id3v1 == TagAction.WriteIfExists && hasId3v1))
            {
                var id3v1 = hasId3v1 ? new ID3v1Tag(path) : new ID3v1Tag();
                id3v1.Title = Truncate(track.Title, 30);
                id3v1.Artist = Truncate(artist, 30);
                id3v1.Album = Truncate(_album.AlbumTitle, 30);
                id3v1.Year = Truncate(year, 4);
                id3v1.Comment = _tagging.UseId3v1Comment ? Truncate(comment, 28) : "";
                id3v1.TrackNumber = Number(track.TrackNumber);
                id3v1.GenreIndex = string.IsNullOrWhiteSpace(_album.Genre) ? 255 : GenreHelper.GetGenreIndex(_album.Genre);
                id3v1.Save(path);
            }
        }

        private static void Set(NameValueList items, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                Remove(items, key);
            else
                items.SetValue(key, value.Trim());
        }

        private static void Remove(NameValueList items, string key)
        {
            foreach (NameValueItem item in items.Where(i => string.Equals(i.Name, key, StringComparison.OrdinalIgnoreCase)).ToList())
                items.Remove(item);
        }

        private static string Truncate(string text, int length)
        {
            text = (text ?? "").Trim();
            return text.Length > length ? text.Substring(0, length) : text;
        }

        // ------------------------------------------------------------------ pictures

        public static string MimeType(byte[] image)
        {
            if (image.Length > 3 && image[0] == 0x89 && image[1] == 'P' && image[2] == 'N' && image[3] == 'G')
                return "image/png";
            if (image.Length > 2 && image[0] == 'G' && image[1] == 'I' && image[2] == 'F')
                return "image/gif";
            return "image/jpeg";
        }

        /// <summary>A FLAC PICTURE block body (front cover) for <paramref name="image"/>.</summary>
        public static byte[] FlacPictureBlock(byte[] image)
        {
            int width = 0, height = 0, depth = 24;
            try
            {
                BitmapFrame frame = BitmapDecoder.Create(new MemoryStream(image), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
                depth = frame.Format.BitsPerPixel > 0 ? frame.Format.BitsPerPixel : 24;
            }
            catch (Exception)
            {
                // Size is informational only.
            }

            byte[] mime = Encoding.ASCII.GetBytes(MimeType(image));
            using (var stream = new MemoryStream())
            {
                WriteInt32BigEndian(stream, (int)PictureType.CoverFront);
                WriteInt32BigEndian(stream, mime.Length);
                stream.Write(mime, 0, mime.Length);
                WriteInt32BigEndian(stream, 0); // description
                WriteInt32BigEndian(stream, width);
                WriteInt32BigEndian(stream, height);
                WriteInt32BigEndian(stream, depth);
                WriteInt32BigEndian(stream, 0); // colors (not indexed)
                WriteInt32BigEndian(stream, image.Length);
                stream.Write(image, 0, image.Length);
                return stream.ToArray();
            }
        }

        private static void WriteInt32BigEndian(Stream stream, int value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static int ReadInt32BigEndian(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        // ------------------------------------------------------------------ playlists, checksums

        private static byte[] Playlist(IEnumerable<string> lines)
        {
            string text = string.Join("\r\n", lines) + "\r\n";
            return Ansi.GetBytes(text);
        }

        private byte[] Sfv(IList<string> files, string folder)
        {
            return HashList(files, folder, ChecksumKind.Sfv);
        }

        private byte[] HashList(IList<string> files, string folder, ChecksumKind kind)
        {
            List<ChecksumItem> items = files.Select(f => new ChecksumItem { Path = f, Hash = ChecksumList.Compute(f, kind) }).ToList();
            return ChecksumList.Build(kind, items, folder, _tagging.ChecksumHideGeneratedBy, _tagging.ChecksumHideFileInfo);
        }

        private static Encoding Ansi
        {
            get
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(1252);
            }
        }

        // ------------------------------------------------------------------ helpers

        private List<AlbumTrack> OrderedTracks()
        {
            return _album.Tracks.OrderBy(t => Number(t.DiscNumber, 1)).ThenBy(t => Number(t.TrackNumber))
                .ThenBy(t => t.OriginalFileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Files in the album folder and its disc folders.</summary>
        private List<string> AlbumFiles()
        {
            var files = new List<string>();
            if (!Directory.Exists(Folder))
                return files;
            files.AddRange(Directory.GetFiles(Folder));
            foreach (string sub in Directory.GetDirectories(Folder))
            {
                if (Album.IsDiscFolderName(Path.GetFileName(sub)) || _album.Tracks.Any(t => string.Equals(Path.GetDirectoryName(t.OriginalFileName), sub, StringComparison.OrdinalIgnoreCase)))
                    files.AddRange(Directory.GetFiles(sub));
            }
            return files;
        }

        /// <summary>Folders below the album folder that hold one disc, with that disc number.</summary>
        private Dictionary<string, int> DiscFolders(List<AlbumTrack> tracks)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in tracks.GroupBy(t => Path.GetDirectoryName(t.OriginalFileName), StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(group.Key, Folder, StringComparison.OrdinalIgnoreCase))
                    continue;
                int disc = DiscOf(group.ToList());
                if (disc > 0)
                    result[group.Key] = disc;
            }
            return result;
        }

        /// <summary>The disc of a group of tracks, or 0 when they are from several discs.</summary>
        private static int DiscOf(List<AlbumTrack> tracks)
        {
            var discs = tracks.Select(t => Number(t.DiscNumber, 1)).Distinct().ToList();
            return discs.Count == 1 ? discs[0] : 0;
        }

        private static int DiscFromName(string path, int fallback)
        {
            var match = DiscInName().Match(Path.GetFileNameWithoutExtension(path));
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
        }

        /// <summary>
        /// The image text of an image: "front" for folder.jpg; for an image an earlier Finish named
        /// ("000-pink_floyd-album-2020-front-grp.jpg") the part it added is taken off again, even when the name was
        /// shortened to the length limit or got the ending more than once ("-grp-grp").
        /// </summary>
        private string ImageText(string image)
        {
            string name = Path.GetFileNameWithoutExtension(image).Trim();
            string head, tail;
            if (_namer.TryGetImageNameParts(Path.GetExtension(image).ToLowerInvariant(), out head, out tail))
                name = StripImageName(name, head, tail);
            if (name.Equals("folder", StringComparison.OrdinalIgnoreCase) || name.Length == 0)
                return "front";
            return name;
        }

        /// <summary>Takes <paramref name="head"/> (or the shortened start of it) and every <paramref name="tail"/> off <paramref name="name"/>.</summary>
        public static string StripImageName(string name, string head, string tail)
        {
            char[] separators = { '_', '-', ' ', '.' };
            string original = name;

            // The ending, as often as an earlier Finish added it.
            if (!string.IsNullOrEmpty(tail.Trim(separators)))
            {
                while (name.Length > tail.Length && name.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - tail.Length);
            }

            // The start: the whole head, or the part of it kept before the length limit cut it. 0000 and 000 both count.
            if (head.Length > 0)
            {
                int best = 0;
                foreach (string candidate in new[] { head, SwapDiscZeros(head) })
                {
                    int common = 0;
                    while (common < candidate.Length && common < name.Length && char.ToLowerInvariant(candidate[common]) == char.ToLowerInvariant(name[common]))
                        common++;
                    // Only when it is clearly the same name: the whole head, or most of it (at least 12 characters).
                    if (common == candidate.Length || common >= Math.Min(candidate.Length, Math.Max(12, candidate.Length / 2)))
                        best = Math.Max(best, common);
                }
                if (best > 0)
                    name = name.Substring(best);
            }

            name = name.Trim(separators);
            return name.Length > 0 ? name : original;
        }

        private static string SwapDiscZeros(string head)
        {
            if (head.StartsWith("0000", StringComparison.Ordinal))
                return head.Substring(1);
            if (head.StartsWith("000", StringComparison.Ordinal))
                return "0" + head;
            return head;
        }

        private static string RelativeTo(string folder, string path)
        {
            string root = folder.TrimEnd('\\') + "\\";
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length) : Path.GetFileName(path);
        }

        private static int Number(string text, int fallback = 0)
        {
            if (string.IsNullOrWhiteSpace(text))
                return fallback;
            var match = Digits().Match(text);
            int number;
            return match.Success && int.TryParse(match.Value, out number) ? number : fallback;
        }

        [System.Text.RegularExpressions.GeneratedRegex(@"[^a-z0-9]+", System.Text.RegularExpressions.RegexOptions.None, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial System.Text.RegularExpressions.Regex NonAlphanumeric();

        [System.Text.RegularExpressions.GeneratedRegex(@"\d+", System.Text.RegularExpressions.RegexOptions.None, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial System.Text.RegularExpressions.Regex Digits();

        [System.Text.RegularExpressions.GeneratedRegex(@"(?:cd|disc|disk)\s*[-_#.]?\s*(\d{1,2})(?!\d)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, CDTag.Common.RegexTimeouts.Milliseconds)]
        private static partial System.Text.RegularExpressions.Regex DiscInName();
    }

    /// <summary>CRC-32 (IEEE) as used in SFV files.</summary>
    public static class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        public static uint Compute(string path)
        {
            using (FileStream stream = File.OpenRead(path))
                return Compute(stream);
        }

        public static uint Compute(Stream stream)
        {
            uint crc = 0xFFFFFFFFu;
            var buffer = new byte[1 << 16];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                    crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFFu;
        }
    }
}

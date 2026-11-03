using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;
using CDTag.Model.Checksum;
using CDTag.Model.Profile;

namespace CDTag.ViewModels.Checksum
{
    /// <summary>Result of one file in the checksum window.</summary>
    public enum ChecksumState
    {
        None,
        Ok,
        Bad,
        Missing,
        Extra
    }

    /// <summary>One file in the checksum window.</summary>
    public class ChecksumRow : INotifyPropertyChanged
    {
        private string _hash;
        private string _status;
        private ChecksumState _state;

        public string FullPath { get; set; }
        public string Name { get; set; }
        public long? Size { get; set; }

        /// <summary>The hash listed in the checksum file (verify only).</summary>
        public string ExpectedHash { get; set; }

        public string Hash
        {
            get { return _hash; }
            set { _hash = value; OnPropertyChanged("Hash"); }
        }

        public string Status
        {
            get { return _status; }
            set { _status = value; OnPropertyChanged("Status"); }
        }

        public ChecksumState State
        {
            get { return _state; }
            set { _state = value; OnPropertyChanged("State"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class ChecksumKindItem
    {
        public ChecksumKind Kind { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Create/Verify Checksum: makes an SFV, MD5 or SHA-1 list of the files in a folder, or checks the files against one.
    /// </summary>
    public class ChecksumViewModel : ViewModelBase, IChecksumViewModel
    {
        /// <summary>Files listed by "Audio files only".</summary>
        public static readonly string[] AudioExtensions = { ".mp3", ".flac", ".wav", ".ape", ".wv", ".ogg", ".opus", ".m4a", ".aac", ".mpc", ".aiff", ".aif", ".dsf", ".dff" };

        private CancellationTokenSource _cancellation;
        private bool _loading;

        public ChecksumViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            Rows = new ObservableCollection<ChecksumRow>();
            ChecksumFiles = new ObservableCollection<string>();
            Kinds = new List<ChecksumKindItem>
            {
                new ChecksumKindItem { Kind = ChecksumKind.Sfv, Name = "SFV (CRC-32)" },
                new ChecksumKindItem { Kind = ChecksumKind.Md5, Name = "MD5" },
                new ChecksumKindItem { Kind = ChecksumKind.Sha1, Name = "SHA-1" }
            };
            _loading = true;
            SelectedKind = Kinds[0];
            AudioOnly = true;
            _loading = false;

            BrowseCommand = new DelegateCommand(Browse, () => !IsBusy);
            CreateCommand = new DelegateCommand(Create, () => !IsBusy && !IsVerifyMode && Rows.Count > 0 && !string.IsNullOrWhiteSpace(OutputFileName));
            VerifyCommand = new DelegateCommand(Verify, () => !IsBusy && IsVerifyMode && Rows.Any(r => r.State != ChecksumState.Extra && r.State != ChecksumState.Missing));
            CancelCommand = new DelegateCommand(() => { if (_cancellation != null) _cancellation.Cancel(); }, () => IsBusy);
            CloseCommand = new DelegateCommand(() => { if (_cancellation != null) _cancellation.Cancel(); CloseWindow(true); });
        }

        public ObservableCollection<ChecksumRow> Rows { get; private set; }

        /// <summary>Checksum files found in the folder (names relative to it).</summary>
        public ObservableCollection<string> ChecksumFiles { get; private set; }

        public List<ChecksumKindItem> Kinds { get; private set; }

        public string Folder
        {
            get { return Get<string>("Folder"); }
            private set { Set("Folder", value); }
        }

        /// <summary>True: verify a checksum file. False: create one.</summary>
        public bool IsVerifyMode
        {
            get { return Get<bool>("IsVerifyMode"); }
            set
            {
                if (IsVerifyMode == value)
                    return;
                Set("IsVerifyMode", value);
                RaisePropertyChanged("IsCreateMode", !value, value);
                Reload();
            }
        }

        public bool IsCreateMode
        {
            get { return !IsVerifyMode; }
            set { IsVerifyMode = !value; }
        }

        public ChecksumKindItem SelectedKind
        {
            get { return Get<ChecksumKindItem>("SelectedKind"); }
            set
            {
                ChecksumKindItem old = SelectedKind;
                Set("SelectedKind", value);
                if (value == null)
                    return;
                // Keep a typed name, only swap the extension.
                string current = OutputFileName;
                if (string.IsNullOrWhiteSpace(current))
                    OutputFileName = DefaultOutputName(value.Kind);
                else if (old != null && current.EndsWith(ChecksumList.GetExtension(old.Kind), StringComparison.OrdinalIgnoreCase))
                    OutputFileName = current.Substring(0, current.Length - ChecksumList.GetExtension(old.Kind).Length) + ChecksumList.GetExtension(value.Kind);
                if (!_loading && !IsVerifyMode)
                    ClearHashes();
            }
        }

        public bool AudioOnly
        {
            get { return Get<bool>("AudioOnly"); }
            set
            {
                Set("AudioOnly", value);
                if (!_loading && !IsVerifyMode)
                    Reload();
            }
        }

        public bool IncludeSubfolders
        {
            get { return Get<bool>("IncludeSubfolders"); }
            set
            {
                Set("IncludeSubfolders", value);
                if (!_loading && !IsVerifyMode)
                    Reload();
            }
        }

        /// <summary>Name of the checksum file to create (in <see cref="Folder"/>).</summary>
        public string OutputFileName
        {
            get { return Get<string>("OutputFileName"); }
            set { Set("OutputFileName", value); }
        }

        public string SelectedChecksumFile
        {
            get { return Get<string>("SelectedChecksumFile"); }
            set
            {
                if (SelectedChecksumFile == value)
                    return;
                Set("SelectedChecksumFile", value);
                if (!_loading && IsVerifyMode)
                    Reload();
            }
        }

        public bool IsBusy
        {
            get { return Get<bool>("IsBusy"); }
            private set { Set("IsBusy", value); }
        }

        public double Progress
        {
            get { return Get<double>("Progress"); }
            private set { Set("Progress", value); }
        }

        public string StatusText
        {
            get { return Get<string>("StatusText"); }
            private set { Set("StatusText", value); }
        }

        public ICommand BrowseCommand { get; private set; }
        public ICommand CreateCommand { get; private set; }
        public ICommand VerifyCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand CloseCommand { get; private set; }

        public void Load(string folder, bool verify, string checksumFile = null)
        {
            _loading = true;
            try
            {
                Folder = folder;
                OutputFileName = DefaultOutputName(SelectedKind.Kind);
                FindChecksumFiles();
                if (checksumFile != null && Folder != null)
                {
                    string relative = ChecksumList.RelativeTo(Folder, checksumFile);
                    if (!ChecksumFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
                        ChecksumFiles.Add(relative);
                    SelectedChecksumFile = ChecksumFiles.First(f => string.Equals(f, relative, StringComparison.OrdinalIgnoreCase));
                }
                // Nothing to verify: start in create mode.
                IsVerifyMode = verify && ChecksumFiles.Count > 0;
            }
            finally
            {
                _loading = false;
            }
            Reload();
        }

        private void Browse()
        {
            string folder;
            if (_dialogService.ShowFolderDialog("Choose the folder", Folder, null, out folder) != true || string.IsNullOrEmpty(folder))
                return;
            Load(folder, IsVerifyMode);
        }

        private string DefaultOutputName(ChecksumKind kind)
        {
            string name = string.IsNullOrEmpty(Folder) ? "" : Path.GetFileName(Folder.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name))
                name = "checksums";
            return name + ChecksumList.GetExtension(kind);
        }

        private void FindChecksumFiles()
        {
            ChecksumFiles.Clear();
            if (!string.IsNullOrEmpty(Folder) && Directory.Exists(Folder))
            {
                foreach (string file in Directory.GetFiles(Folder).Where(ChecksumList.IsChecksumFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    ChecksumFiles.Add(Path.GetFileName(file));
            }
            if (SelectedChecksumFile == null || !ChecksumFiles.Contains(SelectedChecksumFile))
                SelectedChecksumFile = ChecksumFiles.FirstOrDefault();
        }

        /// <summary>Files that a new checksum file would list.</summary>
        private List<string> ListFiles()
        {
            return Directory.GetFiles(Folder, "*.*", IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => !ChecksumList.IsChecksumFile(f))
                .Where(f => !AudioOnly || AudioExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Where(f => (File.GetAttributes(f) & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(f => ChecksumList.RelativeTo(Folder, f), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void Reload()
        {
            if (_loading)
                return;
            Rows.Clear();
            Progress = 0;
            if (string.IsNullOrEmpty(Folder) || !Directory.Exists(Folder))
            {
                StatusText = string.IsNullOrEmpty(Folder) ? "Choose a folder." : "Folder not found: " + Folder;
                return;
            }

            try
            {
                if (IsVerifyMode)
                    LoadChecksumFile();
                else
                    LoadFolder();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                StatusText = "Error: " + ex.Message;
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private void LoadFolder()
        {
            foreach (string file in ListFiles())
                Rows.Add(new ChecksumRow { FullPath = file, Name = ChecksumList.RelativeTo(Folder, file), Size = new FileInfo(file).Length });
            StatusText = Rows.Count == 0 ? "No files to list." + (AudioOnly ? " Only audio files are listed." : "") : string.Format("{0} files.", Rows.Count);
        }

        private void LoadChecksumFile()
        {
            if (SelectedChecksumFile == null)
            {
                StatusText = "No .sfv, .md5 or .sha1 file in this folder.";
                return;
            }

            string checksumPath = Path.Combine(Folder, SelectedChecksumFile);
            string listFolder = Path.GetDirectoryName(checksumPath);
            List<ChecksumEntry> entries = ChecksumList.Parse(checksumPath);
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ChecksumEntry entry in entries)
            {
                string full = Path.GetFullPath(Path.Combine(listFolder, entry.FileName));
                listed.Add(full);
                bool exists = File.Exists(full);
                Rows.Add(new ChecksumRow
                {
                    FullPath = full,
                    Name = entry.FileName,
                    Size = exists ? new FileInfo(full).Length : (long?)null,
                    ExpectedHash = entry.Hash,
                    State = exists ? ChecksumState.None : ChecksumState.Missing,
                    Status = exists ? "" : "Missing"
                });
            }

            // Audio files next to the list that it does not mention.
            foreach (string file in Directory.GetFiles(listFolder).Where(f => AudioExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!listed.Contains(Path.GetFullPath(file)))
                    Rows.Add(new ChecksumRow { FullPath = file, Name = Path.GetFileName(file), Size = new FileInfo(file).Length, State = ChecksumState.Extra, Status = "Not in the list" });
            }

            int missing = Rows.Count(r => r.State == ChecksumState.Missing);
            StatusText = entries.Count == 0
                ? SelectedChecksumFile + " lists no files."
                : string.Format("{0}: {1} files{2}. Click Verify.", SelectedChecksumFile, entries.Count, missing > 0 ? ", " + missing + " missing" : "");
        }

        private void ClearHashes()
        {
            Progress = 0;
            foreach (ChecksumRow row in Rows)
            {
                row.Hash = null;
                row.Status = null;
                row.State = ChecksumState.None;
            }
        }

        /// <summary>Calculates the hash of each row in the background; returns false when cancelled.</summary>
        private async Task<bool> HashRows(IList<ChecksumRow> rows, ChecksumKind kind, Action<ChecksumRow, string> done)
        {
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;
            IsBusy = true;
            Progress = 0;
            CommandManager.InvalidateRequerySuggested();
            long total = Math.Max(1, rows.Sum(r => r.Size ?? 0));
            long hashed = 0;
            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    ChecksumRow row = rows[i];
                    row.Status = "Calculating...";
                    StatusText = string.Format("Calculating {0} of {1}: {2}", i + 1, rows.Count, row.Name);
                    try
                    {
                        string path = row.FullPath;
                        string hash = await Task.Run(() => ChecksumList.Compute(path, kind, token), token);
                        done(row, hash);
                    }
                    catch (OperationCanceledException)
                    {
                        foreach (ChecksumRow rest in rows.Skip(i))
                            rest.Status = "Cancelled";
                        return false;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        row.Hash = null;
                        row.State = ChecksumState.Bad;
                        row.Status = "Error: " + ex.Message;
                    }
                    hashed += row.Size ?? 0;
                    Progress = 100.0 * hashed / total;
                }
                return true;
            }
            finally
            {
                IsBusy = false;
                _cancellation.Dispose();
                _cancellation = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async void Create()
        {
            ChecksumKind kind = SelectedKind.Kind;
            string name = OutputFileName.Trim();
            if (string.IsNullOrEmpty(Path.GetExtension(name)))
                name += ChecksumList.GetExtension(kind);
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox("The file name has characters that are not allowed: " + name, "Create Checksum", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string target = Path.Combine(Folder, name);
            if (File.Exists(target) && MessageBox(name + " already exists. Replace it?", "Create Checksum", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            ClearHashes();
            List<ChecksumRow> rows = Rows.ToList();
            bool finished = await HashRows(rows, kind, (row, hash) =>
            {
                row.Hash = hash;
                row.State = ChecksumState.Ok;
                row.Status = "Done";
            });
            if (!finished)
            {
                StatusText = "Cancelled; " + name + " was not written.";
                return;
            }
            if (rows.Any(r => r.Hash == null))
            {
                StatusText = "Some files could not be read; " + name + " was not written.";
                return;
            }

            TaggingOptions tagging = ProfileSelector.ActiveProfile != null ? ProfileSelector.ActiveProfile.Tagging : null;
            try
            {
                byte[] content = ChecksumList.Build(kind, rows.Select(r => new ChecksumItem { Path = r.FullPath, Hash = r.Hash }).ToList(), Folder,
                                                    tagging != null && tagging.ChecksumHideGeneratedBy, tagging != null && tagging.ChecksumHideFileInfo);
                File.WriteAllBytes(target, content);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                StatusText = "Error writing " + name + ": " + ex.Message;
                return;
            }
            StatusText = string.Format("Wrote {0} ({1} files).", name, rows.Count);
            _loading = true;
            FindChecksumFiles();
            _loading = false;
        }

        private async void Verify()
        {
            ChecksumKind kind = ChecksumList.FromPath(SelectedChecksumFile) ?? ChecksumKind.Sfv;
            List<ChecksumRow> rows = Rows.Where(r => r.State != ChecksumState.Extra && r.State != ChecksumState.Missing).ToList();
            foreach (ChecksumRow row in rows)
            {
                row.Hash = null;
                row.Status = "Waiting";
                row.State = ChecksumState.None;
            }

            bool finished = await HashRows(rows, kind, (row, hash) =>
            {
                row.Hash = hash;
                bool ok = ChecksumList.HashEquals(hash, row.ExpectedHash);
                row.State = ok ? ChecksumState.Ok : ChecksumState.Bad;
                row.Status = ok ? "OK" : "BAD (listed " + row.ExpectedHash + ")";
            });

            int good = Rows.Count(r => r.State == ChecksumState.Ok);
            int bad = Rows.Count(r => r.State == ChecksumState.Bad);
            int missing = Rows.Count(r => r.State == ChecksumState.Missing);
            int extra = Rows.Count(r => r.State == ChecksumState.Extra);
            var parts = new List<string> { good + " OK" };
            if (bad > 0)
                parts.Add(bad + " bad");
            if (missing > 0)
                parts.Add(missing + " missing");
            if (extra > 0)
                parts.Add(extra + " not in the list");
            StatusText = (finished ? "" : "Cancelled. ") + SelectedChecksumFile + ": " + string.Join(", ", parts) + "." +
                         (finished && bad == 0 && missing == 0 ? " All files are OK." : "");
        }
    }
}

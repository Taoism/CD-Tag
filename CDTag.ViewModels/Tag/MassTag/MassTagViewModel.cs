using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;
using CDTag.Model.Tag;

namespace CDTag.ViewModels.Tag.MassTag
{
    /// <summary>One file in Mass Tag; the columns edit <see cref="Values"/>, <see cref="Original"/> is what the file has.</summary>
    public class MassTagRow : INotifyPropertyChanged
    {
        private string _status;

        public MassTagRow(string path, TagValues original)
        {
            FullPath = path;
            Original = original;
            Values = original.Clone();
        }

        public string FullPath { get; private set; }
        public string FileName { get { return Path.GetFileName(FullPath); } }
        public string Folder { get { return Path.GetDirectoryName(FullPath); } }
        public TagValues Original { get; private set; }
        public TagValues Values { get; private set; }

        public string TrackNumber { get { return Values[TagField.TrackNumber]; } set { SetValue(TagField.TrackNumber, value); } }
        public string Artist { get { return Values[TagField.Artist]; } set { SetValue(TagField.Artist, value); } }
        public string Title { get { return Values[TagField.Title]; } set { SetValue(TagField.Title, value); } }
        public string Album { get { return Values[TagField.Album]; } set { SetValue(TagField.Album, value); } }
        public string AlbumArtist { get { return Values[TagField.AlbumArtist]; } set { SetValue(TagField.AlbumArtist, value); } }
        public string Year { get { return Values[TagField.Year]; } set { SetValue(TagField.Year, value); } }
        public string Genre { get { return Values[TagField.Genre]; } set { SetValue(TagField.Genre, value); } }
        public string DiscNumber { get { return Values[TagField.DiscNumber]; } set { SetValue(TagField.DiscNumber, value); } }
        public string Comment { get { return Values[TagField.Comment]; } set { SetValue(TagField.Comment, value); } }

        public bool IsChanged { get { return Values.ChangedFrom(Original).Count > 0; } }

        public string Status
        {
            get { return _status; }
            set
            {
                _status = value;
                OnPropertyChanged("Status");
            }
        }

        public void SetValue(TagField field, string value)
        {
            if (Values[field] == (value ?? "").Trim())
                return;
            Values[field] = value;
            OnPropertyChanged(field.ToString());
            OnPropertyChanged("IsChanged");
            Status = IsChanged ? "Changed" : null;
        }

        /// <summary>The file was saved (or reverted): the values become the original.</summary>
        public void Reset(TagValues original)
        {
            Original = original;
            Values = original.Clone();
            foreach (TagField field in TagValues.Fields)
                OnPropertyChanged(field.ToString());
            OnPropertyChanged("IsChanged");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>One "set this field on the selected files" line.</summary>
    public class MassTagSetter : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _value;

        public TagField Field { get; set; }
        public string Label { get; set; }

        public bool IsEnabled
        {
            get { return _isEnabled; }
            set { _isEnabled = value; OnPropertyChanged("IsEnabled"); }
        }

        public string Value
        {
            get { return _value; }
            set
            {
                _value = value;
                OnPropertyChanged("Value");
                // Typing a value ticks the box.
                if (!string.IsNullOrEmpty(value) && !IsEnabled)
                    IsEnabled = true;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// Mass Tag: edit the tags of many MP3/FLAC files at once. Fields can be set on the selected files in one go, tracks numbered
    /// in list order and titles taken from the file names. Only changed fields are written; other tag data is kept.
    /// </summary>
    public class MassTagViewModel : ViewModelBase, IMassTagViewModel
    {
        private const string AudioFilter = "Audio files (*.mp3;*.flac)|*.mp3;*.flac|All files (*.*)|*.*";

        public MassTagViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            Rows = new ObservableCollection<MassTagRow>();
            Setters = new List<MassTagSetter>
            {
                new MassTagSetter { Field = TagField.Artist, Label = "_Artist" },
                new MassTagSetter { Field = TagField.Album, Label = "Al_bum" },
                new MassTagSetter { Field = TagField.AlbumArtist, Label = "Album a_rtist" },
                new MassTagSetter { Field = TagField.Year, Label = "_Year" },
                new MassTagSetter { Field = TagField.Genre, Label = "_Genre" },
                new MassTagSetter { Field = TagField.DiscNumber, Label = "_Disc" },
                new MassTagSetter { Field = TagField.Comment, Label = "Co_mment" }
            };

            AddCommand = new DelegateCommand(Add);
            RemoveCommand = new DelegateCommand(Remove, () => SelectedRows != null && SelectedRows.Count > 0);
            ApplyCommand = new DelegateCommand(Apply, () => Rows.Count > 0 && Setters.Any(s => s.IsEnabled));
            NumberCommand = new DelegateCommand(NumberTracks, () => Rows.Count > 0);
            TitlesFromNamesCommand = new DelegateCommand(TitlesFromNames, () => Rows.Count > 0);
            RevertCommand = new DelegateCommand(Revert, () => Rows.Any(r => r.IsChanged));
            SaveCommand = new DelegateCommand(Save, () => Rows.Any(r => r.IsChanged));
            CloseCommand = new DelegateCommand(Close);
        }

        public ObservableCollection<MassTagRow> Rows { get; private set; }
        public List<MassTagSetter> Setters { get; private set; }
        public bool HasSaved { get; private set; }

        public IList SelectedRows
        {
            get { return Get<IList>("SelectedRows"); }
            set
            {
                Set("SelectedRows", value);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string StatusText
        {
            get { return Get<string>("StatusText"); }
            private set { Set("StatusText", value); }
        }

        public ICommand AddCommand { get; private set; }
        public ICommand RemoveCommand { get; private set; }
        public ICommand ApplyCommand { get; private set; }
        public ICommand NumberCommand { get; private set; }
        public ICommand TitlesFromNamesCommand { get; private set; }
        public ICommand RevertCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand CloseCommand { get; private set; }

        /// <summary>Selected rows in list order, or all rows when none are selected.</summary>
        private List<MassTagRow> Targets()
        {
            if (SelectedRows == null || SelectedRows.Count == 0)
                return Rows.ToList();
            var selected = new HashSet<MassTagRow>(SelectedRows.OfType<MassTagRow>());
            return Rows.Where(selected.Contains).ToList();
        }

        private string TargetText(List<MassTagRow> targets)
        {
            return targets.Count == Rows.Count ? "all " + targets.Count + " files" : targets.Count + " selected file" + (targets.Count == 1 ? "" : "s");
        }

        public void AddFiles(IEnumerable<string> paths)
        {
            int added = 0, failed = 0;
            foreach (string path in paths)
            {
                IEnumerable<string> files;
                if (Directory.Exists(path))
                {
                    try
                    {
                        files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).Where(MassTagger.CanTag).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        StatusText = "Error: " + ex.Message;
                        continue;
                    }
                }
                else if (File.Exists(path) && MassTagger.CanTag(path))
                {
                    files = new[] { path };
                }
                else
                {
                    continue;
                }

                foreach (string file in files)
                {
                    if (Rows.Any(r => string.Equals(r.FullPath, file, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    try
                    {
                        Rows.Add(new MassTagRow(file, MassTagger.Read(file)));
                        added++;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is NotSupportedException)
                    {
                        failed++;
                    }
                }
            }
            StatusText = Rows.Count == 0 ? "Add MP3 or FLAC files (or drop them on the list)."
                       : string.Format("{0} files{1}.", Rows.Count, failed > 0 ? ", " + failed + " could not be read" : "");
            CommandManager.InvalidateRequerySuggested();
        }

        private void Add()
        {
            string[] fileNames;
            if (_dialogService.ShowOpenFilesDialog("Add audio files", AudioFilter, null, out fileNames) == true && fileNames != null)
                AddFiles(fileNames);
        }

        private void Remove()
        {
            foreach (MassTagRow row in Targets())
                Rows.Remove(row);
            StatusText = Rows.Count + " files.";
        }

        private void Apply()
        {
            List<MassTagRow> targets = Targets();
            List<MassTagSetter> setters = Setters.Where(s => s.IsEnabled).ToList();
            foreach (MassTagRow row in targets)
            {
                foreach (MassTagSetter setter in setters)
                    row.SetValue(setter.Field, setter.Value);
            }
            StatusText = string.Format("Set {0} on {1}. Click Save to write the tags.", string.Join(", ", setters.Select(s => s.Label.Replace("_", "").ToLowerInvariant())), TargetText(targets));
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>Numbers the tracks 1..n in list order, per disc folder (CD1, CD2...).</summary>
        private void NumberTracks()
        {
            List<MassTagRow> targets = Targets();
            foreach (var folder in targets.GroupBy(r => r.Folder, StringComparer.OrdinalIgnoreCase))
            {
                int number = 1;
                foreach (MassTagRow row in folder)
                    row.SetValue(TagField.TrackNumber, (number++).ToString(CultureInfo.InvariantCulture));
            }
            StatusText = "Numbered " + TargetText(targets) + " in list order. Click Save to write the tags.";
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>Track numbers and titles (and artists, for "01-Artist-Title") from the file names.</summary>
        private void TitlesFromNames()
        {
            List<MassTagRow> targets = Targets();
            int discs = targets.Select(r => r.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int changed = 0;
            foreach (MassTagRow row in targets)
            {
                FileNameInfo info = NameGuesser.FromFileName(row.FileName, discs, row.AlbumArtist.Length > 0 ? row.AlbumArtist : row.Artist);
                if (string.IsNullOrWhiteSpace(info.Title))
                    continue;
                row.SetValue(TagField.Title, info.Title);
                if (info.Track > 0)
                    row.SetValue(TagField.TrackNumber, info.Track.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(info.Artist))
                    row.SetValue(TagField.Artist, info.Artist);
                changed++;
            }
            StatusText = string.Format("Read titles from {0} of {1} file names. Click Save to write the tags.", changed, targets.Count);
            CommandManager.InvalidateRequerySuggested();
        }

        private void Revert()
        {
            foreach (MassTagRow row in Rows.Where(r => r.IsChanged))
            {
                row.Reset(row.Original);
                row.Status = null;
            }
            StatusText = "Changes undone.";
            CommandManager.InvalidateRequerySuggested();
        }

        private void Save()
        {
            int saved = 0, failed = 0;
            foreach (MassTagRow row in Rows.Where(r => r.IsChanged).ToList())
            {
                try
                {
                    MassTagger.Write(row.FullPath, row.Values, row.Values.ChangedFrom(row.Original));
                    row.Reset(MassTagger.Read(row.FullPath));
                    row.Status = "Saved";
                    saved++;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is NotSupportedException)
                {
                    row.Status = "Error: " + ex.Message;
                    failed++;
                }
            }
            if (saved > 0)
                HasSaved = true;
            StatusText = string.Format("Saved {0} file{1}{2}.", saved, saved == 1 ? "" : "s", failed > 0 ? ", " + failed + " failed" : "");
            CommandManager.InvalidateRequerySuggested();
        }

        private void Close()
        {
            int changed = Rows.Count(r => r.IsChanged);
            if (changed > 0)
            {
                MessageBoxResult answer = MessageBox(string.Format("{0} file{1} changed. Save the tags before closing?", changed, changed == 1 ? " has" : "s have"),
                                                     "Mass Tag", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel)
                    return;
                if (answer == MessageBoxResult.Yes)
                {
                    Save();
                    if (Rows.Any(r => r.IsChanged))
                        return;
                }
            }
            CloseWindow(true);
        }
    }
}

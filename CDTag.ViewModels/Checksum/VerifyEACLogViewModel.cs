using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Mvvm;
using CDTag.Model.Checksum;

namespace CDTag.ViewModels.Checksum
{
    /// <summary>A .log file in the folder, shown by its path relative to the folder.</summary>
    public class LogFileItem
    {
        public string FullPath { get; set; }
        public string Name { get; set; }
    }

    /// <summary>One log inside a file (EAC appends a log each time a disc is ripped again).</summary>
    public class LogItem
    {
        public EacLog Log { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Verify EAC Log: checks the log checksum (was the log edited?) and rates the rip settings, CRCs, errors and AccurateRip results.
    /// </summary>
    public class VerifyEACLogViewModel : ViewModelBase, IVerifyEACLogViewModel
    {
        private const string LogFilter = "EAC log files (*.log)|*.log|All files (*.*)|*.*";

        public VerifyEACLogViewModel(IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            LogFiles = new ObservableCollection<LogFileItem>();
            Logs = new ObservableCollection<LogItem>();
            Checks = new ObservableCollection<LogCheck>();
            OpenCommand = new DelegateCommand(Open);
            CloseCommand = new DelegateCommand(() => CloseWindow(true));
        }

        public ObservableCollection<LogFileItem> LogFiles { get; private set; }
        public ObservableCollection<LogItem> Logs { get; private set; }
        public ObservableCollection<LogCheck> Checks { get; private set; }

        public string Folder
        {
            get { return Get<string>("Folder"); }
            private set { Set("Folder", value); }
        }

        public LogFileItem SelectedLogFile
        {
            get { return Get<LogFileItem>("SelectedLogFile"); }
            set
            {
                if (SelectedLogFile == value)
                    return;
                Set("SelectedLogFile", value);
                ReadLogFile();
            }
        }

        public LogItem SelectedLog
        {
            get { return Get<LogItem>("SelectedLog"); }
            set
            {
                if (SelectedLog == value)
                    return;
                Set("SelectedLog", value);
                ShowLog();
            }
        }

        /// <summary>More than one log in the file: show the log chooser.</summary>
        public bool HasSeveralLogs
        {
            get { return Get<bool>("HasSeveralLogs"); }
            private set { Set("HasSeveralLogs", value); }
        }

        public string LogText
        {
            get { return Get<string>("LogText"); }
            private set { Set("LogText", value); }
        }

        /// <summary>Overall result: the worst state of all checks.</summary>
        public LogCheckState OverallState
        {
            get { return Get<LogCheckState>("OverallState"); }
            private set { Set("OverallState", value); }
        }

        public string Summary
        {
            get { return Get<string>("Summary"); }
            private set { Set("Summary", value); }
        }

        public ICommand OpenCommand { get; private set; }
        public ICommand CloseCommand { get; private set; }

        public void Load(string folder, string logFile = null)
        {
            Folder = folder;
            LogFiles.Clear();
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                IEnumerable<string> files;
                try
                {
                    // The album folder and its disc folders (CD1, CD2...).
                    files = Directory.GetFiles(folder, "*.log").Concat(Directory.GetDirectories(folder).SelectMany(d => Directory.GetFiles(d, "*.log"))).ToList();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    files = Directory.GetFiles(folder, "*.log");
                }
                foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    LogFiles.Add(new LogFileItem { FullPath = file, Name = ChecksumList.RelativeTo(folder, file) });
            }
            if (logFile != null && !LogFiles.Any(f => string.Equals(f.FullPath, logFile, StringComparison.OrdinalIgnoreCase)))
                LogFiles.Insert(0, new LogFileItem { FullPath = logFile, Name = Folder != null ? ChecksumList.RelativeTo(Folder, logFile) : Path.GetFileName(logFile) });

            LogFileItem selected = logFile != null ? LogFiles.First(f => string.Equals(f.FullPath, logFile, StringComparison.OrdinalIgnoreCase)) : LogFiles.FirstOrDefault();
            if (selected == null)
            {
                Set<LogFileItem>("SelectedLogFile", null);
                ReadLogFile();
            }
            else
            {
                SelectedLogFile = selected;
            }
        }

        private void Open()
        {
            string fileName;
            if (ShowOpenFileDialog("Open EAC log", LogFilter, out fileName) != true || string.IsNullOrEmpty(fileName))
                return;
            Load(Path.GetDirectoryName(fileName), fileName);
        }

        private void ReadLogFile()
        {
            Logs.Clear();
            Checks.Clear();
            LogText = null;
            HasSeveralLogs = false;
            if (SelectedLogFile == null)
            {
                OverallState = LogCheckState.Info;
                Summary = string.IsNullOrEmpty(Folder) ? "Open a log file." : "No .log files in this folder. Click Open to choose one.";
                return;
            }

            List<EacLog> logs;
            try
            {
                logs = EacLog.Split(EacLog.ReadText(File.ReadAllBytes(SelectedLogFile.FullPath)));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                OverallState = LogCheckState.Bad;
                Summary = "Cannot read " + SelectedLogFile.Name + ": " + ex.Message;
                return;
            }

            for (int i = 0; i < logs.Count; i++)
            {
                string album = logs[i].Album ?? "";
                Logs.Add(new LogItem { Log = logs[i], Name = logs.Count == 1 ? album : string.Format("Log {0} of {1}: {2}", i + 1, logs.Count, album) });
            }
            HasSeveralLogs = Logs.Count > 1;
            Set<LogItem>("SelectedLog", null);
            // The last log is the one that counts (the final rip).
            SelectedLog = Logs.LastOrDefault();
            if (SelectedLog == null)
            {
                OverallState = LogCheckState.Warning;
                Summary = SelectedLogFile.Name + " is empty.";
            }
        }

        private void ShowLog()
        {
            Checks.Clear();
            if (SelectedLog == null)
            {
                LogText = null;
                return;
            }

            EacLog log = SelectedLog.Log;
            LogText = log.Text;
            foreach (LogCheck check in log.Evaluate())
                Checks.Add(check);

            int bad = Checks.Count(c => c.State == LogCheckState.Bad);
            int warnings = Checks.Count(c => c.State == LogCheckState.Warning);
            OverallState = bad > 0 ? LogCheckState.Bad : warnings > 0 ? LogCheckState.Warning : LogCheckState.Good;
            if (!log.IsEnglish && log.Version == null && log.ListedChecksum == null)
            {
                OverallState = LogCheckState.Bad;
                Summary = "This does not look like an EAC log.";
                return;
            }

            string checksum = log.ChecksumValid == true ? "Checksum OK" : log.ChecksumValid == false ? "Checksum does NOT match (edited log)" : "No checksum";
            string result = bad > 0 ? string.Format("{0} problem{1}", bad, bad == 1 ? "" : "s") : warnings > 0 ? "No problems" : "Good rip";
            Summary = string.Format("{0}. {1}{2}.", checksum, result, warnings > 0 ? string.Format(", {0} warning{1}", warnings, warnings == 1 ? "" : "s") : "");
        }
    }
}

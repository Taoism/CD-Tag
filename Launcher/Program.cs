using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CDTag.Launcher
{
    /// <summary>
    /// Starts bin\cdtag.exe. When the .NET 10 Desktop Runtime is missing it is installed first: Microsoft's installer runs
    /// quietly (Windows asks for administrator permission); if that is declined or fails, a private copy of the runtime is
    /// unpacked to %LOCALAPPDATA%\CD-Tag\dotnet, which needs no permission.
    /// </summary>
    internal static class Program
    {
        private const int MajorVersion = 10;
        private const string InstallerUrl = "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";
        // A private copy needs both: the .NET runtime (with dotnet.exe and the host) and the Windows Desktop runtime.
        private static readonly string[] ZipUrls =
        {
            "https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.zip",
            "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.zip"
        };
        private const string DownloadPage = "https://dotnet.microsoft.com/download/dotnet/10.0";

        private static string PrivateRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CD-Tag", "dotnet"); }
        }

        /// <summary>Testing: CDTAG_PRIVATE_RUNTIME=1 ignores installed runtimes and uses only the private copy (no installer).</summary>
        private static bool PrivateOnly
        {
            get { return Environment.GetEnvironmentVariable("CDTAG_PRIVATE_RUNTIME") == "1"; }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // cdtag.exe --update <zip url> <program folder> <CD-Tag process id> <version>: started by CD-Tag (Help or
            // Options > Check for updates) from a temporary copy of this file.
            if (args.Length >= 5 && args[0] == "--update")
                return Updater.Run(args[1], args[2], args[3], args[4]);

            string app = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "cdtag.exe");
            if (!File.Exists(app))
            {
                MessageBox.Show("bin\\cdtag.exe is missing. Download CD-Tag again.", "CD-Tag", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            string root = FindRuntime();
            if (root == null)
            {
                root = Install();
                if (root == null)
                    return 1;
            }
            return Start(app, root, args);
        }

        /// <summary>The dotnet folder that has the .NET 10 Desktop Runtime, or null.</summary>
        private static string FindRuntime()
        {
            if (PrivateOnly)
                return HasDesktopRuntime(PrivateRoot) ? PrivateRoot : null;
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("DOTNET_ROOT"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
                PrivateRoot
            };
            return candidates.FirstOrDefault(HasDesktopRuntime);
        }

        /// <summary>The host, Microsoft.NETCore.App 10.x and Microsoft.WindowsDesktop.App 10.x are all there.</summary>
        private static bool HasDesktopRuntime(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(Path.Combine(root, "host", "fxr")))
                return false;
            return HasVersion(Path.Combine(root, "shared", "Microsoft.NETCore.App"))
                   && HasVersion(Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App"));
        }

        private static bool HasVersion(string shared)
        {
            if (!Directory.Exists(shared))
                return false;
            return Directory.GetDirectories(shared).Select(Path.GetFileName).Any(v =>
            {
                int dot = v.IndexOf('.');
                int major;
                return int.TryParse(dot > 0 ? v.Substring(0, dot) : v, out major) && major == MajorVersion;
            });
        }

        private static int Start(string app, string root, string[] args)
        {
            var info = new ProcessStartInfo(app, string.Join(" ", args.Select(Quote)))
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory
            };
            // A private copy (or another non-standard location) is found through DOTNET_ROOT.
            string programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
            if (!string.Equals(Path.GetFullPath(root).TrimEnd('\\'), Path.GetFullPath(programFiles).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                info.EnvironmentVariables["DOTNET_ROOT"] = root;
            try
            {
                Process.Start(info);
                return 0;
            }
            catch (Win32Exception ex)
            {
                MessageBox.Show("CD-Tag could not be started: " + ex.Message, "CD-Tag", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        internal static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                return arg;
            return "\"" + arg.Replace("\\\"", "\\\\\"").Replace("\"", "\\\"") + "\"";
        }

        // ---- Installing the runtime

        private static string Install()
        {
            string result = null;
            string error = null;
            using (var form = new InstallForm())
            {
                form.Shown += async (s, e) =>
                {
                    try
                    {
                        result = await Task.Run(() => InstallRuntime(form));
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                    form.Close();
                };
                Application.Run(form);
            }

            if (result == null)
            {
                var answer = MessageBox.Show(
                    "The .NET 10 Desktop Runtime could not be installed" + (error != null ? ":\n" + error : ".") +
                    "\n\nCD-Tag needs it to run. Open the download page?", "CD-Tag", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(DownloadPage) { UseShellExecute = true });
            }
            return result;
        }

        private static string InstallRuntime(InstallForm form)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string temp = Path.Combine(Path.GetTempPath(), "cdtag-dotnet-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                // 1. Microsoft's installer, quiet; Windows asks for permission.
                if (!PrivateOnly)
                {
                    form.SetStatus("Downloading the .NET 10 Desktop Runtime...");
                    string installer = Path.Combine(temp, "windowsdesktop-runtime-win-x64.exe");
                    Download(InstallerUrl, installer, form);
                    form.SetStatus("Installing the .NET 10 Desktop Runtime...");
                    try
                    {
                        using (Process setup = Process.Start(new ProcessStartInfo(installer, "/install /quiet /norestart") { UseShellExecute = true, Verb = "runas" }))
                        {
                            setup.WaitForExit();
                        }
                    }
                    catch (Win32Exception)
                    {
                        // Permission declined: fall through to the private copy.
                    }
                    string installed = FindRuntime();
                    if (installed != null)
                        return installed;
                }

                // 2. Private copy for this user, no permission needed.
                string unpacked = Path.Combine(temp, "dotnet");
                for (int i = 0; i < ZipUrls.Length; i++)
                {
                    form.SetStatus(string.Format("Downloading the .NET 10 runtime for CD-Tag ({0} of {1})...", i + 1, ZipUrls.Length));
                    string zip = Path.Combine(temp, "runtime" + i + ".zip");
                    Download(ZipUrls[i], zip, form);
                    form.SetStatus("Unpacking...");
                    Extract(zip, unpacked);
                }
                if (!HasDesktopRuntime(unpacked))
                    throw new InvalidDataException("The downloaded runtime is incomplete.");
                if (Directory.Exists(PrivateRoot))
                    Directory.Delete(PrivateRoot, true);
                Directory.CreateDirectory(Path.GetDirectoryName(PrivateRoot));
                Directory.Move(unpacked, PrivateRoot);
                return FindRuntime();
            }
            finally
            {
                try
                {
                    Directory.Delete(temp, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>Unpacks a zip into a folder, overwriting files both runtime zips contain (licence texts).</summary>
        internal static void Extract(string zip, string directory)
        {
            string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(directory, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        continue; // never outside the folder
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
        }

        internal static void Download(string url, string path, InstallForm form)
        {
            using (var client = new WebClient())
            {
                client.DownloadProgressChanged += (s, e) => form.SetProgress(e.ProgressPercentage);
                client.DownloadFileTaskAsync(new Uri(url), path).GetAwaiter().GetResult();
            }
            form.SetProgress(-1);
        }
    }

    /// <summary>Light or dark like Windows (Settings > Personalization > Colors > app mode), with the accent colour.</summary>
    internal sealed class Theme
    {
        public bool Dark { get; private set; }
        public Color Back { get; private set; }
        public Color Text { get; private set; }
        public Color SecondaryText { get; private set; }
        public Color Track { get; private set; }
        public Color Accent { get; private set; }

        public static Theme FromWindows()
        {
            bool dark = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object light = key != null ? key.GetValue("AppsUseLightTheme") : null;
                    dark = light is int && (int)light == 0;
                }
            }
            catch (System.Security.SecurityException)
            {
            }

            Color accent = dark ? Color.FromArgb(0x4C, 0xC2, 0xFF) : Color.FromArgb(0x00, 0x67, 0xC0);
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    object value = key != null ? key.GetValue("AccentColor") : null;
                    if (value is int)
                    {
                        int abgr = (int)value;
                        accent = Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                    }
                }
            }
            catch (System.Security.SecurityException)
            {
            }

            return dark
                ? new Theme { Dark = true, Back = Color.FromArgb(0x20, 0x20, 0x20), Text = Color.FromArgb(0xF0, 0xF0, 0xF0), SecondaryText = Color.FromArgb(0xB8, 0xB8, 0xB8), Track = Color.FromArgb(0x3A, 0x3A, 0x3A), Accent = accent }
                : new Theme { Dark = false, Back = Color.FromArgb(0xF3, 0xF3, 0xF3), Text = Color.FromArgb(0x1A, 0x1A, 0x1A), SecondaryText = Color.FromArgb(0x5A, 0x5A, 0x5A), Track = Color.FromArgb(0xD8, 0xD8, 0xD8), Accent = accent };
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Dark title bar on Windows 10 2004+ / 11 (DWMWA_USE_IMMERSIVE_DARK_MODE).</summary>
        public void ApplyTitleBar(IntPtr handle)
        {
            int on = Dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref on, sizeof(int)); // older Windows 10 builds
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }
    }

    /// <summary>A flat progress bar in the theme colours; Value -1 = busy (a block moving back and forth).</summary>
    internal sealed class ThemedProgress : Control
    {
        private readonly Timer _timer = new Timer { Interval = 30 };
        private int _value = -1;
        private int _offset;

        public ThemedProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            _timer.Tick += (s, e) =>
            {
                _offset = (_offset + 6) % (Width + Width / 3);
                Invalidate();
            };
            _timer.Start();
        }

        public Color Track { get; set; }
        public Color Fill { get; set; }

        public int Value
        {
            get { return _value; }
            set
            {
                _value = value;
                _timer.Enabled = value < 0;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var track = new SolidBrush(Track))
            using (var fill = new SolidBrush(Fill))
            {
                e.Graphics.FillRectangle(track, ClientRectangle);
                if (_value >= 0)
                {
                    e.Graphics.FillRectangle(fill, 0, 0, Width * Math.Min(100, _value) / 100, Height);
                }
                else
                {
                    int block = Width / 3;
                    e.Graphics.FillRectangle(fill, _offset - block, 0, block, Height);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Small window shown while the runtime is downloaded and installed; light or dark like Windows.</summary>
    internal sealed class InstallForm : Form
    {
        private readonly Theme _theme = Theme.FromWindows();
        private readonly Label _status;
        private readonly ThemedProgress _progress;

        public InstallForm()
            : this("CD-Tag needs the .NET 10 Desktop Runtime. It is being installed; this only happens once.")
        {
        }

        public InstallForm(string introText)
        {
            Text = "CD-Tag";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ControlBox = false; // the install has to finish
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 104);
            Font = new Font("Segoe UI", 9F);
            BackColor = _theme.Back;
            ForeColor = _theme.Text;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (ArgumentException)
            {
            }

            var intro = new Label
            {
                Text = introText,
                Left = 14, Top = 12, Width = 412, Height = 36, ForeColor = _theme.Text
            };
            _status = new Label { Text = "Starting...", Left = 14, Top = 52, Width = 412, Height = 20, ForeColor = _theme.SecondaryText };
            _progress = new ThemedProgress { Left = 14, Top = 78, Width = 412, Height = 6, Track = _theme.Track, Fill = _theme.Accent };
            Controls.Add(intro);
            Controls.Add(_status);
            Controls.Add(_progress);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _theme.ApplyTitleBar(Handle);
        }

        public void SetStatus(string text)
        {
            Invoke(new Action(() => _status.Text = text));
        }

        /// <summary>0-100, or -1 for "busy".</summary>
        public void SetProgress(int percent)
        {
            BeginInvoke(new Action(() => _progress.Value = percent));
        }
    }
}

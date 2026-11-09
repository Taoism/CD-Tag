using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CDTag.Launcher
{
    /// <summary>
    /// Installs a new CD-Tag release: waits until CD-Tag has closed, downloads the release zip, unpacks it over the program
    /// folder and starts CD-Tag again. options.json is kept when there is one; profiles, NFO templates and anything else
    /// that isn't in the zip are never touched. Runs from a temporary copy of cdtag.exe, so the starter can be replaced too.
    /// </summary>
    internal static class Updater
    {
        private const string ReleasesPage = "https://github.com/Taoism/CD-Tag/releases/latest";

        public static int Run(string url, string programDirectory, string processId, string version)
        {
            string error = null;
            using (var form = new InstallForm("CD-Tag " + version + " is being installed. CD-Tag starts again when it is done."))
            {
                form.Shown += async (s, e) =>
                {
                    try
                    {
                        await Task.Run(() => Install(url, programDirectory, processId, form));
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                    form.Close();
                };
                Application.Run(form);
            }

            if (error != null)
            {
                var answer = MessageBox.Show("CD-Tag " + version + " could not be installed:\n" + error +
                                             "\n\nOpen the download page?", "CD-Tag", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true });
            }

            string starter = Path.Combine(programDirectory, "cdtag.exe");
            if (File.Exists(starter))
                Restart(starter);
            return error == null ? 0 : 1;
        }

        private static void Install(string url, string programDirectory, string processId, InstallForm form)
        {
            form.SetStatus("Waiting for CD-Tag to close...");
            WaitForExit(processId);

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string temp = Path.Combine(Path.GetTempPath(), "cdtag-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                form.SetStatus("Downloading...");
                string zip = Path.Combine(temp, "CD-Tag.zip");
                Program.Download(url, zip, form);

                form.SetStatus("Unpacking...");
                string unpacked = Path.Combine(temp, "CD-Tag");
                Program.Extract(zip, unpacked);
                if (!File.Exists(Path.Combine(unpacked, "cdtag.exe")) || !File.Exists(Path.Combine(unpacked, "bin", "cdtag.exe")))
                    throw new InvalidDataException("The download is not a CD-Tag release.");

                form.SetStatus("Installing...");
                CopyTree(unpacked, programDirectory, unpacked);
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

        private static void WaitForExit(string processId)
        {
            int id;
            if (!int.TryParse(processId, out id))
                return;
            try
            {
                using (Process process = Process.GetProcessById(id))
                {
                    if (!process.WaitForExit(120000))
                        throw new TimeoutException("CD-Tag did not close. Close it and try again.");
                }
            }
            catch (ArgumentException)
            {
                // Already closed.
            }
        }

        private static void CopyTree(string source, string target, string root)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
            {
                string destination = Path.Combine(target, Path.GetFileName(file));
                // The user's settings stay.
                bool topLevel = string.Equals(Path.GetFullPath(source).TrimEnd('\\'), Path.GetFullPath(root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                if (topLevel && string.Equals(Path.GetFileName(file), "options.json", StringComparison.OrdinalIgnoreCase) && File.Exists(destination))
                    continue;
                CopyWithRetry(file, destination);
            }
            foreach (string directory in Directory.GetDirectories(source))
                CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)), root);
        }

        /// <summary>A file can stay locked for a moment after CD-Tag closed (virus scanner, Windows Search).</summary>
        private static void CopyWithRetry(string source, string destination)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(destination))
                        File.SetAttributes(destination, FileAttributes.Normal);
                    File.Copy(source, destination, true);
                    return;
                }
                catch (IOException) when (attempt < 40)
                {
                    Thread.Sleep(250);
                }
                catch (UnauthorizedAccessException) when (attempt < 40)
                {
                    Thread.Sleep(250);
                }
            }
        }

        /// <summary>Starts CD-Tag again; not as administrator when the update needed permission (explorer starts it normally).</summary>
        private static void Restart(string starter)
        {
            try
            {
                bool elevated;
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                if (elevated)
                    Process.Start(new ProcessStartInfo("explorer.exe", Program.Quote(starter)) { UseShellExecute = false });
                else
                    Process.Start(new ProcessStartInfo(starter) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(starter) });
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
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
        /// <summary>The only place an update is downloaded from (CD-Tag checks it too: UpdateChecker.DownloadPrefix).</summary>
        private const string DownloadPrefix = "https://github.com/Taoism/CD-Tag/releases/download/";
        /// <summary>The name the running starter gets when it replaces itself (deleted at the next start).</summary>
        private const string OldStarterSuffix = ".old";

        public static int Run(string url, string programDirectory, string processId, string version, string sha256)
        {
            string error = null;
            using (var form = new InstallForm("CD-Tag " + version + " is being installed. CD-Tag starts again when it is done."))
            {
                form.Shown += async (s, e) =>
                {
                    try
                    {
                        await Task.Run(() => Install(url, programDirectory, processId, sha256, form));
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

        private static void Install(string url, string programDirectory, string processId, string sha256, InstallForm form)
        {
            // The arguments come from the command line of a process that may run as administrator: check them first.
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !uri.AbsoluteUri.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.Contains("/../"))
                throw new InvalidDataException("The download address is not a CD-Tag release on GitHub.");
            if (string.IsNullOrEmpty(sha256) || sha256.Length != 64)
                throw new InvalidDataException("The update has no valid checksum.");
            if (!Directory.Exists(programDirectory) || !File.Exists(Path.Combine(programDirectory, "bin", "cdtag.exe")))
                throw new InvalidDataException("The program folder is not a CD-Tag folder: " + programDirectory);

            form.SetStatus("Waiting for CD-Tag to close...");
            WaitForExit(processId);

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string temp = CreateWorkFolder();
            try
            {
                form.SetStatus("Downloading...");
                string zip = Path.Combine(temp, "CD-Tag.zip");
                Program.Download(url, zip, form);

                form.SetStatus("Checking the download...");
                string actual;
                using (var sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(zip))
                    actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The download doesn't match the checksum GitHub lists for it; nothing was changed.");

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

        /// <summary>
        /// The folder the zip is downloaded and unpacked in. As administrator only administrators and SYSTEM can write
        /// to it, so a program running as the user can't change the files between the checksum check and the copy.
        /// </summary>
        private static string CreateWorkFolder()
        {
            string temp = Path.Combine(Path.GetTempPath(), "cdtag-update-" + Guid.NewGuid().ToString("N"));
            if (!IsElevated())
            {
                Directory.CreateDirectory(temp);
                return temp;
            }
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (WellKnownSidType sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                                                                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                                                                PropagationFlags.None, AccessControlType.Allow));
            security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            Directory.CreateDirectory(temp, security);
            return temp;
        }

        private static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>Deletes cdtag.exe.old, left by an update that replaced the running starter.</summary>
        public static void RemoveOldStarter(string programDirectory)
        {
            try
            {
                string old = Path.Combine(programDirectory, "cdtag.exe" + OldStarterSuffix);
                if (File.Exists(old))
                    File.Delete(old);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
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
                    // Running in place (as administrator): Windows lets a running exe be renamed, not overwritten.
                    if (IsRunningExe(destination))
                    {
                        string old = destination + OldStarterSuffix;
                        if (File.Exists(old))
                            File.Delete(old);
                        File.Move(destination, old);
                    }
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

        private static bool IsRunningExe(string path)
        {
            return File.Exists(path) && string.Equals(Path.GetFullPath(path), Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase);
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

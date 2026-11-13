using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CDTag.Common.ApplicationServices;
using CDTag.Model.Lookup;

namespace CDTag.Model.Update
{
    /// <summary>A newer CD-Tag release on GitHub.</summary>
    public class UpdateInfo
    {
        public Version Version { get; set; }
        /// <summary>The release zip (CD-Tag-x.y.z-win-x64.zip).</summary>
        public string DownloadUrl { get; set; }
        /// <summary>The release page on GitHub.</summary>
        public string PageUrl { get; set; }
        /// <summary>The release notes (Markdown), may be empty.</summary>
        public string Notes { get; set; }
        /// <summary>SHA-256 of the zip as GitHub lists it (lowercase hex); the updater refuses a download that doesn't match.</summary>
        public string Sha256 { get; set; }
    }

    /// <summary>
    /// Looks for a newer release of CD-Tag on GitHub and installs it: the starter cdtag.exe (next to bin\) is copied to a
    /// temporary folder and run with --update; it waits until CD-Tag has closed, downloads the zip, unpacks it over the
    /// program folder (options.json and profiles are kept) and starts CD-Tag again.
    /// </summary>
    public static class UpdateChecker
    {
        public const string Repository = "Taoism/CD-Tag";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";
        private const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
        private const string TempPrefix = "cdtag-update-";
        /// <summary>The only place an update is downloaded from (the updater checks it again).</summary>
        public const string DownloadPrefix = "https://github.com/" + Repository + "/releases/download/";

        /// <summary>The running version (3 parts, e.g. 3.2.6).</summary>
        public static Version CurrentVersion
        {
            get
            {
                Version v = (Assembly.GetEntryAssembly() ?? typeof(UpdateChecker).Assembly).GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        /// <summary>The latest release when it is newer than <see cref="CurrentVersion"/>, otherwise null. Throws when offline.</summary>
        public static async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd(MusicBrainzClient.UserAgent);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                string json = await http.GetStringAsync(LatestApi, cancellationToken).ConfigureAwait(false);
                UpdateInfo latest = Parse(json);
                return latest != null && latest.Version > CurrentVersion ? latest : null;
            }
        }

        /// <summary>Reads GitHub's "latest release" answer; null when it has no version tag or no Windows zip.</summary>
        public static UpdateInfo Parse(string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement root = document.RootElement;
                Version version = ParseVersion(GetString(root, "tag_name"));
                if (version == null)
                    return null;

                string download = null, sha256 = null;
                JsonElement assets;
                if (root.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    JsonElement zip = assets.EnumerateArray()
                                            .FirstOrDefault(a => (GetString(a, "name") ?? "").EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)
                                                                 && !string.IsNullOrEmpty(GetString(a, "browser_download_url")));
                    if (zip.ValueKind == JsonValueKind.Object)
                    {
                        download = GetString(zip, "browser_download_url");
                        sha256 = ParseDigest(GetString(zip, "digest"));
                    }
                }
                // Only a zip from this repository's releases, with GitHub's SHA-256, is installed.
                if (!IsReleaseDownload(download) || sha256 == null)
                    return null;

                return new UpdateInfo
                {
                    Version = version,
                    DownloadUrl = download,
                    PageUrl = GetString(root, "html_url") ?? ReleasesPage,
                    Notes = GetString(root, "body") ?? "",
                    Sha256 = sha256
                };
            }
        }

        /// <summary>True for an https download from this repository's GitHub releases.</summary>
        public static bool IsReleaseDownload(string url)
        {
            Uri uri;
            return !string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps
                   && uri.AbsoluteUri.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase) && !uri.AbsolutePath.Contains("/../");
        }

        /// <summary>GitHub's asset digest "sha256:9e77..." -> "9e77..." (64 lowercase hex digits); null otherwise.</summary>
        public static string ParseDigest(string digest)
        {
            const string prefix = "sha256:";
            if (string.IsNullOrEmpty(digest) || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            string hex = digest.Substring(prefix.Length).Trim().ToLowerInvariant();
            return hex.Length == 64 && hex.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')) ? hex : null;
        }

        /// <summary>"v3.2.7" or "3.2.7" -> 3.2.7; null when it isn't a version.</summary>
        public static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;
            string text = tag.Trim().TrimStart('v', 'V');
            Version version;
            if (!Version.TryParse(text, out version))
                return null;
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        }

        private static string GetString(JsonElement element, string name)
        {
            JsonElement value;
            return element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        /// <summary>The starter cdtag.exe of a release folder (above bin\), or null for a development build.</summary>
        public static string Starter
        {
            get
            {
                string baseDirectory = AppContext.BaseDirectory.TrimEnd('\\', '/');
                if (!string.Equals(Path.GetFileName(baseDirectory), "bin", StringComparison.OrdinalIgnoreCase))
                    return null;
                string starter = Path.Combine(PathService.ProgramDirectory, "cdtag.exe");
                return File.Exists(starter) ? starter : null;
            }
        }

        /// <summary>True when this copy can update itself (a release folder with the starter).</summary>
        public static bool CanInstall
        {
            get { return Starter != null; }
        }

        /// <summary>
        /// Starts the update. The caller closes CD-Tag right after: the updater waits for this process to end. Asks Windows
        /// for permission when the program folder can't be written (e.g. under Program Files).
        /// </summary>
        public static void StartInstall(UpdateInfo update)
        {
            ArgumentNullException.ThrowIfNull(update);
            string starter = Starter;
            if (starter == null)
                throw new InvalidOperationException("This copy of CD-Tag can't update itself; download the new version from GitHub.");

            if (!IsReleaseDownload(update.DownloadUrl) || update.Sha256 == null)
                throw new InvalidOperationException("The update isn't a CD-Tag release from GitHub; download it from the releases page.");

            string program = PathService.ProgramDirectory;
            string arguments = string.Join(" ", new[]
            {
                "--update", Quote(update.DownloadUrl), Quote(program),
                Environment.ProcessId.ToString(), Quote(update.Version.ToString()), update.Sha256
            });

            ProcessStartInfo info;
            if (CanWrite(program))
            {
                // Run a copy: the starter in the program folder is replaced too.
                string temp = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                string updater = Path.Combine(temp, "cdtag.exe");
                File.Copy(starter, updater);
                info = new ProcessStartInfo(updater, arguments) { UseShellExecute = true, WorkingDirectory = temp };
            }
            else
            {
                // As administrator the starter runs where it is: a folder only administrators can change (a copy in
                // %TEMP% could have a DLL planted next to it). It renames itself to cdtag.exe.old before it is replaced.
                info = new ProcessStartInfo(starter, arguments) { UseShellExecute = true, WorkingDirectory = program, Verb = "runas" };
            }
            Process.Start(info);
        }

        /// <summary>Removes the temporary folders of earlier updates (the updater can't delete its own copy).</summary>
        public static void CleanUp()
        {
            try
            {
                foreach (string directory in Directory.EnumerateDirectories(Path.GetTempPath(), TempPrefix + "*"))
                {
                    try
                    {
                        if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddMinutes(-10))
                            Directory.Delete(directory, true);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static bool CanWrite(string directory)
        {
            string probe = Path.Combine(directory, ".cdtag-write-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string Quote(string arg)
        {
            // Paths never end in a backslash here (trimmed), so no escaping is needed besides the quotes.
            return "\"" + (arg ?? "").TrimEnd('\\') + "\"";
        }
    }
}

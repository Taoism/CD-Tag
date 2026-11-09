using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CDTag.Common;
using CDTag.Model.Update;

namespace CDTag.ViewModel.Update
{
    /// <summary>Looks for a newer CD-Tag on GitHub and asks the user whether to install it.</summary>
    public static class UpdatePrompt
    {
        private static bool _busy;

        /// <summary>
        /// Checks for an update. <paramref name="quiet"/> (the check at startup): says nothing when CD-Tag is up to date or
        /// GitHub can't be reached. Must be called on the UI thread.
        /// </summary>
        public static async Task CheckAsync(Window owner, bool quiet)
        {
            if (_busy)
                return;
            _busy = true;
            try
            {
                UpdateInfo update;
                try
                {
                    update = await UpdateChecker.CheckAsync();
                }
                catch (Exception ex)
                {
                    if (!quiet)
                        Show(owner, "Could not check for updates: " + ex.Message, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (update == null)
                {
                    if (!quiet)
                        Show(owner, string.Format("CD-Tag {0} is the latest version.", UpdateChecker.CurrentVersion), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string intro = string.Format("CD-Tag {0} is available (you have {1}).", update.Version, UpdateChecker.CurrentVersion);
                string notes = Summary(update.Notes);
                if (notes.Length != 0)
                    intro += Environment.NewLine + Environment.NewLine + notes;

                if (!UpdateChecker.CanInstall)
                {
                    // A development build (no starter cdtag.exe above bin\): only the download page.
                    if (Show(owner, intro + Environment.NewLine + Environment.NewLine + "This copy can't update itself. Open the download page?",
                             MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                        OpenPage(update.PageUrl);
                    return;
                }

                if (Show(owner, intro + Environment.NewLine + Environment.NewLine +
                                "Would you like to install it now? CD-Tag closes, updates and starts again. Your options and profiles are kept.",
                         MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                try
                {
                    UpdateChecker.StartInstall(update);
                }
                catch (Exception ex)
                {
                    // Includes declining Windows' permission prompt.
                    Show(owner, "The update could not be started: " + ex.Message, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Close normally so window positions and settings are saved; the updater waits for this process to end.
                Application.Current.Shutdown();
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// The "New in x.y.z" paragraph of the release notes (up to the next blank line), without Markdown markers;
        /// without one, the first paragraph that isn't the download line.
        /// </summary>
        public static string Summary(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
                return "";
            string[] paragraphs = notes.Replace("\r", "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(p => p.Trim()).Where(p => p.Length != 0).ToArray();
            string paragraph = paragraphs.FirstOrDefault(p => p.TrimStart('#', ' ').StartsWith("New in", StringComparison.OrdinalIgnoreCase))
                               ?? paragraphs.FirstOrDefault(p => p.IndexOf("Download", StringComparison.OrdinalIgnoreCase) < 0 && !p.StartsWith("CD-Tag for", StringComparison.Ordinal));
            if (paragraph == null)
                return "";
            string[] lines = paragraph.Split('\n')
                                      .Select(l => l.Trim().TrimStart('#').Trim().Replace("**", "").Replace("`", "").Replace("*", ""))
                                      .Where(l => l.Length != 0)
                                      .ToArray();
            const int max = 10;
            string text = string.Join(Environment.NewLine, lines.Take(max));
            return lines.Length > max ? text + Environment.NewLine + "..." : text;
        }

        private static MessageBoxResult Show(Window owner, string text, MessageBoxButton button, MessageBoxImage icon)
        {
            return ThemedMessageBox.Show(owner, text, "CD-Tag update", button, icon);
        }

        private static void OpenPage(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }
    }
}

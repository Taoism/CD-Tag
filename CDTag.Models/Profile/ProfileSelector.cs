using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDTag.Model.Tag;

namespace CDTag.Model.Profile
{
    /// <summary>
    /// Picks the profile(s) that fit an album's audio file types, and remembers the profile in use.
    /// </summary>
    public static class ProfileSelector
    {
        /// <summary>Choices for <see cref="UserProfile.AudioFileTypes"/> shown in the profile list.</summary>
        public static readonly string[] FileTypeChoices = { "", "mp3", "flac", "mp3,flac" };

        /// <summary>The profile used by Tag Album (set when an album is opened).</summary>
        public static UserProfile ActiveProfile { get; set; }

        /// <summary>Parses <see cref="UserProfile.AudioFileTypes"/>; empty set = any type.</summary>
        public static HashSet<string> GetFileTypes(UserProfile profile)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profile == null || string.IsNullOrWhiteSpace(profile.AudioFileTypes))
                return result;

            foreach (string part in profile.AudioFileTypes.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                result.Add(part.Trim().TrimStart('.').ToLowerInvariant());
            return result;
        }

        /// <summary>Audio file types (extensions without the dot) present in <paramref name="directory"/>.</summary>
        public static HashSet<string> GetAlbumFileTypes(string directory)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return result;

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string extension = Path.GetExtension(file);
                if (Album.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    result.Add(extension.TrimStart('.').ToLowerInvariant());
            }
            return result;
        }

        /// <summary>
        /// Profiles suited to an album containing <paramref name="albumTypes"/>:
        /// profiles made for all of those types; otherwise profiles made for at least one of them;
        /// otherwise the "any type" profiles; otherwise every profile.
        /// </summary>
        public static IList<UserProfile> FindCandidates(IEnumerable<UserProfile> profiles, ICollection<string> albumTypes)
        {
            List<UserProfile> all = profiles.Where(p => p != null).ToList();
            if (albumTypes == null || albumTypes.Count == 0)
                return all;

            var typed = all.Select(p => new { Profile = p, Types = GetFileTypes(p) }).ToList();

            List<UserProfile> coverAll = typed.Where(t => t.Types.Count > 0 && albumTypes.All(t.Types.Contains)).Select(t => t.Profile).ToList();
            if (coverAll.Count > 0)
                return coverAll;

            List<UserProfile> coverSome = typed.Where(t => t.Types.Count > 0 && albumTypes.Any(t.Types.Contains)).Select(t => t.Profile).ToList();
            if (coverSome.Count > 0)
                return coverSome;

            List<UserProfile> generic = typed.Where(t => t.Types.Count == 0).Select(t => t.Profile).ToList();
            return generic.Count > 0 ? generic : all;
        }

        /// <summary>Loads every profile in <paramref name="profileDirectory"/> (bad files are skipped).</summary>
        public static List<UserProfile> LoadAll(string profileDirectory)
        {
            var result = new List<UserProfile>();
            if (!Directory.Exists(profileDirectory))
                return result;

            foreach (string file in Directory.GetFiles(profileDirectory, "*.cfg").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    result.Add(UserProfile.Load(file));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            }
            return result;
        }
    }
}

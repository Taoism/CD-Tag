using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Profile;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Profile
{
    [TestFixture]
    public class ProfileSelectorTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            IoC.ClearAllRegistrations();
            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());
        }

        private static UserProfile P(string name, string types)
        {
            return new UserProfile { ProfileName = name, AudioFileTypes = types };
        }

        private static string[] Names(IEnumerable<UserProfile> profiles)
        {
            return profiles.Select(p => p.ProfileName).ToArray();
        }

        private static HashSet<string> Types(params string[] types)
        {
            return new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
        }

        [Test]
        public void SingleMatchingProfileIsPicked()
        {
            var profiles = new[] { P("Default", null), P("MP3 group", "mp3"), P("FLAC group", "flac") };

            Assert.That(Names(ProfileSelector.FindCandidates(profiles, Types("flac"))), Is.EqualTo(new[] { "FLAC group" }));
            Assert.That(Names(ProfileSelector.FindCandidates(profiles, Types("mp3"))), Is.EqualTo(new[] { "MP3 group" }));
        }

        [Test]
        public void SeveralMatchingProfilesAreAllReturned()
        {
            var profiles = new[] { P("Default", null), P("A", "mp3"), P("B", "MP3"), P("C", "flac") };

            Assert.That(Names(ProfileSelector.FindCandidates(profiles, Types("mp3"))), Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public void AnyTypeProfilesAreTheFallback()
        {
            var profiles = new[] { P("Default", null), P("Other", ""), P("C", "flac") };

            Assert.That(Names(ProfileSelector.FindCandidates(profiles, Types("mp3"))), Is.EqualTo(new[] { "Default", "Other" }));
        }

        [Test]
        public void MixedAlbumPrefersProfileCoveringAllTypes()
        {
            var profiles = new[] { P("A", "mp3"), P("Both", "mp3,flac"), P("C", "flac") };

            Assert.That(Names(ProfileSelector.FindCandidates(profiles, Types("mp3", "flac"))), Is.EqualTo(new[] { "Both" }));
            Assert.That(Names(ProfileSelector.FindCandidates(new[] { P("A", "mp3"), P("C", "flac") }, Types("mp3", "flac"))), Is.EqualTo(new[] { "A", "C" }));
        }

        [Test]
        public void DetectsAudioTypesInFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cdtag-types-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "01.FLAC"), "");
                File.WriteAllText(Path.Combine(dir, "cover.jpg"), "");
                Assert.That(ProfileSelector.GetAlbumFileTypes(dir), Is.EquivalentTo(new[] { "flac" }));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void ImportedLegacyProfilesGetFileTypeFromNameOrFormat()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cdtag-legacytype-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string mp3 = Path.Combine(dir, "GRP.cfg");
                File.WriteAllText(mp3, "[CONFIG]\r\n[NAME0]\r\nDIR=<Artist>-<Album>-<Year>-GRP\r\n");
                string flacByFormat = Path.Combine(dir, "Lossless.cfg");
                File.WriteAllText(flacByFormat, "[CONFIG]\r\n[NAME0]\r\nDIR=<Artist>-<Album>-FLAC-<Year>-GRP\r\n");

                string template;
                Assert.That(LegacyProfile.Convert(mp3, "GRP", out template).AudioFileTypes, Is.EqualTo("mp3"));
                Assert.That(LegacyProfile.Convert(mp3, "GRPFLAC", out template).AudioFileTypes, Is.EqualTo("flac"));
                Assert.That(LegacyProfile.Convert(flacByFormat, "Lossless", out template).AudioFileTypes, Is.EqualTo("flac"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}

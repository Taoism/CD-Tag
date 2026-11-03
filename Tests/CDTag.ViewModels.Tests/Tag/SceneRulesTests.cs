using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Tag;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    [TestFixture]
    public class SceneRulesTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            IoC.ClearAllRegistrations();
            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());
        }

        private static Album FlacAlbum(string genre = "Grunge")
        {
            var tracks = new List<AlbumTrack> { AlbumTrack.FromValues(@"C:\in\01.flac", "1", "1", "Nirvana", "Breed") };
            return new Album(@"C:\in", tracks) { Artist = "Nirvana", AlbumTitle = "Nevermind", Genre = genre };
        }

        private static SceneRelease Release(string name, Album album, params string[] files)
        {
            return new SceneRelease { Name = name, Album = album, Files = files.ToList(), Source = "CD", Discs = 1, Year = "1991" };
        }

        [Test]
        public void AGoodFlacReleaseHasNoFindings()
        {
            var release = Release("Nirvana-Nevermind-CD-FLAC-1991-GRP", FlacAlbum(),
                                  "01-nirvana-breed.flac", "00-nirvana-nevermind-cd-flac-1991.sfv", "00-nirvana-nevermind-cd-flac-1991.nfo",
                                  "00-nirvana-nevermind-cd-flac-1991-proof.jpg", "00-nirvana-nevermind-cd-flac-1991.log");

            Assert.That(SceneRules.Check(SceneRuleSet.Flac2016, release), Is.Empty);
        }

        [Test]
        public void FlacFindingsNameTheRule()
        {
            var release = Release("Nirvana - Nevermind (1991)", FlacAlbum("Alternative Rock"), "01 Breed.flac", "Cover.PNG", "info.txt");
            List<string> issues = SceneRules.Check(SceneRuleSet.Flac2016, release);

            Assert.That(issues, Has.Some.StartsWith("FLAC 1.5: the release name"));
            Assert.That(issues, Has.Some.StartsWith("FLAC 4.1"));
            Assert.That(issues, Has.Some.StartsWith("FLAC 4.4"));
            Assert.That(issues, Has.Some.StartsWith("FLAC 5.2: Cover.PNG"));
            Assert.That(issues, Has.Some.StartsWith("FLAC 1.1.3: info.txt"));
            Assert.That(issues, Has.Some.StartsWith("FLAC 5.1: 01 Breed.flac"));
            Assert.That(issues, Has.Some.StartsWith("FLAC 1.1: the release needs an SFV"));
            Assert.That(issues, Has.Some.StartsWith("FLAC 1.1: the release needs an NFO"));
            Assert.That(issues, Has.Some.StartsWith("FLAC 1.1 / 9.1"));
            Assert.That(issues, Has.Some.Contains("genre \"Alternative Rock\" is not in the allowed list (use AlternRock)"));
        }

        [Test]
        public void Mp3NeedsAMultiDiscTagAndVaName()
        {
            var tracks = new List<AlbumTrack>
            {
                AlbumTrack.FromValues(@"C:\in\101.mp3", "1", "1", "A", "x"),
                AlbumTrack.FromValues(@"C:\in\201.mp3", "2", "1", "B", "y")
            };
            var album = new Album(@"C:\in", tracks) { IsVariousArtists = true, AlbumTitle = "Mix", Genre = "Dance" };
            var release = new SceneRelease
            {
                Name = "Mix-2021-GRP", Album = album, Discs = 2, Year = "2021", Source = "CD",
                Files = new List<string> { "101-a-x.mp3", "201-b-y.mp3", "00-mix.sfv", "00-mix.nfo", "00-mix-proof.jpg" }
            };

            List<string> issues = SceneRules.Check(SceneRuleSet.Mp3_2021, release);

            Assert.That(issues, Has.Some.StartsWith("MP3 3.6"));
            Assert.That(issues, Has.Some.StartsWith("MP3 3.7"));
            Assert.That(issues.Count, Is.EqualTo(2));
        }

        [TestCase("Alternative Rock", "AlternRock")]
        [TestCase("hip hop", "Hip-Hop")]
        [TestCase("grunge", "Grunge")]
        [TestCase("Shoegaze", null)]
        public void OnlineGenresMapToAllowedGenres(string genre, string expected)
        {
            Assert.That(SceneRules.ToAllowedGenre(genre), Is.EqualTo(expected));
        }

        [Test]
        public void FindsTheNewestRuleFileOfEachKind()
        {
            string html = "<a href=\"nfo/2011_FLAC.nfo\"> <a href=\"nfo/2016_FLAC.nfo\"> <a href=\"nfo/2019_MP3.nfo\"> <a href=\"nfo/2021_MP3.nfo\">" +
                          "<a href=\"nfo/2023_WEBFLAC.nfo\"> <a href=\"nfo/2011_FLACv1.1.nfo\">";

            var latest = SceneRulesUpdater.FindLatest(html);

            Assert.That(latest.Select(f => f.FileName), Is.EqualTo(new[] { "2016_FLAC.nfo", "2021_MP3.nfo", "2023_WEBFLAC.nfo" }));
        }

        [Test]
        public void ReadsTheGenreListFromTheFlacRules()
        {
            string nfo =
                "|  6.5 The GENRE field content MUST be the same for all the files in a release  |\r\n" +
                "|      and it MUST be one of the genres listed:                                 |\r\n" +
                "|      A Cappella, Acid, Acid Jazz, Drum & Bass,                                |\r\n" +
                "|      Trip-Hop, Vocal, Unknown.                                                |\r\n" +
                "| NOTE: Both Psychedelic and Psychadelic are correct and may not be nuked       |\r\n";

            Assert.That(SceneRulesUpdater.ParseFlacGenres(nfo), Is.EqualTo(new[] { "A Cappella", "Acid", "Acid Jazz", "Drum & Bass", "Trip-Hop", "Vocal", "Unknown" }));
        }

        [Test]
        public void ReadsTheWholeGenreListFromTheRealRulesWhenPresent()
        {
            // The rules are kept locally (rules/ is not in git); skip when they are not there.
            string path = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, @"..\..\..\..\..\rules\2016_FLAC.nfo"));
            Assume.That(File.Exists(path), "rules/2016_FLAC.nfo not present");
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            List<string> genres = SceneRulesUpdater.ParseFlacGenres(File.ReadAllText(path, System.Text.Encoding.GetEncoding(437)));

            Assert.That(genres.First(), Is.EqualTo("A Cappella"));
            Assert.That(genres.Last(), Is.EqualTo("Unknown"));
            Assert.That(genres, Is.EquivalentTo(SceneRules.FlacGenres.Where(g => g != "Psychadelic")));
        }
    }
}

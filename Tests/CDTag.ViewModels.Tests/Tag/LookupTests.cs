using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Lookup;
using CDTag.Model.Profile;
using CDTag.Model.Tag;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>
    /// MusicBrainz parsing and applying. TestData/musicbrainz-release-ween.json is the MusicBrainz web service response
    /// for release fcaf0850-8726-4faf-bb02-b6b159f23090 (MusicBrainz data is CC0).
    /// </summary>
    [TestFixture]
    public class LookupTests
    {
        private static ReleaseDetails Ween()
        {
            string json = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "musicbrainz-release-ween.json"));
            return MusicBrainzClient.ParseRelease(json);
        }

        [OneTimeSetUp]
        public void Setup()
        {
            IoC.ClearAllRegistrations();
            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());
        }

        [Test]
        public void ParsesRealRelease()
        {
            ReleaseDetails release = Ween();

            Assert.That(release.Id, Is.EqualTo("fcaf0850-8726-4faf-bb02-b6b159f23090"));
            Assert.That(release.Artist, Is.EqualTo("Ween"));
            Assert.That(release.Title, Is.EqualTo("Boognish Provides"));
            Assert.That(release.Date, Is.EqualTo("2026-10-14"));
            Assert.That(release.Label, Is.EqualTo("Rhino"));
            Assert.That(release.CatalogNumber, Is.EqualTo("R2 729341"));
            Assert.That(release.Format, Is.EqualTo("CD"));
            Assert.That(release.Discs, Is.EqualTo(14));
            Assert.That(release.Tracks.Count, Is.EqualTo(179));
            Assert.That(release.IsVariousArtists, Is.False);
            Assert.That(release.Tracks[0].Disc, Is.EqualTo(1));
            Assert.That(release.Tracks[0].Position, Is.EqualTo(1));
            Assert.That(release.Tracks[0].Title, Is.EqualTo("Koko"));
            Assert.That(release.Tracks.First(t => t.Disc == 2).Title, Is.EqualTo("Baby Bitch"));
            Assert.That(release.Tracks[0].Length, Is.GreaterThan(0));
            Assert.That(release.DiscNames[1], Is.EqualTo("McCabe's Guitar Shop, Santa Monica, CA, 10/9/94"));
            Assert.That(release.DiscNames[14], Is.EqualTo("Trocadero Theatre, Philadelphia, PA, 6/16/95 (cont.)"));
        }

        [Test]
        public void AppliesToAnUntaggedMultiDiscAlbum()
        {
            ReleaseDetails release = Ween();
            var tracks = new List<AlbumTrack>
            {
                AlbumTrack.FromValues(@"N:\in\0101 Koko.flac", "1", "1", null, null),
                AlbumTrack.FromValues(@"N:\in\0102.flac", "1", "2", null, null),
                AlbumTrack.FromValues(@"N:\in\0201.flac", "2", "1", null, null)
            };
            var album = new Album(@"N:\in", tracks);
            var info = new ReleaseInfo();
            var sources = new[] { new ReleaseListItem { Nfo = "CD", Dir = "CD" }, new ReleaseListItem { Nfo = "WEB-FLAC", Dir = "WEB-FLAC" } };

            int applied = LookupApplier.Apply(release, album, info, sources);

            Assert.That(applied, Is.EqualTo(3));
            Assert.That(album.Artist, Is.EqualTo("Ween"));
            Assert.That(album.AlbumTitle, Is.EqualTo("Boognish Provides"));
            Assert.That(info.RetailDate, Is.EqualTo("2026-10-14"));
            Assert.That(info.Label, Is.EqualTo("Rhino"));
            Assert.That(info.CatalogNumber, Is.EqualTo("R2 729341"));
            Assert.That(info.Source.Nfo, Is.EqualTo("CD"));
            Assert.That(tracks[0].Title, Is.EqualTo("Koko"));
            Assert.That(tracks[1].Title, Is.EqualTo("Don't Shit Where You Eat"), "typographic apostrophe made plain");
            Assert.That(tracks[2].Title, Is.EqualTo("Baby Bitch"));
            Assert.That(tracks[0].Artist, Is.EqualTo("Ween"));
        }

        [Test]
        public void MatchesInOrderWhenNumbersDoNotFitButCountsDo()
        {
            var release = new ReleaseDetails
            {
                Artist = "A", Title = "B", Discs = 1,
                Tracks = new List<ReleaseTrack> { new ReleaseTrack { Disc = 1, Position = 1, Title = "One" }, new ReleaseTrack { Disc = 1, Position = 2, Title = "Two" } }
            };
            var tracks = new List<AlbumTrack> { AlbumTrack.FromValues(@"C:\x\a.flac", "1", "", null, null), AlbumTrack.FromValues(@"C:\x\b.flac", "1", "", null, null) };
            var album = new Album(@"C:\x", tracks);

            Assert.That(LookupApplier.Apply(release, album, new ReleaseInfo(), null), Is.EqualTo(2));
            Assert.That(tracks.Select(t => t.Title), Is.EqualTo(new[] { "One", "Two" }));
            Assert.That(tracks.Select(t => t.TrackNumber), Is.EqualTo(new[] { "1", "2" }));
        }

        [TestCase("2026-10-14", "2026-10-14")]
        [TestCase("2026-10", "2026-10-00")]
        [TestCase("1994", "1994-00-00")]
        [TestCase("", null)]
        public void DatesBecomeRetailDates(string date, string expected)
        {
            Assert.That(LookupApplier.ToRetailDate(date), Is.EqualTo(expected));
        }

        [Test]
        public void SearchResultsMatchingTrackCountComeFirst()
        {
            string json = "{\"releases\":[" +
                "{\"id\":\"a\",\"score\":100,\"title\":\"X\",\"track-count\":10,\"artist-credit\":[{\"name\":\"Ween\"}],\"media\":[{\"format\":\"CD\"}]}," +
                "{\"id\":\"b\",\"score\":90,\"title\":\"X\",\"track-count\":179,\"artist-credit\":[{\"name\":\"Ween\"}],\"media\":[{\"format\":\"CD\"},{\"format\":\"CD\"}]," +
                "\"label-info\":[{\"catalog-number\":\"R2 1\",\"label\":{\"name\":\"Rhino\"}}]}]}";

            var results = MusicBrainzClient.ParseSearch(json, 179);

            Assert.That(results.Select(r => r.Id), Is.EqualTo(new[] { "b", "a" }));
            Assert.That(results[0].Format, Is.EqualTo("2×CD"));
            Assert.That(results[0].Label, Is.EqualTo("Rhino"));
            Assert.That(results[0].MatchesTrackCount, Is.True);
        }
    }
}

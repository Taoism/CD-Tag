using System.Collections.Generic;
using System.Linq;
using CDTag.Model.Lookup;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>Discogs API parsing, with made-up releases in the shape of api.discogs.com responses.</summary>
    [TestFixture]
    public class DiscogsTests
    {
        private const string TwoDiscRelease = @"{
  ""id"": 1234567,
  ""title"": ""Live At The Hall"",
  ""artists"": [ { ""name"": ""The Band (2)"", ""anv"": """", ""join"": """" } ],
  ""year"": 1999,
  ""released"": ""1999-05-00"",
  ""country"": ""Germany"",
  ""labels"": [ { ""name"": ""Some Label (3)"", ""catno"": ""SL 123-2"" } ],
  ""formats"": [ { ""name"": ""CD"", ""qty"": ""2"", ""descriptions"": [ ""Album"" ] } ],
  ""genres"": [ ""Electronic"" ],
  ""styles"": [ ""Techno"", ""Ambient"" ],
  ""images"": [ { ""type"": ""secondary"", ""uri"": ""https://i.discogs.com/back.jpg"" }, { ""type"": ""primary"", ""uri"": ""https://i.discogs.com/front.jpg"" } ],
  ""tracklist"": [
    { ""position"": """", ""type_"": ""heading"", ""title"": ""CD 1"" },
    { ""position"": ""1-1"", ""type_"": ""track"", ""title"": ""Intro"", ""duration"": ""1:05"" },
    { ""position"": ""1-2"", ""type_"": ""track"", ""title"": ""Second"", ""duration"": ""4:30"",
      ""artists"": [ { ""name"": ""Guest"", ""anv"": ""G."", ""join"": ""&"" }, { ""name"": ""Other*"", ""join"": """" } ] },
    { ""position"": ""2-1"", ""type_"": ""track"", ""title"": ""Third"", ""duration"": ""10:00"" },
    { ""position"": """", ""type_"": ""index"", ""title"": ""Suite"", ""sub_tracks"": [
        { ""position"": ""2-2"", ""type_"": ""track"", ""title"": ""Suite Part I"", ""duration"": ""2:00"" },
        { ""position"": ""2-3"", ""type_"": ""track"", ""title"": ""Suite Part II"", ""duration"": ""3:00"" } ] }
  ]
}";

        [Test]
        public void ParsesTwoDiscRelease()
        {
            ReleaseDetails r = DiscogsClient.ParseRelease(TwoDiscRelease);

            Assert.That(r.Id, Is.EqualTo("discogs:1234567"));
            Assert.That(r.DiscogsId, Is.EqualTo("1234567"));
            Assert.That(r.Source, Is.EqualTo("Discogs"));
            Assert.That(r.Artist, Is.EqualTo("The Band"));
            Assert.That(r.Title, Is.EqualTo("Live At The Hall"));
            Assert.That(r.Date, Is.EqualTo("1999-05"));
            Assert.That(r.Label, Is.EqualTo("Some Label"));
            Assert.That(r.CatalogNumber, Is.EqualTo("SL 123-2"));
            Assert.That(r.Format, Is.EqualTo("CD"));
            Assert.That(r.Discs, Is.EqualTo(2));
            Assert.That(r.Genre, Is.EqualTo("Techno"));
            Assert.That(r.CoverUrl, Is.EqualTo("https://i.discogs.com/front.jpg"));

            Assert.That(r.Tracks.Select(t => t.Disc + "-" + t.Position + " " + t.Title), Is.EqualTo(new[]
            {
                "1-1 Intro", "1-2 Second", "2-1 Third", "2-2 Suite Part I", "2-3 Suite Part II"
            }));
            Assert.That(r.Tracks[0].Length, Is.EqualTo(65));
            Assert.That(r.Tracks[1].Artist, Is.EqualTo("G. & Other"));
            Assert.That(r.Tracks[2].Artist, Is.EqualTo("The Band"));
            Assert.That(r.IsVariousArtists, Is.True, "a guest artist on one track");
        }

        [Test]
        public void TrackNumbersStartingAgainAreTheNextDisc()
        {
            string json = @"{ ""id"": 5, ""title"": ""T"", ""artists"": [ { ""name"": ""Various"" } ],
              ""formats"": [ { ""name"": ""CD"", ""qty"": ""2"" } ],
              ""tracklist"": [
                { ""position"": ""1"", ""title"": ""A"", ""artists"": [ { ""name"": ""X"" } ] },
                { ""position"": ""2"", ""title"": ""B"", ""artists"": [ { ""name"": ""Y"" } ] },
                { ""position"": ""3a"", ""title"": ""C1"", ""duration"": ""1:00"" },
                { ""position"": ""3b"", ""title"": ""C2"", ""duration"": ""2:00"" },
                { ""position"": ""1"", ""title"": ""D"" },
                { ""position"": ""2"", ""title"": ""E"" } ] }";

            ReleaseDetails r = DiscogsClient.ParseRelease(json);

            Assert.That(r.Artist, Is.EqualTo("Various Artists"));
            Assert.That(r.IsVariousArtists, Is.True);
            Assert.That(r.Tracks.Select(t => t.Disc + "-" + t.Position + " " + t.Title), Is.EqualTo(new[]
            {
                "1-1 A", "1-2 B", "1-3 C1 / C2", "2-1 D", "2-2 E"
            }));
            Assert.That(r.Tracks[2].Length, Is.EqualTo(180));
        }

        [Test]
        public void VinylSidesAreNumberedInOrder()
        {
            string json = @"{ ""id"": 6, ""title"": ""T"", ""artists"": [ { ""name"": ""A"" } ], ""released"": ""1980"",
              ""formats"": [ { ""name"": ""Vinyl"", ""qty"": ""1"" } ],
              ""tracklist"": [ { ""position"": ""A1"", ""title"": ""One"" }, { ""position"": ""A2"", ""title"": ""Two"" }, { ""position"": ""B1"", ""title"": ""Three"" } ] }";

            ReleaseDetails r = DiscogsClient.ParseRelease(json);

            Assert.That(r.Tracks.Select(t => t.Disc + "-" + t.Position), Is.EqualTo(new[] { "1-1", "1-2", "1-3" }));
            Assert.That(r.Date, Is.EqualTo("1980"));
            Assert.That(r.Format, Is.EqualTo("Vinyl"));
        }

        [TestCase("https://www.discogs.com/release/1234567-The-Band-Live-At-The-Hall", "1234567")]
        [TestCase("https://www.discogs.com/de/release/42", "42")]
        [TestCase("[r987]", "987")]
        [TestCase("r987", "987")]
        [TestCase("https://www.discogs.com/master/111-Something", null)]
        [TestCase("https://musicbrainz.org/release/fcaf0850-8726-4faf-bb02-b6b159f23090", null)]
        public void ReadsReleaseLinks(string link, string id)
        {
            Assert.That(DiscogsClient.ReleaseIdFromLink(link), Is.EqualTo(id));
        }

        [Test]
        public void SearchListsCdsWithTheAlbumsDiscCountFirst()
        {
            string json = @"{ ""results"": [
              { ""id"": 1, ""type"": ""release"", ""title"": ""The Band (2) - Live At The Hall"", ""year"": ""1999"", ""country"": ""UK"",
                ""format"": [ ""Vinyl"", ""LP"" ], ""format_quantity"": 2, ""label"": [ ""Some Label"" ], ""catno"": ""LP 1"" },
              { ""id"": 2, ""type"": ""release"", ""title"": ""The Band - Live At The Hall"", ""format"": [ ""CD"", ""Album"" ], ""format_quantity"": 1 },
              { ""id"": 3, ""type"": ""release"", ""title"": ""The Band - Live At The Hall"", ""format"": [ ""CD"", ""Album"" ], ""format_quantity"": 2,
                ""label"": [ ""Some Label"" ], ""catno"": ""SL 123-2"" } ] }";

            List<ReleaseSearchResult> results = DiscogsClient.ParseSearch(json, 2);

            Assert.That(results.Select(r => r.Id), Is.EqualTo(new[] { "discogs:3", "discogs:2", "discogs:1" }));
            Assert.That(results[0].Format, Is.EqualTo("2×CD"));
            Assert.That(results[0].Artist, Is.EqualTo("The Band"));
            Assert.That(results[0].Title, Is.EqualTo("Live At The Hall"));
            Assert.That(results[0].CatalogNumber, Is.EqualTo("SL 123-2"));
            Assert.That(results[2].Artist, Is.EqualTo("The Band"));
        }

        [Test]
        public void FillsWhatTheCueToolsDbEntryLacks()
        {
            var ctdb = new ReleaseDetails
            {
                Id = "all:discogs|1234567", DiscogsId = "1234567", Source = "Discogs", Artist = "The Band", Title = "Live At The Hall",
                Date = "1999", Format = "CD", Tracks = new List<ReleaseTrack> { new ReleaseTrack { Disc = 1, Position = 1, Title = "Intro", Length = 66 } }
            };

            Assert.That(DiscogsClient.FillMissing(ctdb, DiscogsClient.ParseRelease(TwoDiscRelease)), Is.True);

            Assert.That(ctdb.Label, Is.EqualTo("Some Label"));
            Assert.That(ctdb.CatalogNumber, Is.EqualTo("SL 123-2"));
            Assert.That(ctdb.Date, Is.EqualTo("1999-05"));
            Assert.That(ctdb.CoverUrl, Is.EqualTo("https://i.discogs.com/front.jpg"));
            Assert.That(ctdb.Tracks.Count, Is.EqualTo(1), "the TOC track list stays");
            Assert.That(ctdb.Tracks[0].Length, Is.EqualTo(66));
        }

        [Test]
        public void CueToolsDbDiscogsEntriesOfEachDiscAreJoinedByDiscogsId()
        {
            var tocs = new List<DiscToc> { new DiscToc { Disc = 1 }, new DiscToc { Disc = 2 } };
            ReleaseDetails Disc(int disc, string title) => new ReleaseDetails
            {
                Id = "Discogs:1234567:0", DiscogsId = "1234567", Source = "Discogs", Artist = "The Band", Title = title, OnlyDisc = disc, Discs = 2,
                Tracks = new List<ReleaseTrack> { new ReleaseTrack { Disc = disc, Position = 1, Title = "T" } }
            };
            var perDisc = new List<List<ReleaseDetails>> { new List<ReleaseDetails> { Disc(1, "Live At The Hall") }, new List<ReleaseDetails> { Disc(2, "Live At The Hall (Bonus Disc)") } };

            List<ReleaseDetails> results = MultiDiscLookup.CombineCtdb(tocs, perDisc);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].DiscogsId, Is.EqualTo("1234567"));
            Assert.That(MultiDiscLookup.DiscsFound(results[0]), Is.EqualTo(2));
        }
    }
}

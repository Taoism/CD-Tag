using System.Collections.Generic;
using System.Linq;
using CDTag.Model.Lookup;
using CDTag.Model.Profile;
using CDTag.Model.Tag;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>Disc-ID lookups of every disc (CD1, CD2, CD3) joined into whole-album results.</summary>
    [TestFixture]
    public class MultiDiscLookupTests
    {
        private static DiscToc Toc(int disc, int tracks)
        {
            var toc = new DiscToc { Disc = disc, Source = "EAC log" };
            for (int i = 0; i < tracks; i++)
            {
                toc.Offsets.Add(i * 15000);
                toc.IsAudio.Add(true);
            }
            toc.LeadOut = tracks * 15000;
            return toc;
        }

        private static ReleaseDetails Ctdb(int disc, int tracks, string source, string artist, string title, string mbid = null, string id = "1")
        {
            return new ReleaseDetails
            {
                Id = source + ":" + id + ":0",
                Source = source,
                Artist = artist,
                Title = title,
                Format = "CD",
                Discs = 1,
                MusicBrainzId = mbid,
                OnlyDisc = disc,
                Tracks = Enumerable.Range(1, tracks).Select(p => new ReleaseTrack { Disc = disc, Position = p, Title = "D" + disc + "T" + p, Artist = artist }).ToList()
            };
        }

        [Test]
        public void StripsDiscNumbersFromTitles()
        {
            Assert.That(MultiDiscLookup.StripDiscNumber("Live Album (Disc 2)"), Is.EqualTo("Live Album"));
            Assert.That(MultiDiscLookup.StripDiscNumber("Live Album CD1"), Is.EqualTo("Live Album"));
            Assert.That(MultiDiscLookup.StripDiscNumber("Live Album - CD 2/3"), Is.EqualTo("Live Album"));
            Assert.That(MultiDiscLookup.StripDiscNumber("Live Album [disc 03]"), Is.EqualTo("Live Album"));
            Assert.That(MultiDiscLookup.StripDiscNumber("Disc 1"), Is.EqualTo("Disc 1"));
            Assert.That(MultiDiscLookup.StripDiscNumber("1999"), Is.EqualTo("1999"));
        }

        [Test]
        public void CtdbEntriesOfEveryDiscAreJoinedIntoOneRelease()
        {
            var tocs = new List<DiscToc> { Toc(1, 3), Toc(2, 2), Toc(3, 4) };
            var perDisc = new List<List<ReleaseDetails>>
            {
                new List<ReleaseDetails> { Ctdb(1, 3, "freedb", "Artist", "Album (CD1)"), Ctdb(1, 3, "MusicBrainz", "Artist", "Album", "mb-1") },
                new List<ReleaseDetails> { Ctdb(2, 2, "MusicBrainz", "Artist", "Album", "mb-1"), Ctdb(2, 2, "freedb", "Artist", "Album (CD2)") },
                new List<ReleaseDetails> { Ctdb(3, 4, "MusicBrainz", "Artist", "Album", "mb-1"), Ctdb(3, 4, "Discogs", "Other", "Something") }
            };

            List<ReleaseDetails> results = MultiDiscLookup.CombineCtdb(tocs, perDisc);

            ReleaseDetails mb = results[0];
            Assert.That(mb.MusicBrainzId, Is.EqualTo("mb-1"));
            Assert.That(mb.OnlyDisc, Is.EqualTo(0));
            Assert.That(mb.Discs, Is.EqualTo(3));
            Assert.That(mb.Tracks.Count, Is.EqualTo(9));
            Assert.That(mb.Tracks.Select(t => t.Disc).Distinct(), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(MultiDiscLookup.DiscsFound(mb), Is.EqualTo(3));

            ReleaseDetails freedb = results[1];
            Assert.That(freedb.Source, Is.EqualTo("freedb"));
            Assert.That(freedb.Title, Is.EqualTo("Album"));
            Assert.That(freedb.OnlyDisc, Is.EqualTo(0));
            Assert.That(MultiDiscLookup.DiscsFound(freedb), Is.EqualTo(2));

            ReleaseDetails other = results[2];
            Assert.That(other.OnlyDisc, Is.EqualTo(3));
            Assert.That(results.Select(r => r.Id).Distinct().Count(), Is.EqualTo(results.Count), "ids are unique");
        }

        [Test]
        public void JoinedCtdbReleaseTagsEveryDisc()
        {
            var tocs = new List<DiscToc> { Toc(1, 2), Toc(2, 2) };
            var perDisc = new List<List<ReleaseDetails>>
            {
                new List<ReleaseDetails> { Ctdb(1, 2, "MusicBrainz", "Artist", "Album", "mb-1") },
                new List<ReleaseDetails> { Ctdb(2, 2, "MusicBrainz", "Artist", "Album", "mb-1") }
            };
            ReleaseDetails joined = MultiDiscLookup.CombineCtdb(tocs, perDisc)[0];

            var tracks = new List<AlbumTrack>();
            for (int disc = 1; disc <= 2; disc++)
            {
                for (int track = 1; track <= 2; track++)
                    tracks.Add(AlbumTrack.FromValues(@"C:\Album\CD" + disc + @"\0" + track + ".flac", disc.ToString(), track.ToString(), null, null));
            }
            var album = new Album(@"C:\Album", tracks);

            Assert.That(LookupApplier.Apply(joined, album, new ReleaseInfo(), null), Is.EqualTo(4));
            Assert.That(album.Tracks.Select(t => t.Title), Is.EqualTo(new[] { "D1T1", "D1T2", "D2T1", "D2T2" }));
        }

        [Test]
        public void MusicBrainzReleasesFoundByEveryDiscComeFirst()
        {
            var a = new ReleaseDetails { Id = "a", Tracks = new List<ReleaseTrack>() };
            var b = new ReleaseDetails { Id = "b", Tracks = new List<ReleaseTrack>() };
            var c = new ReleaseDetails { Id = "c", Tracks = new List<ReleaseTrack>() };
            var perDisc = new List<List<ReleaseDetails>>
            {
                new List<ReleaseDetails> { a, b },
                new List<ReleaseDetails> { c, b },
                new List<ReleaseDetails> { b }
            };

            int all;
            List<ReleaseDetails> results = MultiDiscLookup.CombineMusicBrainz(perDisc, out all);

            Assert.That(results.Select(r => r.Id), Is.EqualTo(new[] { "b", "a", "c" }));
            Assert.That(all, Is.EqualTo(1));
        }
    }
}

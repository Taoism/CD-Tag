using System.IO;
using System.Linq;
using CDTag.Model.Lookup;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>
    /// Disc TOCs from rip logs and the CUETools DB / MusicBrainz disc lookups. TestData/ctdb-nevermind.xml is a trimmed
    /// db.cuetools.net lookup2.php response for Nirvana - Nevermind (two entries each from MusicBrainz, Discogs, freedb).
    /// </summary>
    [TestFixture]
    public class DiscTocTests
    {
        private const string EacLog =
            "Exact Audio Copy V1.6 from 23. October 2020\r\n\r\n" +
            "TOC of the extracted CD\r\n\r\n" +
            "     Track |   Start  |  Length  | Start sector | End sector \r\n" +
            "    ---------------------------------------------------------\r\n" +
            "        1  |  0:00.00 |  5:01.42 |         0    |    22616   \r\n" +
            "        2  |  5:01.42 |  4:14.45 |     22617    |    41736   \r\n" +
            "        3  |  9:16.12 |  3:39.05 |     41737    |    58166   \r\n" +
            "        4  | 12:55.42 |  3:03.60 |     58167    |    71951   \r\n" +
            "        5  | 15:59.27 |  4:16.73 |     71952    |    91224   \r\n" +
            "        6  | 20:16.25 |  2:57.02 |     91225    |   104501   \r\n" +
            "        7  | 23:13.27 |  2:22.53 |    104502    |   115229   \r\n" +
            "        8  | 25:36.05 |  3:43.60 |    115230    |   132014   \r\n" +
            "        9  | 29:20.15 |  2:36.67 |    132015    |   143781   \r\n" +
            "       10  | 31:57.07 |  3:32.38 |    143782    |   159719   \r\n" +
            "       11  | 35:29.45 |  3:16.27 |    159720    |   174446   \r\n" +
            "       12  | 38:45.72 | 20:35.53 |    174447    |   267074   \r\n" +
            "\r\n\r\nRange status and errors\r\n";

        private static DiscToc Nevermind()
        {
            DiscToc toc = DiscToc.FromLog(EacLog, "EAC log");
            toc.Disc = 1;
            return toc;
        }

        [Test]
        public void ReadsTheEacLogTable()
        {
            DiscToc toc = Nevermind();

            Assert.That(toc.AudioTrackCount, Is.EqualTo(12));
            Assert.That(toc.LeadOut, Is.EqualTo(267075));
            Assert.That(toc.CtdbToc, Is.EqualTo("0:22617:41737:58167:71952:91225:104502:115230:132015:143782:159720:174447:267075"));
            Assert.That(toc.MusicBrainzToc, Is.EqualTo("1+12+267225+150+22767+41887+58317+72102+91375+104652+115380+132165+143932+159870+174597"));
        }

        [Test]
        public void ComputesTheMusicBrainzDiscId()
        {
            // The disc ID MusicBrainz lists for this TOC.
            Assert.That(Nevermind().MusicBrainzDiscId, Is.EqualTo("2y0jFMwYQP7gb9iSUKagmoT6DIw-"));
        }

        [Test]
        public void MarksTheDataTrackOfAnEnhancedCd()
        {
            string log =
                "        1  |  0:00.00 |  3:00.00 |         0    |    13499   \r\n" +
                "        2  |  3:00.00 |  3:00.00 |     13500    |    26999   \r\n" +
                "        3  |  8:32.00 |  1:00.00 |     38400    |    42899   \r\n";
            DiscToc toc = DiscToc.FromLog(log);

            Assert.That(toc.AudioTrackCount, Is.EqualTo(2));
            Assert.That(toc.CtdbToc, Is.EqualTo("0:13500:-38400:42900"));
            Assert.That(toc.MusicBrainzToc, Is.EqualTo("1+2+27150+150+13650"));
            Assert.That(toc.TrackSeconds(1), Is.EqualTo(180));
        }

        [Test]
        public void IgnoresASecondTable()
        {
            DiscToc toc = DiscToc.FromLog(EacLog + "\r\n" + EacLog);
            Assert.That(toc.AudioTrackCount, Is.EqualTo(12));
        }

        [Test]
        public void ParsesCueToolsDbMetadata()
        {
            string xml = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "ctdb-nevermind.xml"));
            var releases = CueToolsDbClient.Parse(xml, Nevermind());

            Assert.That(releases.Select(r => r.Source).Distinct(), Is.EqualTo(new[] { "MusicBrainz", "Discogs", "freedb" }));

            ReleaseDetails first = releases[0];
            Assert.That(first.Artist, Is.EqualTo("Nirvana"));
            Assert.That(first.Title, Is.EqualTo("Nevermind"));
            Assert.That(first.Date, Is.EqualTo("1991-09-24"));
            Assert.That(first.Label, Is.EqualTo("Geffen Records"));
            Assert.That(first.CatalogNumber, Is.EqualTo("GED24425"));
            Assert.That(first.MusicBrainzId, Is.EqualTo("8e061dc4-790e-4587-ba53-011e7852f88d"));
            Assert.That(first.CoverUrl, Does.EndWith("-500.jpg"));
            Assert.That(first.OnlyDisc, Is.EqualTo(1));
            Assert.That(first.Tracks.Count, Is.EqualTo(12));
            Assert.That(first.Tracks[0].Title, Is.EqualTo("Smells Like Teen Spirit"));
            Assert.That(first.Tracks[0].Length, Is.EqualTo(302));

            ReleaseDetails freedb = releases.First(r => r.Source == "freedb");
            Assert.That(freedb.Genre, Is.EqualTo("Alternative"));
            Assert.That(freedb.MusicBrainzId, Is.Null);
        }

        [Test]
        public void Mp3TracksGetTheirCdLengthFromTheLog()
        {
            var tracks = Enumerable.Range(1, 12).Select(n => CDTag.Model.Tag.AlbumTrack.FromValues(@"C:\in\" + n.ToString("00") + ".mp3", "1", n.ToString(), "Nirvana", "x", 192, 300)).ToList();
            var album = new CDTag.Model.Tag.Album(@"C:\in", tracks);

            Assert.That(DiscToc.ApplyLogLengths(album, new[] { Nevermind() }), Is.EqualTo(12));
            Assert.That(tracks[0].TotalSeconds, Is.EqualTo(22617m / 75));
            Assert.That(tracks[11].TotalSeconds, Is.EqualTo((267075m - 174447m) / 75));
            Assert.That(tracks[0].IsLengthFromLog, Is.True);
        }

        [Test]
        public void LogLengthsNeedTheSameTrackCount()
        {
            var tracks = Enumerable.Range(1, 11).Select(n => CDTag.Model.Tag.AlbumTrack.FromValues(@"C:\in\" + n + ".mp3", "1", n.ToString(), "A", "x", 192, 300)).ToList();
            var album = new CDTag.Model.Tag.Album(@"C:\in", tracks);

            Assert.That(DiscToc.ApplyLogLengths(album, new[] { Nevermind() }), Is.EqualTo(0));
            Assert.That(tracks[0].TotalSeconds, Is.EqualTo(300m));
        }

        [Test]
        public void NotFoundIsNoMatch()
        {
            Assert.That(CueToolsDbClient.Parse("Not Found", Nevermind()), Is.Empty);
        }
    }
}

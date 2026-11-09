using System;
using System.IO;
using System.Linq;
using CDTag.Model.Tag;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>The same rip in FLAC and MP3 (Tag Album: tag the FLAC and MP3 albums together).</summary>
    [TestFixture]
    public class TwinAlbumTests
    {
        private static string TestData(string name)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", name);
        }

        /// <summary>Album of (disc, track, seconds) tracks in <paramref name="extension"/> files.</summary>
        private static Album Make(string folder, string extension, params decimal[][] tracks)
        {
            var list = tracks.Select(t => AlbumTrack.FromValues(
                Path.Combine(folder, string.Format("{0}{1:00} Track{2}", t[0], t[1], extension)),
                t[0].ToString(), t[1].ToString(), null, null, 0, t[2])).ToList();
            return new Album(folder, list);
        }

        private static readonly decimal[][] TwoDiscs = { new[] { 1m, 1m, 200.4m }, new[] { 1m, 2m, 185m }, new[] { 2m, 1m, 300m } };

        [Test]
        public void SameRipInTheOtherFormatMatches()
        {
            Album flac = Make(@"C:\x\Ween-Album-2CD-FLAC", ".flac", TwoDiscs);
            Album mp3 = Make(@"C:\x\Ween-Album-2CD", ".mp3", new[] { 1m, 1m, 200.43m }, new[] { 1m, 2m, 185.05m }, new[] { 2m, 1m, 299.98m });

            Assert.That(TwinAlbum.FormatOf(flac), Is.EqualTo("flac"));
            Assert.That(TwinAlbum.FormatOf(mp3), Is.EqualTo("mp3"));
            Assert.That(TwinAlbum.IsSameAlbum(flac, mp3), Is.True);
            Assert.That(TwinAlbum.Difference(flac, mp3), Is.LessThan(0.2));
        }

        [Test]
        public void OtherAlbumsDoNotMatch()
        {
            Album flac = Make(@"C:\x\A-FLAC", ".flac", TwoDiscs);

            // A track 3 seconds longer.
            Assert.That(TwinAlbum.IsSameAlbum(flac, Make(@"C:\x\A", ".mp3", new[] { 1m, 1m, 203.4m }, new[] { 1m, 2m, 185m }, new[] { 2m, 1m, 300m })), Is.False);
            // Same lengths, other discs (3 tracks on disc 1).
            Assert.That(TwinAlbum.IsSameAlbum(flac, Make(@"C:\x\A", ".mp3", new[] { 1m, 1m, 200.4m }, new[] { 1m, 2m, 185m }, new[] { 1m, 3m, 300m })), Is.False);
            // One track missing.
            Assert.That(TwinAlbum.IsSameAlbum(flac, Make(@"C:\x\A", ".mp3", new[] { 1m, 1m, 200.4m }, new[] { 1m, 2m, 185m })), Is.False);
            // Same format.
            Assert.That(TwinAlbum.IsSameAlbum(flac, Make(@"C:\x\B-FLAC", ".flac", TwoDiscs)), Is.False);
            // Lengths unknown: cannot tell.
            Assert.That(TwinAlbum.IsSameAlbum(flac, Make(@"C:\x\A", ".mp3", new[] { 1m, 1m, 0m }, new[] { 1m, 2m, 0m }, new[] { 2m, 1m, 0m })), Is.False);
        }

        [Test]
        public void CopyTagsPairsTracksInDiscTrackOrder()
        {
            Album flac = Make(@"C:\x\Ween-Album-2CD-FLAC", ".flac", TwoDiscs);
            flac.Artist = "Ween";
            flac.AlbumTitle = "Album";
            flac.Genre = "Rock";
            flac.ReleaseDate = "1994";
            flac.SetDiscName(2, "Live");
            flac.CoverBytes = new byte[] { 1, 2, 3 };
            string[] titles = { "One", "Two", "Three" };
            for (int i = 0; i < 3; i++)
            {
                flac.Tracks[i].Title = titles[i];
                flac.Tracks[i].Artist = "Ween";
            }

            // MP3 tracks listed in another order.
            Album mp3 = Make(@"C:\x\Ween-Album-2CD", ".mp3", new[] { 2m, 1m, 300m }, new[] { 1m, 1m, 200.4m }, new[] { 1m, 2m, 185m });
            TwinAlbum.CopyTags(flac, mp3);

            Assert.That(mp3.Artist, Is.EqualTo("Ween"));
            Assert.That(mp3.AlbumTitle, Is.EqualTo("Album"));
            Assert.That(mp3.Genre, Is.EqualTo("Rock"));
            Assert.That(mp3.ReleaseDate, Is.EqualTo("1994"));
            Assert.That(mp3.GetDiscName(2), Is.EqualTo("Live"));
            Assert.That(mp3.CoverBytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(TwinAlbum.Ordered(mp3).Select(t => t.Title), Is.EqualTo(titles));
            Assert.That(mp3.Tracks.First(t => t.TotalSeconds == 300m).Title, Is.EqualTo("Three"));
        }

        [Test]
        public void EncoderFromTheFiles()
        {
            var flac = new Album(@"C:\x", new[] { new AlbumTrack(TestData("tagged.flac")) });
            // The test file was made by ffmpeg: no libFLAC version to report.
            Assert.That(TwinAlbum.FlacVendor(TestData("tagged.flac")), Is.EqualTo("Lavf63.1.102"));
            Assert.That(TwinAlbum.EncoderOf(flac), Is.Null);

            // reference libFLAC vendor string: FLAC 1.5.0.
            string path = Path.Combine(Path.GetTempPath(), "cdtag-vendor-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".flac");
            try
            {
                byte[] vendor = System.Text.Encoding.UTF8.GetBytes("reference libFLAC 1.5.0 20250211");
                using (var stream = new FileStream(path, FileMode.Create))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("fLaC"));
                    writer.Write(new byte[] { 0x00, 0, 0, 34 });   // STREAMINFO
                    writer.Write(new byte[34]);
                    int length = 4 + vendor.Length + 4;
                    writer.Write(new byte[] { 0x84, (byte)(length >> 16), (byte)(length >> 8), (byte)length });   // last block: VORBIS_COMMENT
                    writer.Write(vendor.Length);
                    writer.Write(vendor);
                    writer.Write(0);
                }
                Assert.That(TwinAlbum.FlacVendor(path), Is.EqualTo("reference libFLAC 1.5.0 20250211"));
                Assert.That(TwinAlbum.EncoderOf(new Album(@"C:\x", new[] { AlbumTrack.FromValues(path, "1", "1", null, null) })), Is.EqualTo("FLAC 1.5.0"));
            }
            finally
            {
                File.Delete(path);
            }

            var v0 = new Album(@"C:\x", new[] { new AlbumTrack(TestData("lame-v0.mp3")) });
            Assert.That(TwinAlbum.EncoderOf(v0), Is.EqualTo("LAME 3.100 -V0"));
            var cbr = new Album(@"C:\x", new[] { new AlbumTrack(TestData("lame-b320.mp3")) });
            Assert.That(TwinAlbum.EncoderOf(cbr), Is.EqualTo("LAME 3.100 -b 320"));
        }

        [Test]
        public void FindLooksInTheFoldersNextToTheAlbum()
        {
            string root = Path.Combine(Path.GetTempPath(), "cdtag-twin-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                string flacFolder = Path.Combine(root, "Artist-Album-FLAC");
                string mp3Folder = Path.Combine(root, "Artist-Album");
                string otherFolder = Path.Combine(root, "Other-Album");
                foreach (string folder in new[] { flacFolder, mp3Folder, otherFolder })
                    Directory.CreateDirectory(folder);
                File.Copy(TestData("untagged.flac"), Path.Combine(flacFolder, "01 Track.flac"));
                File.Copy(TestData("lame-v0.mp3"), Path.Combine(mp3Folder, "01 Track.mp3"));
                File.Copy(TestData("lame-v0.mp3"), Path.Combine(otherFolder, "01 Track.mp3"));
                File.Copy(TestData("lame-v0.mp3"), Path.Combine(otherFolder, "02 Track.mp3"));

                var flac = new Album(flacFolder);
                Album found = TwinAlbum.Find(flac);
                Assert.That(found, Is.Not.Null);
                Assert.That(found.OldDirectoryName, Is.EqualTo(mp3Folder));
                Assert.That(TwinAlbum.Find(new Album(mp3Folder)).OldDirectoryName, Is.EqualTo(flacFolder));
                // Other selected folders come first, but must still be the same album.
                Assert.That(TwinAlbum.Find(flac, new[] { otherFolder }).OldDirectoryName, Is.EqualTo(mp3Folder));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }
    }
}

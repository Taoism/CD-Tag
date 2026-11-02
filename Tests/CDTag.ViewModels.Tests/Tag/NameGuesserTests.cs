using System.IO;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Tag;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>Filling untagged rips from folder and file names.</summary>
    [TestFixture]
    public class NameGuesserTests
    {
        [TestCase("Ween-Boognish Provides-14CD-2026-FLAC", "Ween", "Boognish Provides", "2026", 14)]
        [TestCase("Ween-Boognish Provides-14CD-2026", "Ween", "Boognish Provides", "2026", 14)]
        [TestCase("Ween-Boognish_Provides-2014-GRP", "Ween", "Boognish Provides", "2014", 0)]
        [TestCase("Alan Wilson - The Blind Owl (2013) [FLAC]", "Alan Wilson", "The Blind Owl", "2013", 0)]
        [TestCase("Test Artist - Test Album (2026)", "Test Artist", "Test Album", "2026", 0)]
        [TestCase("Artist-Album-Part Two-2CD-1999-FLAC", "Artist", "Album-Part Two", "1999", 2)]
        public void ReadsArtistAlbumDiscsYearFromFolder(string folder, string artist, string album, string year, int discs)
        {
            FolderNameInfo info = NameGuesser.FromFolderName(folder);
            Assert.That(info.Artist, Is.EqualTo(artist));
            Assert.That(info.Album, Is.EqualTo(album));
            Assert.That(info.Year, Is.EqualTo(year));
            Assert.That(info.Discs, Is.EqualTo(discs));
        }

        [TestCase("0101 Koko.flac", 14, 1, 1, null, "Koko")]
        [TestCase("1412 Voodoo Lady.flac", 14, 14, 12, null, "Voodoo Lady")]
        [TestCase("101 Koko.flac", 2, 1, 1, null, "Koko")]
        [TestCase("05 Koko.flac", 1, 0, 5, null, "Koko")]
        [TestCase("05 - Koko.mp3", 1, 0, 5, null, "Koko")]
        [TestCase("1-05 Koko.flac", 2, 1, 5, null, "Koko")]
        [TestCase("05 - Ween - Koko.mp3", 1, 0, 5, "Ween", "Koko")]
        [TestCase("05-ween-koko-grp.mp3", 1, 0, 5, null, "ween-koko-grp")]
        [TestCase("05_Some_Song.flac", 1, 0, 5, null, "Some Song")]
        public void ReadsDiscTrackTitleFromFile(string file, int discCount, int disc, int track, string artist, string title)
        {
            FileNameInfo info = NameGuesser.FromFileName(file, discCount, "Ween");
            Assert.That(info.Disc, Is.EqualTo(disc));
            Assert.That(info.Track, Is.EqualTo(track));
            Assert.That(info.Artist, Is.EqualTo(artist));
            Assert.That(info.Title, Is.EqualTo(title));
        }

        [Test]
        public void UntaggedFolderIsFilledFromNames()
        {
            IoC.ClearAllRegistrations();
            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());

            string root = Path.Combine(Path.GetTempPath(), "cdtag-names-" + System.Guid.NewGuid().ToString("N"));
            string dir = Path.Combine(root, "Ween-Boognish Provides-14CD-2026-FLAC");
            Directory.CreateDirectory(dir);
            try
            {
                // An untagged FLAC (tags stripped by copying the audio frames is overkill: use a file with no Vorbis fields).
                string untagged = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "untagged.flac");
                File.Copy(untagged, Path.Combine(dir, "0101 Koko.flac"));
                File.Copy(untagged, Path.Combine(dir, "0102 Don't Shit Where You Eat.flac"));
                File.Copy(untagged, Path.Combine(dir, "1401 Voodoo Lady.flac"));

                var album = new Album(dir);

                Assert.That(album.Artist, Is.EqualTo("Ween"));
                Assert.That(album.AlbumTitle, Is.EqualTo("Boognish Provides"));
                Assert.That(album.ReleaseDate, Is.EqualTo("2026"));
                Assert.That(album.Tracks[0].DiscNumber, Is.EqualTo("1"));
                Assert.That(album.Tracks[0].TrackNumber, Is.EqualTo("1"));
                Assert.That(album.Tracks[0].Title, Is.EqualTo("Koko"));
                Assert.That(album.Tracks[1].Title, Is.EqualTo("Don't Shit Where You Eat"));
                Assert.That(album.Tracks[2].DiscNumber, Is.EqualTo("14"));
                Assert.That(album.Tracks[2].TrackNumber, Is.EqualTo("1"));
                Assert.That(album.Tracks[2].Title, Is.EqualTo("Voodoo Lady"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

using System.IO;
using System.Linq;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Tag;
using IdSharp.Tagging.ID3v2;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>FLAC support in Tag Album. TestData/tagged.flac is a synthetic 0.3 s tone with tags and a cover.</summary>
    [TestFixture]
    public class FlacTrackTests
    {
        private static string FlacPath
        {
            get { return Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "tagged.flac"); }
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
        public void ReadsVorbisCommentsAndCover()
        {
            var track = new AlbumTrack(FlacPath);

            Assert.That(track.IsFlac, Is.True);
            Assert.That(track.Artist, Is.EqualTo("Test Artist"));
            Assert.That(track.Album, Is.EqualTo("Test Album"));
            Assert.That(track.Title, Is.EqualTo("Song One"));
            Assert.That(track.ReleaseDate, Is.EqualTo("2026"));
            Assert.That(track.Genre, Is.EqualTo("Electronic"));
            Assert.That(track.TrackNumber, Is.EqualTo("3"), "total tracks stripped");
            Assert.That(track.DiscNumber, Is.EqualTo("2"), "total discs stripped");
            Assert.That(track.TotalSeconds, Is.GreaterThan(0.2m).And.LessThan(0.4m));

            Assert.That(track.CoverPicture, Is.Not.Null, "embedded PICTURE block");
            Assert.That(track.CoverPicture.PictureType, Is.EqualTo(PictureType.CoverFront));
            Assert.That(track.CoverPicture.ImageSource, Is.Not.Null);
        }

        [Test]
        public void AlbumIncludesSubfoldersInPathOrderAndSkipsHiddenFolders()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cdtag-multidisc-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "CD2"));
                Directory.CreateDirectory(Path.Combine(dir, "CD1"));
                var hidden = Directory.CreateDirectory(Path.Combine(dir, ".hidden"));
                hidden.Attributes |= FileAttributes.Hidden;

                File.Copy(FlacPath, Path.Combine(dir, "CD2", "01.flac"));
                File.Copy(FlacPath, Path.Combine(dir, "CD1", "02.flac"));
                File.Copy(FlacPath, Path.Combine(dir, "CD1", "01.flac"));
                File.Copy(FlacPath, Path.Combine(dir, ".hidden", "99.flac"));

                var album = new Album(dir);

                Assert.That(album.Tracks.Count, Is.EqualTo(3));
                Assert.That(album.Tracks[0].OriginalFileName, Does.EndWith(Path.Combine("CD1", "01.flac")));
                Assert.That(album.Tracks[1].OriginalFileName, Does.EndWith(Path.Combine("CD1", "02.flac")));
                Assert.That(album.Tracks[2].OriginalFileName, Does.EndWith(Path.Combine("CD2", "01.flac")));
                Assert.That(CDTag.Model.Profile.ProfileSelector.GetAlbumFileTypes(dir), Is.EquivalentTo(new[] { "flac" }));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    foreach (var d in new DirectoryInfo(dir).GetDirectories("*", SearchOption.AllDirectories))
                        d.Attributes = FileAttributes.Directory;
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [TestCase("CD1", true)]
        [TestCase("cd 2", true)]
        [TestCase("Disc 1", true)]
        [TestCase("Disk_2 - Live", true)]
        [TestCase("Side A", true)]
        [TestCase("02", true)]
        [TestCase("Test Artist - Test Album (2026)", false)]
        [TestCase("Cdtag Album", false)]
        [TestCase("Scans", false)]
        public void RecognisesDiscFolderNames(string name, bool expected)
        {
            Assert.That(Album.IsDiscFolderName(name), Is.EqualTo(expected));
        }

        [Test]
        public void CollectionFolderIsDetectedButMultiDiscAlbumIsNot()
        {
            string root = Path.Combine(Path.GetTempPath(), "cdtag-collection-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                string albumA = Path.Combine(root, "Artist A - Album (2001)");
                string albumB = Path.Combine(root, "Artist B - Album (2002)");
                Directory.CreateDirectory(Path.Combine(albumA, "CD1"));
                Directory.CreateDirectory(Path.Combine(albumA, "CD2"));
                Directory.CreateDirectory(albumB);
                File.Copy(FlacPath, Path.Combine(albumA, "CD1", "01.flac"));
                File.Copy(FlacPath, Path.Combine(albumA, "CD2", "01.flac"));
                File.Copy(FlacPath, Path.Combine(albumB, "01.flac"));

                Assert.That(Album.IsCollectionFolder(root), Is.True, "folder of albums");
                Assert.That(Album.IsCollectionFolder(albumA), Is.False, "multi-disc album");
                Assert.That(Album.IsCollectionFolder(albumB), Is.False, "single album");
                Assert.That(Album.GetAudioFiles(albumA).Count(), Is.EqualTo(2));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void AlbumLoadsFlacFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cdtag-flac-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.Copy(FlacPath, Path.Combine(dir, "01.flac"));
                File.Copy(FlacPath, Path.Combine(dir, "02.flac"));
                File.WriteAllText(Path.Combine(dir, "notes.txt"), "not audio");

                var album = new Album(dir);

                Assert.That(album.Tracks.Count, Is.EqualTo(2));
                Assert.That(album.AlbumTitle, Is.EqualTo("Test Album"));
                Assert.That(album.Artist, Is.EqualTo("Test Artist"));
                Assert.That(album.Picture, Is.Not.Null);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}

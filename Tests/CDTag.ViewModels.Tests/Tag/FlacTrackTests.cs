using System.IO;
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

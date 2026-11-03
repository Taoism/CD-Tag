using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Lookup;
using CDTag.Model.Profile;
using CDTag.Model.Tag;
using IdSharp.Tagging.ID3v1;
using IdSharp.Tagging.ID3v2;
using IdSharp.Tagging.VorbisComment;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>Finish on throwaway album folders built from the synthetic test files.</summary>
    [TestFixture]
    public class FinishTests
    {
        private string _root;

        [OneTimeSetUp]
        public void Setup()
        {
            IoC.ClearAllRegistrations();
            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());
        }

        [SetUp]
        public void CreateRoot()
        {
            _root = Path.Combine(Path.GetTempPath(), "cdtag-finish-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void DeleteRoot()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        private static string TestFile(string name)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", name);
        }

        private static UserProfile Profile()
        {
            var profile = new UserProfile { ProfileName = "GRP" };
            foreach (NamingFormat format in new[] { profile.FileNaming.SingleCD.SingleArtist, profile.FileNaming.SingleCD.VariousArtists })
            {
                format.Directory = "<Artist>-<Album>-<Year>-GRP";
                format.AudioFile = "<LC><Track>-<Artist>-<Song></LC>-grp";
                format.Playlist = format.Checksum = format.NFO = format.CUE = format.EACLog = "<LC>00-<Artist>-<Album>-<Year></LC>-grp";
                format.Images = "<LC>00-<Artist>-<Album>-<Year>-<ImageText></LC>-grp";
            }
            foreach (NamingFormat format in new[] { profile.FileNaming.MultiCD.SingleArtist, profile.FileNaming.MultiCD.VariousArtists })
            {
                format.Directory = "<Artist>-<Album>-<Discs>CD-<Year>-GRP";
                format.AudioFile = "<LC><Disc><Track>-<Artist>-<Song></LC>-grp";
                format.Playlist = "<LC><Disc>00-<Artist>-<Album>-<Year></LC>-grp";
                format.Checksum = format.NFO = format.CUE = format.EACLog = "<LC>0000-<Artist>-<Album>-<Year></LC>-grp";
                format.Images = "<LC>0000-<Artist>-<Album>-<Year>-<ImageText></LC>-grp";
            }
            profile.FileNaming.UseUnderscores = true;
            profile.Finish.RenameDirectory = true;
            profile.Finish.RenameAudioFiles = true;
            profile.Finish.RenameImageFiles = true;
            profile.Finish.RenameEACLogFiles = false;
            profile.Finish.CreatePlaylist = true;
            profile.Finish.CreateSFV = true;
            profile.Finish.NFO = FinishNFO.CreateNew;
            profile.Finish.DeleteOldFiles = true;
            profile.Finish.DeleteEmptySubdirectories = true;
            profile.DeleteFiles = new[] { "*.sfv", "*.m3u", ".message" };
            profile.Tagging = new TaggingOptions { ChecksumHideGeneratedBy = true, ChecksumHideFileInfo = true };
            return profile;
        }

        private string MakeFolder(string name, string source, params string[] files)
        {
            string folder = Path.Combine(_root, name);
            foreach (string file in files)
            {
                string path = Path.Combine(folder, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.Copy(TestFile(source), path);
            }
            return folder;
        }

        private static byte[] Cover()
        {
            var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgr24, null, new byte[4 * 4 * 3], 4 * 3);
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        private static Album Load(string folder, string artist, string title, params string[] songs)
        {
            var album = new Album(folder);
            album.Artist = artist;
            album.AlbumTitle = title;
            album.Genre = "Rock";
            var tracks = album.Tracks.ToList();
            for (int i = 0; i < tracks.Count; i++)
            {
                tracks[i].Title = songs[i];
                tracks[i].Artist = artist;
            }
            return album;
        }

        private static ReleaseInfo Release()
        {
            return new ReleaseInfo { RetailDate = "1994-00-00", RipDate = "2026-11-02" };
        }

        [Test]
        public void CrcMatchesTheStandardCheckValue()
        {
            Assert.That(Crc32.Compute(new MemoryStream(Encoding.ASCII.GetBytes("123456789"))), Is.EqualTo(0xCBF43926u));
        }

        [Test]
        public void FinishesAFlacAlbum()
        {
            string folder = MakeFolder("Ween - Pure Guava (src)", "untagged.flac", "01.flac", "02.flac");
            File.WriteAllText(Path.Combine(folder, "old.sfv"), "x");
            File.WriteAllText(Path.Combine(folder, "rip.log"), "log");
            File.WriteAllBytes(Path.Combine(folder, "folder.jpg"), Cover());
            long samples = FlacStreamInfo.Read(Path.Combine(folder, "01.flac")).TotalSamples;

            Album album = Load(folder, "Ween", "Pure Guava", "Little Birdy", "Tender Situation");
            album.CoverBytes = Cover();
            UserProfile profile = Profile();
            var finisher = new AlbumFinisher(profile, album, Release(), "#Artist - #Album\r\n#N. #Song  #Time\r\n");

            FinishPlan plan = finisher.Plan();
            Assert.That(plan.Problems, Is.Empty);
            string result = finisher.Run(plan);

            Assert.That(Path.GetFileName(result), Is.EqualTo("Ween-Pure_Guava-1994-GRP"));
            Assert.That(Directory.Exists(folder), Is.False, "old folder renamed");
            string[] names = Directory.GetFiles(result).Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.That(names, Is.EqualTo(new[]
            {
                "00-ween-pure_guava-1994-front-grp.jpg", "00-ween-pure_guava-1994-grp.m3u", "00-ween-pure_guava-1994-grp.nfo",
                "00-ween-pure_guava-1994-grp.sfv", "01-ween-little_birdy-grp.flac", "02-ween-tender_situation-grp.flac", "rip.log"
            }));

            string first = Path.Combine(result, "01-ween-little_birdy-grp.flac");
            var vorbis = new VorbisComment(first);
            Assert.That(vorbis.Title, Is.EqualTo("Little Birdy"));
            Assert.That(vorbis.Artist, Is.EqualTo("Ween"));
            Assert.That(vorbis.Album, Is.EqualTo("Pure Guava"));
            Assert.That(vorbis.Items.GetValue("DATE"), Is.EqualTo("1994"));
            Assert.That(vorbis.Items.GetValue("TRACKNUMBER"), Is.EqualTo("1"));
            Assert.That(vorbis.Items.GetValue("TRACKTOTAL"), Is.EqualTo("2"));
            Assert.That(vorbis.GetPictureBlocks().Count, Is.EqualTo(1));
            Assert.That(Picture.FromFlacPictureBlock(vorbis.GetPictureBlocks()[0]).PictureType, Is.EqualTo(PictureType.CoverFront));
            Assert.That(FlacStreamInfo.Read(first).TotalSamples, Is.EqualTo(samples), "audio untouched");
            Assert.That(ID3v2Tag.DoesTagExist(first), Is.False);

            string[] sfv = File.ReadAllLines(Path.Combine(result, "00-ween-pure_guava-1994-grp.sfv"));
            Assert.That(sfv, Is.EqualTo(new[]
            {
                "01-ween-little_birdy-grp.flac " + Crc32.Compute(first).ToString("X8"),
                "02-ween-tender_situation-grp.flac " + Crc32.Compute(Path.Combine(result, "02-ween-tender_situation-grp.flac")).ToString("X8")
            }));
            Assert.That(File.ReadAllLines(Path.Combine(result, "00-ween-pure_guava-1994-grp.m3u")),
                        Is.EqualTo(new[] { "01-ween-little_birdy-grp.flac", "02-ween-tender_situation-grp.flac" }));
            Assert.That(File.ReadAllText(Path.Combine(result, "00-ween-pure_guava-1994-grp.nfo")), Does.StartWith("Ween - Pure Guava"));
        }

        [Test]
        public void FinishesAnMp3AlbumWithId3v2AndId3v1()
        {
            string folder = MakeFolder("mp3 (src)", "silence.mp3", "01.mp3", "02.mp3");
            byte[] audio = File.ReadAllBytes(Path.Combine(folder, "01.mp3"));

            Album album = Load(folder, "Ween", "Chocolate and Cheese", "Take Me Away", "Spinal Meningitis");
            album.CoverBytes = Cover();
            string result = Finish(album, Profile());

            string first = Path.Combine(result, "01-ween-take_me_away-grp.mp3");
            var id3v2 = new ID3v2Tag(first);
            Assert.That(id3v2.Title, Is.EqualTo("Take Me Away"));
            Assert.That(id3v2.Artist, Is.EqualTo("Ween"));
            Assert.That(id3v2.TrackNumber, Is.EqualTo("1/2"));
            Assert.That(id3v2.Year, Is.EqualTo("1994"));
            Assert.That(id3v2.PictureList.Count, Is.EqualTo(1));
            Assert.That(id3v2.PictureList[0].PictureType, Is.EqualTo(PictureType.CoverFront));

            var id3v1 = new ID3v1Tag(first);
            Assert.That(id3v1.Title, Is.EqualTo("Take Me Away"));
            Assert.That(id3v1.TrackNumber, Is.EqualTo(1));
            Assert.That(id3v1.GenreIndex, Is.EqualTo(GenreHelper.GetGenreIndex("Rock")));

            // The MPEG frames are unchanged between the tags.
            byte[] written = File.ReadAllBytes(first);
            int start = ID3v2Tag.GetTagSize(first);
            Assert.That(written.Skip(start).Take(written.Length - start - 128).ToArray(), Is.EqualTo(audio));
        }

        [Test]
        public void MultiDiscFoldersGetAPlaylistAndChecksumPerDisc()
        {
            string folder = MakeFolder("set (src)", "untagged.flac", @"CD1\01.flac", @"CD1\02.flac", @"CD2\01.flac");
            Album album = Load(folder, "Ween", "Live", "One", "Two", "Three");
            album.SetDiscName(1, "McCabe's");
            string result = Finish(album, Profile());

            Assert.That(Path.GetFileName(result), Is.EqualTo("Ween-Live-2CD-1994-GRP"));
            Assert.That(File.ReadAllLines(Path.Combine(result, "CD1", "100-ween-live-1994-grp.m3u")),
                        Is.EqualTo(new[] { "101-ween-one-grp.flac", "102-ween-two-grp.flac" }));
            Assert.That(File.ReadAllLines(Path.Combine(result, "CD2", "200-ween-live-1994-grp.m3u")), Is.EqualTo(new[] { "201-ween-three-grp.flac" }));
            Assert.That(File.Exists(Path.Combine(result, "CD1", "0000-ween-live-1994-grp.sfv")), Is.True);
            Assert.That(File.Exists(Path.Combine(result, "CD2", "0000-ween-live-1994-grp.sfv")), Is.True);

            var vorbis = new VorbisComment(Path.Combine(result, "CD1", "101-ween-one-grp.flac"));
            Assert.That(vorbis.Items.GetValue("DISCNUMBER"), Is.EqualTo("1"));
            Assert.That(vorbis.Items.GetValue("DISCTOTAL"), Is.EqualTo("2"));
            Assert.That(vorbis.Items.GetValue("DISCSUBTITLE"), Is.EqualTo("McCabe's"));
        }

        [Test]
        public void ProblemsStopFinishBeforeAnythingChanges()
        {
            string folder = MakeFolder("bad (src)", "untagged.flac", "01.flac", "02.flac");
            Album album = Load(folder, "Ween", "Pure Guava", "Same", "Same");
            album.Tracks[1].TrackNumber = "1";
            File.WriteAllText(Path.Combine(folder, "old.sfv"), "x");

            FinishPlan plan = new AlbumFinisher(Profile(), album, Release(), null).Plan();

            Assert.That(plan.CanRun, Is.False);
            Assert.That(plan.Problems, Has.Some.Contains("Two files would be named"));
            Assert.That(() => new AlbumFinisher(Profile(), album, Release(), null).Run(plan), Throws.InvalidOperationException);
            Assert.That(Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n), Is.EqualTo(new[] { "01.flac", "02.flac", "old.sfv" }));
        }

        private static string Finish(Album album, UserProfile profile)
        {
            var finisher = new AlbumFinisher(profile, album, Release(), null);
            FinishPlan plan = finisher.Plan();
            Assert.That(plan.Problems, Is.Empty);
            return finisher.Run(plan);
        }
    }
}

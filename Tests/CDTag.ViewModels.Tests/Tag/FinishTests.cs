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
using CDTag.ViewModels.Tag.TagAlbum;
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
            File.WriteAllText(Path.Combine(folder, "Folder.auCDtect.txt"), "auCDtect");
            File.WriteAllBytes(Path.Combine(folder, "folder.jpg"), Cover());
            long samples = FlacStreamInfo.Read(Path.Combine(folder, "01.flac")).TotalSamples;

            Album album = Load(folder, "Ween", "Pure Guava", "Little Birdy", "Tender Situation");
            album.CoverBytes = Cover();
            UserProfile profile = Profile();
            var finisher = new AlbumFinisher(profile, album, Release(), "#Artist - #Album\r\n#N. #Song  #Time\r\n");

            FinishPlan plan = finisher.Plan();
            Assert.That(plan.Problems, Is.Empty);
            Assert.That(plan.RuleSetName, Is.EqualTo("FLAC rules v3.0"));
            Assert.That(plan.RuleIssues, Has.Some.StartsWith("FLAC 4.1"), "the test profile's folder name has no -FLAC-");
            Assert.That(plan.Describe(), Does.Contain("Scene rules (FLAC rules v3.0)"));
            string result = finisher.Run(plan);

            Assert.That(Path.GetFileName(result), Is.EqualTo("Ween-Pure_Guava-1994-GRP"));
            Assert.That(Directory.Exists(folder), Is.False, "old folder renamed");
            string[] names = Directory.GetFiles(result).Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.That(names, Is.EqualTo(new[]
            {
                "00-ween-pure_guava-1994-front-grp.jpg", "00-ween-pure_guava-1994-grp.m3u", "00-ween-pure_guava-1994-grp.nfo",
                "00-ween-pure_guava-1994-grp.sfv", "01-ween-little_birdy-grp.flac", "02-ween-tender_situation-grp.flac"
            }), "rip.log and Folder.auCDtect.txt are deleted (Keep the rip logs is off)");

            string first = Path.Combine(result, "01-ween-little_birdy-grp.flac");
            var vorbis = new VorbisComment(first);
            Assert.That(vorbis.Title, Is.EqualTo("Little Birdy"));
            Assert.That(vorbis.Artist, Is.EqualTo("Ween"));
            Assert.That(vorbis.Album, Is.EqualTo("Pure Guava"));
            Assert.That(vorbis.Items.GetValue("DATE"), Is.EqualTo("1994"));
            Assert.That(vorbis.Items.GetValue("TRACKNUMBER"), Is.EqualTo("1"));
            Assert.That(vorbis.Items.GetValue("TOTALTRACKS"), Is.EqualTo("2"), "FLAC rules 6.2.1 names");
            Assert.That(vorbis.Items.GetValue("TRACKTOTAL"), Is.Null);
            Assert.That(vorbis.Items.GetValue("DISCNUMBER"), Is.EqualTo("1"));
            Assert.That(vorbis.Items.GetValue("TOTALDISCS"), Is.EqualTo("1"));
            Assert.That(vorbis.GetPictureBlocks().Count, Is.EqualTo(0), "FLAC rules 6.3: picture block must be empty");
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
            UserProfile profile = Profile();
            profile.Finish.MoveOutOfDiscFolders = false;
            string result = Finish(album, profile);

            Assert.That(Path.GetFileName(result), Is.EqualTo("Ween-Live-2CD-1994-GRP"));
            Assert.That(File.ReadAllLines(Path.Combine(result, "CD1", "100-ween-live-1994-grp.m3u")),
                        Is.EqualTo(new[] { "101-ween-one-grp.flac", "102-ween-two-grp.flac" }));
            Assert.That(File.ReadAllLines(Path.Combine(result, "CD2", "200-ween-live-1994-grp.m3u")), Is.EqualTo(new[] { "201-ween-three-grp.flac" }));
            Assert.That(File.ReadAllLines(Path.Combine(result, "000-ween-live-1994-grp.m3u")),
                        Is.EqualTo(new[] { "CD1" + Path.DirectorySeparatorChar + "101-ween-one-grp.flac", "CD1" + Path.DirectorySeparatorChar + "102-ween-two-grp.flac", "CD2" + Path.DirectorySeparatorChar + "201-ween-three-grp.flac" }), "master playlist, disc order");
            Assert.That(File.Exists(Path.Combine(result, "CD1", "000-ween-live-1994-grp.sfv")), Is.True);
            Assert.That(File.Exists(Path.Combine(result, "CD2", "000-ween-live-1994-grp.sfv")), Is.True);

            var vorbis = new VorbisComment(Path.Combine(result, "CD1", "101-ween-one-grp.flac"));
            Assert.That(vorbis.Items.GetValue("DISCNUMBER"), Is.EqualTo("1"));
            Assert.That(vorbis.Items.GetValue("TOTALDISCS"), Is.EqualTo("2"));
            Assert.That(vorbis.Items.GetValue("DISCSUBTITLE"), Is.EqualTo("McCabe's"));
        }

        [Test]
        public void TenOrMoreDiscsGetTwoDigitDiscNumbers()
        {
            string[] files = Enumerable.Range(1, 10).Select(d => @"CD" + d + @"\01.flac").ToArray();
            string folder = MakeFolder("box (src)", "untagged.flac", files);
            Album album = Load(folder, "Ween", "Box", Enumerable.Range(1, 10).Select(d => "Song " + d).ToArray());

            string result = Finish(album, Profile());

            Assert.That(File.Exists(Path.Combine(result, "0101-ween-song_1-grp.flac")), Is.True);
            Assert.That(File.Exists(Path.Combine(result, "1001-ween-song_10-grp.flac")), Is.True);
            Assert.That(Directory.GetDirectories(result), Is.Empty);
        }

        [Test]
        public void DiscFoldersAreEmptiedIntoTheAlbumFolder()
        {
            string folder = MakeFolder("set (src)", "untagged.flac", @"CD1\01.flac", @"CD1\02.flac", @"CD2\01.flac");
            byte[] cover = Cover();
            foreach (string disc in new[] { "CD1", "CD2" })
            {
                File.WriteAllBytes(Path.Combine(folder, disc, "folder.jpg"), cover); // the same cover in both
                File.WriteAllText(Path.Combine(folder, disc, "rip.cue"), "cue " + disc);
                File.WriteAllText(Path.Combine(folder, disc, "rip.log"), "log " + disc);
                File.WriteAllText(Path.Combine(folder, disc, "old.m3u"), "x"); // on the delete list
            }
            File.WriteAllText(Path.Combine(folder, "CD2", "notes.txt"), "notes");
            File.WriteAllText(Path.Combine(folder, "CD1", "Folder.auCDtect.txt"), "auCDtect CD1");
            File.WriteAllText(Path.Combine(folder, "CD2", "Folder.auCDtect.txt"), "auCDtect CD2");
            Album album = Load(folder, "Ween", "Live", "One", "Two", "Three");
            UserProfile profile = Profile();
            profile.Finish.RenameEACLogFiles = true; // log format has no <Disc>
            profile.FileNaming.MultiCD.SingleArtist.CUE = "<LC><Disc>01-<Artist>-<Album>-<Year></LC>-grp";
            var finisher = new AlbumFinisher(profile, album, Release(), null) { KeepLogs = true };

            FinishPlan plan = finisher.Plan();
            Assert.That(plan.Problems, Is.Empty);
            Assert.That(plan.Describe(), Does.Contain("Move the files out of CD1, CD2 into the album folder"));
            string result = finisher.Run(plan);

            Assert.That(Directory.GetDirectories(result), Is.Empty, "CD1 and CD2 are deleted");
            string[] names = Directory.GetFiles(result).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, names));
            Assert.That(names, Does.Contain("101-ween-one-grp.flac"));
            Assert.That(names, Does.Contain("102-ween-two-grp.flac"));
            Assert.That(names, Does.Contain("201-ween-three-grp.flac"));
            Assert.That(names, Does.Contain("101-ween-live-1994-grp.cue"));
            Assert.That(names, Does.Contain("201-ween-live-1994-grp.cue"));
            Assert.That(names, Does.Contain("000-ween-live-1994-grp-cd1.log"));
            Assert.That(names, Does.Contain("000-ween-live-1994-grp-cd2.log"));
            Assert.That(names, Does.Contain("000-ween-live-1994-front-grp.jpg"), "one cover; the identical second copy is deleted");
            Assert.That(names, Does.Contain("notes.txt"));
            Assert.That(File.ReadAllText(Path.Combine(result, "000-ween-live-1994-grp-cd1-aucdtect.txt")), Is.EqualTo("auCDtect CD1"), "auCDtect reports are logs: kept and named like them");
            Assert.That(File.ReadAllText(Path.Combine(result, "000-ween-live-1994-grp-cd2-aucdtect.txt")), Is.EqualTo("auCDtect CD2"));
            Assert.That(names.Count(n => n.EndsWith(".jpg")), Is.EqualTo(1));
            Assert.That(names.Count(n => n.EndsWith(".sfv")), Is.EqualTo(1), "one checksum file for the album");
            Assert.That(names.Any(n => n == "old.m3u"), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(result, "201-ween-live-1994-grp.cue")), Is.EqualTo("cue CD2"));

            string sfv = names.Single(n => n.EndsWith(".sfv"));
            Assert.That(File.ReadAllLines(Path.Combine(result, sfv)).Select(l => l.Split(' ')[0]),
                        Is.EqualTo(new[] { "101-ween-one-grp.flac", "102-ween-two-grp.flac", "201-ween-three-grp.flac" }));
            foreach (string m3u in names.Where(n => n.EndsWith(".m3u")))
                Assert.That(File.ReadAllLines(Path.Combine(result, m3u)).All(l => !l.Contains("\\")), Is.True, "playlist entries are in the album folder");
        }

        [TestCase("CD")]
        [TestCase("cd")]
        public void ZeroPaddedDiscFoldersCd01ToCd10AreEmptiedInOrder(string prefix)
        {
            // CD01 ... CD09, then CD10
            string[] files = Enumerable.Range(1, 10).Select(d => prefix + d.ToString("00") + @"\01.flac").ToArray();
            string folder = MakeFolder("padded (src)", "untagged.flac", files);
            Album album = Load(folder, "Ween", "Box", Enumerable.Range(1, 10).Select(d => "Song " + d).ToArray());

            Assert.That(album.Tracks.Select(t => t.DiscNumber), Is.EqualTo(Enumerable.Range(1, 10).Select(d => d.ToString())));

            string result = Finish(album, Profile());

            Assert.That(Directory.GetDirectories(result), Is.Empty, "CD01 ... CD10 are deleted");
            string[] audio = Directory.GetFiles(result, "*.flac").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.That(audio, Is.EqualTo(Enumerable.Range(1, 10).Select(d => d.ToString("00") + "01-ween-song_" + d + "-grp.flac")));
            string sfv = Directory.GetFiles(result, "*.sfv").Single();
            Assert.That(File.ReadAllLines(sfv).Select(l => l.Split(' ')[0]), Is.EqualTo(audio), "disc order: 01 ... 09, then 10");
        }

        [TestCase("cd")]
        [TestCase("CD")]
        public void ThreePaddedDiscFoldersGive101_201_301(string prefix)
        {
            string folder = MakeFolder("three (src)", "untagged.flac", prefix + @"01\01.flac", prefix + @"02\01.flac", prefix + @"03\01.flac");
            Album album = Load(folder, "Ween", "Three", "One", "Two", "Three");

            string result = Finish(album, Profile());

            Assert.That(Directory.GetDirectories(result), Is.Empty);
            Assert.That(Directory.GetFiles(result, "*.flac").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal),
                        Is.EqualTo(new[] { "101-ween-one-grp.flac", "201-ween-two-grp.flac", "301-ween-three-grp.flac" }));
        }

        [Test]
        public void ArtFoldersGoIntoTheAlbumFolderWithImageNamesAndAreDeleted()
        {
            string folder = MakeFolder("art (src)", "untagged.flac", @"CD1\01.flac", @"CD2\01.flac");
            Directory.CreateDirectory(Path.Combine(folder, "Artwork"));
            File.WriteAllBytes(Path.Combine(folder, "Artwork", "Front.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(folder, "Artwork", "back.webp"), new byte[] { 2 });
            File.WriteAllText(Path.Combine(folder, "Artwork", "Thumbs.db"), "junk");
            Directory.CreateDirectory(Path.Combine(folder, "CD2", "Scans", "Inlay"));
            File.WriteAllBytes(Path.Combine(folder, "CD2", "Scans", "booklet 01.tif"), new byte[] { 3 });
            File.WriteAllBytes(Path.Combine(folder, "CD2", "Scans", "Inlay", "front.png"), new byte[] { 4 }); // same name as Artwork\Front.png
            Directory.CreateDirectory(Path.Combine(folder, "Extras"));
            File.WriteAllBytes(Path.Combine(folder, "Extras", "photo.jpg"), new byte[] { 5 });
            File.WriteAllText(Path.Combine(folder, "Extras", "readme.txt"), "not art");
            Album album = Load(folder, "Ween", "Live", "One", "Two");
            var finisher = new AlbumFinisher(Profile(), album, Release(), null);

            FinishPlan plan = finisher.Plan();
            Assert.That(plan.Problems, Is.Empty);
            Assert.That(plan.Warnings, Has.Some.Contains("Extras"));
            string result = finisher.Run(plan);

            string[] names = Directory.GetFiles(result).Select(Path.GetFileName).ToArray();
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, names));
            Assert.That(names, Does.Contain("000-ween-live-1994-artwork_front-grp.png"));
            Assert.That(names, Does.Contain("000-ween-live-1994-cd2_scans_inlay_front-grp.png"));
            Assert.That(names, Does.Contain("000-ween-live-1994-back-grp.webp"));
            Assert.That(names, Does.Contain("000-ween-live-1994-booklet_01-grp.tif"));
            Assert.That(Directory.GetDirectories(result).Select(Path.GetFileName), Is.EqualTo(new[] { "Extras" }), "Artwork, CD1, CD2 (and Scans) are deleted");
        }

        [Test]
        public void DifferentFilesWithTheSameNameInTwoDiscFoldersGetTheDiscInTheName()
        {
            string folder = MakeFolder("clash (src)", "untagged.flac", @"CD1\01.flac", @"CD2\01.flac");
            File.WriteAllText(Path.Combine(folder, "CD1", "info.txt"), "one");
            File.WriteAllText(Path.Combine(folder, "CD2", "info.txt"), "two");
            Album album = Load(folder, "Ween", "Two", "One", "Two");
            var finisher = new AlbumFinisher(Profile(), album, Release(), null);

            FinishPlan plan = finisher.Plan();

            Assert.That(plan.Problems, Is.Empty);
            Assert.That(plan.Moves.Select(m => Path.GetFileName(m.Target)), Is.SupersetOf(new[] { "info-cd1.txt", "info-cd2.txt" }));
        }

        [Test]
        public void MultiDiscNamesGetTheDiscEvenWhenTheProfileLeavesItOut()
        {
            string folder = MakeFolder("two (src)", "untagged.flac", @"CD1\01.flac", @"CD2\01.flac");
            Album album = Load(folder, "Ween", "Two", "One", "Two");
            UserProfile profile = Profile();
            profile.FileNaming.MultiCD.SingleArtist.AudioFile = "<LC><Track>-<Artist>-<Song></LC>-grp";

            string result = Finish(album, profile);

            Assert.That(File.Exists(Path.Combine(result, "101-ween-one-grp.flac")), Is.True);
            Assert.That(File.Exists(Path.Combine(result, "201-ween-two-grp.flac")), Is.True);
        }

        [Test]
        public void DiscFoldersWinWhenEveryFileIsTaggedDiscOne()
        {
            string folder = MakeFolder("tagged (src)", "untagged.flac", @"CD1\01.flac", @"CD2\01.flac");
            foreach (string file in Directory.GetFiles(folder, "*.flac", SearchOption.AllDirectories))
            {
                var vorbis = new VorbisComment(file);
                vorbis.Items.SetValue("DISCNUMBER", "1");
                vorbis.Items.SetValue("TRACKNUMBER", "1");
                vorbis.Save(file);
            }

            Album album = Load(folder, "Ween", "Two", "One", "Two");

            Assert.That(album.Tracks.Select(t => t.DiscNumber), Is.EqualTo(new[] { "1", "2" }));
        }

        [Test]
        public void FlacCoverCanBeEmbeddedWhenTheProfileSaysSo()
        {
            string folder = MakeFolder("cover (src)", "untagged.flac", "01.flac");
            Album album = Load(folder, "Ween", "Pure Guava", "Little Birdy");
            album.CoverBytes = Cover();
            UserProfile profile = Profile();
            profile.Tagging.EmbedCoverInFlac = true;

            string result = Finish(album, profile);

            var vorbis = new VorbisComment(Directory.GetFiles(result, "*.flac").Single());
            Assert.That(vorbis.GetPictureBlocks().Count, Is.EqualTo(1));
            Assert.That(Picture.FromFlacPictureBlock(vorbis.GetPictureBlocks()[0]).PictureType, Is.EqualTo(PictureType.CoverFront));
        }

        [Test]
        public void WebFlacGetsTheBitDepthInItsNameAndKeepsShopArtwork()
        {
            string folder = MakeFolder("web (src)", "untagged.flac", "01.flac");
            string file = Path.Combine(folder, "01.flac");
            var shop = new VorbisComment(file);
            shop.SetPictureBlocks(new[] { AlbumFinisher.FlacPictureBlock(Cover()) });
            shop.Save(file);

            Album album = Load(folder, "Ween", "Pure Guava", "Little Birdy");
            UserProfile profile = Profile();
            profile.FileNaming.SingleCD.SingleArtist.Directory = "<Artist>-<Album>-WEB-FLAC-<Year>-GRP";
            ReleaseInfo release = Release();
            release.Source = new ReleaseListItem { Nfo = "WEB", Dir = "WEB" };
            release.Url = "https://example.bandcamp.com/album/pure-guava";

            var finisher = new AlbumFinisher(profile, album, release, "#Artist\r\n");
            FinishPlan plan = finisher.Plan();
            Assert.That(plan.RuleSetName, Is.EqualTo("WEB FLAC rules 1.0"));
            string result = finisher.Run(plan);

            Assert.That(Path.GetFileName(result), Is.EqualTo("Ween-Pure_Guava-16BIT-WEB-FLAC-1994-GRP"));
            var vorbis = new VorbisComment(Directory.GetFiles(result, "*.flac").Single());
            Assert.That(vorbis.GetPictureBlocks().Count, Is.EqualTo(1), "WEB FLAC rules do not require an empty picture block");
        }

        [Test]
        public void WebFlacUsesTheChosenBitAndKhz()
        {
            string folder = MakeFolder("web khz (src)", "untagged.flac", "01.flac");
            Album album = Load(folder, "Ween", "Pure Guava", "Little Birdy");
            UserProfile profile = Profile();
            profile.FileNaming.SingleCD.SingleArtist.Directory = "<Artist>-<Album>-<Source>-FLAC-<Year>-GRP";
            ReleaseInfo release = Release();
            release.Source = new ReleaseListItem { Nfo = "WEB-FLAC", Dir = "WEB-FLAC" };
            release.Url = "https://example.bandcamp.com/album/pure-guava";
            string rate = SceneRules.SampleRateTag(album);
            Assert.That(rate, Is.EqualTo("44KHZ"));
            release.Quality = "16BIT";

            var finisher = new AlbumFinisher(profile, album, release, "#Artist\r\n");
            FinishPlan plan = finisher.Plan();
            Assert.That(plan.NewDirectory, Does.EndWith("Ween-Pure_Guava-16BIT-WEB-FLAC-1994-GRP"));
            Assert.That(SceneRules.QualityTags(album), Has.None.StartsWith("16BIT-"), "16 bit has no kHz choices");
            Assert.That(plan.RuleIssues, Has.None.Contains("4.10"));

            release.Quality = "24BIT-96KHZ";
            plan = new AlbumFinisher(profile, album, release, "#Artist\r\n").Plan();
            Assert.That(plan.NewDirectory, Does.EndWith("-24BIT-96KHZ-WEB-FLAC-1994-GRP"));
            Assert.That(plan.RuleIssues, Has.Some.Contains("says 24BIT but the files are 16BIT"));
            Assert.That(plan.RuleIssues, Has.Some.Contains("says 96KHZ but the files are 44KHZ"));
        }

        [Test]
        public void BlurayGetsTheChosenBitAndKhzCheckedAgainstTheFiles()
        {
            string folder = MakeFolder("bluray (src)", "untagged.flac", "01.flac");
            Album album = Load(folder, "Ween", "Pure Guava", "Little Birdy");
            UserProfile profile = Profile();
            profile.FileNaming.SingleCD.SingleArtist.Directory = "<Artist>-<Album>-<Source>-FLAC-<Year>-GRP";
            ReleaseInfo release = Release();
            release.Source = new ReleaseListItem { Nfo = "BLURAY", Dir = "BLURAY" };
            Assert.That(SceneRules.HasBitKhzChoice(album, "BLURAY"), Is.True);
            Assert.That(SceneRules.HasBitKhzChoice(album, "CD"), Is.False);
            Assert.That(SceneRules.FilesQualityTag(album), Is.EqualTo("16BIT"), "read from the files; 16 bit has no kHz");
            Assert.That(SceneRules.QualityTags(album).First(), Is.EqualTo("16BIT"));

            // Auto: nothing added to the name
            release.Quality = "";
            Assert.That(new AlbumFinisher(profile, album, release, "#Artist\r\n").Plan().NewDirectory, Does.EndWith("Ween-Pure_Guava-BLURAY-FLAC-1994-GRP"));

            // 16 bit is only 16BIT (no kHz); the name must match the files
            release.Quality = "16BIT";
            FinishPlan plan = new AlbumFinisher(profile, album, release, "#Artist\r\n").Plan();
            Assert.That(plan.NewDirectory, Does.EndWith("Ween-Pure_Guava-16BIT-BLURAY-FLAC-1994-GRP"));
            Assert.That(plan.RuleIssues, Has.None.Contains("says"));

            release.Quality = "24BIT-96KHZ";
            plan = new AlbumFinisher(profile, album, release, "#Artist\r\n").Plan();
            Assert.That(plan.RuleIssues, Has.Some.EqualTo("FLAC 3.4.2: the release name says 24BIT but the files are 16BIT."));
            Assert.That(plan.RuleIssues, Has.Some.EqualTo("FLAC 3.4.2: the release name says 96KHZ but the files are 44KHZ."));

            release.Quality = "16BIT-44KHZ";
            plan = new AlbumFinisher(profile, album, release, "#Artist\r\n").Plan();
            Assert.That(plan.RuleIssues, Has.Some.EqualTo("FLAC 3.4.2: 16BIT has no kHz tag; use -16BIT- instead of -16BIT-44KHZ-."));
        }

        [Test]
        public void TypesDropBitKhzAndFixEntries()
        {
            ReleaseListItem[] types = new[] { "Normal release", "Remastered", "Remastered-24BiT/44KhZ", "Remastered-24BiT/96KhZ",
                                              "Deluxe Edition-24BIT/48KHZ", "16BIT", "24BIT/96KHZ", "Limited Edition-16BIT", "Promo",
                                              "REPACK", "DIRFIX", "NFOFIX", "DIRFIX Reissue OST", "Reissue OST", "Proper", "24/96", "24/192", "Deluxe Edition" }
                .Select(t => new ReleaseListItem { Nfo = t, Dir = t == "Normal release" ? "" : t }).ToArray();

            Assert.That(TagAlbumViewModel.TypesFor(types).Select(t => t.Nfo), Is.EqualTo(new[] { "Normal release", "Remastered", "Promo", "Reissue OST", "Proper", "Deluxe Edition" }));
        }

        [TestCase("<Artist>-<Album>-<Source>-FLAC-<Year>-GRP", "WEB-FLAC", "REPACK", "Ween-Pure_Guava-REPACK-16BIT-WEB-FLAC-1994-GRP", "01-ween-little_birdy-grp-repack.flac")]
        [TestCase("<Artist>-<Album>-<Source>-FLAC-<Year>-GRP", "CD", "DIRFIX", "Ween-Pure_Guava-DIRFIX-CD-FLAC-1994-GRP", "01-ween-little_birdy-grp.flac")]
        [TestCase("<Artist>-<Album>-<Source>-FLAC-<Year>-GRP", "VINYL", "RERIP", "Ween-Pure_Guava-RERIP-VINYL-FLAC-1994-GRP", "01-ween-little_birdy-grp.flac")]
        [TestCase("<Artist>-<Album>-<Fix>-<Source>-FLAC-<Year>-GRP", "CD", "NFOFIX", "Ween-Pure_Guava-NFOFIX-CD-FLAC-1994-GRP", "01-ween-little_birdy-grp.flac")]
        [TestCase("<Artist>-<Album>-<Source>-FLAC-<Year>-GRP", "CD", "", "Ween-Pure_Guava-CD-FLAC-1994-GRP", "01-ween-little_birdy-grp.flac")]
        public void FixGoesBeforeTheSource(string pattern, string source, string fix, string folder, string file)
        {
            string dir = MakeFolder("fix " + source + fix + " (src)", "untagged.flac", "01.flac");
            Album album = Load(dir, "Ween", "Pure Guava", "Little Birdy");
            UserProfile profile = Profile();
            profile.FileNaming.SingleCD.SingleArtist.Directory = pattern;
            if (profile.FileNaming.Vinyl == null)
                profile.FileNaming.Vinyl = new NamingFormatGroup();
            profile.FileNaming.Vinyl.SingleArtist = profile.FileNaming.SingleCD.SingleArtist;
            ReleaseInfo release = Release();
            release.Source = new ReleaseListItem { Nfo = source, Dir = source };
            release.Fix = fix;
            var namer = new AlbumNamer(profile, album, release);
            Assert.That(namer.GetFolderName(), Is.EqualTo(folder));
            Assert.That(namer.GetTrackFileName(album.Tracks[0]), Is.EqualTo(file));

            var release2 = new SceneRelease { Name = folder, Source = source, Album = album, Files = new System.Collections.Generic.List<string> { Path.Combine(folder, "01-ween-little_birdy-grp.flac") } };
            bool flagged = SceneRules.Check(SceneRuleSet.Flac2016, release2).Any(i => i.StartsWith("FLAC 5.6"));
            Assert.That(flagged, Is.EqualTo(fix == "REPACK"));
        }

        [Test]
        public void SceneTaggingCanBeTurnedOffInOptionsJson()
        {
            // options.json: "enabled": "true" / "false", under EncodePreset
            string json = CDTag.Common.Json.JsonSerializer.SerializeObject(new CDTag.Common.Settings.AppOptionsData { EncodePreset = "LameV0" });
            Assert.That(json, Does.Contain("\"EncodePreset\": \"LameV0\",").And.Contains("\"enabled\": \"true\""));
            Assert.That(json.IndexOf("\"enabled\""), Is.GreaterThan(json.IndexOf("\"EncodePreset\"")));
            Assert.That(json, Does.Not.Contain("IsSceneTagging"));
            Assert.That(System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("enabled").GetString(), Is.EqualTo("true"), "valid JSON");
            var off = CDTag.Common.Json.JsonSerializer.ReadObject<CDTag.Common.Settings.AppOptionsData>("{ \"EncodePreset\": \"LameV0\", \"enabled\": \"false\" }");
            Assert.That(off.IsSceneTagging, Is.False);
            var missing = CDTag.Common.Json.JsonSerializer.ReadObject<CDTag.Common.Settings.AppOptionsData>("{ \"EncodePreset\": \"LameV0\" }");
            Assert.That(missing.IsSceneTagging, Is.True, "missing = on");

            // Off: no bit depth, fix or -repack in names, and no scene rules check
            string dir = MakeFolder("scene off (src)", "untagged.flac", "01.flac");
            Album album = Load(dir, "Ween", "Pure Guava", "Little Birdy");
            UserProfile profile = Profile();
            profile.FileNaming.SingleCD.SingleArtist.Directory = "<Artist>-<Album>-<Source>-FLAC-<Year>-GRP";
            ReleaseInfo release = Release();
            release.Source = new ReleaseListItem { Nfo = "WEB-FLAC", Dir = "WEB-FLAC" };
            release.Fix = "REPACK";
            CDTag.Common.Settings.AppOptionsData options = CDTag.Common.Settings.AppOptions.Current;
            string before = options.SceneTaggingEnabled;
            try
            {
                options.SceneTaggingEnabled = "false";
                var namer = new AlbumNamer(profile, album, release);
                Assert.That(namer.GetFolderName(), Is.EqualTo("Ween-Pure_Guava-WEB-FLAC-1994-GRP"));
                Assert.That(namer.GetTrackFileName(album.Tracks[0]), Is.EqualTo("01-ween-little_birdy-grp.flac"));
                FinishPlan plan = new AlbumFinisher(profile, album, release, "#Artist\r\n").Plan();
                Assert.That(plan.RuleIssues, Is.Empty);
                Assert.That(plan.RuleSetName, Is.Null);

                options.SceneTaggingEnabled = "true";
                Assert.That(new AlbumNamer(profile, album, release).GetFolderName(), Is.EqualTo("Ween-Pure_Guava-REPACK-16BIT-WEB-FLAC-1994-GRP"));
            }
            finally
            {
                options.SceneTaggingEnabled = before;
            }
        }

        [Test]
        public void Mp3FixGoesBeforeTheSourceOrYear()
        {
            var album = new Album(@"C:\in", new[] { AlbumTrack.FromValues(@"C:\in\01.mp3", "1", "1", "Ween", "Little Birdy") }) { Artist = "Ween", AlbumTitle = "Pure Guava", ReleaseDate = "1994" };
            UserProfile profile = Profile();
            profile.FileNaming.SingleCD.SingleArtist.Directory = "<Artist>-<Album>-<Source>-<Year>-GRP";
            ReleaseInfo release = Release();
            release.Source = new ReleaseListItem { Nfo = "WEB", Dir = "WEB" };
            release.Fix = "REPACK";
            Assert.That(new AlbumNamer(profile, album, release).GetFolderName(), Is.EqualTo("Ween-Pure_Guava-REPACK-WEB-1994-GRP"));
            Assert.That(new AlbumNamer(profile, album, release).GetTrackFileName(album.Tracks[0]), Does.Not.Contain("repack"));
        }
        [Test]
        public void WebFlacAlbumsOfferOnlyWebFlacAsTheWebSource()
        {
            string folder = MakeFolder("sources (src)", "untagged.flac", "01.flac");
            Album flac = Load(folder, "Ween", "Pure Guava", "Little Birdy");
            ReleaseListItem[] sources = new[] { "CD", "VINYL", "WEB", "WEB-FLAC" }.Select(s => new ReleaseListItem { Nfo = s, Dir = s }).ToArray();

            Assert.That(TagAlbumViewModel.SourcesFor(sources, flac).Select(s => s.Dir), Is.EqualTo(new[] { "CD", "VINYL", "WEB-FLAC" }));

            var mp3 = new Album(@"C:\in", new[] { AlbumTrack.FromValues(@"C:\in\01.mp3", "1", "1", "A", "T") });
            Assert.That(TagAlbumViewModel.SourcesFor(sources, mp3).Select(s => s.Dir), Is.EqualTo(new[] { "CD", "VINYL", "WEB" }));
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

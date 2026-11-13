using System.Collections.Generic;
using System.Linq;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Profile;
using CDTag.Model.Tag;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>Profile-based naming, checked against CD-Tag 2.x behaviour. Synthetic profile, no real group data.</summary>
    [TestFixture]
    public class AlbumNamerTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            IoC.ClearAllRegistrations();
            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());
        }

        private static readonly ReleaseListItem German = new ReleaseListItem { Nfo = "German", Dir = "DE" };
        private static readonly ReleaseListItem English = new ReleaseListItem { Nfo = "English", Dir = "" };
        private static readonly ReleaseListItem Normal = new ReleaseListItem { Nfo = "Normal release", Dir = "" };
        private static readonly ReleaseListItem Promo = new ReleaseListItem { Nfo = "Promo", Dir = "Promo" };
        private static readonly ReleaseListItem Cd = new ReleaseListItem { Nfo = "CD", Dir = "CD" };

        private static UserProfile Profile(string dir, string audio, string multiDir = null, string multiAudio = null)
        {
            var profile = new UserProfile { ProfileName = "GRP" };
            profile.FileNaming.SingleCD.SingleArtist.Directory = dir;
            profile.FileNaming.SingleCD.SingleArtist.AudioFile = audio;
            profile.FileNaming.SingleCD.SingleArtist.NFO = "<LC>00-<Artist>-<Album>-<Year></LC>-grp";
            profile.FileNaming.SingleCD.VariousArtists.Directory = dir;
            profile.FileNaming.SingleCD.VariousArtists.AudioFile = audio;
            profile.FileNaming.MultiCD.SingleArtist.Directory = multiDir ?? dir;
            profile.FileNaming.MultiCD.SingleArtist.AudioFile = multiAudio ?? audio;
            profile.FileNaming.FileCharacterLimit = 128;
            profile.FileNaming.DirectoryCharacterLimit = 128;
            profile.FileNaming.UseStandardCharactersOnly = true;
            profile.FileNaming.UseLatinCharactersOnly = true;
            profile.FileNaming.DontChangeCaseWordsWithAllCaps = true;
            profile.FileNaming.DontChangeCaseWordsWithNonLetter = true;
            profile.NamingRules = new NamingRules
            {
                Replacements = new[]
                {
                    new ReplacePair { Find = " ", Replace = "_" },
                    new ReplacePair { Find = "--", Replace = "-" },
                    new ReplacePair { Find = "/", Replace = "-" },
                    new ReplacePair { Find = "&", Replace = "and" },
                    new ReplacePair { Find = "Volume 2", Replace = "Vol 2" },
                    new ReplacePair { Find = "_-_", Replace = "-" },
                    new ReplacePair { Find = "__", Replace = "_" }
                },
                LowercaseWords = new[] { "and", "of", "feat" },
                WordSeparators = new[] { "_", " " },
                FieldSeparators = new[] { "-" }
            };
            profile.ExtInfo = new[]
            {
                new ExtInfoDefinition { Begin = "-", FieldSeparator = "", End = "", Items = System.Array.Empty<ExtInfoItem>() },
                new ExtInfoDefinition { Begin = "-", FieldSeparator = "-", End = "", Items = new[] { new ExtInfoItem { Field = "Language 2 letter", Format = "<Value>" } } },
                new ExtInfoDefinition { Begin = "-", FieldSeparator = "-", End = "", Items = new[] { new ExtInfoItem { Field = "Release Type", Format = "<Value>" } } },
                new ExtInfoDefinition { Begin = "", FieldSeparator = "-", End = "", Items = new[] { new ExtInfoItem { Field = "Source", Format = "<Value>" } } }
            };
            return profile;
        }

        private static Album Album(string artist, string album, int trackCount, int discs = 1, string folder = @"C:\music\in")
        {
            var tracks = new List<AlbumTrack>();
            int perDisc = (trackCount + discs - 1) / discs;
            for (int i = 1; i <= trackCount; i++)
            {
                int disc = (i - 1) / perDisc + 1;
                int number = discs > 1 ? (i - 1) % perDisc + 1 : i;
                tracks.Add(AlbumTrack.FromValues(folder + @"\" + i + ".flac", disc.ToString(), number.ToString(), artist, i == 1 ? "Koko" : "Song " + i, 900, 180));
            }
            var result = new Album(folder, tracks) { Artist = artist, AlbumTitle = album, ReleaseDate = "2014" };
            return result;
        }

        private static ReleaseInfo Release(ReleaseListItem language = null, ReleaseListItem type = null, ReleaseListItem source = null)
        {
            return new ReleaseInfo { RetailDate = "2014-00-00", Language = language ?? English, ReleaseType = type ?? Normal, Source = source ?? Cd };
        }

        [Test]
        public void SingleCdFolderAndFileNamesMatchCdTag2()
        {
            var profile = Profile("<SC><Artist>-<Album><ExtInfo><ExtInfo2>-<Year></SC>-GRP", "<LC><Track>-<Artist>-<Song></LC>-grp");
            var album = Album("Ween", "Boognish Provides", 14);
            var namer = new AlbumNamer(profile, album, Release());

            Assert.That(namer.GetFolderName(), Is.EqualTo("Ween-Boognish_Provides-2014-GRP"));
            Assert.That(namer.GetTrackFileName(album.Tracks[0]), Is.EqualTo("01-ween-koko-grp.flac"));
            Assert.That(namer.GetFileName(NameKind.NFO), Is.EqualTo("00-ween-boognish_provides-2014-grp.nfo"));
        }

        [Test]
        public void RetaggingAnImageKeepsOneEnding()
        {
            var profile = Profile("<Artist>", "<LC><Disc><Track>-<Artist>-<Song></LC>-grp");
            profile.FileNaming.MultiCD.SingleArtist.Images = "<LC>0000-<Artist>-<Album>-<Year>-<ImageText></LC>-grp";
            profile.FileNaming.FileCharacterLimit = 100;
            var album = Album("Pink Floyd", "A Momentary Lapse of Reason The High Resolution Remasters", 8, 4);
            var namer = new AlbumNamer(profile, album, Release());
            string head, tail;
            Assert.That(namer.TryGetImageNameParts(".jpg", out head, out tail), Is.True);
            Assert.That(head, Is.EqualTo("000-pink_floyd-a_momentary_lapse_of_reason_the_high_resolution_remasters-2014-"));
            Assert.That(tail, Is.EqualTo("-grp"));

            // Finish three times: the name stays the same, with one -grp.
            string first = namer.GetFileName(NameKind.Images, "A Momentary Lapse of Reason - The High Resolution Remasters - CD 1 and 2", ".jpg");
            string name = first;
            for (int i = 0; i < 3; i++)
            {
                string text = AlbumFinisher.StripImageName(System.IO.Path.GetFileNameWithoutExtension(name), head, tail);
                name = namer.GetFileName(NameKind.Images, text, ".jpg");
            }
            Assert.That(name.Length, Is.LessThanOrEqualTo(100));
            Assert.That(name, Does.EndWith("cd_1_and_2-grp.jpg"));
            Assert.That(name, Does.Not.Contain("-grp-grp"));
            Assert.That(name, Does.Not.Contain("_-_"), "a \" - \" in the text becomes just -");
            Assert.That(name, Does.StartWith("000-pink_floyd-"));
            string again = namer.GetFileName(NameKind.Images, AlbumFinisher.StripImageName(System.IO.Path.GetFileNameWithoutExtension(name), head, tail), ".jpg");
            Assert.That(again, Is.EqualTo(name));

            // The name from the earlier versions (-grp-grp-grp, 000/0000) is cleaned up too.
            string broken = "000-pink_floyd-a_momentary_lapse_of_reason_the_high_resolutio-ason_-_the_high_resolution_remasters_-_cd_1_and_2-grp-grp-grp";
            Assert.That(AlbumFinisher.StripImageName(broken, head, tail), Is.EqualTo("ason_-_the_high_resolution_remasters_-_cd_1_and_2"));
            Assert.That(AlbumFinisher.StripImageName("0000-pink_floyd-a_momentary_lapse_of_reason_the_high_resolution_remasters-2014-front-grp", head, tail), Is.EqualTo("front"));
            // Images that were never renamed keep their name.
            Assert.That(AlbumFinisher.StripImageName("Back Cover", head, tail), Is.EqualTo("Back Cover"));
        }

        [Test]
        public void UpToNineDiscsTheNfoPlaylistChecksumAndImagesGet000()
        {
            var profile = Profile("<Artist>", "<LC><Disc><Track>-<Artist>-<Song></LC>-grp");
            foreach (NamingFormat format in new[] { profile.FileNaming.MultiCD.SingleArtist })
            {
                format.NFO = format.Checksum = "<LC>0000-<Artist>-<Album>-<Discs>CD-<Year></LC>-grp";
                format.Playlist = "<LC>0000-<Artist>-<Album>-<Year></LC>-grp";
                format.Images = "<LC>0000-<Artist>-<Album>-<Year>-<ImageText></LC>-grp";
            }

            var nine = new AlbumNamer(profile, Album("Ween", "Live", 18, 9), Release());
            var ten = new AlbumNamer(profile, Album("Ween", "Live", 20, 10), Release());
            Assert.That(nine.GetFileName(NameKind.NFO), Is.EqualTo("000-ween-live-9cd-2014-grp.nfo"));
            Assert.That(nine.GetFileName(NameKind.Checksum), Is.EqualTo("000-ween-live-9cd-2014-grp.sfv"));
            Assert.That(nine.GetFileName(NameKind.Playlist), Is.EqualTo("000-ween-live-2014-grp.m3u"));
            Assert.That(nine.GetFileName(NameKind.Images, "front", ".jpg"), Is.EqualTo("000-ween-live-2014-front-grp.jpg"));
            Assert.That(ten.GetFileName(NameKind.Images, "front", ".jpg"), Is.EqualTo("0000-ween-live-2014-front-grp.jpg"));

            profile.FileNaming.MultiCD.SingleArtist.Playlist = "<LC><Disc>00-<Artist>-<Album>-<Year></LC>-grp";
            Assert.That(nine.GetFileName(NameKind.Playlist, null, null, 3), Is.EqualTo("300-ween-live-2014-grp.m3u"));
            Assert.That(nine.GetFileName(NameKind.Playlist, null, null, AlbumNamer.AllDiscs), Is.EqualTo("000-ween-live-2014-grp.m3u"));
            Assert.That(ten.GetFileName(NameKind.Playlist, null, null, 3), Is.EqualTo("0300-ween-live-2014-grp.m3u"));
            Assert.That(ten.GetFileName(NameKind.Playlist, null, null, AlbumNamer.AllDiscs), Is.EqualTo("0000-ween-live-2014-grp.m3u"));
            Assert.That(ten.GetFileName(NameKind.NFO), Is.EqualTo("0000-ween-live-10cd-2014-grp.nfo"));
            Assert.That(ten.GetFileName(NameKind.Checksum), Is.EqualTo("0000-ween-live-10cd-2014-grp.sfv"));
        }

        [Test]
        public void LongImageNamesKeepTheImageText()
        {
            var profile = Profile("<Artist>", "<LC><Track>-<Artist>-<Song></LC>-grp");
            profile.FileNaming.SingleCD.SingleArtist.Images = "<LC>0000-<Artist>-<Album>-<Year>-<ImageText></LC>";
            profile.FileNaming.FileCharacterLimit = 64;
            var album = Album("Pink Floyd", "A Momentary Lapse of Reason The High Resolution Remasters", 10);
            var namer = new AlbumNamer(profile, album, Release());

            string front = namer.GetFileName(NameKind.Images, "a_momentary_lapse_of_reason-the_high_resolution_remasters-front", ".jpg");
            string back = namer.GetFileName(NameKind.Images, "a_momentary_lapse_of_reason-the_high_resolution_remasters-back", ".jpg");
            Assert.That(front, Is.Not.EqualTo(back));
            Assert.That(front.Length, Is.LessThanOrEqualTo(64));
            Assert.That(back.Length, Is.LessThanOrEqualTo(64));
            Assert.That(front, Does.StartWith("0000-pink_floyd-").And.EndWith("-front.jpg"));
            Assert.That(back, Does.EndWith("-back.jpg"));

            string cover = namer.GetFileName(NameKind.Images, "front", ".jpg");
            Assert.That(cover, Does.EndWith("-front.jpg"));
            Assert.That(cover, Does.StartWith("0000-pink_floyd-"));
            Assert.That(cover.Length, Is.LessThanOrEqualTo(64));

            // Room enough: nothing is shortened.
            profile.FileNaming.FileCharacterLimit = 200;
            Assert.That(new AlbumNamer(profile, album, Release()).GetFileName(NameKind.Images, "front", ".jpg"),
                        Is.EqualTo("0000-pink_floyd-a_momentary_lapse_of_reason_the_high_resolution_remasters-2014-front.jpg"));
        }

        [Test]
        public void TrackNumbersGetThreeDigitsWhenMoreThan99Tracks()
        {
            var profile = Profile("<Artist>", "<LC><Track>-<Artist>-<Song></LC>-grp");
            var album = Album("Ween", "Boognish Provides", 120);
            Assert.That(new AlbumNamer(profile, album, Release()).GetTrackFileName(album.Tracks[0]), Is.EqualTo("001-ween-koko-grp.flac"));
        }

        [Test]
        public void ExtInfoUsesDirValuesAndOnlyAppearsWhenFilled()
        {
            var profile = Profile("<SC><Artist>-<Album><ExtInfo><ExtInfo2><ExtInfo3>-<ExtInfo4>-FLAC-<Year></SC>-GRP", "<Track>");
            var album = Album("Ween", "Boognish Provides", 3);

            Assert.That(new AlbumNamer(profile, album, Release()).GetFolderName(), Is.EqualTo("Ween-Boognish_Provides-CD-FLAC-2014-GRP"));
            Assert.That(new AlbumNamer(profile, album, Release(German, Promo)).GetFolderName(), Is.EqualTo("Ween-Boognish_Provides-DE-Promo-CD-FLAC-2014-GRP"));
        }

        [Test]
        public void SmartCaseRulesAndCharacterCleanup()
        {
            var profile = Profile("<SC><Artist>-<Album>-<Year></SC>-GRP", "<Track>");

            Assert.That(new AlbumNamer(profile, Album("the art of noise", "and justice & friends of KMFDM", 1), Release()).GetFolderName(),
                        Is.EqualTo("The_Art_of_Noise-And_Justice_and_Friends_of_KMFDM-2014-GRP"));
            Assert.That(new AlbumNamer(profile, Album("Björk", "Medúlla: Volume 2", 1), Release()).GetFolderName(),
                        Is.EqualTo("Bjork-Medulla_Vol_2-2014-GRP"));
            Assert.That(new AlbumNamer(profile, Album("Guns N' Roses", "Don't Cry", 1), Release()).GetFolderName(),
                        Is.EqualTo("Guns_N_Roses-Dont_Cry-2014-GRP"));
        }

        [Test]
        public void LengthLimitKeepsTheSuffix()
        {
            var profile = Profile("<Artist>-<Album><Suffix>-<Year>-GRP", "<Track>");
            profile.FileNaming.DirectoryCharacterLimit = 30;
            string name = new AlbumNamer(profile, Album("Artist", "A Very Long Album Title That Goes On", 1), Release()).GetFolderName();

            Assert.That(name.Length, Is.LessThanOrEqualTo(30));
            Assert.That(name, Does.EndWith("-2014-GRP"));
            Assert.That(name, Does.StartWith("Artist-A_Very"));
        }

        [Test]
        public void MultiDiscAlbumsUseTheMultiCdFormats()
        {
            var profile = Profile("<SC><Artist>-<Album>-<Year></SC>-GRP", "<LC><Track>-<Song></LC>",
                                  multiDir: "<SC><Artist>-<Album>-<Discs>CD-<Year></SC>-GRP", multiAudio: "<LC><Disc><Track>-<Artist>-<Song></LC>-grp");
            var album = Album("Ween", "Boognish Provides", 28, discs: 2);
            var namer = new AlbumNamer(profile, album, Release());

            Assert.That(namer.Discs, Is.EqualTo(2));
            Assert.That(namer.GetFolderName(), Is.EqualTo("Ween-Boognish_Provides-2CD-2014-GRP"));
            Assert.That(namer.GetTrackFileName(album.Tracks[0]), Is.EqualTo("101-ween-koko-grp.flac"));
            Assert.That(namer.GetTrackFileName(album.Tracks.Last()), Is.EqualTo("214-ween-song_28-grp.flac"));
        }
    }
}

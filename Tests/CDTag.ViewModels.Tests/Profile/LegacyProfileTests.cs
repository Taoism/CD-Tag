using System;
using System.IO;
using System.Text;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Profile;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace CDTag.ViewModel.Tests.Profile
{
    /// <summary>Converting CD-Tag 2.x profiles. Uses a synthetic profile (no real group profiles in the repo).</summary>
    [TestFixture]
    public class LegacyProfileTests
    {
        private const string SampleProfile =
            "[CONFIG]\r\nRevision=13\r\n\r\n" +
            "[NAME0]\r\nDIR=<SC><Artist>-<Album>-<Year></SC>-GRP\r\nMP3=<LC><Track>-<Artist>-<Song></LC>-grp\r\nNFO=<LC>00-<Artist>-<Album></LC>-grp\r\nJPG=<LC>00-<Artist>-<ImageText></LC>-grp\r\n\r\n" +
            "[NAME2]\r\nDIR=<SC><Artist>-<Album>-<Discs>CD-<Year></SC>-GRP\r\nMP3=<LC><Disc><Track>-<Artist>-<Song></LC>-grp\r\n\r\n" +
            "[NAME5]\r\nDIR=<Artist> - <Album> (Vinyl)\r\n\r\n" +
            "[NAMEOPT]\r\nCharLimit=128\r\nStdOnly=1\r\nForeign=0\r\nDirCharLimit=100\r\n\r\n" +
            "[SMARTCASE]\r\nAlpha=1\r\nCaps=0\r\n\r\n" +
            "[NFO_OPTIONS]\r\nShowReleaseScreen=1\r\nTemplate=GRP.nfo\r\nShowAvgVBR=0\r\nMultiCDHeader=Disc #Disc/#Discs&nl;\r\nDecChar=,\r\nAutoPreviewNFO=1\r\n\r\n" +
            "[TAG_PROCESS]\r\nRenameDIR=1\r\nRenameMP3=1\r\nRenameCUE=0\r\nMakeM3U=1\r\nMakeSFV=1\r\nMakeNFO=1\r\nRenameNFO=0\r\nDeleteOld=1\r\nRemoveEmptyDirs=0\r\nRenameOrMakeNFO=0\r\n\r\n" +
            "[TAGGING]\r\nID3V2=WriteAlways\r\nID3V1=WriteIfExists\r\nLongTRCK=1\r\nFLAC_ID3V2=Remove\r\nFLAC_ID3V1=Remove\r\nSeparateM3U=1\r\n\r\n" +
            "[TAG_ID3V2]\r\nNoWriteID3v2Comments=1\r\n\r\n" +
            "[SFVMD5]\r\nHideGeneratedBy=1\r\nHideFileInfo=0\r\n";

        private string _directory;

        [OneTimeSetUp]
        public void Setup()
        {
            IoC.ClearAllRegistrations();
            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());
        }

        [SetUp]
        public void CreateDirectory()
        {
            _directory = Path.Combine(Path.GetTempPath(), "cdtag-legacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void DeleteDirectory()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private string WriteSample(string fileName, bool withTemplate)
        {
            string path = Path.Combine(_directory, "source", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, SampleProfile, Encoding.Latin1);
            if (withTemplate)
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(path), "GRP.nfo"), new byte[] { 0xDB, 0xDB, 0x0D, 0x0A });
            return path;
        }

        [Test]
        public void DetectsLegacyAndJsonProfiles()
        {
            string legacy = WriteSample("GRP.cfg", withTemplate: false);
            string json = Path.Combine(_directory, "new.cfg");
            File.WriteAllText(json, "{\n  \"Revision\": 1\n}");

            Assert.That(LegacyProfile.IsLegacyFile(legacy), Is.True);
            Assert.That(LegacyProfile.IsLegacyFile(json), Is.False);
        }

        [Test]
        public void ConvertsNamingNfoAndFinishSettings()
        {
            string path = WriteSample("GRP.cfg", withTemplate: true);

            string template;
            UserProfile profile = LegacyProfile.Convert(path, "GRP", out template);

            Assert.That(profile.ProfileName, Is.EqualTo("GRP"));
            Assert.That(profile.FileNaming.SingleCD.SingleArtist.Directory, Is.EqualTo("<SC><Artist>-<Album>-<Year></SC>-GRP"));
            Assert.That(profile.FileNaming.SingleCD.SingleArtist.AudioFile, Is.EqualTo("<LC><Track>-<Artist>-<Song></LC>-grp"));
            Assert.That(profile.FileNaming.SingleCD.SingleArtist.Images, Is.EqualTo("<LC>00-<Artist>-<ImageText></LC>-grp"));
            Assert.That(profile.FileNaming.SingleCD.SingleArtist.CUE, Is.Null);
            Assert.That(profile.FileNaming.MultiCD.SingleArtist.AudioFile, Is.EqualTo("<LC><Disc><Track>-<Artist>-<Song></LC>-grp"));
            Assert.That(profile.FileNaming.Vinyl.VariousArtists.Directory, Is.EqualTo("<Artist> - <Album> (Vinyl)"));

            Assert.That(profile.FileNaming.FileCharacterLimit, Is.EqualTo(128));
            Assert.That(profile.FileNaming.DirectoryCharacterLimit, Is.EqualTo(100));
            Assert.That(profile.FileNaming.UseStandardCharactersOnly, Is.True);
            Assert.That(profile.FileNaming.UseLatinCharactersOnly, Is.False);
            Assert.That(profile.FileNaming.DontChangeCaseWordsWithNonLetter, Is.True);
            Assert.That(profile.FileNaming.DontChangeCaseWordsWithAllCaps, Is.False);

            Assert.That(profile.NFOOptions.ShowReleaseScreen, Is.True);
            Assert.That(profile.NFOOptions.UseAverageBitrate, Is.False);
            Assert.That(profile.NFOOptions.MultiCDHeader, Is.EqualTo("Disc #Disc/#Discs" + Environment.NewLine));
            Assert.That(profile.NFOOptions.DecimalCharacter, Is.EqualTo(","));
            Assert.That(profile.NFOOptions.AutomaticallyPreviewNFO, Is.True);
            Assert.That(template, Is.EqualTo(Path.Combine(Path.GetDirectoryName(path), "GRP.nfo")));

            Assert.That(profile.Finish.RenameDirectory, Is.True);
            Assert.That(profile.Finish.RenameCUEFiles, Is.False);
            Assert.That(profile.Finish.CreatePlaylist, Is.True);
            Assert.That(profile.Finish.DeleteEmptySubdirectories, Is.False);
            Assert.That(profile.Finish.NFO, Is.EqualTo(FinishNFO.CreateNew));

            Assert.That(profile.Tagging.Id3v2, Is.EqualTo(TagAction.WriteAlways));
            Assert.That(profile.Tagging.Id3v1, Is.EqualTo(TagAction.WriteIfExists));
            Assert.That(profile.Tagging.FlacId3v2, Is.EqualTo(TagAction.Remove));
            Assert.That(profile.Tagging.LongTrackNumbers, Is.True);
            Assert.That(profile.Tagging.SeparatePlaylistPerDisc, Is.True);
            Assert.That(profile.Tagging.NoId3v2Comment, Is.True);
            Assert.That(profile.Tagging.ChecksumHideGeneratedBy, Is.True);
            Assert.That(profile.Tagging.ChecksumHideFileInfo, Is.False);
        }

        [Test]
        public void ImportWritesProfileTemplateAndBackup()
        {
            string path = WriteSample("GRP.cfg", withTemplate: true);
            string profiles = Path.Combine(_directory, "Profiles");
            Directory.CreateDirectory(profiles);

            UserProfile profile = LegacyProfile.Import(path, profiles, "GRP");

            Assert.That(profile.NFOOptions.TemplatePath, Is.EqualTo("GRP.nfo"));
            Assert.That(File.Exists(Path.Combine(profiles, "GRP.nfo")), Is.True);

            string saved = Path.Combine(profiles, "GRP.cfg");
            Assert.That(LegacyProfile.IsLegacyFile(saved), Is.True, "the imported profile stays a CD-Tag 2.x profile");
            Assert.That(File.ReadAllText(saved, LegacyProfile.DosEncoding), Does.Contain("MultiCDHeader=Disc #Disc/#Discs&nl;\r\n"));

            UserProfile reloaded = UserProfile.Load(saved);
            Assert.That(reloaded.FileNaming.SingleCD.SingleArtist.Directory, Is.EqualTo("<SC><Artist>-<Album>-<Year></SC>-GRP"));
            Assert.That(reloaded.Finish.NFO, Is.EqualTo(FinishNFO.CreateNew));
        }

        [Test]
        public void ImportWithoutTemplateLeavesTemplateEmpty()
        {
            string path = WriteSample("GRP.cfg", withTemplate: false);
            string profiles = Path.Combine(_directory, "Profiles");
            Directory.CreateDirectory(profiles);

            UserProfile profile = LegacyProfile.Import(path, profiles, "GRP");

            Assert.That(profile.NFOOptions.TemplatePath, Is.Null);
        }

        [Test]
        public void ExportRoundTripsAndKeepsUnusedTwoXSettings()
        {
            string source = WriteSample("GRP.cfg", withTemplate: true);
            // A 2.x setting this version doesn't use
            File.AppendAllText(source, "\r\n[TAGGING2]\r\nAPE_APEV2=Remove\r\n", Encoding.Latin1);
            string profiles = Path.Combine(_directory, "Profiles");
            Directory.CreateDirectory(profiles);
            UserProfile profile = LegacyProfile.Import(source, profiles, "GRP");

            // Edited in this version
            profile.FileNaming.SingleCD.SingleArtist.Directory = "<SC><Artist>-<Album>-WEB-<Year></SC>-GRP";
            profile.NFOOptions.SingleCDHeader = "Tracks" + Environment.NewLine;
            profile.Tagging.Id3v1 = TagAction.Remove;
            profile.ReleaseInfo = new ReleaseInfoOptions { Sources = new[] { new ReleaseListItem { Nfo = "CD", Dir = "CD" }, new ReleaseListItem { Nfo = "WEB", Dir = "WEB" } } };

            string export = Path.Combine(_directory, "old cdtag");
            var written = LegacyProfileExport.Export(profile, profiles, export);

            string cfg = Path.Combine(export, "GRP.cfg");
            Assert.That(written, Is.EqualTo(new[] { cfg, Path.Combine(export, "GRP.nfo") }));
            Assert.That(LegacyProfile.IsLegacyFile(cfg), Is.True);
            string text = File.ReadAllText(cfg, LegacyProfile.DosEncoding);
            Assert.That(text, Does.Contain("[TAGGING2]\r\nAPE_APEV2=Remove\r\n"), "2.x settings this version doesn't use come back");
            Assert.That(text, Does.Contain("Template=GRP.nfo\r\n"));
            Assert.That(text, Does.Contain("SingleCDHeader=Tracks&nl;\r\n"));
            Assert.That(text, Does.Contain("[SOURCENFO_LIST]\r\n1=CD\r\n2=WEB\r\n"));

            string template;
            UserProfile back = LegacyProfile.Convert(cfg, "GRP", out template);
            Assert.That(template, Is.EqualTo(Path.Combine(export, "GRP.nfo")));
            Assert.That(back.FileNaming.SingleCD.SingleArtist.Directory, Is.EqualTo("<SC><Artist>-<Album>-WEB-<Year></SC>-GRP"));
            Assert.That(back.FileNaming.MultiCD.SingleArtist.AudioFile, Is.EqualTo("<LC><Disc><Track>-<Artist>-<Song></LC>-grp"));
            Assert.That(back.FileNaming.DirectoryCharacterLimit, Is.EqualTo(100));
            Assert.That(back.NFOOptions.MultiCDHeader, Is.EqualTo("Disc #Disc/#Discs" + Environment.NewLine));
            Assert.That(back.NFOOptions.DecimalCharacter, Is.EqualTo(","));
            Assert.That(back.Tagging.Id3v1, Is.EqualTo(TagAction.Remove));
            Assert.That(back.Tagging.Id3v2, Is.EqualTo(TagAction.WriteAlways));
            Assert.That(back.Finish.NFO, Is.EqualTo(FinishNFO.CreateNew));
            Assert.That(back.ReleaseInfo.Sources.Length, Is.EqualTo(2));
        }

        [Test]
        public void ExportsAProfileMadeInThisVersion()
        {
            var profile = new UserProfile { ProfileName = "NEW" };
            profile.FileNaming.SingleCD.SingleArtist.Directory = "<Artist>-<Album>-<Year>-NEW";
            profile.Finish.NFO = FinishNFO.RenameIfExistsElseCreate;

            string export = Path.Combine(_directory, "old cdtag");
            LegacyProfileExport.Export(profile, null, export);

            string template;
            UserProfile back = LegacyProfile.Convert(Path.Combine(export, "NEW.cfg"), "NEW", out template);
            Assert.That(template, Is.Null);
            Assert.That(back.FileNaming.SingleCD.SingleArtist.Directory, Is.EqualTo("<Artist>-<Album>-<Year>-NEW"));
            Assert.That(back.Finish.NFO, Is.EqualTo(FinishNFO.RenameIfExistsElseCreate));
            Assert.That(File.ReadAllText(Path.Combine(export, "NEW.cfg"), LegacyProfile.DosEncoding), Does.StartWith("[CONFIG]\r\nRevision=13\r\n"));
        }

        [Test]
        public void LoadReadsLegacyFileWithoutChangingIt()
        {
            string profiles = Path.Combine(_directory, "Profiles");
            Directory.CreateDirectory(profiles);
            string cfg = Path.Combine(profiles, "GRP.cfg");
            File.WriteAllText(cfg, SampleProfile, Encoding.Latin1);
            File.WriteAllText(Path.Combine(profiles, "GRP.nfo"), "nfo");

            UserProfile profile = UserProfile.Load(cfg);

            Assert.That(profile.ProfileName, Is.EqualTo("GRP"));
            Assert.That(profile.NFOOptions.TemplatePath, Is.EqualTo("GRP.nfo"));
            Assert.That(profile.FileNaming.SingleCD.SingleArtist.Directory, Is.EqualTo("<SC><Artist>-<Album>-<Year></SC>-GRP"));
            ClassicAssert.AreEqual(SampleProfile, File.ReadAllText(cfg, Encoding.Latin1), "CD-Tag 2.x can keep using the file");
            Assert.That(Directory.GetFiles(profiles).Length, Is.EqualTo(2), "nothing else written");
        }

        [Test]
        public void SaveInPlaceKeepsTwoXSettingsAndAddsOwnSection()
        {
            string profiles = Path.Combine(_directory, "Profiles");
            Directory.CreateDirectory(profiles);
            string cfg = Path.Combine(profiles, "GRP.cfg");
            // A 2.x setting this version doesn't use, and a template shared by several 2.x profiles
            File.WriteAllText(cfg, SampleProfile.Replace("Template=GRP.nfo", "Template=group.nfo") + "\r\n[TAGGING2]\r\nAPE_APEV2=Remove\r\n", Encoding.Latin1);
            File.WriteAllText(Path.Combine(profiles, "group.nfo"), "nfo");

            UserProfile profile = UserProfile.Load(cfg);
            Assert.That(profile.NFOOptions.TemplatePath, Is.EqualTo("group.nfo"));
            profile.Tagging.EmbedCoverInFlac = true;
            profile.Finish.CreateMD5 = true;
            profile.AudioFileTypes = "mp3,flac";
            profile.FileNaming.SingleCD.SingleArtist.Directory = "<Artist>-<Album>-GRP";
            LegacyProfileExport.Export(profile, profiles, profiles);

            string text = File.ReadAllText(cfg, LegacyProfile.DosEncoding);
            Assert.That(text, Does.Contain("[TAGGING2]\r\nAPE_APEV2=Remove\r\n"));
            Assert.That(text, Does.Contain("Template=group.nfo\r\n"), "the shared template keeps its name");
            Assert.That(text, Does.Contain("[" + LegacyProfile.ExtraSection + "]"));
            Assert.That(File.Exists(Path.Combine(profiles, "GRP.nfo")), Is.False, "no copy of the template");

            UserProfile back = UserProfile.Load(cfg);
            Assert.That(back.Tagging.EmbedCoverInFlac, Is.True);
            Assert.That(back.Finish.CreateMD5, Is.True);
            Assert.That(back.AudioFileTypes, Is.EqualTo("mp3,flac"));
            Assert.That(back.FileNaming.SingleCD.SingleArtist.Directory, Is.EqualTo("<Artist>-<Album>-GRP"));
            Assert.That(back.Tagging.Id3v2, Is.EqualTo(TagAction.WriteAlways));
        }

        [Test]
        public void ProfilesNextToTheProgramOnlyCountWhenThereAreProfiles()
        {
            Assert.That(PathService.HasProfiles(_directory), Is.False);
            File.WriteAllText(Path.Combine(_directory, "notes.cfg"), "speed = 4");
            Assert.That(PathService.HasProfiles(_directory), Is.False, "a .cfg that isn't a profile");
            File.WriteAllText(Path.Combine(_directory, "GRP.cfg"), SampleProfile, Encoding.Latin1);
            Assert.That(PathService.HasProfiles(_directory), Is.True);
        }
    }
}

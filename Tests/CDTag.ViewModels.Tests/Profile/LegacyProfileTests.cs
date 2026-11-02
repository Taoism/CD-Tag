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
            "[TAG_PROCESS]\r\nRenameDIR=1\r\nRenameMP3=1\r\nRenameCUE=0\r\nMakeM3U=1\r\nMakeSFV=1\r\nMakeNFO=1\r\nRenameNFO=0\r\nDeleteOld=1\r\nRemoveEmptyDirs=0\r\nRenameOrMakeNFO=0\r\n";

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
            Assert.That(File.Exists(Path.Combine(profiles, "GRP" + LegacyProfile.BackupExtension)), Is.True);

            string saved = Path.Combine(profiles, "GRP.cfg");
            Assert.That(LegacyProfile.IsLegacyFile(saved), Is.False, "the imported profile is saved in the new JSON format");
            Assert.That(System.Text.Json.JsonDocument.Parse(File.ReadAllText(saved)).RootElement.GetProperty("NFOOptions").GetProperty("MultiCDHeader").GetString(),
                        Is.EqualTo("Disc #Disc/#Discs" + Environment.NewLine), "saved file is standard JSON");

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
        public void LoadConvertsLegacyFileInPlaceAndKeepsOriginal()
        {
            string profiles = Path.Combine(_directory, "Profiles");
            Directory.CreateDirectory(profiles);
            string cfg = Path.Combine(profiles, "GRP.cfg");
            File.WriteAllText(cfg, SampleProfile, Encoding.Latin1);

            UserProfile profile = UserProfile.Load(cfg);

            Assert.That(profile.ProfileName, Is.EqualTo("GRP"));
            Assert.That(LegacyProfile.IsLegacyFile(cfg), Is.False);
            ClassicAssert.AreEqual(SampleProfile, File.ReadAllText(Path.Combine(profiles, "GRP" + LegacyProfile.BackupExtension), Encoding.Latin1));
        }
    }
}

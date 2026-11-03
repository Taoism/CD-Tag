using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CDTag.Common.Settings;
using CDTag.Model.Encoders;
using CDTag.Model.Tag;
using IdSharp.Tagging.ID3v2;
using NUnit.Framework;

namespace CDTag.ViewModels.Tests.Tag
{
    [TestFixture]
    public class LameTests
    {
        private static string TestData(string name)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", name);
        }

        [TestCase("lame-v0.mp3", "LAME3.100", "LAME 3.100 -V0", true, false)]
        [TestCase("lame-v0-q5.mp3", "LAME3.100", "LAME 3.100 -V0", true, false)]
        [TestCase("lame-b320.mp3", "LAME3.100", "LAME 3.100 -b 320", false, true)]
        [TestCase("lame-v2.mp3", "LAME3.100", "LAME 3.100 -V2", false, false)]
        [TestCase("lame399-v0.mp3", "LAME3.99r", "LAME 3.99 -V0", false, false)]
        public void ReadsTheLameHeader(string file, string encoder, string settings, bool sceneV0, bool scene320)
        {
            LameHeader header = LameHeader.Read(TestData(file));
            Assert.That(header, Is.Not.Null);
            Assert.That(header.Encoder, Is.EqualTo(encoder));
            Assert.That(header.Settings, Is.EqualTo(settings));
            Assert.That(header.IsSceneV0, Is.EqualTo(sceneV0));
            Assert.That(header.IsScene320, Is.EqualTo(scene320));
        }

        [Test]
        public void ReadsTheLameHeaderAfterAnId3v2Tag()
        {
            string path = Path.Combine(Path.GetTempPath(), "cdtag-lame-" + Guid.NewGuid().ToString("N") + ".mp3");
            File.Copy(TestData("lame-b320.mp3"), path);
            try
            {
                var tag = new ID3v2Tag(path) { Artist = "Artist", Title = new string('x', 3000) };
                tag.Save(path);
                Assert.That(LameHeader.Read(path).Settings, Is.EqualTo("LAME 3.100 -b 320"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void NoLameHeader()
        {
            Assert.That(LameHeader.Read(TestData("silence.mp3")) == null || LameHeader.Read(TestData("silence.mp3")).Encoder != null);
            Assert.That(LameHeader.Parse(new byte[100], 100), Is.Null);
        }

        private static List<string> Check(string source, params string[] files)
        {
            int n = 1;
            var tracks = files.Select(file => new AlbumTrack(TestData(file)) { TrackNumber = (n++).ToString() }).ToList();
            var album = new Album(Path.GetDirectoryName(TestData(files[0])), tracks) { Artist = "Artist", AlbumTitle = "Album", Genre = "Rock" };
            var release = new SceneRelease { Name = "Artist-Album-" + (SceneRules.IsWeb(source) ? "WEB-" : "") + "2024-GRP", Source = source, Album = album, Files = new List<string>() };
            return SceneRules.Check(SceneRuleSet.Mp3_2021, release).Where(i => i.StartsWith("MP3 10.2") || i.StartsWith("MP3 13:")).ToList();
        }

        [Test]
        public void CdMp3MustBeLame3100V0()
        {
            Assert.That(Check("CD", "lame-v0.mp3", "lame-v0-q5.mp3"), Is.Empty);
            Assert.That(Check("CD", "lame-v0.mp3", "lame399-v0.mp3").Single(), Does.StartWith("MP3 10.2.2").And.Contains("lame399-v0.mp3 is LAME 3.99 -V0"));
            Assert.That(Check("VINYL", "lame-b320.mp3", "lame-v2.mp3").Single(), Does.Contain("(2 tracks)"));
            Assert.That(Check("MP3CD", "lame-v2.mp3"), Is.Empty);
        }

        [Test]
        public void WebMp3EncodedByTheGroupMustBeLame3100Cbr320()
        {
            Assert.That(Check("WEB", "lame-b320.mp3"), Is.Empty);
            Assert.That(Check("WEB", "lame-v0.mp3"), Has.Some.StartWith("MP3 10.2.1"));
            // Bought as MP3 (another LAME version): released unmodified
            Assert.That(Check("WEB", "lame399-v0.mp3"), Is.Empty);
            Assert.That(Check("WEB", "lame-b320.mp3", "lame399-v0.mp3"), Has.Some.StartWith("MP3 13:"));
        }

        private static string Lame { get { return AudioEncoder.LamePath; } }

        [Test]
        public async Task DecodesToWavAndEncodesToMp3()
        {
            if (AudioEncoder.LamePath == null || AudioEncoder.FlacPath == null)
                Assert.Ignore("lame.exe/flac.exe not in Encoders or on PATH.");

            string dir = Path.Combine(Path.GetTempPath(), "cdtag-encode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.That(AudioEncoder.GetVersion(AudioEncoder.LamePath), Does.StartWith("LAME 3.100"));
                Assert.That(AudioEncoder.GetVersion(AudioEncoder.FlacPath), Does.StartWith("flac "));

                // Decode an MP3 to WAV, encode WAV -> MP3 (-b 320), and check the result
                string wav = Path.Combine(dir, "01-tone.wav");
                await AudioEncoder.DecodeAsync(TestData("lame-v0.mp3"), wav, null, CancellationToken.None);
                Assert.That(new FileInfo(wav).Length, Is.GreaterThan(100000));

                string mp3 = Path.Combine(dir, "01-tone.mp3");
                var reports = new List<int>();
                await AudioEncoder.EncodeMp3Async(wav, mp3, EncodePreset.Lame320, new SyncProgress(reports), CancellationToken.None);
                Assert.That(LameHeader.Read(mp3).IsScene320, Is.True);
                Assert.That(reports.Last(), Is.EqualTo(100));
                Assert.That(Directory.GetFiles(dir).Select(Path.GetFileName), Is.EquivalentTo(new[] { "01-tone.wav", "01-tone.mp3" }));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private class SyncProgress : IProgress<int>
        {
            private readonly List<int> _values;
            public SyncProgress(List<int> values) { _values = values; }
            public void Report(int value) { lock (_values) _values.Add(value); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDTag.Model.Checksum;
using CDTag.Model.Tag;
using NUnit.Framework;

namespace CDTag.ViewModels.Tests.Tag
{
    [TestFixture]
    public class AudioInspectorTests
    {
        private static string TestData(string name)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", name);
        }

        private static InspectorItem Item(List<InspectorItem> items, string name)
        {
            return items.Single(i => i.Name == name);
        }

        [TestCase("lame-v0.mp3")]
        [TestCase("lame-b320.mp3")]
        [TestCase("lame-v2.mp3")]
        [TestCase("lame399-v0.mp3")]
        public void LameFiles_CrcsAndDelayAreRight(string file)
        {
            LameHeader header = LameHeader.Read(TestData(file));
            Assert.That(header.TagCrcValid, "info tag CRC");
            Assert.That(header.EncoderDelay, Is.EqualTo(576));
            Assert.That(header.Lowpass, Is.GreaterThan(15000));
            Assert.That(header.Frames, Is.GreaterThan(0u));

            List<InspectorItem> items = AudioInspector.Inspect(TestData(file));
            Assert.That(Item(items, "Info tag CRC").State, Is.EqualTo(LogCheckState.Good));
            Assert.That(Item(items, "Music CRC").State, Is.EqualTo(LogCheckState.Good), Item(items, "Music CRC").Value);
            Assert.That(Item(items, "Format").Value, Is.EqualTo("MPEG-1 Layer III"));
        }

        [Test]
        public void DamagedAudio_FailsTheMusicCrc()
        {
            string path = Path.Combine(Path.GetTempPath(), "cdtag-inspect-" + Guid.NewGuid().ToString("N") + ".mp3");
            File.Copy(TestData("lame-v0.mp3"), path);
            try
            {
                byte[] data = File.ReadAllBytes(path);
                data[data.Length - 300] ^= 0xFF;
                File.WriteAllBytes(path, data);
                Assert.That(Item(AudioInspector.Inspect(path), "Music CRC").State, Is.EqualTo(LogCheckState.Bad));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Flac_StreamInfoAndVendor()
        {
            List<InspectorItem> items = AudioInspector.Inspect(TestData("tagged.flac"));
            Assert.That(Item(items, "Sample rate").Value, Is.EqualTo("44,100 Hz"));
            Assert.That(Item(items, "Bits per sample").Value, Is.EqualTo("16"));
            Assert.That(Item(items, "Channels").Value, Is.EqualTo("1")); // the synthetic test file is mono
            // The synthetic test files were made with FFmpeg.
            Assert.That(Item(items, "Encoder").Value, Does.StartWith("FFmpeg (Lavf"));
            Assert.That(Item(items, "Encoder").State, Is.EqualTo(LogCheckState.Warning));
            Assert.That(Item(items, "Blocks").Value, Does.Contain("STREAMINFO").And.Contain("VORBIS_COMMENT"));
        }
    }
}

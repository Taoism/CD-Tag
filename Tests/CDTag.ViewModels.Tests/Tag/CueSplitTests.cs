using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Cue;
using CDTag.Model.Encoders;
using CDTag.ViewModel.Tests;
using IdSharp.Tagging.VorbisComment;
using NUnit.Framework;

namespace CDTag.ViewModels.Tests.Tag
{
    [TestFixture]
    public class CueSplitTests
    {
        // 1 frame = 588 samples at 44.1 kHz. Track 1 at 0:00:10 (10 frames of hidden track audio), track 2 pregap at 0:01:00,
        // track 2 at 0:01:30, track 3 at 0:02:00; the image is 0:03:00 long.
        private const string Cue =
            "REM GENRE \"Test Genre\"\r\n" +
            "REM DATE 2026\r\n" +
            "PERFORMER \"Test Artist\"\r\n" +
            "TITLE \"Test’s Album\"\r\n" +
            "FILE \"image.wav\" WAVE\r\n" +
            "  TRACK 01 AUDIO\r\n" +
            "    TITLE \"One: the start\"\r\n" +
            "    INDEX 01 00:00:10\r\n" +
            "  TRACK 02 AUDIO\r\n" +
            "    TITLE \"Två\"\r\n" +
            "    PERFORMER \"Guest\"\r\n" +
            "    ISRC USAAA2600001\r\n" +
            "    INDEX 00 00:01:00\r\n" +
            "    INDEX 01 00:01:30\r\n" +
            "  TRACK 03 AUDIO\r\n" +
            "    TITLE \"Three?\"\r\n" +
            "    INDEX 01 00:02:00\r\n";

        private const int Total = 44100 * 3;
        private string _dir;

        [OneTimeSetUp]
        public void Setup()
        {
            IoC.ClearAllRegistrations();
            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());
        }

        [SetUp]
        public void CreateDir()
        {
            _dir = Path.Combine(Path.GetTempPath(), string.Concat("cdtag-cue-", Guid.NewGuid().ToString("N").AsSpan(0, 8)));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void DeleteDir()
        {
            Directory.Delete(_dir, true);
        }

        /// <summary>Stereo 16-bit samples whose value is the region they are in: 0 hidden, 1 track 1, 2 pregap of 2, 3 track 2, 4 track 3.</summary>
        private static short ValueAt(long sample)
        {
            if (sample < 10 * 588) return 0;
            if (sample < 75 * 588 + 0) return 1;  // 0:01:00
            if (sample < 105 * 588) return 2;     // 0:01:30
            if (sample < 150 * 588) return 3;     // 0:02:00
            return 4;
        }

        private string WriteImage()
        {
            var format = new WavFormat { Channels = 2, SampleRate = 44100, BitsPerSample = 16, BlockAlign = 4 };
            string path = Path.Combine(_dir, "image.wav");
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(format.Header(Total * 4L));
                for (long i = 0; i < Total; i++)
                {
                    short v = ValueAt(i);
                    writer.Write(v);
                    writer.Write(v);
                }
            }
            File.WriteAllText(Path.Combine(_dir, "image.cue"), Cue, new UTF8Encoding(true));
            return path;
        }

        private static List<short> ReadLeft(string wav)
        {
            using (var stream = File.OpenRead(wav))
            {
                WavFormat format = WavFormat.Read(stream);
                stream.Position = format.DataOffset;
                var reader = new BinaryReader(stream);
                var values = new List<short>();
                for (long i = 0; i < format.Samples; i++)
                {
                    values.Add(reader.ReadInt16());
                    reader.ReadInt16();
                }
                return values;
            }
        }

        [Test]
        public void Parse_ReadsAlbumAndTracks()
        {
            CueSheet sheet = CueSheet.Parse(Cue);
            Assert.That(sheet.Performer, Is.EqualTo("Test Artist"));
            Assert.That(sheet.Title, Is.EqualTo("Test’s Album"));
            Assert.That(sheet.Genre, Is.EqualTo("Test Genre"));
            Assert.That(sheet.Date, Is.EqualTo("2026"));
            Assert.That(sheet.IsImage);
            Assert.That(sheet.Tracks.Select(t => t.Index01), Is.EqualTo(new long[] { 10, 105, 150 }));
            Assert.That(sheet.Tracks[1].Index00, Is.EqualTo(75));
            Assert.That(sheet.Tracks[1].Performer, Is.EqualTo("Guest"));
            Assert.That(sheet.HiddenTrackFrames, Is.EqualTo(10));
        }

        [Test]
        public void Plan_NamesAndGapsAppendedToPreviousTrack()
        {
            List<SplitTrack> plan = CueSplitter.Plan(CueSheet.Parse(Cue), 44100, SplitFormat.Wav, hiddenTrack: true);
            Assert.That(plan.Select(t => t.FileName), Is.EqualTo(new[] { "00 - Hidden track.wav", "01 - One - the start.wav", "02 - Guest - Två.wav", "03 - Three.wav" }));
            Assert.That(plan[1].EndSample, Is.EqualTo(105 * 588), "the pregap of track 2 belongs to track 1");
            Assert.That(plan[3].EndSample, Is.EqualTo(-1));
        }

        [Test]
        public async Task SplitWav_CutsAtIndex01()
        {
            string image = WriteImage();
            CueSheet sheet = CueSheet.Load(Path.Combine(_dir, "image.cue"));
            Assert.That(sheet.FindImage(), Is.EqualTo(image));
            string output = Path.Combine(_dir, "out");
            await CueSplitter.SplitAsync(sheet, image, output, SplitFormat.Wav, true, null, CancellationToken.None);

            string[] files = Directory.GetFiles(output).Select(Path.GetFileName).OrderBy(f => f).ToArray();
            Assert.That(files, Is.EqualTo(new[] { "00 - Hidden track.wav", "01 - One - the start.wav", "02 - Guest - Två.wav", "03 - Three.wav" }));

            List<short> hidden = ReadLeft(Path.Combine(output, files[0]));
            List<short> one = ReadLeft(Path.Combine(output, files[1]));
            List<short> two = ReadLeft(Path.Combine(output, files[2]));
            List<short> three = ReadLeft(Path.Combine(output, files[3]));
            Assert.That(hidden.Count, Is.EqualTo(10 * 588));
            Assert.That(hidden.Distinct(), Is.EqualTo(new short[] { 0 }));
            Assert.That(one.Count, Is.EqualTo(95 * 588));
            Assert.That(one.Distinct(), Is.EqualTo(new short[] { 1, 2 }));
            Assert.That(two.Distinct(), Is.EqualTo(new short[] { 3 }));
            Assert.That(three.Count, Is.EqualTo(Total - 150 * 588));
            Assert.That(three.Distinct(), Is.EqualTo(new short[] { 4 }));
            Assert.That(hidden.Count + one.Count + two.Count + three.Count, Is.EqualTo(Total));
        }

        [Test]
        public async Task SplitWav_DoesNotOverwrite()
        {
            string image = WriteImage();
            CueSheet sheet = CueSheet.Load(Path.Combine(_dir, "image.cue"));
            File.WriteAllText(Path.Combine(_dir, "03 - Three.wav"), "keep");
            Assert.ThrowsAsync<IOException>(() => CueSplitter.SplitAsync(sheet, image, _dir, SplitFormat.Wav, false, null, CancellationToken.None));
            Assert.That(File.ReadAllText(Path.Combine(_dir, "03 - Three.wav")), Is.EqualTo("keep"));
            Assert.That(File.Exists(Path.Combine(_dir, "01 - One - the start.wav")), Is.False);
            await Task.CompletedTask;
        }

        [Test]
        public async Task SplitFlacImage_ToTaggedFlac()
        {
            if (AudioEncoder.FlacPath == null)
                Assert.Ignore("flac.exe not in Encoders or on PATH.");

            string wavImage = WriteImage();
            string flacImage = Path.Combine(_dir, "image.flac");
            await AudioEncoder.EncodeFlacAsync(wavImage, flacImage, null, null, CancellationToken.None);
            File.Delete(wavImage);

            CueSheet sheet = CueSheet.Load(Path.Combine(_dir, "image.cue"));
            Assert.That(sheet.FindImage(), Is.EqualTo(flacImage), "FILE says image.wav, the image next to it is FLAC");
            string output = Path.Combine(_dir, "out");
            await CueSplitter.SplitAsync(sheet, flacImage, output, SplitFormat.Flac, false, null, CancellationToken.None);

            string two = Path.Combine(output, "02 - Guest - Två.flac");
            Assert.That(File.Exists(two));
            var tag = new VorbisComment(two);
            Assert.That(tag.Artist, Is.EqualTo("Guest"));
            Assert.That(tag.Title, Is.EqualTo("Två"));
            Assert.That(tag.Album, Is.EqualTo("Test’s Album"), "a curly apostrophe survives (not cp1252)");
            Assert.That(tag.TrackNumber, Is.EqualTo("2"));
            Assert.That(tag.Items.Any(i => i.Name == "ALBUMARTIST" && i.Value == "Test Artist"));
            Assert.That(tag.Items.Any(i => i.Name == "ISRC" && i.Value == "USAAA2600001"));

            // Decode back and compare the samples.
            string decoded = Path.Combine(_dir, "two.wav");
            await AudioEncoder.DecodeAsync(two, decoded, null, CancellationToken.None);
            List<short> samples = ReadLeft(decoded);
            Assert.That(samples.Count, Is.EqualTo(45 * 588));
            Assert.That(samples.Distinct(), Is.EqualTo(new short[] { 3 }));
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Encoders;
using CDTag.ViewModel.Tests;
using CDTag.ViewModels.ConvertAudio;
using NUnit.Framework;

namespace CDTag.ViewModels.Tests.Tag
{
    [TestFixture]
    public class ConvertParallelTests
    {
        private sealed class NoEvents : IEventAggregator
        {
            public void Subscribe<T>(Action<T> handler) { }
            public void Publish<T>(T payload) { }
        }

        [OneTimeSetUp]
        public void Setup()
        {
            IoC.ClearAllRegistrations();
            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());
        }

        /// <summary>Encodes 10 WAV files (decoded from the LAME test file) with the Encode command: all done, several at once.</summary>
        [Test]
        public async Task Encode_ManyFilesAtOnce()
        {
            if (AudioEncoder.LamePath == null)
                Assert.Ignore("lame.exe not in Encoders.");
            string dir = Path.Combine(Path.GetTempPath(), "cdtag-parallel-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            try
            {
                string source = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "lame-v0.mp3");
                string first = Path.Combine(dir, "00.wav");
                await AudioEncoder.DecodeAsync(source, first, null, CancellationToken.None);
                // 20 seconds per file (the test MP3 is 1 s): long enough that the encoders measurably overlap
                byte[] pcm;
                using (var stream = File.OpenRead(first))
                {
                    var format = CDTag.Model.Cue.WavFormat.Read(stream);
                    pcm = new byte[format.DataLength];
                    stream.Position = format.DataOffset;
                    stream.ReadExactly(pcm, 0, pcm.Length);
                }
                using (var output = File.Create(first))
                {
                    byte[] header = CDTag.Model.Rip.CdRipper.CdFormat.Header(pcm.LongLength * 20);
                    output.Write(header, 0, header.Length);
                    for (int k = 0; k < 20; k++)
                        output.Write(pcm, 0, pcm.Length);
                }
                for (int i = 1; i < 10; i++)
                    File.Copy(first, Path.Combine(dir, i.ToString("00") + ".wav"));

                var vm = new ConvertAudioViewModel(new NoEvents());
                vm.AddFiles(Directory.GetFiles(dir, "*.wav").OrderBy(f => f));
                int peakLame = 0;
                var watch = Stopwatch.StartNew();
                vm.EncodeCommand.Execute(null);
                while ((vm.IsBusy || watch.ElapsedMilliseconds < 200) && watch.ElapsedMilliseconds < 120000)
                {
                    peakLame = Math.Max(peakLame, Process.GetProcessesByName("lame").Length);
                    await Task.Delay(20);
                }
                TestContext.Out.WriteLine("{0} ms, up to {1} lame.exe at once; {2}", watch.ElapsedMilliseconds, peakLame, vm.StatusText);
                Assert.That(vm.Files.All(f => f.Status != null && f.Status.StartsWith("Done", StringComparison.Ordinal)), string.Join("; ", vm.Files.Select(f => f.Status)));
                Assert.That(Directory.GetFiles(dir, "??.mp3").Length, Is.EqualTo(10));
                if (Environment.ProcessorCount > 1)
                    Assert.That(peakLame, Is.GreaterThan(1), "more than one encoder ran at the same time");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}

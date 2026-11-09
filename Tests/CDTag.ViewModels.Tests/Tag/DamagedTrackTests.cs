using System;
using System.IO;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Tag;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>An album with files that hold no audio (e.g. an unfinished download: only zero bytes).</summary>
    [TestFixture]
    public class DamagedTrackTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            IoC.ClearAllRegistrations();
            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());
        }

        [Test]
        public void NamesEveryDamagedFileAndWhy()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cdtag-damaged-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "CD1"));
                Directory.CreateDirectory(Path.Combine(dir, "CD2"));
                File.WriteAllBytes(Path.Combine(dir, "CD1", "108 Zeros.mp3"), new byte[300000]);
                var noise = new byte[50000];
                for (int i = 0; i < noise.Length; i++)
                    noise[i] = (byte)(i % 7 + 1); // never 0xFF: no frame sync
                File.WriteAllBytes(Path.Combine(dir, "CD2", "201 Noise.mp3"), noise);

                var ex = Assert.Throws<InvalidDataException>(() => new Album(dir));
                Assert.That(ex.Message, Does.StartWith("2 files have no audio"));
                Assert.That(ex.Message, Does.Contain(@"CD1\108 Zeros.mp3 (only zero bytes"));
                Assert.That(ex.Message, Does.Contain(@"CD2\201 Noise.mp3 (no audio frames found)"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void IsAllZero()
        {
            string file = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(file, new byte[200000]);
                Assert.That(Album.IsAllZero(file), Is.True);
                var data = new byte[200000];
                data[199999] = 1;
                File.WriteAllBytes(file, data);
                Assert.That(Album.IsAllZero(file), Is.False);
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}

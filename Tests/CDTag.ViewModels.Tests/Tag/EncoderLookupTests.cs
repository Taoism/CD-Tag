using System.IO;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Encoders;
using CDTag.ViewModel.Tests;
using NUnit.Framework;

namespace CDTag.ViewModels.Tests.Tag
{
    [TestFixture]
    public class EncoderLookupTests
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
        public void BuiltInEncoders_AreTheNewest()
        {
            string lame = AudioEncoder.BundledPath("lame", "lame.exe");
            string flac = AudioEncoder.BundledPath("flac", "flac.exe");
            if (lame == null || flac == null)
                Assert.Ignore("The Encoders folder is not in this checkout.");
            Assert.That(AudioEncoder.GetVersion(lame), Does.StartWith("LAME 3.100"));
            Assert.That(AudioEncoder.GetVersion(flac), Is.EqualTo("flac 1.5.0"));
        }

        [Test]
        public void YourFlacInEncodersFlac_WinsOverTheBuiltIn()
        {
            string builtIn = AudioEncoder.BundledPath("flac", "flac.exe");
            if (builtIn == null)
                Assert.Ignore("The Encoders folder is not in this checkout.");
            if (!string.IsNullOrWhiteSpace(CDTag.Common.Settings.AppOptions.Current.FlacPath))
                Assert.Ignore("A FLAC path is set in Options.");

            string folder = Path.Combine(AudioEncoder.ProgramDirectory, "Encoders", AudioEncoder.UserFlacFolder);
            Assert.That(Directory.Exists(folder), Is.False, "test folder already exists");
            Directory.CreateDirectory(folder);
            try
            {
                Assert.That(AudioEncoder.FlacPath, Is.EqualTo(builtIn));
                string mine = Path.Combine(folder, "flac.exe");
                File.Copy(builtIn, mine);
                Assert.That(AudioEncoder.FlacPath, Is.EqualTo(mine));
            }
            finally
            {
                Directory.Delete(Path.Combine(AudioEncoder.ProgramDirectory, "Encoders"), true);
            }
        }
    }
}

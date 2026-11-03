using System.Collections.Generic;
using CDTag.Model.Rip;
using NUnit.Framework;

namespace CDTag.ViewModels.Tests.Rip
{
    [TestFixture]
    public class RipCueTests
    {
        [Test]
        public void Pregap_IsIndex00InThePreviousFile_WithIsrcAndCatalog()
        {
            var toc = new List<CdTrack>
            {
                new CdTrack { Number = 1, StartSector = 0, EndSector = 15000, IsAudio = true },
                new CdTrack { Number = 2, StartSector = 15000, EndSector = 30000, IsAudio = true }
            };
            var extras = new DiscExtras { CatalogNumber = "0123456789012", GapsDetected = true };
            extras.Pregaps[2] = 152;   // 2 seconds 2 frames
            extras.Isrc[2] = "USAAA2600002";
            var job = new RipJob
            {
                Toc = toc, Extras = extras,
                Album = new RipAlbum { Artist = "Artist", Title = "Album", Tracks = { new KeyValuePair<string, string>("One", ""), new KeyValuePair<string, string>("Two", "") } },
                Settings = new RipSettings { Format = RipFormat.Flac }
            };
            string cue = job.CueSheetText(new List<string> { @"C:\x\01.flac", @"C:\x\02.flac" });
            string expected =
                "CATALOG 0123456789012\r\n" +
                "PERFORMER \"Artist\"\r\n" +
                "TITLE \"Album\"\r\n" +
                "FILE \"01.flac\" WAVE\r\n" +
                "  TRACK 01 AUDIO\r\n" +
                "    TITLE \"One\"\r\n" +
                "    PERFORMER \"Artist\"\r\n" +
                "    INDEX 01 00:00:00\r\n" +
                "  TRACK 02 AUDIO\r\n" +
                "    TITLE \"Two\"\r\n" +
                "    PERFORMER \"Artist\"\r\n" +
                "    ISRC USAAA2600002\r\n" +
                "    INDEX 00 03:17:73\r\n" +      // 15000 - 152 = 14848 sectors into 01.flac
                "FILE \"02.flac\" WAVE\r\n" +
                "    INDEX 01 00:00:00\r\n";
            Assert.That(cue, Does.EndWith(expected));
        }
    }
}

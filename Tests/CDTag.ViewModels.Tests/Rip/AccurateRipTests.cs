using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDTag.Model.Rip;
using NUnit.Framework;

namespace CDTag.ViewModels.Tests.Rip
{
    [TestFixture]
    public class AccurateRipTests
    {
        // A 13-track audio CD (track starts and lead-out as read from the drive); the database has this URL.
        private static readonly int[] Starts = { 0, 13112, 26032, 41370, 50147, 61227, 74590, 84835, 97437, 106467, 115595, 127270, 139912 };
        private const int LeadOut = 152607;

        [Test]
        public void DiscIdsAndUrl()
        {
            Assert.That(AccurateRip.DatabaseUrl(Starts, LeadOut),
                Is.EqualTo("https://www.accuraterip.com/accuraterip/9/2/4/dBAR-013-0010a429-00a48ec2-b307f20d.bin"));
        }

        [Test]
        public void EnhancedCd_LastAudioTrackEndsBeforeTheDataSession()
        {
            var toc = new List<CdTrack>
            {
                new CdTrack { Number = 1, StartSector = 0, EndSector = 20000, IsAudio = true },
                new CdTrack { Number = 2, StartSector = 20000, EndSector = 50000, IsAudio = true },
                new CdTrack { Number = 3, StartSector = 50000, EndSector = 60000, IsAudio = false }
            };
            // CdDrive.ReadToc shortens the last audio track; AudioLeadOut takes the end of the last audio track.
            toc[1].EndSector = toc[2].StartSector - 11400;
            Assert.That(CdDrive.AudioLeadOut(toc), Is.EqualTo(38600));
        }

        [Test]
        public void Parse_TwoPressings()
        {
            var data = new List<byte>();
            foreach (uint crc in new uint[] { 0x11111111, 0x22222222 })
            {
                data.Add(2);
                data.AddRange(new byte[12]);
                for (int t = 0; t < 2; t++)
                {
                    data.Add((byte)(5 + t));
                    data.AddRange(BitConverter.GetBytes(crc + (uint)t));
                    data.AddRange(BitConverter.GetBytes(0u));
                }
            }
            List<List<AccurateRipEntry>> r = AccurateRip.Parse(data.ToArray(), 2);
            Assert.That(r[0].Select(e => e.Crc), Is.EqualTo(new uint[] { 0x11111111, 0x22222222 }));
            Assert.That(r[1].Select(e => e.Confidence), Is.EqualTo(new[] { 6, 6 }));

            bool accurate; int confidence;
            AccurateRip.Describe(r[1], 0, 0x22222223, out accurate, out confidence);
            Assert.That(accurate && confidence == 6);
            AccurateRip.Describe(r[1], 0, 0x99999999, out accurate, out confidence);
            Assert.That(accurate, Is.False);
        }

        [Test]
        public void TrackCrc_SkipsTheFirstFiveSectorsOfTheFirstTrack()
        {
            // one sample of value 1 at every position: v1 = sum of positions checked
            int n = 10000;
            var bytes = new byte[n * 4];
            for (int i = 0; i < n; i++) bytes[i * 4] = 1;
            var first = new AccurateRip.TrackCrc(n, true, false);
            first.Add(bytes, 0, bytes.Length);
            var middle = new AccurateRip.TrackCrc(n, false, false);
            middle.Add(bytes, 0, bytes.Length);
            uint all = (uint)((long)n * (n + 1) / 2);
            uint skipped = (uint)((long)(AccurateRip.SkipSamples - 1) * AccurateRip.SkipSamples / 2);
            Assert.That(middle.V1, Is.EqualTo(all));
            Assert.That(first.V1, Is.EqualTo(all - skipped));
        }

        [Test]
        public void DriveOffsetRecord()
        {
            var record = new byte[69 * 2];
            BitConverter.GetBytes((short)-12).CopyTo(record, 0);
            System.Text.Encoding.ASCII.GetBytes("ABC - OTHER DRIVE").CopyTo(record, 2);
            BitConverter.GetBytes((short)6).CopyTo(record, 69);
            System.Text.Encoding.ASCII.GetBytes("HL-DT-ST - BDDVDRW UH12NS40").CopyTo(record, 71);
            Assert.That(AccurateRip.FindDriveOffset(record, "HL-DT-ST", "BDDVDRW UH12NS40"), Is.EqualTo(6));
            Assert.That(AccurateRip.FindDriveOffset(record, "ABC", "OTHER  DRIVE"), Is.EqualTo(-12));
            Assert.That(AccurateRip.FindDriveOffset(record, "XYZ", "NONE"), Is.Null);
        }

        [Test]
        public void EacNaming_WithReplacementRules()
        {
            EacSettings s = EacSettings.Defaults();
            string path = s.TrackPath(@"%albumartist%-%albumtitle%-%year%\%tracknr2%-%artist%-%title%",
                                      "Various Artists", "1960's Happy Days: Vol/1", "2026", "Pop", 3, "The Brooklyn Bridge", "What? <Live>");
            Assert.That(path, Is.EqualTo(@"Various Artists-1960's Happy Days- Vol,1-2026\03-The Brooklyn Bridge-What [Live]"));
        }
    }
}

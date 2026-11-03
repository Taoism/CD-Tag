using System;
using System.Linq;
using CDTag.Model.Rip;
using NUnit.Framework;

namespace CDTag.ViewModels.Tests.Rip
{
    /// <summary>Needs a real audio CD: set CDTAG_CD_DRIVE to its drive letter (e.g. "S"). Reads only.</summary>
    [TestFixture]
    public class CdDriveTests
    {
        private static string DriveLetter()
        {
            string letter = Environment.GetEnvironmentVariable("CDTAG_CD_DRIVE");
            if (string.IsNullOrWhiteSpace(letter))
                Assert.Ignore("CDTAG_CD_DRIVE is not set.");
            return letter;
        }

        /// <summary>Rips the first and last track (test & copy) into the temp folder and checks them with AccurateRip.</summary>
        [Test, Explicit("rips two tracks from the real CD")]
        public void RipsFirstAndLastTrack_AccurateRipMatches()
        {
            using (CdDrive drive = CdDrive.Open(DriveLetter()))
            {
                var toc = drive.ReadToc();
                int? offset = AccurateRip.FindDriveOffsetAsync(drive.Vendor, drive.Product).GetAwaiter().GetResult();
                TestContext.Out.WriteLine("{0} | {1} | offset {2}", drive.Vendor, drive.Product, offset);
                Assert.That(offset, Is.Not.Null);

                System.Collections.Generic.List<int> starts; int leadOut;
                AccurateRip.AudioLayout(toc, out starts, out leadOut);
                TestContext.Out.WriteLine(AccurateRip.DatabaseUrl(starts, leadOut));
                var ar = AccurateRip.LookupAsync(starts, leadOut).GetAwaiter().GetResult();
                Assert.That(ar, Is.Not.Null, "AccurateRip reachable");

                var ripper = new CdRipper(drive, toc, offset.Value);
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cdtag-rip-test");
                System.IO.Directory.CreateDirectory(dir);
                int count = starts.Count;
                foreach (int index in new[] { 0, count - 1 })
                {
                    string wav = System.IO.Path.Combine(dir, string.Format("{0:00}.wav", index + 1));
                    RipTrackResult r = ripper.RipTrack(index, wav, null, System.Threading.CancellationToken.None);
                    bool accurate; int confidence;
                    string text = AccurateRip.Describe(ar[index], r.ArV1, r.ArV2, out accurate, out confidence);
                    TestContext.Out.WriteLine("Track {0}: test {1:X8} copy {2:X8} peak {3:0.0} retries {4} suspicious {5} | {6}",
                        r.Number, r.TestCrc, r.CopyCrc, r.Peak, r.Retries, r.Suspicious.Count, text);
                    Assert.That(r.CrcsMatch);
                    Assert.That(accurate, text);
                }
            }
        }

        [Test]
        public void ReadsTocAndSectors()
        {
            using (CdDrive drive = CdDrive.Open(DriveLetter()))
            {
                TestContext.Out.WriteLine("Drive: " + drive.Name);
                Assert.That(drive.IsReady);
                var toc = drive.ReadToc();
                foreach (CdTrack t in toc)
                    TestContext.Out.WriteLine("{0,2} {1,7} {2,7} {3}", t.Number, t.StartSector, t.Sectors, t.IsAudio ? "audio" : "data");
                Assert.That(toc.Count(t => t.IsAudio), Is.GreaterThan(0));

                var buffer = new byte[CdDrive.SectorBytes * CdDrive.MaxSectorsPerRead];
                int lba = toc.First(t => t.IsAudio).StartSector + 1000;
                Assert.That(drive.ReadSectors(lba, CdDrive.MaxSectorsPerRead, buffer), "raw read");
                Assert.That(buffer.Any(b => b != 0), "audio is not all silence");

                var again = new byte[buffer.Length];
                Assert.That(drive.ReadSectors(lba, CdDrive.MaxSectorsPerRead, again));
                Assert.That(again, Is.EqualTo(buffer), "same sectors read twice");
            }
        }
    }
}

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
                    var ctdbEntries = new CDTag.Model.Lookup.CueToolsDbClient().LookupRipsAsync(CDTag.Model.Rip.RipJob.ToDiscToc(toc)).GetAwaiter().GetResult();
                    bool ctdbOk; int ctdbConfidence;
                    string ctdbText = Ctdb.Describe(ctdbEntries, index, r.CtdbCrc, out ctdbOk, out ctdbConfidence);
                    TestContext.Out.WriteLine("  {0}", ctdbText);
                    Assert.That(r.CrcsMatch);
                    Assert.That(accurate, text);
                    Assert.That(ctdbOk, ctdbText);
                }
            }
        }

        /// <summary>Tracks 1 and 13 again with C2 pointers, overread and 8x: AccurateRip must still match.</summary>
        [Test, Explicit("rips two tracks from the real CD")]
        public void DriveOptions_C2OverreadSpeed()
        {
            using (CdDrive drive = CdDrive.Open(DriveLetter()))
            {
                var toc = drive.ReadToc();
                int offset = AccurateRip.FindDriveOffsetAsync(drive.Vendor, drive.Product).GetAwaiter().GetResult().Value;
                System.Collections.Generic.List<int> starts; int leadOut;
                AccurateRip.AudioLayout(toc, out starts, out leadOut);
                var ar = AccurateRip.LookupAsync(starts, leadOut).GetAwaiter().GetResult();
                TestContext.Out.WriteLine("set speed 8x: {0}", drive.SetReadSpeed(8));
                var probe = new byte[CdDrive.SectorBytes * 24];
                int c2;
                TestContext.Out.WriteLine("C2 read: {0}", drive.ReadSectorsC2(1000, 24, probe, 0, out c2));
                var ripper = new CdRipper(drive, toc, offset) { UseC2 = drive.SupportsC2(1000), Overread = true };
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cdtag-rip-test2");
                System.IO.Directory.CreateDirectory(dir);
                foreach (int index in new[] { 0, starts.Count - 1 })
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    RipTrackResult r = ripper.RipTrack(index, System.IO.Path.Combine(dir, (index + 1).ToString("00") + ".wav"), null, System.Threading.CancellationToken.None);
                    bool accurate; int confidence;
                    string text = AccurateRip.Describe(ar[index], r.ArV1, r.ArV2, out accurate, out confidence);
                    TestContext.Out.WriteLine("Track {0}: {1:0.0}s, C2 sectors {2}, retries {3}, overread failed {4} | {5}",
                        r.Number, watch.Elapsed.TotalSeconds, r.C2Errors, r.Retries, ripper.OverreadFailed, text);
                    Assert.That(accurate, text);
                }
                drive.SetReadSpeed(0);
            }
        }

        [Test, Explicit("probes C2 support")]
        public void ProbeC2()
        {
            using (CdDrive drive = CdDrive.Open(DriveLetter()))
            {
                var buffer = new byte[CdDrive.SectorBytes * 24];
                foreach (int lba in new[] { 1000, 50000, 120000 })
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    int c2;
                    bool ok = drive.ReadSectorsC2(lba, 1, buffer, 0, out c2);
                    TestContext.Out.WriteLine("C2 read at {0}: ok {1}, flagged sectors {2}, {3} ms, error {4} ({5})", lba, ok, c2, watch.ElapsedMilliseconds,
                        drive.LastError, new System.ComponentModel.Win32Exception(drive.LastError).Message);
                }
            }
        }

        [Test]
        public void ReadsExtras()
        {
            using (CdDrive drive = CdDrive.Open(DriveLetter()))
            {
                var toc = drive.ReadToc();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var steps = new SyncProgress(m => TestContext.Out.WriteLine("{0,6} ms  {1}", watch.ElapsedMilliseconds, m));
                DiscExtras extras = GapDetector.Read(drive, toc, steps, System.Threading.CancellationToken.None);
                TestContext.Out.WriteLine("{0} ms; MCN {1}; CD-Text {2}; gaps detected {3}", watch.ElapsedMilliseconds, extras.CatalogNumber ?? "-",
                                          extras.CdText == null ? "none" : extras.CdText.AlbumPerformer + " / " + extras.CdText.AlbumTitle, extras.GapsDetected);
                foreach (CdTrack t in toc.Where(t => t.IsAudio))
                {
                    string isrc; extras.Isrc.TryGetValue(t.Number, out isrc);
                    TestContext.Out.WriteLine("  {0,2} pregap {1} isrc {2} text {3} / {4}", t.Number, GapDetector.Time(extras.Pregap(t.Number)), isrc ?? "-",
                                              extras.CdText == null ? "" : extras.CdText.Performer(t.Number), extras.CdText == null ? "" : extras.CdText.Title(t.Number));
                }
                Assert.That(extras.GapsDetected, "the drive returns subchannel data");
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
    
        private sealed class SyncProgress : IProgress<string>
        {
            private readonly Action<string> _report;
            public SyncProgress(Action<string> report) { _report = report; }
            public void Report(string value) { _report(value); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CDTag.Model.Checksum;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Checksum
{
    [TestFixture]
    public class EacLogChecksumTests
    {
        /// <summary>With a 128-bit block, Rijndael is AES: compare with the .NET implementation.</summary>
        [Test]
        public void Rijndael128_MatchesAes256()
        {
            var random = new Random(7);
            for (int n = 0; n < 20; n++)
            {
                var key = new byte[32];
                var block = new byte[16];
                random.NextBytes(key);
                random.NextBytes(block);
                using (var aes = System.Security.Cryptography.Aes.Create())
                {
                    aes.Key = key;
                    byte[] expected = aes.EncryptEcb(block, System.Security.Cryptography.PaddingMode.None);
                    Assert.That(EacLogChecksum.EncryptBlock(key, block), Is.EqualTo(expected));
                }
            }
        }

        private const string SyntheticLog =
            "Exact Audio Copy V1.8 from 15. July 2024\r\n\r\n" +
            "EAC extraction logfile from 1. January 2026, 12:00\r\n\r\n" +
            "Test Artist / Test Album\r\n\r\n" +
            "Used drive  : TEST DRIVE   Adapter: 1  ID: 0\r\n\r\n" +
            "Read mode               : Secure\r\n" +
            "Utilize accurate stream : Yes\r\n" +
            "Defeat audio cache      : Yes\r\n" +
            "Make use of C2 pointers : No\r\n\r\n" +
            "Read offset correction                      : 6\r\n" +
            "Overread into Lead-In and Lead-Out          : No\r\n" +
            "Fill up missing offset samples with silence : Yes\r\n" +
            "Delete leading and trailing silent blocks   : No\r\n" +
            "Null samples used in CRC calculations       : Yes\r\n" +
            "Used interface                              : Native Win32 interface for Win NT & 2000\r\n" +
            "Gap handling                                : Appended to previous track\r\n\r\n" +
            "Used output format : User Defined Encoder\r\n" +
            "Sample format      : 44.100 Hz; 16 Bit; Stereo\r\n\r\n\r\n" +
            "TOC of the extracted CD\r\n\r\n" +
            "     Track |   Start  |  Length  | Start sector | End sector \r\n" +
            "        1  |  0:00.00 |  3:00.00 |         0    |    13499   \r\n" +
            "        2  |  3:00.00 |  2:00.00 |     13500    |    22499   \r\n\r\n\r\n" +
            "Track  1\r\n\r\n" +
            "     Filename C:\\Rips\\01 - One.wav\r\n\r\n" +
            "     Peak level 98.0 %\r\n" +
            "     Extraction speed 8.0 X\r\n" +
            "     Track quality 100.0 %\r\n" +
            "     Test CRC 11111111\r\n" +
            "     Copy CRC 11111111\r\n" +
            "     Accurately ripped (confidence 5)  [AAAAAAAA]  (AR v2)\r\n" +
            "     Copy OK\r\n\r\n" +
            "Track  2\r\n\r\n" +
            "     Filename C:\\Rips\\02 - Two.wav\r\n\r\n" +
            "     Peak level 97.0 %\r\n" +
            "     Extraction speed 8.0 X\r\n" +
            "     Track quality 99.0 %\r\n" +
            "     Test CRC 22222222\r\n" +
            "     Copy CRC 33333333\r\n" +
            "     Suspicious position 0:01:02\r\n" +
            "     Cannot be verified as accurate (confidence 5)  [BBBBBBBB], AccurateRip returned [CCCCCCCC]  (AR v2)\r\n" +
            "     Copy finished\r\n\r\n" +
            "There were errors\r\n\r\n" +
            "End of status report\r\n";

        private static string Sign(string text)
        {
            return text + "\r\n==== Log checksum " + EacLogChecksum.Compute(text) + " ====\r\n";
        }

        [Test]
        public void Synthetic_ParsesAndRates()
        {
            List<EacLog> logs = EacLog.Split(Sign(SyntheticLog));
            Assert.That(logs, Has.Count.EqualTo(1));
            EacLog log = logs[0];
            Assert.That(log.ChecksumValid, Is.True);
            Assert.That(log.IsEnglish && !log.IsRange && !log.NoErrors);
            Assert.That(log.Album, Is.EqualTo("Test Artist / Test Album"));
            Assert.That(log.Settings["Read mode"], Is.EqualTo("Secure"));
            Assert.That(log.Tracks.Select(t => t.Number), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(log.Tracks[0].CopyOk && log.Tracks[0].AccuratelyRipped == true && log.Tracks[0].Quality == 100);
            Assert.That(log.Tracks[1].Errors, Is.EqualTo(new[] { "Suspicious position 0:01:02" }));
            Assert.That(log.Tracks[1].AccuratelyRipped, Is.False);

            List<LogCheck> checks = log.Evaluate();
            Assert.That(checks.Single(c => c.Name == "Log checksum").State, Is.EqualTo(LogCheckState.Good));
            Assert.That(checks.Single(c => c.Name == "Read mode").State, Is.EqualTo(LogCheckState.Good));
            Assert.That(checks.Single(c => c.Name == "Gap handling").State, Is.EqualTo(LogCheckState.Good));
            Assert.That(checks.Single(c => c.Name == "Track 1").State, Is.EqualTo(LogCheckState.Good));
            Assert.That(checks.Single(c => c.Name == "Track 2").State, Is.EqualTo(LogCheckState.Bad));
            Assert.That(checks.Single(c => c.Name == "AccurateRip").State, Is.EqualTo(LogCheckState.Bad));
            Assert.That(checks.Single(c => c.Name == "Errors").State, Is.EqualTo(LogCheckState.Bad));
        }

        [Test]
        public void EditedLog_FailsChecksum()
        {
            string edited = Sign(SyntheticLog).Replace("Copy CRC 33333333", "Copy CRC 22222222");
            EacLog log = EacLog.Split(edited)[0];
            Assert.That(log.ChecksumValid, Is.False);
            Assert.That(log.Evaluate().First().State, Is.EqualTo(LogCheckState.Bad));
        }

        [Test]
        public void LineBreaksDoNotChangeTheChecksum()
        {
            Assert.That(EacLogChecksum.Compute(SyntheticLog), Is.EqualTo(EacLogChecksum.Compute(SyntheticLog.Replace("\r\n", "\n"))));
        }

        [Test]
        public void TwoLogsInOneFile_EachHasItsOwnChecksum()
        {
            string second = SyntheticLog.Replace("Test Album", "Test Album (again)");
            List<EacLog> logs = EacLog.Split(Sign(SyntheticLog) + "\r\n" + Sign(second));
            Assert.That(logs, Has.Count.EqualTo(2));
            Assert.That(logs.All(l => l.ChecksumValid == true));
            Assert.That(logs[1].Album, Is.EqualTo("Test Artist / Test Album (again)"));
        }

        [Test]
        public void UnsignedLog_HasNoChecksum()
        {
            EacLog log = EacLog.Split(SyntheticLog)[0];
            Assert.That(log.ChecksumValid, Is.Null);
            Assert.That(log.Evaluate().First().State, Is.EqualTo(LogCheckState.Warning));
        }

        /// <summary>
        /// Real signed logs are not kept in the repository. Set CDTAG_EAC_LOGS to a folder of EAC logs to check the
        /// algorithm against them; every log with a checksum line must verify.
        /// </summary>
        [Test]
        public void RealLogs_FromEnvironment()
        {
            string folder = Environment.GetEnvironmentVariable("CDTAG_EAC_LOGS");
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                Assert.Ignore("CDTAG_EAC_LOGS is not set.");

            int checkedLogs = 0;
            foreach (string file in Directory.GetFiles(folder, "*.log", SearchOption.AllDirectories))
            {
                string text = EacLog.ReadText(File.ReadAllBytes(file));
                foreach (EacLog log in EacLog.Split(text))
                {
                    if (log.ListedChecksum == null)
                        continue;
                    Assert.That(EacLogChecksum.Compute(log.SignedText), Is.EqualTo(log.ListedChecksum), file);
                    checkedLogs++;
                }
            }
            TestContext.Out.WriteLine("{0} signed logs verified.", checkedLogs);
            Assert.That(checkedLogs, Is.GreaterThan(0));
        }
    }
}

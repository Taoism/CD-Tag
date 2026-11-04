using System.Collections.Generic;
using CDTag.Model.Lookup;
using CDTag.Model.Rip;
using NUnit.Framework;

namespace CDTag.ViewModels.Tests.Rip
{
    [TestFixture]
    public class CtdbTests
    {
        [Test]
        public void TrackCrc_SkipsTheFirstStrideAndTheLastStridePlusRemainder()
        {
            // A two-track "disc" of 20,000 samples per track: the first track skips 5,880 samples at the start, the last
            // skips 5,880 + (40,000 % 5,880 = 4,720) at the end. Every sample is different, so the CRC shows what was included.
            const int n = 20000;
            byte[] Track(int t)
            {
                var b = new byte[n * 4];
                for (int i = 0; i < n; i++) { b[i * 4] = (byte)i; b[i * 4 + 1] = (byte)(i >> 8); b[i * 4 + 2] = (byte)t; }
                return b;
            }
            byte[] one = Track(1), two = Track(2);
            var first = new Ctdb.TrackCrc(n, true, false, 2 * n);
            var last = new Ctdb.TrackCrc(n, false, true, 2 * n);
            // fed in odd chunks, as the ripper does
            foreach (var pair in new[] { (first, one), (last, two) })
                for (int o = 0; o < pair.Item2.Length; o += 4 * 1001)
                    pair.Item1.Add(pair.Item2, o, System.Math.Min(4 * 1001, pair.Item2.Length - o));

            Assert.That(first.Value, Is.EqualTo(Crc32Running.Compute(one, Ctdb.Stride * 4, (n - Ctdb.Stride) * 4)));
            int cut = Ctdb.Stride + (2 * n) % Ctdb.Stride;
            Assert.That(last.Value, Is.EqualTo(Crc32Running.Compute(two, 0, (n - cut) * 4)));
        }

        [Test]
        public void ParseRips_AndDescribe()
        {
            const string xml = "<ctdb xmlns=\"http://db.cuetools.net/ns/mmd-1.0#\">" +
                               "<entry confidence=\"98\" crc32=\"76b68bcc\" id=\"1\" trackcrcs=\"f16a1acf 0a5f91c9\" />" +
                               "<entry confidence=\"3\" crc32=\"11111111\" id=\"2\" trackcrcs=\"00e6b388 0a5f91c9\" /></ctdb>";
            List<CtdbEntry> entries = CueToolsDbClient.ParseRips(xml);
            Assert.That(entries.Count, Is.EqualTo(2));
            bool ok; int confidence;
            string text = Ctdb.Describe(entries, 1, 0x0A5F91C9, out ok, out confidence);
            Assert.That(ok && confidence == 101, text);
            Ctdb.Describe(entries, 0, 0xF16A1ACF, out ok, out confidence);
            Assert.That(ok && confidence == 98);
            Ctdb.Describe(entries, 0, 0x12345678, out ok, out confidence);
            Assert.That(ok, Is.False);
        }
    }
}

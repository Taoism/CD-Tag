using System.Collections.Generic;
using System.Text;
using CDTag.Model.Rip;
using NUnit.Framework;

namespace CDTag.ViewModels.Tests.Rip
{
    [TestFixture]
    public class SubchannelTests
    {
        private static int Crc16Ccitt(byte[] data, int count)
        {
            int crc = 0;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[i] << 8;
                for (int k = 0; k < 8; k++)
                    crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
            }
            return crc;
        }

        /// <summary>A sector's 96 raw P-W bytes carrying <paramref name="q"/> (10 bytes; the CRC is added) in bit 6.</summary>
        private static byte[] RawSubcode(byte[] q10, bool breakCrc = false)
        {
            var q = new byte[12];
            q10.CopyTo(q, 0);
            int crc = Crc16Ccitt(q, 10) ^ 0xFFFF;
            if (breakCrc) crc ^= 1;
            q[10] = (byte)(crc >> 8); q[11] = (byte)crc;
            var raw = new byte[96];
            for (int i = 0; i < 96; i++)
                if ((q[i / 8] & (0x80 >> (i % 8))) != 0)
                    raw[i] = 0x40 | 0x80;   // Q bit plus some P noise
            return raw;
        }

        [Test]
        public void Position()
        {
            QPosition p = QPosition.FromRawSubcode(RawSubcode(new byte[] { 0x01, 0x12, 0x00, 0, 0, 0, 0, 0, 0, 0 }), 0);
            Assert.That(p.Adr == 1 && p.Track == 12 && p.Index == 0);
            Assert.That(QPosition.FromRawSubcode(RawSubcode(new byte[] { 0x01, 0x12, 0x01, 0, 0, 0, 0, 0, 0, 0 }, breakCrc: true), 0), Is.Null);
        }

        [Test]
        public void Isrc()
        {
            // "USAAA2600001": U=37 S=35 A=17 A=17 A=17 (6 bits each), 2 zero bits, then BCD 2 6 0 0 0 0 1, 4 zero bits
            ulong bits = 0;
            foreach (int c in new[] { 37, 35, 17, 17, 17 }) bits = (bits << 6) | (uint)c;
            bits <<= 2;
            foreach (int d in new[] { 2, 6, 0, 0, 0, 0, 1 }) bits = (bits << 4) | (uint)d;
            bits <<= 4;
            var q = new byte[10];
            q[0] = 0x03;
            for (int i = 0; i < 8; i++) q[1 + i] = (byte)(bits >> (56 - i * 8));
            QPosition p = QPosition.FromRawSubcode(RawSubcode(q), 0);
            Assert.That(p.Isrc, Is.EqualTo("USAAA2600001"));
            Assert.That(p.Index, Is.EqualTo(-1), "an ISRC frame is not a position");
        }

        [Test]
        public void CatalogNumber()
        {
            var q = new byte[10];
            q[0] = 0x02;
            string mcn = "0123456789012";
            for (int i = 0; i < 13; i++)
                q[1 + i / 2] |= (byte)((mcn[i] - '0') << (i % 2 == 0 ? 4 : 0));
            Assert.That(QPosition.FromRawSubcode(RawSubcode(q), 0).CatalogNumber, Is.EqualTo(mcn));
        }

        [Test]
        public void CdText_TitlesAndPerformersAcrossPacks()
        {
            // Titles: album, track 1, track 2 ("same as before" tab); performers: album only.
            var packs = new List<byte>();
            void Add(byte type, string text)
            {
                byte[] bytes = Encoding.Latin1.GetBytes(text);
                for (int o = 0; o < bytes.Length; o += 12)
                {
                    packs.Add(type); packs.Add(0); packs.Add(0); packs.Add(0);
                    for (int i = 0; i < 12; i++) packs.Add(o + i < bytes.Length ? bytes[o + i] : (byte)0);
                    packs.Add(0); packs.Add(0);
                }
            }
            Add(0x80, "Album Título\0First Song\0\t\0");
            Add(0x81, "The Band\0");
            CdText t = CdText.Parse(packs.ToArray(), 0, packs.Count);
            Assert.That(t.AlbumTitle, Is.EqualTo("Album Título"));
            Assert.That(t.Title(1), Is.EqualTo("First Song"));
            Assert.That(t.Title(2), Is.EqualTo("First Song"));
            Assert.That(t.AlbumPerformer, Is.EqualTo("The Band"));
        }
    }
}

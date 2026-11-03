using CDTag.Model.Lookup;
using NUnit.Framework;

namespace CDTag.ViewModels.Tests.Rip
{
    [TestFixture]
    public class GnudbTests
    {
        private static DiscToc Toc()
        {
            var toc = new DiscToc { LeadOut = 30000 };
            toc.Offsets.AddRange(new[] { 0, 10000, 20000 });
            toc.IsAudio.AddRange(new[] { true, true, true });
            return toc;
        }

        [Test]
        public void DiscId_FreedbFormula()
        {
            // three tracks at 2, 135 and 268 seconds (with the 150-sector lead-in), 400 seconds long
            Assert.That(GnudbClient.DiscId(Toc()).ToString("x8"), Is.EqualTo("1b019003"));
        }

        [Test]
        public void ParseEntry_VariousArtistsAndLongLines()
        {
            const string entry = "210 rock 1b019003 CD database entry follows (until terminating `.')\r\n" +
                                 "# xmcd\r\n" +
                                 "DTITLE=Various / Test Album\r\n" +
                                 "DYEAR=1999\r\n" +
                                 "DGENRE=Rock\r\n" +
                                 "TTITLE0=Artist One / Song One\r\n" +
                                 "TTITLE1=Artist Two / A very long title that \r\n" +
                                 "TTITLE1=continues on the next line\r\n" +
                                 "TTITLE2=Artist Three / Song’s Three\r\n" +
                                 ".\r\n";
            ReleaseDetails r = GnudbClient.ParseEntry(entry, Toc());
            Assert.That(r.IsVariousArtists);
            Assert.That(r.Artist, Is.EqualTo("Various Artists"));
            Assert.That(r.Title, Is.EqualTo("Test Album"));
            Assert.That(r.Date, Is.EqualTo("1999"));
            Assert.That(r.Tracks[1].Title, Is.EqualTo("A very long title that continues on the next line"));
            Assert.That(r.Tracks[2].Artist, Is.EqualTo("Artist Three"));
            Assert.That(r.Tracks[2].Title, Is.EqualTo("Song’s Three"));
            Assert.That(r.Source, Is.EqualTo("gnudb"));
        }

        [Test]
        public void WithoutAnAddress_NothingIsAsked()
        {
            Assert.That(new GnudbClient(null).IsEnabled, Is.False);
            Assert.That(new GnudbClient("someone@example.org").IsEnabled);
        }
    }
}

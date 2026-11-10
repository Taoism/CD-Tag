using CDTag.Model.Lookup;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>CTDB lookups and the cover links in their answers go over HTTPS only.</summary>
    [TestFixture]
    public class CtdbHttpsTests
    {
        [TestCase("http://coverartarchive.org/release/x/1-500.jpg", "https://coverartarchive.org/release/x/1-500.jpg")]
        [TestCase("https://i.discogs.com/front.jpg", "https://i.discogs.com/front.jpg")]
        [TestCase("http://img.discogs.com:80/a.jpg", "https://img.discogs.com/a.jpg")]
        [TestCase("file:///C:/Windows/win.ini", null)]
        [TestCase("ftp://example.invalid/a.jpg", null)]
        [TestCase("not a url", null)]
        [TestCase(null, null)]
        public void CoverLinksAreHttps(string url, string expected)
        {
            Assert.That(CueToolsDbClient.Secure(url), Is.EqualTo(expected));
        }
    }
}

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>Live: asks db.cue.tools over HTTPS (run by hand: needs the internet).</summary>
    [TestFixture, Explicit("Live CTDB lookup over HTTPS")]
    public class CtdbHttpsLiveTests
    {
        [Test]
        public void RipLookupAnswersOverHttps()
        {
            DiscToc toc = DiscToc.FromLog(DiscTocTests.EacLog, "EAC log");
            var entries = new CueToolsDbClient().LookupRipsAsync(toc).GetAwaiter().GetResult();
            TestContext.Out.WriteLine("entries: " + entries.Count);
            Assert.That(entries, Is.Not.Empty);
        }
    }
}

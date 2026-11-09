using System;
using CDTag.Model.Update;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Update
{
    [TestFixture]
    public class UpdateCheckerTests
    {
        [Test]
        public void ParsesGitHubLatestRelease()
        {
            const string json = @"{
  ""tag_name"": ""v3.2.7"",
  ""html_url"": ""https://github.com/Taoism/CD-Tag/releases/tag/v3.2.7"",
  ""body"": ""## Changes\n- One"",
  ""assets"": [
    { ""name"": ""checksums.txt"", ""browser_download_url"": ""https://example.invalid/checksums.txt"" },
    { ""name"": ""CD-Tag-3.2.7-win-x64.zip"", ""browser_download_url"": ""https://example.invalid/CD-Tag-3.2.7-win-x64.zip"" }
  ]
}";
            UpdateInfo info = UpdateChecker.Parse(json);
            Assert.That(info, Is.Not.Null);
            Assert.That(info.Version, Is.EqualTo(new Version(3, 2, 7)));
            Assert.That(info.DownloadUrl, Is.EqualTo("https://example.invalid/CD-Tag-3.2.7-win-x64.zip"));
            Assert.That(info.PageUrl, Does.EndWith("/v3.2.7"));
            Assert.That(info.Notes, Does.Contain("One"));
        }

        [Test]
        public void NoZipOrNoVersionIsNoUpdate()
        {
            Assert.That(UpdateChecker.Parse(@"{ ""tag_name"": ""v9.0.0"", ""assets"": [] }"), Is.Null);
            Assert.That(UpdateChecker.Parse(@"{ ""tag_name"": ""latest"", ""assets"": [ { ""name"": ""CD-Tag-win-x64.zip"", ""browser_download_url"": ""x"" } ] }"), Is.Null);
        }

        [TestCase("v3.2.6", 3, 2, 6)]
        [TestCase("3.10", 3, 10, 0)]
        [TestCase(" V4.0.1 ", 4, 0, 1)]
        public void ParsesVersionTags(string tag, int major, int minor, int build)
        {
            Assert.That(UpdateChecker.ParseVersion(tag), Is.EqualTo(new Version(major, minor, build)));
            Assert.That(UpdateChecker.ParseVersion(tag) > new Version(3, 2, 5), Is.True);
        }
    }
}

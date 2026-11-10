using CDTag.Common.Json;
using CDTag.Common.Settings;
using NUnit.Framework;

namespace CDTag.Common.Tests.Settings
{
    /// <summary>The Discogs token is written to options.json encrypted (DPAPI), never as plain text.</summary>
    [TestFixture]
    public class DiscogsTokenStorageTests
    {
        private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789";

        [Test]
        public void TokenIsSavedEncryptedAndReadBack()
        {
            string json = JsonSerializer.SerializeObject(new AppOptionsData { DiscogsToken = Token });
            Assert.That(json, Does.Not.Contain(Token));
            Assert.That(json, Does.Contain("DiscogsTokenProtected"));
            Assert.That(json, Does.Not.Contain("\"DiscogsToken\""));

            AppOptionsData loaded = JsonSerializer.ReadObject<AppOptionsData>(json);
            Assert.That(loaded.DiscogsToken, Is.EqualTo(Token));
        }

        [Test]
        public void PlainTokenFromAnOlderOptionsFileIsStillRead()
        {
            AppOptionsData loaded = JsonSerializer.ReadObject<AppOptionsData>("{ \"DiscogsToken\": \"" + Token + "\" }");
            Assert.That(loaded.DiscogsToken, Is.EqualTo(Token));
            Assert.That(JsonSerializer.SerializeObject(loaded), Does.Not.Contain(Token));
        }

        [Test]
        public void NoTokenAndUnreadableTokenAreEmpty()
        {
            Assert.That(Dpapi.Protect(null), Is.Null);
            Assert.That(Dpapi.Unprotect("not base64!"), Is.Null);
            Assert.That(Dpapi.Unprotect("AAAA"), Is.Null);
            Assert.That(JsonSerializer.ReadObject<AppOptionsData>("{ \"DiscogsTokenProtected\": \"AAAA\" }").DiscogsToken, Is.Null);
        }
    }
}

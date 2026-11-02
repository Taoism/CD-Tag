using System.Runtime.Serialization;
using CDTag.Common.Json;
using NUnit.Framework;

namespace CDTag.Common.Tests.Json
{
    [TestFixture]
    public class JsonStringEscapingTest
    {
        [DataContract]
        public class TextHolder
        {
            [DataMember]
            public string Text { get; set; }
        }

        [Test]
        public void ControlCharactersAreEscapedAndRoundTrip()
        {
            const string original = "Disc #Disc/#Discs\r\nline2\ttab \"quoted\" back\\slash \u0001";

            string json = JsonSerializer.SerializeObject(new TextHolder { Text = original });

            Assert.That(json, Does.Contain("\\r\\n"));
            Assert.That(json, Does.Contain("\\t"));
            Assert.That(json, Does.Contain("\\u0001"));
            Assert.That(json.Substring(json.IndexOf(':')), Does.Not.Contain("\t"), "no raw control characters inside the value");

            TextHolder copy = JsonSerializer.ReadObject<TextHolder>(json);
            Assert.That(copy.Text, Is.EqualTo(original));
        }
    }
}

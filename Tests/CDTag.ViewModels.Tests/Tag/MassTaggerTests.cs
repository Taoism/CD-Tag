using System;
using System.IO;
using System.Linq;
using CDTag.Model.Tag;
using IdSharp.Tagging.ID3v2;
using IdSharp.Tagging.VorbisComment;
using NUnit.Framework;

namespace CDTag.ViewModels.Tests.Tag
{
    [TestFixture]
    public class MassTaggerTests
    {
        private string _dir;

        [SetUp]
        public void CreateDir()
        {
            _dir = Path.Combine(Path.GetTempPath(), string.Concat("cdtag-masstag-", Guid.NewGuid().ToString("N").AsSpan(0, 8)));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void DeleteDir()
        {
            Directory.Delete(_dir, true);
        }

        private string Copy(string name)
        {
            string target = Path.Combine(_dir, name);
            File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", name), target);
            return target;
        }

        [Test]
        public void Flac_WritesOnlyChangedFields_KeepsPicture()
        {
            string path = Copy("tagged.flac");
            int pictures = new VorbisComment(path).GetPictureBlocks().Count;
            TagValues before = MassTagger.Read(path);

            TagValues values = before.Clone();
            values[TagField.Album] = "New Album";
            values[TagField.Comment] = "";
            values[TagField.Year] = "1999";
            MassTagger.Write(path, values, values.ChangedFrom(before));

            TagValues after = MassTagger.Read(path);
            Assert.That(after[TagField.Album], Is.EqualTo("New Album"));
            Assert.That(after[TagField.Year], Is.EqualTo("1999"));
            Assert.That(after[TagField.Title], Is.EqualTo(before[TagField.Title]));
            Assert.That(after[TagField.Artist], Is.EqualTo(before[TagField.Artist]));
            Assert.That(new VorbisComment(path).GetPictureBlocks().Count, Is.EqualTo(pictures));
        }

        [Test]
        public void Mp3_CreatesId3v2AndClearsFields()
        {
            string path = Copy("lame-v0.mp3");
            TagValues before = MassTagger.Read(path);
            TagValues values = before.Clone();
            values[TagField.Artist] = "Artist";
            values[TagField.Title] = "Title";
            values[TagField.TrackNumber] = "3";
            values[TagField.Comment] = "Hello";
            MassTagger.Write(path, values, values.ChangedFrom(before));

            var id3v2 = new ID3v2Tag(path);
            Assert.That(id3v2.Artist, Is.EqualTo("Artist"));
            Assert.That(id3v2.TrackNumber, Is.EqualTo("3"));
            Assert.That(MassTagger.Read(path)[TagField.Comment], Is.EqualTo("Hello"));

            TagValues saved = MassTagger.Read(path);
            TagValues cleared = saved.Clone();
            cleared[TagField.Comment] = "";
            MassTagger.Write(path, cleared, cleared.ChangedFrom(saved));
            TagValues after = MassTagger.Read(path);
            Assert.That(after[TagField.Comment], Is.Empty);
            Assert.That(after[TagField.Title], Is.EqualTo("Title"));
            // The audio is still readable with its LAME tag.
            Assert.That(LameHeader.Read(path).Settings, Is.EqualTo("LAME 3.100 -V0"));
        }
    }
}

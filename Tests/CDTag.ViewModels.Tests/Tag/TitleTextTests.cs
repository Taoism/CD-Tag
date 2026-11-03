using CDTag.Model.Tag;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    [TestFixture]
    public class TitleTextTests
    {
        [TestCase("don't shit where you eat", TitleCasing.CapitalizeEveryWord, "Don't Shit Where You Eat")]
        [TestCase("PUSH TH' LITTLE DAISIES (live)", TitleCasing.CapitalizeEveryWord, "Push Th' Little Daisies (Live)")]
        [TestCase("part II - the return/reprise", TitleCasing.CapitalizeEveryWord, "Part II - The Return/Reprise")]
        [TestCase("REMIX DID IT", TitleCasing.CapitalizeEveryWord, "Remix Did It")]
        [TestCase("Part II - the return", TitleCasing.CapitalizeEveryWord, "Part II - The Return")]
        [TestCase("Koko", TitleCasing.Uppercase, "KOKO")]
        [TestCase("Koko", TitleCasing.Lowercase, "koko")]
        [TestCase("WHAT i'M SAYING. AND MORE", TitleCasing.SentenceCase, "What I'm saying. And more")]
        [TestCase("Drifter In The Dark", TitleCasing.SentenceCase, "Drifter in the dark")]
        public void Casing(string text, TitleCasing casing, string expected)
        {
            Assert.That(TitleText.ApplyCasing(text, casing), Is.EqualTo(expected));
        }

        [Test]
        public void ReplacesIgnoringCaseUnlessAsked()
        {
            Assert.That(TitleText.Replace("Live At The Metro", "at the", "at the", false), Is.EqualTo("Live at the Metro"));
            Assert.That(TitleText.Replace("Live At The Metro", "at the", "x", true), Is.EqualTo("Live At The Metro"));
        }

        [Test]
        public void ParsesPastedTracklists()
        {
            string text = "CD1\r\n1. Koko (3:12)\r\n02 - Don't Shit Where You Eat 4:01\r\n\r\n1-03\tDrifter In The Dark\t2:59\r\nA4 Flutes Of Chi\r\n" +
                          "Disc 2:\r\n5) Mister Would You Please Help My Pony?\r\n10 Years Gone\r\n";

            Assert.That(TitleText.ParseTracklist(text), Is.EqualTo(new[]
            {
                "Koko", "Don't Shit Where You Eat", "Drifter In The Dark", "Flutes Of Chi", "Mister Would You Please Help My Pony?", "10 Years Gone"
            }));
        }
    }
}

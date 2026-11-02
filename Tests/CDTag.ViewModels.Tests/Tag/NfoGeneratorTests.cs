using System;
using System.Collections.Generic;
using System.Linq;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Profile;
using CDTag.Model.Tag;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Tag
{
    /// <summary>NFO generation from a CD-Tag 2.x style template (synthetic template, no group art).</summary>
    [TestFixture]
    public class NfoGeneratorTests
    {
        private const string Template =
            "   ARTiST      : #Artist\n" +
            "   TiTLE       : #Album\n" +
            "   LABEL       : #Label\n" +
            "   GENRE       : #Genre\n" +
            " | STREET DATE : #Sdate\n" +
            " | RiP DATE    : #Rdate\n" +
            " | AVG BiTRATE : #Br kbps\n" +
            " | QUALiTY     : #KhzkHz / #Mode\n" +
            " | SiZE        : #Size MB\n" +
            " | URL         : #url\n" +
            " | TIME        : #MM:SS min\n" +
            "   (#)  (TRACK NAME)                    (TiME)\n" +
            "   #N. #Song                            #Time\n" +
            "                          TOTAL TiME:  #MM:SS min\n" +
            "  :: #Rnotes                            ::";

        [OneTimeSetUp]
        public void Setup()
        {
            IoC.ClearAllRegistrations();
            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());
        }

        private static UserProfile Profile()
        {
            var profile = new UserProfile { ProfileName = "GRP" };
            profile.FileNaming.SingleCD.SingleArtist.Directory = "<Artist>-<Album>-<Year>-GRP";
            profile.FileNaming.MultiCD.SingleArtist.Directory = "<Artist>-<Album>-<Discs>CD-<Year>-GRP";
            profile.NFOOptions.MultiCDHeader = "Disc #Disc/#Discs" + Environment.NewLine;
            profile.NFOOptions.TracklistFooter = Environment.NewLine + "   ----";
            profile.ReleaseInfo = new ReleaseInfoOptions { NfoDateFormat = "MM-DD-YYYY" };
            return profile;
        }

        private static Album Album(int discs = 1, int perDisc = 3)
        {
            var tracks = new List<AlbumTrack>();
            string[] titles = { "Koko", "Don't Shit Where You Eat", "Drifter In The Dark" };
            for (int d = 1; d <= discs; d++)
                for (int t = 1; t <= perDisc; t++)
                    tracks.Add(AlbumTrack.FromValues(@"C:\in\" + d + t + ".flac", d.ToString(), t.ToString(), "Ween", titles[(t - 1) % titles.Length], 1000, 125, 2 * 1048576));
            return new Album(@"C:\in", tracks) { Artist = "Ween", AlbumTitle = "Boognish Provides", Genre = "Other", ReleaseDate = "2014" };
        }

        private static ReleaseInfo Release(string notes = null)
        {
            return new ReleaseInfo
            {
                Label = "Some Label",
                RetailDate = "2014-00-00",
                RipDate = "2026-11-02",
                Url = "https://example.com/album",
                Notes = notes
            };
        }

        private static string[] Lines(string nfo)
        {
            return nfo.Replace("\r\n", "\n").Split('\n');
        }

        [Test]
        public void FillsAlbumAndReleaseFields()
        {
            string[] lines = Lines(new NfoGenerator(Profile(), Album(), Release()).Generate(Template));

            Assert.That(lines[0], Is.EqualTo("   ARTiST      : Ween"));
            Assert.That(lines[1], Is.EqualTo("   TiTLE       : Boognish Provides"));
            Assert.That(lines[2], Is.EqualTo("   LABEL       : Some Label"));
            Assert.That(lines[4], Is.EqualTo(" | STREET DATE : 00-00-2014"));
            Assert.That(lines[5], Is.EqualTo(" | RiP DATE    : 11-02-2026"));
            Assert.That(lines[6], Is.EqualTo(" | AVG BiTRATE : 1000 kbps"));
            Assert.That(lines[7], Is.EqualTo(" | QUALiTY     : 44.1kHz / Stereo"));
            Assert.That(lines[8], Is.EqualTo(" | SiZE        : 6.00 MB"));
            Assert.That(lines[9], Is.EqualTo(" | URL         : https://example.com/album"));
            Assert.That(lines[10], Is.EqualTo(" | TIME        : 6:15 min"));
        }

        [Test]
        public void RepeatsTrackLineAndKeepsTimeColumnAligned()
        {
            string[] lines = Lines(new NfoGenerator(Profile(), Album(), Release()).Generate(Template));
            string[] tracks = lines.Where(l => l.StartsWith("   ") && l.Contains("2:05")).ToArray();

            Assert.That(tracks.Length, Is.EqualTo(3));
            Assert.That(tracks[0].TrimEnd(), Does.StartWith("   1. Koko"));
            Assert.That(tracks[1], Does.StartWith("   2. Don't Shit Where You Eat"));
            int column = Template.Split('\n')[12].IndexOf("#Time");
            Assert.That(tracks.Select(t => t.IndexOf("2:05")).Distinct(), Is.EqualTo(new[] { column }), "time column stays aligned");
            Assert.That(lines.Any(l => l == "   ----"), Is.True, "tracklist footer");
        }

        [Test]
        public void MultiDiscGetsDiscHeaders()
        {
            string nfo = new NfoGenerator(Profile(), Album(discs: 2), Release()).Generate(Template);
            Assert.That(Lines(nfo).Count(l => l.StartsWith("Disc ")), Is.EqualTo(2));
            Assert.That(nfo, Does.Contain("Disc 1/2"));
            Assert.That(nfo, Does.Contain("Disc 2/2"));
        }

        [Test]
        public void ReleaseNotesWrapInsideTheBorder()
        {
            string notes = "This is a long release note that should wrap across several lines inside the art border nicely.";
            string[] lines = Lines(new NfoGenerator(Profile(), Album(), Release(notes)).Generate(Template));
            string[] noteLines = lines.Where(l => l.StartsWith("  :: ")).ToArray();

            Assert.That(noteLines.Length, Is.GreaterThan(1));
            Assert.That(noteLines.Select(l => l.Length).Distinct().Count(), Is.EqualTo(1), "right border stays in one column");
            Assert.That(noteLines.All(l => l.EndWith("::")), Is.True);
            Assert.That(string.Join(" ", noteLines.Select(l => l.Substring(5, l.Length - 7).Trim())), Is.EqualTo(notes));
        }

        [Test]
        public void EmptyNotesLeaveTheArtLine()
        {
            string[] lines = Lines(new NfoGenerator(Profile(), Album(), Release()).Generate(Template));
            Assert.That(lines.Last(), Does.StartWith("  :: ").And.EndWith("::"));
        }
    }

    internal static class StringTestExtensions
    {
        public static bool EndWith(this string text, string value)
        {
            return text.EndsWith(value, StringComparison.Ordinal);
        }
    }
}

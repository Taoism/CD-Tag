using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDTag.Common;
using CDTag.Common.ApplicationServices;
using CDTag.Common.Dispatcher;
using CDTag.Model.Profile;
using NUnit.Framework;

namespace CDTag.ViewModel.Tests.Profile
{
    /// <summary>
    /// Loads and saves real CD-Tag 2.x profiles in place. CDTAG_REAL_PROFILES names a folder of COPIES (the files are
    /// rewritten); real profiles are never part of the repo.
    /// </summary>
    [TestFixture]
    public class RealProfileTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            IoC.ClearAllRegistrations();
            IoC.RegisterInstance<IDispatcher>(new UnitTestDispatcher());
            IoC.RegisterInstance<IPathService>(new UnitTestPathService());
            IoC.RegisterInstance<IDialogService>(new UnitTestDialogService());
        }

        [Test]
        public void RealProfiles_SaveInPlaceKeepsEveryTwoXSetting()
        {
            string folder = Environment.GetEnvironmentVariable("CDTAG_REAL_PROFILES");
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                Assert.Ignore("Set CDTAG_REAL_PROFILES to a folder with copies of CD-Tag 2.x profiles.");

            string[] files = Directory.GetFiles(folder, "*.cfg").Where(LegacyProfile.IsLegacyFile).ToArray();
            Assert.That(files, Is.Not.Empty);
            foreach (string cfg in files)
            {
                Dictionary<string, string> before = Values(cfg);
                UserProfile profile = UserProfile.Load(cfg);
                string json = CDTag.Common.Json.JsonSerializer.SerializeObject(profile);

                LegacyProfileExport.Export(profile, folder, folder);

                Dictionary<string, string> after = Values(cfg);
                var lost = before.Where(kv => kv.Key != "CONFIG|LastUpdated" && (!after.ContainsKey(kv.Key) || after[kv.Key] != kv.Value))
                                 .Select(kv => kv.Key + " = '" + kv.Value + "' -> '" + (after.ContainsKey(kv.Key) ? after[kv.Key] : "(gone)") + "'").ToList();
                TestContext.Out.WriteLine("{0}: {1} values, template {2}", Path.GetFileName(cfg), before.Count, profile.NFOOptions.TemplatePath);
                foreach (string line in lost)
                    TestContext.Out.WriteLine("  changed " + line);
                Assert.That(lost, Is.Empty, Path.GetFileName(cfg));

                UserProfile reloaded = UserProfile.Load(cfg);
                Assert.That(CDTag.Common.Json.JsonSerializer.SerializeObject(reloaded), Is.EqualTo(json), Path.GetFileName(cfg) + " reads back the same");
            }
        }

        /// <summary>"SECTION|key" = value; empty values count as missing (2.x reads both the same).</summary>
        private static Dictionary<string, string> Values(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            foreach (string raw in File.ReadAllLines(path, LegacyProfile.DosEncoding))
            {
                string line = raw.TrimStart();
                if (line.Length == 0 || line[0] == ';')
                    continue;
                if (line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    section = end > 1 ? line.Substring(1, end - 1).Trim() : line.Substring(1).Trim();
                    continue;
                }
                int equals = raw.IndexOf('=');
                if (equals <= 0)
                    continue;
                string key = section + "|" + raw.Substring(0, equals).Trim();
                string value = raw.Substring(equals + 1);
                if (value.Length > 0 && !result.ContainsKey(key))
                    result[key] = value;
            }
            return result;
        }
    }
}

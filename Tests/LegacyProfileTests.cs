using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using _4RTools.Model;

namespace Vanilla.Diagnostics.Tests
{
    public static class LegacyProfileTests
    {
        public static int Run()
        {
            int failed = 0;
            var tests = new Dictionary<string, System.Action>
            {
                { "Stock profile switches reset missing sections to defaults", FreshDefaults },
                { "A malformed stock section cannot partially switch profiles", AtomicLoad },
                { "Stock object and action-string sections remain compatible", LegacyFormats }
            };
            foreach (var test in tests)
            {
                try { test.Value(); Console.WriteLine("PASS " + test.Key); }
                catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Key + ": " + ex); }
            }
            Console.WriteLine("Stock profile loading: {0} passed; {1} failed.", tests.Count - failed, failed);
            return failed;
        }

        private static void FreshDefaults()
        {
            WithProfiles(root =>
            {
                File.WriteAllText(Path.Combine(root, "First.json"), "{\"Autopot\":{\"hpPercent\":85},\"AHK20\":{\"AhkDelay\":75}}");
                ProfileSingleton.Load("First");
                var first = ProfileSingleton.GetCurrent();
                Assert(first.Autopot.hpPercent == 85 && first.AHK.AhkDelay == 75, "First profile did not load.");
                File.WriteAllText(Path.Combine(root, "Second.json"), "{}");
                ProfileSingleton.Load("Second");
                Assert(ProfileSingleton.GetCurrent().Autopot.hpPercent == 0 && ProfileSingleton.GetCurrent().AHK.AhkDelay == 10, "Missing section retained the previous character's settings.");
                Assert(first.Name == "First" && first.Autopot.hpPercent == 85, "Previous profile object was mutated.");
            });
        }

        private static void AtomicLoad()
        {
            WithProfiles(root =>
            {
                File.WriteAllText(Path.Combine(root, "Good.json"), "{}");
                ProfileSingleton.Load("Good");
                var previous = ProfileSingleton.GetCurrent();
                string bad = "{\"Autopot\":{\"hpPercent\":90},\"DebuffsRecovery\":null}";
                string badPath = Path.Combine(root, "Bad.json");
                File.WriteAllText(badPath, bad);
                Reject(() => ProfileSingleton.Load("Bad"));
                Assert(ReferenceEquals(previous, ProfileSingleton.GetCurrent()) && previous.Name == "Good", "A late failure partially loaded the new profile.");
                Assert(File.ReadAllText(badPath) == bad, "Malformed file was changed.");
                File.WriteAllText(badPath, "{\"UserPreferences\":{\"toggleStateKey\":\"NotAKey\"}}");
                Reject(() => ProfileSingleton.Load("Bad"));
                Assert(ReferenceEquals(previous, ProfileSingleton.GetCurrent()), "Invalid hotkey replaced the current profile.");
                File.WriteAllText(badPath, "null");
                Reject(() => ProfileSingleton.Load("Bad"));
                Assert(ReferenceEquals(previous, ProfileSingleton.GetCurrent()), "Null input replaced the current profile.");
            });
        }

        private static void LegacyFormats()
        {
            WithProfiles(root =>
            {
                var input = new JObject
                {
                    ["AHK"] = new JObject { ["AhkDelay"] = 42 },
                    ["AutoRefreshSpammer1"] = new JObject { ["RefreshDelay"] = 25 },
                    ["Autopot"] = "{\"hpPercent\":65}",
                    ["VanillaDiagnostics"] = "{\"PollIntervalMilliseconds\":1000}"
                };
                File.WriteAllText(Path.Combine(root, "Legacy.json"), input.ToString());
                ProfileSingleton.Load("Legacy");
                Assert(ProfileSingleton.GetCurrent().AHK.AhkDelay == 42 && ProfileSingleton.GetCurrent().AutoRefreshSpammer1.RefreshDelay == 25, "Object properties were not loaded.");
                Assert(ProfileSingleton.GetCurrent().Autopot.hpPercent == 65 && ProfileSingleton.GetCurrent().VanillaDiagnostics.PollIntervalMilliseconds == 1000, "JSON-string properties were not loaded.");
                input["AHK20"] = "{\"AhkDelay\":88}";
                File.WriteAllText(Path.Combine(root, "Legacy.json"), input.ToString());
                ProfileSingleton.Load("Legacy");
                Assert(ProfileSingleton.GetCurrent().AHK.AhkDelay == 88, "The latest explicit action key did not take precedence.");
            });
        }

        private static void WithProfiles(System.Action<string> test)
        {
            FieldInfo folder = typeof(ProfileSingleton).Assembly.GetType("_4RTools.Utils.AppConfig").GetField("ProfileFolder", BindingFlags.Public | BindingFlags.Static);
            string previousFolder = (string)folder.GetValue(null);
            Profile previousProfile = ProfileSingleton.profile;
            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "4RTools-LegacyProfileTests"));
            string root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                folder.SetValue(null, root + Path.DirectorySeparatorChar);
                test(root);
            }
            finally
            {
                folder.SetValue(null, previousFolder);
                ProfileSingleton.profile = previousProfile;
                string resolved = Path.GetFullPath(root);
                if (!resolved.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Test cleanup escaped its temporary directory.");
                if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
        }

        private static void Reject(System.Action action)
        {
            try { action(); }
            catch (InvalidDataException) { return; }
            throw new Exception("Expected malformed profile to fail.");
        }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using _4RTools.Model.Vanilla.Automation;

namespace Vanilla.Diagnostics.Tests
{
    public static class ProfileStoreTests
    {
        public static int Run()
        {
            int failed = 0;
            var tests = new Dictionary<string, System.Action>
            {
                { "Portable profiles start with safe defaults", Defaults },
                { "Saving preserves sequence and generic rule settings", RoundTrip },
                { "Profile paths reject traversal and Windows reserved names", Names },
                { "Malformed profiles are preserved on load and save", PreserveMalformed },
                { "Profile import validates before replacing existing settings", ImportValidation },
                { "Profile import rejects duplicates, metadata and future schemas", StrictImport },
                { "Oversized profile input is rejected", BoundedInput },
                { "Exported profiles transfer without machine identity", ExportTransfer },
                { "Failed file replacement preserves the previous profile", AtomicFailure }
            };
            foreach (var test in tests)
            {
                try { test.Value(); Console.WriteLine("PASS " + test.Key); }
                catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Key + ": " + ex); }
            }
            Console.WriteLine("Profile storage: {0} passed; {1} failed. Temporary local files only.", tests.Count - failed, failed);
            return failed;
        }

        private static void Defaults()
        {
            WithStore((store, root) =>
            {
                Assert(store.Names().SequenceEqual(new[] { "Default" }), "Default profile was not created.");
                var settings = store.Load("Default");
                Assert(settings.DryRun && !settings.Teleport.Enabled && !settings.SpRecovery.Enabled, "Unsafe initial settings.");
                Assert(File.Exists(Path.Combine(root, "Profiles", "Vanilla", "Default.json")), "Profile was not application-relative.");
            });
        }

        private static void RoundTrip()
        {
            WithStore((store, root) =>
            {
                var settings = Configured();
                store.Save("Farming", settings);
                settings.SpRecovery.Sequence.Clear();
                var loaded = store.Load("Farming");
                Assert(loaded.SpRecovery.Sequence.Count == 2 && loaded.SpRecovery.Sequence[0].Key == 118, "Saved settings shared mutable sequence state.");
                Assert(loaded.Rules.Count == 1 && loaded.Rules[0].Condition == RuleCondition.HealthBelow, "Generic rule was lost.");
                loaded.Teleport.CooldownMs = 21000;
                store.Save("Farming", loaded);
                Assert(store.Load("Farming").Teleport.CooldownMs == 21000, "Replacement was not saved.");
                Assert(!Directory.GetFiles(Path.Combine(root, "Profiles", "Vanilla"), "*.tmp").Any(), "Temporary files remain after save.");
            });
        }

        private static void Names()
        {
            WithStore((store, root) =>
            {
                foreach (string name in new[] { "", " ", "..", "../outside", "..\\outside", "C:\\outside", "file:stream", "trailing.", " leading", "trailing ", "CON", "con.other", "LPT1", "COM9", "AUX", new string('x', 65) })
                    Reject(() => store.Save(name, new VanillaAutomationSettings()));
                store.Save("Árvíztűrő 1", new VanillaAutomationSettings());
                Assert(store.Names().Contains("Árvíztűrő 1"), "A valid Unicode profile name was rejected.");
                Assert(!File.Exists(Path.Combine(root, "outside.json")), "A profile escaped its directory.");
            });
        }

        private static void PreserveMalformed()
        {
            WithStore((store, root) =>
            {
                string path = Path.Combine(root, "Profiles", "Vanilla", "Broken.json");
                const string broken = "{\"Version\": 1, this file is unfinished";
                File.WriteAllText(path, broken);
                Reject(() => store.Load("Broken"));
                Reject(() => store.Save("Broken", new VanillaAutomationSettings()));
                Assert(File.ReadAllText(path) == broken, "Malformed profile was overwritten.");
                Assert(store.Names().Contains("Broken"), "Malformed profile was silently hidden.");
                store.Save("Recovered", Configured());
                Assert(store.Load("Recovered").SpRecovery.Enabled, "Valid profiles cannot be saved alongside a malformed one.");
            });
        }

        private static void ImportValidation()
        {
            WithStore((store, root) =>
            {
                store.Save("Imported", Configured());
                string before = store.Load("Imported").ToJson();
                string path = Path.Combine(root, "import.json");
                File.WriteAllText(path, "{\"Version\":1,\"Teleport\":null}");
                Reject(() => store.Import(path, "Imported"));
                Assert(store.Load("Imported").ToJson() == before, "Bad import changed the existing profile.");
                File.WriteAllText(path, new VanillaAutomationSettings().ToJson());
                store.Import(path, "Imported");
                Assert(!store.Load("Imported").SpRecovery.Enabled, "A validated import did not replace the profile.");
            });
        }

        private static void StrictImport()
        {
            WithStore((store, root) =>
            {
                string path = Path.Combine(root, "invalid.json");
                foreach (string json in new[]
                {
                    "{}", "null", "[]", "{\"Version\":2}",
                    "{\"Version\":1,\"Version\":1}", "{\"Version\":1,\"version\":1}",
                    "{\"Version\":1,\"DryRun\":true,\"dryrun\":false}",
                    "{\"Version\":1,\"$type\":\"Anything\"}",
                    "{\"Version\":1,\"TypoSetting\":true}",
                    "{\"Version\":1} {\"Version\":1}",
                    "{\"Version\":1,\"SpRecovery\":{\"Enabled\":false,\"enabled\":true}}"
                })
                {
                    File.WriteAllText(path, json);
                    Reject(() => store.Import(path, "Invalid"));
                    Assert(!store.Names().Contains("Invalid"), "An invalid import created a profile.");
                }
            });
        }

        private static void BoundedInput()
        {
            WithStore((store, root) =>
            {
                string path = Path.Combine(root, "large.json");
                File.WriteAllText(path, new string(' ', 1024 * 1024 + 1));
                Reject(() => store.Import(path, "Large"));
                Assert(!store.Names().Contains("Large"), "Oversized input created a profile.");
            });
        }

        private static void ExportTransfer()
        {
            WithStore((store, root) =>
            {
                store.Save("Farming", Configured());
                string exported = Path.Combine(root, "farming-export.json");
                store.Export("Farming", exported);
                var other = new AutomationProfileStore(Path.Combine(root, "OtherComputer"));
                other.Import(exported, "Copied");
                Assert(other.Load("Copied").ToJson() == store.Load("Farming").ToJson(), "Export/import changed settings.");
                string content = File.ReadAllText(exported);
                Assert(!content.Contains(root) && !content.Contains("ProcessId") && !content.Contains("Handle"), "Machine identity leaked into profile.");
            });
        }

        private static void AtomicFailure()
        {
            WithStore((store, root) =>
            {
                string path = Path.Combine(root, "Profiles", "Vanilla", "Default.json");
                string before = File.ReadAllText(path);
                // A reader that disallows delete lets validation complete but prevents File.Replace.
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Reject(() => store.Save("Default", Configured()));
                Assert(File.ReadAllText(path) == before, "Failed replacement corrupted the current profile.");
                Assert(!Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp").Any(), "Failed save left temporary output.");
            });
        }

        private static VanillaAutomationSettings Configured()
        {
            var settings = new VanillaAutomationSettings();
            settings.SpRecovery.Enabled = true;
            settings.SpRecovery.Sequence.Add(new SequenceStep { Kind = SequenceStepKind.PressKey, Key = 118, DelayMs = 500 });
            settings.SpRecovery.Sequence.Add(new SequenceStep { Kind = SequenceStepKind.PressKey, Key = 119 });
            settings.Rules.Add(new AutomationRuleSettings
            {
                Name = "HP action", Condition = RuleCondition.HealthBelow,
                Sequence = new List<SequenceStep> { new SequenceStep { Key = 120 } }
            });
            return settings;
        }

        private static void WithStore(System.Action<AutomationProfileStore, string> test)
        {
            string testParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "4RTools-ProfileTests"));
            string root = Path.Combine(testParent, Guid.NewGuid().ToString("N"));
            try { test(new AutomationProfileStore(root), root); }
            finally
            {
                string resolved = Path.GetFullPath(root);
                if (!resolved.StartsWith(testParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Test cleanup escaped its temporary directory.");
                if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
        }

        private static void Reject(System.Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidDataException || ex is Newtonsoft.Json.JsonException || ex is UnauthorizedAccessException) { return; }
            throw new Exception("Expected invalid operation to be rejected.");
        }

        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using _4RTools.Model.Vanilla;

namespace Vanilla.Diagnostics.Tests
{
    internal static class VanillaReconnectRegressionTests
    {
        private static int passed, failed;
        internal static int Run()
        {
            Test("Fresh reconnect settings contain exactly two defaults", FreshDefaults);
            Test("Reconnect settings clone never appends defaults", CloneDoesNotDuplicate);
            Test("Legacy duplicated settings keep the two configured accounts", LegacyDuplicateMigration);
            Test("Runtime status contains only current configured accounts", StatusTracksCurrentSettings);
            Test("Vanilla Launcher.exe uses GAME START launcher mode", VanillaLauncherName);
            Test("Legacy version folders migrate into one persistent Profiles root", PersistentDataMigration);
            Test("Updater only accepts strictly newer semantic versions", UpdaterVersionComparison);
            Test("Legacy login anchors migrate to verified field centers", LoginAnchorMigration);
            Console.WriteLine("Reconnect regressions: {0} passed; {1} failed. No live process was controlled.", passed, failed);
            return failed;
        }
        private static void FreshDefaults()
        {
            string root = Temp();
            try { var value = new VanillaReconnectStore(root).Load(); Assert(value.Accounts.Count == 2, "Fresh settings need two rows."); }
            finally { Delete(root); }
        }
        private static void CloneDoesNotDuplicate()
        {
            var value = VanillaReconnectSettings.CreateDefault();
            value.Accounts[0].Label = "ender02"; value.Accounts[0].UserName = "ender02"; value.Accounts[0].ProtectedPassword = "encrypted-a";
            value.Accounts[1].Label = "slave"; value.Accounts[1].UserName = "ender03"; value.Accounts[1].ProtectedPassword = "encrypted-b";
            var clone = value.Clone();
            Assert(clone.Accounts.Count == 2 && clone.Accounts[0].Label == "ender02" && clone.Accounts[1].Label == "slave", "Clone changed or duplicated rows.");
        }
        private static void LegacyDuplicateMigration()
        {
            string root = Temp();
            try
            {
                var legacy = VanillaReconnectSettings.CreateDefault();
                legacy.Accounts.Add(new VanillaReconnectAccount { Label = "ender02", UserName = "ender02", ProtectedPassword = "encrypted-a", CharacterSlot = 4 });
                legacy.Accounts.Add(new VanillaReconnectAccount { Label = "slave", UserName = "ender03", ProtectedPassword = "encrypted-b", CharacterSlot = 1 });
                string dir = Path.Combine(root, "VanillaReconnect"); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "reconnect.json"), JsonConvert.SerializeObject(legacy, Formatting.Indented));
                var loaded = new VanillaReconnectStore(root).Load();
                Assert(loaded.Accounts.Count == 2 && loaded.Accounts[0].Label == "ender02" && loaded.Accounts[1].Label == "slave", "Configured rows did not win over synthetic defaults.");
            }
            finally { Delete(root); }
        }
        private static void StatusTracksCurrentSettings()
        {
            string root = Temp();
            try
            {
                using (var supervisor = new VanillaReconnectSupervisor(root))
                {
                    var value = supervisor.Settings; value.Accounts.RemoveAt(1); supervisor.Apply(value, false);
                    var rows = supervisor.Statuses(); Assert(rows.Count == 1 && rows[0].Label == "Client 1", "Removed row lingered in status.");
                }
            }
            finally { Delete(root); }
        }
        private static void VanillaLauncherName()
        {
            Type type = typeof(VanillaReconnectSettings).Assembly.GetType("_4RTools.Model.Vanilla.VanillaPatcherLauncher", true);
            MethodInfo method = type.GetMethod("IsPatcher", BindingFlags.Static | BindingFlags.NonPublic);
            Assert((bool)method.Invoke(null, new object[] { @"C:\Games\Vanilla RO\Vanilla Launcher.exe" }), "Vanilla Launcher.exe was not recognized.");
            Assert((bool)method.Invoke(null, new object[] { @"C:\Games\Vanilla RO\patcher.exe" }), "patcher.exe regressed.");
        }
        private static void LoginAnchorMigration()
        {
            var legacy = VanillaReconnectSettings.CreateDefault();
            legacy.Anchors.UserNameY = 0.66;
            legacy.Anchors.PasswordY = 0.685;
            var migrated = legacy.Clone();
            Assert(Math.Abs(migrated.Anchors.UserNameY - 0.677) < 0.0001 && Math.Abs(migrated.Anchors.PasswordY - 0.697) < 0.0001,
                "Legacy login-field coordinates were not migrated.");

            var interim = VanillaReconnectSettings.CreateDefault();
            interim.Anchors.UserNameY = 0.635;
            interim.Anchors.PasswordY = 0.660;
            migrated = interim.Clone();
            Assert(Math.Abs(migrated.Anchors.UserNameY - 0.677) < 0.0001 && Math.Abs(migrated.Anchors.PasswordY - 0.697) < 0.0001,
                "Interim login-field coordinates were not migrated.");
        }
        private static void PersistentDataMigration()
        {
            string root = Temp();
            string previous = Environment.GetEnvironmentVariable(VanillaAppData.DataRootEnvironmentVariable);
            try
            {
                string oldInstall = Path.Combine(root, "4RTools-Vanilla-v0.6.0");
                string newInstall = Path.Combine(root, "4RTools-Vanilla-v0.6.1");
                string data = Path.Combine(root, "user-data");
                Directory.CreateDirectory(Path.Combine(oldInstall, "Profile"));
                Directory.CreateDirectory(Path.Combine(oldInstall, "Profiles", "Vanilla"));
                Directory.CreateDirectory(Path.Combine(oldInstall, "VanillaReconnect"));
                Directory.CreateDirectory(newInstall);
                File.WriteAllText(Path.Combine(oldInstall, "Profile", "Hunter.json"), "{}");
                File.WriteAllText(Path.Combine(oldInstall, "Profiles", "Vanilla", "Farm.json"), "{}");
                File.WriteAllText(Path.Combine(oldInstall, "VanillaReconnect", "reconnect.json"), "{}");
                File.WriteAllText(Path.Combine(oldInstall, "supported_servers.json"), "[]");
                Environment.SetEnvironmentVariable(VanillaAppData.DataRootEnvironmentVariable, data);
                VanillaAppData.InitializeAndMigrateLegacy(newInstall);
                Assert(File.Exists(Path.Combine(data, "Profiles", "Stock", "Hunter.json")), "Stock profile did not migrate.");
                Assert(File.Exists(Path.Combine(data, "Profiles", "Vanilla", "Farm.json")), "Vanilla profile did not migrate.");
                Assert(File.Exists(Path.Combine(data, "VanillaReconnect", "reconnect.json")), "Reconnect settings did not migrate.");
                Assert(File.Exists(Path.Combine(data, "supported_servers.json")), "Local server settings did not migrate.");
                Assert(File.Exists(Path.Combine(oldInstall, "VanillaReconnect", "reconnect.json")), "Sibling release should remain a rollback backup.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(VanillaAppData.DataRootEnvironmentVariable, previous);
                Delete(root);
            }
        }
        private static void UpdaterVersionComparison()
        {
            Assert(VanillaUpdater.IsNewerVersion(new Version(0, 6, 2), new Version(0, 6, 1)), "Newer patch version was rejected.");
            Assert(!VanillaUpdater.IsNewerVersion(new Version(0, 6, 1), new Version(0, 6, 1)), "Equal version was treated as an update.");
            Assert(!VanillaUpdater.IsNewerVersion(new Version(0, 5, 9), new Version(0, 6, 1)), "Older version was treated as an update.");
        }        private static string Temp() { string p = Path.Combine(Path.GetTempPath(), "4rtools-reconnect-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
        private static void Delete(string p) { try { Directory.Delete(p, true); } catch { } }
        private static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); } }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    }
}
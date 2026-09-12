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
        private static string Temp() { string p = Path.Combine(Path.GetTempPath(), "4rtools-reconnect-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
        private static void Delete(string p) { try { Directory.Delete(p, true); } catch { } }
        private static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); } }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    }
}
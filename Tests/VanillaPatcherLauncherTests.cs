using System;
using System.IO;
using System.Reflection;
using _4RTools.Model.Vanilla;

namespace Vanilla.Diagnostics.Tests
{
    internal static class VanillaPatcherLauncherTests
    {
        private static int passed;
        private static int failed;

        internal static int Run()
        {
            Test("Patcher executable detection is exact and case-insensitive", PatcherDetection);
            Test("Patcher beside Vanilla client is preferred when present", PreferAdjacentPatcher);
            Test("GAME START visual detector is available", VisualDetectorAvailable);
            Console.WriteLine("Patcher launcher: {0} passed; {1} failed. No processes were started.", passed, failed);
            return failed;
        }

        private static void PatcherDetection()
        {
            Type type = LauncherType();
            MethodInfo method = type.GetMethod("IsPatcher", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(method != null, "IsPatcher helper is missing.");
            Assert((bool)method.Invoke(null, new object[] { @"C:\Games\Vanilla RO\patcher.exe" }), "patcher.exe must use launcher mode.");
            Assert((bool)method.Invoke(null, new object[] { @"C:\Games\Vanilla RO\PATCHER.EXE" }), "Patcher detection must ignore case.");
            Assert((bool)method.Invoke(null, new object[] { @"C:\Games\Vanilla RO\Vanilla Launcher.exe" }), "Vanilla Launcher.exe must use GAME START launcher mode.");
            Assert(!(bool)method.Invoke(null, new object[] { @"C:\Games\Vanilla RO\Vanilla MMO.exe" }), "Direct game executable must not be treated as patcher.exe.");
        }

        private static void PreferAdjacentPatcher()
        {
            string root = Path.Combine(Path.GetTempPath(), "4rtools-patcher-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string client = Path.Combine(root, "Vanilla MMO.exe");
                string patcher = Path.Combine(root, "patcher.exe");
                File.WriteAllBytes(client, new byte[] { 1 });

                Type type = LauncherType();
                MethodInfo method = type.GetMethod("PreferPatcherBesideClient", BindingFlags.Static | BindingFlags.NonPublic);
                Assert(method != null, "PreferPatcherBesideClient helper is missing.");
                Equal(client, (string)method.Invoke(null, new object[] { client }), "Without patcher.exe, keep the client path.");

                File.WriteAllBytes(patcher, new byte[] { 1 });
                Equal(patcher, (string)method.Invoke(null, new object[] { client }), "Adjacent patcher.exe must be preferred.");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static void VisualDetectorAvailable()
        {
            MethodInfo method = LauncherType().GetMethod("TryFindGameStart", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(method != null, "TryFindGameStart helper is missing.");
            object[] args = { null, 0d, 0d, null };
            Assert(!(bool)method.Invoke(null, args), "A null launcher image must not produce a GAME START candidate.");
        }
        private static Type LauncherType()
        {
            Type type = typeof(VanillaReconnectSettings).Assembly.GetType("_4RTools.Model.Vanilla.VanillaPatcherLauncher", true);
            return type;
        }

        private static void Test(string name, Action test)
        {
            try
            {
                test();
                passed++;
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine("FAIL " + name + ": " + ex);
            }
        }

        private static void Assert(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        private static void Equal(string expected, string actual, string message)
        {
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new Exception(message + " Expected: " + expected + "; actual: " + actual);
        }
    }
}

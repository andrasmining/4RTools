using System;
using System.IO;
using System.Reflection;
using _4RTools.Model.Vanilla;

namespace Vanilla.Diagnostics.Tests
{
    internal static class VanillaSessionLogTests
    {
        private static int passed, failed;
        internal static int Run()
        {
            Test("Session log uses a fresh named file", FreshPath);
            Test("Session log rotates at configured size", Rotation);
            Test("Legacy reconnect.log is archived", LegacyArchive);
            Console.WriteLine("Session logging: {0} passed; {1} failed.", passed, failed);
            return failed;
        }

        private static void FreshPath()
        {
            string root = Temp();
            try
            {
                object log = Create(root, 2048, 8192, "session-a");
                string path = Current(log);
                Assert(Path.GetFileName(path).StartsWith("reconnect-session-a", StringComparison.Ordinal), "Unexpected log name: " + path);
                Assert(File.Exists(path), "Session log must exist.");
            }
            finally { Cleanup(root); }
        }

        private static void Rotation()
        {
            string root = Temp();
            try
            {
                object log = Create(root, 1024, 8192, "rotation");
                MethodInfo write = LogType().GetMethod("WriteLine", BindingFlags.Instance | BindingFlags.NonPublic);
                string first = Current(log);
                for (int i = 0; i < 20; i++) write.Invoke(log, new object[] { new string('x', 180) });
                string current = Current(log);
                Assert(!string.Equals(first, current, StringComparison.OrdinalIgnoreCase), "Log should rotate to another part.");
                Assert(new FileInfo(current).Length < 1400, "Rotated part should remain bounded.");
            }
            finally { Cleanup(root); }
        }

        private static void LegacyArchive()
        {
            string root = Temp();
            try
            {
                string dir = Path.Combine(root, "Logs"); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "reconnect.log"), "legacy");
                Create(root, 2048, 8192, "archive");
                Assert(!File.Exists(Path.Combine(dir, "reconnect.log")), "Legacy fixed log should be moved.");
                Assert(Directory.GetFiles(dir, "reconnect-legacy-*.log").Length == 1, "Legacy archive is missing.");
            }
            finally { Cleanup(root); }
        }

        private static Type LogType() { return typeof(VanillaReconnectSettings).Assembly.GetType("_4RTools.Model.Vanilla.VanillaSessionLog", true); }
        private static object Create(string root, long maxFile, long maxDir, string token)
        {
            return Activator.CreateInstance(LogType(), BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { root, maxFile, maxDir, token }, null);
        }
        private static string Current(object log)
        {
            return (string)LogType().GetProperty("CurrentPath", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(log, null);
        }
        private static string Temp() { string p = Path.Combine(Path.GetTempPath(), "4rtools-log-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
        private static void Cleanup(string root) { try { Directory.Delete(root, true); } catch { } }
        private static void Test(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
        }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace _4RTools.Model.Vanilla
{
    internal static class VanillaDebugLog
    {
        private sealed class DebugSettings
        {
            public int Version { get; set; } = 1;
            public bool Enabled { get; set; } = true;
        }

        private static readonly object Gate = new object();
        private static bool initialized;
        private static bool enabled = true;
        private static string SettingsPath { get { return Path.Combine(VanillaAppData.RootDirectory, "debug.json"); } }
        internal static string LogPath { get { return Path.Combine(VanillaAppData.LogsDirectory, "debug.log"); } }

        internal static bool Enabled
        {
            get { EnsureInitialized(); lock (Gate) return enabled; }
        }

        internal static void Initialize()
        {
            EnsureInitialized();
            Write("APP", "Global debug logging initialized. version=" + SafeVersion()
                + ", pid=" + Process.GetCurrentProcess().Id + ", base='" + AppDomain.CurrentDomain.BaseDirectory
                + "', cwd='" + Environment.CurrentDirectory + "'.");
        }

        internal static void SetEnabled(bool value)
        {
            EnsureInitialized();
            lock (Gate)
            {
                enabled = value;
                try
                {
                    Directory.CreateDirectory(VanillaAppData.RootDirectory);
                    File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(new DebugSettings { Enabled = value }, Formatting.Indented));
                }
                catch { }
            }
            if (value) Write("APP", "Debug logging ENABLED by UI.");
        }

        internal static void Write(string category, string message)
        {
            EnsureInitialized();
            lock (Gate)
            {
                if (!enabled) return;
                try
                {
                    Directory.CreateDirectory(VanillaAppData.LogsDirectory);
                    string line = DateTimeOffset.Now.ToString("O") + " [" + Clean(category) + "] " + (message ?? "") + Environment.NewLine;
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                }
                catch { }
            }
        }

        internal static string BuildClipboardBundle(string reconnectCurrentPath)
        {
            EnsureInitialized();
            Write("UI", "COPY FULL DEBUG LOG requested.");
            var text = new StringBuilder(65536);
            text.AppendLine("=== 4RTOOLS VANILLA GLOBAL DEBUG BUNDLE ===");
            text.AppendLine("Generated: " + DateTimeOffset.Now.ToString("O"));
            text.AppendLine("Version: " + SafeVersion());
            text.AppendLine("Debug enabled: " + Enabled);
            text.AppendLine("Data root: " + VanillaAppData.RootDirectory);
            text.AppendLine();

            try
            {
                text.Append(VanillaMemoryAccessDiagnostics.BuildClipboardBundle(reconnectCurrentPath));
                if (text.Length > 0 && text[text.Length - 1] != '\n') text.AppendLine();
            }
            catch (Exception ex)
            {
                text.AppendLine("MEMORY/RECONNECT BUNDLE FAILED: " + ex.Message);
            }

            var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(VanillaAppData.LogsDirectory))
                {
                    foreach (string path in Directory.GetFiles(VanillaAppData.LogsDirectory, "*", SearchOption.TopDirectoryOnly)
                        .Where(p => string.Equals(Path.GetExtension(p), ".log", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(Path.GetExtension(p), ".txt", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        string full = Path.GetFullPath(path);
                        if (!included.Add(full)) continue;
                        AppendFile(text, full);
                    }
                }
            }
            catch (Exception ex)
            {
                text.AppendLine("LOG DIRECTORY ENUMERATION FAILED: " + ex.Message);
            }

            text.AppendLine("=== END GLOBAL DEBUG BUNDLE ===");
            return text.ToString();
        }

        private static void AppendFile(StringBuilder text, string path)
        {
            text.AppendLine();
            text.AppendLine("=== LOG FILE: " + Path.GetFileName(path) + " ===");
            text.AppendLine("Path: " + path);
            try
            {
                var info = new FileInfo(path);
                const int maxBytes = 4 * 1024 * 1024;
                if (info.Length <= maxBytes)
                {
                    text.Append(File.ReadAllText(path));
                }
                else
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        stream.Seek(-maxBytes, SeekOrigin.End);
                        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                        {
                            text.AppendLine("(file larger than 4 MB; including final 4 MB)");
                            text.Append(reader.ReadToEnd());
                        }
                    }
                }
                if (text.Length > 0 && text[text.Length - 1] != '\n') text.AppendLine();
            }
            catch (Exception ex)
            {
                text.AppendLine("(could not read file: " + ex.Message + ")");
            }
        }

        private static void EnsureInitialized()
        {
            lock (Gate)
            {
                if (initialized) return;
                initialized = true;
                enabled = true; // DEBUG MODE DEFAULT ON during current hardening cycle.
                try
                {
                    if (File.Exists(SettingsPath))
                    {
                        var saved = JsonConvert.DeserializeObject<DebugSettings>(File.ReadAllText(SettingsPath));
                        if (saved != null && saved.Version == 1) enabled = saved.Enabled;
                    }
                }
                catch { enabled = true; }

                try
                {
                    AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    {
                        try { Write("UNHANDLED", e.ExceptionObject == null ? "unknown exception" : e.ExceptionObject.ToString()); } catch { }
                    };
                }
                catch { }
            }
        }

        private static string SafeVersion()
        {
            try { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
            catch { return "unknown"; }
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "DEBUG" : value.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}

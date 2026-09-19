using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
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
            try
            {
                Write("HOST", VanillaHostDiagnostics.Build().Replace("\r", " ").Replace("\n", " | "));
            }
            catch (Exception ex) { Write("HOST", "Host diagnostics failed: " + ex); }
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
                    VanillaLogRotation.Append(LogPath, "debug", line);
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
                text.AppendLine(BuildRecentActionSummary(ReadDebugHistoryLines(DateTimeOffset.Now.AddHours(-24)),
                    DateTimeOffset.Now.AddHours(-24)));
            }
            catch (Exception ex) { text.AppendLine("24h action summary unavailable: " + ex.Message); }
            text.AppendLine();
            try { text.Append(VanillaHostDiagnostics.Build()); }
            catch (Exception ex) { text.AppendLine("HOST DIAGNOSTICS FAILED: " + ex); }
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

            // The memory/reconnect bundle above already contributes the current
            // reconnect, memory-access, Vanilla core and update-error logs. Include only
            // the current debug session here; archived sessions remain separate files on disk
            // so COPY DEBUG LOG cannot accidentally concatenate many historical 10 MB logs.
            AppendFile(text, LogPath);

            text.AppendLine("=== END GLOBAL DEBUG BUNDLE ===");
            return text.ToString();
        }

        private static IEnumerable<string> ReadDebugHistoryLines(DateTimeOffset cutoff)
        {
            if (!Directory.Exists(VanillaAppData.LogsDirectory)) yield break;
            string[] files;
            try
            {
                files = Directory.GetFiles(VanillaAppData.LogsDirectory, "debug*.log", SearchOption.TopDirectoryOnly)
                    .Where(path =>
                    {
                        try { return File.GetLastWriteTimeUtc(path) >= cutoff.UtcDateTime.AddDays(-1); }
                        catch { return false; }
                    })
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch { yield break; }

            foreach (string path in files)
            {
                IEnumerable<string> lines;
                try { lines = File.ReadLines(path); }
                catch { continue; }
                foreach (string line in lines) yield return line;
            }
        }

        internal static string BuildRecentActionSummary(IEnumerable<string> lines, DateTimeOffset cutoff)
        {
            int teleportAttempts = 0, teleportComplete = 0, teleportFailed = 0, teleportCancelled = 0;
            int cartAttempts = 0, cartComplete = 0, cartFailed = 0, cartCancelled = 0, cartHolds = 0, cartItems = 0;
            foreach (string line in lines ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                int firstSpace = line.IndexOf(' ');
                DateTimeOffset timestamp;
                if (firstSpace <= 0 || !DateTimeOffset.TryParse(line.Substring(0, firstSpace),
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp) || timestamp < cutoff)
                    continue;

                if (line.Contains("event=teleport-start")) teleportAttempts++;
                if (line.Contains("event=teleport-complete")) teleportComplete++;
                if (line.Contains("event=teleport-failed")) teleportFailed++;
                if (line.Contains("event=teleport-cancelled")) teleportCancelled++;
                if (line.Contains("event=cart-start")) cartAttempts++;
                if (line.Contains("event=cart-complete"))
                {
                    cartComplete++;
                    cartItems += ExtractIntField(line, "items=");
                }
                if (line.Contains("event=cart-failed")) cartFailed++;
                if (line.Contains("event=cart-cancelled")) cartCancelled++;
                if (line.Contains("event=cart-manual-hold")) cartHolds++;
            }

            var summary = new StringBuilder();
            summary.AppendLine("=== LAST 24 HOURS AUTOMATION ACTION SUMMARY ===");
            summary.AppendLine("Since: " + cutoff.ToString("O"));
            summary.AppendLine("Smart Teleport: attempts=" + teleportAttempts + ", completed=" + teleportComplete
                + ", failed=" + teleportFailed + ", cancelled=" + teleportCancelled + ".");
            summary.AppendLine("Weight/Cart: attempts=" + cartAttempts + ", completed=" + cartComplete
                + ", itemsMoved=" + cartItems + ", failed=" + cartFailed + ", cancelled=" + cartCancelled
                + ", manualHolds=" + cartHolds + ".");
            return summary.ToString().TrimEnd();
        }

        private static int ExtractIntField(string line, string marker)
        {
            int start = line == null ? -1 : line.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return 0;
            start += marker.Length;
            int end = start;
            while (end < line.Length && char.IsDigit(line[end])) end++;
            int value;
            return end > start && int.TryParse(line.Substring(start, end - start), NumberStyles.None,
                CultureInfo.InvariantCulture, out value) ? value : 0;
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
                    Directory.CreateDirectory(VanillaAppData.LogsDirectory);
                    // Every 4RTools process gets a fresh debug.log. The previous live file
                    // becomes a timestamped archive before this process writes its first line.
                    VanillaLogRotation.StartNewSession(LogPath, "debug");
                }
                catch { }

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

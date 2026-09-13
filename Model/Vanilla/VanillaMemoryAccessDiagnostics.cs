using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using _4RTools.Utils;

namespace _4RTools.Model.Vanilla
{
    internal static class VanillaMemoryAccessDiagnostics
    {
        private const uint ProcessVmRead = 0x0010;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint Synchronize = 0x00100000;
        private const uint Th32csSnapModule = 0x00000008;
        private const uint Th32csSnapModule32 = 0x00000010;
        private static readonly object Gate = new object();
        private static readonly HashSet<int> LoggedPids = new HashSet<int>();
        private static bool headerWritten;

        internal static string LogPath { get { return Path.Combine(VanillaAppData.LogsDirectory, "memory-access.log"); } }

        internal static void CaptureCurrentClients()
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName("Vanilla MMO"); }
            catch (Exception ex)
            {
                Write("ENUMERATION FAILED: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            try
            {
                foreach (Process process in processes)
                {
                    int pid;
                    try { pid = process.Id; }
                    catch { continue; }
                    bool shouldLog;
                    lock (Gate) shouldLog = LoggedPids.Add(pid);
                    if (shouldLog) CapturePid(pid);
                }
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
        }

        internal static void RecordObservationFailure(int pid, Exception error)
        {
            if (error == null) return;
            Write("OBSERVATION FAILURE PID=" + pid + ": " + error.GetType().FullName + ": " + error.Message);
        }

        internal static string BuildClipboardBundle(string reconnectCurrentPath)
        {
            CaptureCurrentClients();
            var text = new StringBuilder(32768);
            text.AppendLine("=== 4RTOOLS VANILLA FULL DEBUG LOG ===");
            text.AppendLine("Generated UTC: " + DateTimeOffset.UtcNow.ToString("O"));
            try { text.AppendLine("4RTools version: " + Assembly.GetExecutingAssembly().GetName().Version); } catch { }
            text.AppendLine("Observer: " + ProcessObservationContext.Current);
            text.AppendLine("Data root: " + VanillaAppData.RootDirectory);
            text.AppendLine();

            AppendFileSection(text, "MEMORY ACCESS DEBUG", LogPath);

            string[] reconnectFiles = CurrentReconnectSessionFiles(reconnectCurrentPath);
            if (reconnectFiles.Length == 0)
                AppendMissingSection(text, "RECONNECT SESSION", reconnectCurrentPath);
            else
                foreach (string file in reconnectFiles)
                    AppendFileSection(text, "RECONNECT SESSION - " + Path.GetFileName(file), file);

            AppendFileSection(text, "VANILLA CORE LOG", Path.Combine(VanillaAppData.LogsDirectory, "vanilla.log"));
            AppendFileSection(text, "UPDATE ERROR LOG", Path.Combine(VanillaAppData.LogsDirectory, "update-error.log"));

            text.AppendLine("=== END FULL DEBUG LOG ===");
            return text.ToString();
        }

        private static string[] CurrentReconnectSessionFiles(string currentPath)
        {
            if (string.IsNullOrWhiteSpace(currentPath)) return new string[0];
            try
            {
                string full = Path.GetFullPath(currentPath);
                string directory = Path.GetDirectoryName(full);
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return new string[0];
                string stem = Path.GetFileNameWithoutExtension(full);
                int part = stem.LastIndexOf("-part", StringComparison.OrdinalIgnoreCase);
                if (part > 0)
                {
                    string suffix = stem.Substring(part + 5);
                    int ignored;
                    if (int.TryParse(suffix, out ignored)) stem = stem.Substring(0, part);
                }
                return Directory.GetFiles(directory, stem + "*.log", SearchOption.TopDirectoryOnly)
                    .Where(path =>
                    {
                        string candidate = Path.GetFileNameWithoutExtension(path);
                        if (string.Equals(candidate, stem, StringComparison.OrdinalIgnoreCase)) return true;
                        if (!candidate.StartsWith(stem + "-part", StringComparison.OrdinalIgnoreCase)) return false;
                        int value;
                        return int.TryParse(candidate.Substring((stem + "-part").Length), out value);
                    })
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch { return new string[0]; }
        }

        private static void AppendFileSection(StringBuilder text, string caption, string path)
        {
            text.AppendLine("=== " + caption + " ===");
            text.AppendLine("Path: " + (string.IsNullOrWhiteSpace(path) ? "(none)" : path));
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                text.AppendLine("(file not present)");
                text.AppendLine();
                return;
            }
            try
            {
                text.Append(File.ReadAllText(path));
                if (text.Length > 0 && text[text.Length - 1] != '\n') text.AppendLine();
            }
            catch (Exception ex) { text.AppendLine("(could not read file: " + ex.Message + ")"); }
            text.AppendLine();
        }

        private static void AppendMissingSection(StringBuilder text, string caption, string path)
        {
            text.AppendLine("=== " + caption + " ===");
            text.AppendLine("Path: " + (string.IsNullOrWhiteSpace(path) ? "(none)" : path));
            text.AppendLine("(no current reconnect session log found)");
            text.AppendLine();
        }

        private static void CapturePid(int pid)
        {
            try
            {
                WriteHeaderOnce();
                string configuredTarget = ResolveConfiguredTarget();
                string targetFile = DescribeFile(configuredTarget);
                string targetSession = SessionText(pid);
                string vmRead = ProbeOpen(pid, ProcessVmRead, "VM_READ 0x0010");
                string query = ProbeOpen(pid, ProcessQueryLimitedInformation, "QUERY_LIMITED 0x1000");
                string sync = ProbeOpen(pid, Synchronize, "SYNCHRONIZE 0x00100000");
                string combined = ProbeOpen(pid, ProcessVmRead | ProcessQueryLimitedInformation, "VM_READ|QUERY_LIMITED 0x1010");
                string modules = ProbeModules(pid);
                Write("TARGET PID=" + pid + "; session=" + targetSession + "; configuredTarget=" + targetFile
                    + "; rights=[" + vmRead + "; " + query + "; " + sync + "; " + combined + "]"
                    + "; moduleSnapshot=" + modules + ".");
                WriteBuildProfile();
                WriteRelatedGameFiles(configuredTarget);
            }
            catch (Exception ex)
            {
                Write("TARGET PID=" + pid + "; diagnostic capture failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void WriteHeaderOnce()
        {
            lock (Gate)
            {
                if (headerWritten) return;
                headerWritten = true;
            }

            string observerPath = null;
            try { observerPath = Process.GetCurrentProcess().MainModule.FileName; } catch { }
            string version = null;
            try { version = Assembly.GetExecutingAssembly().GetName().Version.ToString(); } catch { version = "unknown"; }
            string observerFile = DescribeFile(observerPath);
            Write("SESSION 4RToolsVersion=" + version + "; observer=" + observerFile + "; "
                + ProcessObservationContext.Current + "; OS=" + Environment.OSVersion.VersionString
                + "; currentDirectory='" + Environment.CurrentDirectory + "'; dataRoot='" + VanillaAppData.RootDirectory + "'.");
        }

        private static string ResolveConfiguredTarget()
        {
            try
            {
                string reconnect = VanillaAppData.ReconnectSettingsPath;
                if (!File.Exists(reconnect)) return null;
                JObject json = JObject.Parse(File.ReadAllText(reconnect));
                string launch = (string)json["LaunchExecutable"];
                return ReadOnlyProcessMemory.ResolveVanillaExecutableFromLaunch(launch);
            }
            catch (Exception ex)
            {
                Write("CONFIG target resolution failed: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static void WriteBuildProfile()
        {
            try
            {
                string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VanillaBuilds");
                if (!Directory.Exists(directory))
                {
                    Write("BUILD PROFILE directory missing: '" + directory + "'.");
                    return;
                }
                foreach (string path in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    Write("BUILD PROFILE " + DescribeFile(path));
            }
            catch (Exception ex) { Write("BUILD PROFILE scan failed: " + ex.Message); }
        }

        private static string SessionText(int pid)
        {
            uint session;
            if (Native.ProcessIdToSessionId((uint)pid, out session)) return session.ToString();
            return "error Win32 " + Marshal.GetLastWin32Error();
        }

        private static string ProbeOpen(int pid, uint access, string label)
        {
            IntPtr handle = Native.OpenProcess(access, false, pid);
            if (handle == IntPtr.Zero)
                return label + "=DENIED Win32 " + Marshal.GetLastWin32Error();
            try { return label + "=OK"; }
            finally { Native.CloseHandle(handle); }
        }

        private static string ProbeModules(int pid)
        {
            IntPtr snapshot = Native.CreateToolhelp32Snapshot(Th32csSnapModule | Th32csSnapModule32, (uint)pid);
            if (snapshot == new IntPtr(-1))
                return "DENIED Win32 " + Marshal.GetLastWin32Error();
            try { return "OK"; }
            finally { Native.CloseHandle(snapshot); }
        }

        private static string DescribeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "unresolved";
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return "'" + path + "' missing";
                string hash = HashFile(path);
                return "'" + path + "' size=" + info.Length + " modifiedUtc="
                    + info.LastWriteTimeUtc.ToString("O") + " sha256=" + hash;
            }
            catch (Exception ex) { return "'" + path + "' metadataError=" + ex.Message; }
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] hash = sha.ComputeHash(stream);
                var text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        private static void WriteRelatedGameFiles(string configuredTarget)
        {
            if (string.IsNullOrWhiteSpace(configuredTarget)) return;
            string directory;
            try { directory = Path.GetDirectoryName(configuredTarget); }
            catch { return; }
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;

            string[] keywords = { "gepard", "shield", "guard", "anti" };
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => keywords.Any(keyword => Path.GetFileName(path).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Write("RELATED FILE scan failed for '" + directory + "': " + ex.Message);
                return;
            }

            if (files.Length == 0)
            {
                Write("RELATED FILES directory='" + directory + "': no filename matched gepard/shield/guard/anti.");
                return;
            }
            foreach (string file in files) Write("RELATED FILE " + DescribeFile(file));
        }

        private static void Write(string message)
        {
            try
            {
                string directory = VanillaAppData.LogsDirectory;
                Directory.CreateDirectory(directory);
                string line = DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine;
                lock (Gate) File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
            catch { }
        }

        private static class Native
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool CloseHandle(IntPtr value);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
        }
    }
}

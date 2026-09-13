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
                + "; currentDirectory='" + Environment.CurrentDirectory + "'.");
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
                string path = Path.Combine(directory, "memory-access.log");
                string line = DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine;
                lock (Gate) File.AppendAllText(path, line, Encoding.UTF8);
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

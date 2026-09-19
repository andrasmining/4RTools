using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using _4RTools.Utils;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Diagnostic-only target security inspection. It uses only PROCESS_QUERY_LIMITED_INFORMATION
    /// and TOKEN_QUERY so we can explain why PROCESS_VM_READ is denied without changing privileges,
    /// duplicating handles, modifying tokens, or attempting any alternate memory-access route.
    /// </summary>
    internal static class VanillaTargetSecurityDiagnostics
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint TokenQuery = 0x0008;
        private const int TokenUser = 1;
        private const int TokenElevation = 20;
        private const int TokenIntegrityLevel = 25;
        private const int ProcessProtectionLevelInfo = 7;
        private static readonly object Gate = new object();
        private static readonly HashSet<int> LoggedPids = new HashSet<int>();

        internal static void CaptureCurrentClients()
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName("Vanilla MMO"); }
            catch (Exception ex)
            {
                Write("TARGET SECURITY enumeration failed: " + ex.GetType().Name + ": " + ex.Message);
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
            IntPtr process = Native.OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (process == IntPtr.Zero)
            {
                Write("TARGET SECURITY PID=" + pid + "; QUERY_LIMITED open failed: Win32 "
                    + Marshal.GetLastWin32Error() + ".");
                return;
            }

            try
            {
                string livePath = ReadImagePath(process);
                string started = ReadStartTime(process);
                string protection = ReadProtectionLevel(process);
                string token = ReadTargetToken(process);
                string observerSid = ReadObserverSid();
                Write("TARGET SECURITY PID=" + pid + "; livePath=" + livePath
                    + "; startedUtc=" + started + "; protection=" + protection
                    + "; token=[" + token + "]; observerUserSid=" + observerSid
                    + "; observerIntegrity=" + IntegrityText(ProcessObservationContext.Current.IntegrityRid) + ".");
            }
            catch (Exception ex)
            {
                Write("TARGET SECURITY PID=" + pid + "; capture failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally { Native.CloseHandle(process); }
        }

        private static string ReadImagePath(IntPtr process)
        {
            var text = new StringBuilder(32768);
            int length = text.Capacity;
            if (!Native.QueryFullProcessImageName(process, 0, text, ref length))
                return "ERROR Win32 " + Marshal.GetLastWin32Error();
            return "'" + text.ToString() + "'";
        }

        private static string ReadStartTime(IntPtr process)
        {
            long creation, exit, kernel, user;
            if (!Native.GetProcessTimes(process, out creation, out exit, out kernel, out user))
                return "ERROR Win32 " + Marshal.GetLastWin32Error();
            try { return DateTime.FromFileTimeUtc(creation).ToString("O"); }
            catch { return "invalid FILETIME " + creation; }
        }

        private static string ReadProtectionLevel(IntPtr process)
        {
            var info = new ProcessProtectionLevelInformation();
            if (!Native.GetProcessInformation(process, ProcessProtectionLevelInfo, ref info,
                (uint)Marshal.SizeOf(typeof(ProcessProtectionLevelInformation))))
                return "ERROR Win32 " + Marshal.GetLastWin32Error();
            return "0x" + info.ProtectionLevel.ToString("X8");
        }

        private static string ReadTargetToken(IntPtr process)
        {
            IntPtr token;
            if (!Native.OpenProcessToken(process, TokenQuery, out token))
                return "OpenProcessToken(TOKEN_QUERY)=ERROR Win32 " + Marshal.GetLastWin32Error();
            try
            {
                string userSid = ReadSidInfo(token, TokenUser);
                string integrity = ReadIntegrityInfo(token);
                string elevated = ReadElevation(token);
                string observerSid = ReadObserverSid();
                bool sameUser = !string.IsNullOrWhiteSpace(userSid) && !string.IsNullOrWhiteSpace(observerSid)
                    && string.Equals(userSid, observerSid, StringComparison.OrdinalIgnoreCase);
                return "userSid=" + userSid + ", sameUserAsObserver=" + sameUser
                    + ", elevated=" + elevated + ", integrity=" + integrity;
            }
            finally { Native.CloseHandle(token); }
        }

        private static string ReadElevation(IntPtr token)
        {
            IntPtr buffer = Marshal.AllocHGlobal(sizeof(uint));
            try
            {
                uint returned;
                if (!Native.GetTokenInformation(token, TokenElevation, buffer, sizeof(uint), out returned))
                    return "ERROR Win32 " + Marshal.GetLastWin32Error();
                if (returned < sizeof(uint)) return "ERROR incomplete";
                return Marshal.ReadInt32(buffer) != 0 ? "yes" : "no";
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static string ReadIntegrityInfo(IntPtr token)
        {
            uint required;
            Native.GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out required);
            int sizingError = Marshal.GetLastWin32Error();
            if (required == 0) return "ERROR size Win32 " + sizingError;
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)required));
            try
            {
                uint returned;
                if (!Native.GetTokenInformation(token, TokenIntegrityLevel, buffer, required, out returned))
                    return "ERROR Win32 " + Marshal.GetLastWin32Error();
                IntPtr sidPointer = Marshal.ReadIntPtr(buffer);
                if (sidPointer == IntPtr.Zero) return "ERROR null SID";
                var sid = new SecurityIdentifier(sidPointer);
                string value = sid.Value;
                uint rid = IntegrityRid(value);
                return value + " / " + IntegrityText(rid);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static string ReadSidInfo(IntPtr token, int informationClass)
        {
            uint required;
            Native.GetTokenInformation(token, informationClass, IntPtr.Zero, 0, out required);
            int sizingError = Marshal.GetLastWin32Error();
            if (required == 0) return "ERROR size Win32 " + sizingError;
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)required));
            try
            {
                uint returned;
                if (!Native.GetTokenInformation(token, informationClass, buffer, required, out returned))
                    return "ERROR Win32 " + Marshal.GetLastWin32Error();
                IntPtr sidPointer = Marshal.ReadIntPtr(buffer);
                if (sidPointer == IntPtr.Zero) return "ERROR null SID";
                return new SecurityIdentifier(sidPointer).Value;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static string ReadObserverSid()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    return identity.User == null ? "unknown" : identity.User.Value;
            }
            catch (Exception ex) { return "ERROR " + ex.GetType().Name + ": " + ex.Message; }
        }

        private static uint IntegrityRid(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid) || !sid.StartsWith("S-1-16-", StringComparison.Ordinal)) return 0;
            uint value;
            return uint.TryParse(sid.Substring(7), out value) ? value : 0;
        }

        private static string IntegrityText(uint? rid)
        {
            if (!rid.HasValue || rid.Value == 0) return "unknown";
            switch (rid.Value)
            {
                case 0x1000: return "low (0x1000)";
                case 0x2000: return "medium (0x2000)";
                case 0x2100: return "medium-plus (0x2100)";
                case 0x3000: return "high (0x3000)";
                case 0x4000: return "system (0x4000)";
                case 0x5000: return "protected (0x5000)";
                default: return "0x" + rid.Value.ToString("X");
            }
        }

        private static void Write(string message)
        {
            try
            {
                string directory = VanillaAppData.LogsDirectory;
                Directory.CreateDirectory(directory);
                string path = VanillaMemoryAccessDiagnostics.LogPath;
                string line = DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine;
                lock (Gate) VanillaLogRotation.Append(path, "memory-access", line);
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessProtectionLevelInformation
        {
            internal uint ProtectionLevel;
        }

        private static class Native
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool CloseHandle(IntPtr value);
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetProcessInformation(IntPtr process, int informationClass,
                ref ProcessProtectionLevelInformation information, uint informationSize);
            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);
            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr buffer,
                uint length, out uint returnedLength);
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace _4RTools.Utils
{
    // Describes this observer only. It never opens another process or changes a token.
    internal sealed class ProcessObservationContext
    {
        private static readonly Lazy<ProcessObservationContext> current =
            new Lazy<ProcessObservationContext>(CaptureCurrent);

        internal static ProcessObservationContext Current { get { return current.Value; } }
        internal int ProcessId { get; }
        internal int PointerSize { get; }
        internal DateTimeOffset? StartedAtUtc { get; }
        internal bool? Elevated { get; }
        internal uint? IntegrityRid { get; }
        internal string MetadataError { get; }

        internal ProcessObservationContext(int processId, int pointerSize, DateTimeOffset? startedAtUtc,
            bool? elevated, uint? integrityRid, string metadataError = null)
        {
            ProcessId = processId;
            PointerSize = pointerSize;
            StartedAtUtc = startedAtUtc?.ToUniversalTime();
            Elevated = elevated;
            IntegrityRid = integrityRid;
            MetadataError = metadataError;
        }

        internal string DescribeNativeFailure(string operation, int targetProcessId, int error)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} failed for PID {1}: Win32 {2} ({3}). {4}",
                operation, targetProcessId, error, new Win32Exception(error).Message, this);
        }

        public override string ToString()
        {
            string integrity = IntegrityRid.HasValue ? IntegrityLabel(IntegrityRid.Value)
                + " (0x" + IntegrityRid.Value.ToString("X", CultureInfo.InvariantCulture) + ")" : "unknown";
            return string.Format(CultureInfo.InvariantCulture,
                "Observer PID {0}, {1}-bit, started {2}, elevated {3}, integrity {4}.{5}",
                ProcessId, PointerSize * 8,
                StartedAtUtc.HasValue ? StartedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture) : "unknown",
                Elevated.HasValue ? (Elevated.Value ? "yes" : "no") : "unknown", integrity,
                string.IsNullOrEmpty(MetadataError) ? string.Empty : " Self-context error: " + MetadataError);
        }

        private static string IntegrityLabel(uint rid)
        {
            switch (rid)
            {
                case 0x0000: return "untrusted";
                case 0x1000: return "low";
                case 0x2000: return "medium";
                case 0x2100: return "medium-plus";
                case 0x3000: return "high";
                case 0x4000: return "system";
                case 0x5000: return "protected";
                default: return "unrecognized";
            }
        }

        private static ProcessObservationContext CaptureCurrent()
        {
            int pid = unchecked((int)Native.GetCurrentProcessId());
            IntPtr self = Native.GetCurrentProcess(); // Pseudo-handle; never close or duplicate it.
            DateTimeOffset? started = null;
            bool? elevated = null;
            uint? integrity = null;
            var errors = new List<string>();
            try
            {
                long creation, exit, kernel, user;
                if (!Native.GetProcessTimes(self, out creation, out exit, out kernel, out user))
                    throw SelfFailure("GetProcessTimes", Marshal.GetLastWin32Error());
                started = new DateTimeOffset(DateTime.FromFileTimeUtc(creation));
            }
            catch (Exception ex) { errors.Add(ex.Message); }
            try
            {
                SafeAccessTokenHandle token;
                bool opened = Native.OpenProcessToken(self, 0x0008, out token); // TOKEN_QUERY only.
                int openError = Marshal.GetLastWin32Error();
                using (token)
                {
                    if (!opened || token == null || token.IsInvalid) throw SelfFailure("OpenProcessToken(self, TOKEN_QUERY)", openError);
                    try
                    {
                        uint value, returned;
                        if (!Native.GetTokenElevation(token, 20, out value, sizeof(uint), out returned))
                            throw SelfFailure("GetTokenInformation(TokenElevation)", Marshal.GetLastWin32Error());
                        if (returned != sizeof(uint)) throw new InvalidOperationException("TokenElevation returned incomplete self metadata.");
                        elevated = value != 0;
                    }
                    catch (Exception ex) { errors.Add(ex.Message); }
                    try { integrity = ReadIntegrityRid(token); }
                    catch (Exception ex) { errors.Add(ex.Message); }
                }
            }
            catch (Exception ex) { errors.Add(ex.Message); }
            return new ProcessObservationContext(pid, IntPtr.Size, started, elevated, integrity, string.Join("; ", errors));
        }

        private static uint ReadIntegrityRid(SafeAccessTokenHandle token)
        {
            uint required;
            bool sized = Native.GetTokenInformation(token, 25, IntPtr.Zero, 0, out required);
            int error = Marshal.GetLastWin32Error();
            // The documented two-call sizing protocol reads only our own token.
            if (sized || error != 122) throw SelfFailure("GetTokenInformation(TokenIntegrityLevel size)", error);
            if (required < IntPtr.Size + sizeof(uint) || required > 65536)
                throw new InvalidOperationException("TokenIntegrityLevel returned an invalid self metadata size.");
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)required));
            try
            {
                uint returned;
                if (!Native.GetTokenInformation(token, 25, buffer, required, out returned))
                    throw SelfFailure("GetTokenInformation(TokenIntegrityLevel)", Marshal.GetLastWin32Error());
                if (returned < IntPtr.Size + sizeof(uint) || returned > required)
                    throw new InvalidOperationException("TokenIntegrityLevel returned incomplete self metadata.");
                string sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
                uint rid;
                if (!sid.StartsWith("S-1-16-", StringComparison.Ordinal)
                    || !uint.TryParse(sid.Substring(7), NumberStyles.None, CultureInfo.InvariantCulture, out rid))
                    throw new InvalidOperationException("TokenIntegrityLevel returned an unrecognized integrity SID.");
                return rid;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static Exception SelfFailure(string operation, int error)
        {
            return new Win32Exception(error, operation + ": Win32 " + error.ToString(CultureInfo.InvariantCulture)
                + " (" + new Win32Exception(error).Message + ").");
        }

        private static class Native
        {
            [DllImport("kernel32.dll")] internal static extern IntPtr GetCurrentProcess();
            [DllImport("kernel32.dll")] internal static extern uint GetCurrentProcessId();
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
            [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
            [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetTokenElevation(SafeAccessTokenHandle token, int informationClass, out uint value, uint length, out uint returned);
            [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, IntPtr buffer, uint length, out uint returned);
        }
    }
}

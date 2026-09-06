using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace _4RTools.Utils
{
    public interface IReadOnlyProcessMemory : IDisposable
    {
        int ProcessId { get; }
        string ProcessName { get; }
        int PointerSize { get; }
        ulong MainModuleBaseAddress { get; }
        bool IsStopped { get; }
        string LastError { get; }
        void EnsureAlive();
        ulong GetModuleBase(string moduleName);
        byte[] ReadBytes(ulong address, int count);
    }

    public sealed class MemoryObservationException : IOException
    {
        public int? NativeErrorCode { get; private set; }
        public MemoryObservationException(string message, int? nativeErrorCode = null) : base(message)
        {
            NativeErrorCode = nativeErrorCode;
        }
    }

    // This adapter is independent from the legacy reader. It exposes no write or input API.
    public sealed class ReadOnlyProcessMemory : IReadOnlyProcessMemory
    {
        private const uint ProcessVmRead = 0x0010;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private SafeProcessHandle handle;
        private readonly Dictionary<string, ulong> modules = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        public int ProcessId { get; private set; }
        public string ProcessName { get; private set; }
        public string ExecutablePath { get; private set; }
        public uint MainModuleSize { get; private set; }
        public int PointerSize { get; private set; }
        public ulong MainModuleBaseAddress { get; private set; }
        public bool IsStopped { get; private set; }
        public string LastError { get; private set; }

        public ReadOnlyProcessMemory(int processId)
        {
            if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
            ProcessId = processId;
            try
            {
                handle = Native.OpenProcess(ProcessVmRead | ProcessQueryLimitedInformation, false, processId);
                if (handle == null || handle.IsInvalid) throw NativeFailure("OpenProcess", Marshal.GetLastWin32Error());
                bool wow64;
                if (!Native.IsWow64Process(handle, out wow64)) throw NativeFailure("IsWow64Process", Marshal.GetLastWin32Error());
                PointerSize = Environment.Is64BitOperatingSystem && !wow64 ? 8 : 4;
                if (IntPtr.Size == 4 && PointerSize == 8)
                    throw Stop("A 32-bit observer cannot inspect a 64-bit target. No memory read was attempted.");
                var path = new StringBuilder(32768);
                int length = path.Capacity;
                if (!Native.QueryFullProcessImageName(handle, 0, path, ref length)) throw NativeFailure("QueryFullProcessImageName", Marshal.GetLastWin32Error());
                ExecutablePath = path.ToString();
                ProcessName = Path.GetFileNameWithoutExtension(ExecutablePath);
                ReadModuleMetadata();
                EnsureAlive();
            }
            catch (Exception ex)
            {
                LastError = LastError ?? ex.Message;
                Dispose();
                throw;
            }
        }

        public void EnsureAlive()
        {
            ThrowIfStopped();
            uint exitCode;
            if (!Native.GetExitCodeProcess(handle, out exitCode)) throw NativeFailure("GetExitCodeProcess", Marshal.GetLastWin32Error());
            if (exitCode != 259) throw Stop("Process " + ProcessId + " exited with code " + exitCode + ". Session stopped.");
        }

        public ulong GetModuleBase(string moduleName)
        {
            ThrowIfStopped();
            if (string.IsNullOrWhiteSpace(moduleName)) return MainModuleBaseAddress;
            ulong address;
            if (!modules.TryGetValue(moduleName, out address)) throw Stop("Module '" + moduleName + "' was not present in the selected process. Session stopped.");
            return address;
        }

        public byte[] ReadBytes(ulong address, int count)
        {
            ThrowIfStopped();
            if (count < 1 || count > 256) throw Stop("Read size must be between 1 and 256 bytes. Session stopped.");
            try { ValidateRange(address, count, PointerSize); }
            catch (ArgumentException ex) { throw Stop(ex.Message); }
            var buffer = new byte[count];
            UIntPtr read;
            IntPtr pointer = IntPtr.Size == 4 ? new IntPtr(unchecked((int)(uint)address)) : new IntPtr(unchecked((long)address));
            bool success = Native.ReadProcessMemory(handle, pointer, buffer, new UIntPtr((uint)count), out read);
            int error = Marshal.GetLastWin32Error();
            if (!success) throw NativeFailure("ReadProcessMemory at 0x" + address.ToString("X") + " (" + count + " bytes; received " + read.ToUInt64() + ")", error);
            if (read.ToUInt64() != (ulong)count)
                throw Stop("ReadProcessMemory at 0x" + address.ToString("X") + " returned only " + read.ToUInt64() + " of " + count + " bytes (Win32 299, partial copy). Session stopped.", 299);
            return buffer;
        }

        public static void ValidateRange(ulong address, int count, int pointerSize)
        {
            if (pointerSize != 4 && pointerSize != 8) throw new ArgumentException("Target pointer size must be 4 or 8 bytes.");
            if (count <= 0) throw new ArgumentException("Read size must be positive.");
            ulong maximum = pointerSize == 4 ? uint.MaxValue : ulong.MaxValue;
            if (address == 0 || address > maximum || (ulong)(count - 1) > maximum - address)
                throw new ArgumentException("Address range 0x" + address.ToString("X") + " + " + count + " bytes is null, overflowing, or outside the target pointer width.");
        }

        private void ReadModuleMetadata()
        {
            // Toolhelp obtains module metadata without requesting another process handle/access mask.
            using (SafeSnapshotHandle snapshot = Native.CreateToolhelp32Snapshot(0x00000008 | 0x00000010, ProcessId))
            {
                if (snapshot.IsInvalid) throw NativeFailure("CreateToolhelp32Snapshot (module metadata)", Marshal.GetLastWin32Error());
                var entry = new Native.ModuleEntry { Size = (uint)Marshal.SizeOf(typeof(Native.ModuleEntry)) };
                if (!Native.Module32First(snapshot, ref entry)) throw NativeFailure("Module32First", Marshal.GetLastWin32Error());
                MainModuleBaseAddress = ToAddress(entry.BaseAddress);
                MainModuleSize = entry.BaseSize;
                do
                {
                    modules[entry.ModuleName] = ToAddress(entry.BaseAddress);
                    entry.Size = (uint)Marshal.SizeOf(typeof(Native.ModuleEntry));
                } while (Native.Module32Next(snapshot, ref entry));
                int error = Marshal.GetLastWin32Error();
                if (error != 18) throw NativeFailure("Module32Next", error); // ERROR_NO_MORE_FILES is normal enumeration completion.
            }
        }

        private static ulong ToAddress(IntPtr pointer)
        {
            return IntPtr.Size == 4 ? unchecked((uint)pointer.ToInt32()) : unchecked((ulong)pointer.ToInt64());
        }

        private MemoryObservationException NativeFailure(string operation, int code)
        {
            return Stop(operation + " failed for PID " + ProcessId + ": Win32 " + code + " (" + new Win32Exception(code).Message + "). Session stopped; no retry or alternate access attempted.", code);
        }

        private MemoryObservationException Stop(string error, int? code = null)
        {
            LastError = LastError ?? error;
            Dispose();
            return new MemoryObservationException(LastError, code);
        }

        private void ThrowIfStopped()
        {
            if (IsStopped) throw new MemoryObservationException(LastError ?? "Observation session is closed.");
        }

        public void Dispose()
        {
            IsStopped = true;
            if (handle != null) handle.Dispose();
        }

        private sealed class SafeSnapshotHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeSnapshotHandle() : base(true) { }
            protected override bool ReleaseHandle() { return Native.CloseHandle(handle); }
        }

        private static class Native
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct ModuleEntry
            {
                internal uint Size, ModuleId, ProcessId, GlobalUsage, ProcessUsage;
                internal IntPtr BaseAddress;
                internal uint BaseSize;
                internal IntPtr ModuleHandle;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string ModuleName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string ExePath;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool IsWow64Process(SafeProcessHandle process, [MarshalAs(UnmanagedType.Bool)] out bool wow64);
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder name, ref int size);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, [Out] byte[] bytes, UIntPtr count, out UIntPtr bytesRead);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern SafeSnapshotHandle CreateToolhelp32Snapshot(uint flags, int processId);
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool Module32First(SafeSnapshotHandle snapshot, ref ModuleEntry entry);
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool Module32Next(SafeSnapshotHandle snapshot, ref ModuleEntry entry);
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool CloseHandle(IntPtr value);
        }
    }
}

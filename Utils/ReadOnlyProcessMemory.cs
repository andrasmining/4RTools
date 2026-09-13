using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
                // Do not ask the protected client for PROCESS_QUERY_* rights merely to discover
                // metadata. Toolhelp gives us the module base/path without opening a target
                // process handle, and the PE header on disk gives us the pointer width.
                ReadModuleMetadata();
                PointerSize = ReadPointerSize(ExecutablePath);
                if (IntPtr.Size == 4 && PointerSize == 8)
                    throw Stop("A 32-bit observer cannot inspect a 64-bit target. No memory read was attempted.");

                // PROCESS_VM_READ is the only target-process access right this observer needs.
                // Requesting QUERY_LIMITED_INFORMATION together with it made the whole OpenProcess
                // call fail when Vanilla denied process-query metadata even though read access may
                // still be permitted.
                handle = Native.OpenProcess(ProcessVmRead, false, processId);
                if (handle == null || handle.IsInvalid)
                    throw NativeFailure("OpenProcess(read, 0x0010)", Marshal.GetLastWin32Error());
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
            // Avoid GetExitCodeProcess/Process.HasExited because both require process-query
            // metadata rights. A one-byte read from the already-known main module proves that
            // the VM_READ session is still usable and naturally fails if the process is gone.
            ReadBytes(MainModuleBaseAddress, 1);
        }

        public ulong GetModuleBase(string moduleName)
        {
            ThrowIfStopped();
            if (string.IsNullOrWhiteSpace(moduleName)) return MainModuleBaseAddress;
            ulong address;
            if (!modules.TryGetValue(moduleName, out address))
                throw Stop("Module '" + moduleName + "' was not present in the selected process. Session stopped.");
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
            if (!success)
                throw NativeFailure("ReadProcessMemory at 0x" + address.ToString("X") + " (" + count + " bytes; received " + read.ToUInt64() + ")", error);
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
            // Toolhelp obtains module metadata without requesting a process-query handle/access mask.
            using (SafeSnapshotHandle snapshot = Native.CreateToolhelp32Snapshot(0x00000008 | 0x00000010, ProcessId))
            {
                if (snapshot.IsInvalid) throw NativeFailure("CreateToolhelp32Snapshot (module metadata)", Marshal.GetLastWin32Error());
                var entry = new Native.ModuleEntry { Size = (uint)Marshal.SizeOf(typeof(Native.ModuleEntry)) };
                if (!Native.Module32First(snapshot, ref entry)) throw NativeFailure("Module32First", Marshal.GetLastWin32Error());
                MainModuleBaseAddress = ToAddress(entry.BaseAddress);
                MainModuleSize = entry.BaseSize;
                ExecutablePath = entry.ExePath;
                if (string.IsNullOrWhiteSpace(ExecutablePath))
                    throw Stop("Toolhelp did not return the Vanilla executable path. Session stopped.");
                ProcessName = Path.GetFileNameWithoutExtension(ExecutablePath);
                do
                {
                    modules[entry.ModuleName] = ToAddress(entry.BaseAddress);
                    entry.Size = (uint)Marshal.SizeOf(typeof(Native.ModuleEntry));
                } while (Native.Module32Next(snapshot, ref entry));
                int error = Marshal.GetLastWin32Error();
                if (error != 18) throw NativeFailure("Module32Next", error); // ERROR_NO_MORE_FILES is normal enumeration completion.
            }
        }

        private static int ReadPointerSize(string executablePath)
        {
            try
            {
                using (var stream = new FileStream(executablePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new BinaryReader(stream))
                {
                    if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
                        throw new InvalidDataException("Executable does not contain a valid DOS header.");
                    stream.Position = 0x3C;
                    int peOffset = reader.ReadInt32();
                    if (peOffset < 0 || peOffset > stream.Length - 26)
                        throw new InvalidDataException("Executable contains an invalid PE header offset.");
                    stream.Position = peOffset;
                    if (reader.ReadUInt32() != 0x00004550)
                        throw new InvalidDataException("Executable does not contain a valid PE signature.");
                    ushort machine = reader.ReadUInt16();
                    stream.Position = peOffset + 24;
                    ushort optionalMagic = reader.ReadUInt16();
                    if (machine == 0x014C && optionalMagic == 0x010B) return 4;
                    if (machine == 0x8664 && optionalMagic == 0x020B) return 8;
                    throw new InvalidDataException("Unsupported executable architecture: machine 0x" + machine.ToString("X4")
                        + ", optional header 0x" + optionalMagic.ToString("X4") + ".");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                throw new MemoryObservationException("Could not determine target pointer width from '" + executablePath + "': " + ex.Message);
            }
        }

        private static ulong ToAddress(IntPtr pointer)
        {
            return IntPtr.Size == 4 ? unchecked((uint)pointer.ToInt32()) : unchecked((ulong)pointer.ToInt64());
        }

        private MemoryObservationException NativeFailure(string operation, int code)
        {
            return Stop(ProcessObservationContext.Current.DescribeNativeFailure(operation, ProcessId, code)
                + " Session stopped; no retry or alternate access attempted.", code);
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

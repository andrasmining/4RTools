using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json.Linq;

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
                PeImageMetadata configuredImage = TryReadConfiguredImageMetadata();
                if (configuredImage != null)
                    ApplyConfiguredImageMetadata(configuredImage);
                else
                    ReadModuleMetadataWithToolhelp();

                if (IntPtr.Size == 4 && PointerSize == 8)
                    throw Stop("A 32-bit observer cannot inspect a 64-bit target. No memory read was attempted.");

                // VM_READ is the only target-process access right used by Vanilla observation.
                handle = Native.OpenProcess(ProcessVmRead, false, processId);
                if (handle == null || handle.IsInvalid)
                    throw NativeFailure("OpenProcess(read, 0x0010)", Marshal.GetLastWin32Error());

                if (configuredImage != null)
                {
                    string mismatch;
                    if (!RemoteMainImageMatches(configuredImage, out mismatch))
                        throw Stop("The configured Vanilla executable at '" + configuredImage.Path
                            + "' did not match the live image at preferred base 0x" + configuredImage.ImageBase.ToString("X")
                            + ". " + mismatch + " Module metadata is protected, so no alternate module-enumeration path was attempted.");
                }
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
            // Avoid Process.HasExited/GetExitCodeProcess because those require process-query rights.
            // A one-byte read from the verified main image proves that the VM_READ session is alive.
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
            IntPtr pointer = ToIntPtr(address);
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

        private void ApplyConfiguredImageMetadata(PeImageMetadata image)
        {
            ExecutablePath = image.Path;
            ProcessName = Path.GetFileNameWithoutExtension(image.Path);
            PointerSize = image.PointerSize;
            MainModuleBaseAddress = image.ImageBase;
            MainModuleSize = image.SizeOfImage;
            modules[Path.GetFileName(image.Path)] = image.ImageBase;
        }

        private PeImageMetadata TryReadConfiguredImageMetadata()
        {
            string executable = ResolveConfiguredVanillaExecutable();
            if (string.IsNullOrWhiteSpace(executable)) return null;
            try { return ReadPeImageMetadata(executable); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                throw new MemoryObservationException("Could not read configured Vanilla executable metadata from '"
                    + executable + "': " + ex.Message);
            }
        }

        internal static string ResolveVanillaExecutableFromLaunch(string launchExecutable)
        {
            if (string.IsNullOrWhiteSpace(launchExecutable)) return null;
            try
            {
                string full = Path.GetFullPath(launchExecutable);
                if (File.Exists(full) && string.Equals(Path.GetFileName(full), "Vanilla MMO.exe", StringComparison.OrdinalIgnoreCase))
                    return full;
                string directory = Path.GetDirectoryName(full);
                if (string.IsNullOrWhiteSpace(directory)) return null;
                string sibling = Path.Combine(directory, "Vanilla MMO.exe");
                return File.Exists(sibling) ? sibling : null;
            }
            catch { return null; }
        }

        private static string ResolveConfiguredVanillaExecutable()
        {
            try
            {
                string root = Environment.GetEnvironmentVariable("FOURRTOOLS_DATA_ROOT");
                if (string.IsNullOrWhiteSpace(root))
                    root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "4RTools Vanilla");
                string reconnect = Path.Combine(root, "VanillaReconnect", "reconnect.json");
                if (!File.Exists(reconnect)) return null;
                JObject json = JObject.Parse(File.ReadAllText(reconnect));
                string launch = (string)json["LaunchExecutable"];
                return ResolveVanillaExecutableFromLaunch(launch);
            }
            catch { return null; }
        }

        private bool RemoteMainImageMatches(PeImageMetadata expected, out string evidence)
        {
            evidence = null;
            byte[] dos;
            int error;
            if (!TryReadRaw(expected.ImageBase, 64, out dos, out error))
            {
                evidence = "ReadProcessMemory of the expected image base failed with Win32 " + error + ".";
                return false;
            }
            if (BitConverter.ToUInt16(dos, 0) != 0x5A4D)
            {
                evidence = "The live image does not start with an MZ header.";
                return false;
            }
            int peOffset = BitConverter.ToInt32(dos, 0x3C);
            if (peOffset < 0x40 || peOffset > 0x1000)
            {
                evidence = "The live image contains an invalid PE header offset.";
                return false;
            }
            byte[] pe;
            if (!TryReadRaw(expected.ImageBase + (uint)peOffset, 96, out pe, out error))
            {
                evidence = "Reading the live PE header failed with Win32 " + error + ".";
                return false;
            }
            if (BitConverter.ToUInt32(pe, 0) != 0x00004550)
            {
                evidence = "The live image does not contain a PE signature.";
                return false;
            }
            ushort machine = BitConverter.ToUInt16(pe, 4);
            uint timestamp = BitConverter.ToUInt32(pe, 8);
            ushort optionalMagic = BitConverter.ToUInt16(pe, 24);
            uint sizeOfImage = BitConverter.ToUInt32(pe, 80);
            if (machine != expected.Machine || timestamp != expected.TimeDateStamp
                || optionalMagic != expected.OptionalMagic || sizeOfImage != expected.SizeOfImage)
            {
                evidence = "Live PE identity differs from the configured executable (machine/timestamp/optional-header/image-size mismatch).";
                return false;
            }
            return true;
        }

        private bool TryReadRaw(ulong address, int count, out byte[] bytes, out int error)
        {
            bytes = new byte[count];
            UIntPtr received;
            bool success = Native.ReadProcessMemory(handle, ToIntPtr(address), bytes, new UIntPtr((uint)count), out received);
            error = success ? 0 : Marshal.GetLastWin32Error();
            return success && received.ToUInt64() == (ulong)count;
        }

        private void ReadModuleMetadataWithToolhelp()
        {
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
                PeImageMetadata image = ReadPeImageMetadata(ExecutablePath);
                PointerSize = image.PointerSize;
                do
                {
                    modules[entry.ModuleName] = ToAddress(entry.BaseAddress);
                    entry.Size = (uint)Marshal.SizeOf(typeof(Native.ModuleEntry));
                } while (Native.Module32Next(snapshot, ref entry));
                int error = Marshal.GetLastWin32Error();
                if (error != 18) throw NativeFailure("Module32Next", error);
            }
        }

        private static PeImageMetadata ReadPeImageMetadata(string executablePath)
        {
            using (var stream = new FileStream(executablePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 0x100 || reader.ReadUInt16() != 0x5A4D)
                    throw new InvalidDataException("Executable does not contain a valid DOS header.");
                stream.Position = 0x3C;
                int peOffset = reader.ReadInt32();
                if (peOffset < 0x40 || peOffset > stream.Length - 96)
                    throw new InvalidDataException("Executable contains an invalid PE header offset.");
                stream.Position = peOffset;
                if (reader.ReadUInt32() != 0x00004550)
                    throw new InvalidDataException("Executable does not contain a valid PE signature.");
                ushort machine = reader.ReadUInt16();
                reader.ReadUInt16();
                uint timestamp = reader.ReadUInt32();
                stream.Position = peOffset + 24;
                ushort optionalMagic = reader.ReadUInt16();
                int pointerSize;
                ulong imageBase;
                if (machine == 0x014C && optionalMagic == 0x010B)
                {
                    pointerSize = 4;
                    stream.Position = peOffset + 24 + 28;
                    imageBase = reader.ReadUInt32();
                }
                else if (machine == 0x8664 && optionalMagic == 0x020B)
                {
                    pointerSize = 8;
                    stream.Position = peOffset + 24 + 24;
                    imageBase = reader.ReadUInt64();
                }
                else
                    throw new InvalidDataException("Unsupported executable architecture: machine 0x" + machine.ToString("X4")
                        + ", optional header 0x" + optionalMagic.ToString("X4") + ".");
                stream.Position = peOffset + 24 + 56;
                uint sizeOfImage = reader.ReadUInt32();
                if (imageBase == 0 || sizeOfImage == 0)
                    throw new InvalidDataException("Executable PE metadata contains an invalid image base or image size.");
                return new PeImageMetadata
                {
                    Path = Path.GetFullPath(executablePath),
                    Machine = machine,
                    TimeDateStamp = timestamp,
                    OptionalMagic = optionalMagic,
                    PointerSize = pointerSize,
                    ImageBase = imageBase,
                    SizeOfImage = sizeOfImage
                };
            }
        }

        private static ulong ToAddress(IntPtr pointer)
        {
            return IntPtr.Size == 4 ? unchecked((uint)pointer.ToInt32()) : unchecked((ulong)pointer.ToInt64());
        }

        private static IntPtr ToIntPtr(ulong address)
        {
            return IntPtr.Size == 4 ? new IntPtr(unchecked((int)(uint)address)) : new IntPtr(unchecked((long)address));
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

        private sealed class PeImageMetadata
        {
            public string Path;
            public ushort Machine;
            public uint TimeDateStamp;
            public ushort OptionalMagic;
            public int PointerSize;
            public ulong ImageBase;
            public uint SizeOfImage;
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

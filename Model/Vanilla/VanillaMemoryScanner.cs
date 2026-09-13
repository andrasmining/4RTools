using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using _4RTools.Utils;

namespace _4RTools.Model.Vanilla
{
    public enum VanillaMemoryScanValueType { Byte, UInt16, Int16, UInt32, Int32 }
    public enum VanillaMemoryScanScope { MainModule, AllWritableMemory }
    public enum VanillaMemoryScanComparison { Changed, Unchanged, Increased, Decreased, Exact }

    internal struct VanillaScanRegion
    {
        public ulong Address, Size;
        public uint State, Protection;
    }

    // Allows deterministic observation-failure tests without opening a game process.
    internal interface IVanillaMemoryScanSource : IDisposable
    {
        int ProcessId { get; }
        string ProcessName { get; }
        string ExecutablePath { get; }
        string ExecutableSha256 { get; }
        ulong MainModuleBaseAddress { get; }
        uint MainModuleSize { get; }
        ulong WritableScanStart { get; }
        ulong WritableScanEnd { get; }
        VanillaScanRegion Query(ulong address);
        byte[] Read(ulong address, int count);
    }

    public sealed class VanillaMemoryCandidate
    {
        internal int BlockIndex { get; set; }
        internal int Offset { get; set; }
        public ulong Address { get; internal set; }
        public ulong? MainModuleOffset { get; internal set; }
        public long PreviousValue { get; internal set; }
        public long CurrentValue { get; internal set; }
        public long Delta { get { return CurrentValue - PreviousValue; } }
        public string AddressHex { get { return "0x" + Address.ToString("X8", CultureInfo.InvariantCulture); } }
        public string ModuleOffsetHex { get { return MainModuleOffset.HasValue ? "0x" + MainModuleOffset.Value.ToString("X", CultureInfo.InvariantCulture) : "—"; } }
    }

    // Read-only candidate finder. No target writes, allocation, injection, input, or access fallback.
    public sealed class VanillaMemoryDiscoverySession : IDisposable
    {
        private const uint ProcessVmRead = 0x0010, ProcessQueryInformation = 0x0400;
        private const uint MemCommit = 0x1000, PageGuard = 0x100, PageNoAccess = 0x01;
        private const int BlockSize = 64 * 1024, MaximumCandidates = 250000;
        private const long MaximumSnapshotBytes = 384L * 1024L * 1024L;
        private readonly IVanillaMemoryScanSource memory;
        private List<ScanBlock> baseline = new List<ScanBlock>();
        private List<VanillaMemoryCandidate> candidates = new List<VanillaMemoryCandidate>();
        private VanillaMemoryScanValueType baselineType;
        private VanillaMemoryScanScope baselineScope;
        private bool hasBaseline, disposed;

        public Guid SessionId { get; } = Guid.NewGuid();
        public bool IsStopped { get; private set; }
        public string LastError { get; private set; }
        public int? NativeErrorCode { get; private set; }
        public DateTimeOffset? LastSampleAtUtc { get; private set; }
        public bool HasComparison { get; private set; }

        public int ProcessId { get; private set; }
        public string ProcessName { get; private set; }
        public string ExecutablePath { get; private set; }
        public string ExecutableSha256 { get; private set; }
        public ulong MainModuleBaseAddress { get; private set; }
        public uint MainModuleSize { get; private set; }
        public bool HasBaseline { get { return hasBaseline; } }
        public VanillaMemoryScanValueType ValueType { get { return baselineType; } }
        public VanillaMemoryScanScope Scope { get { return baselineScope; } }
        public IReadOnlyList<VanillaMemoryCandidate> Candidates { get { return candidates.AsReadOnly(); } }
        public long BaselineBytes { get; private set; }
        public ulong ScanStart { get { return baselineScope == VanillaMemoryScanScope.MainModule ? MainModuleBaseAddress : memory.WritableScanStart; } }
        public ulong ScanEnd { get { return baselineScope == VanillaMemoryScanScope.MainModule ? checked(MainModuleBaseAddress + MainModuleSize) : memory.WritableScanEnd; } }

        public VanillaMemoryDiscoverySession(int processId) : this(new NativeScanSource(processId)) { }

        internal VanillaMemoryDiscoverySession(IVanillaMemoryScanSource memory)
        {
            this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
            ProcessId = memory.ProcessId;
            ProcessName = memory.ProcessName;
            ExecutablePath = memory.ExecutablePath;
            ExecutableSha256 = memory.ExecutableSha256;
            MainModuleBaseAddress = memory.MainModuleBaseAddress;
            MainModuleSize = memory.MainModuleSize;
        }

        public void CaptureBaseline(VanillaMemoryScanValueType valueType, VanillaMemoryScanScope scope)
        {
            ThrowIfUnavailable();
            if (!Enum.IsDefined(typeof(VanillaMemoryScanValueType), valueType)) throw new ArgumentOutOfRangeException(nameof(valueType));
            if (!Enum.IsDefined(typeof(VanillaMemoryScanScope), scope)) throw new ArgumentOutOfRangeException(nameof(scope));
            ClearScan();
            baselineType = valueType; baselineScope = scope;
            var captured = new List<ScanBlock>();
            long total = 0;
            foreach (MemoryRange range in EnumerateRanges(scope))
            {
                ulong cursor = range.Address, end = checked(range.Address + range.Size);
                while (cursor < end)
                {
                    int count = (int)Math.Min((ulong)BlockSize, end - cursor);
                    if (count > MaximumSnapshotBytes - total)
                        throw new InvalidOperationException("The selected scan scope exceeds the 384 MiB snapshot limit. Use Main module first or narrow the workflow.");
                    captured.Add(new ScanBlock { Address = cursor, Bytes = ReadExact(cursor, count) });
                    total += count;
                    cursor += (uint)count;
                }
            }
            if (captured.Count == 0) throw new InvalidOperationException("No readable memory ranges were available in the selected scope.");
            baseline = captured; BaselineBytes = total; hasBaseline = true;
            LastSampleAtUtc = DateTimeOffset.UtcNow;
        }

        public void StartExact(VanillaMemoryScanValueType valueType, VanillaMemoryScanScope scope, long exactValue)
        {
            ValidateSearchValue(valueType, exactValue);
            CaptureBaseline(valueType, scope);
            var found = new List<VanillaMemoryCandidate>();
            int width = Width(valueType);
            for (int blockIndex = 0; blockIndex < baseline.Count; blockIndex++)
            {
                ScanBlock block = baseline[blockIndex];
                for (int offset = FirstAlignedOffset(block.Address, width); offset + width <= block.Bytes.Length; offset += width)
                {
                    long value = ReadValue(block.Bytes, offset, valueType);
                    if (value == exactValue) AddCandidate(found, blockIndex, offset, value, value);
                }
            }
            candidates = found; HasComparison = true;
        }

        public void FirstCompare(VanillaMemoryScanComparison comparison, long? exactValue)
        {
            ThrowIfUnavailable();
            if (!hasBaseline) throw new InvalidOperationException("Capture a baseline first.");
            if (HasComparison) throw new InvalidOperationException("A comparison has already completed. Refine its candidates, or reset explicitly to start a new scan.");
            if (comparison == VanillaMemoryScanComparison.Unchanged)
                throw new InvalidOperationException("Do not start with Unchanged: it normally produces millions of candidates. Start with Changed, Increased, Decreased, or Exact, then refine with Unchanged.");
            ValidateExact(comparison, exactValue);
            var found = new List<VanillaMemoryCandidate>();
            var compared = new List<ScanBlock>();
            int width = Width(baselineType);
            for (int blockIndex = 0; blockIndex < baseline.Count; blockIndex++)
            {
                ScanBlock block = baseline[blockIndex];
                byte[] current = ReadExact(block.Address, block.Bytes.Length);
                for (int offset = FirstAlignedOffset(block.Address, width); offset + width <= block.Bytes.Length; offset += width)
                {
                    long before = ReadValue(block.Bytes, offset, baselineType), after = ReadValue(current, offset, baselineType);
                    if (Matches(before, after, comparison, exactValue)) AddCandidate(found, blockIndex, offset, before, after);
                }
                compared.Add(new ScanBlock { Address = block.Address, Bytes = current });
            }
            baseline = compared; candidates = found; HasComparison = true; LastSampleAtUtc = DateTimeOffset.UtcNow;
        }

        public void Refine(VanillaMemoryScanComparison comparison, long? exactValue)
        {
            ThrowIfUnavailable();
            if (!hasBaseline) throw new InvalidOperationException("Capture a baseline or run an Exact scan first.");
            if (candidates.Count == 0) throw new InvalidOperationException("There are no candidates to refine. Reset and start a new scan.");
            ValidateExact(comparison, exactValue);
            var groups = candidates.GroupBy(item => item.BlockIndex).ToDictionary(group => group.Key, group => group.ToList());
            var kept = new List<VanillaMemoryCandidate>();
            var compared = new List<ScanBlock>(baseline);
            foreach (var entry in groups)
            {
                ScanBlock block = baseline[entry.Key];
                byte[] current = ReadExact(block.Address, block.Bytes.Length);
                foreach (VanillaMemoryCandidate candidate in entry.Value)
                {
                    long before = candidate.CurrentValue, after = ReadValue(current, candidate.Offset, baselineType);
                    if (!Matches(before, after, comparison, exactValue)) continue;
                    AddCandidate(kept, candidate.BlockIndex, candidate.Offset, before, after);
                }
                compared[entry.Key] = new ScanBlock { Address = block.Address, Bytes = current };
            }
            baseline = compared; candidates = kept.OrderBy(item => item.Address).ToList(); LastSampleAtUtc = DateTimeOffset.UtcNow;
        }

        public string ExportReport()
        {
            ThrowIfDisposed();
            return JsonConvert.SerializeObject(new
            {
                SessionId, ProcessId, ProcessName, ExecutablePath, ExecutableSha256,
                ExportedAtUtc = DateTimeOffset.UtcNow, LastSampleAtUtc, HasBaseline, HasComparison, IsStopped, LastError, NativeErrorCode,
                ScanStart = "0x" + ScanStart.ToString("X", CultureInfo.InvariantCulture), ScanEndExclusive = "0x" + ScanEnd.ToString("X", CultureInfo.InvariantCulture),
                MainModuleBase = "0x" + MainModuleBaseAddress.ToString("X8", CultureInfo.InvariantCulture),
                MainModuleSize, ValueType = baselineType.ToString(), Scope = baselineScope.ToString(), BaselineBytes,
                CandidateCount = candidates.Count,
                Candidates = candidates.Select(item => new { Address = item.AddressHex, MainModuleOffset = item.MainModuleOffset.HasValue ? item.ModuleOffsetHex : null, item.PreviousValue, item.CurrentValue, item.Delta }).ToArray(),
                Warning = "Discovery candidates are not verified gameplay mappings. Reproduce the transition and validate across relog/client restart before promotion."
            }, Formatting.Indented);
        }

        public string MappingSnippet(VanillaMemoryCandidate candidate)
        {
            ThrowIfUnavailable();
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (!candidates.Contains(candidate)) throw new ArgumentException("Candidate is not from the current completed scan.", nameof(candidate));
            string encoding = EncodingName(baselineType);
            var mapping = new Dictionary<string, object>();
            if (candidate.MainModuleOffset.HasValue)
            {
                mapping["Module"] = Path.GetFileName(ExecutablePath);
                mapping["Address"] = "0x" + candidate.MainModuleOffset.Value.ToString("X", CultureInfo.InvariantCulture);
            }
            else { mapping["Module"] = null; mapping["Address"] = candidate.AddressHex; }
            mapping["Encoding"] = encoding;
            mapping["Evidence"] = "DISCOVERY CANDIDATE ONLY — verify semantics and stability before adding to a build profile.";
            return JsonConvert.SerializeObject(mapping, Formatting.Indented);
        }

        public void Reset() { ThrowIfUnavailable(); ClearScan(); }

        private void ClearScan() { baseline.Clear(); candidates.Clear(); BaselineBytes = 0; hasBaseline = false; HasComparison = false; LastSampleAtUtc = null; }

        private IEnumerable<MemoryRange> EnumerateRanges(VanillaMemoryScanScope scope)
        {
            ulong scanStart = ScanStart, scanEnd = ScanEnd;
            ulong cursor = scanStart;
            while (cursor < scanEnd)
            {
                VanillaScanRegion info;
                try { info = memory.Query(cursor); }
                catch (Exception ex) { throw Stop(ex); }
                ulong baseAddress = info.Address, regionSize = info.Size;
                if (regionSize == 0 || baseAddress > cursor || baseAddress > ulong.MaxValue - regionSize || baseAddress + regionSize <= cursor)
                    throw Stop(new MemoryObservationException("VirtualQueryEx returned an invalid or non-advancing range at 0x" + cursor.ToString("X", CultureInfo.InvariantCulture) + "."));
                ulong regionEnd = baseAddress + regionSize;
                ulong start = Math.Max(baseAddress, scanStart), end = Math.Min(regionEnd, scanEnd);
                if (end > start && IsReadable(info) && (scope != VanillaMemoryScanScope.AllWritableMemory || IsWritable(info.Protection)))
                    yield return new MemoryRange { Address = start, Size = end - start };
                cursor = regionEnd;
            }
        }

        private static bool IsReadable(VanillaScanRegion info)
        {
            if (info.State != MemCommit || (info.Protection & PageGuard) != 0 || (info.Protection & PageNoAccess) != 0) return false;
            uint value = info.Protection & 0xFF;
            return value == 0x02 || value == 0x04 || value == 0x08 || value == 0x20 || value == 0x40 || value == 0x80;
        }
        private static bool IsWritable(uint protect) { uint value = protect & 0xFF; return value == 0x04 || value == 0x08 || value == 0x40 || value == 0x80; }

        private byte[] ReadExact(ulong address, int count)
        {
            try
            {
                ReadOnlyProcessMemory.ValidateRange(address, count, 4);
                byte[] bytes = memory.Read(address, count);
                if (bytes == null || bytes.Length != count)
                    throw new MemoryObservationException("ReadProcessMemory at 0x" + address.ToString("X", CultureInfo.InvariantCulture)
                        + " returned " + (bytes == null ? "null" : bytes.Length.ToString(CultureInfo.InvariantCulture)) + " of " + count + " bytes (Win32 299, partial copy).", 299);
                return bytes;
            }
            catch (Exception ex) { throw Stop(ex); }
        }

        private void AddCandidate(List<VanillaMemoryCandidate> target, int blockIndex, int offset, long before, long after)
        {
            if (target.Count >= MaximumCandidates) throw new InvalidOperationException("More than 250,000 candidates matched. Reset and use a narrower scope/transition/value before continuing.");
            ulong address = checked(baseline[blockIndex].Address + (uint)offset);
            target.Add(new VanillaMemoryCandidate { BlockIndex = blockIndex, Offset = offset, Address = address,
                MainModuleOffset = address >= MainModuleBaseAddress && address < MainModuleBaseAddress + MainModuleSize ? (ulong?)(address - MainModuleBaseAddress) : null,
                PreviousValue = before, CurrentValue = after });
        }

        private void ValidateExact(VanillaMemoryScanComparison comparison, long? exactValue)
        {
            if (!Enum.IsDefined(typeof(VanillaMemoryScanComparison), comparison)) throw new ArgumentOutOfRangeException(nameof(comparison));
            if (comparison != VanillaMemoryScanComparison.Exact) return;
            if (!exactValue.HasValue) throw new ArgumentException("Exact value is required.");
            ValidateSearchValue(baselineType, exactValue.Value);
        }
        private static bool Matches(long before, long after, VanillaMemoryScanComparison comparison, long? exact)
        {
            switch (comparison)
            {
                case VanillaMemoryScanComparison.Changed: return before != after;
                case VanillaMemoryScanComparison.Unchanged: return before == after;
                case VanillaMemoryScanComparison.Increased: return after > before;
                case VanillaMemoryScanComparison.Decreased: return after < before;
                case VanillaMemoryScanComparison.Exact: return exact.HasValue && after == exact.Value;
                default: throw new ArgumentOutOfRangeException(nameof(comparison));
            }
        }
        private static int Width(VanillaMemoryScanValueType type) { return type == VanillaMemoryScanValueType.Byte ? 1 : (type == VanillaMemoryScanValueType.UInt16 || type == VanillaMemoryScanValueType.Int16 ? 2 : 4); }
        private static int FirstAlignedOffset(ulong address, int width) { if (width == 1) return 0; ulong remainder = address % (uint)width; return remainder == 0 ? 0 : (int)((uint)width - remainder); }
        private static long ReadValue(byte[] bytes, int offset, VanillaMemoryScanValueType type)
        {
            switch (type)
            {
                case VanillaMemoryScanValueType.Byte: return bytes[offset];
                case VanillaMemoryScanValueType.UInt16: return BitConverter.ToUInt16(bytes, offset);
                case VanillaMemoryScanValueType.Int16: return BitConverter.ToInt16(bytes, offset);
                case VanillaMemoryScanValueType.UInt32: return BitConverter.ToUInt32(bytes, offset);
                case VanillaMemoryScanValueType.Int32: return BitConverter.ToInt32(bytes, offset);
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static long ParseSearchValue(VanillaMemoryScanValueType type, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Enter an exact value.");
            string value = text.Trim(); long parsed;
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                ulong raw;
                ulong maximum = type == VanillaMemoryScanValueType.Byte ? byte.MaxValue
                    : (type == VanillaMemoryScanValueType.UInt16 || type == VanillaMemoryScanValueType.Int16) ? ushort.MaxValue : uint.MaxValue;
                if (!ulong.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out raw) || raw > maximum) throw new ArgumentException("Hexadecimal value does not fit " + type + ": " + text);
                parsed = type == VanillaMemoryScanValueType.Int32 ? unchecked((int)(uint)raw) : type == VanillaMemoryScanValueType.Int16 ? unchecked((short)(ushort)raw) : (long)raw;
            }
            else if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) throw new ArgumentException("Invalid integer value: " + text);
            ValidateSearchValue(type, parsed); return parsed;
        }
        private static void ValidateSearchValue(VanillaMemoryScanValueType type, long value)
        {
            if (!Enum.IsDefined(typeof(VanillaMemoryScanValueType), type)) throw new ArgumentOutOfRangeException(nameof(type));
            bool valid = type == VanillaMemoryScanValueType.Byte ? value >= byte.MinValue && value <= byte.MaxValue
                : type == VanillaMemoryScanValueType.UInt16 ? value >= ushort.MinValue && value <= ushort.MaxValue
                : type == VanillaMemoryScanValueType.Int16 ? value >= short.MinValue && value <= short.MaxValue
                : type == VanillaMemoryScanValueType.UInt32 ? value >= uint.MinValue && value <= uint.MaxValue
                : value >= int.MinValue && value <= int.MaxValue;
            if (!valid) throw new ArgumentOutOfRangeException(nameof(value), "Value does not fit " + type + ".");
        }
        private static string EncodingName(VanillaMemoryScanValueType type)
        {
            if (type == VanillaMemoryScanValueType.UInt32 || type == VanillaMemoryScanValueType.Int32) return type.ToString();
            throw new InvalidOperationException(type + " is a discovery datatype without a supported field-mapping encoding. Export the candidate report with its original datatype; verify semantics and extend the schema before promotion.");
        }
        internal static ulong BoundedWritableScanEnd(ulong systemMaximumAddress, uint allocationGranularity)
        {
            if (systemMaximumAddress < 0x10000UL || allocationGranularity < 4096 || allocationGranularity > 0x100000)
                throw new ArgumentException("Windows application-address limits are invalid.");
            // Do not infer target LARGE_ADDRESS_AWARE/4GT settings. Keep the broad scan below
            // the ordinary 2 GiB boundary and its final allocation-granularity reservation.
            return Math.Min(systemMaximumAddress, 0x80000000UL - allocationGranularity - 1) + 1;
        }
        private static ulong ToAddress(IntPtr pointer) { return IntPtr.Size == 4 ? unchecked((uint)pointer.ToInt32()) : unchecked((ulong)pointer.ToInt64()); }
        private static IntPtr ToIntPtr(ulong address) { return IntPtr.Size == 4 ? new IntPtr(unchecked((int)(uint)address)) : new IntPtr(unchecked((long)address)); }
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(VanillaMemoryDiscoverySession)); }
        private void ThrowIfUnavailable()
        {
            ThrowIfDisposed();
            if (IsStopped) throw new MemoryObservationException(LastError, NativeErrorCode);
        }
        private MemoryObservationException Stop(Exception error)
        {
            if (!IsStopped)
            {
                NativeErrorCode = (error as MemoryObservationException)?.NativeErrorCode ?? (error as Win32Exception)?.NativeErrorCode;
                LastError = error.Message + " Discovery session stopped; no retry or alternate access was attempted.";
                IsStopped = true; ClearScan(); memory.Dispose();
            }
            return new MemoryObservationException(LastError, NativeErrorCode);
        }
        public void Dispose() { if (disposed) return; disposed = true; IsStopped = true; ClearScan(); memory.Dispose(); }

        private sealed class NativeScanSource : IVanillaMemoryScanSource
        {
            private readonly SafeProcessHandle handle;
            public int ProcessId { get; }
            public string ProcessName { get; }
            public string ExecutablePath { get; }
            public string ExecutableSha256 { get; }
            public ulong MainModuleBaseAddress { get; }
            public uint MainModuleSize { get; }
            public ulong WritableScanStart { get; }
            public ulong WritableScanEnd { get; }

            public NativeScanSource(int processId)
            {
                using (var reader = new ReadOnlyProcessMemory(processId))
                {
                    if (!string.Equals(reader.ProcessName, "Vanilla MMO", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Memory discovery is restricted to Vanilla MMO.");
                    if (reader.PointerSize != 4)
                        throw new InvalidOperationException("Memory discovery currently supports the verified 32-bit Vanilla client only.");
                    ProcessId = processId; ProcessName = reader.ProcessName; ExecutablePath = reader.ExecutablePath;
                    MainModuleBaseAddress = reader.MainModuleBaseAddress; MainModuleSize = reader.MainModuleSize;
                }
                ExecutableSha256 = VanillaExecutableIdentity.Read(ExecutablePath).Sha256;
                SystemInformation system;
                Native.GetSystemInfo(out system);
                WritableScanStart = Math.Max(0x10000UL, ToAddress(system.MinimumApplicationAddress));
                WritableScanEnd = BoundedWritableScanEnd(ToAddress(system.MaximumApplicationAddress), system.AllocationGranularity);
                handle = Native.OpenProcess(ProcessVmRead | ProcessQueryInformation, false, processId);
                if (handle == null || handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    handle?.Dispose();
                    throw Failure("OpenProcess(read/query, 0x0410)", error);
                }
            }

            public VanillaScanRegion Query(ulong address)
            {
                MemoryBasicInformation info;
                uint expected = (uint)Marshal.SizeOf(typeof(MemoryBasicInformation));
                UIntPtr result = Native.VirtualQueryEx(handle, ToIntPtr(address), out info, new UIntPtr(expected));
                if (result == UIntPtr.Zero) throw Failure("VirtualQueryEx at 0x" + address.ToString("X", CultureInfo.InvariantCulture), Marshal.GetLastWin32Error());
                if (result.ToUInt64() != expected) throw new MemoryObservationException("VirtualQueryEx returned incomplete region metadata at 0x" + address.ToString("X", CultureInfo.InvariantCulture) + ".");
                return new VanillaScanRegion { Address = ToAddress(info.BaseAddress), Size = info.RegionSize.ToUInt64(), State = info.State, Protection = info.Protect };
            }

            public byte[] Read(ulong address, int count)
            {
                byte[] bytes = new byte[count]; UIntPtr received;
                bool success = Native.ReadProcessMemory(handle, ToIntPtr(address), bytes, new UIntPtr((uint)count), out received);
                int error = Marshal.GetLastWin32Error();
                string operation = "ReadProcessMemory at 0x" + address.ToString("X", CultureInfo.InvariantCulture) + " (" + count + " bytes; received " + received.ToUInt64() + ")";
                if (!success) throw Failure(operation, error);
                if (received.ToUInt64() != (ulong)count) throw Failure(operation, 299);
                return bytes;
            }

            private MemoryObservationException Failure(string operation, int error)
            {
                return new MemoryObservationException(operation + " failed for PID " + ProcessId + ": Win32 " + error + " (" + new Win32Exception(error).Message + ").", error);
            }
            public void Dispose() { handle?.Dispose(); }
        }

        private sealed class ScanBlock { public ulong Address; public byte[] Bytes; }
        private struct MemoryRange { public ulong Address, Size; }
        [StructLayout(LayoutKind.Sequential)] private struct MemoryBasicInformation
        { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public UIntPtr RegionSize; public uint State, Protect, Type; }
        [StructLayout(LayoutKind.Sequential)] private struct SystemInformation
        {
            public ushort ProcessorArchitecture, Reserved;
            public uint PageSize;
            public IntPtr MinimumApplicationAddress, MaximumApplicationAddress;
            public UIntPtr ActiveProcessorMask;
            public uint NumberOfProcessors, ProcessorType, AllocationGranularity;
            public ushort ProcessorLevel, ProcessorRevision;
        }
        private static class Native
        {
            [DllImport("kernel32.dll")] internal static extern void GetSystemInfo(out SystemInformation information);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern UIntPtr VirtualQueryEx(SafeProcessHandle process, IntPtr address, out MemoryBasicInformation buffer, UIntPtr length);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, [Out] byte[] buffer, UIntPtr size, out UIntPtr bytesRead);
        }
    }
}

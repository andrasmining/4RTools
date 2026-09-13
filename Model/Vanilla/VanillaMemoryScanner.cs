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
        private readonly SafeProcessHandle handle;
        private readonly List<ScanBlock> baseline = new List<ScanBlock>();
        private List<VanillaMemoryCandidate> candidates = new List<VanillaMemoryCandidate>();
        private VanillaMemoryScanValueType baselineType;
        private VanillaMemoryScanScope baselineScope;
        private bool hasBaseline, disposed;

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

        public VanillaMemoryDiscoverySession(int processId)
        {
            using (var memory = new ReadOnlyProcessMemory(processId))
            {
                if (!string.Equals(memory.ProcessName, "Vanilla MMO", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Memory discovery is restricted to Vanilla MMO.");
                if (memory.PointerSize != 4)
                    throw new InvalidOperationException("Memory discovery currently supports the verified 32-bit Vanilla client only.");
                ProcessId = processId;
                ProcessName = memory.ProcessName;
                ExecutablePath = memory.ExecutablePath;
                MainModuleBaseAddress = memory.MainModuleBaseAddress;
                MainModuleSize = memory.MainModuleSize;
            }
            ExecutableSha256 = VanillaExecutableIdentity.Read(ExecutablePath).Sha256;
            handle = Native.OpenProcess(ProcessVmRead | ProcessQueryInformation, false, processId);
            if (handle == null || handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess(read/query) failed for Vanilla PID " + processId + ". No alternate access was attempted.");
        }

        public void CaptureBaseline(VanillaMemoryScanValueType valueType, VanillaMemoryScanScope scope)
        {
            ThrowIfDisposed();
            baseline.Clear(); candidates.Clear();
            baselineType = valueType; baselineScope = scope;
            long total = 0;
            foreach (MemoryRange range in EnumerateRanges(scope))
            {
                ulong cursor = range.Address, end = checked(range.Address + range.Size);
                while (cursor < end)
                {
                    int count = (int)Math.Min((ulong)BlockSize, end - cursor);
                    baseline.Add(new ScanBlock { Address = cursor, Bytes = ReadExact(cursor, count) });
                    total += count;
                    if (total > MaximumSnapshotBytes)
                    {
                        baseline.Clear();
                        throw new InvalidOperationException("The selected scan scope exceeds the 384 MiB snapshot limit. Use Main module first or narrow the workflow.");
                    }
                    cursor += (uint)count;
                }
            }
            if (baseline.Count == 0) throw new InvalidOperationException("No readable memory ranges were available in the selected scope.");
            BaselineBytes = total; hasBaseline = true;
        }

        public void StartExact(VanillaMemoryScanValueType valueType, VanillaMemoryScanScope scope, long exactValue)
        {
            CaptureBaseline(valueType, scope);
            ValidateSearchValue(valueType, exactValue);
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
            candidates = found;
        }

        public void FirstCompare(VanillaMemoryScanComparison comparison, long? exactValue)
        {
            ThrowIfDisposed();
            if (!hasBaseline) throw new InvalidOperationException("Capture a baseline first.");
            if (comparison == VanillaMemoryScanComparison.Unchanged)
                throw new InvalidOperationException("Do not start with Unchanged: it normally produces millions of candidates. Start with Changed, Increased, Decreased, or Exact, then refine with Unchanged.");
            ValidateExact(comparison, exactValue);
            var found = new List<VanillaMemoryCandidate>();
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
                block.Bytes = current;
            }
            candidates = found;
        }

        public void Refine(VanillaMemoryScanComparison comparison, long? exactValue)
        {
            ThrowIfDisposed();
            if (!hasBaseline) throw new InvalidOperationException("Capture a baseline or run an Exact scan first.");
            if (candidates.Count == 0) throw new InvalidOperationException("There are no candidates to refine. Reset and start a new scan.");
            ValidateExact(comparison, exactValue);
            var groups = candidates.GroupBy(item => item.BlockIndex).ToDictionary(group => group.Key, group => group.ToList());
            var kept = new List<VanillaMemoryCandidate>();
            foreach (var entry in groups)
            {
                ScanBlock block = baseline[entry.Key];
                byte[] current = ReadExact(block.Address, block.Bytes.Length);
                foreach (VanillaMemoryCandidate candidate in entry.Value)
                {
                    long before = candidate.CurrentValue, after = ReadValue(current, candidate.Offset, baselineType);
                    if (!Matches(before, after, comparison, exactValue)) continue;
                    candidate.PreviousValue = before; candidate.CurrentValue = after; kept.Add(candidate);
                }
                block.Bytes = current;
            }
            candidates = kept.OrderBy(item => item.Address).ToList();
        }

        public string ExportReport()
        {
            ThrowIfDisposed();
            return JsonConvert.SerializeObject(new
            {
                ProcessId, ProcessName, ExecutablePath, ExecutableSha256,
                MainModuleBase = "0x" + MainModuleBaseAddress.ToString("X8", CultureInfo.InvariantCulture),
                MainModuleSize, ValueType = baselineType.ToString(), Scope = baselineScope.ToString(), BaselineBytes,
                CandidateCount = candidates.Count,
                Candidates = candidates.Select(item => new { Address = item.AddressHex, MainModuleOffset = item.MainModuleOffset.HasValue ? item.ModuleOffsetHex : null, item.PreviousValue, item.CurrentValue, item.Delta }).ToArray(),
                Warning = "Discovery candidates are not verified gameplay mappings. Reproduce the transition and validate across relog/client restart before promotion."
            }, Formatting.Indented);
        }

        public string MappingSnippet(VanillaMemoryCandidate candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            var mapping = new Dictionary<string, object>();
            if (candidate.MainModuleOffset.HasValue)
            {
                mapping["Module"] = Path.GetFileName(ExecutablePath);
                mapping["Address"] = "0x" + candidate.MainModuleOffset.Value.ToString("X", CultureInfo.InvariantCulture);
            }
            else { mapping["Module"] = null; mapping["Address"] = candidate.AddressHex; }
            mapping["Encoding"] = EncodingName(baselineType);
            mapping["Evidence"] = "DISCOVERY CANDIDATE ONLY — verify semantics and stability before adding to a build profile.";
            return JsonConvert.SerializeObject(mapping, Formatting.Indented);
        }

        public void Reset() { baseline.Clear(); candidates.Clear(); BaselineBytes = 0; hasBaseline = false; }

        private IEnumerable<MemoryRange> EnumerateRanges(VanillaMemoryScanScope scope)
        {
            ulong scanStart = scope == VanillaMemoryScanScope.MainModule ? MainModuleBaseAddress : 0x10000UL;
            ulong scanEnd = scope == VanillaMemoryScanScope.MainModule ? checked(MainModuleBaseAddress + MainModuleSize) : 0xFFF00000UL;
            ulong cursor = scanStart; int returned = 0;
            while (cursor < scanEnd)
            {
                MemoryBasicInformation info;
                UIntPtr result = Native.VirtualQueryEx(handle, ToIntPtr(cursor), out info, new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryBasicInformation))));
                if (result == UIntPtr.Zero) break;
                ulong baseAddress = ToAddress(info.BaseAddress), regionSize = info.RegionSize.ToUInt64();
                if (regionSize == 0) break;
                ulong regionEnd = baseAddress > ulong.MaxValue - regionSize ? ulong.MaxValue : baseAddress + regionSize;
                ulong start = Math.Max(baseAddress, scanStart), end = Math.Min(regionEnd, scanEnd);
                if (end > start && IsReadable(info) && (scope != VanillaMemoryScanScope.AllWritableMemory || IsWritable(info.Protect)))
                { returned++; yield return new MemoryRange { Address = start, Size = end - start }; }
                ulong next = regionEnd > cursor ? regionEnd : cursor + 0x1000UL;
                if (next <= cursor) break; cursor = next;
            }
            if (returned == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualQueryEx returned no readable ranges. No alternate access was attempted.");
        }

        private static bool IsReadable(MemoryBasicInformation info)
        {
            if (info.State != MemCommit || (info.Protect & PageGuard) != 0 || (info.Protect & PageNoAccess) != 0) return false;
            uint value = info.Protect & 0xFF;
            return value == 0x02 || value == 0x04 || value == 0x08 || value == 0x20 || value == 0x40 || value == 0x80;
        }
        private static bool IsWritable(uint protect) { uint value = protect & 0xFF; return value == 0x04 || value == 0x08 || value == 0x40 || value == 0x80; }

        private byte[] ReadExact(ulong address, int count)
        {
            byte[] bytes = new byte[count]; UIntPtr received;
            if (!Native.ReadProcessMemory(handle, ToIntPtr(address), bytes, new UIntPtr((uint)count), out received) || received.ToUInt64() != (ulong)count)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ReadProcessMemory failed at 0x" + address.ToString("X", CultureInfo.InvariantCulture) + ". Discovery stopped; no retry or alternate access was attempted.");
            return bytes;
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
                if (!ulong.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out raw) || raw > uint.MaxValue) throw new ArgumentException("Invalid hexadecimal value: " + text);
                parsed = type == VanillaMemoryScanValueType.Int32 ? unchecked((int)(uint)raw) : type == VanillaMemoryScanValueType.Int16 ? unchecked((short)(ushort)raw) : (long)raw;
            }
            else if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) throw new ArgumentException("Invalid integer value: " + text);
            ValidateSearchValue(type, parsed); return parsed;
        }
        private static void ValidateSearchValue(VanillaMemoryScanValueType type, long value)
        {
            bool valid = type == VanillaMemoryScanValueType.Byte ? value >= byte.MinValue && value <= byte.MaxValue
                : type == VanillaMemoryScanValueType.UInt16 ? value >= ushort.MinValue && value <= ushort.MaxValue
                : type == VanillaMemoryScanValueType.Int16 ? value >= short.MinValue && value <= short.MaxValue
                : type == VanillaMemoryScanValueType.UInt32 ? value >= uint.MinValue && value <= uint.MaxValue
                : value >= int.MinValue && value <= int.MaxValue;
            if (!valid) throw new ArgumentOutOfRangeException(nameof(value), "Value does not fit " + type + ".");
        }
        private static string EncodingName(VanillaMemoryScanValueType type) { return type == VanillaMemoryScanValueType.Byte ? "Boolean8" : type == VanillaMemoryScanValueType.UInt32 ? "UInt32" : type == VanillaMemoryScanValueType.Int32 ? "Int32" : type.ToString(); }
        private static ulong ToAddress(IntPtr pointer) { return IntPtr.Size == 4 ? unchecked((uint)pointer.ToInt32()) : unchecked((ulong)pointer.ToInt64()); }
        private static IntPtr ToIntPtr(ulong address) { return IntPtr.Size == 4 ? new IntPtr(unchecked((int)(uint)address)) : new IntPtr(unchecked((long)address)); }
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(VanillaMemoryDiscoverySession)); }
        public void Dispose() { if (disposed) return; disposed = true; baseline.Clear(); candidates.Clear(); if (handle != null) handle.Dispose(); }

        private sealed class ScanBlock { public ulong Address; public byte[] Bytes; }
        private struct MemoryRange { public ulong Address, Size; }
        [StructLayout(LayoutKind.Sequential)] private struct MemoryBasicInformation
        { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public UIntPtr RegionSize; public uint State, Protect, Type; }
        private static class Native
        {
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern UIntPtr VirtualQueryEx(SafeProcessHandle process, IntPtr address, out MemoryBasicInformation buffer, UIntPtr length);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, [Out] byte[] buffer, UIntPtr size, out UIntPtr bytesRead);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using _4RTools.Utils;

namespace _4RTools.Model.Vanilla
{
    public sealed class DemoStateSource : IStateSource
    {
        private readonly Guid session = Guid.NewGuid();
        private VanillaClientState previous;
        private int sample;
        public bool IsStopped { get; private set; }
        public string Status { get { return IsStopped ? "Demo stopped." : "DEMO — synthetic values; no process access or input."; } }

        public VanillaClientState Poll(DateTimeOffset now)
        {
            now = now.ToUniversalTime();
            if (previous != null && now < previous.SampledAtUtc) now = previous.SampledAtUtc;
            Dictionary<VanillaField, object> values = null;
            if (!IsStopped)
            {
                int step = sample;
                sample = (sample + 1) % 12;
                values = new Dictionary<VanillaField, object>
                {
                    { VanillaField.CurrentHP, (uint)(1000 - (step % 4) * 50) }, { VanillaField.MaxHP, 1000U },
                    { VanillaField.CurrentSP, (uint)(step == 0 ? 0 : 300 - step * 10) }, { VanillaField.MaxSP, 300U },
                    { VanillaField.CharacterName, "Demo character" }, { VanillaField.X, 100 + step / 3 }, { VanillaField.Y, 200 },
                    { VanillaField.CurrentTargetId, step < 4 ? 0UL : (step < 8 ? 1001UL : 1002UL) },
                    { VanillaField.ActionState, step < 4 ? 0U : 1U }, { VanillaField.Map, "synthetic_map" },
                    { VanillaField.AutobattleEnabled, true }, { VanillaField.StatusEffects, step < 6 ? new uint[0] : new uint[] { 1, 2 } }
                };
            }
            previous = VanillaClientState.Create(session, now, previous, values, null, null, IsStopped ? "Demo stopped." : null);
            previous.IsDemo = true;
            previous.ProcessName = "DEMO (no process)";
            previous.ConnectionStatus = Status;
            return previous;
        }

        public void Dispose() { IsStopped = true; previous = null; }
    }

    // Owned by the diagnostics UI timer; no background polling, game input or rule execution.
    public sealed class MemoryStateSource : IStateSource
    {
        private readonly IReadOnlyProcessMemory memory;
        private readonly VanillaMemoryMap map;
        private Guid session = Guid.NewGuid();
        private VanillaClientState previous;
        private string failure;
        public bool IsStopped { get; private set; }
        public string Status { get; private set; }

        public MemoryStateSource(int processId, VanillaMemoryMap map) : this(OpenValidated(processId, map), map) { }

        public MemoryStateSource(IReadOnlyProcessMemory memory, VanillaMemoryMap map)
        {
            this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
            try
            {
                // Freeze the configuration for this session, including offsets and evidence.
                this.map = VanillaMemoryMap.Parse((map ?? throw new ArgumentNullException(nameof(map))).ToJson());
                if (memory.PointerSize != 4 && memory.PointerSize != 8) throw new ArgumentException("Target pointer size must be 4 or 8 bytes.");
                if (!string.IsNullOrWhiteSpace(this.map.ProcessName) && !string.Equals(
                    NormalizeProcessName(this.map.ProcessName), NormalizeProcessName(memory.ProcessName), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Configured process '" + this.map.ProcessName + "' does not match selected process '" + memory.ProcessName + "'.");
                if (memory.IsStopped) throw new MemoryObservationException(memory.LastError ?? "Memory session is already stopped.");
                Status = this.map.Fields.Count == 0
                    ? "Connected — metadata only; no fields configured."
                    : "Observing configured addresses — signal meanings remain unverified.";
            }
            catch
            {
                memory.Dispose();
                throw;
            }
        }

        private static IReadOnlyProcessMemory OpenValidated(int processId, VanillaMemoryMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            map.Validate();
            return new ReadOnlyProcessMemory(processId);
        }

        private static string NormalizeProcessName(string name)
        {
            return name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }

        public VanillaClientState Poll(DateTimeOffset now)
        {
            now = now.ToUniversalTime();
            if (previous != null && now < previous.SampledAtUtc) now = previous.SampledAtUtc;
            Dictionary<VanillaField, object> values = null;
            Dictionary<VanillaField, ulong> addresses = null;
            string operation = "Process metadata";
            if (!IsStopped)
            {
                try
                {
                    memory.EnsureAlive();
                    values = new Dictionary<VanillaField, object>();
                    addresses = new Dictionary<VanillaField, ulong>();
                    foreach (var entry in map.Fields.OrderBy(item => item.Key))
                    {
                        operation = entry.Key.ToString();
                        ulong address = ResolveAddress(entry.Value);
                        byte[] bytes = ReadExact(address, entry.Value.ReadSize);
                        values.Add(entry.Key, Decode(entry.Key, entry.Value, bytes));
                        addresses.Add(entry.Key, address);
                    }
                }
                catch (Exception ex)
                {
                    failure = operation + ": " + ex.Message;
                    IsStopped = true;
                    Status = "Stopped after observation failure; session closed. " + failure;
                    memory.Dispose();
                    // Discard partial samples and all continuity when any read/query/decoding fails.
                    values = null;
                    addresses = null;
                    previous = null;
                    session = Guid.NewGuid();
                }
            }
            previous = VanillaClientState.Create(session, now, previous, values, addresses, map,
                IsStopped ? (failure ?? "Observation session stopped.") : null);
            previous.ProcessId = memory.ProcessId;
            previous.ProcessName = memory.ProcessName;
            previous.ModuleBaseAddress = memory.MainModuleBaseAddress;
            previous.TargetPointerSize = memory.PointerSize;
            previous.ConnectionStatus = Status;
            return previous;
        }

        private ulong ResolveAddress(VanillaFieldMapping mapping)
        {
            ulong address = VanillaMemoryMap.ParseAddress(mapping.Address);
            if (!string.IsNullOrWhiteSpace(mapping.Module)) address = checked(memory.GetModuleBase(mapping.Module) + address);
            foreach (string offset in mapping.PointerOffsets)
            {
                byte[] bytes = ReadExact(address, memory.PointerSize);
                ulong pointer = memory.PointerSize == 4 ? BitConverter.ToUInt32(bytes, 0) : BitConverter.ToUInt64(bytes, 0);
                if (pointer == 0) throw new MemoryObservationException("Null pointer at 0x" + address.ToString("X") + "; session stopped.");
                address = checked(pointer + VanillaMemoryMap.ParseAddress(offset));
            }
            ReadOnlyProcessMemory.ValidateRange(address, mapping.ReadSize, memory.PointerSize);
            return address;
        }

        private byte[] ReadExact(ulong address, int count)
        {
            ReadOnlyProcessMemory.ValidateRange(address, count, memory.PointerSize);
            byte[] bytes = memory.ReadBytes(address, count);
            if (bytes == null || bytes.Length != count)
                throw new MemoryObservationException("Read at 0x" + address.ToString("X") + " returned " + (bytes == null ? "null" : bytes.Length.ToString()) + " instead of " + count + " bytes; session stopped.");
            return bytes;
        }

        private static object Decode(VanillaField field, VanillaFieldMapping mapping, byte[] bytes)
        {
            switch (mapping.Encoding)
            {
                case VanillaValueEncoding.UInt32:
                    uint unsigned = BitConverter.ToUInt32(bytes, 0);
                    return field == VanillaField.CurrentTargetId ? (object)(ulong)unsigned : unsigned;
                case VanillaValueEncoding.UInt64: return BitConverter.ToUInt64(bytes, 0);
                case VanillaValueEncoding.Int32: return BitConverter.ToInt32(bytes, 0);
                case VanillaValueEncoding.Boolean8:
                    if (bytes[0] > 1) throw new MemoryObservationException("Boolean8 sample was neither 0 nor 1; configured encoding is unverified.");
                    return bytes[0] == 1;
                case VanillaValueEncoding.Utf8:
                    int end = Array.IndexOf(bytes, (byte)0);
                    return new UTF8Encoding(false, true).GetString(bytes, 0, end < 0 ? bytes.Length : end);
                case VanillaValueEncoding.UInt32Array:
                    var effects = new uint[bytes.Length / 4];
                    for (int i = 0; i < effects.Length; i++) effects[i] = BitConverter.ToUInt32(bytes, i * 4);
                    return effects;
                default: throw new ArgumentException("Unsupported encoding.");
            }
        }

        public void Dispose()
        {
            IsStopped = true;
            Status = failure == null ? "Observation session stopped." : Status;
            previous = null;
            memory.Dispose();
        }
    }
}

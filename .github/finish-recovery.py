from pathlib import Path
import sys
mode=sys.argv[1]
assert mode in ('tests','fix')
def edit(name,old,new):
 p=Path(name);raw=p.read_bytes();s=raw.decode().replace('\r\n','\n');assert s.count(old)==1,(name,old[:90],s.count(old));s=s.replace(old,new);p.write_bytes(s.replace('\n','\r\n').encode() if b'\r\n' in raw else s.encode())
if mode=='tests':
 edit('Tests/VanillaNativeRecoveryTests.cs','ShowInTaskbar = false','ShowInTaskbar = true')
 edit('Tests/VanillaRecoveryWatchdogTests.cs','            Test("Intermediate movement survives a return to the same position", Intermediate);','''            Test("Intermediate movement survives a return to the same position", Intermediate);
            Test("First observed intermediate movement resets the deadline", FirstIntermediate);
            Test("Fleet tracks intermediate movement without a readiness field", FleetMovement);
            Test("Fleet does not infer motion across an invalid observation", FleetInvalidMovement);''')
 edit('Tests/VanillaRecoveryWatchdogTests.cs','        private static void Context()','''        private static void FirstIntermediate()
        {
            var w = new VanillaMovementWatchdog(); Check(w, 0, S(0));
            Check(w, 1, S(1, moved: .5));
            Assert(Check(w, 120, S(120, moved: .5)) == null, "First intermediate motion was lost.");
            Assert(Check(w, 121, S(121, moved: .5)) != null, "Motion kept extending without another move.");
        }
        private static void FleetMovement()
        {
            var reader = new Reader { Pid = 101 };
            using (var fleet = new VanillaFleetMonitor(Path.GetTempPath(),
                () => new VanillaFleetMonitor.IProcessMetadata[] { new Meta { ProcessId = 101 } }, pid => reader, () => null))
            {
                fleet.Poll(); var w = new VanillaMovementWatchdog(); Check(w, 0, fleet.LatestPosition(101));
                reader.Frame = S(.5, 11); fleet.Poll();
                reader.Frame = S(1, 10); fleet.Poll();
                var returned = fleet.LatestPosition(101);
                Assert(returned.MovementAt.HasValue && returned.MovementAt.Value == Epoch.AddSeconds(1),
                    "Verified X/Y motion depended on unavailable ClientReady or was lost between polls.");
                Check(w, 1, returned);
                Assert(Check(w, 120, S(120, moved: 1)) == null, "Healthy movement-and-return caused restart.");
            }
        }
        private static void FleetInvalidMovement()
        {
            var reader = new Reader { Pid = 101 };
            using (var fleet = new VanillaFleetMonitor(Path.GetTempPath(),
                () => new VanillaFleetMonitor.IProcessMetadata[] { new Meta { ProcessId = 101 } }, pid => reader, () => null))
            {
                fleet.Poll(); reader.Frame = S(.5, 11, verified: false); fleet.Poll();
                reader.Frame = S(1, 10); fleet.Poll();
                Assert(!fleet.LatestPosition(101).MovementAt.HasValue, "Unverified position fabricated motion.");
            }
        }
        private static void Context()''')
 edit('Tests/VanillaRecoveryWatchdogTests.cs','internal int Pid;internal bool Disposed;public bool IsStopped','internal int Pid;internal VanillaPositionSample Frame;internal bool Disposed;public bool IsStopped')
 edit('Tests/VanillaRecoveryWatchdogTests.cs','Position=S(0,pid:Pid)','Position=Frame??S(0,pid:Pid)')
else:
 edit('Model/Vanilla/VanillaMovementWatchdog.cs','''                bool intermediateMovement = sample.MovementAt.HasValue && movementAt.HasValue
                    && sample.MovementAt.Value > movementAt.Value && sample.MovementAt.Value <= sample.At;''','''                bool intermediateMovement = sample.MovementAt.HasValue && observedAt.HasValue
                    && sample.MovementAt.Value > observedAt.Value && sample.MovementAt.Value <= sample.At;''')
 edit('Model/Vanilla/VanillaMovementWatchdog.cs','''            var next = clients.ToDictionary(c => c.ProcessId, c => c.Position);
            System.Threading.Volatile.Write(ref positionCache, next);''','''            var previous = System.Threading.Volatile.Read(ref positionCache);
            var next = new Dictionary<int, VanillaPositionSample>();
            foreach (var client in clients)
            {
                VanillaPositionSample before;
                previous.TryGetValue(client.ProcessId, out before);
                next[client.ProcessId] = TrackPosition(client.Position, before);
            }
            System.Threading.Volatile.Write(ref positionCache, next);''')
 edit('Model/Vanilla/VanillaMovementWatchdog.cs','''        // Only called after the supervisor has positively confirmed process exit.''','''        private static bool UsablePosition(VanillaPositionSample value)
        {
            return value != null && value.Verified && value.Error == null && value.Session != Guid.Empty
                && value.X.HasValue && value.Y.HasValue;
        }

        private static VanillaPositionSample TrackPosition(VanillaPositionSample value, VanillaPositionSample before)
        {
            if (!UsablePosition(value)) return value;
            DateTimeOffset? moved = value.MovementAt.HasValue && value.MovementAt.Value <= value.At ? value.MovementAt : null;
            bool continuous = UsablePosition(before) && before.Pid == value.Pid && before.Session == value.Session
                && string.Equals(before.Map, value.Map, StringComparison.Ordinal)
                && value.At > before.At && (value.At - before.At).TotalSeconds <= 3;
            if (continuous)
            {
                // Position is independently verified even when ClientReady/combat fields
                // are unsupported. Do not promote those other fields to known/true.
                if (value.X != before.X || value.Y != before.Y) moved = value.At;
                else if (before.MovementAt.HasValue && (!moved.HasValue || before.MovementAt > moved)) moved = before.MovementAt;
            }
            return new VanillaPositionSample(value.Pid, value.Session, value.At, value.X, value.Y,
                value.Map, value.Verified, value.Error, moved);
        }

        // Only called after the supervisor has positively confirmed process exit.''')
 edit('Model/Vanilla/VanillaMovementWatchdog.cs','private DateTimeOffset? observedAt, movementAt;','private DateTimeOffset? observedAt;')
 edit('Model/Vanilla/VanillaMovementWatchdog.cs','observedAt = movementAt = null;','observedAt = null;')
 edit('Model/Vanilla/VanillaMovementWatchdog.cs','                movementAt = sample.MovementAt;\n','')
print('Applied',mode,'corrections.')

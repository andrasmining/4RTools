using System;
using System.Collections.Generic;
using System.Linq;

namespace _4RTools.Model.Vanilla
{
    // Immutable, typed output of the existing fingerprinted read-only fleet reader.
    // Missing, failed and unverified values are never replaced with coordinate zero.
    internal sealed class VanillaPositionSample
    {
        internal readonly int Pid;
        internal readonly Guid Session;
        internal readonly DateTimeOffset At;
        internal readonly DateTimeOffset? MovementAt;
        internal readonly int? X, Y;
        internal readonly string Map, Error;
        internal readonly bool Verified;
        internal VanillaPositionSample(int pid, Guid session, DateTimeOffset at, int? x, int? y,
            string map, bool verified, string error = null, DateTimeOffset? movementAt = null)
        { Pid = pid; Session = session; At = at; X = x; Y = y; Map = map; Verified = verified; Error = error; MovementAt = movementAt; }
    }

    internal sealed class VanillaMovementWatchdog
    {
        internal const int TimeoutSeconds = 30;
        private bool armed, baseline;
        private int pid, x, y;
        private Guid session;
        private string map;
        private TimeSpan progressAt, previousClock;
        private DateTimeOffset? observedAt;
        internal bool IsArmed { get { return armed; } }

        internal void Reset() { armed = baseline = false; observedAt = null; }

        internal string Observe(int processId, VanillaPositionSample sample, TimeSpan now, DateTimeOffset utc)
        {
            if (!armed || pid != processId || now < previousClock)
            {
                Reset(); armed = true; pid = processId; progressAt = now;
            }
            previousClock = now;
            string unavailable = sample == null ? "coordinates unavailable" : sample.Error;
            bool valid = sample != null && sample.Pid == processId && sample.Verified && sample.Error == null
                && sample.Session != Guid.Empty && sample.X.HasValue && sample.Y.HasValue
                && sample.At <= utc && (utc - sample.At).TotalSeconds <= 3
                && (!observedAt.HasValue || sample.At > observedAt.Value);
            if (valid)
            {
                if (baseline && (sample.Session != session || (sample.Map != null && map != null && !string.Equals(sample.Map, map, StringComparison.Ordinal))))
                {
                    // A new session/map starts a new baseline; it is not inferred movement.
                    baseline = false; progressAt = now;
                }
                bool intermediateMovement = sample.MovementAt.HasValue && observedAt.HasValue
                    && sample.MovementAt.Value > observedAt.Value && sample.MovementAt.Value <= sample.At;
                if (baseline && (sample.X.Value != x || sample.Y.Value != y || intermediateMovement)) progressAt = now;
                x = sample.X.Value; y = sample.Y.Value;
                session = sample.Session; map = sample.Map ?? map;
                baseline = true; observedAt = sample.At;
            }
            // A first/reappearing unchanged reading is not movement and must not extend
            // the deadline. Neither stale snapshots nor transient unknowns reset it.
            if (now - progressAt < TimeSpan.FromSeconds(TimeoutSeconds)) return null;
            return "No verified X/Y movement for " + (int)(now - progressAt).TotalSeconds + "s; "
                + (valid ? "coordinates unchanged at " + x + "," + y
                    : string.IsNullOrWhiteSpace(unavailable) ? "coordinates unreadable, unverified or stale" : unavailable);
        }
    }

    public sealed partial class VanillaFleetMonitor
    {
        private IReadOnlyDictionary<int, VanillaPositionSample> positionCache = new Dictionary<int, VanillaPositionSample>();

        internal VanillaPositionSample LatestPosition(int pid)
        {
            VanillaPositionSample sample;
            var cache = System.Threading.Volatile.Read(ref positionCache);
            return cache.TryGetValue(pid, out sample) ? sample : null;
        }

        private void PublishPositions(IReadOnlyList<VanillaFleetClientInfo> clients)
        {
            var previous = System.Threading.Volatile.Read(ref positionCache);
            var next = new Dictionary<int, VanillaPositionSample>();
            foreach (var client in clients)
            {
                VanillaPositionSample before;
                previous.TryGetValue(client.ProcessId, out before);
                next[client.ProcessId] = TrackPosition(client.Position, before);
            }
            System.Threading.Volatile.Write(ref positionCache, next);
        }

        private static bool UsablePosition(VanillaPositionSample value)
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

        // Only called after the supervisor has positively confirmed process exit.
        // Prevents a quickly recycled PID from inheriting an old stopped reader.
        internal void ConfirmClientExited(int pid)
        {
            lock (gate)
            {
                IClientReader reader;
                if (readers.TryGetValue(pid, out reader))
                { readers.Remove(pid); try { reader.Dispose(); } catch (Exception ex) { VanillaDebugLog.Write("RECOVERY", "Exited reader cleanup: " + ex.Message); } }
                System.Threading.Volatile.Write(ref characterCache, Array.AsReadOnly(characterCache.Where(c => c.ProcessId != pid).ToArray()));
                var next = positionCache.Where(p => p.Key != pid).ToDictionary(p => p.Key, p => p.Value);
                System.Threading.Volatile.Write(ref positionCache, next);
            }
        }
    }

    public sealed partial class VanillaReconnectSupervisor
    {
        private Func<int, VanillaPositionSample> positionSource;
        private System.Action<int> positionClientExited;

        internal void SetPositionSource(Func<int, VanillaPositionSample> source, System.Action<int> exited)
        {
            lock (gate)
            {
                positionSource = source ?? throw new ArgumentNullException(nameof(source));
                positionClientExited = exited;
                foreach (Runtime runtime in runtimes.Values) runtime.MovementWatchdog.Reset();
            }
        }

        private bool CheckMovementWatchdog(Runtime runtime, DateTimeOffset now, Func<DateTime> startTimeUtc,
            Func<VanillaVisualState> readVisual)
        {
            if (!running || disposed || !settings.AutoRecover || !runtime.Account.Enabled || !runtime.ProcessId.HasValue
                || runtime.ScriptRunning || runtime.RecoveryOwned || positionSource == null || runtime.AutobattleRecoveryExhausted
                || (!runtime.ResumeSent && !runtime.HasBeenOnline && !runtime.MovementRecoveryPending))
            { runtime.MovementWatchdog.Reset(); return false; }
            VanillaPositionSample sample = null;
            try { sample = positionSource(runtime.ProcessId.Value); }
            catch (Exception ex) { Log(runtime.Account.Label + ": coordinate source unavailable: " + ex.Message); }
            string reason = runtime.MovementWatchdog.Observe(runtime.ProcessId.Value, sample, restartEnvironment.MonotonicNow, now);
            if (reason == null) return false;
            string visualEvidence;
            try { runtime.Visual = readVisual(); visualEvidence = runtime.Visual.ToString(); }
            catch (Exception ex) { visualEvidence = "capture unavailable: " + ex.Message; }
            bool firstTrigger = !runtime.MovementRecoveryPending;
            runtime.MovementRecoveryPending = true;
            runtime.ResumeVerificationFailed = false;
            runtime.ResumeFailureDetail = null;
            string detail = reason + "; visual=" + visualEvidence
                + ". Autofarming watchdog will try the configured hotkey before restarting the client.";
            if (firstTrigger) Log(runtime.Account.Label + ": " + detail);
            RequestVerifiedResume(runtime, "30s movement watchdog fired.", true);
            return true;
        }
    }
}

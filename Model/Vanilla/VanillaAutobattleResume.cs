using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using _4RTools.Utils;

namespace _4RTools.Model.Vanilla
{
    /// <summary>One bounded resume attempt, shared by startup, recovery and the resume diagnostic.</summary>
    internal sealed class VanillaAutobattleResumeVerifier
    {
        internal const int MaximumAttempts = 3;
        internal const int ObservationWindowMs = 10000;
        internal const int PollIntervalMs = 100;
        internal const int MaximumSampleAgeMs = 1000;
        private bool started;
        internal int Attempts { get; private set; }
        internal bool MovementVerified { get; private set; }

        internal async Task VerifyAsync(int processId, Func<VanillaClientState> read, System.Action focus, System.Action send,
            Func<bool> cancelled, Func<TimeSpan> clock, Func<DateTimeOffset> utcNow,
            Func<int, Task> delay, System.Action<string> report)
        {
            if (started) throw new InvalidOperationException("A resume verification cannot be restarted.");
            started = true;
            if (processId <= 0 || read == null || focus == null || send == null || cancelled == null
                || clock == null || utcNow == null || delay == null || report == null)
                throw new ArgumentException("Resume verification requires a client and complete observation/input services.");

            VanillaClientState identity = null, baseline = null, previousRead = null;
            TimeSpan lastClock = clock();
            Func<TimeSpan> now = () =>
            {
                TimeSpan value = clock();
                if (value < lastClock) throw new InvalidOperationException("Resume verification clock moved backwards.");
                lastClock = value;
                return value;
            };
            System.Action checkCancelled = () =>
            {
                if (cancelled()) throw new OperationCanceledException("Autobattle verification cancelled; no further hotkeys will be sent.");
            };
            Func<VanillaClientState> sample = () =>
            {
                checkCancelled();
                TimeSpan began = now();
                VanillaClientState state = read();
                checkCancelled();
                TimeSpan finished = now();
                if ((finished - began).TotalMilliseconds > MaximumSampleAgeMs || ReferenceEquals(state, previousRead))
                    throw new InvalidOperationException("Autobattle coordinates are stale; verification stopped.");
                ValidateSample(state, identity, processId, utcNow());
                identity = identity ?? state;
                previousRead = state;
                return state;
            };

            for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                checkCancelled();
                if (attempt > 1) report("Retrying autobattle " + attempt + "/" + MaximumAttempts);
                focus();
                checkCancelled();
                VanillaClientState current = sample();
                // Focusing may take time. Recheck after focus, before pressing a toggle again.
                if (baseline != null && Moved(baseline, current))
                {
                    MovementVerified = true;
                    report("Movement verified after " + Attempts + "/" + MaximumAttempts + " attempts");
                    return;
                }
                baseline = current;
                checkCancelled();
                Attempts = attempt;
                send();
                checkCancelled();
                TimeSpan deadline = now() + TimeSpan.FromMilliseconds(ObservationWindowMs);
                report("Verifying autobattle " + attempt + "/" + MaximumAttempts + " (10s)");
                while (true)
                {
                    current = sample();
                    if (Moved(baseline, current))
                    {
                        MovementVerified = true;
                        report("Movement verified after " + attempt + "/" + MaximumAttempts + " attempts");
                        return;
                    }
                    TimeSpan remaining = deadline - now();
                    if (remaining <= TimeSpan.Zero) break;
                    await delay(Math.Max(1, Math.Min(PollIntervalMs, (int)Math.Ceiling(remaining.TotalMilliseconds))))
                        .ConfigureAwait(false);
                    checkCancelled();
                }
            }
            throw new InvalidOperationException("Failed: no movement after 3 attempts (10 seconds per attempt). No further autobattle hotkeys will be sent.");
        }

        private static bool Moved(VanillaClientState before, VanillaClientState after)
        {
            return before.X.Value != after.X.Value || before.Y.Value != after.Y.Value;
        }

        internal static void ValidateSample(VanillaClientState state, VanillaClientState identity, int processId, DateTimeOffset now)
        {
            if (state == null || state.IsDemo || state.Error != null || state.ProcessId != processId || state.SessionId == Guid.Empty)
                throw new InvalidOperationException("Autobattle observation is unavailable or belongs to a different client.");
            double age = (now - state.SampledAtUtc).TotalMilliseconds;
            if (age < 0 || age > MaximumSampleAgeMs)
                throw new InvalidOperationException("Autobattle coordinates are stale; verification stopped.");
            foreach (VanillaField field in new[] { VanillaField.X, VanillaField.Y, VanillaField.Map,
                VanillaField.CharacterName, VanillaField.CurrentHP, VanillaField.MaxHP })
            {
                StateValue value;
                if (state.Fields == null || !state.Fields.TryGetValue(field, out value) || value == null
                    || !value.IsAvailable || value.Validation != StateValidation.Valid
                    || value.LastObservedAtUtc != state.SampledAtUtc)
                    throw new InvalidOperationException("Autobattle requires fresh, verified " + field + "; no hotkey sent for an unknown value.");
            }
            if (state.X.Value < 0 || state.Y.Value < 0 || string.IsNullOrWhiteSpace(state.Map.Value)
                || string.IsNullOrWhiteSpace(state.CharacterName.Value) || state.CurrentHP.Value == 0
                || state.MaxHP.Value == 0 || state.CurrentHP.Value > state.MaxHP.Value)
                throw new InvalidOperationException("Autobattle requires valid coordinates, map and a living character.");
            if ((state.Loading.IsAvailable && state.Loading.Validation == StateValidation.Valid && state.Loading.Value)
                || (state.ClientReady.IsAvailable && state.ClientReady.Validation == StateValidation.Valid && !state.ClientReady.Value))
                throw new InvalidOperationException("The client is loading or no longer ready for autobattle.");
            if (identity != null && (identity.SessionId != state.SessionId
                || !string.Equals(identity.Map.Value, state.Map.Value, StringComparison.Ordinal)
                || !string.Equals(identity.CharacterName.Value, state.CharacterName.Value, StringComparison.Ordinal)))
                throw new InvalidOperationException("Client session, map or character changed during autobattle verification.");
        }
    }

    internal static class VanillaAutobattleStatus
    {
        internal static string Compact(VanillaReconnectStage stage, string detail)
        {
            if (stage != VanillaReconnectStage.VerifyingAutobattle) return stage.ToString();
            detail = detail ?? "";
            if (detail.IndexOf("Movement verified", StringComparison.Ordinal) >= 0) return "Movement verified";
            string prefix = detail.IndexOf("Retrying", StringComparison.Ordinal) >= 0 ? "Retry " : "Verify ";
            for (int attempt = 1; attempt <= VanillaAutobattleResumeVerifier.MaximumAttempts; attempt++)
            {
                string number = attempt + "/" + VanillaAutobattleResumeVerifier.MaximumAttempts;
                if (detail.IndexOf(number, StringComparison.Ordinal) >= 0) return prefix + number;
            }
            return "Verifying autobattle";
        }
    }

    public sealed partial class VanillaReconnectSupervisor
    {
        // Invalidates workers even if STOP is immediately followed by another START with the same PID.
        private int resumeVerificationGeneration;

        // Shipped build profiles live beside the executable, never in mutable user data.
        internal static string AutobattleBuildProfileDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VanillaBuilds"); }
        }

        private bool ResumeWorkerCancelled(Runtime owner, int pid, int generation)
        {
            lock (gate)
            {
                Runtime current;
                return disposed || generation != Volatile.Read(ref resumeVerificationGeneration)
                    || !runtimes.TryGetValue(owner.Account.Id, out current) || !ReferenceEquals(owner, current)
                    || current.ProcessId != pid || current.ResumeOperationGeneration != generation
                    || !current.Account.Enabled || !current.ScriptRunning || CharacterOwnershipChanged(current, pid);
            }
        }

        private void ResumeProgress(Runtime owner, int pid, int generation, string detail)
        {
            lock (gate)
            {
                Runtime current;
                if (generation != Volatile.Read(ref resumeVerificationGeneration) || disposed || !runtimes.TryGetValue(owner.Account.Id, out current)
                    || !ReferenceEquals(owner, current) || current.ProcessId != pid
                    || current.ResumeOperationGeneration != generation || !current.ScriptRunning) return;
                SetStage(current, VanillaReconnectStage.VerifyingAutobattle, detail);
            }
            Log(owner.Account.Label + ": " + detail);
            RaiseUpdated();
        }

        private async Task VerifyAutobattleResumeAsync(VanillaReconnectAccount account, int pid,
            Func<bool> cancelled, System.Action<string> progress)
        {
            if (cancelled()) throw new OperationCanceledException("Autobattle verification cancelled.");
            using (var memory = new ReadOnlyProcessMemory(pid))
            {
                var identity = VanillaExecutableIdentity.Read(memory.ExecutablePath);
                var profile = VanillaBuildProfile.Find(AutobattleBuildProfileDirectory, identity,
                    message => Log(account.Label + ": " + message));
                if (profile == null) throw new InvalidOperationException("No verified Vanilla build profile; autobattle resume was not sent.");
                var adapter = new VanillaStateAdapter(profile, identity);
                var file = new FileInfo(memory.ExecutablePath);
                long executableLength = file.Length;
                DateTime executableWriteTime = file.LastWriteTimeUtc;
                using (var source = new MemoryStateSource(memory, profile.MemoryMap))
                using (var input = new VanillaForegroundInput(pid))
                {
                    input.CancellationRequested = cancelled;
                    var clock = Stopwatch.StartNew();
                    var verifier = new VanillaAutobattleResumeVerifier();
                    VanillaClientState lastIdentity = null;
                    Func<VanillaClientState> read = () =>
                    {
                        var currentFile = new FileInfo(memory.ExecutablePath);
                        if (!currentFile.Exists || currentFile.Length != executableLength || currentFile.LastWriteTimeUtc != executableWriteTime)
                            throw new InvalidOperationException("The Vanilla executable changed during verification.");
                        if (VanillaVisualProbe.Classify(input.Window) != VanillaVisualState.Gameplay)
                            throw new InvalidOperationException("Gameplay is no longer confirmed; autobattle verification stopped.");
                        var state = source.Poll(DateTimeOffset.UtcNow);
                        if (source.IsStopped) throw new InvalidOperationException(state.Error ?? source.Status);
                        adapter.Observe(state, clock.Elapsed);
                        ValidateExpectedCharacter(account, state);
                        lastIdentity = state;
                        return state;
                    };
                    await verifier.VerifyAsync(pid, read, input.Activate,
                        () => input.ChordInVerifiedForeground(account.ResumeCtrl, account.ResumeAlt, account.ResumeShift,
                            (Keys)account.ResumeKey), cancelled, () => clock.Elapsed, () => DateTimeOffset.UtcNow,
                        milliseconds => Task.Delay(milliseconds), progress).ConfigureAwait(false);
                    lock (gate)
                    {
                        Runtime owner;
                        if (!cancelled() && runtimes.TryGetValue(account.Id, out owner) && owner.ProcessId == pid)
                        {
                            owner.ConfirmedCharacter = VanillaCharacterIdentity.FromState(lastIdentity);
                            var fleetIdentity = CurrentCharacter(pid);
                            if (fleetIdentity != null && owner.ConfirmedCharacter != null
                                && VanillaCharacterRoster.Same(fleetIdentity.CharacterName, owner.ConfirmedCharacter.CharacterName))
                                owner.CharacterSession = fleetIdentity.Session;
                        }
                    }
                }
            }
        }

        private bool RunOwnedClientStep(Runtime owner, int pid, Func<bool> cancelled, Func<bool> action)
        {
            lock (gate)
            {
                Runtime current;
                if (cancelled() || disposed || !runtimes.TryGetValue(owner.Account.Id, out current)
                    || !ReferenceEquals(owner, current) || current.ProcessId != pid || !current.Account.Enabled || CharacterOwnershipChanged(current, pid))
                    throw new OperationCanceledException("Client ownership changed; no window action performed.");
                return action();
            }
        }

        internal static bool CanAdoptExistingGameplayClient(bool resumeSent, bool failed, bool busy)
        {
            return resumeSent && !failed && !busy;
        }

        private static void RecordDiagnosticResumeResult(Runtime runtime, bool succeeded, string failure)
        {
            runtime.ResumeSent = succeeded;
            runtime.ResumeVerificationFailed = !succeeded;
            runtime.ResumeFailureDetail = succeeded ? null : "Autobattle verification failed: " + failure;
            if (succeeded) runtime.HasBeenOnline = true;
        }

        private void QueueVerifiedResume(Runtime runtime)
        {
            if (runtime.ScriptRunning || runtime.ResumeVerificationFailed || !runtime.ProcessId.HasValue) return;
            Runtime owner = OtherRecoveryOwner(runtime);
            if (owner != null)
            {
                SetStage(runtime, VanillaReconnectStage.WaitingForGameplay,
                    "Queued: waiting for " + owner.Account.Label + " before autobattle verification");
                return;
            }
            int pid = runtime.ProcessId.Value;
            int generation = Interlocked.Increment(ref resumeVerificationGeneration);
            runtime.ResumeOperationGeneration = generation;
            var account = runtime.Account.Clone();
            runtime.ScriptRunning = true;
            runtime.RecoveryOwned = true;
            SetStage(runtime, VanillaReconnectStage.VerifyingAutobattle, "Preparing autobattle verification 1/3");
            // The continuation is owned by this Task, never an async-void ThreadPool callback.
            Task.Run(async () =>
            {
                string error = null;
                bool cancelled = false;
                try
                {
                    await VerifyAutobattleResumeAsync(account, pid,
                        () => !IsRunning || ResumeWorkerCancelled(runtime, pid, generation),
                        detail => ResumeProgress(runtime, pid, generation, detail)).ConfigureAwait(false);
                    if (!IsRunning || ResumeWorkerCancelled(runtime, pid, generation)) throw new OperationCanceledException();
                    if (!RunOwnedClientStep(runtime, pid,
                        () => !IsRunning || ResumeWorkerCancelled(runtime, pid, generation),
                        () => KeepAssignedClientMinimized(account.Id)))
                        throw new InvalidOperationException("Movement verified but client minimization could not be confirmed.");
                }
                catch (OperationCanceledException) { cancelled = true; }
                catch (Exception ex) { error = ex.Message; }
                lock (gate)
                {
                    // An old worker must never publish success or clear a new worker's input lease.
                    Runtime current;
                    if (!runtimes.TryGetValue(account.Id, out current) || !ReferenceEquals(runtime, current)
                        || runtime.ProcessId != pid || runtime.ResumeOperationGeneration != generation || !runtime.ScriptRunning) return;
                    cancelled = cancelled || !running || ResumeWorkerCancelled(runtime, pid, generation);
                    runtime.ScriptRunning = false;
                    if (cancelled)
                    {
                        runtime.RecoveryOwned = false;
                        SetStage(runtime, VanillaReconnectStage.Stopped, "Autobattle verification cancelled");
                    }
                    else if (error != null)
                    {
                        runtime.RecoveryOwned = false;
                        runtime.ResumeVerificationFailed = true;
                        runtime.ResumeFailureDetail = "Autobattle verification failed: " + error;
                        SetStage(runtime, VanillaReconnectStage.Error, runtime.ResumeFailureDetail);
                        Log(account.Label + ": " + runtime.ResumeFailureDetail);
                    }
                    else
                    {
                        runtime.ResumeSent = true;
                        runtime.HasBeenOnline = true;
                        ResetRecoverySuccessLocked(runtime);
                        SetStage(runtime, VanillaReconnectStage.Online, "Movement verified; client minimized");
                    }
                }
                RaiseUpdated();
            }).ContinueWith(task =>
            {
                // Consume unexpected infrastructure/UI subscriber failures; do not terminate the application.
                VanillaDebugLog.Write("AUTOBATTLE", "Resume worker failed: " + task.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}

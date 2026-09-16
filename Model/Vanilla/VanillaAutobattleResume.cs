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
        internal const int PostLoginSettleMs = 7000;
        internal const int MaximumClientRestarts = 3;
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
                report("Sending autobattle hotkey attempt " + attempt + "/" + MaximumAttempts);
                send();
                checkCancelled();
                TimeSpan deadline = now() + TimeSpan.FromMilliseconds(ObservationWindowMs);
                report("Autobattle hotkey sent; verifying X/Y movement " + attempt + "/" + MaximumAttempts + " (10s)");
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
            throw new InvalidOperationException("Failed: no verified X/Y movement after 3 autobattle hotkey attempts (10 seconds per attempt).");
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
        private System.Action<string, string, bool> autobattleResumeTestHook;

        internal void SetAutobattleResumeTestHook(System.Action<string, string, bool> hook)
        { lock (gate) autobattleResumeTestHook = hook; }

        private void RequestVerifiedResume(Runtime runtime, string trigger, bool movementRecovery)
        {
            var hook = autobattleResumeTestHook;
            if (hook != null) { hook(runtime.Account.Id, trigger, movementRecovery); return; }
            QueueVerifiedResume(runtime, trigger, movementRecovery);
        }

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
                                && VanillaCharacterRoster.Key(fleetIdentity) != null
                                && VanillaCharacterRoster.Key(fleetIdentity) == VanillaCharacterRoster.Key(owner.ConfirmedCharacter))
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

        private void CompleteAutobattleRecoverySuccessLocked(Runtime runtime)
        {
            if (runtime.AutobattleRestartAttempts > 0 || runtime.MovementRecoveryPending)
                Log(runtime.Account.Label + ": verified X/Y movement; bounded restart budget reset after "
                    + runtime.AutobattleRestartAttempts + "/" + VanillaAutobattleResumeVerifier.MaximumClientRestarts + " restart attempts.");
            runtime.MovementRecoveryPending = false;
            runtime.AutobattleRestartInProgress = false;
            runtime.AutobattleRestartAttempts = 0;
            runtime.AutobattleRecoveryExhausted = false;
            runtime.MovementWatchdog.Reset();
            ResetRecoverySuccessLocked(runtime);
        }

        private bool TryBeginAutobattleRestartAttemptLocked(Runtime runtime, DateTimeOffset now, string reason)
        {
            if (runtime.AutobattleRecoveryExhausted) return false;
            if (runtime.AutobattleRestartAttempts >= VanillaAutobattleResumeVerifier.MaximumClientRestarts)
            {
                FailAutobattleRecoveryPermanentlyLocked(runtime, reason);
                return false;
            }
            runtime.AutobattleRestartAttempts++;
            runtime.AutobattleRestartInProgress = true;
            runtime.MovementRecoveryPending = true;
            runtime.NextRecoveryAt = null;
            Log(runtime.Account.Label + ": client restart attempt " + runtime.AutobattleRestartAttempts + "/"
                + VanillaAutobattleResumeVerifier.MaximumClientRestarts + " starting because " + reason + ".");
            return true;
        }

        private void QueueAutobattleClientRestartLocked(Runtime runtime, DateTimeOffset now, string reason)
        {
            runtime.RecoveryOwned = false;
            runtime.ScriptRunning = false;
            runtime.ResumeSent = false;
            runtime.MovementRecoveryPending = true;
            // The previous hotkey/replacement cycle has ended. A newly consumed restart
            // attempt below owns the next replacement cycle.
            runtime.AutobattleRestartInProgress = false;
            Runtime owner = OtherRecoveryOwner(runtime);
            if (owner != null)
            {
                SetStage(runtime, VanillaReconnectStage.WaitingForClient,
                    "Autobattle recovery queued behind " + owner.Account.Label + "; restart budget not consumed");
                Log(runtime.Account.Label + ": autobattle recovery is queued behind " + owner.Account.Label
                    + "; no restart attempt was consumed while waiting.");
                return;
            }
            if (!TryBeginAutobattleRestartAttemptLocked(runtime, now, reason)) return;
            int pid = runtime.ProcessId.GetValueOrDefault();
            if (pid <= 0)
            {
                runtime.AutobattleRestartInProgress = false;
                ScheduleRecoveryFailureLocked(runtime, now, reason + "; affected PID is no longer available");
                return;
            }
            QueueClientRestart(runtime, now, reason + ". Restart attempt " + runtime.AutobattleRestartAttempts + "/"
                + VanillaAutobattleResumeVerifier.MaximumClientRestarts + ".", false, () => restartEnvironment.GetStartTimeUtc(pid));
        }

        private void FailAutobattleRecoveryPermanentlyLocked(Runtime runtime, string reason)
        {
            string detail = "FAILED permanently after " + VanillaAutobattleResumeVerifier.MaximumClientRestarts
                + " client restart attempts without verified X/Y movement: " + reason;
            runtime.AutobattleRecoveryExhausted = true;
            runtime.AutobattleRestartInProgress = false;
            runtime.MovementRecoveryPending = false;
            runtime.ResumeSent = false;
            runtime.ResumeVerificationFailed = true;
            runtime.ResumeFailureDetail = detail;
            runtime.ScriptRunning = false;
            runtime.RecoveryOwned = false;
            runtime.ClosingForRecovery = false;
            running = false;
            timer?.Change(Timeout.Infinite, Timeout.Infinite);
            Interlocked.Increment(ref resumeVerificationGeneration);
            Interlocked.Increment(ref diagnosticGeneration);
            foreach (Runtime other in runtimes.Values)
            {
                other.ScriptRunning = false;
                other.RecoveryOwned = false;
                other.ClosingForRecovery = false;
                if (ReferenceEquals(other, runtime)) SetStage(other, VanillaReconnectStage.Error, detail);
                else if (other.Account.Enabled) SetStage(other, VanillaReconnectStage.Stopped,
                    "Supervisor stopped after " + runtime.Account.Label + " exhausted its movement recovery budget");
            }
            Log(runtime.Account.Label + ": " + detail + ". Supervisor OFF; no further automatic hotkeys or restarts will be attempted until manually started again.");
            VanillaDebugLog.Write("AUTOBATTLE", runtime.Account.Label + ": " + detail);
        }

        private void QueueVerifiedResume(Runtime runtime)
        {
            QueueVerifiedResume(runtime, null, false);
        }

        private void QueueVerifiedResume(Runtime runtime, string trigger, bool movementRecovery)
        {
            if (runtime.ScriptRunning || runtime.AutobattleRecoveryExhausted || !runtime.ProcessId.HasValue) return;
            if (runtime.ResumeVerificationFailed && !movementRecovery) return;
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
            runtime.ResumeSent = false;
            if (movementRecovery) runtime.MovementRecoveryPending = true;
            string preparation = (string.IsNullOrWhiteSpace(trigger) ? "" : trigger + " ")
                + "Preparing autobattle hotkey verification 1/3";
            SetStage(runtime, VanillaReconnectStage.VerifyingAutobattle, preparation);
            Log(account.Label + ": " + preparation);
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
                        runtime.ResumeVerificationFailed = true;
                        runtime.ResumeFailureDetail = "Autobattle verification failed: " + error;
                        runtime.MovementRecoveryPending = true;
                        Log(account.Label + ": " + runtime.ResumeFailureDetail);
                        runtime.ScriptRunning = false;
                        QueueAutobattleClientRestartLocked(runtime, restartEnvironment.UtcNow, runtime.ResumeFailureDetail);
                    }
                    else
                    {
                        runtime.ResumeSent = true;
                        runtime.ResumeVerificationFailed = false;
                        runtime.ResumeFailureDetail = null;
                        runtime.HasBeenOnline = true;
                        CompleteAutobattleRecoverySuccessLocked(runtime);
                        SetStage(runtime, VanillaReconnectStage.Online, "Movement verified; client minimized; recovery budget reset");
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

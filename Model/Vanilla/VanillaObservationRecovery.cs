using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using _4RTools.Model.Vanilla;
using _4RTools.Utils;

namespace _4RTools.Utils
{
    internal sealed class ProcessObservationAccessFailure
    {
        public int ProcessId { get; set; }
        public int NativeErrorCode { get; set; }
        public string Operation { get; set; }
        public string Detail { get; set; }
        public DateTimeOffset ObservedAt { get; set; }
    }

    /// <summary>
    /// Process-local record of a normal read-only OpenProcess denial. This is not a retry or
    /// alternate-access mechanism. It lets the normal reconnect supervisor replace an already
    /// protected client with a fresh client process while 4RTools is running.
    /// </summary>
    internal static class ProcessObservationAccessRegistry
    {
        private const string ReadOnlyOpenOperation = "OpenProcess(read/limited-query, 0x1010)";
        private static readonly object Gate = new object();
        private static readonly Dictionary<int, ProcessObservationAccessFailure> InitialOpenFailures =
            new Dictionary<int, ProcessObservationAccessFailure>();

        internal static bool IsRestartableInitialOpenFailure(string operation, int nativeErrorCode)
        {
            return nativeErrorCode == 5 && string.Equals(operation, ReadOnlyOpenOperation, StringComparison.Ordinal);
        }

        internal static void RecordInitialOpenFailure(int processId, string operation, int nativeErrorCode, string detail)
        {
            if (processId <= 0 || !IsRestartableInitialOpenFailure(operation, nativeErrorCode)) return;
            lock (Gate)
            {
                InitialOpenFailures[processId] = new ProcessObservationAccessFailure
                {
                    ProcessId = processId,
                    NativeErrorCode = nativeErrorCode,
                    Operation = operation,
                    Detail = detail,
                    ObservedAt = DateTimeOffset.UtcNow
                };
            }
        }

        internal static void RecordOpenSuccess(int processId)
        {
            if (processId <= 0) return;
            lock (Gate) InitialOpenFailures.Remove(processId);
        }

        internal static bool RequiresFreshClientProcess(int processId, out string detail)
        {
            lock (Gate)
            {
                ProcessObservationAccessFailure failure;
                if (!InitialOpenFailures.TryGetValue(processId, out failure))
                {
                    detail = null;
                    return false;
                }
                detail = failure.Detail;
                return true;
            }
        }

        internal static int[] DeniedProcessIds()
        {
            lock (Gate) return InitialOpenFailures.Keys.ToArray();
        }

        internal static void Forget(int processId)
        {
            lock (Gate) InitialOpenFailures.Remove(processId);
        }
    }
}

namespace _4RTools.Model.Vanilla
{
    internal static class VanillaObservationRecoveryPolicy
    {
        internal static bool ShouldRecycle(bool supervisorRunning, bool autoRecover, bool scriptRunning, bool readOnlyOpenDenied)
        {
            return supervisorRunning && autoRecover && !scriptRunning && readOnlyOpenDenied;
        }

        internal static bool HasUnambiguousStartupAssignment(int enabledAccountCount, int liveProcessCount, int deniedProcessCount)
        {
            if (enabledAccountCount <= 0 || liveProcessCount <= 0 || deniedProcessCount <= 0) return false;
            if (liveProcessCount > 2 || deniedProcessCount > liveProcessCount || liveProcessCount > enabledAccountCount) return false;
            if (enabledAccountCount == 1) return liveProcessCount == 1;
            return liveProcessCount == enabledAccountCount;
        }

        internal static int TemporaryClientLimit(int configuredLimit, int enabledAccountCount, int liveProcessCount)
        {
            int required = Math.Max(1, Math.Min(2, Math.Min(enabledAccountCount, liveProcessCount)));
            return Math.Max(Math.Max(1, Math.Min(2, configuredLimit)), required);
        }
    }

    public sealed partial class VanillaReconnectSupervisor
    {
        /// <summary>
        /// Recycles at most one assigned client whose normal read-only OpenProcess was denied.
        /// Existing healthy clients remain running. Recovery ownership keeps the replacement
        /// serialized until the affected client reaches gameplay or fails/backoffs.
        /// </summary>
        internal void RecoverObservationDeniedClientIfNeeded()
        {
            lock (gate)
            {
                if (!VanillaObservationRecoveryPolicy.ShouldRecycle(running, settings.AutoRecover, false, true)) return;
                DateTimeOffset now = DateTimeOffset.UtcNow;
                var desired = settings.Accounts.Where(a => a.Enabled).Take(settings.MaxClients).ToList();

                foreach (VanillaReconnectAccount account in desired)
                {
                    Runtime runtime;
                    if (!runtimes.TryGetValue(account.Id, out runtime) || !runtime.ProcessId.HasValue) continue;
                    int pid = runtime.ProcessId.Value;
                    string accessDetail;
                    bool denied = ProcessObservationAccessRegistry.RequiresFreshClientProcess(pid, out accessDetail);
                    if (!VanillaObservationRecoveryPolicy.ShouldRecycle(running, settings.AutoRecover, runtime.ScriptRunning, denied)) continue;

                    Runtime owner = OtherRecoveryOwner(runtime);
                    if (owner != null)
                    {
                        SetStage(runtime, VanillaReconnectStage.WaitingForClient,
                            "Queued: read-only observation needs a fresh client; waiting for " + owner.Account.Label + " recovery to finish");
                        return;
                    }

                    Process process = null;
                    try
                    {
                        process = Process.GetProcessById(pid);
                        process.Refresh();
                        if (process.HasExited)
                        {
                            ProcessObservationAccessRegistry.Forget(pid);
                            runtime.ProcessId = null;
                            runtime.RecoveryOwned = true;
                            runtime.NextRecoveryAt = now;
                            SetStage(runtime, VanillaReconnectStage.WaitingForClient,
                                "Previous client exited; fresh read-only-observable relaunch queued");
                            return;
                        }

                        bool failedFreshRecovery = runtime.RecoveryOwned;
                        string reason = failedFreshRecovery
                            ? "Fresh recovery client still denied the normal read-only observation handle"
                            : "Existing client denied the normal read-only observation handle after this 4RTools start";

                        CloseForRecovery(runtime, process, now, reason, failedFreshRecovery);

                        if (!runtime.ProcessId.HasValue)
                            ProcessObservationAccessRegistry.Forget(pid);

                        if (!failedFreshRecovery)
                        {
                            // Hold the global recovery lease from the recycle through successful gameplay,
                            // so a second denied client stays open and untouched until this one is finished.
                            runtime.RecoveryOwned = true;
                            Log(runtime.Account.Label + ": read-only observation could not be opened for PID " + pid
                                + "; recycling only this client through the normal sequential launcher path. Verified build offsets are unchanged."
                                + (string.IsNullOrWhiteSpace(accessDetail) ? string.Empty : " Original access error: " + accessDetail));
                        }
                        return;
                    }
                    catch (ArgumentException)
                    {
                        ProcessObservationAccessRegistry.Forget(pid);
                        runtime.ProcessId = null;
                        runtime.RecoveryOwned = true;
                        runtime.NextRecoveryAt = now;
                        SetStage(runtime, VanillaReconnectStage.WaitingForClient,
                            "Client exited while preparing fresh observation; relaunch queued");
                        return;
                    }
                    catch (Exception ex)
                    {
                        SetStage(runtime, VanillaReconnectStage.Backoff,
                            "Could not recycle client for fresh read-only observation: " + ex.Message);
                        Log(runtime.Account.Label + ": could not recycle PID " + pid + " for fresh read-only observation: " + ex.Message);
                        return;
                    }
                    finally
                    {
                        if (process != null) process.Dispose();
                    }
                }
            }
        }
    }
}

namespace _4RTools.Forms
{
    public partial class Container
    {
        private bool observationRecoveryHooked;
        private bool startupObservationRepairStarting;
        private bool startupObservationRepairOwnsSupervisor;
        private string startupObservationRepairLastBlockedKey;
        private VanillaReconnectSettings startupObservationRepairOriginalSettings;
        private readonly HashSet<string> startupObservationRepairAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void HookObservationRecovery()
        {
            if (observationRecoveryHooked || integratedReconnectSupervisor == null) return;
            observationRecoveryHooked = true;
            integratedReconnectSupervisor.Updated += RecoverDeniedObservationAccess;
            vanillaTimer.Tick += ObservationRecoveryTick;
            TryStartOneShotObservationRepair();
            RecoverDeniedObservationAccess();
        }

        private void ObservationRecoveryTick(object sender, EventArgs e)
        {
            if (IsDisposed || smokeTest) return;
            TryStartOneShotObservationRepair();
            RecoverDeniedObservationAccess();
        }

        private static int[] LiveVanillaProcessIdsForRepair()
        {
            Process[] processes = Process.GetProcessesByName("Vanilla MMO");
            try
            {
                return processes.Where(process =>
                {
                    try { return !process.HasExited; }
                    catch { return false; }
                }).OrderBy(process =>
                {
                    try { return process.StartTime; }
                    catch { return DateTime.MaxValue; }
                }).Select(process => process.Id).ToArray();
            }
            finally { foreach (Process process in processes) process.Dispose(); }
        }

        private void TryStartOneShotObservationRepair()
        {
            if (startupObservationRepairStarting || integratedReconnectSupervisor == null) return;
            if (startupObservationRepairOwnsSupervisor)
            {
                if (!integratedReconnectSupervisor.IsRunning)
                    RestoreOneShotObservationSettings("One-time fresh-observation repair was stopped before completion.");
                return;
            }
            if (integratedReconnectSupervisor.IsRunning) return;

            int[] livePids = LiveVanillaProcessIdsForRepair();
            var liveSet = new HashSet<int>(livePids);
            foreach (int stale in ProcessObservationAccessRegistry.DeniedProcessIds().Where(pid => !liveSet.Contains(pid)).ToArray())
                ProcessObservationAccessRegistry.Forget(stale);
            int[] deniedPids = ProcessObservationAccessRegistry.DeniedProcessIds().Where(liveSet.Contains).OrderBy(pid => pid).ToArray();
            if (deniedPids.Length == 0)
            {
                startupObservationRepairLastBlockedKey = null;
                return;
            }

            VanillaReconnectSettings reconnect = integratedReconnectSupervisor.Settings;
            var enabled = reconnect.Accounts.Where(a => a.Enabled).ToArray();
            string decisionKey = string.Join(",", deniedPids) + "|live=" + string.Join(",", livePids)
                + "|enabled=" + string.Join(",", enabled.Select(a => a.Id)) + "|auto=" + reconnect.AutoRecover
                + "|launcher=" + reconnect.LaunchExecutable;

            if (!VanillaObservationRecoveryPolicy.HasUnambiguousStartupAssignment(enabled.Length, livePids.Length, deniedPids.Length))
            {
                LogObservationRepairBlockedOnce(decisionKey,
                    "Read-only observation is unavailable for pre-existing Vanilla PID(s) " + string.Join(", ", deniedPids)
                    + ". Verified offsets are still loaded. Automatic one-shot recycle was not started because the live PID-to-account assignment is ambiguous."
                    + " Enabled accounts=" + enabled.Length + ", live clients=" + livePids.Length + ".");
                return;
            }

            int temporaryLimit = VanillaObservationRecoveryPolicy.TemporaryClientLimit(reconnect.MaxClients, enabled.Length, livePids.Length);
            bool credentialsAvailable = enabled.Take(temporaryLimit)
                .All(a => !string.IsNullOrWhiteSpace(a.UserName) && !string.IsNullOrWhiteSpace(a.ProtectedPassword));
            if (!reconnect.AutoRecover || !credentialsAvailable || string.IsNullOrWhiteSpace(reconnect.LaunchExecutable)
                || !File.Exists(reconnect.LaunchExecutable))
            {
                LogObservationRepairBlockedOnce(decisionKey,
                    "Read-only observation is unavailable for pre-existing Vanilla PID(s) " + string.Join(", ", deniedPids)
                    + ". Verified offsets are still loaded. Automatic one-shot client recycle was not started because reconnect recovery is not fully configured/enabled.");
                return;
            }

            startupObservationRepairStarting = true;
            try
            {
                startupObservationRepairLastBlockedKey = null;
                startupObservationRepairOriginalSettings = reconnect.Clone();
                if (temporaryLimit != reconnect.MaxClients)
                {
                    var repairSettings = reconnect.Clone();
                    repairSettings.MaxClients = temporaryLimit;
                    integratedReconnectSupervisor.Apply(repairSettings, false);
                    integratedReconnectSupervisor.RecordSupervisorLog(
                        "One-time fresh-observation repair temporarily widened the active client limit from " + reconnect.MaxClients
                        + " to " + temporaryLimit + " so every currently running configured client can be identified. Saved settings were not changed.");
                }

                integratedReconnectSupervisor.RecordSupervisorLog(
                    "Pre-existing Vanilla PID(s) " + string.Join(", ", deniedPids)
                    + " denied the normal read-only observation handle after 4RTools started. Starting one-time sequential client recycle; no alternate memory access will be attempted.");
                integratedReconnectSupervisor.Start();
                startupObservationRepairOwnsSupervisor = true;
                startupObservationRepairAccounts.Clear();
                foreach (VanillaReconnectStatus status in integratedReconnectSupervisor.Statuses())
                    if (status.ProcessId.HasValue && deniedPids.Contains(status.ProcessId.Value))
                        startupObservationRepairAccounts.Add(status.AccountId);

                if (startupObservationRepairAccounts.Count == 0)
                {
                    integratedReconnectSupervisor.RecordSupervisorLog(
                        "One-time fresh-observation repair could not match the denied PID to a managed account. Returning supervisor/settings to their previous state.");
                    RestoreOneShotObservationSettings(null);
                }
            }
            finally
            {
                startupObservationRepairStarting = false;
            }
        }

        private void LogObservationRepairBlockedOnce(string key, string message)
        {
            if (string.Equals(startupObservationRepairLastBlockedKey, key, StringComparison.Ordinal)) return;
            startupObservationRepairLastBlockedKey = key;
            integratedReconnectSupervisor.RecordSupervisorLog(message);
        }

        private void RecoverDeniedObservationAccess()
        {
            if (startupObservationRepairStarting || IsDisposed || integratedReconnectSupervisor == null || !integratedReconnectSupervisor.IsRunning) return;
            try
            {
                integratedReconnectSupervisor.RecoverObservationDeniedClientIfNeeded();
                CompleteOneShotObservationRepairIfReady();
            }
            catch (Exception ex)
            {
                integratedReconnectSupervisor.RecordSupervisorLog("Fresh-observation recovery check failed: " + ex.Message);
            }
        }

        private void CompleteOneShotObservationRepairIfReady()
        {
            if (!startupObservationRepairOwnsSupervisor || startupObservationRepairAccounts.Count == 0) return;
            var statuses = integratedReconnectSupervisor.Statuses()
                .Where(s => startupObservationRepairAccounts.Contains(s.AccountId)).ToArray();
            if (statuses.Length != startupObservationRepairAccounts.Count) return;
            foreach (VanillaReconnectStatus status in statuses)
            {
                string detail;
                if (!status.ProcessId.HasValue || status.Stage != VanillaReconnectStage.Online
                    || ProcessObservationAccessRegistry.RequiresFreshClientProcess(status.ProcessId.Value, out detail)) return;
            }

            integratedReconnectSupervisor.RecordSupervisorLog(
                "One-time fresh-observation repair completed. Recovered client(s) are Online/minimized; returning supervisor/settings to their previous state.");
            RestoreOneShotObservationSettings(null);
        }

        private void RestoreOneShotObservationSettings(string logMessage)
        {
            bool wasOwned = startupObservationRepairOwnsSupervisor;
            VanillaReconnectSettings original = startupObservationRepairOriginalSettings;
            startupObservationRepairOwnsSupervisor = false;
            startupObservationRepairAccounts.Clear();
            startupObservationRepairOriginalSettings = null;
            if (!string.IsNullOrWhiteSpace(logMessage) && integratedReconnectSupervisor != null)
                integratedReconnectSupervisor.RecordSupervisorLog(logMessage);
            if (integratedReconnectSupervisor == null) return;
            if (wasOwned && integratedReconnectSupervisor.IsRunning) integratedReconnectSupervisor.Stop();
            if (original != null) integratedReconnectSupervisor.Apply(original, false);
        }
    }
}

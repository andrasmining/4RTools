using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private readonly HashSet<string> startupObservationRepairAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void HookObservationRecovery()
        {
            if (observationRecoveryHooked || integratedReconnectSupervisor == null) return;
            observationRecoveryHooked = true;
            integratedReconnectSupervisor.Updated += RecoverDeniedObservationAccess;
            TryStartOneShotObservationRepair();
            RecoverDeniedObservationAccess();
        }

        private void TryStartOneShotObservationRepair()
        {
            if (startupObservationRepairStarting || integratedReconnectSupervisor == null || integratedReconnectSupervisor.IsRunning) return;
            int[] deniedPids = ProcessObservationAccessRegistry.DeniedProcessIds();
            if (deniedPids.Length == 0) return;

            VanillaReconnectSettings reconnect = integratedReconnectSupervisor.Settings;
            var enabled = reconnect.Accounts.Where(a => a.Enabled).Take(reconnect.MaxClients).ToArray();
            bool credentialsAvailable = enabled.Any(a => !string.IsNullOrWhiteSpace(a.UserName) && !string.IsNullOrWhiteSpace(a.ProtectedPassword));
            if (!reconnect.AutoRecover || !credentialsAvailable || string.IsNullOrWhiteSpace(reconnect.LaunchExecutable)
                || !File.Exists(reconnect.LaunchExecutable))
            {
                integratedReconnectSupervisor.RecordSupervisorLog(
                    "Read-only observation is unavailable for pre-existing Vanilla PID(s) " + string.Join(", ", deniedPids)
                    + ". Verified offsets are still loaded. Automatic one-shot client recycle was not started because reconnect recovery is not fully configured/enabled.");
                return;
            }

            startupObservationRepairStarting = true;
            try
            {
                integratedReconnectSupervisor.RecordSupervisorLog(
                    "Pre-existing Vanilla PID(s) " + string.Join(", ", deniedPids)
                    + " denied the normal read-only observation handle after 4RTools started. Starting one-time sequential client recycle; no alternate memory access will be attempted.");
                integratedReconnectSupervisor.Start();
                startupObservationRepairOwnsSupervisor = true;
                startupObservationRepairAccounts.Clear();
                foreach (VanillaReconnectStatus status in integratedReconnectSupervisor.Statuses())
                    if (status.ProcessId.HasValue && deniedPids.Contains(status.ProcessId.Value))
                        startupObservationRepairAccounts.Add(status.AccountId);
            }
            finally
            {
                startupObservationRepairStarting = false;
            }
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

            startupObservationRepairOwnsSupervisor = false;
            startupObservationRepairAccounts.Clear();
            integratedReconnectSupervisor.RecordSupervisorLog(
                "One-time fresh-observation repair completed. Recovered client(s) are Online/minimized; returning supervisor to its previous stopped state.");
            integratedReconnectSupervisor.Stop();
        }
    }
}

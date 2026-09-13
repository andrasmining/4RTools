using System;
using System.Linq;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Closes the small race between the launcher worker reporting a new Vanilla process and
    /// the next supervisor poll. Without this guard the launcher worker could return, clear
    /// ScriptRunning, and leave ProcessId null until the next poll; that poll could then start
    /// the same account again. The launcher already reports the exact PID it created, so bind
    /// that PID immediately while the first account still owns the global recovery lease.
    /// </summary>
    public sealed partial class VanillaReconnectSupervisor
    {
        private bool strictSequentialLaunchGuardEnabled;

        internal void EnableStrictSequentialLaunchGuard()
        {
            lock (gate)
            {
                if (strictSequentialLaunchGuardEnabled) return;
                strictSequentialLaunchGuardEnabled = true;
            }
            Logged += CaptureLauncherReportedVanillaPid;
        }

        private void CaptureLauncherReportedVanillaPid(string line)
        {
            int pid;
            string label = null;
            bool bound = false;

            lock (gate)
            {
                if (disposed || !running) return;
                bool patcherMode = VanillaPatcherLauncher.IsPatcher(settings.LaunchExecutable);
                if (!TryParseLaunchedVanillaPid(line, patcherMode, out pid)) return;
                if (runtimes.Values.Any(runtime => runtime.ProcessId == pid)) return;

                Runtime[] pending = runtimes.Values
                    .Where(runtime => IsPendingSequentialLaunch(runtime.RecoveryOwned, runtime.ScriptRunning,
                        runtime.ProcessId.HasValue, runtime.Stage))
                    .ToArray();
                if (pending.Length != 1) return;

                Runtime owner = pending[0];
                label = owner.Account.Label;
                Bind(owner, pid, true, "Launcher reported new Vanilla client");
                bound = true;
            }

            if (!bound) return;
            Log(label + ": sequential launch guard bound Vanilla PID " + pid
                + " immediately; this client keeps the recovery lease until gameplay/resume and minimization complete.");
            RaiseUpdated();
        }

        internal static bool IsPendingSequentialLaunch(bool recoveryOwned, bool scriptRunning, bool hasProcessId,
            VanillaReconnectStage stage)
        {
            return recoveryOwned && scriptRunning && !hasProcessId
                && (stage == VanillaReconnectStage.Launching || stage == VanillaReconnectStage.WaitingForWindow);
        }

        internal static bool TryParseLaunchedVanillaPid(string line, bool patcherMode, out int pid)
        {
            pid = 0;
            if (string.IsNullOrWhiteSpace(line)) return false;

            if (patcherMode)
            {
                const string marker = "Patcher started Vanilla MMO (PID ";
                int start = line.IndexOf(marker, StringComparison.Ordinal);
                if (start < 0) return false;
                start += marker.Length;
                int end = line.IndexOf(')', start);
                if (end <= start) return false;
                return int.TryParse(line.Substring(start, end - start), out pid) && pid > 0;
            }

            // Direct executable mode has no patcher child: the started process itself is Vanilla.
            const string directMarker = "Launcher start requested:";
            int direct = line.IndexOf(directMarker, StringComparison.Ordinal);
            if (direct < 0) return false;
            const string pidMarker = "startedPID=";
            int pidStart = line.IndexOf(pidMarker, direct, StringComparison.Ordinal);
            if (pidStart < 0) return false;
            pidStart += pidMarker.Length;
            int pidEnd = line.IndexOf(',', pidStart);
            if (pidEnd < 0) pidEnd = line.Length;
            return int.TryParse(line.Substring(pidStart, pidEnd - pidStart).Trim(), out pid) && pid > 0;
        }
    }

    internal sealed partial class VanillaReconnectForm
    {
        private bool strictSequentialLaunchGuardHooked;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (strictSequentialLaunchGuardHooked) return;
            strictSequentialLaunchGuardHooked = true;
            supervisor.EnableStrictSequentialLaunchGuard();
            InstallHardenedSupervisorButtons();
            InstallHardenedAutoStartBridge();
            InstallFullDebugLogUi();
        }
    }
}

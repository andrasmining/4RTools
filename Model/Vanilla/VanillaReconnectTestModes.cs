using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    public sealed partial class VanillaReconnectSupervisor
    {
        private const int SwMinimize = 6;

        public bool MinimizeAssignedClient(string accountId)
        {
            return MinimizeAssignedClientCore(accountId, true);
        }

        public bool KeepAssignedClientMinimized(string accountId)
        {
            return MinimizeAssignedClientCore(accountId, false);
        }

        public void KeepOnlineClientsMinimized()
        {
            if (!IsRunning) return;
            foreach (VanillaReconnectStatus status in Statuses().Where(s => s.ProcessId.HasValue && ShouldKeepClientMinimized(s.Stage, s.VisualState)))
                KeepAssignedClientMinimized(status.AccountId);
        }

        internal static bool ShouldKeepClientMinimized(VanillaReconnectStage stage, VanillaVisualState visual)
        {
            return stage == VanillaReconnectStage.Online || visual == VanillaVisualState.Gameplay;
        }

        internal void RecordSupervisorLog(string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) Log(text);
        }

        private bool MinimizeAssignedClientCore(string accountId, bool testLog)
        {
            int pid;
            string label;
            lock (gate)
            {
                Runtime runtime;
                if (!runtimes.TryGetValue(accountId, out runtime)) throw new ArgumentException("Unknown account.");
                if (!runtime.ProcessId.HasValue) return false;
                pid = runtime.ProcessId.Value;
                label = runtime.Account.Label;
            }

            try
            {
                using (var process = Process.GetProcessById(pid))
                {
                    process.Refresh();
                    if (process.HasExited || process.MainWindowHandle == IntPtr.Zero) return false;
                    IntPtr window = process.MainWindowHandle;
                    if (IsIconic(window)) return true;
                    ShowWindow(window, SwMinimize);
                    if (!IsIconic(window))
                    {
                        if (testLog) Log("TEST " + label + ": could not confirm that Vanilla client PID " + pid + " was minimized.");
                        else Log(label + ": could not confirm that supervised Vanilla client PID " + pid + " was minimized.");
                        return false;
                    }
                    Log((testLog ? "TEST " : string.Empty) + label + ": Vanilla client PID " + pid
                        + (testLog ? " minimized and left running." : " minimized and left running by supervisor policy."));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log((testLog ? "TEST " : string.Empty) + label + ": could not minimize PID " + pid + ": " + ex.Message);
                return false;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
    }

    internal sealed partial class VanillaReconnectForm
    {
        private bool supervisedMinimizeHooked;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ConfigureScopedTestUi();
            HookSupervisedMinimizePolicy();
        }

        private void HookSupervisedMinimizePolicy()
        {
            if (supervisedMinimizeHooked) return;
            supervisedMinimizeHooked = true;
            supervisor.Updated += KeepSupervisedClientsMinimized;
            supervisor.Logged += KeepSupervisedClientsMinimizedBeforeNextRecovery;
            Disposed += (s, e) =>
            {
                try { supervisor.Updated -= KeepSupervisedClientsMinimized; } catch { }
                try { supervisor.Logged -= KeepSupervisedClientsMinimizedBeforeNextRecovery; } catch { }
            };
            KeepSupervisedClientsMinimized();
        }

        private void KeepSupervisedClientsMinimizedBeforeNextRecovery(string line)
        {
            if (string.IsNullOrEmpty(line)
                || line.IndexOf(": recovery succeeded; exponential retry state reset.", StringComparison.Ordinal) < 0) return;
            // ResetRecoverySuccessLocked logs synchronously before it releases the global recovery lease.
            // Minimize the just-recovered gameplay client here so the next queued client cannot begin
            // until the successful client is already minimized and left running.
            KeepSupervisedClientsMinimized();
        }

        private void KeepSupervisedClientsMinimized()
        {
            if (IsDisposed || !supervisor.IsRunning) return;
            try { supervisor.KeepOnlineClientsMinimized(); }
            catch (Exception ex) { supervisor.RecordSupervisorLog("Supervisor minimize policy failed: " + ex.Message); }
        }

        private void ConfigureScopedTestUi()
        {
            RemoveButtonByText(this, "DETECT RUNNING CLIENTS");
            RemoveButtonByText(this, "TEST STARTUP (SEQUENTIAL)");

            GroupBox box = FindGroupBox(this, "Step-by-step selected-account diagnostic");
            if (box == null) return;
            box.Text = "Startup tests and selected-client diagnostics";
            box.Height = 116;
            box.Controls.Clear();

            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                WrapContents = true,
                Padding = new Padding(0, 4, 0, 0)
            };
            AddButton(row, "TEST SELECTED CLIENT", TestSelectedClientOnly);
            AddButton(row, "TEST ALL CLIENTS", TestAllClientsKeepOpen);
            AddButton(row, "1 GAME START", () => RunSelectedStep(VanillaReconnectTestStep.LauncherGameStart));
            AddButton(row, "2 PROXY", () => RunSelectedStep(VanillaReconnectTestStep.ProxySelection));
            AddButton(row, "3 FILL USER/PW", () => RunSelectedStep(VanillaReconnectTestStep.FillCredentials));
            AddButton(row, "4 SUBMIT LOGIN", () => RunSelectedStep(VanillaReconnectTestStep.SubmitCredentials));
            AddButton(row, "5 SERVER", () => RunSelectedStep(VanillaReconnectTestStep.SelectGameServer));
            AddButton(row, "6 CHARACTER", () => RunSelectedStep(VanillaReconnectTestStep.SelectCharacter));
            AddButton(row, "7 RESUME HOTKEY", () => RunSelectedStep(VanillaReconnectTestStep.ResumeHotkey));
            AddButton(row, "STOP TEST", StopCurrentTest);
            box.Controls.Add(row);

            TipByText(this, "START SUPERVISOR",
                "Start continuous recovery monitoring. Missing clients are recovered one at a time. A healthy client is kept running and minimized; while another client recovers, healthy clients remain minimized and untouched.");
            TipByText(this, "TEST SELECTED CLIENT",
                "Cold-start test for the selected account only. Other assigned Vanilla clients stay open and are never tested. The selected client is left running and minimized after success.");
            TipByText(this, "TEST ALL CLIENTS",
                "Cold-start all enabled accounts sequentially. Client 1 is completed, left running and minimized; then client 2 is started, completed, left running and minimized. The supervisor is not started during this test.");
            TipByText(this, "1 GAME START", "Selected account only.");
            TipByText(this, "2 PROXY", "Selected account only.");
            TipByText(this, "3 FILL USER/PW", "Selected account only.");
            TipByText(this, "4 SUBMIT LOGIN", "Selected account only.");
            TipByText(this, "5 SERVER", "Selected account only.");
            TipByText(this, "6 CHARACTER", "Selected account only.");
            TipByText(this, "7 RESUME HOTKEY", "Selected account only. On success the selected Vanilla client is minimized and left running.");
        }

        private static GroupBox FindGroupBox(Control root, string text)
        {
            foreach (Control child in root.Controls)
            {
                var group = child as GroupBox;
                if (group != null && string.Equals(group.Text, text, StringComparison.Ordinal)) return group;
                if (child.HasChildren)
                {
                    GroupBox nested = FindGroupBox(child, text);
                    if (nested != null) return nested;
                }
            }
            return null;
        }

        private static void RemoveButtonByText(Control root, string text)
        {
            foreach (Control child in root.Controls.Cast<Control>().ToArray())
            {
                if (child is Button && string.Equals(child.Text, text, StringComparison.Ordinal))
                {
                    root.Controls.Remove(child);
                    child.Dispose();
                    continue;
                }
                if (child.HasChildren) RemoveButtonByText(child, text);
            }
        }

        private void TestSelectedClientOnly()
        {
            VanillaReconnectAccount selected = SelectedAccount();
            if (selected == null)
            {
                MessageBox.Show(this, "Select one account/client row first.", "Selected-client test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                PrepareIsolatedTest();
                ValidateTestAccount(selected);

                VanillaReconnectStatus selectedStatus = supervisor.Statuses()
                    .FirstOrDefault(s => string.Equals(s.AccountId, selected.Id, StringComparison.OrdinalIgnoreCase));
                if (selectedStatus != null && selectedStatus.ProcessId.HasValue && IsRunningProcess(selectedStatus.ProcessId.Value))
                {
                    MessageBox.Show(this,
                        "Close only the selected client's Vanilla window first. Other assigned clients may stay open. TEST SELECTED CLIENT will not test or relaunch them.",
                        "Selected-client cold-start test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int[] livePids = LiveVanillaPids();
                int[] claimedByOthers = supervisor.Statuses()
                    .Where(s => !string.Equals(s.AccountId, selected.Id, StringComparison.OrdinalIgnoreCase)
                        && s.ProcessId.HasValue && IsRunningProcess(s.ProcessId.Value))
                    .Select(s => s.ProcessId.Value)
                    .Distinct()
                    .ToArray();
                int[] unassigned = livePids.Except(claimedByOthers).ToArray();
                if (unassigned.Length > 0)
                    throw new InvalidOperationException("The selected client cannot be identified with certainty because an unassigned Vanilla client is still running (PID "
                        + string.Join(", ", unassigned) + "). Close only the selected client's old window first; other already-assigned clients may stay open.");
                if (livePids.Length >= settings.MaxClients)
                    throw new InvalidOperationException("The configured client limit (" + settings.MaxClients + ") is already reached, so the selected cold-start test cannot launch another Vanilla client.");

                int generation = BeginTest("SELECTED CLIENT TEST: " + selected.Label + " only");
                supervisor.RecordTestLog("Selected-client cold-start test started for '" + selected.Label + "' only. Other assigned clients are left untouched.");
                RunClientSequenceAsync(generation, new[] { selected }, false);
            }
            catch (Exception ex) { FailTestImmediately("Selected-client test", ex); }
        }

        private void TestAllClientsKeepOpen()
        {
            try
            {
                PrepareIsolatedTest();
                VanillaReconnectAccount[] configured = settings.Accounts.Where(a => a.Enabled).Take(settings.MaxClients).ToArray();
                if (configured.Length == 0) throw new InvalidOperationException("Enable at least one account first.");
                foreach (VanillaReconnectAccount account in configured) ValidateTestAccount(account);

                int[] livePids = LiveVanillaPids();
                if (livePids.Length > 0)
                {
                    MessageBox.Show(this,
                        "Close the managed Vanilla clients before TEST ALL CLIENTS. The test will start the first client, finish it, leave it running and minimize it, then start the second client and do the same.",
                        "All-clients cold-start test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int generation = BeginTest("ALL CLIENTS TEST: 0/" + configured.Length + " completed");
                supervisor.RecordTestLog("All-clients cold-start test started for " + configured.Length
                    + " client(s). Each successful client will remain running and be minimized before the next starts.");
                RunClientSequenceAsync(generation, configured, true);
            }
            catch (Exception ex) { FailTestImmediately("All-clients test", ex); }
        }

        private void PrepareIsolatedTest()
        {
            if (testRunning) throw new InvalidOperationException("A recovery test is already running.");
            ReadTop();
            supervisor.Apply(settings, true);
            // Full/step diagnostics must never enable continuous recovery. Otherwise the watchdog
            // can act on a client after a test has already succeeded.
            if (supervisor.IsRunning) supervisor.Stop();
        }

        private static void ValidateTestAccount(VanillaReconnectAccount account)
        {
            if (string.IsNullOrWhiteSpace(account.UserName) || string.IsNullOrWhiteSpace(account.ProtectedPassword))
                throw new InvalidOperationException("Account '" + account.Label + "' needs username and password before an end-to-end test.");
        }

        private void RunClientSequenceAsync(int generation, VanillaReconnectAccount[] accountsToTest, bool allClients)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                for (int index = 0; index < accountsToTest.Length; index++)
                {
                    if (generation != testGeneration) return;
                    VanillaReconnectAccount account = accountsToTest[index];
                    UpdateTestState(generation, (allClients ? "ALL CLIENTS TEST: " + (index + 1) + "/" + accountsToTest.Length + " — " : "SELECTED CLIENT TEST: ")
                        + account.Label);
                    supervisor.RecordTestLog("Starting full diagnostic chain for '" + account.Label + "'.");

                    string failure;
                    if (!RunFullDiagnosticChainBlocking(generation, account.Id, out failure))
                    {
                        if (failure != null && failure.StartsWith("STOPPED:", StringComparison.Ordinal))
                            CompleteTestStopped(generation, failure.Substring(8).Trim());
                        else CompleteTest(generation, false, failure ?? ("Test failed for " + account.Label + "."));
                        return;
                    }

                    if (!supervisor.MinimizeAssignedClient(account.Id))
                    {
                        CompleteTest(generation, false, account.Label + " completed its login sequence, but 4RTools could not minimize its Vanilla window. The client was left running; check the reconnect log.");
                        return;
                    }
                    supervisor.RecordTestLog("Completed '" + account.Label + "'; client is still running and minimized. Continuing to the next client only after this completion.");
                }

                string message = allClients
                    ? "All configured clients passed. Each client was completed sequentially, left running, and minimized; no successful client was closed before the next one started."
                    : "Selected client passed. It was left running and minimized; no other configured client was tested.";
                CompleteTest(generation, true, message);
            });
        }

        private bool RunFullDiagnosticChainBlocking(int generation, string accountId, out string failure)
        {
            for (int value = (int)VanillaReconnectTestStep.LauncherGameStart; value <= (int)VanillaReconnectTestStep.ResumeHotkey; value++)
            {
                if (generation != testGeneration)
                {
                    failure = "STOPPED: test cancelled.";
                    return false;
                }

                VanillaReconnectTestStep step = (VanillaReconnectTestStep)value;
                try
                {
                    if (step != VanillaReconnectTestStep.LauncherGameStart) supervisor.AssignSingleDiagnosticClient(accountId);
                    supervisor.RunDiagnosticStep(accountId, step);
                }
                catch (Exception ex)
                {
                    failure = "Step " + step + " could not start: " + ex.Message;
                    return false;
                }

                if (!WaitForDiagnosticCompletion(accountId, step,
                    step == VanillaReconnectTestStep.LauncherGameStart ? 150000 : 45000, out failure)) return false;

                if (step == VanillaReconnectTestStep.ResumeHotkey) continue;
                int delay = step == VanillaReconnectTestStep.LauncherGameStart
                    ? Math.Max(1000, settings.GepardWaitMs)
                    : step == VanillaReconnectTestStep.SelectCharacter
                        ? Math.Max(1000, settings.GameLoadMs)
                        : Math.Max(500, settings.StageDelayMs);
                int waited = 0;
                while (waited < delay)
                {
                    if (generation != testGeneration)
                    {
                        failure = "STOPPED: test cancelled.";
                        return false;
                    }
                    int slice = Math.Min(250, delay - waited);
                    Thread.Sleep(slice);
                    waited += slice;
                }
            }
            failure = null;
            return true;
        }

        private void RunSelectedStep(VanillaReconnectTestStep step)
        {
            VanillaReconnectAccount selected = SelectedAccount();
            if (selected == null)
            {
                MessageBox.Show(this, "Select one account/client row first.", "Step test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                if (testRunning)
                {
                    supervisor.CancelDiagnosticTest();
                    testGeneration++;
                    testRunning = false;
                }
                ReadTop();
                supervisor.Apply(settings, true);
                if (supervisor.IsRunning) supervisor.Stop();

                int[] livePids = LiveVanillaPids();
                VanillaReconnectStatus selectedStatus = supervisor.Statuses()
                    .FirstOrDefault(s => string.Equals(s.AccountId, selected.Id, StringComparison.OrdinalIgnoreCase));
                bool selectedRunning = selectedStatus != null && selectedStatus.ProcessId.HasValue && IsRunningProcess(selectedStatus.ProcessId.Value);

                if (!selectedRunning)
                {
                    int[] claimedByOthers = supervisor.Statuses()
                        .Where(s => !string.Equals(s.AccountId, selected.Id, StringComparison.OrdinalIgnoreCase)
                            && s.ProcessId.HasValue && IsRunningProcess(s.ProcessId.Value))
                        .Select(s => s.ProcessId.Value)
                        .Distinct()
                        .ToArray();
                    int[] unassigned = livePids.Except(claimedByOthers).ToArray();
                    if (unassigned.Length == 1)
                    {
                        supervisor.AssignSingleDiagnosticClient(selected.Id);
                        selectedStatus = supervisor.Statuses()
                            .FirstOrDefault(s => string.Equals(s.AccountId, selected.Id, StringComparison.OrdinalIgnoreCase));
                        selectedRunning = selectedStatus != null && selectedStatus.ProcessId.HasValue && IsRunningProcess(selectedStatus.ProcessId.Value);
                    }
                    else if (unassigned.Length > 1)
                    {
                        throw new InvalidOperationException("More than one unassigned Vanilla client is running, so the selected account cannot be identified with certainty. Close the unintended unassigned client first.");
                    }
                }

                if (selectedRunning)
                {
                    if (step == VanillaReconnectTestStep.LauncherGameStart)
                    {
                        int satisfied = BeginTest("STEP 1 already satisfied for " + selected.Label);
                        CompleteTest(satisfied, true, "The selected account already has a running Vanilla client (PID " + selectedStatus.ProcessId.Value
                            + "). Only the selected account was checked.");
                        return;
                    }

                    int generation = BeginTest("STEP TEST: " + step + " for " + selected.Label + " only");
                    supervisor.RecordTestLog("Selected-only step " + step + " started for '" + selected.Label + "' PID " + selectedStatus.ProcessId.Value + ".");
                    supervisor.RunDiagnosticStep(selected.Id, step);
                    WaitForSelectedStep(generation, selected.Id, selected.Label, step, 45000);
                    return;
                }

                if (livePids.Length >= settings.MaxClients)
                    throw new InvalidOperationException("The selected account has no assigned running client and the configured client limit is already reached.");

                int chainGeneration = BeginTest("SELECTED CHAIN: " + selected.Label + " through " + step);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    string failure;
                    if (!RunDiagnosticChainThroughBlocking(chainGeneration, selected.Id, step, out failure))
                    {
                        if (failure != null && failure.StartsWith("STOPPED:", StringComparison.Ordinal)) CompleteTestStopped(chainGeneration, failure.Substring(8).Trim());
                        else CompleteTest(chainGeneration, false, failure);
                        return;
                    }
                    if (step == VanillaReconnectTestStep.ResumeHotkey && !supervisor.MinimizeAssignedClient(selected.Id))
                    {
                        CompleteTest(chainGeneration, false, selected.Label + " passed through ResumeHotkey but could not be minimized. The client was left running.");
                        return;
                    }
                    CompleteTest(chainGeneration, true, "Selected account passed through " + step + ". No other client was tested."
                        + (step == VanillaReconnectTestStep.ResumeHotkey ? " The selected client was left running and minimized." : string.Empty));
                });
            }
            catch (Exception ex) { FailTestImmediately("Selected step " + step, ex); }
        }

        private bool RunDiagnosticChainThroughBlocking(int generation, string accountId, VanillaReconnectTestStep targetStep, out string failure)
        {
            for (int value = (int)VanillaReconnectTestStep.LauncherGameStart; value <= (int)targetStep; value++)
            {
                if (generation != testGeneration)
                {
                    failure = "STOPPED: test cancelled.";
                    return false;
                }
                VanillaReconnectTestStep step = (VanillaReconnectTestStep)value;
                if (step != VanillaReconnectTestStep.LauncherGameStart) supervisor.AssignSingleDiagnosticClient(accountId);
                supervisor.RunDiagnosticStep(accountId, step);
                if (!WaitForDiagnosticCompletion(accountId, step,
                    step == VanillaReconnectTestStep.LauncherGameStart ? 150000 : 45000, out failure)) return false;
                if (step == targetStep) break;
                int delay = step == VanillaReconnectTestStep.LauncherGameStart
                    ? Math.Max(1000, settings.GepardWaitMs)
                    : step == VanillaReconnectTestStep.SelectCharacter
                        ? Math.Max(1000, settings.GameLoadMs)
                        : Math.Max(500, settings.StageDelayMs);
                int waited = 0;
                while (waited < delay)
                {
                    if (generation != testGeneration)
                    {
                        failure = "STOPPED: test cancelled.";
                        return false;
                    }
                    int slice = Math.Min(250, delay - waited);
                    Thread.Sleep(slice);
                    waited += slice;
                }
            }
            failure = null;
            return true;
        }

        private void WaitForSelectedStep(int generation, string accountId, string label, VanillaReconnectTestStep step, int timeoutMs)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string failure;
                if (!WaitForDiagnosticCompletion(accountId, step, timeoutMs, out failure))
                {
                    if (failure != null && failure.StartsWith("STOPPED:", StringComparison.Ordinal)) CompleteTestStopped(generation, failure.Substring(8).Trim());
                    else CompleteTest(generation, false, failure);
                    return;
                }
                if (step == VanillaReconnectTestStep.ResumeHotkey && !supervisor.MinimizeAssignedClient(accountId))
                {
                    CompleteTest(generation, false, label + " completed ResumeHotkey but could not be minimized. The client was left running.");
                    return;
                }
                CompleteTest(generation, true, "Step " + step + " passed for selected client " + label + " only."
                    + (step == VanillaReconnectTestStep.ResumeHotkey ? " The client was left running and minimized." : string.Empty));
            });
        }

        private static int[] LiveVanillaPids()
        {
            Process[] live = Process.GetProcessesByName("Vanilla MMO");
            try { return live.Where(p => !p.HasExited).OrderBy(p => p.StartTime).Select(p => p.Id).ToArray(); }
            finally { foreach (Process process in live) process.Dispose(); }
        }

        private void UpdateTestState(int generation, string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => UpdateTestState(generation, text)));
                return;
            }
            if (generation != testGeneration) return;
            testState.Text = text;
            testState.ForeColor = Color.DarkSlateBlue;
        }
    }
}

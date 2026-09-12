using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    public enum VanillaReconnectTestStep
    {
        LauncherGameStart,
        ProxySelection,
        FillCredentials,
        SubmitCredentials,
        SelectGameServer,
        SelectCharacter,
        ResumeHotkey
    }

    public sealed partial class VanillaReconnectSupervisor
    {
        private int diagnosticGeneration;

        public void CancelDiagnosticTest()
        {
            Interlocked.Increment(ref diagnosticGeneration);
            lock (gate)
            {
                foreach (var runtime in runtimes.Values)
                {
                    if (runtime.Detail != null && runtime.Detail.StartsWith("Diagnostic", StringComparison.Ordinal))
                    {
                        runtime.ScriptRunning = false;
                        SetStage(runtime, VanillaReconnectStage.Stopped, "Diagnostic test stopped by user");
                    }
                }
            }
            RaiseUpdated();
        }

        private bool DiagnosticCancelled(int generation)
        {
            return disposed || generation != Volatile.Read(ref diagnosticGeneration);
        }
        public void AssignSingleDiagnosticClient(string accountId)
        {
            lock (gate)
            {
                Runtime runtime;
                if (!runtimes.TryGetValue(accountId, out runtime)) throw new ArgumentException("Unknown account.");
                if (runtime.ProcessId.HasValue)
                {
                    try
                    {
                        using (var existing = Process.GetProcessById(runtime.ProcessId.Value))
                            if (!existing.HasExited) return;
                    }
                    catch { runtime.ProcessId = null; }
                }

                var processes = GetVanillaProcesses().OrderBy(p => SafeStart(p)).ToList();
                try
                {
                    if (processes.Count == 0) throw new InvalidOperationException("No running Vanilla client was found for this step test.");
                    if (processes.Count > 1) throw new InvalidOperationException("This is a one-client diagnostic. Leave only the Vanilla client you want to test running, then press the step button again.");
                    runtime.ProcessId = processes[0].Id;
                    runtime.ResumeSent = true;
                    runtime.ScriptRunning = false;
                    SetStage(runtime, VanillaReconnectStage.Stopped, "Selected for one-client diagnostic (PID " + runtime.ProcessId.Value + ")");
                }
                finally { foreach (var process in processes) process.Dispose(); }
            }
            RaiseUpdated();
        }

        public void RunDiagnosticStep(string accountId, VanillaReconnectTestStep step)
        {
            VanillaReconnectAccount account;
            VanillaReconnectSettings config;
            int? pid = null;
            int generation = Interlocked.Increment(ref diagnosticGeneration);
            lock (gate)
            {
                Runtime runtime;
                if (!runtimes.TryGetValue(accountId, out runtime)) throw new ArgumentException("Unknown account.");
                if (runtime.ScriptRunning) throw new InvalidOperationException("Another action is already running for this account.");
                if (step != VanillaReconnectTestStep.LauncherGameStart)
                {
                    if (!runtime.ProcessId.HasValue) throw new InvalidOperationException("No Vanilla client is assigned to this selected account for the step test.");
                    pid = runtime.ProcessId.Value;
                }
                runtime.ScriptRunning = true;
                account = runtime.Account.Clone();
                config = settings.Clone();
                SetStage(runtime, VanillaReconnectStage.LoggingIn, "Diagnostic step running: " + step);
            }
            ThreadPool.QueueUserWorkItem(_ => DiagnosticStepWorker(accountId, account, config, pid, step, generation));
            RaiseUpdated();
        }

        private void DiagnosticStepWorker(string accountId, VanillaReconnectAccount account, VanillaReconnectSettings config, int? pid, VanillaReconnectTestStep step, int generation)
        {
            string error = null;
            string stopped = null;
            int? discoveredPid = pid;
            try
            {
                Log("TEST " + account.Label + ": starting step " + step + ".");
                if (step == VanillaReconnectTestStep.LauncherGameStart)
                {
                    discoveredPid = VanillaPatcherLauncher.Launch(config.LaunchExecutable, config.LaunchArguments,
                        message => Log("TEST " + account.Label + ": " + message), () => DiagnosticCancelled(generation));
                    if (!discoveredPid.HasValue) throw new InvalidOperationException("Launcher test did not produce a Vanilla process ID.");
                }
                else
                {
                    using (var input = new VanillaForegroundInput(discoveredPid.Value))
                    {
                        switch (step)
                        {
                            case VanillaReconnectTestStep.ProxySelection:
                                input.ClickNormalized(config.Anchors.ServiceListX, config.Anchors.ServiceListY);
                                input.Press(Keys.Home);
                                for (int i = 0; i < (int)config.Proxy; i++) input.Press(Keys.Down);
                                input.Press(Keys.Enter);
                                break;
                            case VanillaReconnectTestStep.FillCredentials:
                                string password = store.UnprotectPassword(account.ProtectedPassword);
                                if (string.IsNullOrEmpty(account.UserName) || string.IsNullOrEmpty(password)) throw new InvalidOperationException("Username/password is missing.");
                                input.ClickNormalized(config.Anchors.UserNameX, config.Anchors.UserNameY);
                                Thread.Sleep(180);
                                input.ReplaceFocusedText(account.UserName);
                                input.Press(Keys.Tab);
                                Thread.Sleep(180);
                                input.ReplaceFocusedText(password);
                                Log("TEST " + account.Label + ": username then password filled without submitting; password was not logged.");
                                break;
                            case VanillaReconnectTestStep.SubmitCredentials:
                                input.Press(Keys.Enter);
                                break;
                            case VanillaReconnectTestStep.SelectGameServer:
                                input.ClickNormalized(config.Anchors.ServiceListX, config.Anchors.ServiceListY);
                                input.Press(Keys.Home);
                                input.Press(Keys.Enter);
                                break;
                            case VanillaReconnectTestStep.SelectCharacter:
                                int slot = Math.Max(1, Math.Min(15, account.CharacterSlot)) - 1;
                                int col = slot % 5, row = slot / 5;
                                input.ClickNormalized(config.Anchors.CharacterGridX + col * config.Anchors.CharacterStepX,
                                    config.Anchors.CharacterGridY + row * config.Anchors.CharacterStepY);
                                Thread.Sleep(450);
                                input.ClickNormalized(config.Anchors.GameStartX, config.Anchors.GameStartY);
                                break;
                            case VanillaReconnectTestStep.ResumeHotkey:
                                input.Chord(account.ResumeCtrl, account.ResumeAlt, account.ResumeShift, (Keys)account.ResumeKey);
                                break;
                        }
                    }
                }
            }
            catch (OperationCanceledException ex) { stopped = ex.Message; }
            catch (Exception ex)
            {
                bool closed = false;
                if (discoveredPid.HasValue)
                {
                    try { using (var process = Process.GetProcessById(discoveredPid.Value)) closed = process.HasExited; }
                    catch { closed = true; }
                }
                if (closed) stopped = "Vanilla window/process was closed.";
                else error = ex.Message;
            }
            finally
            {
                if (generation == Volatile.Read(ref diagnosticGeneration))
                {
                    lock (gate)
                    {
                        Runtime runtime;
                        if (runtimes.TryGetValue(accountId, out runtime))
                        {
                            if (discoveredPid.HasValue) runtime.ProcessId = discoveredPid;
                            runtime.ScriptRunning = false;
                            if (stopped != null) SetStage(runtime, VanillaReconnectStage.Stopped, "Diagnostic test stopped: " + stopped);
                            else SetStage(runtime, error == null ? VanillaReconnectStage.WaitingForGameplay : VanillaReconnectStage.Error,
                                error == null ? "Diagnostic step completed: " + step : "Diagnostic step failed: " + error);
                        }
                    }
                    if (stopped != null) Log("TEST " + account.Label + ": STOPPED: " + stopped);
                    else if (error == null) Log("TEST " + account.Label + ": step " + step + " completed.");
                    else Log("TEST " + account.Label + ": step " + step + " FAILED: " + error);
                    RaiseUpdated();
                }
            }
        }
    }

    internal sealed partial class VanillaReconnectForm
    {
        private Control BuildStepTests()
        {
            var box = new GroupBox
            {
                Text = "Step-by-step one-client diagnostic (select one account row first)",
                Dock = DockStyle.Top,
                Height = 72,
                Padding = new Padding(8),
                AutoSize = false
            };
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                WrapContents = true,
                Padding = new Padding(0, 4, 0, 0)
            };
            AddButton(row, "1 GAME START", () => RunStepTest(VanillaReconnectTestStep.LauncherGameStart));
            AddButton(row, "2 PROXY", () => RunStepTest(VanillaReconnectTestStep.ProxySelection));
            AddButton(row, "3 FILL USER/PW", () => RunStepTest(VanillaReconnectTestStep.FillCredentials));
            AddButton(row, "4 SUBMIT LOGIN", () => RunStepTest(VanillaReconnectTestStep.SubmitCredentials));
            AddButton(row, "5 SERVER", () => RunStepTest(VanillaReconnectTestStep.SelectGameServer));
            AddButton(row, "6 CHARACTER", () => RunStepTest(VanillaReconnectTestStep.SelectCharacter));
            AddButton(row, "7 RESUME HOTKEY", () => RunStepTest(VanillaReconnectTestStep.ResumeHotkey));
            AddButton(row, "STOP TEST", StopCurrentTest);
            box.Controls.Add(row);
            return box;
        }

        private void ConfigureStepTestHoverHelp()
        {
            TipByText(this, "1 GAME START", "If no Vanilla client exists, test GAME START from the launcher. If a client already exists, step 1 is already satisfied.");
            TipByText(this, "2 PROXY", "If one Vanilla client exists, test only Proxy. If none exists, automatically run from GAME START through Proxy.");
            TipByText(this, "3 FILL USER/PW", "Existing client: test only credential filling. No client: automatically run all earlier numbered steps first. Credentials are filled without submitting.");
            TipByText(this, "4 SUBMIT LOGIN", "Existing client: submit only. No client: automatically run all earlier numbered steps first, then submit.");
            TipByText(this, "5 SERVER", "Existing client: server step only. No client: automatically run all earlier numbered steps first.");
            TipByText(this, "6 CHARACTER", "Existing client: character step only. No client: automatically run all earlier numbered steps first.");
            TipByText(this, "7 RESUME HOTKEY", "Existing client: resume hotkey only. No client: automatically run the complete numbered sequence first.");
            TipByText(this, "STOP TEST", "Cancel the current diagnostic/recovery test immediately. Closing the launcher or the one diagnostic Vanilla client also stops the test automatically.");
        }

        private void StopCurrentTest()
        {
            supervisor.CancelDiagnosticTest();
            if (supervisor.IsRunning) supervisor.Stop();
            testGeneration++;
            testRunning = false;
            testState.Text = "TEST STOPPED";
            testState.ForeColor = Color.DarkOrange;
        }

        private void CompleteTestStopped(int generation, string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)(() => CompleteTestStopped(generation, message))); return; }
            if (generation != testGeneration) return;
            testRunning = false;
            testState.Text = "TEST STOPPED";
            testState.ForeColor = Color.DarkOrange;
        }
        private void RunStepTest(VanillaReconnectTestStep step)
        {
            var selected = SelectedAccount();
            if (selected == null) { MessageBox.Show(this, "Select one account row first.", "Step test"); return; }
            try
            {
                // A newer numbered step deliberately supersedes any previous diagnostic wait.
                // This is important when the user manually advances the game while step 1 is
                // still waiting for the launcher result.
                if (testRunning)
                {
                    supervisor.CancelDiagnosticTest();
                    testRunning = false;
                    testGeneration++;
                }

                ReadTop();
                supervisor.Apply(settings, true);
                // Always reset supervisor runtime flags before a manual step, even when the
                // continuous supervisor itself is already stopped.
                supervisor.Stop();

                var live = Process.GetProcessesByName("Vanilla MMO");
                int liveCount;
                try { liveCount = live.Length; }
                finally { foreach (var process in live) process.Dispose(); }
                if (liveCount > 1)
                    throw new InvalidOperationException("One-client diagnostics found multiple Vanilla clients. Leave only the client you want to test running.");

                if (liveCount == 1)
                {
                    supervisor.AssignSingleDiagnosticClient(selected.Id);
                    if (step == VanillaReconnectTestStep.LauncherGameStart)
                    {
                        int already = BeginTest("STEP 1 already satisfied for " + selected.Label);
                        CompleteTest(already, true, "A Vanilla client is already running. GAME START is therefore already satisfied; press the next numbered step to test only that stage.");
                        return;
                    }

                    int generation = BeginTest("STEP TEST: " + step + " for " + selected.Label);
                    supervisor.RunDiagnosticStep(selected.Id, step);
                    WaitForDiagnosticStep(generation, selected.Id, step, 45000);
                    return;
                }

                // No Vanilla window exists: run the numbered diagnostic from the beginning.
                // This lets every button work independently instead of requiring the user to
                // remember which earlier test left the client on which screen.
                int chainGeneration = BeginTest("CHAIN TEST from GAME START through " + step + " for " + selected.Label);
                RunDiagnosticChainFromScratch(chainGeneration, selected.Id, step);
            }
            catch (Exception ex) { FailTestImmediately("Step test " + step, ex); }
        }

        private void RunDiagnosticChainFromScratch(int generation, string accountId, VanillaReconnectTestStep targetStep)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    for (int value = (int)VanillaReconnectTestStep.LauncherGameStart; value <= (int)targetStep; value++)
                    {
                        if (generation != testGeneration) return;
                        var step = (VanillaReconnectTestStep)value;
                        if (step != VanillaReconnectTestStep.LauncherGameStart) supervisor.AssignSingleDiagnosticClient(accountId);
                        supervisor.RunDiagnosticStep(accountId, step);
                        string failure;
                        if (!WaitForDiagnosticCompletion(accountId, step, step == VanillaReconnectTestStep.LauncherGameStart ? 150000 : 45000, out failure))
                        {
                            if (failure != null && failure.StartsWith("STOPPED:", StringComparison.Ordinal)) CompleteTestStopped(generation, failure.Substring(8).Trim());
                            else CompleteTest(generation, false, failure);
                            return;
                        }
                        if (step == targetStep)
                        {
                            CompleteTest(generation, true, "Chain test passed through " + targetStep + ". If the screen is correct, continue with the next numbered step.");
                            return;
                        }

                        int delay = step == VanillaReconnectTestStep.LauncherGameStart
                            ? Math.Max(1000, settings.GepardWaitMs)
                            : step == VanillaReconnectTestStep.SelectCharacter
                                ? Math.Max(1000, settings.GameLoadMs)
                                : Math.Max(500, settings.StageDelayMs);
                        int waited = 0;
                        while (waited < delay)
                        {
                            if (generation != testGeneration) return;
                            int slice = Math.Min(250, delay - waited);
                            Thread.Sleep(slice);
                            waited += slice;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (ex.Message.IndexOf("No running Vanilla client", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.IndexOf("Vanilla client exited", StringComparison.OrdinalIgnoreCase) >= 0)
                        CompleteTestStopped(generation, "Vanilla window/process was closed; test stopped.");
                    else CompleteTest(generation, false, "Chain test failed: " + ex.Message);
                }
            });
        }

        private bool WaitForDiagnosticCompletion(string accountId, VanillaReconnectTestStep step, int timeoutMs, out string failure)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            string successDetail = "Diagnostic step completed: " + step;
            string failurePrefix = "Diagnostic step failed:";
            string stoppedPrefix = "Diagnostic test stopped:";
            while (DateTime.UtcNow < deadline)
            {
                var current = supervisor.Statuses().FirstOrDefault(s => string.Equals(s.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
                if (current != null)
                {
                    if (string.Equals(current.Detail, successDetail, StringComparison.Ordinal))
                    {
                        failure = null;
                        return true;
                    }
                    if (current.Detail != null && current.Detail.StartsWith(stoppedPrefix, StringComparison.Ordinal))
                    {
                        failure = "STOPPED: " + current.Detail;
                        return false;
                    }
                    if (current.Detail != null && current.Detail.StartsWith(failurePrefix, StringComparison.Ordinal))
                    {
                        failure = current.Detail;
                        return false;
                    }
                }
                Thread.Sleep(250);
            }
            failure = "Step " + step + " timed out. Press COPY LOG and paste the reconnect log for analysis.";
            return false;
        }
        private void WaitForDiagnosticStep(int generation, string accountId, VanillaReconnectTestStep step, int timeoutMs)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                string successDetail = "Diagnostic step completed: " + step;
                string failurePrefix = "Diagnostic step failed:";
                string stoppedPrefix = "Diagnostic test stopped:";
                while (DateTime.UtcNow < deadline)
                {
                    var current = supervisor.Statuses().FirstOrDefault(s => string.Equals(s.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
                    if (current != null)
                    {
                        if (string.Equals(current.Detail, successDetail, StringComparison.Ordinal))
                        {
                            CompleteTest(generation, true, "Step " + step + " passed. Continue with the next numbered step when the Vanilla screen is ready.");
                            return;
                        }
                        if (current.Detail != null && current.Detail.StartsWith(stoppedPrefix, StringComparison.Ordinal))
                        {
                            CompleteTestStopped(generation, current.Detail);
                            return;
                        }
                        if (current.Detail != null && current.Detail.StartsWith(failurePrefix, StringComparison.Ordinal))
                        {
                            CompleteTest(generation, false, current.Detail);
                            return;
                        }
                    }
                    Thread.Sleep(250);
                }
                CompleteTest(generation, false, "Step " + step + " timed out. Press COPY LOG and paste the reconnect log for analysis.");
            });
        }
    }
}

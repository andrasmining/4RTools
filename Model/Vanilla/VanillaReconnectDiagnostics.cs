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
            ThreadPool.QueueUserWorkItem(_ => DiagnosticStepWorker(accountId, account, config, pid, step));
            RaiseUpdated();
        }

        private void DiagnosticStepWorker(string accountId, VanillaReconnectAccount account, VanillaReconnectSettings config, int? pid, VanillaReconnectTestStep step)
        {
            string error = null;
            int? discoveredPid = pid;
            try
            {
                Log("TEST " + account.Label + ": starting step " + step + ".");
                if (step == VanillaReconnectTestStep.LauncherGameStart)
                {
                    discoveredPid = VanillaPatcherLauncher.Launch(config.LaunchExecutable, config.LaunchArguments,
                        message => Log("TEST " + account.Label + ": " + message), () => disposed);
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
            catch (Exception ex) { error = ex.Message; }
            finally
            {
                lock (gate)
                {
                    Runtime runtime;
                    if (runtimes.TryGetValue(accountId, out runtime))
                    {
                        if (discoveredPid.HasValue) runtime.ProcessId = discoveredPid;
                        runtime.ScriptRunning = false;
                        SetStage(runtime, error == null ? VanillaReconnectStage.WaitingForGameplay : VanillaReconnectStage.Error,
                            error == null ? "Diagnostic step completed: " + step : "Diagnostic step failed: " + error);
                    }
                }
                if (error == null) Log("TEST " + account.Label + ": step " + step + " completed.");
                else Log("TEST " + account.Label + ": step " + step + " FAILED: " + error);
                RaiseUpdated();
            }
        }
    }

    internal sealed partial class VanillaReconnectForm
    {
        private Control BuildStepTests()
        {
            var box = new GroupBox { Text = "One-client step tests (select one account row first)", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8) };
            var row = Flow();
            AddButton(row, "1 GAME START", () => RunStepTest(VanillaReconnectTestStep.LauncherGameStart));
            AddButton(row, "2 PROXY", () => RunStepTest(VanillaReconnectTestStep.ProxySelection));
            AddButton(row, "3 FILL USER/PW", () => RunStepTest(VanillaReconnectTestStep.FillCredentials));
            AddButton(row, "4 SUBMIT LOGIN", () => RunStepTest(VanillaReconnectTestStep.SubmitCredentials));
            AddButton(row, "5 SERVER", () => RunStepTest(VanillaReconnectTestStep.SelectGameServer));
            AddButton(row, "6 CHARACTER", () => RunStepTest(VanillaReconnectTestStep.SelectCharacter));
            AddButton(row, "7 RESUME HOTKEY", () => RunStepTest(VanillaReconnectTestStep.ResumeHotkey));
            box.Controls.Add(row);
            return box;
        }

        private void ConfigureStepTestHoverHelp()
        {
            TipByText(this, "1 GAME START", "Selected account only. Close Vanilla first. This opens the launcher, performs one real foreground click on GAME START, and waits for Vanilla/Gepard. It stops there.");
            TipByText(this, "2 PROXY", "Selected account only. With one Vanilla client running, choose the configured proxy on Select Service.");
            TipByText(this, "3 FILL USER/PW", "Selected account only. With one client running at login, explicitly click/replace username FIRST, Tab to password, replace password, and DO NOT submit so you can visually verify both fields.");
            TipByText(this, "4 SUBMIT LOGIN", "Selected account only. Press Enter once to submit the credentials currently visible on the login screen.");
            TipByText(this, "5 SERVER", "Selected account only. Select the first/only game server and press Enter.");
            TipByText(this, "6 CHARACTER", "Selected account only. Click the configured character slot and character-screen Game Start.");
            TipByText(this, "7 RESUME HOTKEY", "Selected account only. Focus that exact Vanilla window and send the configured Autobattle resume hotkey once, e.g. Alt+2.");
        }

        private void RunStepTest(VanillaReconnectTestStep step)
        {
            var selected = SelectedAccount();
            if (selected == null) { MessageBox.Show(this, "Select one account row first.", "Step test"); return; }
            try
            {
                if (testRunning) throw new InvalidOperationException("Another test is already running.");
                ReadTop();
                supervisor.Apply(settings, true);
                if (supervisor.IsRunning) supervisor.Stop();

                if (step == VanillaReconnectTestStep.LauncherGameStart)
                {
                    var live = Process.GetProcessesByName("Vanilla MMO");
                    try
                    {
                        if (live.Length > 0) throw new InvalidOperationException("Close all Vanilla clients before step 1 so GAME START can be verified unambiguously.");
                    }
                    finally { foreach (var process in live) process.Dispose(); }
                }
                else supervisor.AssignSingleDiagnosticClient(selected.Id);

                int generation = BeginTest("STEP TEST: " + step + " for " + selected.Label);
                supervisor.RunDiagnosticStep(selected.Id, step);
                WaitForDiagnosticStep(generation, selected.Id, step, step == VanillaReconnectTestStep.LauncherGameStart ? 150000 : 30000);
            }
            catch (Exception ex) { FailTestImmediately("Step test " + step, ex); }
        }

        private void WaitForDiagnosticStep(int generation, string accountId, VanillaReconnectTestStep step, int timeoutMs)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                string successDetail = "Diagnostic step completed: " + step;
                string failurePrefix = "Diagnostic step failed:";
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

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
                    AdoptExistingClients(false);
                    if (!runtime.ProcessId.HasValue) throw new InvalidOperationException("No running Vanilla client is assigned to the selected account. Use DETECT RUNNING CLIENTS first.");
                    pid = runtime.ProcessId.Value;
                }
                runtime.ScriptRunning = true;
                account = runtime.Account.Clone();
                config = settings.Clone();
                SetStage(runtime, VanillaReconnectStage.LoggingIn, "Diagnostic step: " + step);
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
                                Log("TEST " + account.Label + ": credentials filled without submitting; password was not logged.");
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
            TipByText(this, "1 GAME START", "Selected account only. Stops automatic recovery, opens the configured launcher, clicks GAME START and waits for one Vanilla process. It does not continue login.");
            TipByText(this, "2 PROXY", "Selected running client only. Choose the configured proxy on Select Service.");
            TipByText(this, "3 FILL USER/PW", "Selected running client only. Explicitly focuses/replaces username first, Tabs to password, replaces password, and deliberately does not submit so you can inspect it.");
            TipByText(this, "4 SUBMIT LOGIN", "Selected running client only. Press Enter once to submit the visible credentials.");
            TipByText(this, "5 SERVER", "Selected running client only. Select the first/only game server and press Enter.");
            TipByText(this, "6 CHARACTER", "Selected running client only. Click the configured character slot and character-screen Game Start.");
            TipByText(this, "7 RESUME HOTKEY", "Selected running client only. Focus that exact Vanilla window and send the configured Autobattle resume hotkey once.");
        }

        private void RunStepTest(VanillaReconnectTestStep step)
        {
            var selected = SelectedAccount();
            if (selected == null) { MessageBox.Show(this, "Select one account row first.", "Step test"); return; }
            try
            {
                ReadTop();
                supervisor.Apply(settings, true);
                if (supervisor.IsRunning) supervisor.Stop();
                if (step != VanillaReconnectTestStep.LauncherGameStart) supervisor.DetectRunningClients();
                supervisor.RunDiagnosticStep(selected.Id, step);
                testState.Text = "STEP TEST RUNNING: " + step + " for " + selected.Label;
                testState.ForeColor = Color.DarkSlateBlue;
            }
            catch (Exception ex) { FailTestImmediately("Step test " + step, ex); }
        }
    }
}
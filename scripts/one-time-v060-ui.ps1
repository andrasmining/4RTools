Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
$nl = [Environment]::NewLine
function ReadText([string]$p) { [IO.File]::ReadAllText($p) }
function WriteText([string]$p,[string]$t) { [IO.File]::WriteAllText($p,$t,$utf8) }

$p='Model/Vanilla/VanillaReconnect.cs'
$t=ReadText $p
$t=$t.Replace('public double UserNameY { get; set; } = 0.66;','public double UserNameY { get; set; } = 0.635;')
$t=$t.Replace('public double PasswordY { get; set; } = 0.685;','public double PasswordY { get; set; } = 0.660;')

$old='            Accounts = unique;'+$nl+'        }'+$nl+$nl+'        private static bool IsSyntheticDefault'
if(-not $t.Contains($old)){throw 'NormalizeAccounts anchor missing'}
$new='            Accounts = unique;'+$nl+
'            if (Anchors == null) Anchors = new VanillaUiAnchors();'+$nl+
'            if (Math.Abs(Anchors.UserNameY - 0.66) < 0.0001 && Math.Abs(Anchors.PasswordY - 0.685) < 0.0001) { Anchors.UserNameY = 0.635; Anchors.PasswordY = 0.660; }'+$nl+
'        }'+$nl+$nl+'        private static bool IsSyntheticDefault'
$t=$t.Replace($old,$new)

$old='    public sealed class VanillaReconnectSupervisor : IDisposable'
if(-not $t.Contains($old)){throw 'Supervisor declaration anchor missing'}
$t=$t.Replace($old,'    public sealed partial class VanillaReconnectSupervisor : IDisposable')
$old='    internal sealed class VanillaReconnectForm : Form'
if(-not $t.Contains($old)){throw 'Reconnect form declaration anchor missing'}
$t=$t.Replace($old,'    internal sealed partial class VanillaReconnectForm : Form')

$old='        public string SettingsPath { get { return store.FilePath; } }'
if(-not $t.Contains($old)){throw 'SettingsPath anchor missing'}
$t=$t.Replace($old,$old+$nl+'        public string LogPath { get { return Path.Combine(baseDirectory, "Logs", "reconnect.log"); } }')

# Login and resume now use verified ordinary foreground input. Vanilla arrives with caret in password,
# so username is explicitly clicked/replaced first and password is reached by a single Tab.
$old='            using (var input = new VanillaTargetedInput(runtime.ProcessId.Value))'+$nl+'            {'+$nl+'                input.Activate();'+$nl+'                input.Chord(runtime.Account.ResumeCtrl, runtime.Account.ResumeAlt, runtime.Account.ResumeShift, (Keys)runtime.Account.ResumeKey);'
if(-not $t.Contains($old)){throw 'SendResume anchor missing'}
$new='            using (var input = new VanillaForegroundInput(runtime.ProcessId.Value))'+$nl+'            {'+$nl+'                input.Chord(runtime.Account.ResumeCtrl, runtime.Account.ResumeAlt, runtime.Account.ResumeShift, (Keys)runtime.Account.ResumeKey);'
$t=$t.Replace($old,$new)

$old='                using (var input = new VanillaTargetedInput(pid))'+$nl+'                {'
if(-not $t.Contains($old)){throw 'LoginWorker input anchor missing'}
$t=$t.Replace($old,'                using (var input = new VanillaForegroundInput(pid))'+$nl+'                {')

$old='                    input.Activate();'+$nl+
'                    input.ClickNormalized(config.Anchors.UserNameX, config.Anchors.UserNameY);'+$nl+
'                    input.SelectAll();'+$nl+
'                    input.TypeText(account.UserName);'+$nl+
'                    input.ClickNormalized(config.Anchors.PasswordX, config.Anchors.PasswordY);'+$nl+
'                    input.SelectAll();'+$nl+
'                    input.TypeText(password);'+$nl+
'                    input.Press(Keys.Enter);'
if(-not $t.Contains($old)){throw 'Credential block anchor missing'}
$new='                    input.Activate();'+$nl+
'                    input.ClickNormalized(config.Anchors.UserNameX, config.Anchors.UserNameY);'+$nl+
'                    Thread.Sleep(180);'+$nl+
'                    input.ReplaceFocusedText(account.UserName);'+$nl+
'                    input.Press(Keys.Tab);'+$nl+
'                    Thread.Sleep(180);'+$nl+
'                    input.ReplaceFocusedText(password);'+$nl+
'                    Thread.Sleep(220);'+$nl+
'                    input.Press(Keys.Enter);'
$t=$t.Replace($old,$new)

$old='        private readonly Label testState = new Label { AutoSize = true, ForeColor = Color.DarkSlateBlue };'
if(-not $t.Contains($old)){throw 'testState anchor missing'}
$t=$t.Replace($old,$old+$nl+'        private readonly ToolTip help = new ToolTip { InitialDelay = 650, ReshowDelay = 200, AutoPopDelay = 30000, ShowAlways = true };'+$nl+'        private bool exitRequested;')

$old='            BuildUi();'+$nl+'            supervisor.Updated += SupervisorUpdated;'
if(-not $t.Contains($old)){throw 'BuildUi anchor missing'}
$t=$t.Replace($old,'            BuildUi();'+$nl+'            ConfigureHoverHelp();'+$nl+'            supervisor.Updated += SupervisorUpdated;')

$old='            AddButton(commands, "DETECT RUNNING CLIENTS", DetectRunningClients);'+$nl+'            runState.Font'
if(-not $t.Contains($old)){throw 'commands anchor missing'}
$new='            AddButton(commands, "DETECT RUNNING CLIENTS", DetectRunningClients);'+$nl+
'            AddButton(commands, "OPEN LOG", OpenLog);'+$nl+
'            AddButton(commands, "COPY LOG", CopyLog);'+$nl+'            runState.Font'
$t=$t.Replace($old,$new)

$old='            top.Controls.Add(tests);'+$nl+$nl+'            var info = new Label'
if(-not $t.Contains($old)){throw 'tests row anchor missing'}
$t=$t.Replace($old,'            top.Controls.Add(tests);'+$nl+'            top.Controls.Add(BuildStepTests());'+$nl+$nl+'            var info = new Label')

$anchor='        private void LoadFromSupervisor()'
if(-not $t.Contains($anchor)){throw 'LoadFromSupervisor anchor missing'}
$methods=@'
        private void ConfigureHoverHelp()
        {
            help.SetToolTip(launchPath, "Path to Vanilla Launcher.exe / patcher.exe. Recovery starts it and presses GAME START before waiting for Vanilla/Gepard.");
            help.SetToolTip(launchArgs, "Optional launcher command-line arguments. Leave blank unless Vanilla requires them.");
            help.SetToolTip(proxy, "Proxy chosen on Vanilla's first Select Service screen.");
            help.SetToolTip(maxClients, "Maximum supervised Vanilla clients on this PC. Normally leave this at 2.");
            help.SetToolTip(startWithApp, "If checked, opening 4RTools automatically starts recovery monitoring. If unchecked, 4RTools can be open while the supervisor remains stopped.");
            help.SetToolTip(autoRecover, "Automatically relaunch and relog clients that close or return to a login screen.");
            help.SetToolTip(visualWatchdog, "Classifies Vanilla screenshots as gameplay/login/modal states. This does not modify the game or Gepard.");
            help.SetToolTip(accounts, "Your one or two configured account profiles. Select a row before using selected-account actions.");
            help.SetToolTip(status, "Runtime state only: account -> assigned PID -> recovery stage -> detected screen -> detail. These are not additional accounts.");
            help.SetToolTip(log, "Reconnect/test log. Secret contents are never written here.");
            TipByText(this, "Save", "Save exactly the settings and account rows currently shown.");
            TipByText(this, "START SUPERVISOR", "Start continuous recovery monitoring now. Existing clients are adopted; missing clients can be relaunched.");
            TipByText(this, "STOP", "Stop automatic recovery. Running Vanilla clients stay open.");
            TipByText(this, "DETECT RUNNING CLIENTS", "Find currently running Vanilla MMO.exe processes and assign them to configured account rows.");
            TipByText(this, "TEST STARTUP (clients closed)", "Full cold-start test: launcher -> GAME START -> proxy -> login -> server -> character -> resume hotkey.");
            TipByText(this, "TEST RESTART RECOVERY", "Close detected Vanilla windows normally and verify the full relaunch/relogin recovery path.");
            TipByText(this, "ARM MANUAL NETWORK-DROP TEST", "Arm a five-minute recovery test while you briefly disconnect/reconnect internet yourself.");
            TipByText(this, "OPEN LOG", "Open Logs\\reconnect.log in your default text editor.");
            TipByText(this, "COPY LOG", "Copy the complete reconnect log to the clipboard for pasting into ChatGPT.");
            ConfigureStepTestHoverHelp();
        }

        private void TipByText(Control root, string textValue, string tip)
        {
            foreach (Control child in root.Controls)
            {
                if (string.Equals(child.Text, textValue, StringComparison.Ordinal)) help.SetToolTip(child, tip);
                if (child.HasChildren) TipByText(child, textValue, tip);
            }
        }

        private void OpenLog()
        {
            try
            {
                string path = supervisor.LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path)) File.WriteAllText(path, string.Empty);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open reconnect log", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void CopyLog()
        {
            try
            {
                string path = supervisor.LogPath;
                string contents = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
                Clipboard.SetText(contents.Length == 0 ? "(reconnect log is empty)" : contents);
                testState.Text = "Reconnect log copied to clipboard.";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Copy reconnect log", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

'@
$t=$t.Replace($anchor,$methods+$anchor)

$old=@'
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
'@
if(-not $t.Contains($old)){throw 'OnFormClosing anchor missing'}
$new=@'
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing)
            {
                exitRequested = true;
                e.Cancel = true;
                BeginInvoke((MethodInvoker)Application.Exit);
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) Hide();
        }
'@
$t=$t.Replace($old,$new)

$old='            menu.Items.Add("Stop reconnect supervisor", null, (s, e) => { supervisor?.Stop(); });'
if(-not $t.Contains($old)){throw 'tray menu anchor missing'}
$new=$old+$nl+'            menu.Items.Add(new ToolStripSeparator());'+$nl+'            menu.Items.Add("Exit 4RTools", null, (s, e) => Application.Exit());'
$t=$t.Replace($old,$new)
WriteText $p $t

# Granular one-client diagnostics live in a partial to keep the production reconnect state machine readable.
$diag=@'
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
'@
WriteText 'Model/Vanilla/VanillaReconnectDiagnostics.cs' $diag

$p='Forms/Container.cs';$t=ReadText $p;$t=$t.Replace('v0.5.0','v0.6.0');WriteText $p $t
$p='Properties/AssemblyInfo.cs';$t=ReadText $p;$t=$t.Replace('0.5.0.0','0.6.0.0');WriteText $p $t

$notes=@'
# 4RTools Vanilla 0.6.0

- Launcher GAME START clicks now target the child control under the visible button instead of only the top-level launcher window.
- Login entry explicitly overwrites username first, Tabs to password, overwrites password, then submits. Legacy bad login-field anchors are migrated automatically.
- Resume hotkeys use verified foreground ordinary Windows input so modifier combinations such as Alt+2 go to the selected Vanilla client.
- Added seven selected-account step tests: GAME START, proxy, fill credentials without submitting, submit login, server, character, resume hotkey.
- Added hover documentation for reconnect controls and tests.
- Added OPEN LOG and COPY LOG.
- X exits 4RTools; minimizing the reconnect window hides it to the tray. Tray menu includes Exit 4RTools.

CI validates compilation, tests and portable packaging. Real Vanilla/Gepard interaction remains a local live test and is not claimed from CI.
'@
WriteText 'RELEASE-NOTES.md' $notes

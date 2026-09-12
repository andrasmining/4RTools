[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    function Replace-Once([string] $Path, [string] $Old, [string] $New) {
        $text = [IO.File]::ReadAllText($Path)
        $first = $text.IndexOf($Old, [StringComparison]::Ordinal)
        if ($first -lt 0) { throw "Expected fragment not found in $Path: $Old" }
        if ($text.IndexOf($Old, $first + $Old.Length, [StringComparison]::Ordinal) -ge 0) { throw "Fragment not unique in $Path: $Old" }
        [IO.File]::WriteAllText($Path, $text.Substring(0, $first) + $New + $text.Substring($first + $Old.Length), $utf8)
    }
    function Replace-RegexOnce([string] $Path, [string] $Pattern, [string] $Replacement) {
        $text = [IO.File]::ReadAllText($Path)
        $regex = [regex]::new($Pattern)
        $matches = $regex.Matches($text)
        if ($matches.Count -ne 1) { throw "Expected one match in $Path, found $($matches.Count): $Pattern" }
        [IO.File]::WriteAllText($Path, $regex.Replace($text, $Replacement, 1), $utf8)
    }

    $reconnect = 'Model/Vanilla/VanillaReconnect.cs'

    # Fix the duplicate-account bug at its source: Newtonsoft used to append the
    # two property-initializer defaults every time settings were cloned/loaded.
    Replace-RegexOnce $reconnect '(?s)        public List<VanillaReconnectAccount> Accounts \{ get; set; \} = new List<VanillaReconnectAccount>.*?        public void Validate\(\)' @'
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<VanillaReconnectAccount> Accounts { get; set; } = new List<VanillaReconnectAccount>();

        public static VanillaReconnectSettings CreateDefault()
        {
            var value = new VanillaReconnectSettings();
            value.Accounts.Add(new VanillaReconnectAccount { Label = "Client 1" });
            value.Accounts.Add(new VanillaReconnectAccount { Label = "Client 2" });
            return value;
        }

        public VanillaReconnectSettings Clone()
        {
            var value = JsonConvert.DeserializeObject<VanillaReconnectSettings>(
                JsonConvert.SerializeObject(this), new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
            if (value == null) throw new InvalidDataException("Reconnect settings could not be cloned.");
            value.NormalizeAccounts();
            return value;
        }

        public void NormalizeAccounts()
        {
            if (Accounts == null) Accounts = new List<VanillaReconnectAccount>();
            var unique = Accounts.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Id))
                .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(a => IsSyntheticDefault(a) ? 0 : 1).First())
                .ToList();
            if (unique.Count > 2)
            {
                var preferred = unique.Where(a => !IsSyntheticDefault(a)).ToList();
                foreach (var account in unique)
                {
                    if (preferred.Count >= 2) break;
                    if (!preferred.Contains(account)) preferred.Add(account);
                }
                unique = preferred.Take(2).ToList();
            }
            Accounts = unique;
        }

        private static bool IsSyntheticDefault(VanillaReconnectAccount account)
        {
            if (account == null) return true;
            bool defaultLabel = string.Equals(account.Label, "Client 1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(account.Label, "Client 2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(account.Label, "Client", StringComparison.OrdinalIgnoreCase);
            return defaultLabel && string.IsNullOrWhiteSpace(account.UserName) && string.IsNullOrWhiteSpace(account.ProtectedPassword);
        }

        public void Validate()
'@
    Replace-Once $reconnect '            if (Accounts == null || Accounts.Count == 0 || Accounts.Count > 8)' '            if (Accounts == null || Accounts.Count == 0 || Accounts.Count > 2)'
    Replace-Once $reconnect '                throw new ArgumentException("Configure between 1 and 8 account profiles.");' '                throw new ArgumentException("Configure one or two account profiles on this PC.");'

    Replace-RegexOnce $reconnect '(?s)        public VanillaReconnectSettings Load\(\)\s*\{.*?\n        \}\n\n        public void Save' @'
        public VanillaReconnectSettings Load()
        {
            if (!File.Exists(path)) return VanillaReconnectSettings.CreateDefault();
            var value = JsonConvert.DeserializeObject<VanillaReconnectSettings>(File.ReadAllText(path),
                new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
            if (value == null) throw new InvalidDataException("Reconnect settings are empty.");
            value.NormalizeAccounts();
            value.Validate();
            return value;
        }

        public void Save
'@

    # Status is now an exact projection of the current settings list, so removed
    # rows cannot linger in the lower status pane.
    Replace-RegexOnce $reconnect '(?s)        public IReadOnlyList<VanillaReconnectStatus> Statuses\(\)\s*\{.*?\n        \}\n\n        public void Apply' @'
        public IReadOnlyList<VanillaReconnectStatus> Statuses()
        {
            lock (gate)
            {
                return settings.Accounts.Select(account =>
                {
                    Runtime runtime;
                    if (!runtimes.TryGetValue(account.Id, out runtime))
                        return new VanillaReconnectStatus { AccountId = account.Id, Label = account.Label, Stage = VanillaReconnectStage.Stopped,
                            VisualState = VanillaVisualState.Unknown, Detail = "No runtime state", UpdatedAt = DateTimeOffset.UtcNow };
                    return new VanillaReconnectStatus
                    {
                        AccountId = runtime.Account.Id, Label = runtime.Account.Label, ProcessId = runtime.ProcessId,
                        Stage = runtime.Stage, VisualState = runtime.Visual, Detail = runtime.Detail, UpdatedAt = runtime.StageAt
                    };
                }).ToList();
            }
        }

        public void Apply
'@

    Replace-Once $reconnect '                AdoptExistingClients();' '                AdoptExistingClients(true);'

    # Replace the old launcher-path helper with actual running-client detection.
    Replace-RegexOnce $reconnect '(?s)        public void DetectLauncherFromRunningClient\(\)\s*\{.*?\n        \}\n\n        private void Tick' @'
        public int DetectRunningClients()
        {
            int detected;
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(VanillaReconnectSupervisor));
                RebuildRuntimes();
                detected = AdoptExistingClients(running);
            }
            if (detected > 0) Log("Detected and assigned " + detected + " running Vanilla client(s) in account-list order.");
            RaiseUpdated();
            return detected;
        }

        public void RecordTestLog(string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) Log("TEST: " + text);
        }

        private void Tick
'@

    Replace-RegexOnce $reconnect '(?s)        private void AdoptExistingClients\(\)\s*\{.*?\n        \}\n\n        private List<Process> GetVanillaProcesses' @'
        private int AdoptExistingClients(bool supervise)
        {
            var existing = GetVanillaProcesses().OrderBy(p => SafeStart(p)).ToList();
            try
            {
                var aliveIds = new HashSet<int>(existing.Select(p => p.Id));
                var enabled = settings.Accounts.Where(a => a.Enabled).Take(settings.MaxClients).ToList();
                var claimed = new HashSet<int>();
                int assigned = 0;
                foreach (var account in enabled)
                {
                    Runtime runtime = runtimes[account.Id];
                    Process match = null;
                    if (runtime.ProcessId.HasValue && aliveIds.Contains(runtime.ProcessId.Value))
                        match = existing.FirstOrDefault(p => p.Id == runtime.ProcessId.Value);
                    if (match == null) match = existing.FirstOrDefault(p => !claimed.Contains(p.Id));
                    if (match == null) continue;
                    claimed.Add(match.Id);
                    assigned++;
                    runtime.ProcessId = match.Id;
                    runtime.ResumeSent = true;
                    runtime.ScriptRunning = false;
                    runtime.Visual = VanillaVisualState.Unknown;
                    runtime.LoginLikeSince = runtime.GameplaySince = null;
                    SetStage(runtime, supervise ? VanillaReconnectStage.WaitingForGameplay : VanillaReconnectStage.Stopped,
                        supervise ? "Existing Vanilla client adopted (PID " + match.Id + ")"
                            : "Running Vanilla client detected (PID " + match.Id + "); start supervisor to monitor");
                }
                return assigned;
            }
            finally { foreach (var process in existing) process.Dispose(); }
        }

        private List<Process> GetVanillaProcesses
'@

    # Recovery manager: larger, exactly two profiles, automatic detection, and
    # actual cold-start/restart tests plus an armed monitor for a manual network drop.
    Replace-Once $reconnect '        private readonly Label runState = new Label { AutoSize = true };' @'
        private readonly Label runState = new Label { AutoSize = true };
        private readonly Label testState = new Label { AutoSize = true, ForeColor = Color.DarkSlateBlue };
        private bool testRunning;
        private int testGeneration;
'@
    Replace-Once $reconnect '            Size = new Size(1000, 720);' '            Size = new Size(1180, 850);'
    Replace-Once $reconnect '            MinimumSize = new Size(850, 600);' '            MinimumSize = new Size(1050, 720);'
    Replace-Once $reconnect '            LoadFromSupervisor();' '            LoadFromSupervisor();
            try { supervisor.DetectRunningClients(); } catch { }'
    Replace-Once $reconnect '            pathRow.Controls.Add(new Label { Text = "Launcher EXE (patcher.exe recommended)", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });' '            pathRow.Controls.Add(new Label { Text = "Launcher EXE (Vanilla Launcher.exe / patcher.exe)", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });'
    Replace-Once $reconnect '            AddButton(pathRow, "Use patcher from running client", DetectPath);
' ''

    Replace-RegexOnce $reconnect '(?s)            var commands = Flow\(\);\s*            AddButton\(commands, "Save", Save\);.*?            top.Controls.Add\(commands\);' @'
            var commands = Flow();
            AddButton(commands, "Save", Save);
            AddButton(commands, "START SUPERVISOR", StartSupervisor);
            AddButton(commands, "STOP", () => supervisor.Stop());
            AddButton(commands, "DETECT RUNNING CLIENTS", DetectRunningClients);
            runState.Font = new Font(Font, FontStyle.Bold);
            runState.Margin = new Padding(16, 8, 0, 0);
            commands.Controls.Add(runState);
            top.Controls.Add(commands);

            var tests = Flow();
            AddButton(tests, "TEST STARTUP (clients closed)", TestStartup);
            AddButton(tests, "TEST RESTART RECOVERY", TestRestartRecovery);
            AddButton(tests, "ARM MANUAL NETWORK-DROP TEST", ArmManualNetworkDropTest);
            testState.Margin = new Padding(16, 8, 0, 0);
            tests.Controls.Add(testState);
            top.Controls.Add(tests);
'@
    Replace-Once $reconnect '                AutoSize = true, MaximumSize = new Size(930, 0),' '                AutoSize = true, MaximumSize = new Size(1100, 0),'
    Replace-Once $reconnect '            var statusBox = new GroupBox { Text = "Live recovery status", Dock = DockStyle.Fill, Padding = new Padding(8) };' '            var statusBox = new GroupBox { Text = "Live recovery status (configured account -> assigned PID, detected screen and recovery stage)", Dock = DockStyle.Fill, Padding = new Padding(8) };'
    Replace-Once $reconnect '            status.Columns.Add("Detail", 480);' '            status.Columns.Add("Detail", 610);'

    Replace-RegexOnce $reconnect '(?s)        private void DetectPath\(\)\s*\{.*?\n        \}\n\n        private void RefreshAccounts' @'
        private void DetectRunningClients()
        {
            try
            {
                ReadTop(); supervisor.Apply(settings, true); LoadFromSupervisor();
                int detected = supervisor.DetectRunningClients();
                RefreshStatus();
                MessageBox.Show(this, detected == 0 ? "No running Vanilla MMO clients were found."
                    : detected + " running Vanilla client(s) detected and assigned in account-list order.",
                    "Running clients", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Running clients", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void RefreshAccounts
'@

    Replace-RegexOnce $reconnect '        private void AddAccount\(\)\s*\{' @'
        private void AddAccount()
        {
            if (settings.Accounts.Count >= 2)
            {
                MessageBox.Show(this, "Vanilla allows two managed account profiles on this PC. Edit or remove an existing row first.");
                return;
            }
'@

    Replace-RegexOnce $reconnect '(?s)        private void ManualLogin\(\)\s*\{.*?\n        \}\n\n        private void SupervisorUpdated\(\)' @'
        private void ManualLogin()
        {
            var selected = SelectedAccount();
            if (selected == null) return;
            try { ReadTop(); supervisor.Apply(settings, true); supervisor.RunLoginNow(selected.Id); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Manual reconnect"); }
        }

        private VanillaReconnectAccount[] TestAccounts()
        {
            var configured = settings.Accounts.Where(a => a.Enabled).Take(settings.MaxClients).ToArray();
            if (configured.Length == 0) throw new InvalidOperationException("Enable at least one account first.");
            foreach (var account in configured)
                if (string.IsNullOrWhiteSpace(account.UserName) || string.IsNullOrWhiteSpace(account.ProtectedPassword))
                    throw new InvalidOperationException("Account '" + account.Label + "' needs username and password before an end-to-end test.");
            return configured;
        }

        private void TestStartup()
        {
            try
            {
                if (testRunning) throw new InvalidOperationException("A recovery test is already running.");
                ReadTop(); supervisor.Apply(settings, true); LoadFromSupervisor();
                var configured = TestAccounts();
                var live = Process.GetProcessesByName("Vanilla MMO");
                try
                {
                    if (live.Length > 0)
                    {
                        MessageBox.Show(this, "The cold-start test requires the Vanilla clients to be closed first. Close them, then press TEST STARTUP again.",
                            "Startup test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }
                finally { foreach (var process in live) process.Dispose(); }
                int generation = BeginTest("STARTUP TEST: launching " + configured.Length + " configured client(s)...");
                supervisor.Start();
                WaitForOnline(generation, "Startup test", configured.Select(a => a.Id).ToArray(), false, 300000);
            }
            catch (Exception ex) { FailTestImmediately("Startup test", ex); }
        }

        private void TestRestartRecovery()
        {
            try
            {
                if (testRunning) throw new InvalidOperationException("A recovery test is already running.");
                ReadTop(); supervisor.Apply(settings, true); LoadFromSupervisor();
                var configured = TestAccounts();
                if (!supervisor.IsRunning) supervisor.Start();
                supervisor.DetectRunningClients();
                var ids = configured.Select(a => a.Id).ToArray();
                var current = supervisor.Statuses().Where(s => ids.Contains(s.AccountId)).ToArray();
                if (current.Length != ids.Length || current.Any(s => !s.ProcessId.HasValue))
                    throw new InvalidOperationException("Every configured account must have a detected running Vanilla client before the restart-recovery test.");
                if (MessageBox.Show(this, "This test closes the detected Vanilla client windows normally and verifies that the supervisor launches, logs in and restores all configured clients. Continue?",
                    "Test restart recovery", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                int generation = BeginTest("RESTART TEST: closing clients and waiting for full relaunch...");
                WaitForOnline(generation, "Restart recovery test", ids, true, 300000);
                foreach (int pid in current.Select(s => s.ProcessId.Value))
                {
                    using (var process = Process.GetProcessById(pid))
                    {
                        if (!process.CloseMainWindow()) throw new InvalidOperationException("Vanilla PID " + pid + " did not accept a normal window-close request.");
                    }
                }
                supervisor.RecordTestLog("Restart recovery test requested normal close for " + current.Length + " Vanilla client(s).");
            }
            catch (Exception ex) { FailTestImmediately("Restart recovery test", ex); }
        }

        private void ArmManualNetworkDropTest()
        {
            try
            {
                if (testRunning) throw new InvalidOperationException("A recovery test is already running.");
                ReadTop(); supervisor.Apply(settings, true); LoadFromSupervisor();
                var configured = TestAccounts();
                if (!supervisor.IsRunning) supervisor.Start();
                supervisor.DetectRunningClients();
                var ids = configured.Select(a => a.Id).ToArray();
                var current = supervisor.Statuses().Where(s => ids.Contains(s.AccountId)).ToArray();
                if (current.Length != ids.Length || current.Any(s => !s.ProcessId.HasValue))
                    throw new InvalidOperationException("Every configured account must have a detected running Vanilla client before arming the network-drop test.");
                int generation = BeginTest("NETWORK-DROP TEST ARMED: briefly disconnect/reconnect internet now...");
                supervisor.RecordTestLog("Manual network-drop recovery test armed; waiting for a detected disconnect and return to Online.");
                WaitForOnline(generation, "Manual network-drop recovery test", ids, true, 300000);
                MessageBox.Show(this, "The test is armed for five minutes. Briefly disconnect your internet connection, wait long enough for Vanilla to drop to its login/reconnect state, then reconnect. 4RTools will report PASS only after it observes the disruption and all configured clients return Online.",
                    "Manual network-drop test armed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { FailTestImmediately("Manual network-drop test", ex); }
        }

        private int BeginTest(string text)
        {
            testRunning = true; testGeneration++; testState.Text = text; testState.ForeColor = Color.DarkSlateBlue; return testGeneration;
        }

        private void WaitForOnline(int generation, string testName, string[] accountIds, bool requireTransition, int timeoutMs)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool sawTransition = !requireTransition;
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    var sample = supervisor.Statuses().Where(s => accountIds.Contains(s.AccountId)).ToArray();
                    if (sample.Any(s => s.Stage != VanillaReconnectStage.Online)) sawTransition = true;
                    if (sample.Any(s => s.Stage == VanillaReconnectStage.Error || s.Stage == VanillaReconnectStage.NeedsConfiguration))
                    {
                        CompleteTest(generation, false, testName + " stopped: " + string.Join(" | ", sample.Select(s => s.Label + ": " + s.Detail)));
                        return;
                    }
                    if (sawTransition && sample.Length == accountIds.Length && sample.All(s => s.Stage == VanillaReconnectStage.Online && s.ProcessId.HasValue))
                    {
                        CompleteTest(generation, true, testName + " passed: all configured clients returned Online through the normal recovery path.");
                        return;
                    }
                    Thread.Sleep(500);
                }
                CompleteTest(generation, false, testName + " timed out. Check Live recovery status and the reconnect log for the exact stage that stopped progressing.");
            });
        }

        private void CompleteTest(int generation, bool success, string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)(() => CompleteTest(generation, success, message))); return; }
            if (generation != testGeneration) return;
            testRunning = false; testState.Text = success ? "TEST PASSED" : "TEST FAILED";
            testState.ForeColor = success ? Color.DarkGreen : Color.DarkRed;
            MessageBox.Show(this, message, "4RTools Vanilla test", MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void FailTestImmediately(string name, Exception ex)
        {
            testRunning = false; testState.Text = "TEST FAILED"; testState.ForeColor = Color.DarkRed;
            MessageBox.Show(this, ex.Message, name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SupervisorUpdated()
'@

    # The actual launcher in the user's install is "Vanilla Launcher.exe".
    $launcher = 'Model/Vanilla/VanillaPatcherLauncher.cs'
    Replace-RegexOnce $launcher '(?s)        internal static bool IsPatcher\(string executablePath\)\s*\{.*?\n        \}' @'
        internal static bool IsPatcher(string executablePath)
        {
            string name = Path.GetFileName(executablePath);
            return string.Equals(name, "patcher.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Vanilla Launcher.exe", StringComparison.OrdinalIgnoreCase);
        }
'@
    Replace-Once $launcher '                log?.Invoke("Started patcher.exe; waiting for GAME START to become usable.");' '                log?.Invoke("Started Vanilla launcher; waiting for GAME START to become usable.");'
    Replace-Once $launcher '                                log?.Invoke("Clicked patcher GAME START; waiting for Vanilla/Gepard startup.");' '                                log?.Invoke("Clicked launcher GAME START; waiting for Vanilla/Gepard startup.");'
    Replace-Once $launcher '                                log?.Invoke("Patcher window is not ready yet: " + ex.Message);' '                                log?.Invoke("Launcher window is not ready yet: " + ex.Message);'
    Replace-Once $launcher '                throw new TimeoutException("patcher.exe did not start a new Vanilla MMO client within " + (timeoutMs / 1000) + " seconds.");' '                throw new TimeoutException("Vanilla launcher did not start a new Vanilla MMO client within " + (timeoutMs / 1000) + " seconds.");'
    Replace-RegexOnce $launcher '(?s)            string patcher = Path.Combine\(directory, "patcher.exe"\);\s*            return File.Exists\(patcher\) \? patcher : clientExecutablePath;' @'
            string vanillaLauncher = Path.Combine(directory, "Vanilla Launcher.exe");
            if (File.Exists(vanillaLauncher)) return vanillaLauncher;
            string patcher = Path.Combine(directory, "patcher.exe");
            return File.Exists(patcher) ? patcher : clientExecutablePath;
'@

    # Main application: large/resizable and Vanilla is primary tab #1.
    $container = 'Forms/Container.cs'
    Replace-Once $container '            this.Text = "4RTools - Vanilla extension v0.4.0";' '            this.Text = "4RTools - Vanilla extension v0.5.0";
            ConfigureVanillaFirstLayout();'
    Replace-Once $container '                if (client.IsVanilla) atkDefMode.SelectedTab = atkDefMode.TabPages[atkDefMode.TabPages.Count - 1];' '                if (client.IsVanilla && atkDefMode.TabPages.ContainsKey("tabPageVanilla")) atkDefMode.SelectedTab = atkDefMode.TabPages["tabPageVanilla"];'
    Replace-Once $container '            var page = new TabPage("Vanilla");' '            var page = new TabPage("Vanilla") { Name = "tabPageVanilla" };'
    Replace-Once $container '            atkDefMode.TabPages.Add(page);' '            atkDefMode.TabPages.Insert(0, page);'
    Replace-Once $container '        private void SetBackGroundColorOfMDIForm()' @'
        private void ConfigureVanillaFirstLayout()
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = new Size(1050, 700);
            ClientSize = new Size(1180, 760);
            panelFooter.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            panelFooter.Location = new Point(0, ClientSize.Height - panelFooter.Height);
            panelFooter.Width = ClientSize.Width;
            lblLinkDiscord.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            panelDiscImage.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblLinkDiscord.Left = panelFooter.Width - lblLinkDiscord.Width - 16;
            panelDiscImage.Left = lblLinkDiscord.Left - panelDiscImage.Width - 8;
            atkDefMode.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            atkDefMode.Size = new Size(ClientSize.Width - 30, panelFooter.Top - atkDefMode.Top - 6);
            vanillaStatus.MaximumSize = new Size(1080, 0);
        }

        private void SetBackGroundColorOfMDIForm()
'@

    # Portable smoke test enforces the UI contract.
    $program = 'Program.cs'
    Replace-Once $program 'Success = true, Version = "0.4.0", PointerBytes = IntPtr.Size,' 'Success = true, Version = "0.5.0", PointerBytes = IntPtr.Size,'
    Replace-Once $program @'
                    if (form.AutomationEnabled || form.GameplayAttached || IntPtr.Size != 4)
                        throw new InvalidOperationException("Unexpected startup automation, game attachment, or process architecture.");
'@ @'
                    if (form.AutomationEnabled || form.GameplayAttached || IntPtr.Size != 4)
                        throw new InvalidOperationException("Unexpected startup automation, game attachment, or process architecture.");
                    var mainTabs = Descendants(form).OfType<System.Windows.Forms.TabControl>()
                        .OrderByDescending(tabs => tabs.TabPages.Count).FirstOrDefault();
                    if (mainTabs == null || mainTabs.TabPages.Count == 0 || mainTabs.TabPages[0].Text != "Vanilla")
                        throw new InvalidOperationException("Vanilla must be the first primary feature tab.");
                    if (form.ClientSize.Width < 1100 || form.ClientSize.Height < 700)
                        throw new InvalidOperationException("Main window is too small for the no-scroll Vanilla layout.");
'@

    # Offline regression suite for persistence/status/launcher behavior.
    $tests = @'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using _4RTools.Model.Vanilla;

namespace Vanilla.Diagnostics.Tests
{
    internal static class VanillaReconnectRegressionTests
    {
        private static int passed, failed;
        internal static int Run()
        {
            Test("Fresh reconnect settings contain exactly two defaults", FreshDefaults);
            Test("Reconnect settings clone never appends defaults", CloneDoesNotDuplicate);
            Test("Legacy duplicated settings keep the two configured accounts", LegacyDuplicateMigration);
            Test("Runtime status contains only current configured accounts", StatusTracksCurrentSettings);
            Test("Vanilla Launcher.exe uses GAME START launcher mode", VanillaLauncherName);
            Console.WriteLine("Reconnect regressions: {0} passed; {1} failed. No live process was controlled.", passed, failed);
            return failed;
        }
        private static void FreshDefaults()
        {
            string root = Temp();
            try { var value = new VanillaReconnectStore(root).Load(); Assert(value.Accounts.Count == 2, "Fresh settings need two rows."); }
            finally { Delete(root); }
        }
        private static void CloneDoesNotDuplicate()
        {
            var value = VanillaReconnectSettings.CreateDefault();
            value.Accounts[0].Label = "ender02"; value.Accounts[0].UserName = "ender02"; value.Accounts[0].ProtectedPassword = "encrypted-a";
            value.Accounts[1].Label = "slave"; value.Accounts[1].UserName = "ender03"; value.Accounts[1].ProtectedPassword = "encrypted-b";
            var clone = value.Clone();
            Assert(clone.Accounts.Count == 2 && clone.Accounts[0].Label == "ender02" && clone.Accounts[1].Label == "slave", "Clone changed or duplicated rows.");
        }
        private static void LegacyDuplicateMigration()
        {
            string root = Temp();
            try
            {
                var legacy = VanillaReconnectSettings.CreateDefault();
                legacy.Accounts.Add(new VanillaReconnectAccount { Label = "ender02", UserName = "ender02", ProtectedPassword = "encrypted-a", CharacterSlot = 4 });
                legacy.Accounts.Add(new VanillaReconnectAccount { Label = "slave", UserName = "ender03", ProtectedPassword = "encrypted-b", CharacterSlot = 1 });
                string dir = Path.Combine(root, "VanillaReconnect"); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "reconnect.json"), JsonConvert.SerializeObject(legacy, Formatting.Indented));
                var loaded = new VanillaReconnectStore(root).Load();
                Assert(loaded.Accounts.Count == 2 && loaded.Accounts[0].Label == "ender02" && loaded.Accounts[1].Label == "slave", "Configured rows did not win over synthetic defaults.");
            }
            finally { Delete(root); }
        }
        private static void StatusTracksCurrentSettings()
        {
            string root = Temp();
            try
            {
                using (var supervisor = new VanillaReconnectSupervisor(root))
                {
                    var value = supervisor.Settings; value.Accounts.RemoveAt(1); supervisor.Apply(value, false);
                    var rows = supervisor.Statuses(); Assert(rows.Count == 1 && rows[0].Label == "Client 1", "Removed row lingered in status.");
                }
            }
            finally { Delete(root); }
        }
        private static void VanillaLauncherName()
        {
            Type type = typeof(VanillaReconnectSettings).Assembly.GetType("_4RTools.Model.Vanilla.VanillaPatcherLauncher", true);
            MethodInfo method = type.GetMethod("IsPatcher", BindingFlags.Static | BindingFlags.NonPublic);
            Assert((bool)method.Invoke(null, new object[] { @"C:\Games\Vanilla RO\Vanilla Launcher.exe" }), "Vanilla Launcher.exe was not recognized.");
            Assert((bool)method.Invoke(null, new object[] { @"C:\Games\Vanilla RO\patcher.exe" }), "patcher.exe regressed.");
        }
        private static string Temp() { string p = Path.Combine(Path.GetTempPath(), "4rtools-reconnect-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
        private static void Delete(string p) { try { Directory.Delete(p, true); } catch { } }
        private static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); } }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    }
}
'@
    [IO.File]::WriteAllText('Tests/VanillaReconnectRegressionTests.cs', $tests, $utf8)
    Replace-Once 'Tests/Vanilla.Diagnostics.Tests.csproj' '<Compile Include="VanillaPatcherLauncherTests.cs" />' '<Compile Include="VanillaPatcherLauncherTests.cs" />
    <Compile Include="VanillaReconnectRegressionTests.cs" />'
    Replace-Once 'Tests/Program.cs' '            failed += VanillaPatcherLauncherTests.Run();' '            failed += VanillaPatcherLauncherTests.Run();
            failed += VanillaReconnectRegressionTests.Run();'
    Replace-Once 'Tests/VanillaPatcherLauncherTests.cs' '            Assert((bool)method.Invoke(null, new object[] { @"C:\Games\Vanilla RO\PATCHER.EXE" }), "Patcher detection must ignore case.");' '            Assert((bool)method.Invoke(null, new object[] { @"C:\Games\Vanilla RO\PATCHER.EXE" }), "Patcher detection must ignore case.");
            Assert((bool)method.Invoke(null, new object[] { @"C:\Games\Vanilla RO\Vanilla Launcher.exe" }), "Vanilla Launcher.exe must use GAME START launcher mode.");'

    # Version and documentation. Assembly version bump triggers the verified release workflow.
    Replace-Once 'Properties/AssemblyInfo.cs' '[assembly: AssemblyVersion("0.4.0.0")]' '[assembly: AssemblyVersion("0.5.0.0")]'
    Replace-Once 'Properties/AssemblyInfo.cs' '[assembly: AssemblyFileVersion("0.4.0.0")]' '[assembly: AssemblyFileVersion("0.5.0.0")]'

    $notesPath = 'RELEASE-NOTES.md'
    $notes = [IO.File]::ReadAllText($notesPath)
    $notes = $notes.Replace('# 4RTools Vanilla 0.4.0', '# 4RTools Vanilla 0.5.0')
    $marker = '## New in 0.4.0: patcher startup and GitHub Releases'
    if (-not $notes.Contains($marker)) { throw '0.4.0 release marker missing.' }
    $section = @'
## New in 0.5.0: recovery UX and repeatable end-to-end tests

The original 4RTools window is now large/resizable and **Vanilla is the first primary tab**, so the feature strip fits without the old horizontal tab scrolling. The reconnect manager is larger as well.

The account duplication bug is fixed at serialization level. A PC now has one or two reconnect profiles only; Save persists exactly the edited rows. Existing files containing duplicated blank `Client 1` / `Client 2` rows are normalized on load, preferring the configured credential-bearing rows. Live recovery status is an exact view of the current configured list, so deleted accounts disappear immediately. Its title explains that it shows account -> assigned PID -> detected screen -> current recovery stage.

Running Vanilla clients are detected automatically when the manager opens and when the supervisor starts; **DETECT RUNNING CLIENTS** provides an explicit refresh. Already-running clients are adopted without sending the Autobattle resume hotkey. With two anonymous existing processes, account-list order is the deterministic assignment order.

The launcher driver now recognizes the actual `Vanilla Launcher.exe` as well as `patcher.exe`, clicks the visible GAME START button, and then continues through the normal Gepard/client and login path. The obsolete `Use patcher from running client` shortcut was removed.

Testing is now built into the manager: **TEST STARTUP (clients closed)** validates the complete launcher/login/character/Autobattle path; **TEST RESTART RECOVERY** closes the detected game windows normally and validates full automatic relaunch/relogin; **ARM MANUAL NETWORK-DROP TEST** watches a real user-triggered connection interruption and reports PASS only after it observes a disruption and all configured clients return Online. 4RTools does not modify the PC network configuration for that test.

'@
    $notes = $notes.Replace($marker, $section + $marker)
    $notes = $notes.Replace('The intended 0.4.0 output is:', 'The intended 0.5.0 output is:')
    $notes = $notes.Replace('dist/4RTools-Vanilla-v0.4.0/', 'dist/4RTools-Vanilla-v0.5.0/')
    $notes = $notes.Replace('dist/4RTools-Vanilla-v0.4.0-portable.zip', 'dist/4RTools-Vanilla-v0.5.0-portable.zip')
    [IO.File]::WriteAllText($notesPath, $notes, $utf8)

    $readmePath = 'packaging/README.txt'
    $readme = [IO.File]::ReadAllText($readmePath)
    $readme = $readme.Replace('   running, choose "Use patcher from running client"; otherwise browse to the Vanilla', '   running, 4RTools detects it automatically; browse directly to the Vanilla')
    $readme = $readme.Replace('   executable/launcher that normally starts the protected client.', '   Launcher.exe (or patcher.exe) that normally starts the protected client.')
    [IO.File]::WriteAllText($readmePath, $readme, $utf8)

    Remove-Item 'scripts/one-time-v050.ps1'
    Remove-Item '.github/workflows/one-time-v050.yml'
}
finally { Pop-Location }

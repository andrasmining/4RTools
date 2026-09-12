Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
$nl = [Environment]::NewLine
function Read([string]$p) { [IO.File]::ReadAllText($p) }
function Write([string]$p,[string]$t) { [IO.File]::WriteAllText($p,$t,$utf8) }

$p='Model/Vanilla/VanillaReconnect.cs'
$t=Read $p
$t=$t.Replace('public double UserNameY { get; set; } = 0.66;','public double UserNameY { get; set; } = 0.635;')
$t=$t.Replace('public double PasswordY { get; set; } = 0.685;','public double PasswordY { get; set; } = 0.660;')

$old='            Accounts = unique;'+$nl+'        }'+$nl+$nl+'        private static bool IsSyntheticDefault'
if(-not $t.Contains($old)){throw 'NormalizeAccounts anchor missing'}
$new='            Accounts = unique;'+$nl+
'            if (Anchors == null) Anchors = new VanillaUiAnchors();'+$nl+
'            if (Math.Abs(Anchors.UserNameY - 0.66) < 0.0001 && Math.Abs(Anchors.PasswordY - 0.685) < 0.0001) { Anchors.UserNameY = 0.635; Anchors.PasswordY = 0.660; }'+$nl+
'        }'+$nl+$nl+'        private static bool IsSyntheticDefault'
$t=$t.Replace($old,$new)

$old='        public string SettingsPath { get { return store.FilePath; } }'
if(-not $t.Contains($old)){throw 'SettingsPath anchor missing'}
$t=$t.Replace($old,$old+$nl+'        public string LogPath { get { return Path.Combine(baseDirectory, "Logs", "reconnect.log"); } }'.Replace('\"','"'))

$old='        private readonly Label testState = new Label { AutoSize = true, ForeColor = Color.DarkSlateBlue };'
if(-not $t.Contains($old)){throw 'testState anchor missing'}
$t=$t.Replace($old,$old+$nl+'        private readonly ToolTip help = new ToolTip { InitialDelay = 650, ReshowDelay = 200, AutoPopDelay = 30000, ShowAlways = true };'+$nl+'        private bool exitRequested;')

$old='            BuildUi();'+$nl+'            supervisor.Updated += SupervisorUpdated;'
if(-not $t.Contains($old)){throw 'BuildUi anchor missing'}
$t=$t.Replace($old,'            BuildUi();'+$nl+'            ConfigureHoverHelp();'+$nl+'            supervisor.Updated += SupervisorUpdated;')

$old='            AddButton(commands, "DETECT RUNNING CLIENTS", DetectRunningClients);'.Replace('\"','"')+$nl+'            runState.Font'
if(-not $t.Contains($old)){throw 'commands anchor missing'}
$new='            AddButton(commands, "DETECT RUNNING CLIENTS", DetectRunningClients);'.Replace('\"','"')+$nl+
'            AddButton(commands, "OPEN LOG", OpenLog);'.Replace('\"','"')+$nl+
'            AddButton(commands, "COPY LOG", CopyLog);'.Replace('\"','"')+$nl+'            runState.Font'
$t=$t.Replace($old,$new)

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
$methods=$methods.Replace('\"','"')
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

$old='            menu.Items.Add("Stop reconnect supervisor", null, (s, e) => { supervisor?.Stop(); });'.Replace('\"','"')
if(-not $t.Contains($old)){throw 'tray menu anchor missing'}
$new=$old+$nl+'            menu.Items.Add(new ToolStripSeparator());'+$nl+'            menu.Items.Add("Exit 4RTools", null, (s, e) => Application.Exit());'.Replace('\"','"')
$t=$t.Replace($old,$new)
Write $p $t

$p='Forms/Container.cs';$t=Read $p;$t=$t.Replace('v0.5.0','v0.6.0');Write $p $t
$p='Properties/AssemblyInfo.cs';$t=Read $p;$t=$t.Replace('0.5.0.0','0.6.0.0');Write $p $t

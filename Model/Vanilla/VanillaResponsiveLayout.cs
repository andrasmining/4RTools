using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Responsive presentation layer for the integrated recovery workspace. Reuses the existing
    /// behavior/event wiring while keeping the visible surface compact and allowing smaller RDP
    /// desktops to scroll instead of clipping controls.
    /// </summary>
    internal sealed partial class VanillaReconnectForm
    {
        private const int WideRecoveryBreakpoint = 1250;
        private bool responsiveRecoveryHookInstalled;
        private bool responsiveRecoveryApplied;
        private TableLayoutPanel responsiveRoot;
        private TableLayoutPanel responsiveBottom;
        private GroupBox responsiveAccountBox;
        private GroupBox responsiveStatusBox;
        private GroupBox responsiveLogBox;
        private ContextMenuStrip responsiveTestsMenu;

        internal void ScheduleResponsiveRecoveryLayout()
        {
            if (responsiveRecoveryHookInstalled) return;
            responsiveRecoveryHookInstalled = true;

            Shown += (s, e) => BeginInvoke((MethodInvoker)InstallResponsiveRecoveryLayout);
            SizeChanged += (s, e) =>
            {
                if (!responsiveRecoveryApplied || IsDisposed) return;
                ApplyResponsiveHeights();
                ApplyResponsiveRecoveryBreakpoint();
                ResizeRecoveryColumns();
            };
        }

        internal static bool UseWideRecoveryLayout(int availableWidth)
        {
            return availableWidth >= WideRecoveryBreakpoint;
        }

        internal static int PreferredAccountsPanelHeight(int availableHeight)
        {
            if (availableHeight < 720) return 132;
            if (availableHeight < 860) return 148;
            if (availableHeight < 1000) return 164;
            return 178;
        }

        private void InstallResponsiveRecoveryLayout()
        {
            if (responsiveRecoveryApplied || IsDisposed) return;
            responsiveRoot = Controls.OfType<TableLayoutPanel>().FirstOrDefault();
            if (responsiveRoot == null) return;

            responsiveRecoveryApplied = true;
            SuspendLayout();
            responsiveRoot.SuspendLayout();
            try
            {
                responsiveRoot.Padding = new Padding(5);
                responsiveRoot.Margin = Padding.Empty;
                responsiveRoot.AutoScroll = true;
                responsiveRoot.AutoScrollMinSize = new Size(900, 440);
                responsiveRoot.RowStyles.Clear();
                responsiveRoot.RowCount = 4;
                responsiveRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                responsiveRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, PreferredAccountsPanelHeight(ClientSize.Height)));
                responsiveRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                responsiveRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));

                TableLayoutPanel oldTop = responsiveRoot.GetControlFromPosition(0, 0) as TableLayoutPanel;
                responsiveAccountBox = FindGroupBoxStarting(responsiveRoot, "Accounts");
                responsiveStatusBox = FindGroupBoxStarting(responsiveRoot, "Live recovery status");
                responsiveLogBox = FindGroupBoxStarting(responsiveRoot, "Reconnect log");

                if (oldTop != null) RebuildMinimalRecoveryHeader(oldTop);
                if (responsiveAccountBox != null)
                {
                    responsiveAccountBox.Text = "Accounts";
                    responsiveAccountBox.Padding = new Padding(5);
                    responsiveAccountBox.Margin = new Padding(0, 3, 0, 3);
                    help.SetToolTip(responsiveAccountBox,
                        "Up to two managed Vanilla accounts. Select Edit or double-click a row to change account name, username, DPAPI-protected password, character slot, proxy and resume hotkey.");
                }

                ConfigureAccountsGrid();
                ConfigureStatusList();
                ConfigureReconnectLog();
                BuildResponsiveRuntimeBottom();
                ApplyResponsiveHeights();
                ApplyResponsiveRecoveryBreakpoint();
                ResizeRecoveryColumns();
            }
            finally
            {
                responsiveRoot.ResumeLayout(true);
                ResumeLayout(true);
            }
        }

        private void RebuildMinimalRecoveryHeader(TableLayoutPanel top)
        {
            Button browse = FindButton(this, "Browse...") ?? FindButton(this, "Browseâ€¦");
            Button start = FindButton(this, "START SUPERVISOR");
            Button stop = FindButton(this, "STOP");
            GroupBox diagnostics = FindGroupBoxStarting(this, "Startup tests and selected-client diagnostics");
            Label info = FindLabelStarting(this, "Passwords are encrypted");

            // Preserve test status before detaching the old diagnostics group.
            if (testState.Parent != null) testState.Parent.Controls.Remove(testState);

            top.SuspendLayout();
            try
            {
                top.Controls.Clear();
                top.RowStyles.Clear();
                top.ColumnStyles.Clear();
                top.ColumnCount = 1;
                top.RowCount = 1;
                top.Dock = DockStyle.Top;
                top.AutoSize = true;
                top.Margin = Padding.Empty;
                top.Padding = Padding.Empty;
                top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

                var mainRow = CompactFlow();
                mainRow.WrapContents = true;
                mainRow.Controls.Add(new Label
                {
                    Text = "Launcher",
                    AutoSize = true,
                    Font = new Font(Font, FontStyle.Bold),
                    Margin = new Padding(0, 7, 5, 0)
                });

                launchPath.Dock = DockStyle.None;
                launchPath.Width = 300;
                launchPath.Margin = new Padding(0, 3, 4, 3);
                mainRow.Controls.Add(launchPath);
                if (browse != null)
                {
                    browse.AutoSize = true;
                    browse.Margin = new Padding(0, 2, 7, 2);
                    mainRow.Controls.Add(browse);
                }

                startWithApp.Text = "Start with 4RTools";
                autoRecover.Text = "Auto relog";
                visualWatchdog.Text = "Visual watchdog";
                startWithApp.Margin = new Padding(0, 7, 9, 0);
                autoRecover.Margin = new Padding(0, 7, 9, 0);
                visualWatchdog.Margin = new Padding(0, 7, 9, 0);
                mainRow.Controls.Add(startWithApp);
                mainRow.Controls.Add(autoRecover);
                mainRow.Controls.Add(visualWatchdog);

                if (start != null)
                {
                    start.Text = "START";
                    start.AutoSize = true;
                    start.Margin = new Padding(2);
                    mainRow.Controls.Add(start);
                    help.SetToolTip(start, "Start serialized recovery supervision. Missing clients are completed one at a time.");
                }
                if (stop != null)
                {
                    stop.AutoSize = true;
                    stop.Margin = new Padding(2);
                    mainRow.Controls.Add(stop);
                }

                Button tests = BuildTestsMenuButton();
                mainRow.Controls.Add(tests);

                runState.Margin = new Padding(7, 7, 0, 0);
                mainRow.Controls.Add(runState);
                testState.Margin = new Padding(7, 7, 0, 0);
                mainRow.Controls.Add(testState);
                top.Controls.Add(mainRow, 0, 0);

                if (diagnostics != null)
                {
                    diagnostics.Visible = false;
                    diagnostics.Height = 0;
                }
                if (info != null)
                {
                    info.Visible = false;
                    help.SetToolTip(accounts,
                        "Passwords are stored with Windows DPAPI for this Windows user and are never written to logs. Recovery uses ordinary Windows input and does not modify Gepard. Double-click a row to edit all account settings including proxy.");
                }
            }
            finally { top.ResumeLayout(true); }
        }

        private Button BuildTestsMenuButton()
        {
            if (responsiveTestsMenu != null)
            {
                try { responsiveTestsMenu.Dispose(); } catch { }
            }
            responsiveTestsMenu = new ContextMenuStrip();
            AddTestMenuItem("Test selected client", TestSelectedClientOnly);
            AddTestMenuItem("Test all clients", TestAllClientsKeepOpen);
            responsiveTestsMenu.Items.Add(new ToolStripSeparator());
            AddTestMenuItem("1 - Game start", () => RunSelectedStep(VanillaReconnectTestStep.LauncherGameStart));
            AddTestMenuItem("2 - Proxy", () => RunSelectedStep(VanillaReconnectTestStep.ProxySelection));
            AddTestMenuItem("3 - Fill user/password", () => RunSelectedStep(VanillaReconnectTestStep.FillCredentials));
            AddTestMenuItem("4 - Submit login", () => RunSelectedStep(VanillaReconnectTestStep.SubmitCredentials));
            AddTestMenuItem("5 - Server", () => RunSelectedStep(VanillaReconnectTestStep.SelectGameServer));
            AddTestMenuItem("6 - Character", () => RunSelectedStep(VanillaReconnectTestStep.SelectCharacter));
            AddTestMenuItem("7 - Resume hotkey", () => RunSelectedStep(VanillaReconnectTestStep.ResumeHotkey));
            responsiveTestsMenu.Items.Add(new ToolStripSeparator());
            AddTestMenuItem("Network-drop test", ArmManualNetworkDropTest);
            AddTestMenuItem("Stop current test", StopCurrentTest);

            var button = new Button { Text = "TESTS ▾", AutoSize = true, Margin = new Padding(2) };
            button.Click += (s, e) => responsiveTestsMenu.Show(button, new Point(0, button.Height));
            help.SetToolTip(button, "Startup/recovery diagnostics and individual startup-step tests.");
            Disposed += (s, e) =>
            {
                try { responsiveTestsMenu?.Dispose(); } catch { }
                responsiveTestsMenu = null;
            };
            return button;
        }

        private void AddTestMenuItem(string text, System.Action action)
        {
            responsiveTestsMenu.Items.Add(text, null, (s, e) => action());
        }

        private void ApplyResponsiveHeights()
        {
            if (responsiveRoot == null || responsiveRoot.RowStyles.Count < 2) return;
            responsiveRoot.RowStyles[1].SizeType = SizeType.Absolute;
            responsiveRoot.RowStyles[1].Height = PreferredAccountsPanelHeight(Math.Max(0, ClientSize.Height));
        }

        private void ConfigureAccountsGrid()
        {
            accounts.BorderStyle = BorderStyle.FixedSingle;
            accounts.BackgroundColor = SystemColors.Window;
            accounts.AllowUserToResizeRows = false;
            accounts.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            accounts.RowTemplate.Height = 24;
            accounts.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            accounts.ColumnHeadersHeight = 25;
            accounts.ScrollBars = ScrollBars.Vertical;
            accounts.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            foreach (DataGridViewColumn column in accounts.Columns)
            {
                column.MinimumWidth = 54;
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                switch (column.Name)
                {
                    case "Enabled": column.FillWeight = 7; column.MinimumWidth = 58; break;
                    case "Label": column.FillWeight = 15; break;
                    case "User": column.FillWeight = 18; break;
                    case "Slot": column.FillWeight = 8; column.MinimumWidth = 64; break;
                    case "Hotkey": column.FillWeight = 13; column.MinimumWidth = 90; break;
                    case "Secret": column.FillWeight = 11; column.MinimumWidth = 82; break;
                    case "AccountProxy": column.FillWeight = 11; column.MinimumWidth = 85; break;
                    default: column.FillWeight = 12; break;
                }
            }
        }

        private void ConfigureStatusList()
        {
            status.BorderStyle = BorderStyle.FixedSingle;
            status.HideSelection = false;
            status.FullRowSelect = true;
            status.SizeChanged += (s, e) => ResizeRecoveryColumns();
            if (responsiveStatusBox != null)
            {
                responsiveStatusBox.Text = "Status";
                responsiveStatusBox.Padding = new Padding(5);
                responsiveStatusBox.Margin = new Padding(0, 0, 4, 0);
                help.SetToolTip(responsiveStatusBox, "Current account/PID assignment, recovery stage, detected screen and detailed runtime state.");
            }
        }

        private void ConfigureReconnectLog()
        {
            log.Font = new Font("Consolas", 8.25F);
            log.WordWrap = false;
            log.ScrollBars = ScrollBars.Both;
            if (responsiveLogBox != null)
            {
                responsiveLogBox.Text = "Log";
                responsiveLogBox.Padding = new Padding(5);
                responsiveLogBox.Margin = new Padding(4, 0, 0, 0);
                help.SetToolTip(responsiveLogBox, "Current reconnect/startup events. The full application diagnostic bundle is available from COPY DEBUG LOG at the top.");
            }
        }

        private void BuildResponsiveRuntimeBottom()
        {
            if (responsiveRoot == null || responsiveStatusBox == null || responsiveLogBox == null) return;
            responsiveRoot.Controls.Remove(responsiveStatusBox);
            responsiveRoot.Controls.Remove(responsiveLogBox);

            responsiveBottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 0),
                Padding = Padding.Empty
            };
            responsiveRoot.Controls.Add(responsiveBottom, 0, 2);
        }

        private void ApplyResponsiveRecoveryBreakpoint()
        {
            if (responsiveRoot == null || responsiveBottom == null || responsiveStatusBox == null || responsiveLogBox == null) return;
            bool wide = UseWideRecoveryLayout(Math.Max(0, responsiveRoot.ClientSize.Width));

            responsiveBottom.SuspendLayout();
            try
            {
                responsiveBottom.Controls.Clear();
                responsiveBottom.ColumnStyles.Clear();
                responsiveBottom.RowStyles.Clear();
                if (wide)
                {
                    responsiveBottom.ColumnCount = 2;
                    responsiveBottom.RowCount = 1;
                    responsiveBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
                    responsiveBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
                    responsiveBottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                    responsiveStatusBox.Margin = new Padding(0, 0, 4, 0);
                    responsiveLogBox.Margin = new Padding(4, 0, 0, 0);
                    responsiveBottom.Controls.Add(responsiveStatusBox, 0, 0);
                    responsiveBottom.Controls.Add(responsiveLogBox, 1, 0);
                    responsiveRoot.AutoScrollMinSize = new Size(900, 440);
                }
                else
                {
                    responsiveBottom.ColumnCount = 1;
                    responsiveBottom.RowCount = 2;
                    responsiveBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    responsiveBottom.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
                    responsiveBottom.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
                    responsiveStatusBox.Margin = new Padding(0, 0, 0, 3);
                    responsiveLogBox.Margin = new Padding(0, 3, 0, 0);
                    responsiveBottom.Controls.Add(responsiveStatusBox, 0, 0);
                    responsiveBottom.Controls.Add(responsiveLogBox, 0, 1);
                    responsiveRoot.AutoScrollMinSize = new Size(900, 560);
                }
            }
            finally { responsiveBottom.ResumeLayout(true); }
        }

        private void ResizeRecoveryColumns()
        {
            if (status.Columns.Count < 5 || status.ClientSize.Width <= 0) return;
            int width = Math.Max(500, status.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);
            int account = Math.Max(100, (int)(width * 0.14));
            int pid = Math.Max(58, (int)(width * 0.08));
            int stage = Math.Max(110, (int)(width * 0.16));
            int screen = Math.Max(90, (int)(width * 0.13));
            int detail = Math.Max(170, width - account - pid - stage - screen - 6);
            status.Columns[0].Width = account;
            status.Columns[1].Width = pid;
            status.Columns[2].Width = stage;
            status.Columns[3].Width = screen;
            status.Columns[4].Width = detail;
        }

        private static FlowLayoutPanel CompactFlow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                Margin = Padding.Empty,
                Padding = new Padding(0, 1, 0, 1)
            };
        }

        private static GroupBox FindGroupBoxStarting(Control root, string prefix)
        {
            foreach (Control child in root.Controls)
            {
                GroupBox group = child as GroupBox;
                if (group != null && group.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return group;
                if (child.HasChildren)
                {
                    GroupBox nested = FindGroupBoxStarting(child, prefix);
                    if (nested != null) return nested;
                }
            }
            return null;
        }

        private static Label FindLabelStarting(Control root, string prefix)
        {
            foreach (Control child in root.Controls)
            {
                Label label = child as Label;
                if (label != null && label.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return label;
                if (child.HasChildren)
                {
                    Label nested = FindLabelStarting(child, prefix);
                    if (nested != null) return nested;
                }
            }
            return null;
        }
    }
}

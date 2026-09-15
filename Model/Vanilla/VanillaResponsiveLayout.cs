using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Minimal responsive recovery workspace. Full-HD uses a 2:1 split: launcher/actions/accounts
    /// on the left, reconnect log on the right. Runtime state is surfaced directly in the account
    /// table; the old standalone status panel is intentionally removed from the visible layout.
    /// </summary>
    internal sealed partial class VanillaReconnectForm
    {
        private const int WideRecoveryBreakpoint = 1100;
        private bool responsiveRecoveryHookInstalled;
        private bool responsiveRecoveryApplied;
        private TableLayoutPanel responsiveRoot;
        private TableLayoutPanel responsiveLeft;
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
                ApplyWorkspaceSplit();
                ResizeAccountColumns();
            };
        }

        internal static bool UseWideRecoveryLayout(int availableWidth)
        {
            return availableWidth >= WideRecoveryBreakpoint;
        }

        internal static int MinimumVisibleAccountRows(int accountCount)
        {
            // Four real profile rows plus one visually empty row is the minimum. More profiles
            // remain visible until the available panel height is exhausted; then the grid scrolls.
            return Math.Max(5, accountCount + 1);
        }

        // Retained for regression compatibility; the account area is no longer assigned a fixed
        // height. This is only the minimum space reserved for the table/buttons inside the left pane.
        internal static int PreferredAccountsPanelHeight(int availableHeight)
        {
            if (availableHeight < 620) return 170;
            if (availableHeight < 760) return 190;
            return 210;
        }

        private void InstallResponsiveRecoveryLayout()
        {
            if (responsiveRecoveryApplied || IsDisposed) return;
            responsiveRoot = Controls.OfType<TableLayoutPanel>().FirstOrDefault();
            if (responsiveRoot == null) return;

            TableLayoutPanel oldTop = responsiveRoot.GetControlFromPosition(0, 0) as TableLayoutPanel;
            responsiveAccountBox = FindGroupBoxStarting(responsiveRoot, "Accounts");
            responsiveStatusBox = FindGroupBoxStarting(responsiveRoot, "Live recovery status");
            responsiveLogBox = FindGroupBoxStarting(responsiveRoot, "Reconnect log");
            if (oldTop == null || responsiveAccountBox == null || responsiveLogBox == null) return;

            responsiveRecoveryApplied = true;
            SuspendLayout();
            responsiveRoot.SuspendLayout();
            try
            {
                RebuildMinimalRecoveryHeader(oldTop);
                ConfigureAccountsGrid();
                ConfigureReconnectLog();

                responsiveAccountBox.Text = "Accounts";
                responsiveAccountBox.Padding = new Padding(5);
                responsiveAccountBox.Margin = new Padding(0, 3, 0, 0);
                responsiveAccountBox.MinimumSize = new Size(0, PreferredAccountsPanelHeight(ClientSize.Height));
                help.SetToolTip(responsiveAccountBox,
                    "Saved account profiles. Any number may be stored; at most two may be enabled at once. Runtime PID/state appears in the same table. Double-click or Edit a row to change account details, proxy or hotkey.");

                if (responsiveStatusBox != null)
                {
                    responsiveStatusBox.Visible = false;
                    responsiveStatusBox.MinimumSize = Size.Empty;
                    responsiveStatusBox.MaximumSize = new Size(0, 0);
                    responsiveRoot.Controls.Remove(responsiveStatusBox);
                }

                responsiveLeft = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                    ColumnCount = 1,
                    RowCount = 2
                };
                responsiveLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                responsiveLeft.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                responsiveLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                responsiveRoot.Controls.Remove(oldTop);
                responsiveRoot.Controls.Remove(responsiveAccountBox);
                responsiveRoot.Controls.Remove(responsiveLogBox);
                responsiveLeft.Controls.Add(oldTop, 0, 0);
                responsiveLeft.Controls.Add(responsiveAccountBox, 0, 1);

                responsiveRoot.Controls.Clear();
                responsiveRoot.Padding = new Padding(5);
                responsiveRoot.Margin = Padding.Empty;
                responsiveRoot.AutoScroll = true;
                responsiveRoot.AutoScrollMinSize = new Size(820, 430);
                ApplyWorkspaceSplit();
                ResizeAccountColumns();
            }
            finally
            {
                responsiveRoot.ResumeLayout(true);
                ResumeLayout(true);
            }
        }

        private void ApplyWorkspaceSplit()
        {
            if (responsiveRoot == null || responsiveLeft == null || responsiveLogBox == null) return;
            bool wide = UseWideRecoveryLayout(Math.Max(0, responsiveRoot.ClientSize.Width));

            responsiveRoot.SuspendLayout();
            try
            {
                responsiveRoot.Controls.Clear();
                responsiveRoot.ColumnStyles.Clear();
                responsiveRoot.RowStyles.Clear();

                if (wide)
                {
                    // User-facing primary layout: two thirds for configuration/accounts, one third log.
                    responsiveRoot.ColumnCount = 2;
                    responsiveRoot.RowCount = 1;
                    responsiveRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66.6667F));
                    responsiveRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333F));
                    responsiveRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                    responsiveLeft.Margin = new Padding(0, 0, 4, 0);
                    responsiveLogBox.Margin = new Padding(4, 0, 0, 0);
                    responsiveRoot.Controls.Add(responsiveLeft, 0, 0);
                    responsiveRoot.Controls.Add(responsiveLogBox, 1, 0);
                    responsiveRoot.AutoScrollMinSize = new Size(820, 430);
                }
                else
                {
                    // On genuinely narrow windows preserve usability by stacking; scrolling is the fallback.
                    responsiveRoot.ColumnCount = 1;
                    responsiveRoot.RowCount = 2;
                    responsiveRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    responsiveRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 67F));
                    responsiveRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));
                    responsiveLeft.Margin = new Padding(0, 0, 0, 3);
                    responsiveLogBox.Margin = new Padding(0, 3, 0, 0);
                    responsiveRoot.Controls.Add(responsiveLeft, 0, 0);
                    responsiveRoot.Controls.Add(responsiveLogBox, 0, 1);
                    responsiveRoot.AutoScrollMinSize = new Size(760, 650);
                }
            }
            finally { responsiveRoot.ResumeLayout(true); }
        }

        private void RebuildMinimalRecoveryHeader(TableLayoutPanel top)
        {
            Button browse = FindButton(this, "Browse...") ?? FindButton(this, "Browseâ€¦");
            Button start = FindButton(this, "START SUPERVISOR");
            Button stop = FindButton(this, "STOP");
            GroupBox diagnostics = FindGroupBoxStarting(this, "Startup tests and selected-client diagnostics");
            Label info = FindLabelStarting(this, "Passwords are encrypted");

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
                top.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                top.Margin = Padding.Empty;
                top.Padding = Padding.Empty;
                top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

                FlowLayoutPanel mainRow = CompactFlow();
                mainRow.WrapContents = true;
                mainRow.Controls.Add(new Label
                {
                    Text = "Launcher",
                    AutoSize = true,
                    Font = new Font(Font, FontStyle.Bold),
                    Margin = new Padding(0, 6, 5, 0)
                });

                launchPath.Dock = DockStyle.None;
                launchPath.Width = 250;
                launchPath.Margin = new Padding(0, 2, 4, 2);
                mainRow.Controls.Add(launchPath);
                if (browse != null)
                {
                    browse.AutoSize = true;
                    browse.Margin = new Padding(0, 1, 6, 1);
                    mainRow.Controls.Add(browse);
                }

                startWithApp.Text = "Start with 4RTools";
                autoRecover.Text = "Auto relog";
                visualWatchdog.Text = "Visual watchdog";
                startWithApp.Margin = new Padding(0, 6, 7, 0);
                autoRecover.Margin = new Padding(0, 6, 7, 0);
                visualWatchdog.Margin = new Padding(0, 6, 7, 0);
                mainRow.Controls.Add(startWithApp);
                mainRow.Controls.Add(autoRecover);
                mainRow.Controls.Add(visualWatchdog);

                if (start != null)
                {
                    start.Text = "START";
                    start.AutoSize = true;
                    start.Margin = new Padding(1);
                    mainRow.Controls.Add(start);
                    help.SetToolTip(start, "Start serialized recovery supervision. Missing clients are completed one at a time.");
                }
                if (stop != null)
                {
                    stop.AutoSize = true;
                    stop.Margin = new Padding(1);
                    mainRow.Controls.Add(stop);
                }

                mainRow.Controls.Add(BuildTestsMenuButton());
                runState.Margin = new Padding(6, 6, 0, 0);
                mainRow.Controls.Add(runState);
                testState.Margin = new Padding(6, 6, 0, 0);
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
                        "Saved account profiles. Double-click to edit. Passwords are protected with Windows DPAPI and never written to logs. Runtime PID/state is shown in the same table.");
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

            var button = new Button { Text = "TESTS ▾", AutoSize = true, Margin = new Padding(1) };
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
            accounts.MinimumSize = new Size(0, 25 + 24 * 5 + 2); // header + at least four rows + one blank-row worth of air.
            ResizeAccountColumns();
        }

        private void ResizeAccountColumns()
        {
            if (accounts == null || accounts.IsDisposed || accounts.Columns.Count == 0) return;
            foreach (DataGridViewColumn column in accounts.Columns)
            {
                column.MinimumWidth = 48;
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                switch (column.Name)
                {
                    case "Enabled": column.FillWeight = 7; column.MinimumWidth = 55; break;
                    case "Label": column.FillWeight = 15; break;
                    case "User": column.FillWeight = 18; break;
                    case "Slot": column.FillWeight = 7; column.MinimumWidth = 56; break;
                    case "Hotkey": column.FillWeight = 11; column.MinimumWidth = 76; break;
                    case "Secret": column.FillWeight = 9; column.MinimumWidth = 70; break;
                    case "AccountProxy": column.FillWeight = 10; column.MinimumWidth = 72; break;
                    case "RuntimePid": column.FillWeight = 7; column.MinimumWidth = 58; break;
                    case "RuntimeStatus": column.FillWeight = 16; column.MinimumWidth = 105; break;
                    default: column.FillWeight = 10; break;
                }
            }
        }

        private void ConfigureReconnectLog()
        {
            log.Font = new Font("Consolas", 8.25F);
            log.WordWrap = false;
            log.ScrollBars = ScrollBars.Both;
            responsiveLogBox.Text = "Log";
            responsiveLogBox.Padding = new Padding(5);
            responsiveLogBox.Margin = new Padding(4, 0, 0, 0);
            help.SetToolTip(responsiveLogBox,
                "Current reconnect/startup events. The full application diagnostic bundle is available from COPY DEBUG LOG at the top.");
        }

        private static FlowLayoutPanel CompactFlow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = Padding.Empty,
                Padding = Padding.Empty
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

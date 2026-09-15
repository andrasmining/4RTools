using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Responsive presentation layer for the recovery workspace. The original reconnect form
    /// predates the integrated Vanilla shell and used a tall, fixed desktop-oriented layout.
    /// Recompose the existing controls at runtime so behavior/event wiring stays untouched while
    /// Full-HD/RDP windows use the available space efficiently and smaller windows can scroll.
    /// </summary>
    internal sealed partial class VanillaReconnectForm
    {
        private const int WideRecoveryBreakpoint = 1250;
        private bool responsiveRecoveryHookInstalled;
        private bool responsiveRecoveryApplied;
        private TableLayoutPanel responsiveRoot;
        private TableLayoutPanel responsiveBottom;
        private GroupBox responsiveStatusBox;
        private GroupBox responsiveLogBox;
        private Label responsiveInfo;

        internal void ScheduleResponsiveRecoveryLayout()
        {
            if (responsiveRecoveryHookInstalled) return;
            responsiveRecoveryHookInstalled = true;

            // ConfigureScopedTestUi runs after base.OnShown(), so defer our composition until the
            // current UI message has completed and all runtime-added test controls exist.
            Shown += (s, e) => BeginInvoke((MethodInvoker)InstallResponsiveRecoveryLayout);
            SizeChanged += (s, e) =>
            {
                if (!responsiveRecoveryApplied || IsDisposed) return;
                ApplyResponsiveRecoveryBreakpoint();
                ResizeRecoveryColumns();
                ResizeResponsiveInfo();
            };
        }

        internal static bool UseWideRecoveryLayout(int availableWidth)
        {
            return availableWidth >= WideRecoveryBreakpoint;
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
                responsiveRoot.Padding = new Padding(6);
                responsiveRoot.Margin = Padding.Empty;
                responsiveRoot.AutoScroll = true;
                responsiveRoot.AutoScrollMinSize = new Size(900, 520);
                responsiveRoot.RowStyles.Clear();
                responsiveRoot.RowCount = 4;
                responsiveRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                responsiveRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
                responsiveRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                responsiveRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));

                TableLayoutPanel oldTop = responsiveRoot.GetControlFromPosition(0, 0) as TableLayoutPanel;
                GroupBox accountBox = FindGroupBoxStarting(responsiveRoot, "Accounts on this PC");
                responsiveStatusBox = FindGroupBoxStarting(responsiveRoot, "Live recovery status");
                responsiveLogBox = FindGroupBoxStarting(responsiveRoot, "Reconnect log");

                if (oldTop != null) RebuildCompactRecoveryHeader(oldTop);
                if (accountBox != null)
                {
                    accountBox.Text = "Accounts (maximum 2 enabled clients)";
                    accountBox.Padding = new Padding(6);
                    accountBox.Margin = new Padding(0, 4, 0, 4);
                }

                ConfigureAccountsGrid();
                ConfigureStatusList();
                ConfigureReconnectLog();
                BuildResponsiveRuntimeBottom();
                ApplyResponsiveRecoveryBreakpoint();
                ResizeRecoveryColumns();
                ResizeResponsiveInfo();
            }
            finally
            {
                responsiveRoot.ResumeLayout(true);
                ResumeLayout(true);
            }
        }

        private void RebuildCompactRecoveryHeader(TableLayoutPanel top)
        {
            Button browse = FindButton(this, "Browse...") ?? FindButton(this, "Browseâ€¦");
            Button start = FindButton(this, "START SUPERVISOR");
            Button stop = FindButton(this, "STOP");
            Button network = FindButton(this, "ARM MANUAL NETWORK-DROP TEST");
            GroupBox diagnostics = FindGroupBoxStarting(this, "Startup tests and selected-client diagnostics");
            responsiveInfo = FindLabelStarting(this, "Passwords are encrypted");

            top.SuspendLayout();
            try
            {
                top.Controls.Clear();
                top.RowStyles.Clear();
                top.ColumnStyles.Clear();
                top.ColumnCount = 1;
                top.RowCount = 4;
                top.Dock = DockStyle.Top;
                top.AutoSize = true;
                top.Margin = Padding.Empty;
                top.Padding = Padding.Empty;
                for (int i = 0; i < 4; i++) top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

                var launcherRow = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    ColumnCount = 3,
                    RowCount = 1,
                    Margin = Padding.Empty,
                    Padding = new Padding(0, 1, 0, 1)
                };
                launcherRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                launcherRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                launcherRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                launcherRow.Controls.Add(new Label
                {
                    Text = "Launcher",
                    AutoSize = true,
                    Font = new Font(Font, FontStyle.Bold),
                    Margin = new Padding(0, 7, 8, 0)
                }, 0, 0);
                launchPath.Dock = DockStyle.Fill;
                launchPath.Width = 520;
                launchPath.Margin = new Padding(0, 3, 6, 3);
                launcherRow.Controls.Add(launchPath, 1, 0);
                if (browse != null)
                {
                    browse.AutoSize = true;
                    browse.Margin = new Padding(0, 2, 0, 2);
                    launcherRow.Controls.Add(browse, 2, 0);
                }
                top.Controls.Add(launcherRow, 0, 0);

                var supervisorRow = CompactFlow();
                startWithApp.Margin = new Padding(0, 7, 14, 0);
                autoRecover.Margin = new Padding(0, 7, 14, 0);
                visualWatchdog.Margin = new Padding(0, 7, 14, 0);
                supervisorRow.Controls.Add(startWithApp);
                supervisorRow.Controls.Add(autoRecover);
                supervisorRow.Controls.Add(visualWatchdog);
                if (start != null) supervisorRow.Controls.Add(start);
                if (stop != null) supervisorRow.Controls.Add(stop);
                runState.Margin = new Padding(12, 7, 0, 0);
                supervisorRow.Controls.Add(runState);
                top.Controls.Add(supervisorRow, 0, 1);

                if (diagnostics != null)
                {
                    diagnostics.Text = "Startup diagnostics";
                    diagnostics.Dock = DockStyle.Top;
                    diagnostics.Height = 62;
                    diagnostics.MinimumSize = new Size(0, 58);
                    diagnostics.Margin = new Padding(0, 3, 0, 3);
                    diagnostics.Padding = new Padding(5, 3, 5, 4);
                    FlowLayoutPanel diagnosticRow = diagnostics.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
                    if (diagnosticRow != null)
                    {
                        diagnosticRow.Dock = DockStyle.Fill;
                        diagnosticRow.AutoSize = false;
                        diagnosticRow.WrapContents = true;
                        diagnosticRow.Padding = Padding.Empty;
                        diagnosticRow.Margin = Padding.Empty;
                        if (network != null) diagnosticRow.Controls.Add(network);
                        testState.Margin = new Padding(10, 7, 0, 0);
                        diagnosticRow.Controls.Add(testState);
                    }
                    top.Controls.Add(diagnostics, 0, 2);
                }
                else
                {
                    var diagnosticFallback = CompactFlow();
                    if (network != null) diagnosticFallback.Controls.Add(network);
                    diagnosticFallback.Controls.Add(testState);
                    top.Controls.Add(diagnosticFallback, 0, 2);
                }

                if (responsiveInfo != null)
                {
                    responsiveInfo.Text = "Passwords use Windows DPAPI and are never logged. Recovery uses ordinary Windows input only and does not modify Gepard. Passwords must be entered once on each PC.";
                    responsiveInfo.Margin = new Padding(0, 2, 0, 2);
                    responsiveInfo.ForeColor = Color.DimGray;
                    responsiveInfo.AutoSize = true;
                    top.Controls.Add(responsiveInfo, 0, 3);
                }
            }
            finally { top.ResumeLayout(true); }
        }

        private void ConfigureAccountsGrid()
        {
            accounts.BorderStyle = BorderStyle.FixedSingle;
            accounts.BackgroundColor = SystemColors.Window;
            accounts.AllowUserToResizeRows = false;
            accounts.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            accounts.RowTemplate.Height = 22;
            accounts.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            accounts.ColumnHeadersHeight = 24;
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
                responsiveStatusBox.Text = "Live recovery status";
                responsiveStatusBox.Padding = new Padding(6);
                responsiveStatusBox.Margin = new Padding(0, 0, 4, 0);
            }
        }

        private void ConfigureReconnectLog()
        {
            log.Font = new Font("Consolas", 8.25F);
            log.WordWrap = false;
            log.ScrollBars = ScrollBars.Both;
            if (responsiveLogBox != null)
            {
                responsiveLogBox.Text = "Reconnect log";
                responsiveLogBox.Padding = new Padding(6);
                responsiveLogBox.Margin = new Padding(4, 0, 0, 0);
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
                    responsiveRoot.AutoScrollMinSize = new Size(900, 500);
                }
                else
                {
                    responsiveBottom.ColumnCount = 1;
                    responsiveBottom.RowCount = 2;
                    responsiveBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    responsiveBottom.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
                    responsiveBottom.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
                    responsiveStatusBox.Margin = new Padding(0, 0, 0, 3);
                    responsiveLogBox.Margin = new Padding(0, 3, 0, 0);
                    responsiveBottom.Controls.Add(responsiveStatusBox, 0, 0);
                    responsiveBottom.Controls.Add(responsiveLogBox, 0, 1);
                    responsiveRoot.AutoScrollMinSize = new Size(900, 610);
                }
            }
            finally { responsiveBottom.ResumeLayout(true); }
        }

        private void ResizeRecoveryColumns()
        {
            if (status.Columns.Count < 5 || status.ClientSize.Width <= 0) return;
            int width = Math.Max(500, status.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);
            int account = Math.Max(105, (int)(width * 0.14));
            int pid = Math.Max(62, (int)(width * 0.08));
            int stage = Math.Max(115, (int)(width * 0.16));
            int screen = Math.Max(95, (int)(width * 0.13));
            int detail = Math.Max(170, width - account - pid - stage - screen - 6);
            status.Columns[0].Width = account;
            status.Columns[1].Width = pid;
            status.Columns[2].Width = stage;
            status.Columns[3].Width = screen;
            status.Columns[4].Width = detail;
        }

        private void ResizeResponsiveInfo()
        {
            if (responsiveInfo == null || responsiveRoot == null) return;
            responsiveInfo.MaximumSize = new Size(Math.Max(420, responsiveRoot.ClientSize.Width - 20), 0);
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

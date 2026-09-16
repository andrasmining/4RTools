using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    // Presentation only. Reuses the existing controls/handlers, account catalog and supervisor.
    // Bounds are calculated from the viewport, not inherited TableLayoutPanel preferred sizes.
    // This avoids nested GrowOnly/percentage layouts retaining the former full-width grid.
    internal sealed partial class VanillaReconnectForm
    {
        private bool responsiveRecoveryHookInstalled;
        private bool responsiveRecoveryApplied;
        private bool arrangingRecovery;
        private Panel responsiveRoot;
        private Panel responsiveLeft;
        private FlowLayoutPanel responsiveHeader;
        private FlowLayoutPanel responsiveAccountButtons;
        private GroupBox responsiveAccountBox;
        private GroupBox responsiveLogBox;
        private ContextMenuStrip responsiveTestsMenu;

        internal void ScheduleResponsiveRecoveryLayout()
        {
            if (responsiveRecoveryHookInstalled) return;
            responsiveRecoveryHookInstalled = true;
            Shown += (s, e) => BeginInvoke((MethodInvoker)InstallResponsiveRecoveryLayout);
            SizeChanged += (s, e) => ArrangeRecoveryWorkspace();
            FontChanged += (s, e) => ArrangeRecoveryWorkspace();
        }

        internal static bool UseWideRecoveryLayout(int availableWidth) { return availableWidth >= 1100; }
        internal static int MinimumVisibleAccountRows(int accountCount) { return Math.Max(5, accountCount + 1); }
        internal static int PreferredAccountsPanelHeight(int availableHeight)
        {
            if (availableHeight < 620) return 170;
            if (availableHeight < 760) return 190;
            return 210;
        }

        private void InstallResponsiveRecoveryLayout()
        {
            if (responsiveRecoveryApplied || IsDisposed) return;
            TableLayoutPanel oldRoot = Controls.OfType<TableLayoutPanel>().FirstOrDefault();
            if (oldRoot == null) return;
            responsiveAccountBox = FindGroupBoxStarting(oldRoot, "Characters");
            responsiveLogBox = FindGroupBoxStarting(oldRoot, "Reconnect log");
            Button browse = FindButton(this, "Browse...") ?? FindButton(this, "Browseâ€¦");
            Button start = FindButton(this, "START SUPERVISOR");
            Button stop = FindButton(this, "STOP");
            Button add = FindButton(this, "Add"), edit = FindButton(this, "Edit"), remove = FindButton(this, "Remove");
            if (responsiveAccountBox == null || responsiveLogBox == null || browse == null || start == null || stop == null) return;

            SuspendLayout();
            try
            {
                AutoScroll = false;
                AutoScrollMinSize = Size.Empty;
                responsiveRoot = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, AutoScroll = true };
                responsiveLeft = new Panel { Margin = Padding.Empty };
                responsiveHeader = CompactFlow();
                responsiveAccountButtons = CompactFlow();
                BuildRecoveryHeader(browse, start, stop);

                // Retain hidden compatibility controls used by ReadTop and RefreshStatus, but
                // remove their old layout containers. They must not contribute preferred sizes.
                var hidden = new Panel { Visible = false, Size = Size.Empty };
                hidden.Controls.Add(launchArgs); hidden.Controls.Add(proxy); hidden.Controls.Add(maxClients); hidden.Controls.Add(status);
                responsiveRoot.Controls.Add(hidden);

                foreach (Button button in new[] { add, edit, remove })
                {
                    if (button == null) continue;
                    CompactButton(button);
                    responsiveAccountButtons.Controls.Add(button);
                }
                Control[] obsolete = responsiveAccountBox.Controls.Cast<Control>().ToArray();
                if (accounts.Parent != null) accounts.Parent.Controls.Remove(accounts);
                responsiveAccountBox.Controls.Clear();
                foreach (Control control in obsolete) control.Dispose();
                responsiveAccountBox.Controls.Add(accounts);
                responsiveAccountBox.Controls.Add(responsiveAccountButtons);
                responsiveAccountBox.Dock = DockStyle.None;
                responsiveAccountBox.MinimumSize = Size.Empty;
                responsiveAccountBox.Margin = Padding.Empty;
                responsiveAccountBox.Padding = new Padding(6);
                responsiveAccountBox.Text = "Characters";
                help.SetToolTip(responsiveAccountBox, "One row per character; at most two enabled at once. Double-click or Edit a row. Hover a clipped value or Status for the full detail.");
                ConfigureAccountsGrid();

                responsiveLogBox.Dock = DockStyle.None;
                responsiveLogBox.MinimumSize = Size.Empty;
                responsiveLogBox.Margin = Padding.Empty;
                responsiveLogBox.Padding = new Padding(6);
                responsiveLogBox.Text = "Log";
                log.WordWrap = true;
                log.ScrollBars = ScrollBars.Vertical;
                help.SetToolTip(log, "Reconnect/startup events. Lines wrap to this pane; COPY DEBUG LOG includes the complete diagnostic bundle.");

                responsiveLeft.Controls.Add(responsiveHeader);
                responsiveLeft.Controls.Add(responsiveAccountBox);
                responsiveRoot.Controls.Add(responsiveLeft);
                responsiveRoot.Controls.Add(responsiveLogBox);
                Controls.Remove(oldRoot);
                Controls.Add(responsiveRoot);
                oldRoot.Dispose();
                responsiveRecoveryApplied = true;
                responsiveRoot.Layout += (s, e) => ArrangeRecoveryWorkspace();
                runState.TextChanged += (s, e) => ArrangeRecoveryWorkspace();
                testState.TextChanged += (s, e) => ArrangeRecoveryWorkspace();
                accounts.RowsAdded += (s, e) => ArrangeRecoveryWorkspace();
                accounts.RowsRemoved += (s, e) => ArrangeRecoveryWorkspace();
            }
            finally { ResumeLayout(true); }
            ArrangeRecoveryWorkspace();
        }

        private void BuildRecoveryHeader(Button browse, Button start, Button stop)
        {
            var caption = new Label { Text = "Launcher", AutoSize = true, Margin = new Padding(0, 6, 4, 0) };
            responsiveHeader.Controls.Add(caption);
            launchPath.Dock = DockStyle.None;
            launchPath.MinimumSize = Size.Empty;
            launchPath.Margin = new Padding(0, 2, 4, 2);
            responsiveHeader.Controls.Add(launchPath);
            CompactButton(browse); responsiveHeader.Controls.Add(browse);
            startWithApp.Text = "Start with 4RTools";
            autoRecover.Text = "Auto relog";
            visualWatchdog.Text = "Visual watchdog";
            foreach (CheckBox check in new[] { startWithApp, autoRecover, visualWatchdog })
            {
                check.Dock = DockStyle.None;
                check.AutoSize = true;
                check.Margin = new Padding(4, 6, 4, 0);
                responsiveHeader.Controls.Add(check);
            }
            start.Text = "START";
            CompactButton(start); CompactButton(stop);
            responsiveHeader.Controls.Add(start); responsiveHeader.Controls.Add(stop);
            responsiveHeader.Controls.Add(BuildTestsMenuButton());
            runState.Margin = new Padding(5, 6, 0, 0);
            responsiveHeader.Controls.Add(runState);
            testState.AutoSize = false;
            testState.AutoEllipsis = true;
            testState.Margin = new Padding(5, 6, 0, 0);
            responsiveHeader.Controls.Add(testState);
        }

        private static void CompactButton(Button button)
        {
            button.Dock = DockStyle.None;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(54, 0);
            button.Margin = new Padding(2);
            button.Padding = new Padding(5, 0, 5, 0);
        }

        private void ArrangeRecoveryWorkspace()
        {
            if (!responsiveRecoveryApplied || arrangingRecovery || IsDisposed || responsiveRoot == null) return;
            arrangingRecovery = true;
            try
            {
                int gap = Math.Max(4, Font.Height / 3);
                int width = Math.Max(1, responsiveRoot.ClientSize.Width - gap * 2);
                int height = Math.Max(1, responsiveRoot.ClientSize.Height - gap * 2);
                float scale = Font.SizeInPoints / 9F;
                accounts.Font = Font;
                int rowHeight = Math.Max(24, Font.Height + 8);
                accounts.RowTemplate.Height = rowHeight;
                foreach (DataGridViewRow row in accounts.Rows)
                    if (row.Height != rowHeight) row.Height = rowHeight;
                accounts.ColumnHeadersHeight = Math.Max(26, Font.Height + 10);
                launchPath.Width = Math.Min(Math.Max(160, (int)(200 * scale)), Math.Max(160, width / 4));
                testState.Size = new Size(Math.Min(230, width / 3), Font.Height + 4);
                testState.Visible = !string.IsNullOrWhiteSpace(testState.Text);
                help.SetToolTip(testState, testState.Text);

                int minimumColumns = accounts.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).Sum(ColumnMinimumWidth)
                    + SystemInformation.VerticalScrollBarWidth + 20;
                int leftWidth = (width - gap) * 2 / 3;
                bool wide = UseWideRecoveryLayout(width) && leftWidth >= minimumColumns;
                if (!wide) leftWidth = width;
                int headerHeight = MeasureFlow(responsiveHeader, leftWidth);
                int buttonsHeight = MeasureFlow(responsiveAccountButtons, Math.Max(1, leftWidth - 16));
                int minimumGrid = accounts.ColumnHeadersHeight + rowHeight * Math.Min(5, MinimumVisibleAccountRows(accounts.Rows.Count)) + 4;
                int minimumLeft = headerHeight + gap + minimumGrid + buttonsHeight + Font.Height + 26;
                int minimumLog = Math.Max(116, Font.Height * 6);
                int needed = wide ? minimumLeft : minimumLeft + minimumLog + gap;
                Size scrollMinimum = new Size(0, needed + gap * 2);
                if (responsiveRoot.AutoScrollMinSize != scrollMinimum) responsiveRoot.AutoScrollMinSize = scrollMinimum;
                height = Math.Max(height, needed);
                int leftHeight = wide ? height : Math.Max(minimumLeft, height * 2 / 3);
                if (!wide) leftHeight = Math.Min(leftHeight, height - minimumLog - gap);
                Point origin = new Point(gap + responsiveRoot.AutoScrollPosition.X, gap + responsiveRoot.AutoScrollPosition.Y);
                Put(responsiveLeft, origin.X, origin.Y, leftWidth, leftHeight);
                Put(responsiveHeader, 0, 0, leftWidth, headerHeight);
                Put(responsiveAccountBox, 0, headerHeight + gap, leftWidth, Math.Max(1, leftHeight - headerHeight - gap));
                Rectangle inner = responsiveAccountBox.DisplayRectangle;
                buttonsHeight = MeasureFlow(responsiveAccountButtons, inner.Width);
                int gridHeight = Math.Max(1, inner.Height - buttonsHeight - gap);
                Put(accounts, inner.Left, inner.Top, inner.Width, gridHeight);
                Put(responsiveAccountButtons, inner.Left, inner.Top + gridHeight + gap, inner.Width, buttonsHeight);
                if (wide) Put(responsiveLogBox, origin.X + leftWidth + gap, origin.Y, width - leftWidth - gap, height);
                else Put(responsiveLogBox, origin.X, origin.Y + leftHeight + gap, width, height - leftHeight - gap);
                ResizeAccountColumns();
            }
            finally { arrangingRecovery = false; }
        }

        private static void Put(Control control, int x, int y, int width, int height)
        {
            Rectangle bounds = new Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));
            if (control.Bounds != bounds) control.Bounds = bounds;
        }

        private static int MeasureFlow(FlowLayoutPanel flow, int width)
        {
            flow.Width = Math.Max(1, width);
            flow.PerformLayout();
            return flow.Controls.Cast<Control>().Where(c => c.Visible)
                .Select(c => c.Bottom + c.Margin.Bottom).DefaultIfEmpty(0).Max() + flow.Padding.Bottom;
        }

        private void ConfigureAccountsGrid()
        {
            accounts.Dock = DockStyle.None;
            accounts.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            accounts.MinimumSize = Size.Empty;
            accounts.MaximumSize = Size.Empty;
            accounts.Margin = Padding.Empty;
            accounts.BorderStyle = BorderStyle.FixedSingle;
            accounts.BackgroundColor = SystemColors.Window;
            accounts.AllowUserToResizeRows = false;
            accounts.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            accounts.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            accounts.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            accounts.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            accounts.ScrollBars = ScrollBars.Both;
            accounts.ShowCellToolTips = true;
            accounts.Columns["Enabled"].HeaderText = "On";
            accounts.Columns["Slot"].HeaderText = "Slot";
            accounts.Columns["Hotkey"].HeaderText = "Resume";
            foreach (DataGridViewColumn column in accounts.Columns)
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.MinimumWidth = 24;
            }
            accounts.CellToolTipTextNeeded += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                DataGridViewCell cell = accounts.Rows[e.RowIndex].Cells[e.ColumnIndex];
                e.ToolTipText = string.IsNullOrEmpty(cell.ToolTipText) ? Convert.ToString(cell.Value) : cell.ToolTipText;
            };
        }

        private int ColumnMinimumWidth(DataGridViewColumn column)
        {
            int standard;
            switch (column.Name)
            {
                case "Enabled": standard = 34; break;
                case "Label": standard = 100; break;
                case "CharacterName": standard = 110; break;
                case "User": standard = 90; break;
                case "Slot": standard = 38; break;
                case "Hotkey": standard = 68; break;
                case "Secret": standard = 70; break;
                case "AccountProxy": standard = 68; break;
                case "RuntimePid": standard = 52; break;
                case "RuntimeStatus": standard = 100; break;
                default: standard = 60; break;
            }
            int textWidth = TextRenderer.MeasureText(column.HeaderText, accounts.Font, Size.Empty, TextFormatFlags.NoPadding).Width + 14;
            return Math.Max(textWidth, (int)Math.Ceiling(standard * Font.SizeInPoints / 9F));
        }

        private void ResizeAccountColumns()
        {
            if (accounts.IsDisposed || accounts.ClientSize.Width <= 0) return;
            DataGridViewColumn[] columns = accounts.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).ToArray();
            // ClientSize includes the managed scrollbar and the grid's painted frame. Reserve
            // both frame edges as well as the scrollbar, even before a long list makes it visible.
            // Otherwise the final Status column extends under the scrollbar by one pixel and
            // DataGridView adds a horizontal scrollbar, wasting another row of vertical space.
            int available = Math.Max(0, accounts.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            int[] widths = columns.Select(ColumnMinimumWidth).ToArray();
            int spare = Math.Max(0, available - widths.Sum());
            string[] flexible = { "Label", "User", "CharacterName", "RuntimeStatus" };
            int[] weights = columns.Select(c => flexible.Contains(c.Name) ? (c.Name == "RuntimeStatus" ? 2 : 3) : 0).ToArray();
            int totalWeight = weights.Sum();
            for (int i = 0; i < columns.Length; i++)
            {
                if (weights[i] > 0 && totalWeight > 0)
                {
                    int extra = spare * weights[i] / totalWeight;
                    widths[i] += extra;
                    spare -= extra;
                    totalWeight -= weights[i];
                }
                if (columns[i].Width != widths[i]) columns[i].Width = widths[i];
            }
            accounts.HorizontalScrollingOffset = 0;
        }

        private Button BuildTestsMenuButton()
        {
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
            var button = new Button { Text = "TESTS \u25BE" };
            CompactButton(button);
            button.Click += (s, e) => responsiveTestsMenu.Show(button, new Point(0, button.Height));
            help.SetToolTip(button, "Startup tests and individual recovery steps.");
            Disposed += (s, e) => responsiveTestsMenu.Dispose();
            return button;
        }

        private void AddTestMenuItem(string text, System.Action action)
        {
            responsiveTestsMenu.Items.Add(text, null, (s, e) => action());
        }

        private static FlowLayoutPanel CompactFlow()
        {
            return new FlowLayoutPanel { AutoSize = false, WrapContents = true, Margin = Padding.Empty, Padding = Padding.Empty, Size = new Size(1, 1) };
        }

        private static GroupBox FindGroupBoxStarting(Control root, string prefix)
        {
            foreach (Control child in root.Controls)
            {
                GroupBox group = child as GroupBox;
                if (group != null && group.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return group;
                GroupBox nested = child.HasChildren ? FindGroupBoxStarting(child, prefix) : null;
                if (nested != null) return nested;
            }
            return null;
        }
    }
}

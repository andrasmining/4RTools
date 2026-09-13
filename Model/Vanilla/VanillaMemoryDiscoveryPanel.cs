using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    public sealed class VanillaMemoryDiscoveryPanel : UserControl
    {
        private const int MaximumDisplayedCandidates = 5000;
        private readonly VanillaFleetMonitor fleetMonitor;
        private readonly ComboBox clients = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        private readonly ComboBox valueType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
        private readonly ComboBox scope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        private readonly TextBox exact = new TextBox { Width = 130 };
        private readonly Button baseline = MakeButton("CAPTURE BASELINE"), changed = MakeButton("CHANGED"), unchanged = MakeButton("UNCHANGED"),
            increased = MakeButton("INCREASED"), decreased = MakeButton("DECREASED"), exactScan = MakeButton("EXACT"), reset = MakeButton("RESET"),
            copy = MakeButton("COPY SELECTED CANDIDATE"), copyAll = MakeButton("COPY ALL CANDIDATES"), export = MakeButton("EXPORT CANDIDATES…");
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(1200, 0), ForeColor = Color.DimGray };
        private readonly DataGridView grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        private readonly Timer refreshTimer = new Timer { Interval = 2000 };
        private readonly Dictionary<int, string> stoppedClients = new Dictionary<int, string>();
        private VanillaMemoryDiscoverySession session;
        private bool busy, disposed;

        public VanillaMemoryDiscoveryPanel(VanillaFleetMonitor fleetMonitor)
        {
            this.fleetMonitor = fleetMonitor ?? throw new ArgumentNullException(nameof(fleetMonitor));
            Dock = DockStyle.Fill; BackColor = Color.White; AutoScroll = true;
            BuildLayout();
            foreach (VanillaMemoryScanValueType item in Enum.GetValues(typeof(VanillaMemoryScanValueType))) valueType.Items.Add(item);
            valueType.SelectedItem = VanillaMemoryScanValueType.UInt32;
            scope.Items.Add(new Choice<VanillaMemoryScanScope>(VanillaMemoryScanScope.MainModule, "Main module — stable offsets first"));
            scope.Items.Add(new Choice<VanillaMemoryScanScope>(VanillaMemoryScanScope.AllWritableMemory, "Writable memory below 2 GiB — heap"));
            scope.SelectedIndex = 0;
            WireEvents(); RefreshClients();
            refreshTimer.Tick += (s, e) => { if (!busy) RefreshClients(); };
            refreshTimer.Start();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), RowCount = 5, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(new Label { AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Text = "Memory finder — read-only state discovery" }, 0, 0);
            root.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(1200, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 5, 0, 10),
                Text = "Unknown state: Capture baseline → perform exactly one controlled in-game change → CHANGED/INCREASED/DECREASED → repeat and refine. Use UNCHANGED only after the list is already small. EXACT accepts decimal or 0x hex and can start a scan without a baseline. Main module is the best first pass for stable offsets. The writable-memory scope includes heap/state allocations below a conservative 2 GiB user-address limit; higher private memory is excluded. This tool never writes game memory or sends input."
            }, 0, 1);
            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            controls.Controls.Add(Caption("Client")); controls.Controls.Add(clients);
            var refresh = MakeButton("REFRESH"); refresh.Click += (s, e) => RefreshClients(); controls.Controls.Add(refresh);
            controls.Controls.Add(Caption("Type")); controls.Controls.Add(valueType);
            controls.Controls.Add(Caption("Scope")); controls.Controls.Add(scope);
            controls.Controls.Add(Caption("Exact value")); controls.Controls.Add(exact);
            root.Controls.Add(controls, 0, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 7, 0, 7) };
            foreach (Control control in new Control[] { baseline, changed, unchanged, increased, decreased, exactScan, reset, copy, copyAll, export }) actions.Controls.Add(control);
            actions.Controls.Add(status); root.Controls.Add(actions, 0, 3);
            foreach (string column in new[] { "Address", "Main module + offset", "Previous", "Current", "Delta" }) grid.Columns.Add(column, column);
            grid.Columns[0].FillWeight = 120; grid.Columns[1].FillWeight = 130;
            root.Controls.Add(grid, 0, 4); Controls.Add(root);
        }

        private void WireEvents()
        {
            baseline.Click += async (s, e) =>
            {
                VanillaMemoryScanValueType type = SelectedType();
                VanillaMemoryScanScope selectedScope = SelectedScope();
                await RunAsync("Capturing read-only baseline…", current =>
                {
                    current.CaptureBaseline(type, selectedScope); return "Baseline captured.";
                });
            };
            changed.Click += async (s, e) => await CompareSafelyAsync(VanillaMemoryScanComparison.Changed);
            unchanged.Click += async (s, e) => await CompareSafelyAsync(VanillaMemoryScanComparison.Unchanged);
            increased.Click += async (s, e) => await CompareSafelyAsync(VanillaMemoryScanComparison.Increased);
            decreased.Click += async (s, e) => await CompareSafelyAsync(VanillaMemoryScanComparison.Decreased);
            exactScan.Click += async (s, e) => await CompareSafelyAsync(VanillaMemoryScanComparison.Exact);
            reset.Click += (s, e) => Guard(() => { session?.Reset(); grid.Rows.Clear(); status.Text = "Reset. Capture a new baseline or run Exact."; SetButtons(true); });
            copy.Click += (s, e) => Guard(CopySelected);
            copyAll.Click += (s, e) => Guard(CopyAllCandidates);
            export.Click += (s, e) => Guard(ExportCandidates);
            clients.SelectedIndexChanged += (s, e) => ResetSession("Client changed; discovery state reset.");
            valueType.SelectedIndexChanged += (s, e) => { if (session != null && session.HasBaseline) ResetSession("Value type changed; capture a new baseline."); };
            scope.SelectedIndexChanged += (s, e) => { if (session != null && session.HasBaseline) ResetSession("Scope changed; capture a new baseline."); };
        }

        private async Task CompareSafelyAsync(VanillaMemoryScanComparison comparison)
        {
            try { await CompareAsync(comparison); }
            catch (Exception ex) { if (!disposed && !IsDisposed) status.Text = "Stopped: " + ex.Message; }
        }

        private async Task CompareAsync(VanillaMemoryScanComparison comparison)
        {
            long? value = comparison == VanillaMemoryScanComparison.Exact
                ? (long?)VanillaMemoryDiscoverySession.ParseSearchValue(SelectedType(), exact.Text) : null;
            VanillaMemoryScanValueType type = SelectedType();
            VanillaMemoryScanScope selectedScope = SelectedScope();
            await RunAsync("Reading candidates…", current =>
            {
                if (comparison == VanillaMemoryScanComparison.Exact && !current.HasBaseline)
                    current.StartExact(type, selectedScope, value.Value);
                else if (!current.HasComparison) current.FirstCompare(comparison, value);
                else current.Refine(comparison, value);
                return comparison + " filter complete.";
            });
        }

        private async Task RunAsync(string pending, Func<VanillaMemoryDiscoverySession, string> operation)
        {
            if (busy) return;
            busy = true; SetButtons(false); status.Text = pending;
            try
            {
                // Capture UI selection on the UI thread; the worker only uses its frozen session/options.
                VanillaMemoryDiscoverySession current = EnsureSession();
                string message = await Task.Run(() => operation(current));
                if (disposed || IsDisposed) return;
                RefreshGrid(); status.Text = message + " " + DescribeSession();
            }
            catch (Exception ex)
            {
                if (!disposed && !IsDisposed)
                {
                    if (session != null && session.IsStopped) stoppedClients[session.ProcessId] = session.LastError ?? ex.Message;
                    RefreshGrid();
                    status.Text = "Stopped: " + ex.Message;
                }
            }
            finally { busy = false; if (!disposed && !IsDisposed) SetButtons(true); }
        }

        private VanillaMemoryDiscoverySession EnsureSession()
        {
            ClientChoice choice = clients.SelectedItem as ClientChoice;
            if (choice == null) throw new InvalidOperationException("No live Vanilla client is selected.");
            string stopped;
            if (stoppedClients.TryGetValue(choice.ProcessId, out stopped))
                throw new InvalidOperationException("Discovery remains stopped for PID " + choice.ProcessId + ". " + stopped);
            if (session != null && session.ProcessId == choice.ProcessId) return session;
            session?.Dispose(); session = null;
            try { session = new VanillaMemoryDiscoverySession(choice.ProcessId); return session; }
            catch (Exception ex) { stoppedClients[choice.ProcessId] = ex.Message; throw; }
        }

        private void RefreshClients()
        {
            int selectedPid = (clients.SelectedItem as ClientChoice)?.ProcessId ?? 0;
            IReadOnlyList<VanillaFleetClientInfo> live;
            try { live = fleetMonitor.Poll().Take(2).ToArray(); }
            catch (Exception ex) { status.Text = "Client refresh failed: " + ex.Message; return; }
            ClientChoice[] replacement = live.Select(item => new ClientChoice(item.ProcessId, item.CharacterName, item.Build)).ToArray();
            bool same = replacement.Length == clients.Items.Count;
            if (same) for (int i = 0; i < replacement.Length; i++)
            {
                ClientChoice old = clients.Items[i] as ClientChoice;
                if (old == null || old.ProcessId != replacement[i].ProcessId || old.CharacterName != replacement[i].CharacterName) { same = false; break; }
            }
            if (same) return;
            clients.BeginUpdate(); clients.Items.Clear(); foreach (ClientChoice item in replacement) clients.Items.Add(item);
            int index = -1;
            for (int i = 0; i < clients.Items.Count; i++) if (((ClientChoice)clients.Items[i]).ProcessId == selectedPid) { index = i; break; }
            clients.SelectedIndex = index >= 0 ? index : (clients.Items.Count > 0 ? 0 : -1); clients.EndUpdate();
            if (selectedPid != 0 && index < 0) ResetSession("Selected client exited; discovery state reset.");
        }

        private void RefreshGrid()
        {
            grid.Rows.Clear(); if (session == null) return;
            foreach (VanillaMemoryCandidate candidate in session.Candidates.Take(MaximumDisplayedCandidates))
            {
                int row = grid.Rows.Add(candidate.AddressHex, candidate.ModuleOffsetHex,
                    candidate.PreviousValue.ToString(CultureInfo.InvariantCulture), candidate.CurrentValue.ToString(CultureInfo.InvariantCulture), candidate.Delta.ToString(CultureInfo.InvariantCulture));
                grid.Rows[row].Tag = candidate;
            }
        }

        private string DescribeSession()
        {
            if (session == null) return "No discovery session.";
            string shown = session.Candidates.Count > MaximumDisplayedCandidates ? " Showing first " + MaximumDisplayedCandidates + "." : string.Empty;
            return string.Format(CultureInfo.InvariantCulture, "PID {0} | {1} | {2:N0} baseline bytes | {3:N0} candidates.{4}", session.ProcessId, session.ValueType, session.BaselineBytes, session.Candidates.Count, shown);
        }

        private void CopySelected()
        {
            if (session == null) throw new InvalidOperationException("No discovery session exists.");
            if (grid.SelectedRows.Count == 0) throw new InvalidOperationException("Select a candidate row first.");
            VanillaMemoryCandidate candidate = grid.SelectedRows[0].Tag as VanillaMemoryCandidate;
            if (candidate == null) throw new InvalidOperationException("Selected row has no candidate data.");
            Clipboard.SetText(session.MappingSnippet(candidate));
            status.Text = "Candidate copied. It is intentionally marked unverified.";
        }

        private void CopyAllCandidates()
        {
            if (session == null) throw new InvalidOperationException("No discovery session exists.");
            if (session.Candidates.Count == 0) throw new InvalidOperationException("There are no candidate results to copy.");
            Clipboard.SetText(FormatCandidatesForClipboard(session.ProcessId, session.ValueType, session.Scope, session.Candidates));
            status.Text = session.Candidates.Count.ToString(CultureInfo.InvariantCulture) + " candidates copied to clipboard.";
        }

        internal static string FormatCandidatesForClipboard(int processId, VanillaMemoryScanValueType type, VanillaMemoryScanScope scanScope, IReadOnlyList<VanillaMemoryCandidate> candidates)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            var text = new StringBuilder();
            text.Append("PID=").Append(processId.ToString(CultureInfo.InvariantCulture))
                .Append("\tType=").Append(type)
                .Append("\tScope=").Append(scanScope)
                .Append("\tCandidates=").Append(candidates.Count.ToString(CultureInfo.InvariantCulture)).AppendLine();
            text.AppendLine("Address\tMain module + offset\tPrevious\tCurrent\tDelta");
            foreach (VanillaMemoryCandidate candidate in candidates)
            {
                text.Append(candidate.AddressHex).Append('\t')
                    .Append(candidate.ModuleOffsetHex).Append('\t')
                    .Append(candidate.PreviousValue.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(candidate.CurrentValue.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(candidate.Delta.ToString(CultureInfo.InvariantCulture)).AppendLine();
            }
            return text.ToString();
        }

        private void ExportCandidates()
        {
            if (session == null || (!session.HasBaseline && !session.IsStopped)) throw new InvalidOperationException("No discovery session exists.");
            using (var dialog = new SaveFileDialog { Filter = "JSON report (*.json)|*.json", FileName = "vanilla-memory-candidates-" + session.ProcessId + ".json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dialog.FileName, session.ExportReport()); status.Text = "Candidate report exported: " + dialog.FileName;
            }
        }

        private VanillaMemoryScanValueType SelectedType() { return valueType.SelectedItem is VanillaMemoryScanValueType ? (VanillaMemoryScanValueType)valueType.SelectedItem : VanillaMemoryScanValueType.UInt32; }
        private VanillaMemoryScanScope SelectedScope() { var choice = scope.SelectedItem as Choice<VanillaMemoryScanScope>; return choice == null ? VanillaMemoryScanScope.MainModule : choice.Value; }
        private void ResetSession(string message)
        {
            if (busy) return;
            session?.Dispose(); session = null; grid.Rows.Clear();
            ClientChoice choice = clients.SelectedItem as ClientChoice;
            string stopped;
            status.Text = choice != null && stoppedClients.TryGetValue(choice.ProcessId, out stopped)
                ? "Discovery remains stopped for PID " + choice.ProcessId + ". " + stopped : message;
            SetButtons(true);
        }
        private void SetButtons(bool enabled)
        {
            ClientChoice choice = clients.SelectedItem as ClientChoice;
            bool canRead = enabled && choice != null && !stoppedClients.ContainsKey(choice.ProcessId);
            baseline.Enabled = changed.Enabled = unchanged.Enabled = increased.Enabled = decreased.Enabled = exactScan.Enabled = reset.Enabled = copy.Enabled = canRead;
            copyAll.Enabled = canRead && session != null && session.Candidates.Count > 0;
            export.Enabled = enabled && session != null && (session.HasBaseline || session.IsStopped);
            clients.Enabled = valueType.Enabled = scope.Enabled = enabled;
        }
        private static Button MakeButton(string text) { return new Button { Text = text, AutoSize = true, Margin = new Padding(4) }; }
        private static Label Caption(string text) { return new Label { Text = text, AutoSize = true, Margin = new Padding(10, 8, 4, 0) }; }
        private void Guard(System.Action action) { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { disposed = true; refreshTimer.Stop(); refreshTimer.Dispose(); session?.Dispose(); session = null; }
            base.Dispose(disposing);
        }

        private sealed class ClientChoice
        {
            public int ProcessId { get; private set; }
            public string CharacterName { get; private set; }
            private readonly string build;
            public ClientChoice(int processId, string characterName, string build) { ProcessId = processId; CharacterName = string.IsNullOrWhiteSpace(characterName) ? "Vanilla MMO" : characterName; this.build = build; }
            public override string ToString() { return CharacterName + " — PID " + ProcessId + (string.IsNullOrWhiteSpace(build) ? string.Empty : " — " + build); }
        }
        private sealed class Choice<T>
        {
            public T Value { get; private set; }
            private readonly string label;
            public Choice(T value, string label) { Value = value; this.label = label; }
            public override string ToString() { return label; }
        }
    }
}

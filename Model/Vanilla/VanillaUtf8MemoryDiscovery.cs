using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using _4RTools.Utils;

namespace _4RTools.Model.Vanilla
{
    public sealed class VanillaUtf8MemoryCandidate
    {
        public ulong Address { get; internal set; }
        public ulong? MainModuleOffset { get; internal set; }
        public string PreviousText { get; internal set; }
        public string CurrentText { get; internal set; }
        public string AddressHex { get { return "0x" + Address.ToString("X8", CultureInfo.InvariantCulture); } }
        public string ModuleOffsetHex { get { return MainModuleOffset.HasValue ? "0x" + MainModuleOffset.Value.ToString("X", CultureInfo.InvariantCulture) : "—"; } }
    }

    /// <summary>
    /// Read-only UTF-8 finder for stable strings held in writable, non-executable sections of the
    /// verified Vanilla main image. It never writes target memory or sends input.
    /// </summary>
    public sealed class VanillaUtf8MemoryDiscoverySession : IDisposable
    {
        private const int MaximumCandidates = 25000;
        private const int MaximumTextBytes = 96;
        private const int ReadWindow = 256;
        private readonly ReadOnlyProcessMemory memory;
        private readonly VanillaExecutableIdentity identity;
        private readonly List<VanillaUtf8MemoryCandidate> candidates = new List<VanillaUtf8MemoryCandidate>();
        private bool disposed;

        public int ProcessId { get; private set; }
        public string ProcessName { get; private set; }
        public string ExecutablePath { get; private set; }
        public string ExecutableSha256 { get; private set; }
        public ulong MainModuleBaseAddress { get; private set; }
        public IReadOnlyList<VanillaUtf8MemoryCandidate> Candidates { get { return candidates.AsReadOnly(); } }
        public string CurrentSearchText { get; private set; }
        public bool HasComparison { get; private set; }
        public bool IsStopped { get; private set; }
        public string LastError { get; private set; }
        public DateTimeOffset? LastSampleAtUtc { get; private set; }

        public VanillaUtf8MemoryDiscoverySession(int processId)
        {
            ProcessId = processId;
            try
            {
                memory = new ReadOnlyProcessMemory(processId);
                if (!string.Equals(memory.ProcessName, "Vanilla MMO", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("UTF-8 discovery is restricted to Vanilla MMO.");
                if (memory.PointerSize != 4)
                    throw new InvalidOperationException("UTF-8 discovery currently supports the verified 32-bit Vanilla client only.");
                ProcessName = memory.ProcessName;
                ExecutablePath = memory.ExecutablePath;
                MainModuleBaseAddress = memory.MainModuleBaseAddress;
                identity = VanillaExecutableIdentity.Read(ExecutablePath);
                ExecutableSha256 = identity.Sha256;
            }
            catch
            {
                memory?.Dispose();
                throw;
            }
        }

        public void SearchExact(string text)
        {
            ThrowIfUnavailable();
            byte[] pattern = EncodeSearchText(text);
            if (HasComparison)
            {
                if (candidates.Count == 0) throw new InvalidOperationException("There are no UTF-8 candidates to refine. Reset and start a new text search.");
                Refine(pattern, text);
            }
            else
            {
                Start(pattern, text);
            }
        }

        private void Start(byte[] pattern, string text)
        {
            var found = new List<VanillaUtf8MemoryCandidate>();
            var seen = new HashSet<ulong>();
            try
            {
                foreach (VanillaImageSection section in identity.Sections.Where(IsWritableDataSection))
                {
                    ulong start = checked(MainModuleBaseAddress + section.Offset);
                    ulong end = checked(start + section.Size);
                    ulong cursor = start;
                    int overlap = pattern.Length - 1;
                    while (cursor < end)
                    {
                        int count = (int)Math.Min((ulong)ReadWindow, end - cursor);
                        byte[] bytes = memory.ReadBytes(cursor, count);
                        foreach (int offset in FindPatternOffsets(bytes, pattern))
                        {
                            ulong address = checked(cursor + (uint)offset);
                            if (!seen.Add(address)) continue;
                            if (found.Count >= MaximumCandidates)
                                throw new InvalidOperationException("More than 25,000 UTF-8 candidates matched. Use a longer/more specific text value.");
                            found.Add(NewCandidate(address, text, text));
                        }
                        if (count < ReadWindow) break;
                        cursor = checked(cursor + (uint)Math.Max(1, count - overlap));
                    }
                }
            }
            catch (Exception ex)
            {
                Stop(ex);
                throw;
            }
            candidates.Clear();
            candidates.AddRange(found.OrderBy(item => item.Address));
            CurrentSearchText = text;
            HasComparison = true;
            LastSampleAtUtc = DateTimeOffset.UtcNow;
        }

        private void Refine(byte[] pattern, string text)
        {
            var kept = new List<VanillaUtf8MemoryCandidate>();
            try
            {
                foreach (VanillaUtf8MemoryCandidate candidate in candidates)
                {
                    byte[] current = memory.ReadBytes(candidate.Address, pattern.Length);
                    if (!BytesEqual(current, pattern)) continue;
                    kept.Add(NewCandidate(candidate.Address, candidate.CurrentText, text));
                }
            }
            catch (Exception ex)
            {
                Stop(ex);
                throw;
            }
            candidates.Clear();
            candidates.AddRange(kept);
            CurrentSearchText = text;
            LastSampleAtUtc = DateTimeOffset.UtcNow;
        }

        public string MappingSnippet(VanillaUtf8MemoryCandidate candidate)
        {
            ThrowIfUnavailable();
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (!candidates.Contains(candidate)) throw new ArgumentException("Candidate is not from the current UTF-8 result set.", nameof(candidate));
            int textBytes = Encoding.UTF8.GetByteCount(candidate.CurrentText ?? string.Empty);
            var mapping = new Dictionary<string, object>();
            if (candidate.MainModuleOffset.HasValue)
            {
                mapping["Module"] = Path.GetFileName(ExecutablePath);
                mapping["Address"] = "0x" + candidate.MainModuleOffset.Value.ToString("X", CultureInfo.InvariantCulture);
            }
            else
            {
                mapping["Module"] = null;
                mapping["Address"] = candidate.AddressHex;
            }
            mapping["Encoding"] = "Utf8";
            mapping["ByteCount"] = Math.Max(16, Math.Min(128, textBytes + 1));
            mapping["Evidence"] = "DISCOVERY CANDIDATE ONLY — verify the text across map/state changes before adding to a build profile.";
            return JsonConvert.SerializeObject(mapping, Formatting.Indented);
        }

        public string CopyAllText()
        {
            ThrowIfDisposed();
            var text = new StringBuilder();
            text.Append("PID=").Append(ProcessId.ToString(CultureInfo.InvariantCulture))
                .Append("\tType=UTF8")
                .Append("\tCandidates=").Append(candidates.Count.ToString(CultureInfo.InvariantCulture))
                .Append("\tSearch=").Append(CurrentSearchText ?? string.Empty).AppendLine();
            text.AppendLine("Address\tMain module + offset\tPrevious text\tCurrent text");
            foreach (VanillaUtf8MemoryCandidate candidate in candidates)
            {
                text.Append(candidate.AddressHex).Append('\t')
                    .Append(candidate.ModuleOffsetHex).Append('\t')
                    .Append(candidate.PreviousText ?? string.Empty).Append('\t')
                    .Append(candidate.CurrentText ?? string.Empty).AppendLine();
            }
            return text.ToString();
        }

        public void Reset()
        {
            ThrowIfUnavailable();
            candidates.Clear();
            CurrentSearchText = null;
            HasComparison = false;
            LastSampleAtUtc = null;
        }

        internal static byte[] EncodeSearchText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Enter UTF-8 text to search for.");
            if (text.IndexOf('\0') >= 0) throw new ArgumentException("Search text cannot contain a NUL character.");
            byte[] bytes;
            try { bytes = new UTF8Encoding(false, true).GetBytes(text); }
            catch (EncoderFallbackException ex) { throw new ArgumentException("Search text is not valid UTF-8 input.", ex); }
            if (bytes.Length < 2) throw new ArgumentException("Use at least two UTF-8 bytes for a text search.");
            if (bytes.Length > MaximumTextBytes) throw new ArgumentException("UTF-8 search text is limited to " + MaximumTextBytes + " bytes.");
            return bytes;
        }

        internal static int[] FindPatternOffsets(byte[] bytes, byte[] pattern)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (pattern == null || pattern.Length == 0) throw new ArgumentException("Pattern is empty.", nameof(pattern));
            var matches = new List<int>();
            for (int offset = 0; offset + pattern.Length <= bytes.Length; offset++)
            {
                bool match = true;
                for (int index = 0; index < pattern.Length; index++)
                {
                    if (bytes[offset + index] == pattern[index]) continue;
                    match = false;
                    break;
                }
                if (match) matches.Add(offset);
            }
            return matches.ToArray();
        }

        private VanillaUtf8MemoryCandidate NewCandidate(ulong address, string previous, string current)
        {
            return new VanillaUtf8MemoryCandidate
            {
                Address = address,
                MainModuleOffset = address >= MainModuleBaseAddress && address < MainModuleBaseAddress + identity.ImageSize
                    ? (ulong?)(address - MainModuleBaseAddress) : null,
                PreviousText = previous,
                CurrentText = current
            };
        }

        private static bool IsWritableDataSection(VanillaImageSection section)
        {
            return section != null && section.Size > 0 && (section.Flags & 0x80000000) != 0 && (section.Flags & 0x20000000) == 0;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private void Stop(Exception ex)
        {
            if (IsStopped) return;
            LastError = ex.Message;
            IsStopped = true;
            candidates.Clear();
            memory.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(VanillaUtf8MemoryDiscoverySession));
        }

        private void ThrowIfUnavailable()
        {
            ThrowIfDisposed();
            if (IsStopped) throw new MemoryObservationException(LastError ?? "UTF-8 discovery session is stopped.");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            IsStopped = true;
            candidates.Clear();
            memory.Dispose();
        }
    }

    /// <summary>Small purpose-built UI for exact/refined UTF-8 discovery such as current map names.</summary>
    public sealed class VanillaUtf8MemoryDiscoveryPanel : UserControl
    {
        private const int MaximumDisplayedCandidates = 5000;
        private readonly VanillaFleetMonitor fleetMonitor;
        private readonly ComboBox clients = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        private readonly TextBox searchText = new TextBox { Width = 220 };
        private readonly Button search = MakeButton("TEXT EXACT / REFINE"), reset = MakeButton("RESET"),
            copy = MakeButton("COPY SELECTED"), copyAll = MakeButton("COPY ALL CANDIDATES");
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(1200, 0), ForeColor = Color.DimGray };
        private readonly DataGridView grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        private readonly Timer refreshTimer = new Timer { Interval = 2000 };
        private VanillaUtf8MemoryDiscoverySession session;
        private bool busy, disposed;

        public VanillaUtf8MemoryDiscoveryPanel(VanillaFleetMonitor fleetMonitor)
        {
            this.fleetMonitor = fleetMonitor ?? throw new ArgumentNullException(nameof(fleetMonitor));
            Dock = DockStyle.Fill;
            BackColor = Color.White;
            BuildLayout();
            WireEvents();
            RefreshClients();
            refreshTimer.Tick += (s, e) => { if (!busy) RefreshClients(); };
            refreshTimer.Start();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), RowCount = 4, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(new Label { AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Text = "UTF-8 text finder — read-only main-module strings" }, 0, 0);
            root.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(1200, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 5, 0, 10),
                Text = "Use this for stable strings such as map names. Enter the exact visible/internal text and search. Then change state (for example yuno_fild08 → yuno_fild02), enter the new text, and press TEXT EXACT / REFINE again. Refinement keeps only addresses whose contents changed to the new string. The scan is read-only and limited to writable, non-executable sections of Vanilla MMO.exe."
            }, 0, 1);
            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            controls.Controls.Add(Caption("Client")); controls.Controls.Add(clients);
            var refresh = MakeButton("REFRESH"); refresh.Click += (s, e) => RefreshClients(); controls.Controls.Add(refresh);
            controls.Controls.Add(Caption("UTF-8 text")); controls.Controls.Add(searchText);
            foreach (Control control in new Control[] { search, reset, copy, copyAll }) controls.Controls.Add(control);
            controls.Controls.Add(status);
            root.Controls.Add(controls, 0, 2);
            foreach (string column in new[] { "Address", "Main module + offset", "Previous text", "Current text" }) grid.Columns.Add(column, column);
            grid.Columns[0].FillWeight = 100; grid.Columns[1].FillWeight = 120;
            root.Controls.Add(grid, 0, 3);
            Controls.Add(root);
        }

        private void WireEvents()
        {
            search.Click += async (s, e) => await SearchAsync();
            reset.Click += (s, e) => Guard(() => { session?.Reset(); grid.Rows.Clear(); status.Text = "Reset. Enter text and start a new search."; SetButtons(true); });
            copy.Click += (s, e) => Guard(CopySelected);
            copyAll.Click += (s, e) => Guard(CopyAll);
            clients.SelectedIndexChanged += (s, e) => ResetSession("Client changed; text discovery reset.");
        }

        private async Task SearchAsync()
        {
            if (busy) return;
            string text = searchText.Text;
            busy = true; SetButtons(false); status.Text = "Searching UTF-8 text…";
            try
            {
                VanillaUtf8MemoryDiscoverySession current = EnsureSession();
                await Task.Run(() => current.SearchExact(text));
                if (disposed || IsDisposed) return;
                RefreshGrid();
                status.Text = string.Format(CultureInfo.InvariantCulture, "PID {0} | UTF-8 | {1:N0} candidates | current search: {2}",
                    current.ProcessId, current.Candidates.Count, current.CurrentSearchText);
            }
            catch (Exception ex)
            {
                if (!disposed && !IsDisposed) status.Text = "Stopped: " + ex.Message;
            }
            finally { busy = false; if (!disposed && !IsDisposed) SetButtons(true); }
        }

        private VanillaUtf8MemoryDiscoverySession EnsureSession()
        {
            ClientChoice choice = clients.SelectedItem as ClientChoice;
            if (choice == null) throw new InvalidOperationException("No live Vanilla client is selected.");
            if (session != null && session.ProcessId == choice.ProcessId && !session.IsStopped) return session;
            session?.Dispose();
            session = new VanillaUtf8MemoryDiscoverySession(choice.ProcessId);
            return session;
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
            clients.SelectedIndex = index >= 0 ? index : (clients.Items.Count > 0 ? 0 : -1);
            clients.EndUpdate();
            if (selectedPid != 0 && index < 0) ResetSession("Selected client exited; text discovery reset.");
        }

        private void RefreshGrid()
        {
            grid.Rows.Clear();
            if (session == null) return;
            foreach (VanillaUtf8MemoryCandidate candidate in session.Candidates.Take(MaximumDisplayedCandidates))
            {
                int row = grid.Rows.Add(candidate.AddressHex, candidate.ModuleOffsetHex, candidate.PreviousText, candidate.CurrentText);
                grid.Rows[row].Tag = candidate;
            }
        }

        private void CopySelected()
        {
            if (session == null) throw new InvalidOperationException("No UTF-8 discovery session exists.");
            if (grid.SelectedRows.Count == 0) throw new InvalidOperationException("Select a candidate row first.");
            VanillaUtf8MemoryCandidate candidate = grid.SelectedRows[0].Tag as VanillaUtf8MemoryCandidate;
            if (candidate == null) throw new InvalidOperationException("Selected row has no candidate data.");
            Clipboard.SetText(session.MappingSnippet(candidate));
            status.Text = "UTF-8 candidate copied. It remains unverified until confirmed in live state changes.";
        }

        private void CopyAll()
        {
            if (session == null || session.Candidates.Count == 0) throw new InvalidOperationException("There are no UTF-8 candidates to copy.");
            Clipboard.SetText(session.CopyAllText());
            status.Text = session.Candidates.Count.ToString(CultureInfo.InvariantCulture) + " UTF-8 candidates copied.";
        }

        private void ResetSession(string message)
        {
            if (busy) return;
            session?.Dispose(); session = null; grid.Rows.Clear(); status.Text = message; SetButtons(true);
        }

        private void SetButtons(bool enabled)
        {
            bool canRead = enabled && clients.SelectedItem != null;
            search.Enabled = reset.Enabled = canRead;
            copy.Enabled = copyAll.Enabled = canRead && session != null && session.Candidates.Count > 0;
            clients.Enabled = searchText.Enabled = enabled;
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
            public ClientChoice(int processId, string characterName, string build)
            {
                ProcessId = processId;
                CharacterName = string.IsNullOrWhiteSpace(characterName) ? "Vanilla MMO" : characterName;
                this.build = build;
            }
            public override string ToString() { return CharacterName + " — PID " + ProcessId + (string.IsNullOrWhiteSpace(build) ? string.Empty : " — " + build); }
        }
    }
}

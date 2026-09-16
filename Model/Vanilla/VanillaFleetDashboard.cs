using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using _4RTools.Model.Vanilla.Automation;
using _4RTools.Utils;

namespace _4RTools.Model.Vanilla
{
    public sealed class VanillaFleetClientInfo
    {
        public int ProcessId { get; internal set; }
        public string CharacterName { get; internal set; }
        public uint? CurrentHP { get; internal set; }
        public uint? MaxHP { get; internal set; }
        public uint? CurrentSP { get; internal set; }
        public uint? MaxSP { get; internal set; }
        public bool HpVerified { get; internal set; }
        public bool SpVerified { get; internal set; }
        public bool NameVerified { get; internal set; }
        public string Location { get; internal set; }
        public string Activity { get; internal set; }
        public string Build { get; internal set; }
        public string Error { get; internal set; }
        public bool Ready { get; internal set; }
        internal VanillaPositionSample Position { get; set; }
        internal VanillaCharacterIdentity Identity { get; set; }

        public decimal? HpPercent { get { return Percent(CurrentHP, MaxHP); } }
        public decimal? SpPercent { get { return Percent(CurrentSP, MaxSP); } }

        private static decimal? Percent(uint? current, uint? maximum)
        {
            if (!current.HasValue || !maximum.HasValue || maximum.Value == 0) return null;
            return Math.Max(0m, Math.Min(100m, current.Value * 100m / maximum.Value));
        }
    }

    /// <summary>
    /// Keeps lightweight read-only observations for the at-most-two live Vanilla clients.
    /// It never sends input. Each PID is fingerprinted and resolved through the audited build profile.
    /// </summary>
    public sealed partial class VanillaFleetMonitor : IDisposable
    {
        private readonly string baseDirectory;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Dictionary<int, IClientReader> readers = new Dictionary<int, IClientReader>();
        private readonly Func<IEnumerable<IProcessMetadata>> enumerateProcesses;
        private readonly Func<int, IClientReader> createReader;
        private readonly Func<ProcessObservationContext> observerContext;
        private readonly object gate = new object();
        private MemoryObservationException enumerationFailure;
        private bool disposed;
        private int pollCount;

        internal int PollCount { get { lock (gate) return pollCount; } }

        public VanillaFleetMonitor(string baseDirectory)
            : this(baseDirectory, EnumerateProcesses, null, () => ProcessObservationContext.Current)
        {
        }

        internal VanillaFleetMonitor(string baseDirectory, Func<IEnumerable<IProcessMetadata>> enumerateProcesses,
            Func<int, IClientReader> createReader, Func<ProcessObservationContext> observerContext)
        {
            this.baseDirectory = Path.GetFullPath(baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory)));
            this.enumerateProcesses = enumerateProcesses ?? throw new ArgumentNullException(nameof(enumerateProcesses));
            this.createReader = createReader ?? (pid => new Reader(this.baseDirectory, pid));
            this.observerContext = observerContext ?? throw new ArgumentNullException(nameof(observerContext));
        }

        public IReadOnlyList<VanillaFleetClientInfo> Poll()
        {
            lock (gate)
            {
                if (disposed) return new VanillaFleetClientInfo[0];
                pollCount++;
                if (enumerationFailure != null) throw enumerationFailure;
                var live = new List<int>();
                var enumerated = new HashSet<int>();
                foreach (IProcessMetadata process in EnumerateOrStop())
                {
                    using (process)
                    {
                        int pid = process.ProcessId;
                        if (!enumerated.Add(pid)) continue;

                        // Process.GetProcessesByName already gave us a current PID snapshot. Do not
                        // immediately ask System.Diagnostics for HasExited/MainWindowHandle: those
                        // convenience properties request process-query/synchronization access and can
                        // be denied by a protected Vanilla client before our actual VM_READ-only
                        // observation is even attempted. Let the read-only reader be the authority.
                        live.Add(pid);
                    }
                }
                live = live.Distinct().OrderBy(value => value).ToList();
                foreach (int dead in readers.Keys.Where(pid => !enumerated.Contains(pid)).ToArray())
                {
                    readers[dead].Dispose();
                    readers.Remove(dead);
                }
                foreach (int pid in live)
                {
                    if (readers.ContainsKey(pid)) continue;
                    try { readers.Add(pid, createReader(pid)); }
                    catch (Exception ex) { readers.Add(pid, Reader.Failed(pid, ex.Message)); }
                }
                var clients = live.Select(pid => readers[pid].Poll(clock.Elapsed)).ToArray();
                PublishPositions(clients);
                PublishCharacters(clients);
                return clients;
            }
        }

        private IProcessMetadata[] EnumerateOrStop()
        {
            var processes = new List<IProcessMetadata>();
            try
            {
                // Own every wrapper returned by the process enumeration snapshot.
                foreach (IProcessMetadata process in enumerateProcesses()) processes.Add(process);
                return processes.ToArray();
            }
            catch (Exception ex)
            {
                foreach (IProcessMetadata process in processes) process.Dispose();
                foreach (IClientReader reader in readers.Values) reader.Dispose();
                readers.Clear();
                System.Threading.Volatile.Write(ref characterCache, new VanillaCharacterIdentity[0]);
                const string operation = "Process.GetProcessesByName(Vanilla MMO)";
                int? nativeCode = (ex as Win32Exception)?.NativeErrorCode
                    ?? (ex as MemoryObservationException)?.NativeErrorCode;
                string details = nativeCode.HasValue
                    ? "Win32 " + nativeCode.Value + " (" + new Win32Exception(nativeCode.Value).Message + ")"
                    : ex.Message;
                enumerationFailure = new MemoryObservationException(operation + " failed: " + details + ". "
                    + observerContext() + " Fleet observation stopped; no further enumeration will be attempted.", nativeCode);
                throw enumerationFailure;
            }
        }

        public IReadOnlyList<int> LiveProcessIds()
        {
            return Poll().Select(item => item.ProcessId).ToArray();
        }

        internal interface IProcessMetadata : IDisposable
        {
            int ProcessId { get; }
            bool HasExited { get; }
            IntPtr MainWindowHandle { get; }
        }

        internal interface IClientReader : IDisposable
        {
            bool IsStopped { get; }
            VanillaFleetClientInfo Poll(TimeSpan now);
        }

        private static IEnumerable<IProcessMetadata> EnumerateProcesses()
        {
            return Process.GetProcessesByName("Vanilla MMO").Select(process => new ProcessMetadata(process)).ToArray();
        }

        private sealed class ProcessMetadata : IProcessMetadata
        {
            private readonly Process process;
            public ProcessMetadata(Process process) { this.process = process; }
            public int ProcessId { get { return process.Id; } }
            // Retained on the internal test abstraction for compatibility. Production polling no
            // longer calls either property because they require rights unrelated to memory reading.
            public bool HasExited { get { return process.HasExited; } }
            public IntPtr MainWindowHandle { get { return process.MainWindowHandle; } }
            public void Dispose() { process.Dispose(); }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                foreach (var reader in readers.Values) reader.Dispose();
                readers.Clear();
                System.Threading.Volatile.Write(ref characterCache, new VanillaCharacterIdentity[0]);
                clock.Stop();
            }
        }

        private sealed class Reader : IClientReader
        {
            private readonly int processId;
            private readonly MemoryStateSource source;
            private readonly VanillaStateAdapter adapter;
            private readonly string fingerprint, build;
            private string stoppedError;
            private bool disposed;
            private VanillaFleetClientInfo last;

            public bool IsStopped { get { return disposed || stoppedError != null || source?.IsStopped == true; } }

            public static Reader Failed(int processId, string error) { return new Reader(processId, error); }

            private Reader(int processId, string error)
            {
                this.processId = processId;
                stoppedError = error;
            }

            public Reader(string baseDirectory, int processId)
            {
                this.processId = processId;
                ReadOnlyProcessMemory memory = null;
                try
                {
                    memory = new ReadOnlyProcessMemory(processId);
                    if (!string.Equals(memory.ProcessName, "Vanilla MMO", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Selected process is not Vanilla MMO.");
                    if (memory.PointerSize != 4) throw new InvalidOperationException("Only the 32-bit Vanilla client is supported by the current build profile.");
                    var identity = VanillaExecutableIdentity.Read(memory.ExecutablePath);
                    fingerprint = identity.Sha256;
                    var profile = VanillaBuildProfile.Find(Path.Combine(baseDirectory, "VanillaBuilds"), identity, _ => { });
                    if (profile == null) throw new InvalidOperationException("No verified Vanilla build profile matches this executable.");
                    build = profile.Label;
                    adapter = new VanillaStateAdapter(profile, identity);
                    source = new MemoryStateSource(memory, profile.MemoryMap);
                    memory = null; // MemoryStateSource owns it now.
                }
                finally { memory?.Dispose(); }
            }

            public VanillaFleetClientInfo Poll(TimeSpan now)
            {
                if (disposed) return last ?? ErrorInfo("Observation closed.");
                if (stoppedError != null) return last = ErrorInfo(stoppedError);
                try
                {
                    var snapshot = source.Poll(DateTimeOffset.UtcNow);
                    snapshot.Fingerprint = fingerprint;
                    snapshot.BuildProfile = build;
                    var observation = adapter.Observe(snapshot, now);
                    if (source.IsStopped || snapshot.Error != null)
                    {
                        stoppedError = snapshot.Error ?? source.Status;
                        source.Dispose();
                        return last = ErrorInfo(stoppedError);
                    }
                    return last = BuildInfo(snapshot, observation);
                }
                catch (Exception ex)
                {
                    stoppedError = ex.Message;
                    source.Dispose();
                    return last = ErrorInfo(stoppedError);
                }
            }

            private VanillaFleetClientInfo BuildInfo(VanillaClientState state, RuleObservation observation)
            {
                string name = state.CharacterName.IsAvailable ? state.CharacterName.Value : "Unknown character";
                var location = BuildLocation(state);
                var activity = BuildActivity(state, observation);
                return new VanillaFleetClientInfo
                {
                    ProcessId = processId,
                    Position = new VanillaPositionSample(processId, state.SessionId, state.SampledAtUtc,
                        state.X.IsAvailable ? (int?)state.X.Value : null, state.Y.IsAvailable ? (int?)state.Y.Value : null,
                        state.Map.IsAvailable && state.Map.Validation == StateValidation.Valid ? state.Map.Value : null,
                        state.X.Validation == StateValidation.Valid && state.Y.Validation == StateValidation.Valid,
                        state.Error, state.LastMovementAtUtc),
                    CharacterName = name,
                    Identity = VanillaCharacterIdentity.FromState(state),
                    CurrentHP = state.CurrentHP.IsAvailable ? (uint?)state.CurrentHP.Value : null,
                    MaxHP = state.MaxHP.IsAvailable ? (uint?)state.MaxHP.Value : null,
                    CurrentSP = state.CurrentSP.IsAvailable ? (uint?)state.CurrentSP.Value : null,
                    MaxSP = state.MaxSP.IsAvailable ? (uint?)state.MaxSP.Value : null,
                    HpVerified = state.CurrentHP.Validation == StateValidation.Valid && state.MaxHP.Validation == StateValidation.Valid,
                    SpVerified = state.CurrentSP.Validation == StateValidation.Valid && state.MaxSP.Validation == StateValidation.Valid,
                    NameVerified = state.CharacterName.Validation == StateValidation.Valid,
                    Location = location,
                    Activity = activity,
                    Build = build,
                    Ready = observation.Ready,
                    Error = null
                };
            }

            private static string BuildLocation(VanillaClientState state)
            {
                bool map = state.Map.IsAvailable && state.Map.Validation == StateValidation.Valid;
                bool xy = state.X.IsAvailable && state.Y.IsAvailable
                    && state.X.Validation == StateValidation.Valid && state.Y.Validation == StateValidation.Valid;
                if (map && xy) return state.Map.Value + "  (" + state.X.Value + ", " + state.Y.Value + ")";
                if (map) return state.Map.Value;
                if (xy) return "(" + state.X.Value + ", " + state.Y.Value + ")";
                return "Location mapping pending";
            }

            private static string BuildActivity(VanillaClientState state, RuleObservation observation)
            {
                if (observation.Loading) return "Loading";
                if (observation.IsCasting == true) return "Casting";
                if (observation.InCombat == true) return "In combat";
                if (state.LastMovementAtUtc.HasValue && DateTimeOffset.UtcNow - state.LastMovementAtUtc.Value < TimeSpan.FromSeconds(2))
                    return "Moving";
                if (observation.HasTarget == true) return "Target acquired";
                if (observation.PositionValidated) return "Stationary";
                return "Activity mapping pending";
            }

            private VanillaFleetClientInfo ErrorInfo(string error)
            {
                return new VanillaFleetClientInfo
                {
                    ProcessId = processId,
                    CharacterName = "Vanilla MMO",
                    Location = "Unavailable",
                    Activity = "Observation unavailable",
                    Build = build,
                    Error = error,
                    Ready = false
                };
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                source?.Dispose();
            }
        }
    }

    /// <summary>Always-visible two-client status strip for the Vanilla-first workspace.</summary>
    public sealed class VanillaFleetDashboardPanel : UserControl
    {
        private readonly VanillaFleetMonitor monitor;
        private readonly bool observeClients;
        private readonly Timer timer = new Timer { Interval = 500 };
        private readonly ClientCard[] cards = { new ClientCard("Client 1"), new ClientCard("Client 2") };
        private readonly Label extra = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 4, 0, 0) };

        internal bool IsPolling { get { return timer.Enabled; } }

        public VanillaFleetDashboardPanel(VanillaFleetMonitor monitor, bool observeClients = true)
        {
            this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            this.observeClients = observeClients;
            Dock = DockStyle.Top;
            Height = 150;
            MinimumSize = new Size(600, 145);
            BackColor = Color.White;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(4) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(cards[0], 0, 0);
            root.Controls.Add(cards[1], 1, 0);
            root.Controls.Add(extra, 0, 1);
            root.SetColumnSpan(extra, 2);
            Controls.Add(root);
            timer.Tick += (s, e) => RefreshNow();
            if (observeClients) timer.Start();
            RefreshNow();
        }

        public void RefreshNow()
        {
            if (!observeClients)
            {
                SetText(extra, "Offline UI validation: live client observation is disabled.");
                return;
            }
            IReadOnlyList<VanillaFleetClientInfo> clients;
            try { clients = monitor.Poll(); }
            catch (Exception ex)
            {
                foreach (ClientCard card in cards) card.ShowObservationUnavailable(ex.Message);
                SetText(extra, "Live memory observation error: " + ex.Message);
                return;
            }
            for (int i = 0; i < cards.Length; i++) cards[i].ShowClient(i < clients.Count ? clients[i] : null);
            int unavailable = clients.Count(client => client.Error != null);
            if (unavailable > 0) SetText(extra, clients.Count + " Vanilla processes detected; observation stopped for " + unavailable
                + ". Hover over a client observation error for details.");
            else if (clients.Count <= 2) SetText(extra, clients.Count == 0
                ? "No Vanilla clients running. Recovery & relog can start the configured clients."
                : "Live values are read from the selected Vanilla build's read-only memory map. Location/activity appear automatically when those mappings are verified.");
            else SetText(extra, clients.Count + " Vanilla processes detected; the dashboard shows the first two only.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        private static void SetText(Label label, string text)
        {
            if (!string.Equals(label.Text, text, StringComparison.Ordinal)) label.Text = text;
        }

        private sealed class ClientCard : GroupBox
        {
            private readonly Label title = new Label { AutoSize = true, Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
            private readonly Label hp = new Label { AutoSize = true };
            private readonly Label sp = new Label { AutoSize = true };
            private readonly Label location = new Label { AutoSize = true, ForeColor = Color.DimGray };
            private readonly Label activity = new Label { AutoSize = true, ForeColor = Color.DimGray };
            private readonly StaticLevelBar hpBar = new StaticLevelBar { Height = 8, Dock = DockStyle.Top };
            private readonly StaticLevelBar spBar = new StaticLevelBar { Height = 8, Dock = DockStyle.Top };
            private readonly string emptyTitle;
            private readonly ToolTip errorTip = new ToolTip { AutoPopDelay = 30000 };

            public ClientCard(string emptyTitle)
            {
                this.emptyTitle = emptyTitle;
                Dock = DockStyle.Fill;
                Margin = new Padding(4);
                Padding = new Padding(10);
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                layout.Controls.Add(title, 0, 0); layout.SetColumnSpan(title, 2);
                layout.Controls.Add(hp, 0, 1); layout.Controls.Add(sp, 1, 1);
                layout.Controls.Add(hpBar, 0, 2); layout.Controls.Add(spBar, 1, 2);
                layout.Controls.Add(location, 0, 3); layout.SetColumnSpan(location, 2);
                layout.Controls.Add(activity, 0, 4); layout.SetColumnSpan(activity, 2);
                Controls.Add(layout);
                ShowClient(null);
            }

            public void ShowClient(VanillaFleetClientInfo info)
            {
                errorTip.SetToolTip(this, info?.Error);
                errorTip.SetToolTip(activity, info?.Error);
                if (info == null)
                {
                    if (!string.Equals(Text, emptyTitle, StringComparison.Ordinal)) Text = emptyTitle;
                    SetText(title, "Not running");
                    SetText(hp, "HP —");
                    SetText(sp, "SP —");
                    hpBar.Value = spBar.Value = 0;
                    SetText(location, "Location —");
                    SetText(activity, "Waiting for client");
                    return;
                }
                string caption = "PID " + info.ProcessId;
                if (!string.Equals(Text, caption, StringComparison.Ordinal)) Text = caption;
                SetText(title, info.CharacterName + (info.NameVerified ? "" : "  [unverified name]"));
                SetText(hp, "HP  " + Vital(info.CurrentHP, info.MaxHP) + (info.HpVerified ? "" : "  [unverified]"));
                SetText(sp, "SP  " + Vital(info.CurrentSP, info.MaxSP) + (info.SpVerified ? "" : "  [unverified]"));
                hpBar.Value = Clamp(info.HpPercent);
                spBar.Value = Clamp(info.SpPercent);
                SetText(location, "Location: " + info.Location);
                SetText(activity, info.Error == null ? "Activity: " + info.Activity : "Observation: " + info.Error);
            }

            public void ShowObservationUnavailable(string error)
            {
                ShowClient(null);
                SetText(title, "Status unavailable");
                SetText(activity, "Observation: " + error);
                errorTip.SetToolTip(this, error);
                errorTip.SetToolTip(activity, error);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) errorTip.Dispose();
                base.Dispose(disposing);
            }

            private static string Vital(uint? current, uint? maximum)
            {
                return current.HasValue && maximum.HasValue ? current.Value + " / " + maximum.Value : "Unavailable";
            }
            private static int Clamp(decimal? value) { return value.HasValue ? Math.Max(0, Math.Min(100, (int)Math.Round(value.Value))) : 0; }
        }

        private sealed class StaticLevelBar : Control
        {
            private int value;

            public StaticLevelBar()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                BackColor = Color.Gainsboro;
                ForeColor = Color.Green;
            }

            public int Value
            {
                get { return value; }
                set
                {
                    int next = Math.Max(0, Math.Min(100, value));
                    if (this.value == next) return;
                    this.value = next;
                    Invalidate();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.Clear(BackColor);
                int width = (int)Math.Round(ClientSize.Width * (value / 100d));
                if (width <= 0 || ClientSize.Height <= 0) return;
                using (var brush = new SolidBrush(ForeColor))
                    e.Graphics.FillRectangle(brush, 0, 0, Math.Min(width, ClientSize.Width), ClientSize.Height);
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;
using _4RTools.Model;
using _4RTools.Model.Vanilla;
using _4RTools.Utils;

namespace _4RTools.Forms
{
    public sealed class VanillaDiagnosticsForm : Form, IObserver
    {
        private readonly Subject subject;
        private readonly Timer timer = new Timer();
        private readonly ComboBox processes = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        private readonly NumericUpDown interval = new NumericUpDown { Minimum = 250, Maximum = 10000, Increment = 250, Width = 80 };
        private readonly TextBox mapEditor = new TextBox { Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 10) };
        private readonly Label connection = new Label { AutoSize = true, MaximumSize = new Size(1050, 0) };
        private readonly Label identity = new Label { AutoSize = true, MaximumSize = new Size(1050, 0) };
        private readonly TextBox note = new TextBox { Width = 300 };
        private readonly DataGridView values = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        private readonly TextBox events = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private IStateSource source;
        private VanillaClientState latest;
        private VanillaExecutableIdentity executable;
        private VanillaStateAdapter adapter;
        private string executablePath;
        private string buildProfile;

        public VanillaDiagnosticsForm(Subject subject = null)
        {
            this.subject = subject;
            subject?.Attach(this);
            Text = "Vanilla Automation — read-only diagnostics (local extension)";
            ClientSize = new Size(1100, 740);
            MinimumSize = new Size(820, 540);
            StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            controls.Controls.Add(processes);
            AddButton(controls, "Refresh processes", RefreshProcesses);
            AddButton(controls, "Connect read-only", Connect);
            AddButton(controls, "Offline demo", StartDemo);
            AddButton(controls, "Disconnect", () => Stop("Disconnected; observations cleared."));
            AddButton(controls, "Pause / resume", () =>
            {
                if (source == null || source.IsStopped) return;
                if (timer.Enabled) { timer.Stop(); connection.Text = "PAUSED — displayed observations are stale; no automation is attached."; Log("Polling paused."); }
                else { BeginPolling(); Log("Polling resumed."); }
            });
            controls.Controls.Add(new Label { Text = "Poll (ms)", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            controls.Controls.Add(interval);
            interval.ValueChanged += (s, e) => timer.Interval = (int)interval.Value;
            layout.Controls.Add(controls, 0, 0);
            layout.Controls.Add(connection, 0, 1);
            layout.Controls.Add(identity, 0, 2);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var stateTab = new TabPage("Observed state");
            foreach (string name in new[] { "Field", "Value", "Validation", "Address", "Last observed (UTC)", "Last changed (UTC)", "Evidence / error" })
                values.Columns.Add(name, name);
            stateTab.Controls.Add(values);
            tabs.TabPages.Add(stateTab);
            var mapTab = new TabPage("Advanced diagnostics");
            var mapLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            mapLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mapLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mapLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mapLayout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(1000, 0), Text = "No Vanilla addresses are supplied. Configured values are observations, not verified game signals. Changes apply on the next connection. No scanning or input is performed." }, 0, 0);
            mapLayout.Controls.Add(mapEditor, 0, 1);
            var mapButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            AddButton(mapButtons, "Load map…", LoadMap);
            AddButton(mapButtons, "Save settings to profile", SaveSettings);
            mapLayout.Controls.Add(mapButtons, 0, 2);
            mapTab.Controls.Add(mapLayout);
            tabs.TabPages.Add(mapTab);
            layout.Controls.Add(tabs, 0, 3);
            var logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var logButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            logButtons.Controls.Add(note);
            AddButton(logButtons, "Mark controlled action", () => { Log("Action note: " + note.Text); note.Clear(); });
            AddButton(logButtons, "Export current snapshot…", ExportSnapshot);
            AddButton(logButtons, "Export log…", () =>
            {
                using (var dialog = new SaveFileDialog { Filter = "Text log (*.txt)|*.txt", FileName = "vanilla-diagnostics.txt" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, events.Text);
            });
            AddButton(logButtons, "Clear log", () => events.Clear());
            logLayout.Controls.Add(logButtons, 0, 0);
            logLayout.Controls.Add(events, 0, 1);
            layout.Controls.Add(logLayout, 0, 4);
            Controls.Add(layout);
            timer.Tick += (s, e) => Poll();
            LoadSettings();
            RefreshProcesses();
            Stop("Disconnected. Use Offline demo without a game, or explicitly connect read-only. Automation is not enabled.");
        }

        private void AddButton(Control parent, string text, System.Action action)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (s, e) =>
            {
                try { action(); }
                catch (Exception ex) { connection.Text = ex.Message; Log(ex.ToString()); }
            };
            parent.Controls.Add(button);
        }

        private void RefreshProcesses()
        {
            processes.Items.Clear();
            foreach (Process process in Process.GetProcessesByName("Vanilla MMO"))
            {
                using (process) processes.Items.Add(new ProcessChoice(process.Id, process.ProcessName));
            }
            if (processes.Items.Count > 0) processes.SelectedIndex = 0;
        }

        public void StartDemo()
        {
            Stop("Starting offline demo.");
            source = new DemoStateSource();
            BeginPolling();
        }

        private void Connect()
        {
            Stop("Connecting read-only.");
            var choice = processes.SelectedItem as ProcessChoice;
            if (choice == null) throw new InvalidOperationException("No Vanilla MMO process selected. Start the game and refresh the list.");
            var configured = VanillaMemoryMap.Parse(mapEditor.Text);
            var memory = new ReadOnlyProcessMemory(choice.Id);
            try
            {
                executablePath = memory.ExecutablePath;
                executable = VanillaExecutableIdentity.Read(executablePath);
                var profile = VanillaBuildProfile.Find(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VanillaBuilds"), executable, Log);
                buildProfile = profile?.Label ?? "Unknown build";
                if (configured.Fields.Count == 0 && profile != null) configured = profile.MemoryMap;
                adapter = new VanillaStateAdapter(profile, executable);
                source = new MemoryStateSource(memory, configured);
            }
            catch { memory.Dispose(); throw; }
            BeginPolling();
        }

        private void BeginPolling()
        {
            timer.Interval = (int)interval.Value;
            Poll();
            if (source != null && !source.IsStopped) timer.Start();
        }

        private void Poll()
        {
            if (source == null) return;
            try
            {
                latest = source.Poll(DateTimeOffset.UtcNow);
                latest.ExecutablePath = executablePath;
                latest.Fingerprint = executable?.Sha256;
                latest.BuildProfile = buildProfile;
                adapter?.Observe(latest, TimeSpan.Zero);
                connection.Text = (latest.IsDemo ? "OFFLINE DEMO — simulated values. " : "LIVE READ-ONLY — see each field's validation. ") + source.Status;
                identity.Text = string.Format(CultureInfo.InvariantCulture, "{0} | PID: {1} | module: {2} | target pointer bytes: {3} | profile: {4}",
                    latest.ProcessName, latest.ProcessId, latest.ModuleBaseAddress.HasValue ? "0x" + latest.ModuleBaseAddress.Value.ToString("X8") : "—", latest.TargetPointerSize, ProfileSingleton.GetCurrent().Name);
                if (executable != null) identity.Text += " | SHA256: " + executable.Sha256 + " | " + executablePath;
                if (buildProfile != null) identity.Text += " | Build: " + buildProfile;
                values.Rows.Clear();
                foreach (var field in latest.Fields)
                {
                    StateValue observed = field.Value;
                    values.Rows.Add(field.Key, observed.IsAvailable ? FormatValue(observed.UntypedValue) : "Unavailable",
                        observed.Validation,
                        observed.Address.HasValue ? "0x" + observed.Address.Value.ToString("X", CultureInfo.InvariantCulture) : "—",
                        Time(observed.LastObservedAtUtc), Time(observed.LastChangedAtUtc), observed.Error ?? observed.Evidence);
                }
                if (source.IsStopped)
                {
                    timer.Stop();
                    Log(source.Status + " Polling stopped. No retry or alternate access is attempted.");
                }
            }
            catch (Exception ex)
            {
                Stop("Observation stopped: " + ex.Message);
                Log(ex.ToString());
            }
        }

        private static string FormatValue(object value)
        {
            var statuses = value as uint[];
            return statuses == null ? Convert.ToString(value, CultureInfo.InvariantCulture) : string.Join(", ", statuses);
        }

        private static string Time(DateTimeOffset? value) => value?.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "—";

        private void Stop(string status)
        {
            timer.Stop();
            source?.Dispose();
            source = null;
            latest = null;
            executable = null;
            adapter = null;
            executablePath = null;
            buildProfile = null;
            values.Rows.Clear();
            identity.Text = "No current observation.";
            connection.Text = status;
            Log(status);
        }

        private void LoadSettings()
        {
            var settings = ProfileSingleton.GetCurrent().VanillaDiagnostics;
            settings.Validate();
            interval.Value = settings.PollIntervalMilliseconds;
            mapEditor.Text = JsonConvert.SerializeObject(VanillaMemoryMap.Parse(settings.MemoryMapJson), Formatting.Indented);
        }

        private void SaveSettings()
        {
            var settings = new VanillaDiagnosticsSettings { PollIntervalMilliseconds = (int)interval.Value, MemoryMapJson = mapEditor.Text };
            ProfileSingleton.SetVanillaDiagnostics(settings);
            Log("Settings saved to profile " + ProfileSingleton.GetCurrent().Name + ".");
        }

        private void LoadMap()
        {
            using (var dialog = new OpenFileDialog { Filter = "JSON map (*.json)|*.json", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var map = VanillaMemoryMap.Load(dialog.FileName);
                mapEditor.Text = JsonConvert.SerializeObject(map, Formatting.Indented);
                Log("Loaded map " + dialog.FileName + ". Reconnect to apply.");
            }
        }

        private void ExportSnapshot()
        {
            if (latest == null) throw new InvalidOperationException("There is no current snapshot to export.");
            using (var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "vanilla-observation.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dialog.FileName, JsonConvert.SerializeObject(new { Executable = executable, ExecutablePath = executablePath, Paused = !timer.Enabled, Snapshot = latest, Notes = events.Text }, Formatting.Indented));
            }
        }

        private void Log(string message)
        {
            // Keep the live UI bounded; exports explicitly include only the retained notes.
            if (events.TextLength > 32000) events.Text = events.Text.Substring(events.TextLength - 16000);
            events.AppendText(DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine);
        }

        public void Update(ISubject sender)
        {
            var message = (sender as Subject)?.Message;
            if (message?.code == MessageCode.PROFILE_CHANGED)
            {
                Stop("Profile changed; observations cleared.");
                LoadSettings();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Stop();
                timer.Dispose();
                source?.Dispose();
                subject?.Detach(this);
            }
            base.Dispose(disposing);
        }

        private sealed class ProcessChoice
        {
            public int Id { get; }
            private readonly string name;
            public ProcessChoice(int id, string name) { Id = id; this.name = name; }
            public override string ToString() => name + ".exe — " + Id;
        }
    }
}

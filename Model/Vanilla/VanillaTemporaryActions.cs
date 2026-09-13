using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Newtonsoft.Json;
using _4RTools.Model.Vanilla.Automation;
using _4RTools.Utils;

namespace _4RTools.Model.Vanilla
{
    public sealed class VanillaTemporaryActionSettings
    {
        public int Version { get; set; } = 1;
        public int ActionKey { get; set; }
        public int IntervalMs { get; set; } = 1500;
        public bool ClickTargetAfterKey { get; set; }
        public decimal TargetXPercent { get; set; } = 50m;
        public decimal TargetYPercent { get; set; } = 50m;
        public int TargetClickDelayMs { get; set; } = 180;
        public bool SpRestEnabled { get; set; } = true;
        public decimal RestBelowPercent { get; set; } = 10m;
        public decimal ResumeAbovePercent { get; set; } = 80m;
        public int SitStandKey { get; set; } = (int)Keys.Insert;

        public void Validate()
        {
            if (Version != 1) throw new ArgumentException("Unsupported temporary-action settings version.");
            if (ActionKey != 0 && (ActionKey < 8 || ActionKey > 254)) throw new ArgumentException("Action key is invalid.");
            if (SitStandKey < 8 || SitStandKey > 254) throw new ArgumentException("Sit/stand key is invalid.");
            if (IntervalMs < 250 || IntervalMs > 3600000) throw new ArgumentException("Action interval must be between 0.25 seconds and one hour.");
            if (TargetClickDelayMs < 0 || TargetClickDelayMs > 5000) throw new ArgumentException("Target click delay must be between 0 and 5 seconds.");
            if (TargetXPercent < 0 || TargetXPercent > 100 || TargetYPercent < 0 || TargetYPercent > 100)
                throw new ArgumentException("Target position must be inside 0..100 percent of the selected client area.");
            if (RestBelowPercent <= 0 || RestBelowPercent >= 100 || ResumeAbovePercent <= 0 || ResumeAbovePercent > 100
                || ResumeAbovePercent <= RestBelowPercent)
                throw new ArgumentException("SP rest thresholds need 0 < rest-below < resume-above <= 100.");
        }

        public VanillaTemporaryActionSettings Clone()
        {
            Validate();
            return JsonConvert.DeserializeObject<VanillaTemporaryActionSettings>(JsonConvert.SerializeObject(this));
        }
    }

    /// <summary>
    /// A deliberately small, one-client temporary action state machine. Decisions use only verified
    /// read-only HP/SP/name state. Actions are ordinary window input bound to the selected PID.
    /// </summary>
    public sealed class VanillaTemporaryActionRunner : IDisposable
    {
        private readonly string baseDirectory;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private ReadOnlyProcessMemory memory;
        private MemoryStateSource source;
        private VanillaStateAdapter adapter;
        private VanillaWindowInput input;
        private VanillaTemporaryActionSettings settings = new VanillaTemporaryActionSettings();
        private VanillaClientState snapshot;
        private RuleObservation observation;
        private TimeSpan nextActionAt, pendingClickAt;
        private bool pendingClick, disposed;

        public bool Active { get; private set; }
        public bool Resting { get; private set; }
        public int ProcessId { get; private set; }
        public string CharacterName { get; private set; }
        public string Status { get; private set; } = "Stopped";
        public int CompletedActions { get; private set; }
        public decimal? SpPercent { get; private set; }

        public VanillaTemporaryActionRunner(string baseDirectory)
        {
            this.baseDirectory = Path.GetFullPath(baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory)));
        }

        public void Start(int processId, VanillaTemporaryActionSettings value)
        {
            Stop("Restarting temporary action");
            Connect(processId);
            settings = (value ?? throw new ArgumentNullException(nameof(value))).Clone();
            if (settings.ActionKey == 0) throw new InvalidOperationException("Choose the temporary action hotkey first.");
            Active = true;
            Resting = false;
            pendingClick = false;
            CompletedActions = 0;
            nextActionAt = clock.Elapsed;
            Status = "Running for " + CharacterName;
        }

        public void Tick()
        {
            if (!Active || disposed) return;
            try
            {
                PollVerifiedState();
                TimeSpan now = clock.Elapsed;
                if (settings.SpRestEnabled)
                {
                    decimal sp = SpPercent.Value;
                    if (!Resting && sp <= settings.RestBelowPercent)
                    {
                        SendKey(settings.SitStandKey);
                        Resting = true;
                        pendingClick = false;
                        Status = "Resting for SP: " + sp.ToString("0.0") + "%";
                        return;
                    }
                    if (Resting)
                    {
                        if (sp < settings.ResumeAbovePercent)
                        {
                            Status = "Resting for SP: " + sp.ToString("0.0") + "% / resume at " + settings.ResumeAbovePercent.ToString("0.#") + "%";
                            return;
                        }
                        SendKey(settings.SitStandKey);
                        Resting = false;
                        nextActionAt = now + TimeSpan.FromMilliseconds(500);
                        Status = "SP recovered; standing";
                        return;
                    }
                }

                if (pendingClick)
                {
                    if (now < pendingClickAt) return;
                    SendTargetClick();
                    pendingClick = false;
                    CompletedActions++;
                    nextActionAt = now + TimeSpan.FromMilliseconds(settings.IntervalMs);
                    Status = "Action " + CompletedActions + " completed | SP " + SpPercent.Value.ToString("0.0") + "%";
                    return;
                }
                if (now < nextActionAt) return;
                SendKey(settings.ActionKey);
                if (settings.ClickTargetAfterKey)
                {
                    pendingClick = true;
                    pendingClickAt = now + TimeSpan.FromMilliseconds(settings.TargetClickDelayMs);
                    Status = "Skill key sent; waiting to click target";
                }
                else
                {
                    CompletedActions++;
                    nextActionAt = now + TimeSpan.FromMilliseconds(settings.IntervalMs);
                    Status = "Action " + CompletedActions + " completed | SP " + SpPercent.Value.ToString("0.0") + "%";
                }
            }
            catch (Exception ex) { Stop("Stopped: " + ex.Message); }
        }

        public void Stop(string reason = "Stopped")
        {
            Active = false;
            pendingClick = false;
            Resting = false;
            Status = reason ?? "Stopped";
            try { input?.ReleaseAll(); } catch { }
            CloseConnection();
        }

        public static PointF CaptureTargetPercent(int processId)
        {
            using (var process = Process.GetProcessById(processId))
            {
                IntPtr window = process.MainWindowHandle;
                if (window == IntPtr.Zero) throw new InvalidOperationException("Selected Vanilla client has no main window.");
                RECT rect;
                if (!GetClientRect(window, out rect)) throw new InvalidOperationException("Could not read the selected Vanilla client area.");
                POINT cursor;
                if (!GetCursorPos(out cursor) || !ScreenToClient(window, ref cursor)) throw new InvalidOperationException("Could not map the mouse cursor into the selected client.");
                int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
                if (width <= 0 || height <= 0 || cursor.X < 0 || cursor.Y < 0 || cursor.X >= width || cursor.Y >= height)
                    throw new InvalidOperationException("Move the mouse over the target inside the selected Vanilla window, then capture again.");
                return new PointF(cursor.X * 100f / width, cursor.Y * 100f / height);
            }
        }

        private void Connect(int processId)
        {
            ReadOnlyProcessMemory opened = null;
            try
            {
                opened = new ReadOnlyProcessMemory(processId);
                if (!string.Equals(opened.ProcessName, "Vanilla MMO", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Select a Vanilla MMO process.");
                if (opened.PointerSize != 4) throw new InvalidOperationException("The current Vanilla build profile supports the 32-bit client only.");
                var identity = VanillaExecutableIdentity.Read(opened.ExecutablePath);
                var profile = VanillaBuildProfile.Find(Path.Combine(baseDirectory, "VanillaBuilds"), identity, _ => { });
                if (profile == null) throw new InvalidOperationException("This Vanilla executable has no matching verified build profile.");
                foreach (var field in new[] { VanillaField.CurrentHP, VanillaField.MaxHP, VanillaField.CurrentSP, VanillaField.MaxSP, VanillaField.CharacterName })
                    if (!profile.VerifiedFields.Contains(field)) throw new InvalidOperationException(field + " is not yet verified for this Vanilla build.");
                memory = opened;
                opened = null;
                source = new MemoryStateSource(memory, profile.MemoryMap);
                adapter = new VanillaStateAdapter(profile, identity);
                ProcessId = processId;
                PollVerifiedState();
                input = new VanillaWindowInput(memory, CanSend);
            }
            finally { opened?.Dispose(); }
        }

        private void PollVerifiedState()
        {
            snapshot = source.Poll(DateTimeOffset.UtcNow);
            if (source.IsStopped || snapshot.Error != null) throw new InvalidOperationException(snapshot.Error ?? source.Status);
            observation = adapter.Observe(snapshot, clock.Elapsed);
            if (!observation.TrustedBuild || !observation.HealthValidated || !observation.SpValidated)
                throw new InvalidOperationException("Verified HP/SP state is unavailable.");
            if (observation.CurrentHP.GetValueOrDefault() == 0) throw new InvalidOperationException("Character is dead.");
            if (snapshot.CharacterName.Validation != StateValidation.Valid || string.IsNullOrWhiteSpace(snapshot.CharacterName.Value))
                throw new InvalidOperationException("Verified character name is unavailable.");
            CharacterName = snapshot.CharacterName.Value;
            SpPercent = observation.MaxSP.GetValueOrDefault() == 0 ? (decimal?)null
                : observation.CurrentSP.Value * 100m / observation.MaxSP.Value;
            if (!SpPercent.HasValue) throw new InvalidOperationException("SP maximum is zero or unavailable.");
        }

        private bool CanSend()
        {
            if (!Active || disposed || source == null || source.IsStopped || snapshot == null || observation == null) return false;
            if (!observation.TrustedBuild || !observation.HealthValidated || !observation.SpValidated || observation.CurrentHP.GetValueOrDefault() == 0) return false;
            return snapshot.ProcessId == ProcessId && snapshot.CharacterName.Validation == StateValidation.Valid
                && DateTimeOffset.UtcNow - snapshot.SampledAtUtc < TimeSpan.FromSeconds(2);
        }

        private void SendKey(int key)
        {
            input.Send(new SequenceStep { Kind = SequenceStepKind.PressKey, Key = key });
        }

        private void SendTargetClick()
        {
            using (var process = Process.GetProcessById(ProcessId))
            {
                RECT rect;
                if (!GetClientRect(process.MainWindowHandle, out rect)) throw new InvalidOperationException("Could not read target client size.");
                int width = Math.Max(1, rect.Right - rect.Left), height = Math.Max(1, rect.Bottom - rect.Top);
                int x = Math.Max(0, Math.Min(width - 1, (int)Math.Round(width * settings.TargetXPercent / 100m)));
                int y = Math.Max(0, Math.Min(height - 1, (int)Math.Round(height * settings.TargetYPercent / 100m)));
                input.Send(new SequenceStep { Kind = SequenceStepKind.Click, X = x, Y = y });
            }
        }

        private void CloseConnection()
        {
            try { input?.Dispose(); } catch { }
            input = null;
            try { source?.Dispose(); } catch { }
            source = null;
            memory = null; // MemoryStateSource owns and disposed it.
            adapter = null;
            snapshot = null;
            observation = null;
            ProcessId = 0;
            CharacterName = null;
            SpPercent = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Stop("Disposed");
            disposed = true;
            clock.Stop();
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ScreenToClient(IntPtr window, ref POINT point);
    }

    public sealed class VanillaTemporaryActionsPanel : UserControl
    {
        private readonly string settingsPath = Path.Combine(VanillaAppData.RootDirectory, "temporary-actions.json");
        private readonly VanillaTemporaryActionRunner runner;
        private readonly Timer timer = new Timer { Interval = 150 };
        private readonly ComboBox clients = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
        private readonly ComboBox actionKey = KeyBox();
        private readonly ComboBox sitKey = KeyBox();
        private readonly NumericUpDown interval = Number(0.25m, 3600m, 1.5m, 2);
        private readonly CheckBox clickTarget = new CheckBox { Text = "Click captured target after hotkey", AutoSize = true };
        private readonly NumericUpDown targetX = Number(0, 100, 50, 2);
        private readonly NumericUpDown targetY = Number(0, 100, 50, 2);
        private readonly NumericUpDown clickDelay = Number(0, 5, 0.18m, 2);
        private readonly CheckBox spRest = new CheckBox { Text = "Memory-driven SP rest", AutoSize = true, Checked = true };
        private readonly NumericUpDown restBelow = Number(1, 99, 10, 1);
        private readonly NumericUpDown resumeAbove = Number(1, 100, 80, 1);
        private readonly Button start = new Button { Text = "START TEMPORARY ACTION", AutoSize = true };
        private readonly Button stop = new Button { Text = "STOP", AutoSize = true, Enabled = false };
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(1100, 0), ForeColor = Color.DimGray };
        private DateTime lastProcessRefresh = DateTime.MinValue;

        public VanillaTemporaryActionsPanel(string baseDirectory)
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            BackColor = Color.White;
            runner = new VanillaTemporaryActionRunner(baseDirectory);
            BuildLayout();
            LoadSettings();
            RefreshClients();
            timer.Tick += (s, e) =>
            {
                if ((DateTime.UtcNow - lastProcessRefresh).TotalSeconds > 2) RefreshClients();
                runner.Tick();
                status.Text = runner.Status + (runner.SpPercent.HasValue ? " | SP " + runner.SpPercent.Value.ToString("0.0") + "%" : "");
                start.Enabled = !runner.Active;
                stop.Enabled = runner.Active;
            };
            timer.Start();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16), ColumnCount = 3, RowCount = 11 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(Header("Temporary actions"), 0, 0); root.SetColumnSpan(root.GetControlFromPosition(0, 0), 3);
            Add(root, 1, "Client", clients, "Choose which of the two live Vanilla clients receives the temporary action.");
            Add(root, 2, "Action / skill hotkey", actionKey, "Example: put Novice Spirit on a hotkey and select that key here.");
            Add(root, 3, "Repeat interval (seconds)", interval, "The action repeats until STOP or the client/state becomes invalid.");
            root.Controls.Add(clickTarget, 0, 4); root.SetColumnSpan(clickTarget, 3);
            var target = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            target.Controls.Add(new Label { Text = "X %", AutoSize = true, Margin = new Padding(0, 7, 4, 0) }); target.Controls.Add(targetX);
            target.Controls.Add(new Label { Text = "Y %", AutoSize = true, Margin = new Padding(10, 7, 4, 0) }); target.Controls.Add(targetY);
            var capture = new Button { Text = "CAPTURE MOUSE AS TARGET", AutoSize = true, Margin = new Padding(12, 0, 0, 0) };
            capture.Click += (s, e) => Guard(CaptureTarget);
            target.Controls.Add(capture);
            Add(root, 5, "Target position", target, "Move the mouse over the target character inside the selected Vanilla window, then capture. Stored as client-relative %, so resolution/DPI changes do not change the target point.");
            Add(root, 6, "Target click delay (seconds)", clickDelay, "Small delay between the skill hotkey and target click.");
            root.Controls.Add(spRest, 0, 7); root.SetColumnSpan(spRest, 3);
            var rest = new FlowLayoutPanel { AutoSize = true };
            rest.Controls.Add(new Label { Text = "Sit at or below", AutoSize = true, Margin = new Padding(0, 7, 4, 0) }); rest.Controls.Add(restBelow);
            rest.Controls.Add(new Label { Text = "%   Stand/resume at", AutoSize = true, Margin = new Padding(8, 7, 4, 0) }); rest.Controls.Add(resumeAbove);
            rest.Controls.Add(new Label { Text = "%", AutoSize = true, Margin = new Padding(4, 7, 0, 0) });
            Add(root, 8, "SP thresholds", rest, "Uses verified CurrentSP/MaxSP memory values with hysteresis; it never reads the SP bar from the screen.");
            Add(root, 9, "Sit / stand toggle key", sitKey, "Default Ragnarok sit/stand key is Insert; configure whatever your client actually uses.");
            var buttons = new FlowLayoutPanel { AutoSize = true };
            start.Click += (s, e) => Guard(StartRunner); stop.Click += (s, e) => runner.Stop("Stopped by user");
            var save = new Button { Text = "SAVE SETTINGS", AutoSize = true }; save.Click += (s, e) => Guard(SaveSettings);
            buttons.Controls.Add(start); buttons.Controls.Add(stop); buttons.Controls.Add(save); buttons.Controls.Add(status);
            root.Controls.Add(buttons, 0, 10); root.SetColumnSpan(buttons, 3);
            Controls.Add(root);
        }

        private void StartRunner()
        {
            var selected = clients.SelectedItem as ClientChoice;
            if (selected == null) throw new InvalidOperationException("Start Vanilla and select a client first.");
            var value = ReadSettings();
            SaveSettings(value);
            runner.Start(selected.Id, value);
        }

        private void CaptureTarget()
        {
            var selected = clients.SelectedItem as ClientChoice;
            if (selected == null) throw new InvalidOperationException("Select a live Vanilla client first.");
            PointF point = VanillaTemporaryActionRunner.CaptureTargetPercent(selected.Id);
            targetX.Value = Math.Max(targetX.Minimum, Math.Min(targetX.Maximum, (decimal)point.X));
            targetY.Value = Math.Max(targetY.Minimum, Math.Min(targetY.Maximum, (decimal)point.Y));
            clickTarget.Checked = true;
            status.Text = "Captured target at " + point.X.ToString("0.0") + "%, " + point.Y.ToString("0.0") + "% of the selected client.";
        }

        private void RefreshClients()
        {
            int? selected = (clients.SelectedItem as ClientChoice)?.Id;
            var list = new List<ClientChoice>();
            foreach (Process process in Process.GetProcessesByName("Vanilla MMO").OrderBy(item => item.Id))
            {
                using (process)
                {
                    try { if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero) list.Add(new ClientChoice(process.Id, process.MainWindowTitle)); }
                    catch { }
                }
            }
            if (list.Select(x => x.Id).SequenceEqual(clients.Items.Cast<ClientChoice>().Select(x => x.Id))) { lastProcessRefresh = DateTime.UtcNow; return; }
            clients.Items.Clear(); foreach (var item in list) clients.Items.Add(item);
            clients.SelectedItem = list.FirstOrDefault(item => item.Id == selected) ?? list.FirstOrDefault();
            lastProcessRefresh = DateTime.UtcNow;
        }

        private VanillaTemporaryActionSettings ReadSettings()
        {
            var value = new VanillaTemporaryActionSettings
            {
                ActionKey = SelectedKey(actionKey), IntervalMs = (int)(interval.Value * 1000m), ClickTargetAfterKey = clickTarget.Checked,
                TargetXPercent = targetX.Value, TargetYPercent = targetY.Value, TargetClickDelayMs = (int)(clickDelay.Value * 1000m),
                SpRestEnabled = spRest.Checked, RestBelowPercent = restBelow.Value, ResumeAbovePercent = resumeAbove.Value, SitStandKey = SelectedKey(sitKey)
            };
            value.Validate();
            return value;
        }

        private void LoadSettings()
        {
            VanillaTemporaryActionSettings value = null;
            try { if (File.Exists(settingsPath)) value = JsonConvert.DeserializeObject<VanillaTemporaryActionSettings>(File.ReadAllText(settingsPath)); value?.Validate(); }
            catch { value = null; }
            value = value ?? new VanillaTemporaryActionSettings();
            SelectKey(actionKey, value.ActionKey); SelectKey(sitKey, value.SitStandKey);
            interval.Value = Math.Max(interval.Minimum, Math.Min(interval.Maximum, value.IntervalMs / 1000m));
            clickTarget.Checked = value.ClickTargetAfterKey; targetX.Value = value.TargetXPercent; targetY.Value = value.TargetYPercent;
            clickDelay.Value = value.TargetClickDelayMs / 1000m; spRest.Checked = value.SpRestEnabled; restBelow.Value = value.RestBelowPercent; resumeAbove.Value = value.ResumeAbovePercent;
        }

        private void SaveSettings() { SaveSettings(ReadSettings()); }
        private void SaveSettings(VanillaTemporaryActionSettings value)
        {
            value.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            File.WriteAllText(settingsPath, JsonConvert.SerializeObject(value, Formatting.Indented));
            status.Text = "Temporary-action settings saved.";
        }

        private void Guard(System.Action action)
        {
            try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Temporary actions", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); runner.Dispose(); }
            base.Dispose(disposing);
        }

        private static void Add(TableLayoutPanel root, int row, string label, Control control, string hint)
        {
            root.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, row);
            root.Controls.Add(control, 1, row);
            root.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(700, 0), Margin = new Padding(10, 8, 0, 0) }, 2, row);
        }
        private static Label Header(string text) { return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 11F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 12) }; }
        private static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals) { return new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Width = 110 }; }
        private static ComboBox KeyBox()
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, DisplayMember = "Label", ValueMember = "Value" };
            var choices = new[] { new KeyChoice(0, "None") }.Concat(
                Enum.GetValues(typeof(Keys)).Cast<Keys>().Select(key => (int)key).Where(value => value >= 8 && value <= 254)
                    .Distinct().OrderBy(value => value).Select(value => new KeyChoice(value, ((Keys)value).ToString()))).ToArray();
            box.DataSource = choices;
            return box;
        }
        private static int SelectedKey(ComboBox box) { return box.SelectedItem is KeyChoice ? ((KeyChoice)box.SelectedItem).Value : 0; }
        private static void SelectKey(ComboBox box, int key)
        {
            box.SelectedItem = box.Items.Cast<KeyChoice>().FirstOrDefault(item => item.Value == key);
            if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0;
        }

        private sealed class KeyChoice
        {
            public int Value { get; private set; } public string Label { get; private set; }
            public KeyChoice(int value, string label) { Value = value; Label = label; }
        }
        private sealed class ClientChoice
        {
            public int Id { get; private set; } private readonly string title;
            public ClientChoice(int id, string title) { Id = id; this.title = title; }
            public override string ToString() { return "Vanilla MMO.exe — PID " + Id + (string.IsNullOrWhiteSpace(title) ? "" : " — " + title); }
        }
    }
}

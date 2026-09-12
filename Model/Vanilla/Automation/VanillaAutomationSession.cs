using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using _4RTools.Forms;
using _4RTools.Utils;

namespace _4RTools.Model.Vanilla.Automation
{
    /// <summary>Owns one explicitly selected process and cancels all actions when that selection changes.</summary>
    public sealed class VanillaAutomationSession : IAutomationSession, IInputSink
    {
        private readonly string baseDirectory, logPath;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly AutomationProfileStore profiles;
        private readonly AutomationEngine engine;
        private MemoryStateSource source;
        private ReadOnlyProcessMemory memory;
        private VanillaWindowInput input;
        private VanillaStateAdapter adapter;
        private volatile RuleObservation observation;
        private volatile VanillaClientState publishedSnapshot;
        private VanillaAutomationSettings settings;
        private long executableLength;
        private DateTime executableWriteTime;
        private string connectionStatus = "Disconnected", loggingError;
        private bool disposed;

        public VanillaAutomationSession(string baseDirectory)
        {
            this.baseDirectory = Path.GetFullPath(baseDirectory);
            VanillaAppData.InitializeAndMigrateLegacy(this.baseDirectory);
            logPath = Path.Combine(VanillaAppData.LogsDirectory, "vanilla.log");
            profiles = new AutomationProfileStore(VanillaAppData.RootDirectory);
            CurrentProfileName = "Default";
            try { settings = profiles.Load(CurrentProfileName); }
            catch (Exception ex)
            {
                // Preserve malformed preferences and start a separate, safe profile.
                CurrentProfileName = "Recovered-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                settings = new VanillaAutomationSettings();
                profiles.Save(CurrentProfileName, settings);
                Log("Previous default profile could not be loaded and was preserved: " + ex.Message);
            }
            engine = new AutomationEngine(settings, this, Log);
            Log("4RTools Vanilla 0.2.0 started; automation OFF.");
        }

        public VanillaAutomationSettings Settings { get { return settings.Clone(); } }
        public VanillaClientState Snapshot { get { return publishedSnapshot; } }
        public Func<string> EnableGuard { get; set; }
        public event System.Action ConfigurationChanging;
        public bool IsObservationFresh
        {
            get
            {
                var currentSource = source;
                var currentObservation = observation;
                var currentSnapshot = publishedSnapshot;
                return !disposed && loggingError == null && currentSource != null && !currentSource.IsStopped
                    && currentSnapshot != null && currentObservation != null && currentSnapshot.SessionId == currentObservation.SessionId
                    && currentObservation.ObservedAt <= clock.Elapsed
                    && clock.Elapsed - currentObservation.ObservedAt <= TimeSpan.FromMilliseconds(settings.MaxStateAgeMs);
            }
        }
        public VanillaWindowInput CreateStockInput(Func<bool> permitted)
        {
            var currentMemory = memory;
            if (currentMemory == null || !IsObservationFresh) throw new InvalidOperationException("Connect Vanilla before starting stock input.");
            return new VanillaWindowInput(currentMemory, permitted);
        }
        public string Status { get { return connectionStatus + " | " + engine.Status + (loggingError == null ? "" : " | " + loggingError); } }
        public string Fingerprint { get; private set; }
        public string BuildProfile { get; private set; }
        public string ExecutablePath { get; private set; }
        public string CurrentProfileName { get; private set; }
        public string ActivitySummary
        {
            get
            {
                string target = observation?.TargetValidated == true ? (observation.HasTarget == true ? "Present" : "None") : "Unavailable";
                string combat = observation?.CombatValidated == true ? (observation.InCombat == true ? "Active" : "Idle") : "Unavailable";
                string casting = observation?.CastingValidated == true ? (observation.IsCasting == true ? "Yes" : "No") : "Unavailable";
                return "Target: " + target + " | Combat: " + combat + " | Casting: " + casting
                    + Environment.NewLine + "No target: " + Duration(engine.NoTargetSince) + " | No combat: " + Duration(engine.NoCombatSince)
                    + " | Last teleport: " + (engine.LastTeleportAt.HasValue ? Duration(engine.LastTeleportAt) + " ago" : "None this session");
            }
        }
        private string Duration(TimeSpan? start)
        {
            return start.HasValue ? Math.Max(0, (clock.Elapsed - start.Value).TotalSeconds).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " s" : "Unavailable";
        }
        public bool IsEnabled { get { return engine.Enabled; } }
        public bool FarmingEnabled { get { return engine.FarmingEnabled; } }
        public event System.Action<string> Logged;

        public void Connect(int processId)
        {
            Disconnect();
            try
            {
                memory = new ReadOnlyProcessMemory(processId);
                if (!string.Equals(memory.ProcessName, "Vanilla MMO", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Select a Vanilla MMO client.");
                if (memory.PointerSize != 4) throw new InvalidOperationException("This release supports the 32-bit Vanilla client only.");
                ExecutablePath = memory.ExecutablePath;
                var identity = VanillaExecutableIdentity.Read(ExecutablePath);
                Fingerprint = identity.Sha256;
                var file = new FileInfo(ExecutablePath);
                executableLength = file.Length;
                executableWriteTime = file.LastWriteTimeUtc;
                var profile = VanillaBuildProfile.Find(Path.Combine(baseDirectory, "VanillaBuilds"), identity, Log);
                BuildProfile = profile?.Label ?? "Unknown build; gameplay state unavailable";
                adapter = new VanillaStateAdapter(profile, identity);
                source = new MemoryStateSource(memory, profile?.MemoryMap ?? new VanillaMemoryMap());
                connectionStatus = "Connected read-only to PID " + processId;
                Log(connectionStatus + "; build " + Fingerprint + "; " + BuildProfile);
                Tick();
            }
            catch (Exception ex)
            {
                CloseObservation();
                connectionStatus = "Connection stopped: " + ex.Message;
                Log(connectionStatus);
                throw;
            }
        }

        public void Tick()
        {
            if (disposed || source == null) return;
            try
            {
                var file = new FileInfo(ExecutablePath);
                if (!file.Exists || file.Length != executableLength || file.LastWriteTimeUtc != executableWriteTime)
                    throw new InvalidOperationException("The client executable changed; reconnect to identify its build.");
                TimeSpan sampleStarted = clock.Elapsed;
                var snapshot = source.Poll(DateTimeOffset.UtcNow);
                snapshot.ExecutablePath = ExecutablePath;
                snapshot.Fingerprint = Fingerprint;
                snapshot.BuildProfile = BuildProfile;
                if (source.IsStopped)
                    throw new InvalidOperationException(snapshot.Error ?? source.Status);
                observation = adapter.Observe(snapshot, sampleStarted);
                publishedSnapshot = snapshot;
                engine.Tick(clock.Elapsed, observation);
                if (loggingError != null) engine.Stop("Automation stopped: activity log unavailable");
            }
            catch (Exception ex)
            {
                engine.Stop("Automation stopped: " + ex.Message);
                CloseObservation();
                connectionStatus = "Observation stopped: " + ex.Message;
                Log(connectionStatus);
            }
        }

        public void SetEnabled(bool enabled)
        {
            string blocked = enabled ? EnableGuard?.Invoke() : null;
            if (blocked != null) throw new InvalidOperationException(blocked);
            if (enabled && loggingError != null) throw new IOException(loggingError);
            engine.SetEnabled(enabled);
            if (enabled)
            {
                Tick();
                // A disconnected session has no timer sample to reject it.
                if (observation == null) engine.Stop("Automation stopped: connect a client with verified gameplay state first");
            }
        }
        public void SetFarmingEnabled(bool enabled) { engine.SetFarmingEnabled(enabled); }
        public void TestOnce()
        {
            // Test Once must not also run automatic rules. The engine checks this sample's freshness.
            engine.TestOnce(clock.Elapsed, observation);
        }

        public void ApplySettings(VanillaAutomationSettings value)
        {
            ConfigurationChanging?.Invoke();
            engine.Stop("Settings changed; automation OFF");
            var validated = value.Clone();
            engine.Configure(validated);
            settings = validated;
            observation = null;
        }
        public IReadOnlyList<string> GetProfileNames() { return profiles.Names(); }
        public void LoadProfile(string name)
        {
            engine.Stop("Profile changed; automation OFF");
            var loaded = profiles.Load(name);
            ApplySettings(loaded);
            CurrentProfileName = name;
            Log("Loaded profile " + name);
        }
        public void SaveProfile(string name, VanillaAutomationSettings value)
        {
            var validated = value.Clone();
            profiles.Save(name, validated);
            if (!string.Equals(name, CurrentProfileName, StringComparison.OrdinalIgnoreCase)
                || validated.ToJson() != settings.ToJson()) ApplySettings(validated);
            CurrentProfileName = name;
        }
        public void ImportProfile(string path, string name)
        {
            engine.Stop("Profile import; automation OFF");
            profiles.Import(path, name);
            LoadProfile(name);
        }
        public void ExportProfile(string path) { profiles.Export(CurrentProfileName, path); }

        void IInputSink.Send(SequenceStep step)
        {
            if (!CanSend()) throw new InvalidOperationException("Input is disabled or the state is stale.");
            if (input == null) input = new VanillaWindowInput(memory, CanSend);
            input.Send(step);
        }
        private bool CanSend()
        {
            return !disposed && loggingError == null && engine.Enabled && !settings.DryRun && source != null && !source.IsStopped
                && observation != null && observation.TrustedBuild && observation.Ready && !observation.Loading
                && observation.HealthValidated && observation.CurrentHP > 0
                && clock.Elapsed - observation.ObservedAt <= TimeSpan.FromMilliseconds(settings.MaxStateAgeMs);
        }
        void IInputSink.ReleaseAll() { input?.ReleaseAll(); }

        public void Disconnect()
        {
            engine.Stop("Disconnected; automation OFF");
            engine.SetFarmingEnabled(false);
            CloseObservation();
            Fingerprint = BuildProfile = ExecutablePath = null;
            connectionStatus = "Disconnected";
        }
        private void CloseObservation()
        {
            try { input?.Dispose(); }
            catch (Exception ex) { Log("Input release stopped: " + ex.Message); }
            finally
            {
                input = null;
                source?.Dispose();
                source = null;
                memory?.Dispose();
                memory = null;
                publishedSnapshot = null;
                observation = null;
                adapter = null;
            }
        }
        private void Log(string value)
        {
            string line = DateTimeOffset.UtcNow.ToString("O") + " " + value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 1024 * 1024)
                {
                    string oldest = logPath + ".3";
                    if (File.Exists(oldest)) File.Delete(oldest);
                    for (int index = 2; index >= 1; index--)
                        if (File.Exists(logPath + "." + index)) File.Move(logPath + "." + index, logPath + "." + (index + 1));
                    File.Move(logPath, logPath + ".1");
                }
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch (Exception ex) { loggingError = "Activity log could not be written: " + ex.Message; }
            Logged?.Invoke(line);
        }
        public void Dispose()
        {
            if (disposed) return;
            Disconnect();
            disposed = true;
            clock.Stop();
        }
    }
}

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;
using _4RTools.Model.Vanilla.Automation;
using _4RTools.Utils;

namespace _4RTools.Model.Vanilla
{
    /// <summary>Shares validated observations with the original 4RTools feature models.</summary>
    public sealed class VanillaStockBackend : IDisposable
    {
        private readonly Func<VanillaClientState> snapshot;
        private readonly Func<bool> fresh;
        private readonly Func<Func<bool>, IInputSink> inputFactory;
        private readonly object sync = new object();
        private readonly int processId;
        private readonly Guid sessionId;
        private IInputSink input;
        private bool enabled, disposed, automaticInput;
        private string failure;
        public string LastFailure { get { lock (sync) return failure; } }
        public bool Enabled { get { lock (sync) return enabled && !disposed && failure == null; } }

        public VanillaStockBackend(Func<VanillaClientState> snapshot, Func<bool> fresh, Func<Func<bool>, IInputSink> inputFactory)
        {
            this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            this.fresh = fresh ?? throw new ArgumentNullException(nameof(fresh));
            this.inputFactory = inputFactory ?? throw new ArgumentNullException(nameof(inputFactory));
            var state = snapshot();
            if (state == null || !state.ProcessId.HasValue || state.SessionId == Guid.Empty)
                throw new InvalidOperationException("Connect the selected Vanilla client read-only before attaching its stock features.");
            processId = state.ProcessId.Value;
            sessionId = state.SessionId;
        }

        public string Capabilities
        {
            get
            {
                lock (sync)
                {
                    var state = GetSnapshot();
                    if (state == null) return failure ?? "Vanilla observation unavailable or stale";
                    return "Vanilla: HP " + VitalLabel(state.CurrentHP, state.MaxHP)
                        + "; SP " + VitalLabel(state.CurrentSP, state.MaxSP)
                        + "; name " + Label(state.CharacterName) + "; ready "
                        + (Valid(state.ClientReady) ? (IsReady(state) ? "verified" : "not ready") : Label(state.ClientReady))
                        + "; statuses " + Label(state.StatusEffects);
                }
            }
        }

        public string GetEnableError(Profile profile)
        {
            lock (sync)
            {
                string error = InputError();
                if (error != null) return error;
                if (profile == null) return "Select a profile before enabling Vanilla features.";
                var state = GetSnapshot();
                if (state == null) return "Vanilla observation changed while checking capabilities; reconnect before enabling features.";
                if (profile.AHK != null && (profile.AHK.ahkMode != AHK.COMPATIBILITY || profile.AHK.mouseFlick || profile.AHK.noShift))
                    return "For Vanilla, select AHK Compatibility and turn off Mouse Flick and No Shift; those options use global input.";
                if (profile.Autopot == null || profile.AutopotYgg == null || profile.Autobuff == null
                    || profile.StatusRecovery == null || profile.DebuffsRecovery == null)
                    return "The selected profile has an invalid feature configuration.";
                foreach (var pot in new[] { profile.Autopot, profile.AutopotYgg })
                {
                    if ((Configured(pot.hpKey, pot.hpPercent) || Configured(pot.spKey, pot.spPercent)) && !IsReady(state))
                        return "Autopot requires independently verified in-game readiness; that signal is unavailable for this Vanilla build.";
                    if (Configured(pot.spKey, pot.spPercent) && !ValidVitals(state.CurrentSP, state.MaxSP))
                        return "SP Autopot requires verified current/max SP for this Vanilla build.";
                }
                if (profile.Autobuff.buffMapping == null || profile.StatusRecovery.buffMapping == null || profile.DebuffsRecovery.buffMapping == null)
                    return "The selected profile has an invalid status configuration.";
                bool statusActions = profile.Autobuff.buffMapping.Count > 0 || profile.StatusRecovery.buffMapping.Count > 0
                    || profile.StatusRecovery.autoStand || profile.DebuffsRecovery.buffMapping.Count > 0;
                if (statusActions && (!IsReady(state) || !Valid(state.StatusEffects)))
                    return "Autobuff and status recovery require verified in-game readiness and status effects for this Vanilla build.";
                if (new[] { profile.AutoRefreshSpammer1, profile.AutoRefreshSpammer2, profile.AutoRefreshSpammer3 }
                    .Any(timer => timer != null && timer.RefreshKey != Key.None) && !IsReady(state))
                    return "Automatic skill timers require independently verified in-game readiness for this Vanilla build.";
                return null;
            }
        }

        public void SetEnabled(bool value)
        {
            lock (sync)
            {
                if (!value) { enabled = false; ReleaseInput(); return; }
                string error = InputError();
                if (error != null) throw new InvalidOperationException(error);
                enabled = true;
            }
        }

        public bool TryReadCharacterName(out string name)
        {
            lock (sync)
            {
                var state = GetSnapshot();
                // Displaying an observed name does not grant it input capability.
                name = state != null && state.CharacterName.IsAvailable ? state.CharacterName.Value : null;
                return name != null;
            }
        }

        public uint ReadVital(VanillaField field)
        {
            lock (sync)
            {
                var state = RequireSnapshot();
                bool hp = field == VanillaField.CurrentHP || field == VanillaField.MaxHP;
                if (!hp && field != VanillaField.CurrentSP && field != VanillaField.MaxSP) throw new ArgumentException("Expected an HP or SP field.");
                if (!(hp ? ValidVitals(state.CurrentHP, state.MaxHP) : ValidVitals(state.CurrentSP, state.MaxSP)))
                    throw new InvalidOperationException("Vanilla " + (hp ? "HP" : "SP") + " state is unavailable or unverified.");
                return ((StateValue<uint>)state.Fields[field]).Value;
            }
        }

        public bool IsBelow(bool hp, int percent)
        {
            lock (sync)
            {
                var state = RequireAutomaticState();
                var current = hp ? state.CurrentHP : state.CurrentSP;
                var maximum = hp ? state.MaxHP : state.MaxSP;
                if (!ValidVitals(current, maximum)) throw new InvalidOperationException("Vanilla " + (hp ? "HP" : "SP") + " state is unavailable or unverified.");
                if (percent < 0 || percent > 100) throw new ArgumentOutOfRangeException(nameof(percent), "Potion threshold must be between 0 and 100.");
                return current.Value * 100m < maximum.Value * (decimal)percent;
            }
        }

        public uint[] ReadStatuses()
        {
            lock (sync)
            {
                var state = RequireAutomaticState();
                if (!Valid(state.StatusEffects)) throw new InvalidOperationException("Vanilla status effects are unavailable or unverified.");
                return state.StatusEffects.Value;
            }
        }

        public void EnsureAutomaticReady() { lock (sync) RequireAutomaticState(); }

        public bool SendWindowMessage(IntPtr window, int message, Keys key, int data, bool automatic = false)
        {
            lock (sync)
            {
                try
                {
                    _4RThread.ThrowIfCancellationRequested();
                    if (!enabled) throw new OperationCanceledException("Vanilla stock automation is OFF.");
                    string error = InputError();
                    if (error != null) throw new InvalidOperationException(error);
                    uint owner;
                    if (window == IntPtr.Zero) throw new InvalidOperationException("The selected Vanilla window is unavailable.");
                    if (GetWindowThreadProcessId(window, out owner) == 0)
                    {
                        int code = Marshal.GetLastWin32Error();
                        throw new InvalidOperationException("GetWindowThreadProcessId failed: Win32 " + code + ". Input stopped.");
                    }
                    if (owner != processId)
                        throw new InvalidOperationException("Vanilla input window does not belong to the selected client.");
                    // Stock features send repeated key downs, sometimes without key ups.
                    // Each is delivered as a complete press so cancellation cannot leave a held key.
                    if (message == 0x0101 || message == 0x0202) return true;
                    SequenceStep step;
                    if (message == 0x0100) step = new SequenceStep { Kind = SequenceStepKind.PressKey, Key = (int)key };
                    else if (message == 0x0201)
                    {
                        var point = System.Windows.Forms.Cursor.Position;
                        var clientPoint = new NativePoint { X = point.X, Y = point.Y };
                        if (!ScreenToClient(window, ref clientPoint))
                        {
                            int code = Marshal.GetLastWin32Error();
                            throw new InvalidOperationException("ScreenToClient failed: Win32 " + code + ". Input stopped.");
                        }
                        step = new SequenceStep { Kind = SequenceStepKind.Click, X = clientPoint.X, Y = clientPoint.Y };
                    }
                    else throw new InvalidOperationException("This window message is not supported by the read-only Vanilla bridge.");
                    Send(step, automatic);
                    return true;
                }
                catch (OperationCanceledException) { throw; }
                catch (ThreadInterruptedException) when (_4RThread.IsCancellationRequested) { throw; }
                catch (Exception ex) { Fail(ex.Message); throw; }
            }
        }

        // Separate from native message translation so capabilities/input failures can be tested offline.
        public void Send(SequenceStep step, bool automatic = false)
        {
            lock (sync)
            {
                try
                {
                    _4RThread.ThrowIfCancellationRequested();
                    if (!enabled) throw new OperationCanceledException("Vanilla stock automation is OFF.");
                    string error = InputError();
                    if (error != null) throw new InvalidOperationException(error);
                    if (automatic) RequireAutomaticState();
                    if (step == null || (step.Kind != SequenceStepKind.PressKey && step.Kind != SequenceStepKind.Click))
                        throw new ArgumentException("Stock input supports complete key presses and clicks only.");
                    if (input == null) input = inputFactory(() =>
                    {
                        _4RThread.ThrowIfCancellationRequested();
                        return Enabled && InputError() == null && (!automaticInput || IsReady(GetSnapshot()));
                    });
                    automaticInput = automatic;
                    try { input.Send(step); }
                    finally { automaticInput = false; }
                }
                catch (OperationCanceledException) { throw; }
                catch (ThreadInterruptedException) when (_4RThread.IsCancellationRequested) { throw; }
                catch (Exception ex) { Fail(ex.Message); throw; }
            }
        }

        public void Fail(string reason)
        {
            lock (sync)
            {
                failure = failure ?? reason ?? "Vanilla action stopped.";
                enabled = false;
                ReleaseInput();
            }
        }
        private string InputError()
        {
            if (disposed) return "The selected Vanilla connection has been closed.";
            if (failure != null) return failure;
            var state = GetSnapshot();
            if (state == null) return "Vanilla state is unavailable, stale or belongs to a different client. Reconnect before enabling features.";
            if (!ValidVitals(state.CurrentHP, state.MaxHP) || state.CurrentHP.Value == 0)
                return "Vanilla input requires verified HP/max HP and a living character.";
            if (!Valid(state.CharacterName) || string.IsNullOrWhiteSpace(state.CharacterName.Value))
                return "Vanilla input requires a verified character name.";
            if (Valid(state.Loading) && state.Loading.Value) return "Vanilla is loading; automation is paused.";
            if (Valid(state.ClientReady) && !state.ClientReady.Value) return "Vanilla is not ready for in-game input.";
            return null;
        }
        private VanillaClientState GetSnapshot()
        {
            if (disposed || !fresh()) return null;
            var state = snapshot();
            return state != null && !state.IsDemo && state.Error == null && state.ProcessId == processId && state.SessionId == sessionId ? state : null;
        }
        private VanillaClientState RequireSnapshot()
        {
            var state = GetSnapshot();
            if (state == null) throw new InvalidOperationException("Vanilla observation is unavailable or stale.");
            return state;
        }
        private VanillaClientState RequireAutomaticState()
        {
            var state = RequireSnapshot();
            if (!IsReady(state)) throw new InvalidOperationException("Vanilla automatic actions require verified in-game readiness.");
            if (!ValidVitals(state.CurrentHP, state.MaxHP) || state.CurrentHP.Value == 0)
                throw new InvalidOperationException("Vanilla automatic actions require verified HP and a living character.");
            return state;
        }
        private void ReleaseInput()
        {
            if (input == null) return;
            try { input.ReleaseAll(); }
            catch (Exception ex) { failure = failure ?? "Vanilla input cleanup failed: " + ex.Message; }
        }
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                enabled = false;
                ReleaseInput();
                try { (input as IDisposable)?.Dispose(); }
                finally { input = null; disposed = true; }
            }
        }
        private static bool Valid(StateValue value) { return value != null && value.IsAvailable && value.Validation == StateValidation.Valid; }
        private static bool ValidVitals(StateValue<uint> current, StateValue<uint> maximum)
        { return Valid(current) && Valid(maximum) && maximum.Value > 0 && current.Value <= maximum.Value; }
        private static bool IsReady(VanillaClientState state)
        { return state != null && Valid(state.ClientReady) && state.ClientReady.Value && (!Valid(state.Loading) || !state.Loading.Value); }
        private static bool Configured(Key key, int percent) { return key != Key.None && percent > 0; }
        private static string Label(params StateValue[] values)
        {
            if (values.Any(value => value == null || !value.IsAvailable)) return "unavailable";
            if (values.Any(value => value.Validation == StateValidation.Invalid)) return "invalid";
            return values.All(Valid) ? "verified" : "not verified";
        }
        private static string VitalLabel(StateValue<uint> current, StateValue<uint> maximum)
        {
            string label = Label(current, maximum);
            return label == "verified" && !ValidVitals(current, maximum) ? "invalid" : label;
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
    }
}

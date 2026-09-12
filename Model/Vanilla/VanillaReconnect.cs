using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace _4RTools.Model.Vanilla
{
    public enum VanillaProxyRoute { Global = 0, Singapore = 1, Tokyo = 2, LosAngeles = 3 }
    public enum VanillaVisualState { Unknown, Gameplay, LoginShell, ModalDialog }
    public enum VanillaReconnectStage
    {
        Stopped, WaitingForClient, Launching, WaitingForWindow, LoggingIn, SelectingCharacter,
        WaitingForGameplay, Online, AcknowledgingPopup, NeedsConfiguration, Backoff, Error
    }

    public sealed class VanillaReconnectAccount
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public bool Enabled { get; set; } = true;
        public string Label { get; set; } = "Client";
        public string UserName { get; set; } = "";
        public string ProtectedPassword { get; set; } = "";
        public int CharacterSlot { get; set; } = 1;
        public int ResumeKey { get; set; } = (int)Keys.D2;
        public bool ResumeCtrl { get; set; } = true;
        public bool ResumeAlt { get; set; }
        public bool ResumeShift { get; set; }

        public VanillaReconnectAccount Clone()
        {
            return JsonConvert.DeserializeObject<VanillaReconnectAccount>(JsonConvert.SerializeObject(this));
        }

        public string HotkeyText
        {
            get
            {
                var parts = new List<string>();
                if (ResumeCtrl) parts.Add("Ctrl");
                if (ResumeAlt) parts.Add("Alt");
                if (ResumeShift) parts.Add("Shift");
                parts.Add(((Keys)ResumeKey).ToString());
                return string.Join("+", parts);
            }
        }
    }

    public sealed class VanillaUiAnchors
    {
        // Normalized client coordinates derived from Vanilla's centered UI. They are resolution independent.
        public double ServiceListX { get; set; } = 0.50;
        public double ServiceListY { get; set; } = 0.60;
        public double UserNameX { get; set; } = 0.48;
        public double UserNameY { get; set; } = 0.635;
        public double PasswordX { get; set; } = 0.48;
        public double PasswordY { get; set; } = 0.660;
        public double CharacterGridX { get; set; } = 0.292;
        public double CharacterGridY { get; set; } = 0.375;
        public double CharacterStepX { get; set; } = 0.080;
        public double CharacterStepY { get; set; } = 0.151;
        public double GameStartX { get; set; } = 0.705;
        public double GameStartY { get; set; } = 0.650;
    }

    public sealed class VanillaReconnectSettings
    {
        public int Version { get; set; } = 1;
        public bool StartWith4RTools { get; set; }
        public bool AutoRecover { get; set; } = true;
        public bool VisualWatchdog { get; set; } = true;
        public string LaunchExecutable { get; set; } = "";
        public string LaunchArguments { get; set; } = "";
        public VanillaProxyRoute Proxy { get; set; } = VanillaProxyRoute.Tokyo;
        public int MaxClients { get; set; } = 2;
        public int PollMs { get; set; } = 1500;
        public int GepardWaitMs { get; set; } = 8000;
        public int StageDelayMs { get; set; } = 2200;
        public int GameLoadMs { get; set; } = 8000;
        public int LoginStableMs { get; set; } = 4500;
        public int RetryBackoffMs { get; set; } = 30000;
        public int PopupCooldownMs { get; set; } = 5000;
        public VanillaUiAnchors Anchors { get; set; } = new VanillaUiAnchors();
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<VanillaReconnectAccount> Accounts { get; set; } = new List<VanillaReconnectAccount>();

        public static VanillaReconnectSettings CreateDefault()
        {
            var value = new VanillaReconnectSettings();
            value.Accounts.Add(new VanillaReconnectAccount { Label = "Client 1" });
            value.Accounts.Add(new VanillaReconnectAccount { Label = "Client 2" });
            return value;
        }

        public VanillaReconnectSettings Clone()
        {
            var value = JsonConvert.DeserializeObject<VanillaReconnectSettings>(
                JsonConvert.SerializeObject(this), new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
            if (value == null) throw new InvalidDataException("Reconnect settings could not be cloned.");
            value.NormalizeAccounts();
            return value;
        }

        public void NormalizeAccounts()
        {
            if (Accounts == null) Accounts = new List<VanillaReconnectAccount>();
            var unique = Accounts.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Id))
                .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(a => IsSyntheticDefault(a) ? 0 : 1).First())
                .ToList();
            if (unique.Count > 2)
            {
                var preferred = unique.Where(a => !IsSyntheticDefault(a)).ToList();
                foreach (var account in unique)
                {
                    if (preferred.Count >= 2) break;
                    if (!preferred.Contains(account)) preferred.Add(account);
                }
                unique = preferred.Take(2).ToList();
            }
            Accounts = unique;
            if (Anchors == null) Anchors = new VanillaUiAnchors();
            if (Math.Abs(Anchors.UserNameY - 0.66) < 0.0001 && Math.Abs(Anchors.PasswordY - 0.685) < 0.0001) { Anchors.UserNameY = 0.635; Anchors.PasswordY = 0.660; }
        }

        private static bool IsSyntheticDefault(VanillaReconnectAccount account)
        {
            if (account == null) return true;
            bool defaultLabel = string.Equals(account.Label, "Client 1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(account.Label, "Client 2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(account.Label, "Client", StringComparison.OrdinalIgnoreCase);
            return defaultLabel && string.IsNullOrWhiteSpace(account.UserName) && string.IsNullOrWhiteSpace(account.ProtectedPassword);
        }

        public void Validate()
        {
            if (Version != 1) throw new ArgumentException("Unsupported reconnect profile version.");
            if (MaxClients < 1 || MaxClients > 2) throw new ArgumentException("Vanilla allows at most 2 managed clients per PC.");
            if (PollMs < 500 || PollMs > 30000) throw new ArgumentException("Polling must be between 0.5 and 30 seconds.");
            if (GepardWaitMs < 1000 || GepardWaitMs > 120000) throw new ArgumentException("Gepard wait must be between 1 and 120 seconds.");
            if (StageDelayMs < 250 || StageDelayMs > 30000) throw new ArgumentException("Stage delay must be between 0.25 and 30 seconds.");
            if (GameLoadMs < 1000 || GameLoadMs > 120000) throw new ArgumentException("Game load wait must be between 1 and 120 seconds.");
            if (LoginStableMs < 1000 || LoginStableMs > 60000) throw new ArgumentException("Login detection wait must be between 1 and 60 seconds.");
            if (RetryBackoffMs < 5000 || RetryBackoffMs > 600000) throw new ArgumentException("Retry backoff must be between 5 seconds and 10 minutes.");
            if (PopupCooldownMs < 1000 || PopupCooldownMs > 60000) throw new ArgumentException("Popup cooldown must be between 1 and 60 seconds.");
            if (Anchors == null) throw new ArgumentException("UI anchors are missing.");
            Check01(Anchors.ServiceListX); Check01(Anchors.ServiceListY);
            Check01(Anchors.UserNameX); Check01(Anchors.UserNameY);
            Check01(Anchors.PasswordX); Check01(Anchors.PasswordY);
            Check01(Anchors.CharacterGridX); Check01(Anchors.CharacterGridY);
            Check01(Anchors.GameStartX); Check01(Anchors.GameStartY);
            if (Anchors.CharacterStepX <= 0 || Anchors.CharacterStepX > .25 || Anchors.CharacterStepY <= 0 || Anchors.CharacterStepY > .25)
                throw new ArgumentException("Character-grid steps are invalid.");
            if (Accounts == null || Accounts.Count == 0 || Accounts.Count > 2)
                throw new ArgumentException("Configure one or two account profiles on this PC.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var account in Accounts)
            {
                if (account == null || string.IsNullOrWhiteSpace(account.Id) || !ids.Add(account.Id))
                    throw new ArgumentException("Every account profile needs a unique ID.");
                if (string.IsNullOrWhiteSpace(account.Label) || account.Label.Length > 80)
                    throw new ArgumentException("Every account needs a label.");
                if (account.UserName != null && account.UserName.Length > 128)
                    throw new ArgumentException("Username is too long.");
                if (account.CharacterSlot < 1 || account.CharacterSlot > 15)
                    throw new ArgumentException("Character slot must be between 1 and 15.");
                if (account.ResumeKey < 8 || account.ResumeKey > 254)
                    throw new ArgumentException("Resume hotkey is invalid.");
            }
        }

        private static void Check01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1)
                throw new ArgumentException("A normalized UI anchor is invalid.");
        }
    }

    public sealed class VanillaReconnectStatus
    {
        public string AccountId { get; set; }
        public string Label { get; set; }
        public int? ProcessId { get; set; }
        public VanillaReconnectStage Stage { get; set; }
        public VanillaVisualState VisualState { get; set; }
        public string Detail { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public sealed class VanillaReconnectStore
    {
        private readonly string directory;
        private readonly string path;
        public VanillaReconnectStore(string baseDirectory)
        {
            directory = Path.Combine(Path.GetFullPath(baseDirectory), "VanillaReconnect");
            path = Path.Combine(directory, "reconnect.json");
        }
        public string FilePath { get { return path; } }
        public bool Exists { get { return File.Exists(path); } }

        public VanillaReconnectSettings Load()
        {
            if (!File.Exists(path)) return VanillaReconnectSettings.CreateDefault();
            var value = JsonConvert.DeserializeObject<VanillaReconnectSettings>(File.ReadAllText(path),
                new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
            if (value == null) throw new InvalidDataException("Reconnect settings are empty.");
            value.NormalizeAccounts();
            value.Validate();
            return value;
        }

        public void Save(VanillaReconnectSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var copy = settings.Clone();
            copy.Validate();
            Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(copy, Formatting.Indented));
            if (File.Exists(path))
            {
                string backup = path + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
                File.Replace(temp, path, backup);
            }
            else File.Move(temp, path);
        }

        public string ProtectPassword(string clearText)
        {
            if (string.IsNullOrEmpty(clearText)) return "";
            return VanillaSecretProtector.Protect(clearText);
        }

        public string UnprotectPassword(string protectedText)
        {
            if (string.IsNullOrWhiteSpace(protectedText)) return "";
            return VanillaSecretProtector.Unprotect(protectedText);
        }
    }

    internal static class VanillaSecretProtector
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB { public int cbData; public IntPtr pbData; }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DATA_BLOB input, string description, IntPtr entropy, IntPtr reserved,
            IntPtr prompt, int flags, out DATA_BLOB output);
        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB input, IntPtr description, IntPtr entropy, IntPtr reserved,
            IntPtr prompt, int flags, out DATA_BLOB output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr value);
        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        public static string Protect(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            DATA_BLOB input = ToBlob(bytes), output = new DATA_BLOB();
            try
            {
                if (!CryptProtectData(ref input, "4RTools Vanilla reconnect secret", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, out output))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not protect the password.");
                return Convert.ToBase64String(FromBlob(output));
            }
            finally { FreeInput(input); FreeOutput(output); Array.Clear(bytes, 0, bytes.Length); }
        }

        public static string Unprotect(string protectedText)
        {
            byte[] bytes = Convert.FromBase64String(protectedText);
            DATA_BLOB input = ToBlob(bytes), output = new DATA_BLOB();
            try
            {
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, out output))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "The password was encrypted for a different Windows user or PC. Re-enter it on this PC.");
                byte[] clear = FromBlob(output);
                try { return Encoding.UTF8.GetString(clear); }
                finally { Array.Clear(clear, 0, clear.Length); }
            }
            finally { FreeInput(input); FreeOutput(output); Array.Clear(bytes, 0, bytes.Length); }
        }

        private static DATA_BLOB ToBlob(byte[] bytes)
        {
            var blob = new DATA_BLOB { cbData = bytes.Length, pbData = Marshal.AllocHGlobal(bytes.Length) };
            Marshal.Copy(bytes, 0, blob.pbData, bytes.Length);
            return blob;
        }
        private static byte[] FromBlob(DATA_BLOB blob)
        {
            var bytes = new byte[blob.cbData];
            if (blob.cbData > 0) Marshal.Copy(blob.pbData, bytes, 0, blob.cbData);
            return bytes;
        }
        private static void FreeInput(DATA_BLOB blob) { if (blob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(blob.pbData); }
        private static void FreeOutput(DATA_BLOB blob) { if (blob.pbData != IntPtr.Zero) LocalFree(blob.pbData); }
    }

    internal sealed class VanillaTargetedInput : IDisposable
    {
        private readonly Process process;
        private IntPtr window;
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint type);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);

        public VanillaTargetedInput(int processId)
        {
            process = Process.GetProcessById(processId);
            RefreshWindow();
        }

        public IntPtr Window { get { RefreshWindow(); return window; } }

        public void Activate()
        {
            RefreshWindow();
            ShowWindow(window, 9);
            SetForegroundWindow(window);
        }

        public void ClickNormalized(double x, double y)
        {
            RefreshWindow();
            RECT rect;
            if (!GetClientRect(window, out rect)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read Vanilla client area.");
            int width = Math.Max(1, rect.Right - rect.Left), height = Math.Max(1, rect.Bottom - rect.Top);
            int px = Math.Max(0, Math.Min(width - 1, (int)Math.Round(x * width)));
            int py = Math.Max(0, Math.Min(height - 1, (int)Math.Round(y * height)));
            IntPtr point = new IntPtr((py << 16) | (px & 0xFFFF));
            Post(0x0200, IntPtr.Zero, point);
            Post(0x0201, new IntPtr(1), point);
            Post(0x0202, IntPtr.Zero, point);
        }

        public void Press(Keys key)
        {
            Key(key, false);
            Thread.Sleep(35);
            Key(key, true);
        }

        public void Chord(bool ctrl, bool alt, bool shift, Keys key)
        {
            if (ctrl) Key(Keys.ControlKey, false);
            if (alt) Key(Keys.Menu, false);
            if (shift) Key(Keys.ShiftKey, false);
            Thread.Sleep(35);
            Press(key);
            if (shift) Key(Keys.ShiftKey, true);
            if (alt) Key(Keys.Menu, true);
            if (ctrl) Key(Keys.ControlKey, true);
        }

        public void SelectAll() { Chord(true, false, false, Keys.A); }

        public void TypeText(string text)
        {
            if (text == null) return;
            RefreshWindow();
            foreach (char c in text)
            {
                Post(0x0102, new IntPtr(c), IntPtr.Zero);
                Thread.Sleep(8);
            }
        }

        private void Key(Keys key, bool up)
        {
            RefreshWindow();
            uint scan = MapVirtualKey((uint)key, 4);
            uint flags = 1U | ((scan & 0xFF) << 16);
            if ((scan & 0xFF00) != 0) flags |= 1U << 24;
            if (up) flags |= 0xC0000000U;
            Post(up ? 0x0101U : 0x0100U, new IntPtr((int)key), new IntPtr(unchecked((int)flags)));
        }

        private void Post(uint msg, IntPtr w, IntPtr l)
        {
            if (!PostMessage(window, msg, w, l))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Vanilla window rejected input message 0x" + msg.ToString("X") + ".");
        }

        private void RefreshWindow()
        {
            if (process.HasExited) throw new InvalidOperationException("Vanilla client exited.");
            process.Refresh();
            window = process.MainWindowHandle;
            if (window == IntPtr.Zero || !IsWindow(window)) throw new InvalidOperationException("Vanilla client window is not ready.");
        }

        public void Dispose() { process.Dispose(); }
    }

    internal static class VanillaVisualProbe
    {
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

        public static VanillaVisualState Classify(IntPtr hwnd)
        {
            RECT rect;
            if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out rect)) return VanillaVisualState.Unknown;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width < 320 || height < 240) return VanillaVisualState.Unknown;
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = graphics.GetHdc();
                bool ok;
                try { ok = PrintWindow(hwnd, hdc, 1); }
                finally { graphics.ReleaseHdc(hdc); }
                if (!ok) return VanillaVisualState.Unknown;

                int global = 0, bright = 0, top = 0, topDark = 0, center = 0, centerNeutralLight = 0;
                const int sx = 48, sy = 30;
                for (int gy = 0; gy < sy; gy++)
                {
                    int y = Math.Min(height - 1, (int)((gy + .5) * height / sy));
                    double ny = (gy + .5) / sy;
                    for (int gx = 0; gx < sx; gx++)
                    {
                        int x = Math.Min(width - 1, (int)((gx + .5) * width / sx));
                        double nx = (gx + .5) / sx;
                        Color c = bitmap.GetPixel(x, y);
                        int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
                        int lum = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                        global++;
                        if (lum >= 205) bright++;
                        if (nx <= .27 && ny <= .16)
                        {
                            top++;
                            if (lum < 165) topDark++;
                        }
                        if (nx >= .30 && nx <= .70 && ny >= .40 && ny <= .68)
                        {
                            center++;
                            if (lum >= 175 && max - min <= 50) centerNeutralLight++;
                        }
                    }
                }
                double brightRatio = global == 0 ? 0 : (double)bright / global;
                double hudDark = top == 0 ? 0 : (double)topDark / top;
                double modal = center == 0 ? 0 : (double)centerNeutralLight / center;

                if (modal >= .25 && brightRatio < .55 && hudDark >= .16) return VanillaVisualState.ModalDialog;
                if (hudDark >= .20 && brightRatio < .72) return VanillaVisualState.Gameplay;
                if (brightRatio >= .50 && hudDark < .16) return VanillaVisualState.LoginShell;
                return VanillaVisualState.Unknown;
            }
        }
    }

    public sealed partial class VanillaReconnectSupervisor : IDisposable
    {
        private sealed class Runtime
        {
            public VanillaReconnectAccount Account;
            public int? ProcessId;
            public VanillaReconnectStage Stage = VanillaReconnectStage.WaitingForClient;
            public VanillaVisualState Visual;
            public string Detail = "Waiting";
            public DateTimeOffset StageAt = DateTimeOffset.UtcNow;
            public DateTimeOffset? LoginLikeSince;
            public DateTimeOffset? GameplaySince;
            public DateTimeOffset? LastRecovery;
            public DateTimeOffset? LastPopup;
            public DateTimeOffset? LastLaunch;
            public bool ScriptRunning;
            public bool ResumeSent;
        }

        private readonly object gate = new object();
        private readonly string baseDirectory;
        private readonly VanillaReconnectStore store;
        private readonly Dictionary<string, Runtime> runtimes = new Dictionary<string, Runtime>(StringComparer.OrdinalIgnoreCase);
        private System.Threading.Timer timer;
        private VanillaReconnectSettings settings;
        private bool running, disposed, ticking;
        public event System.Action Updated;
        public event System.Action<string> Logged;

        public VanillaReconnectSupervisor(string baseDirectory)
        {
            this.baseDirectory = Path.GetFullPath(baseDirectory);
            store = new VanillaReconnectStore(this.baseDirectory);
            settings = store.Load();
            RebuildRuntimes();
        }

        public VanillaReconnectSettings Settings { get { lock (gate) return settings.Clone(); } }
        public bool IsRunning { get { lock (gate) return running; } }
        public string SettingsPath { get { return store.FilePath; } }
        public string LogPath { get { return Path.Combine(baseDirectory, "Logs", "reconnect.log"); } }

        public IReadOnlyList<VanillaReconnectStatus> Statuses()
        {
            lock (gate)
            {
                return settings.Accounts.Select(account =>
                {
                    Runtime runtime;
                    if (!runtimes.TryGetValue(account.Id, out runtime))
                        return new VanillaReconnectStatus { AccountId = account.Id, Label = account.Label, Stage = VanillaReconnectStage.Stopped,
                            VisualState = VanillaVisualState.Unknown, Detail = "No runtime state", UpdatedAt = DateTimeOffset.UtcNow };
                    return new VanillaReconnectStatus
                    {
                        AccountId = runtime.Account.Id, Label = runtime.Account.Label, ProcessId = runtime.ProcessId,
                        Stage = runtime.Stage, VisualState = runtime.Visual, Detail = runtime.Detail, UpdatedAt = runtime.StageAt
                    };
                }).ToList();
            }
        }

        public void Apply(VanillaReconnectSettings value, bool save)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var copy = value.Clone(); copy.Validate();
            lock (gate)
            {
                settings = copy;
                RebuildRuntimes();
                if (save) store.Save(settings);
                RecreateTimer();
            }
            RaiseUpdated();
        }

        public string GetPassword(VanillaReconnectAccount account) { return store.UnprotectPassword(account.ProtectedPassword); }
        public string ProtectPassword(string password) { return store.ProtectPassword(password); }

        public void Start()
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(VanillaReconnectSupervisor));
                settings.Validate();
                running = true;
                RebuildRuntimes();
                AdoptExistingClients(true);
                RecreateTimer();
            }
            Log("Reconnect supervisor ON. It uses only ordinary window input and does not alter Gepard or game memory.");
            RaiseUpdated();
        }

        public void Stop()
        {
            lock (gate)
            {
                running = false;
                timer?.Change(Timeout.Infinite, Timeout.Infinite);
                foreach (var runtime in runtimes.Values)
                {
                    runtime.ScriptRunning = false;
                    SetStage(runtime, VanillaReconnectStage.Stopped, "Supervisor stopped");
                }
            }
            Log("Reconnect supervisor OFF.");
            RaiseUpdated();
        }

        public void RunLoginNow(string accountId)
        {
            lock (gate)
            {
                Runtime runtime;
                if (!runtimes.TryGetValue(accountId, out runtime)) throw new ArgumentException("Unknown account.");
                if (!runtime.ProcessId.HasValue) throw new InvalidOperationException("This account has no assigned Vanilla client.");
                QueueLogin(runtime, false, "Manual reconnect requested");
            }
        }

        public int DetectRunningClients()
        {
            int detected;
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(VanillaReconnectSupervisor));
                RebuildRuntimes();
                detected = AdoptExistingClients(running);
            }
            if (detected > 0) Log("Detected and assigned " + detected + " running Vanilla client(s) in account-list order.");
            RaiseUpdated();
            return detected;
        }

        public void RecordTestLog(string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) Log("TEST: " + text);
        }

        private void Tick(object ignored)
        {
            lock (gate)
            {
                if (!running || disposed || ticking) return;
                ticking = true;
            }
            try
            {
                lock (gate) TickLocked();
            }
            catch (Exception ex) { Log("Supervisor tick failed: " + ex.Message); }
            finally { lock (gate) ticking = false; RaiseUpdated(); }
        }

        private void TickLocked()
        {
            var now = DateTimeOffset.UtcNow;
            var desired = settings.Accounts.Where(a => a.Enabled).Take(settings.MaxClients).ToList();
            var desiredIds = new HashSet<string>(desired.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var runtime in runtimes.Values.Where(r => !desiredIds.Contains(r.Account.Id)))
                if (runtime.Stage != VanillaReconnectStage.Stopped) SetStage(runtime, VanillaReconnectStage.Stopped, "Account disabled or above client limit");

            var alive = GetVanillaProcesses();
            var aliveIds = new HashSet<int>(alive.Select(p => p.Id));
            foreach (var runtime in runtimes.Values)
            {
                if (runtime.ProcessId.HasValue && !aliveIds.Contains(runtime.ProcessId.Value))
                {
                    int old = runtime.ProcessId.Value;
                    runtime.ProcessId = null;
                    runtime.ResumeSent = false;
                    runtime.Visual = VanillaVisualState.Unknown;
                    runtime.LoginLikeSince = runtime.GameplaySince = null;
                    runtime.ScriptRunning = false;
                    SetStage(runtime, VanillaReconnectStage.WaitingForClient, "PID " + old + " exited; waiting to relaunch");
                    Log(runtime.Account.Label + ": Vanilla exited; relaunch will be attempted.");
                }
            }

            var claimed = new HashSet<int>(runtimes.Values.Where(r => r.ProcessId.HasValue).Select(r => r.ProcessId.Value));
            foreach (var runtime in desired.Select(a => runtimes[a.Id]))
            {
                if (!runtime.ProcessId.HasValue)
                {
                    var candidate = alive.Where(p => !claimed.Contains(p.Id)).OrderBy(p => SafeStart(p)).FirstOrDefault();
                    if (candidate != null)
                    {
                        Bind(runtime, candidate.Id, runtime.LastLaunch.HasValue, runtime.LastLaunch.HasValue ? "New Vanilla client detected" : "Existing Vanilla client adopted");
                        claimed.Add(candidate.Id);
                    }
                    else if (CanLaunch(runtime, alive.Count, now)) Launch(runtime, now);
                    continue;
                }
                if (runtime.ScriptRunning) continue;
                Probe(runtime, now);
            }
            foreach (var p in alive) p.Dispose();
        }

        private void Probe(Runtime runtime, DateTimeOffset now)
        {
            Process p = null;
            try
            {
                p = Process.GetProcessById(runtime.ProcessId.Value);
                p.Refresh();
                if (p.MainWindowHandle == IntPtr.Zero)
                {
                    SetStage(runtime, VanillaReconnectStage.WaitingForWindow, "Waiting for Vanilla main window");
                    return;
                }

                VanillaVisualState visual = settings.VisualWatchdog ? VanillaVisualProbe.Classify(p.MainWindowHandle) : VanillaVisualState.Unknown;
                runtime.Visual = visual;
                if (visual == VanillaVisualState.Gameplay)
                {
                    runtime.LoginLikeSince = null;
                    if (!runtime.GameplaySince.HasValue) runtime.GameplaySince = now;
                    if (!runtime.ResumeSent && (now - runtime.GameplaySince.Value).TotalMilliseconds >= Math.Max(1500, settings.StageDelayMs))
                    {
                        SendResume(runtime);
                        runtime.ResumeSent = true;
                        SetStage(runtime, VanillaReconnectStage.Online, "Gameplay detected; Autobattle resume hotkey sent once");
                    }
                    else if (runtime.ResumeSent) SetStage(runtime, VanillaReconnectStage.Online, "Gameplay detected");
                    return;
                }

                runtime.GameplaySince = null;
                if (visual == VanillaVisualState.ModalDialog)
                {
                    if (!runtime.LastPopup.HasValue || (now - runtime.LastPopup.Value).TotalMilliseconds >= settings.PopupCooldownMs)
                    {
                        using (var input = new VanillaTargetedInput(runtime.ProcessId.Value)) { input.Activate(); input.Press(Keys.Enter); }
                        runtime.LastPopup = now;
                        runtime.ResumeSent = false;
                        SetStage(runtime, VanillaReconnectStage.AcknowledgingPopup, "Detected modal dialog; pressed Enter to acknowledge it");
                        Log(runtime.Account.Label + ": acknowledged a Vanilla modal dialog.");
                    }
                    return;
                }

                if (visual == VanillaVisualState.LoginShell)
                {
                    if (!runtime.LoginLikeSince.HasValue) runtime.LoginLikeSince = now;
                    if (settings.AutoRecover
                        && (now - runtime.LoginLikeSince.Value).TotalMilliseconds >= settings.LoginStableMs
                        && (!runtime.LastRecovery.HasValue || (now - runtime.LastRecovery.Value).TotalMilliseconds >= settings.RetryBackoffMs))
                    {
                        QueueLogin(runtime, runtime.LastLaunch.HasValue && (!runtime.LastRecovery.HasValue || runtime.LastLaunch.Value > runtime.LastRecovery.Value), "Login shell detected after disconnect");
                    }
                    else SetStage(runtime, VanillaReconnectStage.WaitingForGameplay, "Vanilla login/service screen detected");
                    return;
                }

                runtime.LoginLikeSince = null;
                if (runtime.Stage == VanillaReconnectStage.Launching || runtime.Stage == VanillaReconnectStage.WaitingForWindow)
                {
                    if (runtime.LastLaunch.HasValue && (now - runtime.LastLaunch.Value).TotalMilliseconds >= settings.GepardWaitMs)
                        QueueLogin(runtime, true, "New client reached initial login window");
                }
                else SetStage(runtime, runtime.Stage == VanillaReconnectStage.Online ? VanillaReconnectStage.Online : VanillaReconnectStage.WaitingForGameplay,
                    "Window state is unknown; no recovery input sent");
            }
            catch (Exception ex)
            {
                SetStage(runtime, VanillaReconnectStage.Backoff, "Probe failed: " + ex.Message);
            }
            finally { p?.Dispose(); }
        }

        private void SendResume(Runtime runtime)
        {
            using (var input = new VanillaForegroundInput(runtime.ProcessId.Value))
            {
                input.Chord(runtime.Account.ResumeCtrl, runtime.Account.ResumeAlt, runtime.Account.ResumeShift, (Keys)runtime.Account.ResumeKey);
            }
            Log(runtime.Account.Label + ": sent resume hotkey " + runtime.Account.HotkeyText + ".");
        }

        private bool CanLaunch(Runtime runtime, int aliveCount, DateTimeOffset now)
        {
            if (!settings.AutoRecover || aliveCount >= settings.MaxClients || runtime.ScriptRunning || runtimes.Values.Any(r => r.ScriptRunning && !r.ProcessId.HasValue)) return false;
            if (string.IsNullOrWhiteSpace(settings.LaunchExecutable) || !File.Exists(settings.LaunchExecutable))
            {
                SetStage(runtime, VanillaReconnectStage.NeedsConfiguration, "Set the Vanilla launch executable");
                return false;
            }
            if (runtime.LastLaunch.HasValue && (now - runtime.LastLaunch.Value).TotalMilliseconds < settings.RetryBackoffMs) return false;
            return true;
        }

        private void Launch(Runtime runtime, DateTimeOffset now)
        {
            string executable = settings.LaunchExecutable;
            string arguments = settings.LaunchArguments ?? "";
            string accountId = runtime.Account.Id;
            string label = runtime.Account.Label;
            runtime.LastLaunch = now;
            runtime.ResumeSent = false;
            runtime.ScriptRunning = true;
            SetStage(runtime, VanillaReconnectStage.Launching,
                VanillaPatcherLauncher.IsPatcher(executable)
                    ? "Starting patcher.exe and waiting for GAME START"
                    : "Starting configured Vanilla executable");

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string error = null;
                bool aborted = false;
                try
                {
                    VanillaPatcherLauncher.Launch(executable, arguments,
                        message => Log(label + ": " + message),
                        () =>
                        {
                            lock (gate)
                            {
                                aborted = disposed || !running;
                                return aborted;
                            }
                        });
                }
                catch (Exception ex) { error = ex.Message; }

                lock (gate)
                {
                    Runtime current;
                    if (!runtimes.TryGetValue(accountId, out current)) return;
                    current.ScriptRunning = false;
                    if (aborted || disposed || !running)
                    {
                        SetStage(current, VanillaReconnectStage.Stopped, "Supervisor stopped");
                    }
                    else if (error == null)
                    {
                        SetStage(current, VanillaReconnectStage.WaitingForWindow,
                            "Launcher completed; waiting for Vanilla MMO window");
                    }
                    else
                    {
                        SetStage(current, VanillaReconnectStage.Backoff, "Launch failed: " + error);
                        Log(label + ": launch failed: " + error);
                    }
                }
                RaiseUpdated();
            });
        }
        private void Bind(Runtime runtime, int pid, bool freshLaunch, string detail)
        {
            runtime.ProcessId = pid;
            // Never toggle Autobattle merely because 4RTools adopted an already-running client.
            // A fresh launch or a relog sequence explicitly arms the one-shot resume hotkey.
            runtime.ResumeSent = !freshLaunch;
            runtime.LoginLikeSince = runtime.GameplaySince = null;
            SetStage(runtime, freshLaunch ? VanillaReconnectStage.Launching : VanillaReconnectStage.WaitingForGameplay, detail + " (PID " + pid + ")");
        }

        private void QueueLogin(Runtime runtime, bool freshLaunch, string reason)
        {
            if (runtime.ScriptRunning || !runtime.ProcessId.HasValue) return;
            if (string.IsNullOrWhiteSpace(runtime.Account.UserName) || string.IsNullOrWhiteSpace(runtime.Account.ProtectedPassword))
            {
                SetStage(runtime, VanillaReconnectStage.NeedsConfiguration, "Username/password missing");
                return;
            }
            runtime.ScriptRunning = true;
            runtime.LastRecovery = DateTimeOffset.UtcNow;
            runtime.ResumeSent = false;
            SetStage(runtime, VanillaReconnectStage.LoggingIn, reason);
            int pid = runtime.ProcessId.Value;
            var account = runtime.Account.Clone();
            var config = settings.Clone();
            ThreadPool.QueueUserWorkItem(_ => LoginWorker(runtime.Account.Id, pid, account, config, freshLaunch));
        }

        private void LoginWorker(string accountId, int pid, VanillaReconnectAccount account, VanillaReconnectSettings config, bool freshLaunch)
        {
            string error = null;
            try
            {
                string password = store.UnprotectPassword(account.ProtectedPassword);
                if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("Password is empty.");
                WaitForWindow(pid, 60000);
                using (var input = new VanillaForegroundInput(pid))
                {
                    input.Activate();
                    if (freshLaunch)
                    {
                        Thread.Sleep(config.GepardWaitMs);
                        input.Activate();
                        input.ClickNormalized(config.Anchors.ServiceListX, config.Anchors.ServiceListY);
                        input.Press(Keys.Home);
                        for (int i = 0; i < (int)config.Proxy; i++) input.Press(Keys.Down);
                        input.Press(Keys.Enter);
                        Thread.Sleep(config.StageDelayMs);
                    }

                    input.Activate();
                    input.ClickNormalized(config.Anchors.UserNameX, config.Anchors.UserNameY);
                    Thread.Sleep(180);
                    input.ReplaceFocusedText(account.UserName);
                    input.Press(Keys.Tab);
                    Thread.Sleep(180);
                    input.ReplaceFocusedText(password);
                    Thread.Sleep(220);
                    input.Press(Keys.Enter);
                    Thread.Sleep(config.StageDelayMs);

                    input.ClickNormalized(config.Anchors.ServiceListX, config.Anchors.ServiceListY);
                    input.Press(Keys.Home);
                    input.Press(Keys.Enter);
                    Thread.Sleep(config.StageDelayMs);

                    int slot = Math.Max(1, Math.Min(15, account.CharacterSlot)) - 1;
                    int col = slot % 5, row = slot / 5;
                    input.ClickNormalized(config.Anchors.CharacterGridX + col * config.Anchors.CharacterStepX,
                        config.Anchors.CharacterGridY + row * config.Anchors.CharacterStepY);
                    Thread.Sleep(350);
                    input.ClickNormalized(config.Anchors.GameStartX, config.Anchors.GameStartY);
                    Thread.Sleep(config.GameLoadMs);
                }
            }
            catch (Exception ex) { error = ex.Message; }
            finally
            {
                lock (gate)
                {
                    Runtime runtime;
                    if (runtimes.TryGetValue(accountId, out runtime) && runtime.ProcessId == pid)
                    {
                        runtime.ScriptRunning = false;
                        runtime.LoginLikeSince = runtime.GameplaySince = null;
                        if (error == null) SetStage(runtime, VanillaReconnectStage.WaitingForGameplay,
                            "Login sequence completed; waiting for gameplay before sending " + account.HotkeyText);
                        else SetStage(runtime, VanillaReconnectStage.Backoff, "Login sequence failed: " + error);
                    }
                }
                if (error == null) Log(account.Label + ": login sequence completed; no password was logged.");
                else Log(account.Label + ": login sequence failed: " + error);
                RaiseUpdated();
            }
        }

        private static void WaitForWindow(int pid, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                using (var p = Process.GetProcessById(pid))
                {
                    p.Refresh();
                    if (p.MainWindowHandle != IntPtr.Zero) return;
                    if (p.HasExited) throw new InvalidOperationException("Vanilla exited while waiting for its window.");
                }
                Thread.Sleep(250);
            }
            throw new TimeoutException("Vanilla main window did not appear in time.");
        }

        private int AdoptExistingClients(bool supervise)
        {
            var existing = GetVanillaProcesses().OrderBy(p => SafeStart(p)).ToList();
            try
            {
                var aliveIds = new HashSet<int>(existing.Select(p => p.Id));
                var enabled = settings.Accounts.Where(a => a.Enabled).Take(settings.MaxClients).ToList();
                var claimed = new HashSet<int>();
                int assigned = 0;
                foreach (var account in enabled)
                {
                    Runtime runtime = runtimes[account.Id];
                    Process match = null;
                    if (runtime.ProcessId.HasValue && aliveIds.Contains(runtime.ProcessId.Value))
                        match = existing.FirstOrDefault(p => p.Id == runtime.ProcessId.Value);
                    if (match == null) match = existing.FirstOrDefault(p => !claimed.Contains(p.Id));
                    if (match == null) continue;
                    claimed.Add(match.Id);
                    assigned++;
                    runtime.ProcessId = match.Id;
                    runtime.ResumeSent = true;
                    runtime.ScriptRunning = false;
                    runtime.Visual = VanillaVisualState.Unknown;
                    runtime.LoginLikeSince = runtime.GameplaySince = null;
                    SetStage(runtime, supervise ? VanillaReconnectStage.WaitingForGameplay : VanillaReconnectStage.Stopped,
                        supervise ? "Existing Vanilla client adopted (PID " + match.Id + ")"
                            : "Running Vanilla client detected (PID " + match.Id + "); start supervisor to monitor");
                }
                return assigned;
            }
            finally { foreach (var process in existing) process.Dispose(); }
        }

        private List<Process> GetVanillaProcesses()
        {
            return Process.GetProcessesByName("Vanilla MMO").Where(p =>
            {
                try { return !p.HasExited; }
                catch { p.Dispose(); return false; }
            }).ToList();
        }

        private static DateTime SafeStart(Process p)
        {
            try { return p.StartTime; }
            catch { return DateTime.MaxValue; }
        }

        private void RebuildRuntimes()
        {
            var wanted = new HashSet<string>(settings.Accounts.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
            foreach (string old in runtimes.Keys.Where(k => !wanted.Contains(k)).ToList()) runtimes.Remove(old);
            foreach (var account in settings.Accounts)
            {
                Runtime runtime;
                if (!runtimes.TryGetValue(account.Id, out runtime))
                {
                    runtime = new Runtime { Account = account.Clone() };
                    runtimes.Add(account.Id, runtime);
                }
                else runtime.Account = account.Clone();
            }
        }

        private void RecreateTimer()
        {
            if (timer == null) timer = new System.Threading.Timer(Tick, null, running ? 250 : Timeout.Infinite, running ? settings.PollMs : Timeout.Infinite);
            else timer.Change(running ? 250 : Timeout.Infinite, running ? settings.PollMs : Timeout.Infinite);
        }

        private void SetStage(Runtime runtime, VanillaReconnectStage stage, string detail)
        {
            if (runtime.Stage == stage && runtime.Detail == detail) return;
            runtime.Stage = stage;
            runtime.Detail = detail;
            runtime.StageAt = DateTimeOffset.UtcNow;
        }

        private void Log(string text)
        {
            string line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text;
            try
            {
                string dir = Path.Combine(baseDirectory, "Logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "reconnect.log"), line + Environment.NewLine);
            }
            catch { }
            var handler = Logged;
            if (handler != null) handler(line);
        }

        private void RaiseUpdated()
        {
            var handler = Updated;
            if (handler != null) handler();
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                running = false;
                timer?.Dispose();
                timer = null;
            }
        }
    }

    /// <summary>
    /// Normal-start bootstrap for the Vanilla fork. It deliberately does nothing for developer/headless -- commands.
    /// The tray entry keeps reconnect recovery available without replacing the original 4RTools UI.
    /// </summary>
    public static class VanillaReconnectBootstrap
    {
        private static readonly object Gate = new object();
        private static bool initialized, started;
        private static VanillaReconnectSupervisor supervisor;
        private static NotifyIcon tray;
        private static VanillaReconnectForm form;

        public static void Initialize()
        {
            lock (Gate)
            {
                if (initialized) return;
                initialized = true;
                string[] args = Environment.GetCommandLineArgs();
                if (args.Skip(1).Any(a => a.StartsWith("--", StringComparison.OrdinalIgnoreCase))) return;
                Application.Idle += OnFirstIdle;
                Application.ApplicationExit += OnExit;
            }
        }

        private static void OnFirstIdle(object sender, EventArgs e)
        {
            lock (Gate)
            {
                if (started) return;
                started = true;
                Application.Idle -= OnFirstIdle;
                supervisor = new VanillaReconnectSupervisor(AppDomain.CurrentDomain.BaseDirectory);
                tray = new NotifyIcon
                {
                    Text = "4RTools Vanilla reconnect",
                    Icon = SystemIcons.Application,
                    Visible = true,
                    ContextMenuStrip = BuildMenu()
                };
                tray.DoubleClick += (s, a) => ShowManager();
                var current = supervisor.Settings;
                if (current.StartWith4RTools) supervisor.Start();
                if (!new VanillaReconnectStore(AppDomain.CurrentDomain.BaseDirectory).Exists)
                    ShowManager();
            }
        }

        private static ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Vanilla reconnect manager", null, (s, e) => ShowManager());
            menu.Items.Add("Start reconnect supervisor", null, (s, e) => { supervisor?.Start(); });
            menu.Items.Add("Stop reconnect supervisor", null, (s, e) => { supervisor?.Stop(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit 4RTools", null, (s, e) => Application.Exit());
            return menu;
        }

        public static void ShowManager()
        {
            if (supervisor == null) return;
            if (form == null || form.IsDisposed) form = new VanillaReconnectForm(supervisor);
            if (!form.Visible) form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.BringToFront();
            form.Activate();
        }

        private static void OnExit(object sender, EventArgs e)
        {
            lock (Gate)
            {
                try { supervisor?.Dispose(); } catch { }
                try { if (tray != null) { tray.Visible = false; tray.Dispose(); } } catch { }
                supervisor = null; tray = null;
            }
        }
    }

    internal sealed partial class VanillaReconnectForm : Form
    {
        private readonly VanillaReconnectSupervisor supervisor;
        private VanillaReconnectSettings settings;
        private readonly TextBox launchPath = new TextBox { Width = 520 };
        private readonly TextBox launchArgs = new TextBox { Width = 300 };
        private readonly ComboBox proxy = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown maxClients = new NumericUpDown { Minimum = 1, Maximum = 2, Width = 55 };
        private readonly CheckBox startWithApp = new CheckBox { Text = "Start supervisor with 4RTools", AutoSize = true };
        private readonly CheckBox autoRecover = new CheckBox { Text = "Auto relaunch/relogin", AutoSize = true };
        private readonly CheckBox visualWatchdog = new CheckBox { Text = "Detect login screens/popups visually", AutoSize = true };
        private readonly DataGridView accounts = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        private readonly ListView status = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        private readonly TextBox log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly Label runState = new Label { AutoSize = true };
        private readonly Label testState = new Label { AutoSize = true, ForeColor = Color.DarkSlateBlue };
        private readonly ToolTip help = new ToolTip { InitialDelay = 650, ReshowDelay = 200, AutoPopDelay = 30000, ShowAlways = true };
        private bool exitRequested;
        private bool testRunning;
        private int testGeneration;

        public VanillaReconnectForm(VanillaReconnectSupervisor supervisor)
        {
            this.supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
            Text = "4RTools Vanilla — Restart & Relog";
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1180, 850);
            MinimumSize = new Size(1050, 720);
            BuildUi();
            ConfigureHoverHelp();
            supervisor.Updated += SupervisorUpdated;
            supervisor.Logged += SupervisorLogged;
            LoadFromSupervisor();
            try { supervisor.DetectRunningClients(); } catch { }
            RefreshStatus();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 4, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 47));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 20));

            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            var pathRow = Flow();
            pathRow.Controls.Add(new Label { Text = "Launcher EXE (Vanilla Launcher.exe / patcher.exe)", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            pathRow.Controls.Add(launchPath);
            AddButton(pathRow, "Browse…", Browse);
            top.Controls.Add(pathRow);

            var opts = Flow();
            opts.Controls.Add(new Label { Text = "Arguments", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            opts.Controls.Add(launchArgs);
            opts.Controls.Add(new Label { Text = "Proxy", AutoSize = true, Margin = new Padding(12, 8, 8, 0) });
            proxy.DataSource = Enum.GetValues(typeof(VanillaProxyRoute));
            opts.Controls.Add(proxy);
            opts.Controls.Add(new Label { Text = "Clients", AutoSize = true, Margin = new Padding(12, 8, 8, 0) });
            opts.Controls.Add(maxClients);
            top.Controls.Add(opts);

            var switches = Flow();
            switches.Controls.Add(startWithApp); switches.Controls.Add(autoRecover); switches.Controls.Add(visualWatchdog);
            top.Controls.Add(switches);

            var commands = Flow();
            AddButton(commands, "Save", Save);
            AddButton(commands, "START SUPERVISOR", StartSupervisor);
            AddButton(commands, "STOP", () => supervisor.Stop());
            AddButton(commands, "DETECT RUNNING CLIENTS", DetectRunningClients);
            AddButton(commands, "OPEN LOG", OpenLog);
            AddButton(commands, "COPY LOG", CopyLog);
            runState.Font = new Font(Font, FontStyle.Bold);
            runState.Margin = new Padding(16, 8, 0, 0);
            commands.Controls.Add(runState);
            top.Controls.Add(commands);

            var tests = Flow();
            AddButton(tests, "TEST STARTUP (clients closed)", TestStartup);
            AddButton(tests, "TEST RESTART RECOVERY", TestRestartRecovery);
            AddButton(tests, "ARM MANUAL NETWORK-DROP TEST", ArmManualNetworkDropTest);
            testState.Margin = new Padding(16, 8, 0, 0);
            tests.Controls.Add(testState);
            top.Controls.Add(tests);
            top.Controls.Add(BuildStepTests());

            var info = new Label
            {
                AutoSize = true, MaximumSize = new Size(1100, 0),
                Text = "Passwords are encrypted with Windows DPAPI for this Windows user and are never written to logs. " +
                       "Copying the folder to another PC does not copy usable passwords; enter them once on each PC. " +
                       "The relogger uses ordinary window input only. It does not bypass or modify Gepard.",
                ForeColor = Color.DimGray, Margin = new Padding(0, 6, 0, 8)
            };
            top.Controls.Add(info);
            root.Controls.Add(top, 0, 0);

            accounts.Columns.Add("Enabled", "Enabled");
            accounts.Columns.Add("Label", "Account");
            accounts.Columns.Add("User", "Username");
            accounts.Columns.Add("Slot", "Char slot");
            accounts.Columns.Add("Hotkey", "Resume hotkey");
            accounts.Columns.Add("Secret", "Password");
            var accountBox = new GroupBox { Text = "Accounts on this PC (Vanilla max 2 active clients)", Dock = DockStyle.Fill, Padding = new Padding(8) };
            var accountLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            accountLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            accountLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            accountLayout.Controls.Add(accounts, 0, 0);
            var accountButtons = Flow();
            AddButton(accountButtons, "Add", AddAccount);
            AddButton(accountButtons, "Edit", EditAccount);
            AddButton(accountButtons, "Remove", RemoveAccount);
            AddButton(accountButtons, "Run login now (selected)", ManualLogin);
            accountLayout.Controls.Add(accountButtons, 0, 1);
            accountBox.Controls.Add(accountLayout);
            root.Controls.Add(accountBox, 0, 1);

            status.Columns.Add("Account", 180);
            status.Columns.Add("PID", 80);
            status.Columns.Add("Stage", 150);
            status.Columns.Add("Screen", 120);
            status.Columns.Add("Detail", 610);
            var statusBox = new GroupBox { Text = "Live recovery status (configured account -> assigned PID, detected screen and recovery stage)", Dock = DockStyle.Fill, Padding = new Padding(8) };
            statusBox.Controls.Add(status);
            root.Controls.Add(statusBox, 0, 2);

            var logBox = new GroupBox { Text = "Reconnect log", Dock = DockStyle.Fill, Padding = new Padding(8) };
            logBox.Controls.Add(log);
            root.Controls.Add(logBox, 0, 3);
            Controls.Add(root);
        }

        private static FlowLayoutPanel Flow()
        {
            return new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Padding = new Padding(0, 3, 0, 3) };
        }

        private static void AddButton(Control parent, string text, System.Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Margin = new Padding(4) };
            button.Click += (s, e) => action();
            parent.Controls.Add(button);
        }

        private void ConfigureHoverHelp()
        {
            help.SetToolTip(launchPath, "Path to Vanilla Launcher.exe / patcher.exe. Recovery starts it and presses GAME START before waiting for Vanilla/Gepard.");
            help.SetToolTip(launchArgs, "Optional launcher command-line arguments. Leave blank unless Vanilla requires them.");
            help.SetToolTip(proxy, "Proxy chosen on Vanilla's first Select Service screen.");
            help.SetToolTip(maxClients, "Maximum supervised Vanilla clients on this PC. Normally leave this at 2.");
            help.SetToolTip(startWithApp, "If checked, opening 4RTools automatically starts recovery monitoring. If unchecked, 4RTools can be open while the supervisor remains stopped.");
            help.SetToolTip(autoRecover, "Automatically relaunch and relog clients that close or return to a login screen.");
            help.SetToolTip(visualWatchdog, "Classifies Vanilla screenshots as gameplay/login/modal states. This does not modify the game or Gepard.");
            help.SetToolTip(accounts, "Your one or two configured account profiles. Select a row before using selected-account actions.");
            help.SetToolTip(status, "Runtime state only: account -> assigned PID -> recovery stage -> detected screen -> detail. These are not additional accounts.");
            help.SetToolTip(log, "Reconnect/test log. Secret contents are never written here.");
            TipByText(this, "Save", "Save exactly the settings and account rows currently shown.");
            TipByText(this, "START SUPERVISOR", "Start continuous recovery monitoring now. Existing clients are adopted; missing clients can be relaunched.");
            TipByText(this, "STOP", "Stop automatic recovery. Running Vanilla clients stay open.");
            TipByText(this, "DETECT RUNNING CLIENTS", "Find currently running Vanilla MMO.exe processes and assign them to configured account rows.");
            TipByText(this, "TEST STARTUP (clients closed)", "Full cold-start test: launcher -> GAME START -> proxy -> login -> server -> character -> resume hotkey.");
            TipByText(this, "TEST RESTART RECOVERY", "Close detected Vanilla windows normally and verify the full relaunch/relogin recovery path.");
            TipByText(this, "ARM MANUAL NETWORK-DROP TEST", "Arm a five-minute recovery test while you briefly disconnect/reconnect internet yourself.");
            TipByText(this, "OPEN LOG", "Open Logs\\reconnect.log in your default text editor.");
            TipByText(this, "COPY LOG", "Copy the complete reconnect log to the clipboard for pasting into ChatGPT.");
            ConfigureStepTestHoverHelp();
        }

        private void TipByText(Control root, string textValue, string tip)
        {
            foreach (Control child in root.Controls)
            {
                if (string.Equals(child.Text, textValue, StringComparison.Ordinal)) help.SetToolTip(child, tip);
                if (child.HasChildren) TipByText(child, textValue, tip);
            }
        }

        private void OpenLog()
        {
            try
            {
                string path = supervisor.LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path)) File.WriteAllText(path, string.Empty);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open reconnect log", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void CopyLog()
        {
            try
            {
                string path = supervisor.LogPath;
                string contents = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
                Clipboard.SetText(contents.Length == 0 ? "(reconnect log is empty)" : contents);
                testState.Text = "Reconnect log copied to clipboard.";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Copy reconnect log", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void LoadFromSupervisor()
        {
            settings = supervisor.Settings;
            launchPath.Text = settings.LaunchExecutable ?? "";
            launchArgs.Text = settings.LaunchArguments ?? "";
            proxy.SelectedItem = settings.Proxy;
            maxClients.Value = settings.MaxClients;
            startWithApp.Checked = settings.StartWith4RTools;
            autoRecover.Checked = settings.AutoRecover;
            visualWatchdog.Checked = settings.VisualWatchdog;
            RefreshAccounts();
        }

        private void ReadTop()
        {
            settings.LaunchExecutable = launchPath.Text.Trim();
            settings.LaunchArguments = launchArgs.Text;
            settings.Proxy = proxy.SelectedItem is VanillaProxyRoute ? (VanillaProxyRoute)proxy.SelectedItem : VanillaProxyRoute.Tokyo;
            settings.MaxClients = (int)maxClients.Value;
            settings.StartWith4RTools = startWithApp.Checked;
            settings.AutoRecover = autoRecover.Checked;
            settings.VisualWatchdog = visualWatchdog.Checked;
        }

        private void Save()
        {
            try { ReadTop(); supervisor.Apply(settings, true); LoadFromSupervisor(); MessageBox.Show(this, "Reconnect settings saved.", "4RTools Vanilla"); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot save reconnect settings", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void StartSupervisor()
        {
            try { ReadTop(); supervisor.Apply(settings, true); supervisor.Start(); RefreshStatus(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot start reconnect supervisor", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void Browse()
        {
            using (var dialog = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*", CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) launchPath.Text = dialog.FileName;
        }

        private void DetectRunningClients()
        {
            try
            {
                ReadTop(); supervisor.Apply(settings, true); LoadFromSupervisor();
                int detected = supervisor.DetectRunningClients();
                RefreshStatus();
                MessageBox.Show(this, detected == 0 ? "No running Vanilla MMO clients were found."
                    : detected + " running Vanilla client(s) detected and assigned in account-list order.",
                    "Running clients", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Running clients", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void RefreshAccounts()
        {
            accounts.Rows.Clear();
            foreach (var account in settings.Accounts)
            {
                int row = accounts.Rows.Add(account.Enabled ? "Yes" : "No", account.Label, account.UserName,
                    account.CharacterSlot, account.HotkeyText, string.IsNullOrWhiteSpace(account.ProtectedPassword) ? "Not set" : "Encrypted");
                accounts.Rows[row].Tag = account.Id;
            }
        }

        private VanillaReconnectAccount SelectedAccount()
        {
            if (accounts.SelectedRows.Count == 0) return null;
            string id = accounts.SelectedRows[0].Tag as string;
            return settings.Accounts.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private void AddAccount()
        {
            if (settings.Accounts.Count >= 2)
            {
                MessageBox.Show(this, "Vanilla allows two managed account profiles on this PC. Edit or remove an existing row first.");
                return;
            }
            var account = new VanillaReconnectAccount { Label = "Client " + (settings.Accounts.Count + 1) };
            using (var dialog = new VanillaReconnectAccountDialog(supervisor, account))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                settings.Accounts.Add(dialog.Account);
                RefreshAccounts();
            }
        }

        private void EditAccount()
        {
            var selected = SelectedAccount();
            if (selected == null) return;
            using (var dialog = new VanillaReconnectAccountDialog(supervisor, selected.Clone()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                int index = settings.Accounts.FindIndex(a => a.Id == selected.Id);
                settings.Accounts[index] = dialog.Account;
                RefreshAccounts();
            }
        }

        private void RemoveAccount()
        {
            var selected = SelectedAccount();
            if (selected == null) return;
            if (settings.Accounts.Count <= 1) { MessageBox.Show(this, "Keep at least one account profile."); return; }
            settings.Accounts.RemoveAll(a => a.Id == selected.Id);
            RefreshAccounts();
        }

        private void ManualLogin()
        {
            var selected = SelectedAccount();
            if (selected == null) return;
            try { ReadTop(); supervisor.Apply(settings, true); supervisor.RunLoginNow(selected.Id); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Manual reconnect"); }
        }

        private VanillaReconnectAccount[] TestAccounts()
        {
            var configured = settings.Accounts.Where(a => a.Enabled).Take(settings.MaxClients).ToArray();
            if (configured.Length == 0) throw new InvalidOperationException("Enable at least one account first.");
            foreach (var account in configured)
                if (string.IsNullOrWhiteSpace(account.UserName) || string.IsNullOrWhiteSpace(account.ProtectedPassword))
                    throw new InvalidOperationException("Account '" + account.Label + "' needs username and password before an end-to-end test.");
            return configured;
        }

        private void TestStartup()
        {
            try
            {
                if (testRunning) throw new InvalidOperationException("A recovery test is already running.");
                ReadTop(); supervisor.Apply(settings, true); LoadFromSupervisor();
                var configured = TestAccounts();
                var live = Process.GetProcessesByName("Vanilla MMO");
                try
                {
                    if (live.Length > 0)
                    {
                        MessageBox.Show(this, "The cold-start test requires the Vanilla clients to be closed first. Close them, then press TEST STARTUP again.",
                            "Startup test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }
                finally { foreach (var process in live) process.Dispose(); }
                int generation = BeginTest("STARTUP TEST: launching " + configured.Length + " configured client(s)...");
                supervisor.Start();
                WaitForOnline(generation, "Startup test", configured.Select(a => a.Id).ToArray(), false, 300000);
            }
            catch (Exception ex) { FailTestImmediately("Startup test", ex); }
        }

        private void TestRestartRecovery()
        {
            try
            {
                if (testRunning) throw new InvalidOperationException("A recovery test is already running.");
                ReadTop(); supervisor.Apply(settings, true); LoadFromSupervisor();
                var configured = TestAccounts();
                if (!supervisor.IsRunning) supervisor.Start();
                supervisor.DetectRunningClients();
                var ids = configured.Select(a => a.Id).ToArray();
                var current = supervisor.Statuses().Where(s => ids.Contains(s.AccountId)).ToArray();
                if (current.Length != ids.Length || current.Any(s => !s.ProcessId.HasValue))
                    throw new InvalidOperationException("Every configured account must have a detected running Vanilla client before the restart-recovery test.");
                if (MessageBox.Show(this, "This test closes the detected Vanilla client windows normally and verifies that the supervisor launches, logs in and restores all configured clients. Continue?",
                    "Test restart recovery", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                int generation = BeginTest("RESTART TEST: closing clients and waiting for full relaunch...");
                WaitForOnline(generation, "Restart recovery test", ids, true, 300000);
                foreach (int pid in current.Select(s => s.ProcessId.Value))
                {
                    using (var process = Process.GetProcessById(pid))
                    {
                        if (!process.CloseMainWindow()) throw new InvalidOperationException("Vanilla PID " + pid + " did not accept a normal window-close request.");
                    }
                }
                supervisor.RecordTestLog("Restart recovery test requested normal close for " + current.Length + " Vanilla client(s).");
            }
            catch (Exception ex) { FailTestImmediately("Restart recovery test", ex); }
        }

        private void ArmManualNetworkDropTest()
        {
            try
            {
                if (testRunning) throw new InvalidOperationException("A recovery test is already running.");
                ReadTop(); supervisor.Apply(settings, true); LoadFromSupervisor();
                var configured = TestAccounts();
                if (!supervisor.IsRunning) supervisor.Start();
                supervisor.DetectRunningClients();
                var ids = configured.Select(a => a.Id).ToArray();
                var current = supervisor.Statuses().Where(s => ids.Contains(s.AccountId)).ToArray();
                if (current.Length != ids.Length || current.Any(s => !s.ProcessId.HasValue))
                    throw new InvalidOperationException("Every configured account must have a detected running Vanilla client before arming the network-drop test.");
                int generation = BeginTest("NETWORK-DROP TEST ARMED: briefly disconnect/reconnect internet now...");
                supervisor.RecordTestLog("Manual network-drop recovery test armed; waiting for a detected disconnect and return to Online.");
                WaitForOnline(generation, "Manual network-drop recovery test", ids, true, 300000);
                MessageBox.Show(this, "The test is armed for five minutes. Briefly disconnect your internet connection, wait long enough for Vanilla to drop to its login/reconnect state, then reconnect. 4RTools will report PASS only after it observes the disruption and all configured clients return Online.",
                    "Manual network-drop test armed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { FailTestImmediately("Manual network-drop test", ex); }
        }

        private int BeginTest(string text)
        {
            testRunning = true; testGeneration++; testState.Text = text; testState.ForeColor = Color.DarkSlateBlue; return testGeneration;
        }

        private void WaitForOnline(int generation, string testName, string[] accountIds, bool requireTransition, int timeoutMs)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool sawTransition = !requireTransition;
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    var sample = supervisor.Statuses().Where(s => accountIds.Contains(s.AccountId)).ToArray();
                    if (sample.Any(s => s.Stage != VanillaReconnectStage.Online)) sawTransition = true;
                    if (sample.Any(s => s.Stage == VanillaReconnectStage.Error || s.Stage == VanillaReconnectStage.NeedsConfiguration))
                    {
                        CompleteTest(generation, false, testName + " stopped: " + string.Join(" | ", sample.Select(s => s.Label + ": " + s.Detail)));
                        return;
                    }
                    if (sawTransition && sample.Length == accountIds.Length && sample.All(s => s.Stage == VanillaReconnectStage.Online && s.ProcessId.HasValue))
                    {
                        CompleteTest(generation, true, testName + " passed: all configured clients returned Online through the normal recovery path.");
                        return;
                    }
                    Thread.Sleep(500);
                }
                CompleteTest(generation, false, testName + " timed out. Check Live recovery status and the reconnect log for the exact stage that stopped progressing.");
            });
        }

        private void CompleteTest(int generation, bool success, string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)(() => CompleteTest(generation, success, message))); return; }
            if (generation != testGeneration) return;
            testRunning = false; testState.Text = success ? "TEST PASSED" : "TEST FAILED";
            testState.ForeColor = success ? Color.DarkGreen : Color.DarkRed;
            MessageBox.Show(this, message, "4RTools Vanilla test", MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void FailTestImmediately(string name, Exception ex)
        {
            testRunning = false; testState.Text = "TEST FAILED"; testState.ForeColor = Color.DarkRed;
            MessageBox.Show(this, ex.Message, name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SupervisorUpdated()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)RefreshStatus); return; }
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (IsDisposed) return;
            runState.Text = supervisor.IsRunning ? "RUNNING" : "STOPPED";
            runState.ForeColor = supervisor.IsRunning ? Color.DarkGreen : Color.DarkRed;
            status.BeginUpdate();
            try
            {
                status.Items.Clear();
                foreach (var item in supervisor.Statuses())
                {
                    var row = new ListViewItem(item.Label);
                    row.SubItems.Add(item.ProcessId.HasValue ? item.ProcessId.Value.ToString() : "—");
                    row.SubItems.Add(item.Stage.ToString());
                    row.SubItems.Add(item.VisualState.ToString());
                    row.SubItems.Add(item.Detail ?? "");
                    status.Items.Add(row);
                }
            }
            finally { status.EndUpdate(); }
        }

        private void SupervisorLogged(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((System.Action<string>)SupervisorLogged, line); return; }
            log.AppendText(line + Environment.NewLine);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing)
            {
                exitRequested = true;
                e.Cancel = true;
                BeginInvoke((MethodInvoker)Application.Exit);
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                supervisor.Updated -= SupervisorUpdated;
                supervisor.Logged -= SupervisorLogged;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class VanillaReconnectAccountDialog : Form
    {
        private readonly VanillaReconnectSupervisor supervisor;
        private readonly CheckBox enabled = new CheckBox { Text = "Enabled on this PC", AutoSize = true };
        private readonly TextBox label = new TextBox { Width = 260 };
        private readonly TextBox user = new TextBox { Width = 260 };
        private readonly TextBox password = new TextBox { Width = 260, UseSystemPasswordChar = true };
        private readonly NumericUpDown slot = new NumericUpDown { Minimum = 1, Maximum = 15, Width = 80 };
        private readonly TextBox hotkey = new TextBox { Width = 160, ReadOnly = true };
        private int key;
        private bool ctrl, alt, shift;
        public VanillaReconnectAccount Account { get; private set; }

        public VanillaReconnectAccountDialog(VanillaReconnectSupervisor supervisor, VanillaReconnectAccount account)
        {
            this.supervisor = supervisor;
            Account = account;
            Text = "Vanilla account";
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(470, 360);
            KeyPreview = true;
            Build();
            enabled.Checked = account.Enabled;
            label.Text = account.Label;
            user.Text = account.UserName;
            slot.Value = account.CharacterSlot;
            key = account.ResumeKey; ctrl = account.ResumeCtrl; alt = account.ResumeAlt; shift = account.ResumeShift;
            UpdateHotkey();
            try { password.Text = supervisor.GetPassword(account); }
            catch { password.Text = ""; }
            hotkey.KeyDown += CaptureHotkey;
        }

        private void Build()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 8 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(table, 0, "", enabled);
            AddRow(table, 1, "Label", label);
            AddRow(table, 2, "Username", user);
            AddRow(table, 3, "Password", password);
            AddRow(table, 4, "Character slot (1–15)", slot);
            AddRow(table, 5, "Resume hotkey", hotkey);
            var hint = new Label { AutoSize = true, MaximumSize = new Size(290, 0), Text = "Click the hotkey box and press the combination (default Ctrl+2).", ForeColor = Color.DimGray };
            table.Controls.Add(hint, 1, 6);
            var buttons = NewFlow();
            var ok = new Button { Text = "Save", DialogResult = DialogResult.None, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            ok.Click += Save;
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            table.Controls.Add(buttons, 1, 7);
            Controls.Add(table);
            AcceptButton = ok; CancelButton = cancel;
        }

        private static FlowLayoutPanel NewFlow()
        {
            return new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        }

        private static void AddRow(TableLayoutPanel table, int row, string caption, Control control)
        {
            if (!string.IsNullOrEmpty(caption)) table.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 7, 8, 0) }, 0, row);
            table.Controls.Add(control, 1, row);
        }

        private void CaptureHotkey(object sender, KeyEventArgs e)
        {
            Keys candidate = e.KeyCode;
            if (candidate == Keys.ControlKey || candidate == Keys.ShiftKey || candidate == Keys.Menu) return;
            key = (int)candidate; ctrl = e.Control; alt = e.Alt; shift = e.Shift;
            UpdateHotkey();
            e.SuppressKeyPress = true; e.Handled = true;
        }

        private void UpdateHotkey()
        {
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            parts.Add(((Keys)key).ToString());
            hotkey.Text = string.Join("+", parts);
        }

        private void Save(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(label.Text)) throw new ArgumentException("Enter an account label.");
                if (string.IsNullOrWhiteSpace(user.Text)) throw new ArgumentException("Enter the Vanilla username.");
                if (string.IsNullOrEmpty(password.Text)) throw new ArgumentException("Enter the password.");
                Account.Enabled = enabled.Checked;
                Account.Label = label.Text.Trim();
                Account.UserName = user.Text.Trim();
                Account.ProtectedPassword = supervisor.ProtectPassword(password.Text);
                Account.CharacterSlot = (int)slot.Value;
                Account.ResumeKey = key; Account.ResumeCtrl = ctrl; Account.ResumeAlt = alt; Account.ResumeShift = shift;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Account settings", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}

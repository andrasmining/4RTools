Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
function ReadText([string]$p) { [IO.File]::ReadAllText($p) }
function WriteText([string]$p,[string]$t) { [IO.File]::WriteAllText($p,$t,$utf8) }
function ReplaceExact([string]$p,[string]$old,[string]$new) {
  $t=ReadText $p
  if(-not $t.Contains($old)){ throw "Anchor not found in $p`n$old" }
  WriteText $p ($t.Replace($old,$new))
}

# -----------------------------------------------------------------------------
# 1. Make foreground activation substantially more reliable, while retaining a
#    launcher-only mode that may click even if Windows refuses foreground focus.
# -----------------------------------------------------------------------------
$p='Model/Vanilla/VanillaForegroundInput.cs'
$t=ReadText $p
$old='        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);'
$new=@'
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr hwnd, POINT point, uint flags);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint CWP_SKIPINVISIBLE = 0x0001;
        private const uint CWP_SKIPDISABLED = 0x0002;
        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const int MK_LBUTTON = 0x0001;
'@
if(-not $t.Contains($old)){ throw 'ForegroundInput import anchor not found.' }
$t=$t.Replace($old,$new)

$old=@'
        public void Activate()
        {
            RefreshWindow();
            for (int attempt = 0; attempt < 5; attempt++)
            {
                ShowWindow(window, 9);
                BringWindowToTop(window);
                SetForegroundWindow(window);
                Thread.Sleep(90);
                if (GetForegroundWindow() == window) return;
            }
            throw new InvalidOperationException("Windows did not give focus to the selected Vanilla window. No input was sent.");
        }
'@
$new=@'
        public void Activate()
        {
            RefreshWindow();
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (TryActivateWindow()) return;
                Thread.Sleep(90);
            }
            throw new InvalidOperationException("Windows did not give focus to the selected Vanilla window. No keyboard input was sent.");
        }

        private bool TryActivateWindow()
        {
            RefreshWindow();
            ShowWindow(window, 9);
            BringWindowToTop(window);

            IntPtr foreground = GetForegroundWindow();
            uint foregroundPid;
            uint targetPid;
            uint currentThread = GetCurrentThreadId();
            uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out foregroundPid);
            uint targetThread = GetWindowThreadProcessId(window, out targetPid);
            bool attachedForeground = false;
            bool attachedTarget = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
                if (targetThread != 0 && targetThread != currentThread)
                    attachedTarget = AttachThreadInput(currentThread, targetThread, true);
                BringWindowToTop(window);
                SetForegroundWindow(window);
                SetFocus(window);
                Thread.Sleep(100);
                return GetForegroundWindow() == window;
            }
            finally
            {
                if (attachedTarget) AttachThreadInput(currentThread, targetThread, false);
                if (attachedForeground) AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
'@
if(-not $t.Contains($old)){ throw 'ForegroundInput Activate anchor not found.' }
$t=$t.Replace($old,$new)

$old=@'
            else
            {
                // SetForegroundWindow is intentionally best-effort here. Windows may deny focus
                // when 4RTools did not most recently receive user input, even though the launcher
                // is already visible. A mouse click only needs the launcher restored and raised;
                // the real screen-coordinate click below can then reach GAME START without
                // requiring keyboard focus.
                ShowWindow(window, 9);
                BringWindowToTop(window);
                SetForegroundWindow(window);
                Thread.Sleep(120);
            }
'@
$new=@'
            else
            {
                // Launcher mouse clicks do not require keyboard focus. We still try a stronger
                // normal Windows activation first, but never abort the click solely because the
                // foreground lock rejected it.
                TryActivateWindow();
                Thread.Sleep(120);
            }
'@
if(-not $t.Contains($old)){ throw 'ForegroundInput best-effort branch anchor not found.' }
$t=$t.Replace($old,$new)

$anchor='        public void Press(Keys key)'
$method=@'
        public void ClickNormalizedWindowMessage(double x, double y)
        {
            RefreshWindow();
            TryActivateWindow();
            if (x < 0 || x > 1 || y < 0 || y > 1) throw new ArgumentOutOfRangeException("Normalized coordinates must be within 0..1.");
            RECT rect;
            if (!GetClientRect(window, out rect)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read Vanilla client area.");
            int width = Math.Max(1, rect.Right - rect.Left), height = Math.Max(1, rect.Bottom - rect.Top);
            var point = new POINT
            {
                X = Math.Max(0, Math.Min(width - 1, (int)Math.Round(x * width))),
                Y = Math.Max(0, Math.Min(height - 1, (int)Math.Round(y * height)))
            };

            IntPtr target = ChildWindowFromPointEx(window, point, CWP_SKIPINVISIBLE | CWP_SKIPDISABLED);
            if (target == IntPtr.Zero) target = window;
            var targetPoint = point;
            if (target != window)
            {
                if (!ClientToScreen(window, ref targetPoint) || !ScreenToClient(target, ref targetPoint))
                    target = window;
            }
            if (target == window) targetPoint = point;

            IntPtr packed = new IntPtr((targetPoint.Y << 16) | (targetPoint.X & 0xFFFF));
            SendMessage(target, WM_MOUSEMOVE, IntPtr.Zero, packed);
            SendMessage(target, WM_LBUTTONDOWN, new IntPtr(MK_LBUTTON), packed);
            Thread.Sleep(120);
            SendMessage(target, WM_LBUTTONUP, IntPtr.Zero, packed);
            Thread.Sleep(130);
        }

'@
if(-not $t.Contains($anchor)){ throw 'ForegroundInput Press anchor not found.' }
$t=$t.Replace($anchor,$method+$anchor)
WriteText $p $t

# -----------------------------------------------------------------------------
# 2. Launcher: visible-screen capture fallback, alternate real click/direct child
#    click strategies, and automatic cancellation when the user closes launcher.
# -----------------------------------------------------------------------------
$p='Model/Vanilla/VanillaPatcherLauncher.cs'
$t=ReadText $p
$t=$t.Replace('        private struct RECT { public int Left, Top, Right, Bottom; }','        private struct RECT { public int Left, Top, Right, Bottom; }`r`n        [StructLayout(LayoutKind.Sequential)]`r`n        private struct POINT { public int X, Y; }')
$t=$t.Replace('        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);','        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);`r`n`r`n        [DllImport("user32.dll", SetLastError = true)]`r`n        private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);')
$t=$t.Replace('        internal const int DefaultRetryMs = 8000;','        internal const int DefaultRetryMs = 10000;')

$old=@'
                int fallbackAttempt = 0;
                double[] fallbackY = { gameStartY, 0.795, 0.825, 0.745 };

                while (DateTime.UtcNow < deadline)
'@
$new=@'
                int fallbackAttempt = 0;
                int clickAttempt = 0;
                double[] fallbackY = { gameStartY, gameStartY, 0.795, 0.795, 0.825, 0.825, 0.745, 0.745 };
                bool sawLauncherWindow = false;
                DateTime? launcherWindowLostAt = null;

                while (DateTime.UtcNow < deadline)
'@
if(-not $t.Contains($old)){ throw 'Patcher loop setup anchor not found.' }
$t=$t.Replace($old,$new)

$old=@'
                        if (patcherPid.HasValue)
                        {
                            try
                            {
                                double clickX = gameStartX;
                                double clickY;
                                string evidence;
                                bool visuallyDetected = TryFindGameStartOnWindow(patcherPid.Value, out clickX, out clickY, out evidence);
                                if (!visuallyDetected)
                                {
                                    clickY = fallbackY[fallbackAttempt % fallbackY.Length];
                                    fallbackAttempt++;
                                    evidence = "visual detector unavailable; lower-center fallback sweep";
                                }

                                using (var input = new VanillaForegroundInput(patcherPid.Value))
                                    input.ClickNormalized(clickX, clickY, requireForeground: false);

                                log?.Invoke(string.Format(
                                    "GAME START click sent to launcher PID {0} at normalized ({1:0.000}, {2:0.000}) [{3}]; foreground focus is best-effort for launcher mouse clicks; waiting for Vanilla/Gepard startup before any retry.",
                                    patcherPid.Value, clickX, clickY, evidence));
                            }
                            catch (Exception ex)
                            {
                                log?.Invoke("Launcher window is not ready yet: " + ex.Message);
                            }
                            nextClick = DateTime.UtcNow.AddMilliseconds(retryMs);
                        }
                        else
                        {
                            log?.Invoke("Launcher process exists but no usable launcher window is visible yet.");
                            nextClick = DateTime.UtcNow.AddMilliseconds(1000);
                        }
'@
$new=@'
                        if (patcherPid.HasValue)
                        {
                            sawLauncherWindow = true;
                            launcherWindowLostAt = null;
                            try
                            {
                                double clickX = gameStartX;
                                double clickY;
                                string evidence;
                                bool visuallyDetected = TryFindGameStartOnWindow(patcherPid.Value, out clickX, out clickY, out evidence);
                                if (!visuallyDetected)
                                {
                                    clickY = fallbackY[fallbackAttempt % fallbackY.Length];
                                    fallbackAttempt++;
                                    evidence = "visual detector unavailable; lower-center fallback sweep";
                                }

                                string strategy;
                                using (var input = new VanillaForegroundInput(patcherPid.Value))
                                {
                                    if ((clickAttempt & 1) == 0)
                                    {
                                        input.ClickNormalized(clickX, clickY, requireForeground: false);
                                        strategy = "screen-coordinate SendInput";
                                    }
                                    else
                                    {
                                        input.ClickNormalizedWindowMessage(clickX, clickY);
                                        strategy = "targeted launcher-window click";
                                    }
                                }
                                clickAttempt++;

                                log?.Invoke(string.Format(
                                    "GAME START click sent to launcher PID {0} at normalized ({1:0.000}, {2:0.000}) using {3} [{4}]; waiting for Vanilla/Gepard startup before retry.",
                                    patcherPid.Value, clickX, clickY, strategy, evidence));
                            }
                            catch (Exception ex)
                            {
                                log?.Invoke("Launcher window is not ready yet: " + ex.Message);
                            }
                            nextClick = DateTime.UtcNow.AddMilliseconds(retryMs);
                        }
                        else
                        {
                            if (sawLauncherWindow)
                            {
                                if (!launcherWindowLostAt.HasValue) launcherWindowLostAt = DateTime.UtcNow;
                                else if ((DateTime.UtcNow - launcherWindowLostAt.Value).TotalMilliseconds >= 3000)
                                {
                                    log?.Invoke("Launcher window was closed before Vanilla started; stopping this test/recovery launch attempt.");
                                    throw new OperationCanceledException("Vanilla launcher window was closed.");
                                }
                            }
                            log?.Invoke("Launcher process exists but no usable launcher window is visible yet.");
                            nextClick = DateTime.UtcNow.AddMilliseconds(1000);
                        }
'@
if(-not $t.Contains($old)){ throw 'Patcher click block anchor not found.' }
$t=$t.Replace($old,$new)

$old=@'
                    using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        IntPtr hdc = graphics.GetHdc();
                        bool captured;
                        try { captured = PrintWindow(hwnd, hdc, 1); }
                        finally { graphics.ReleaseHdc(hdc); }
                        if (!captured) return false;
                        return TryFindGameStart(bitmap, out x, out y, out evidence);
                    }
'@
$new=@'
                    using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                    {
                        bool detected = false;
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            IntPtr hdc = graphics.GetHdc();
                            bool captured;
                            try { captured = PrintWindow(hwnd, hdc, 1); }
                            finally { graphics.ReleaseHdc(hdc); }
                            if (captured) detected = TryFindGameStart(bitmap, out x, out y, out evidence);
                        }
                        if (detected) return true;

                        // GPU/webview launchers often return a blank PrintWindow image even while
                        // visibly rendered. When the launcher is visible, capture the actual client
                        // pixels from the screen instead; this remains ordinary read-only desktop UI.
                        var origin = new POINT { X = 0, Y = 0 };
                        if (!ClientToScreen(hwnd, ref origin)) return false;
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(width, height));
                        }
                        bool screenDetected = TryFindGameStart(bitmap, out x, out y, out evidence);
                        if (screenDetected) evidence = "visible-screen capture; " + evidence;
                        return screenDetected;
                    }
'@
if(-not $t.Contains($old)){ throw 'Patcher PrintWindow block anchor not found.' }
$t=$t.Replace($old,$new)
WriteText $p $t

# -----------------------------------------------------------------------------
# 3. Numbered diagnostic workers get a real cancellation generation. Window/process
#    closure is treated as STOPPED, not as an endlessly-running failure.
# -----------------------------------------------------------------------------
$p='Model/Vanilla/VanillaReconnectDiagnostics.cs'
$t=ReadText $p
$t=$t.Replace('    public sealed partial class VanillaReconnectSupervisor`r`n    {`r`n        public void AssignSingleDiagnosticClient', '    public sealed partial class VanillaReconnectSupervisor`r`n    {`r`n        private int diagnosticGeneration;`r`n`r`n        public void CancelDiagnosticTest()`r`n        {`r`n            Interlocked.Increment(ref diagnosticGeneration);`r`n            lock (gate)`r`n            {`r`n                foreach (var runtime in runtimes.Values)`r`n                {`r`n                    if (runtime.Detail != null && runtime.Detail.StartsWith("Diagnostic", StringComparison.Ordinal))`r`n                    {`r`n                        runtime.ScriptRunning = false;`r`n                        SetStage(runtime, VanillaReconnectStage.Stopped, "Diagnostic test stopped by user");`r`n                    }`r`n                }`r`n            }`r`n            RaiseUpdated();`r`n        }`r`n`r`n        private bool DiagnosticCancelled(int generation)`r`n        {`r`n            return disposed || generation != Volatile.Read(ref diagnosticGeneration);`r`n        }`r`n`r`n        public void AssignSingleDiagnosticClient')

$old=@'
        public void RunDiagnosticStep(string accountId, VanillaReconnectTestStep step)
        {
            VanillaReconnectAccount account;
            VanillaReconnectSettings config;
            int? pid = null;
            lock (gate)
            {
                Runtime runtime;
                if (!runtimes.TryGetValue(accountId, out runtime)) throw new ArgumentException("Unknown account.");
                if (runtime.ScriptRunning) throw new InvalidOperationException("Another action is already running for this account.");
                if (step != VanillaReconnectTestStep.LauncherGameStart)
                {
                    if (!runtime.ProcessId.HasValue) throw new InvalidOperationException("No Vanilla client is assigned to this selected account for the step test.");
                    pid = runtime.ProcessId.Value;
                }
                runtime.ScriptRunning = true;
                account = runtime.Account.Clone();
                config = settings.Clone();
                SetStage(runtime, VanillaReconnectStage.LoggingIn, "Diagnostic step running: " + step);
            }
            ThreadPool.QueueUserWorkItem(_ => DiagnosticStepWorker(accountId, account, config, pid, step));
            RaiseUpdated();
        }

        private void DiagnosticStepWorker(string accountId, VanillaReconnectAccount account, VanillaReconnectSettings config, int? pid, VanillaReconnectTestStep step)
        {
            string error = null;
            int? discoveredPid = pid;
'@
$new=@'
        public void RunDiagnosticStep(string accountId, VanillaReconnectTestStep step)
        {
            VanillaReconnectAccount account;
            VanillaReconnectSettings config;
            int? pid = null;
            int generation = Interlocked.Increment(ref diagnosticGeneration);
            lock (gate)
            {
                Runtime runtime;
                if (!runtimes.TryGetValue(accountId, out runtime)) throw new ArgumentException("Unknown account.");
                if (step != VanillaReconnectTestStep.LauncherGameStart)
                {
                    if (!runtime.ProcessId.HasValue) throw new InvalidOperationException("No Vanilla client is assigned to this selected account for the step test.");
                    pid = runtime.ProcessId.Value;
                }
                runtime.ScriptRunning = true;
                account = runtime.Account.Clone();
                config = settings.Clone();
                SetStage(runtime, VanillaReconnectStage.LoggingIn, "Diagnostic step running: " + step);
            }
            ThreadPool.QueueUserWorkItem(_ => DiagnosticStepWorker(accountId, account, config, pid, step, generation));
            RaiseUpdated();
        }

        private void DiagnosticStepWorker(string accountId, VanillaReconnectAccount account, VanillaReconnectSettings config, int? pid, VanillaReconnectTestStep step, int generation)
        {
            string error = null;
            string stopped = null;
            int? discoveredPid = pid;
'@
if(-not $t.Contains($old)){ throw 'Diagnostic RunDiagnosticStep anchor not found.' }
$t=$t.Replace($old,$new)
$t=$t.Replace('                        message => Log("TEST " + account.Label + ": " + message), () => disposed);','                        message => Log("TEST " + account.Label + ": " + message), () => DiagnosticCancelled(generation));')

$old='            catch (Exception ex) { error = ex.Message; }'
$new=@'
            catch (OperationCanceledException ex) { stopped = ex.Message; }
            catch (Exception ex)
            {
                if (discoveredPid.HasValue && !IsDiagnosticProcessAlive(discoveredPid.Value))
                    stopped = "Vanilla window/process was closed.";
                else error = ex.Message;
            }
'@
if(-not $t.Contains($old)){ throw 'Diagnostic catch anchor not found.' }
$t=$t.Replace($old,$new)

$old=@'
                lock (gate)
                {
                    Runtime runtime;
                    if (runtimes.TryGetValue(accountId, out runtime))
                    {
                        if (discoveredPid.HasValue) runtime.ProcessId = discoveredPid;
                        runtime.ScriptRunning = false;
                        SetStage(runtime, error == null ? VanillaReconnectStage.WaitingForGameplay : VanillaReconnectStage.Error,
                            error == null ? "Diagnostic step completed: " + step : "Diagnostic step failed: " + error);
                    }
                }
                if (error == null) Log("TEST " + account.Label + ": step " + step + " completed.");
                else Log("TEST " + account.Label + ": step " + step + " FAILED: " + error);
                RaiseUpdated();
            }
        }
'@
$new=@'
                if (generation != Volatile.Read(ref diagnosticGeneration)) return;
                lock (gate)
                {
                    Runtime runtime;
                    if (runtimes.TryGetValue(accountId, out runtime))
                    {
                        if (discoveredPid.HasValue) runtime.ProcessId = discoveredPid;
                        runtime.ScriptRunning = false;
                        if (stopped != null)
                            SetStage(runtime, VanillaReconnectStage.Stopped, "Diagnostic test stopped: " + stopped);
                        else
                            SetStage(runtime, error == null ? VanillaReconnectStage.WaitingForGameplay : VanillaReconnectStage.Error,
                                error == null ? "Diagnostic step completed: " + step : "Diagnostic step failed: " + error);
                    }
                }
                if (stopped != null) Log("TEST " + account.Label + ": STOPPED: " + stopped);
                else if (error == null) Log("TEST " + account.Label + ": step " + step + " completed.");
                else Log("TEST " + account.Label + ": step " + step + " FAILED: " + error);
                RaiseUpdated();
            }
        }

        private static bool IsDiagnosticProcessAlive(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId)) return !process.HasExited;
            }
            catch { return false; }
        }
'@
if(-not $t.Contains($old)){ throw 'Diagnostic finally anchor not found.' }
$t=$t.Replace($old,$new)

# A newer numbered test cancels the currently running diagnostic worker too.
$t=$t.Replace('                if (testRunning)`r`n                {`r`n                    testRunning = false;`r`n                    testGeneration++;`r`n                }','                if (testRunning)`r`n                {`r`n                    supervisor.CancelDiagnosticTest();`r`n                    testRunning = false;`r`n                    testGeneration++;`r`n                }')

# Treat diagnostic STOPPED state distinctly in both waiter implementations.
$t=$t.Replace('            string failurePrefix = "Diagnostic step failed:";','            string failurePrefix = "Diagnostic step failed:";`r`n            string stoppedPrefix = "Diagnostic test stopped:";',1)
# Replace second occurrence as well.
$idx=$t.IndexOf('                string failurePrefix = "Diagnostic step failed:";', $t.IndexOf('private void WaitForDiagnosticStep'))
if($idx -ge 0){ $t=$t.Insert($idx+'                string failurePrefix = "Diagnostic step failed:";'.Length, '`r`n                string stoppedPrefix = "Diagnostic test stopped:";') }

$t=$t.Replace('                    if (current.Detail != null && current.Detail.StartsWith(failurePrefix, StringComparison.Ordinal))`r`n                    {`r`n                        failure = current.Detail;`r`n                        return false;`r`n                    }','                    if (current.Detail != null && current.Detail.StartsWith(stoppedPrefix, StringComparison.Ordinal))`r`n                    {`r`n                        failure = "STOPPED: " + current.Detail;`r`n                        return false;`r`n                    }`r`n                    if (current.Detail != null && current.Detail.StartsWith(failurePrefix, StringComparison.Ordinal))`r`n                    {`r`n                        failure = current.Detail;`r`n                        return false;`r`n                    }')
$t=$t.Replace('                            CompleteTest(generation, false, failure);','                            if (failure != null && failure.StartsWith("STOPPED:", StringComparison.Ordinal)) CompleteTestStopped(generation, failure.Substring(8).Trim());`r`n                            else CompleteTest(generation, false, failure);')
$t=$t.Replace('                        if (current.Detail != null && current.Detail.StartsWith(failurePrefix, StringComparison.Ordinal))`r`n                        {`r`n                            CompleteTest(generation, false, current.Detail);`r`n                            return;`r`n                        }','                        if (current.Detail != null && current.Detail.StartsWith(stoppedPrefix, StringComparison.Ordinal))`r`n                        {`r`n                            CompleteTestStopped(generation, current.Detail);`r`n                            return;`r`n                        }`r`n                        if (current.Detail != null && current.Detail.StartsWith(failurePrefix, StringComparison.Ordinal))`r`n                        {`r`n                            CompleteTest(generation, false, current.Detail);`r`n                            return;`r`n                        }')

# If the user closes Vanilla during the between-step chain, report STOPPED instead of failure.
$t=$t.Replace('                catch (Exception ex)`r`n                {`r`n                    CompleteTest(generation, false, "Chain test stopped: " + ex.Message);`r`n                }','                catch (Exception ex)`r`n                {`r`n                    if (ex.Message.IndexOf("No running Vanilla client", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.IndexOf("Vanilla client exited", StringComparison.OrdinalIgnoreCase) >= 0)`r`n                        CompleteTestStopped(generation, "Vanilla window/process was closed; test stopped.");`r`n                    else CompleteTest(generation, false, "Chain test failed: " + ex.Message);`r`n                }')
WriteText $p $t

# -----------------------------------------------------------------------------
# 4. Main reconnect UI: STOP TEST button and a clean stopped state.
# -----------------------------------------------------------------------------
$p='Model/Vanilla/VanillaReconnect.cs'
$t=ReadText $p
$t=$t.Replace('            AddButton(tests, "ARM MANUAL NETWORK-DROP TEST", ArmManualNetworkDropTest);','            AddButton(tests, "ARM MANUAL NETWORK-DROP TEST", ArmManualNetworkDropTest);`r`n            AddButton(tests, "STOP TEST", StopCurrentTest);')
$t=$t.Replace('            TipByText(this, "ARM MANUAL NETWORK-DROP TEST", "Arm a five-minute recovery test while you briefly disconnect/reconnect internet yourself.");','            TipByText(this, "ARM MANUAL NETWORK-DROP TEST", "Arm a five-minute recovery test while you briefly disconnect/reconnect internet yourself.");`r`n            TipByText(this, "STOP TEST", "Immediately cancel the active recovery/numbered diagnostic test. Closing the launcher or the one diagnostic Vanilla client also stops numbered tests automatically.");')

$anchor=@'
        private int BeginTest(string text)
        {
            testRunning = true; testGeneration++; testState.Text = text; testState.ForeColor = Color.DarkSlateBlue; return testGeneration;
        }
'@
$replacement=@'
        private void StopCurrentTest()
        {
            supervisor.CancelDiagnosticTest();
            if (supervisor.IsRunning) supervisor.Stop();
            testGeneration++;
            testRunning = false;
            testState.Text = "TEST STOPPED";
            testState.ForeColor = Color.DarkOrange;
        }

        private int BeginTest(string text)
        {
            testRunning = true; testGeneration++; testState.Text = text; testState.ForeColor = Color.DarkSlateBlue; return testGeneration;
        }
'@
if(-not $t.Contains($anchor)){ throw 'Reconnect BeginTest anchor not found.' }
$t=$t.Replace($anchor,$replacement)

$anchor=@'
        private void CompleteTest(int generation, bool success, string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)(() => CompleteTest(generation, success, message))); return; }
            if (generation != testGeneration) return;
            testRunning = false; testState.Text = success ? "TEST PASSED" : "TEST FAILED";
            testState.ForeColor = success ? Color.DarkGreen : Color.DarkRed;
            MessageBox.Show(this, message, "4RTools Vanilla test", MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
'@
$replacement=@'
        private void CompleteTestStopped(int generation, string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)(() => CompleteTestStopped(generation, message))); return; }
            if (generation != testGeneration) return;
            testRunning = false;
            testState.Text = "TEST STOPPED";
            testState.ForeColor = Color.DarkOrange;
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
'@
if(-not $t.Contains($anchor)){ throw 'Reconnect CompleteTest anchor not found.' }
$t=$t.Replace($anchor,$replacement)
WriteText $p $t
Write-Host '0.6.4 live test control and launcher reliability patch applied.'

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
function ReadText([string]$p) { [IO.File]::ReadAllText($p) }
function WriteText([string]$p,[string]$t) { [IO.File]::WriteAllText($p,$t,$utf8) }

# --- Launcher reliability -----------------------------------------------------
$p='Model/Vanilla/VanillaPatcherLauncher.cs'
$t=ReadText $p
$t=$t.Replace('        internal const int DefaultRetryMs = 8000;', '        internal const int DefaultRetryMs = 10000;')
$t=$t.Replace('        private struct RECT { public int Left, Top, Right, Bottom; }', @'
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }
'@)
$t=$t.Replace('        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);', @'
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
'@)
$t=$t.Replace(@'
                int fallbackAttempt = 0;
                double[] fallbackY = { gameStartY, 0.795, 0.825, 0.745 };

                while (DateTime.UtcNow < deadline)
'@, @'
                int fallbackAttempt = 0;
                int clickAttempt = 0;
                double[] fallbackY = { gameStartY, 0.795, 0.825, 0.745 };
                bool sawLauncherWindow = false;
                DateTime? launcherWindowLostAt = null;

                while (DateTime.UtcNow < deadline)
'@)

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
                                if ((clickAttempt++ & 1) == 0)
                                {
                                    using (var input = new VanillaForegroundInput(patcherPid.Value))
                                        input.ClickNormalized(clickX, clickY, requireForeground: false);
                                    strategy = "screen-coordinate SendInput";
                                }
                                else
                                {
                                    using (var input = new VanillaTargetedInput(patcherPid.Value))
                                    {
                                        input.Activate();
                                        input.ClickNormalized(clickX, clickY);
                                    }
                                    strategy = "targeted launcher window message";
                                }

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
                                else if ((DateTime.UtcNow - launcherWindowLostAt.Value).TotalMilliseconds >= 2500)
                                {
                                    log?.Invoke("Launcher window was closed before Vanilla started; stopping this test/recovery launch attempt.");
                                    throw new OperationCanceledException("Vanilla launcher window was closed.");
                                }
                            }
                            log?.Invoke("Launcher process exists but no usable launcher window is visible yet.");
                            nextClick = DateTime.UtcNow.AddMilliseconds(1000);
                        }
'@
if(-not $t.Contains($old)){ throw 'Launcher click block anchor not found.' }
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
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            IntPtr hdc = graphics.GetHdc();
                            bool captured;
                            try { captured = PrintWindow(hwnd, hdc, 1); }
                            finally { graphics.ReleaseHdc(hdc); }
                            if (captured && TryFindGameStart(bitmap, out x, out y, out evidence)) return true;
                        }

                        var origin = new POINT { X = 0, Y = 0 };
                        if (!ClientToScreen(hwnd, ref origin)) return false;
                        using (var graphics = Graphics.FromImage(bitmap))
                            graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(width, height));
                        if (!TryFindGameStart(bitmap, out x, out y, out evidence)) return false;
                        evidence = "visible-screen capture; " + evidence;
                        return true;
                    }
'@
if(-not $t.Contains($old)){ throw 'Launcher capture block anchor not found.' }
$t=$t.Replace($old,$new)
WriteText $p $t

# --- Diagnostic cancellation and STOP TEST ----------------------------------
$p='Model/Vanilla/VanillaReconnectDiagnostics.cs'
$t=ReadText $p
$anchor="    public sealed partial class VanillaReconnectSupervisor`r`n    {"
$insert=@'
    public sealed partial class VanillaReconnectSupervisor
    {
        private int diagnosticGeneration;

        public void CancelDiagnosticTest()
        {
            Interlocked.Increment(ref diagnosticGeneration);
            lock (gate)
            {
                foreach (var runtime in runtimes.Values)
                {
                    if (runtime.Detail != null && runtime.Detail.StartsWith("Diagnostic", StringComparison.Ordinal))
                    {
                        runtime.ScriptRunning = false;
                        SetStage(runtime, VanillaReconnectStage.Stopped, "Diagnostic test stopped by user");
                    }
                }
            }
            RaiseUpdated();
        }

        private bool DiagnosticCancelled(int generation)
        {
            return disposed || generation != Volatile.Read(ref diagnosticGeneration);
        }
'@
if(-not $t.Contains($anchor)){ throw 'Diagnostic supervisor anchor not found.' }
$t=$t.Replace($anchor,$insert)

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
if(-not $t.Contains($old)){ throw 'Diagnostic worker header anchor not found.' }
$t=$t.Replace($old,$new)
$t=$t.Replace('                        message => Log("TEST " + account.Label + ": " + message), () => disposed);', '                        message => Log("TEST " + account.Label + ": " + message), () => DiagnosticCancelled(generation));')

$old='            catch (Exception ex) { error = ex.Message; }'
$new=@'
            catch (OperationCanceledException ex) { stopped = ex.Message; }
            catch (Exception ex)
            {
                bool closed = false;
                if (discoveredPid.HasValue)
                {
                    try { using (var process = Process.GetProcessById(discoveredPid.Value)) closed = process.HasExited; }
                    catch { closed = true; }
                }
                if (closed) stopped = "Vanilla window/process was closed.";
                else error = ex.Message;
            }
'@
if(-not $t.Contains($old)){ throw 'Diagnostic catch anchor not found.' }
$t=$t.Replace($old,$new)

$old=@'
            finally
            {
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
'@
$new=@'
            finally
            {
                if (generation == Volatile.Read(ref diagnosticGeneration))
                {
                    lock (gate)
                    {
                        Runtime runtime;
                        if (runtimes.TryGetValue(accountId, out runtime))
                        {
                            if (discoveredPid.HasValue) runtime.ProcessId = discoveredPid;
                            runtime.ScriptRunning = false;
                            if (stopped != null) SetStage(runtime, VanillaReconnectStage.Stopped, "Diagnostic test stopped: " + stopped);
                            else SetStage(runtime, error == null ? VanillaReconnectStage.WaitingForGameplay : VanillaReconnectStage.Error,
                                error == null ? "Diagnostic step completed: " + step : "Diagnostic step failed: " + error);
                        }
                    }
                    if (stopped != null) Log("TEST " + account.Label + ": STOPPED: " + stopped);
                    else if (error == null) Log("TEST " + account.Label + ": step " + step + " completed.");
                    else Log("TEST " + account.Label + ": step " + step + " FAILED: " + error);
                    RaiseUpdated();
                }
            }
'@
if(-not $t.Contains($old)){ throw 'Diagnostic finally anchor not found.' }
$t=$t.Replace($old,$new)

$t=$t.Replace('            AddButton(row, "7 RESUME HOTKEY", () => RunStepTest(VanillaReconnectTestStep.ResumeHotkey));', @'
            AddButton(row, "7 RESUME HOTKEY", () => RunStepTest(VanillaReconnectTestStep.ResumeHotkey));
            AddButton(row, "STOP TEST", StopCurrentTest);
'@)
$t=$t.Replace('            TipByText(this, "7 RESUME HOTKEY", "Existing client: resume hotkey only. No client: automatically run the complete numbered sequence first.");', @'
            TipByText(this, "7 RESUME HOTKEY", "Existing client: resume hotkey only. No client: automatically run the complete numbered sequence first.");
            TipByText(this, "STOP TEST", "Cancel the current diagnostic/recovery test immediately. Closing the launcher or the one diagnostic Vanilla client also stops the test automatically.");
'@)
$t=$t.Replace(@'
                if (testRunning)
                {
                    testRunning = false;
                    testGeneration++;
                }
'@, @'
                if (testRunning)
                {
                    supervisor.CancelDiagnosticTest();
                    testRunning = false;
                    testGeneration++;
                }
'@)

$anchor='        private void RunStepTest(VanillaReconnectTestStep step)'
$method=@'
        private void StopCurrentTest()
        {
            supervisor.CancelDiagnosticTest();
            if (supervisor.IsRunning) supervisor.Stop();
            testGeneration++;
            testRunning = false;
            testState.Text = "TEST STOPPED";
            testState.ForeColor = Color.DarkOrange;
        }

        private void CompleteTestStopped(int generation, string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)(() => CompleteTestStopped(generation, message))); return; }
            if (generation != testGeneration) return;
            testRunning = false;
            testState.Text = "TEST STOPPED";
            testState.ForeColor = Color.DarkOrange;
        }

'@
if(-not $t.Contains($anchor)){ throw 'RunStepTest anchor missing.' }
$t=$t.Replace($anchor,$method+$anchor)

# Waiters recognize manual closure/cancellation as STOPPED rather than FAILED.
$t=$t.Replace('            string failurePrefix = "Diagnostic step failed:";' + [Environment]::NewLine + '            while (DateTime.UtcNow < deadline)', '            string failurePrefix = "Diagnostic step failed:";' + [Environment]::NewLine + '            string stoppedPrefix = "Diagnostic test stopped:";' + [Environment]::NewLine + '            while (DateTime.UtcNow < deadline)')
$t=$t.Replace('                string failurePrefix = "Diagnostic step failed:";' + [Environment]::NewLine + '                while (DateTime.UtcNow < deadline)', '                string failurePrefix = "Diagnostic step failed:";' + [Environment]::NewLine + '                string stoppedPrefix = "Diagnostic test stopped:";' + [Environment]::NewLine + '                while (DateTime.UtcNow < deadline)')
$t=$t.Replace(@'
                    if (current.Detail != null && current.Detail.StartsWith(failurePrefix, StringComparison.Ordinal))
                    {
                        failure = current.Detail;
                        return false;
                    }
'@, @'
                    if (current.Detail != null && current.Detail.StartsWith(stoppedPrefix, StringComparison.Ordinal))
                    {
                        failure = "STOPPED: " + current.Detail;
                        return false;
                    }
                    if (current.Detail != null && current.Detail.StartsWith(failurePrefix, StringComparison.Ordinal))
                    {
                        failure = current.Detail;
                        return false;
                    }
'@)
$t=$t.Replace('                            CompleteTest(generation, false, failure);', '                            if (failure != null && failure.StartsWith("STOPPED:", StringComparison.Ordinal)) CompleteTestStopped(generation, failure.Substring(8).Trim());' + [Environment]::NewLine + '                            else CompleteTest(generation, false, failure);')
$t=$t.Replace(@'
                        if (current.Detail != null && current.Detail.StartsWith(failurePrefix, StringComparison.Ordinal))
                        {
                            CompleteTest(generation, false, current.Detail);
                            return;
                        }
'@, @'
                        if (current.Detail != null && current.Detail.StartsWith(stoppedPrefix, StringComparison.Ordinal))
                        {
                            CompleteTestStopped(generation, current.Detail);
                            return;
                        }
                        if (current.Detail != null && current.Detail.StartsWith(failurePrefix, StringComparison.Ordinal))
                        {
                            CompleteTest(generation, false, current.Detail);
                            return;
                        }
'@)
$t=$t.Replace('                    CompleteTest(generation, false, "Chain test stopped: " + ex.Message);', @'
                    if (ex.Message.IndexOf("No running Vanilla client", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.IndexOf("Vanilla client exited", StringComparison.OrdinalIgnoreCase) >= 0)
                        CompleteTestStopped(generation, "Vanilla window/process was closed; test stopped.");
                    else CompleteTest(generation, false, "Chain test failed: " + ex.Message);
'@)
WriteText $p $t
Write-Host '0.6.4 live test control patch applied.'

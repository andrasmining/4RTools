using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Cold-start orchestration for the normal START SUPERVISOR path.
    ///
    /// Initial client creation is intentionally separate from the periodic supervisor timer:
    /// one configured account is brought all the way from launcher -> proxy -> login -> server
    /// -> character -> confirmed gameplay -> one resume hotkey -> minimized before another
    /// missing account is allowed to start. Transient visual-recognition failures never close
    /// the just-created client and never advance to the next account.
    /// </summary>
    public sealed partial class VanillaReconnectSupervisor
    {
        private int hardenedStartupGeneration;
        private bool hardenedStartupRunning;

        private sealed class HardenedProxyReady
        {
            public Bitmap Image;
            public VanillaProxyLayout Layout;
            public string Evidence;
        }

        public bool IsHardenedStartupRunning
        {
            get { lock (gate) return hardenedStartupRunning; }
        }

        internal static bool SequentialStartupMayAdvance(bool gameplayConfirmed, bool resumeSent, bool minimized, bool failed)
        {
            return gameplayConfirmed && resumeSent && minimized && !failed;
        }

        public void StartHardenedSequentialStartup(Action<bool, string> completed)
        {
            VanillaReconnectSettings config;
            VanillaReconnectAccount[] configured;
            int generation;

            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(VanillaReconnectSupervisor));
                if (running) throw new InvalidOperationException("Stop the reconnect supervisor before starting the serialized cold-start sequence.");
                if (hardenedStartupRunning) throw new InvalidOperationException("A serialized startup sequence is already running.");
                settings.Validate();
                RebuildRuntimes();
                config = settings.Clone();
                configured = config.Accounts.Where(a => a.Enabled).Take(config.MaxClients).Select(a => a.Clone()).ToArray();
                if (configured.Length == 0) throw new InvalidOperationException("Enable at least one account first.");
                hardenedStartupRunning = true;
                generation = Interlocked.Increment(ref hardenedStartupGeneration);
            }

            Log("Sequential startup BEGIN for " + configured.Length + " client(s). No second client may start until the current client is in gameplay, resume hotkey was sent once, and its window is minimized.");
            ThreadPool.QueueUserWorkItem(_ => HardenedStartupWorker(generation, configured, config, completed));
        }

        public void CancelHardenedSequentialStartup()
        {
            Interlocked.Increment(ref hardenedStartupGeneration);
            lock (gate)
            {
                hardenedStartupRunning = false;
                foreach (Runtime runtime in runtimes.Values)
                {
                    if (runtime.Detail != null && runtime.Detail.StartsWith("Sequential startup", StringComparison.Ordinal))
                    {
                        runtime.ScriptRunning = false;
                        runtime.RecoveryOwned = false;
                        SetStage(runtime, VanillaReconnectStage.Stopped, "Sequential startup stopped by user");
                    }
                }
            }
            Log("Sequential startup STOP requested. No additional client will be launched.");
            RaiseUpdated();
        }

        private bool HardenedStartupCancelled(int generation)
        {
            return disposed || generation != Volatile.Read(ref hardenedStartupGeneration);
        }

        private void HardenedStartupWorker(int generation, VanillaReconnectAccount[] accounts, VanillaReconnectSettings config,
            Action<bool, string> completed)
        {
            bool success = false;
            string result = null;
            try
            {
                for (int index = 0; index < accounts.Length; index++)
                {
                    if (HardenedStartupCancelled(generation)) throw new OperationCanceledException("Sequential startup cancelled.");
                    VanillaReconnectAccount account = accounts[index];
                    Runtime runtime;
                    int? existingPid;
                    lock (gate)
                    {
                        if (!runtimes.TryGetValue(account.Id, out runtime)) throw new InvalidOperationException("Runtime disappeared for " + account.Label + ".");
                        existingPid = runtime.ProcessId.HasValue && HardenedIsAlive(runtime.ProcessId.Value) ? runtime.ProcessId : null;
                    }

                    if (existingPid.HasValue)
                    {
                        Log(account.Label + ": existing Vanilla PID " + existingPid.Value + " is already assigned; verifying gameplay before allowing the next client.");
                        using (var input = new VanillaForegroundInput(existingPid.Value))
                        {
                            WaitForGameplayStable(input, existingPid.Value, generation, 12000, account.Label + ": existing client");
                        }
                        if (!KeepAssignedClientMinimized(account.Id))
                            throw new InvalidOperationException(account.Label + ": existing client is running but could not be minimized; next client was NOT started.");
                        lock (gate)
                        {
                            runtime.ResumeSent = true; // adopted existing clients must never have their toggle resent automatically.
                            runtime.HasBeenOnline = true;
                            runtime.RecoveryOwned = false;
                            runtime.ScriptRunning = false;
                            SetStage(runtime, VanillaReconnectStage.Online, "Existing gameplay client verified and minimized");
                        }
                        Log(account.Label + ": existing gameplay client verified and minimized; sequential gate released for the next configured account.");
                        RaiseUpdated();
                        continue;
                    }

                    RunOneHardenedStartup(generation, account, config, index + 1, accounts.Length);
                }

                if (HardenedStartupCancelled(generation)) throw new OperationCanceledException("Sequential startup cancelled.");
                lock (gate) hardenedStartupRunning = false;

                // Only now start the continuous watchdog. At this point every configured client
                // has independently completed gameplay + resume + minimize, so adopting them is safe.
                Start();
                success = true;
                result = "Sequential startup completed. Every configured client reached gameplay, received its one-shot resume hotkey, and was minimized before the next client started. Continuous supervisor is now ON.";
                Log(result);
            }
            catch (OperationCanceledException ex)
            {
                result = ex.Message;
                lock (gate) hardenedStartupRunning = false;
                Log("Sequential startup stopped: " + result);
            }
            catch (Exception ex)
            {
                result = ex.Message;
                lock (gate) hardenedStartupRunning = false;
                Log("Sequential startup FAILED and stopped before starting any later queued client: " + result);
            }
            finally
            {
                RaiseUpdated();
                if (completed != null)
                {
                    try { completed(success, result ?? (success ? "Sequential startup completed." : "Sequential startup stopped.")); }
                    catch { }
                }
            }
        }

        private void RunOneHardenedStartup(int generation, VanillaReconnectAccount account, VanillaReconnectSettings config,
            int ordinal, int total)
        {
            if (string.IsNullOrWhiteSpace(config.LaunchExecutable) || !File.Exists(config.LaunchExecutable))
                throw new InvalidOperationException("Set the Vanilla launch executable before starting the supervisor.");
            if (string.IsNullOrWhiteSpace(account.UserName) || string.IsNullOrWhiteSpace(account.ProtectedPassword))
                throw new InvalidOperationException(account.Label + ": username/password is missing.");

            Runtime runtime;
            lock (gate)
            {
                runtime = runtimes[account.Id];
                runtime.ScriptRunning = true;
                runtime.RecoveryOwned = true;
                runtime.ResumeSent = false;
                runtime.HasBeenOnline = false;
                runtime.NextRecoveryAt = null;
                SetStage(runtime, VanillaReconnectStage.Launching,
                    "Sequential startup " + ordinal + "/" + total + ": launcher; all later clients are blocked");
            }
            RaiseUpdated();
            Log(account.Label + ": sequential startup " + ordinal + "/" + total + " owns the startup lease. No later client will launch until this one is completed and minimized.");

            int? pid = null;
            try
            {
                pid = VanillaPatcherLauncher.Launch(config.LaunchExecutable, config.LaunchArguments,
                    message => Log(account.Label + ": " + message),
                    () => HardenedStartupCancelled(generation),
                    debugDirectory: Path.Combine(baseDirectory, "Logs"));
                if (!pid.HasValue) throw new InvalidOperationException(account.Label + ": launcher did not produce a Vanilla MMO process ID.");

                lock (gate)
                {
                    Bind(runtime, pid.Value, true, "Sequential startup: launcher produced Vanilla client");
                    runtime.ScriptRunning = true;
                    runtime.RecoveryOwned = true;
                }
                RaiseUpdated();

                WaitForWindow(pid.Value, 60000);
                if (HardenedStartupCancelled(generation)) throw new OperationCanceledException("Sequential startup cancelled.");

                using (var input = new VanillaForegroundInput(pid.Value))
                {
                    // No fixed Gepard sleep here. Poll the actual expected proxy screen and act
                    // shortly after it is recognized twice in succession.
                    SelectProxyWhenVisible(input, pid.Value, account, config, generation);

                    // Login detector already polls the live focused window. There is no blind stage delay.
                    string password = store.UnprotectPassword(account.ProtectedPassword);
                    if (string.IsNullOrEmpty(password)) throw new InvalidOperationException(account.Label + ": decrypted password is empty.");
                    HumanSettle(generation, 180);
                    FillDetectedCredentials(input, account, password, pid.Value, true, account.Label + ": sequential startup: ");

                    // Wait for the server dialog itself, then verify it actually closes.
                    if (HardenedStartupCancelled(generation)) throw new OperationCanceledException("Sequential startup cancelled.");
                    SelectDetectedGameServer(input, pid.Value, 900, account.Label + ": sequential startup: ");

                    // Character selection has no text field to key off. Wait for the auth/server/proxy
                    // surfaces to disappear and for the resulting interactive frame to settle instead
                    // of sleeping a fixed multi-second interval.
                    WaitForCharacterSurface(input, pid.Value, generation, config);
                    int slot = Math.Max(1, Math.Min(15, account.CharacterSlot)) - 1;
                    int col = slot % 5, row = slot / 5;
                    input.Activate();
                    HumanSettle(generation, 160);
                    input.ClickNormalized(config.Anchors.CharacterGridX + col * config.Anchors.CharacterStepX,
                        config.Anchors.CharacterGridY + row * config.Anchors.CharacterStepY);
                    HumanSettle(generation, 260);
                    input.Activate();
                    input.ClickNormalized(config.Anchors.GameStartX, config.Anchors.GameStartY);
                    Log(account.Label + ": character slot " + account.CharacterSlot + " selected; GAME START clicked with the Vanilla window foreground-verified.");

                    // Do not use a fixed GameLoadMs. The resume toggle is sent only after gameplay
                    // has been visually observed in consecutive samples.
                    WaitForGameplayStable(input, pid.Value, generation, 60000, account.Label + ": post-character load");
                    HumanSettle(generation, 320);
                    input.Activate();
                    HumanSettle(generation, 140);
                    input.Chord(account.ResumeCtrl, account.ResumeAlt, account.ResumeShift, (Keys)account.ResumeKey);
                    Log(account.Label + ": gameplay stable; sent resume hotkey " + account.HotkeyText + " exactly once after foreground verification.");
                }

                bool minimized = KeepAssignedClientMinimized(account.Id);
                bool mayAdvance = SequentialStartupMayAdvance(true, true, minimized, false);
                if (!mayAdvance)
                    throw new InvalidOperationException(account.Label + ": gameplay/login completed but the client could not be confirmed minimized; next client was NOT started.");

                lock (gate)
                {
                    runtime.ResumeSent = true;
                    runtime.HasBeenOnline = true;
                    runtime.ScriptRunning = false;
                    runtime.RecoveryOwned = false;
                    runtime.RecoveryFailures = 0;
                    runtime.NextRecoveryAt = null;
                    SetStage(runtime, VanillaReconnectStage.Online,
                        "Sequential startup complete: gameplay confirmed, resume sent once, minimized");
                }
                RaiseUpdated();
                Log(account.Label + ": sequential startup COMPLETE and minimized. Only now is the next configured client allowed to start.");
            }
            catch
            {
                lock (gate)
                {
                    runtime.ScriptRunning = false;
                    runtime.RecoveryOwned = false;
                    SetStage(runtime, VanillaReconnectStage.Error,
                        "Sequential startup failed; client left running if present; later clients were not started");
                }
                RaiseUpdated();
                // Deliberately DO NOT close or kill pid here. A transient recognition/focus failure
                // must never destroy the just-started client or cause the next account to launch.
                throw;
            }
        }

        private void SelectProxyWhenVisible(VanillaForegroundInput input, int pid, VanillaReconnectAccount account,
            VanillaReconnectSettings config, int generation)
        {
            HardenedProxyReady ready = WaitForProxyReady(input, generation, 45000);
            using (ready.Image)
            {
                int routeIndex = (int)config.Proxy;
                if (routeIndex < 0 || routeIndex >= ready.Layout.Rows.Length)
                    throw new InvalidOperationException("Configured proxy route is outside the detected proxy list.");
                Rectangle safe = ready.Layout.Rows[routeIndex];
                var random = new Random(unchecked(Environment.TickCount ^ pid ^ (routeIndex * 7919)));
                int marginX = Math.Max(1, safe.Width / 4), marginY = Math.Max(1, safe.Height / 4);
                int px = random.Next(safe.Left + marginX, Math.Max(safe.Left + marginX + 1, safe.Right - marginX));
                int py = random.Next(safe.Top + marginY, Math.Max(safe.Top + marginY + 1, safe.Bottom - marginY));
                double x = (px + 0.5) / ready.Image.Width;
                double y = (py + 0.5) / ready.Image.Height;

                input.Activate();
                HumanSettle(generation, 180);
                input.ClickNormalized(x, y);
                for (int i = 0; i < 8; i++) { input.Press(Keys.Up); HumanSettle(generation, 35); }
                for (int i = 0; i < routeIndex; i++) { input.Press(Keys.Down); HumanSettle(generation, 45); }
                input.Activate();
                input.Press(Keys.Enter);
                Log(account.Label + ": proxy " + config.Proxy + " selected immediately after two stable detections; " + ready.Evidence);
            }
        }

        private HardenedProxyReady WaitForProxyReady(VanillaForegroundInput input, int generation, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            int consecutive = 0;
            string last = "not sampled";
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (HardenedStartupCancelled(generation)) throw new OperationCanceledException("Sequential startup cancelled.");
                Bitmap image = null;
                try
                {
                    image = input.CaptureClientBitmap(); // Capture foreground-verifies the exact Vanilla window.
                    VanillaProxyLayout layout;
                    string evidence;
                    if (VanillaProxyPattern.TryDetect(image, out layout, out evidence))
                    {
                        consecutive++;
                        last = evidence;
                        if (consecutive >= 2)
                            return new HardenedProxyReady { Image = image, Layout = layout, Evidence = evidence };
                    }
                    else
                    {
                        consecutive = 0;
                        last = evidence;
                    }
                }
                finally
                {
                    if (consecutive < 2 && image != null) image.Dispose();
                }
                Thread.Sleep(170);
            }
            throw new InvalidOperationException("Proxy screen did not become stably recognizable; client was left running and no later client was started. Last detector result: " + last);
        }

        private void WaitForCharacterSurface(VanillaForegroundInput input, int pid, int generation, VanillaReconnectSettings config)
        {
            Stopwatch watch = Stopwatch.StartNew();
            int ready = 0;
            string last = "waiting for auth/server/proxy surfaces to disappear";
            while (watch.ElapsedMilliseconds < 30000)
            {
                if (HardenedStartupCancelled(generation)) throw new OperationCanceledException("Sequential startup cancelled.");
                using (Bitmap image = input.CaptureClientBitmap())
                {
                    VanillaLoginLayout login;
                    VanillaServerLayout server;
                    VanillaProxyLayout proxyLayout;
                    string evidence;
                    bool auth = VanillaAuthPattern.TryDetectLogin(image, out login, out evidence);
                    bool serverVisible = VanillaAuthPattern.TryDetectServerDialog(image, out server, out evidence);
                    bool proxyVisible = VanillaProxyPattern.TryDetect(image, out proxyLayout, out evidence);
                    bool usable = IsInteractiveFrame(image);
                    if (!auth && !serverVisible && !proxyVisible && usable && watch.ElapsedMilliseconds >= 500)
                    {
                        ready++;
                        if (ready >= 3)
                        {
                            try
                            {
                                string path = Path.Combine(baseDirectory, "Logs", "character-screen-ready.png");
                                Directory.CreateDirectory(Path.GetDirectoryName(path));
                                image.Save(path);
                            }
                            catch { }
                            Log("Sequential startup: character surface for PID " + pid + " became stable after " + watch.ElapsedMilliseconds + " ms; acting after three focused samples instead of a fixed load delay.");
                            return;
                        }
                    }
                    else
                    {
                        ready = 0;
                        last = "login=" + auth + ", server=" + serverVisible + ", proxy=" + proxyVisible + ", interactive=" + usable;
                    }
                }
                Thread.Sleep(180);
            }
            throw new InvalidOperationException("Character selection surface was not observed safely; client was left running and no later client was started. Last state: " + last);
        }

        private static bool IsInteractiveFrame(Bitmap image)
        {
            if (image == null || image.Width < 320 || image.Height < 240) return false;
            long sum = 0, sumSquares = 0;
            int count = 0;
            int stepX = Math.Max(8, image.Width / 48), stepY = Math.Max(8, image.Height / 32);
            for (int y = stepY / 2; y < image.Height; y += stepY)
            for (int x = stepX / 2; x < image.Width; x += stepX)
            {
                Color c = image.GetPixel(x, y);
                int lum = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                sum += lum;
                sumSquares += lum * lum;
                count++;
            }
            if (count == 0) return false;
            double mean = sum / (double)count;
            double variance = sumSquares / (double)count - mean * mean;
            return mean >= 18 && variance >= 180;
        }

        private void WaitForGameplayStable(VanillaForegroundInput input, int pid, int generation, int timeoutMs, string context)
        {
            Stopwatch watch = Stopwatch.StartNew();
            int consecutive = 0;
            VanillaVisualState last = VanillaVisualState.Unknown;
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (HardenedStartupCancelled(generation)) throw new OperationCanceledException("Sequential startup cancelled.");
                input.Activate();
                using (var process = Process.GetProcessById(pid))
                {
                    process.Refresh();
                    if (process.HasExited) throw new InvalidOperationException(context + ": Vanilla client exited while waiting for gameplay.");
                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        consecutive = 0;
                        Thread.Sleep(180);
                        continue;
                    }
                    last = VanillaVisualProbe.Classify(process.MainWindowHandle);
                }

                if (last == VanillaVisualState.Gameplay && watch.ElapsedMilliseconds >= 700)
                {
                    consecutive++;
                    if (consecutive >= 3)
                    {
                        Log(context + ": gameplay confirmed in three consecutive foreground samples after " + watch.ElapsedMilliseconds + " ms.");
                        return;
                    }
                }
                else consecutive = 0;

                Thread.Sleep(200);
            }
            throw new InvalidOperationException(context + ": gameplay was not confirmed stably within " + (timeoutMs / 1000)
                + " seconds (last visual state " + last + "); client was left running and no later client was started.");
        }

        private void HumanSettle(int generation, int milliseconds)
        {
            int remaining = Math.Max(0, milliseconds);
            while (remaining > 0)
            {
                if (HardenedStartupCancelled(generation)) throw new OperationCanceledException("Sequential startup cancelled.");
                int slice = Math.Min(80, remaining);
                Thread.Sleep(slice);
                remaining -= slice;
            }
        }

        private static bool HardenedIsAlive(int pid)
        {
            try { using (var process = Process.GetProcessById(pid)) return !process.HasExited; }
            catch { return false; }
        }
    }

    internal sealed partial class VanillaReconnectForm
    {
        private bool hardenedSupervisorButtonsInstalled;

        internal void InstallHardenedSupervisorButtons()
        {
            if (hardenedSupervisorButtonsInstalled) return;
            hardenedSupervisorButtonsInstalled = true;
            ReplaceSupervisorButton(this, "START SUPERVISOR", "START SUPERVISOR", StartSupervisorHardened,
                "Cold-start missing clients strictly one at a time. The current client must reach gameplay, receive its resume hotkey once, and be minimized before another client can start.");
            ReplaceSupervisorButton(this, "STOP", "STOP", StopSupervisorHardened,
                "Stop continuous supervision and cancel any in-progress serialized startup. Running Vanilla clients are left open.");
        }

        private void ReplaceSupervisorButton(Control root, string oldText, string newText, Action action, string tooltip)
        {
            foreach (Control child in root.Controls.Cast<Control>().ToArray())
            {
                var button = child as Button;
                if (button != null && string.Equals(button.Text, oldText, StringComparison.Ordinal))
                {
                    Control parent = button.Parent;
                    int index = parent.Controls.GetChildIndex(button);
                    parent.Controls.Remove(button);
                    button.Dispose();
                    var replacement = new Button { Text = newText, AutoSize = true, Margin = new Padding(4) };
                    replacement.Click += (s, e) => action();
                    parent.Controls.Add(replacement);
                    parent.Controls.SetChildIndex(replacement, index);
                    help.SetToolTip(replacement, tooltip);
                    return;
                }
                if (child.HasChildren) ReplaceSupervisorButton(child, oldText, newText, action, tooltip);
            }
        }

        private void StartSupervisorHardened()
        {
            try
            {
                if (testRunning) throw new InvalidOperationException("Stop the current diagnostic test before starting the supervisor.");
                if (supervisor.IsHardenedStartupRunning) throw new InvalidOperationException("Sequential startup is already running.");
                ReadTop();
                supervisor.Apply(settings, true);
                if (supervisor.IsRunning) supervisor.Stop();
                supervisor.DetectRunningClients();
                runState.Text = "STARTING";
                runState.ForeColor = Color.DarkOrange;
                testState.Text = "Sequential startup: one client at a time; waiting for observed UI states.";
                testState.ForeColor = Color.DarkSlateBlue;
                supervisor.StartHardenedSequentialStartup(HardenedSupervisorStartupCompleted);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Cannot start reconnect supervisor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopSupervisorHardened()
        {
            try { supervisor.CancelHardenedSequentialStartup(); } catch { }
            try { supervisor.Stop(); } catch { }
            RefreshStatus();
        }

        private void HardenedSupervisorStartupCompleted(bool success, string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => HardenedSupervisorStartupCompleted(success, message)));
                return;
            }

            RefreshStatus();
            testState.Text = success ? "SEQUENTIAL STARTUP COMPLETE" : "SEQUENTIAL STARTUP STOPPED";
            testState.ForeColor = success ? Color.DarkGreen : Color.DarkRed;
            if (!success)
                MessageBox.Show(this, message, "Sequential startup stopped", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

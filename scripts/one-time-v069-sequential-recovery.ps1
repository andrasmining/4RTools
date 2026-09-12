Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
function ReadText([string]$p){ [IO.File]::ReadAllText($p) }
function WriteText([string]$p,[string]$t){ [IO.File]::WriteAllText($p,$t,$utf8) }
function ReplaceRequired([string]$text,[string]$old,[string]$new,[string]$name){ if(-not $text.Contains($old)){ throw "Missing patch anchor: $name" }; return $text.Replace($old,$new) }

$path='Model/Vanilla/VanillaReconnect.cs'
$text=ReadText $path

$text=ReplaceRequired $text @'
        public int RetryBackoffMs { get; set; } = 30000;
        public int PopupCooldownMs { get; set; } = 5000;
'@ @'
        public int RetryBackoffMs { get; set; } = 30000;
        public int MaxRetryBackoffMs { get; set; } = 3600000;
        public int PopupCooldownMs { get; set; } = 5000;
'@ 'settings max backoff'

$text=ReplaceRequired $text @'
            if (RetryBackoffMs < 5000 || RetryBackoffMs > 600000) throw new ArgumentException("Retry backoff must be between 5 seconds and 10 minutes.");
            if (PopupCooldownMs < 1000 || PopupCooldownMs > 60000) throw new ArgumentException("Popup cooldown must be between 1 and 60 seconds.");
'@ @'
            if (RetryBackoffMs < 5000 || RetryBackoffMs > 600000) throw new ArgumentException("Retry backoff must be between 5 seconds and 10 minutes.");
            if (MaxRetryBackoffMs < RetryBackoffMs || MaxRetryBackoffMs > 3600000)
                throw new ArgumentException("Maximum reconnect backoff must be at least the base backoff and at most 60 minutes.");
            if (PopupCooldownMs < 1000 || PopupCooldownMs > 60000) throw new ArgumentException("Popup cooldown must be between 1 and 60 seconds.");
'@ 'settings validation'

$settingsEnd=@'
    public sealed class VanillaReconnectStatus
'@
$policy=@'
    internal static class VanillaRecoveryPolicy
    {
        internal static int RetryDelayMs(int failureCount, int baseMs, int maxMs)
        {
            if (failureCount <= 0) return 0;
            long delay = Math.Max(1, baseMs);
            long ceiling = Math.Max(delay, maxMs);
            for (int attempt = 1; attempt < failureCount && delay < ceiling; attempt++)
                delay = Math.Min(ceiling, delay * 2L);
            return (int)Math.Min(int.MaxValue, delay);
        }

        internal static bool BlocksParallelRecovery(bool recoveryOwned, bool scriptRunning)
        {
            return recoveryOwned || scriptRunning;
        }
    }

    public sealed class VanillaReconnectStatus
'@
$text=ReplaceRequired $text $settingsEnd $policy 'recovery policy insertion'

$text=ReplaceRequired $text @'
            public DateTimeOffset? LastLaunch;
            public bool ScriptRunning;
            public bool ResumeSent;
'@ @'
            public DateTimeOffset? LastLaunch;
            public DateTimeOffset? NextRecoveryAt;
            public int RecoveryFailures;
            public bool ScriptRunning;
            public bool ResumeSent;
            public bool RecoveryOwned;
            public bool HasBeenOnline;
'@ 'runtime recovery fields'

# Process-exit handling: distinguish a normal online disconnect from a failed recovery attempt.
$text=ReplaceRequired $text @'
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
'@ @'
                if (runtime.ProcessId.HasValue && !aliveIds.Contains(runtime.ProcessId.Value))
                {
                    int old = runtime.ProcessId.Value;
                    bool failedDuringRecovery = runtime.RecoveryOwned;
                    runtime.ProcessId = null;
                    runtime.ResumeSent = false;
                    runtime.Visual = VanillaVisualState.Unknown;
                    runtime.LoginLikeSince = runtime.GameplaySince = null;
                    runtime.ScriptRunning = false;
                    runtime.RecoveryOwned = false;
                    runtime.HasBeenOnline = false;
                    if (failedDuringRecovery)
                        ScheduleRecoveryFailureLocked(runtime, now, "PID " + old + " exited during recovery");
                    else
                    {
                        runtime.NextRecoveryAt = now;
                        SetStage(runtime, VanillaReconnectStage.WaitingForClient, "PID " + old + " exited; queued for sequential relaunch");
                        Log(runtime.Account.Label + ": Vanilla exited; first relaunch attempt is queued immediately.");
                    }
                }
'@ 'process exit recovery'

$text=$text.Replace('Bind(runtime, candidate.Id, runtime.LastLaunch.HasValue, runtime.LastLaunch.HasValue ? "New Vanilla client detected" : "Existing Vanilla client adopted");',
'Bind(runtime, candidate.Id, runtime.RecoveryOwned, runtime.RecoveryOwned ? "New Vanilla client detected" : "Existing Vanilla client adopted");')

# Add backoff guard at the start of Probe.
$text=ReplaceRequired $text @'
        private void Probe(Runtime runtime, DateTimeOffset now)
        {
            Process p = null;
            try
            {
                p = Process.GetProcessById(runtime.ProcessId.Value);
'@ @'
        private void Probe(Runtime runtime, DateTimeOffset now)
        {
            Process p = null;
            try
            {
                if (runtime.NextRecoveryAt.HasValue && runtime.NextRecoveryAt.Value > now)
                {
                    SetStage(runtime, VanillaReconnectStage.Backoff, BackoffDetail(runtime, now));
                    return;
                }
                p = Process.GetProcessById(runtime.ProcessId.Value);
'@ 'probe backoff guard'

# Gameplay success resets exponential backoff and releases the global sequential recovery lease.
$text=ReplaceRequired $text @'
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
'@ @'
                if (visual == VanillaVisualState.Gameplay)
                {
                    runtime.LoginLikeSince = null;
                    runtime.HasBeenOnline = true;
                    if (!runtime.GameplaySince.HasValue) runtime.GameplaySince = now;
                    if (!runtime.ResumeSent && (now - runtime.GameplaySince.Value).TotalMilliseconds >= Math.Max(1500, settings.StageDelayMs))
                    {
                        SendResume(runtime);
                        runtime.ResumeSent = true;
                        ResetRecoverySuccessLocked(runtime);
                        SetStage(runtime, VanillaReconnectStage.Online, "Gameplay detected; Autobattle resume hotkey sent once");
                    }
                    else if (runtime.ResumeSent)
                    {
                        ResetRecoverySuccessLocked(runtime);
                        SetStage(runtime, VanillaReconnectStage.Online, "Gameplay detected");
                    }
                    return;
                }
'@ 'gameplay success reset'

# A modal after confirmed gameplay is treated as the disconnect/logged-out condition the user described.
$text=ReplaceRequired $text @'
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
'@ @'
                if (visual == VanillaVisualState.ModalDialog)
                {
                    if (runtime.HasBeenOnline && settings.AutoRecover)
                    {
                        CloseForRecovery(runtime, p, now, "Disconnect/logged-out modal detected after gameplay", false);
                        return;
                    }
                    if (!runtime.LastPopup.HasValue || (now - runtime.LastPopup.Value).TotalMilliseconds >= settings.PopupCooldownMs)
                    {
                        using (var input = new VanillaTargetedInput(runtime.ProcessId.Value)) { input.Activate(); input.Press(Keys.Enter); }
                        runtime.LastPopup = now;
                        runtime.ResumeSent = false;
                        SetStage(runtime, VanillaReconnectStage.AcknowledgingPopup, "Pre-login modal detected; pressed Enter to acknowledge it");
                        Log(runtime.Account.Label + ": acknowledged a pre-login Vanilla modal dialog.");
                    }
                    return;
                }
'@ 'modal recovery behavior'

# Login/service shell after having been online means disconnect; after an attempted fresh login it means failure/backoff.
$text=ReplaceRequired $text @'
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
'@ @'
                if (visual == VanillaVisualState.LoginShell)
                {
                    if (!runtime.LoginLikeSince.HasValue) runtime.LoginLikeSince = now;
                    if (runtime.HasBeenOnline && settings.AutoRecover
                        && (now - runtime.LoginLikeSince.Value).TotalMilliseconds >= settings.LoginStableMs)
                    {
                        CloseForRecovery(runtime, p, now, "Login/service screen detected after gameplay disconnect", false);
                        return;
                    }
                    if (settings.AutoRecover && (now - runtime.LoginLikeSince.Value).TotalMilliseconds >= settings.LoginStableMs)
                    {
                        if (runtime.RecoveryOwned && runtime.LastRecovery.HasValue && !runtime.ScriptRunning)
                        {
                            CloseForRecovery(runtime, p, now, "Login/service screen remained after a recovery attempt", true);
                            return;
                        }
                        QueueLogin(runtime, runtime.RecoveryOwned, "Initial/recovery login shell detected");
                    }
                    else SetStage(runtime, VanillaReconnectStage.WaitingForGameplay, "Vanilla login/service screen detected");
                    return;
                }
'@ 'login shell recovery behavior'

# Probe exceptions during a claimed recovery become a bounded failure rather than a hot loop.
$text=ReplaceRequired $text @'
            catch (Exception ex)
            {
                SetStage(runtime, VanillaReconnectStage.Backoff, "Probe failed: " + ex.Message);
            }
'@ @'
            catch (Exception ex)
            {
                if (runtime.RecoveryOwned) ScheduleRecoveryFailureLocked(runtime, now, "Probe failed: " + ex.Message);
                else SetStage(runtime, VanillaReconnectStage.Backoff, "Probe failed: " + ex.Message);
            }
'@ 'probe exception backoff'

# Replace fixed launch gate with a per-account exponential schedule and global sequential lease.
$canStart=$text.IndexOf('        private bool CanLaunch(Runtime runtime, int aliveCount, DateTimeOffset now)')
$launchStart=$text.IndexOf('        private void Launch(Runtime runtime, DateTimeOffset now)', $canStart)
if($canStart -lt 0 -or $launchStart -le $canStart){ throw 'Missing CanLaunch/Launch block.' }
$canReplacement=@'
        private bool CanLaunch(Runtime runtime, int aliveCount, DateTimeOffset now)
        {
            if (!settings.AutoRecover || aliveCount >= settings.MaxClients || runtime.ScriptRunning) return false;
            Runtime owner = OtherRecoveryOwner(runtime);
            if (owner != null)
            {
                SetStage(runtime, VanillaReconnectStage.WaitingForClient,
                    "Queued: waiting for " + owner.Account.Label + " recovery to finish before starting this client");
                return false;
            }
            if (string.IsNullOrWhiteSpace(settings.LaunchExecutable) || !File.Exists(settings.LaunchExecutable))
            {
                SetStage(runtime, VanillaReconnectStage.NeedsConfiguration, "Set the Vanilla launch executable");
                return false;
            }
            if (runtime.NextRecoveryAt.HasValue && runtime.NextRecoveryAt.Value > now)
            {
                SetStage(runtime, VanillaReconnectStage.Backoff, BackoffDetail(runtime, now));
                return false;
            }
            return true;
        }

'@
$text=$text.Substring(0,$canStart)+$canReplacement+$text.Substring($launchStart)

# Launch acquires the sequential recovery lease and preserves failure count until Online.
$text=ReplaceRequired $text @'
            runtime.LastLaunch = now;
            runtime.ResumeSent = false;
            runtime.ScriptRunning = true;
'@ @'
            runtime.LastLaunch = now;
            runtime.NextRecoveryAt = null;
            runtime.ResumeSent = false;
            runtime.ScriptRunning = true;
            runtime.RecoveryOwned = true;
            runtime.HasBeenOnline = false;
'@ 'launch acquire lease'

$text=ReplaceRequired $text @'
                    else
                    {
                        SetStage(current, VanillaReconnectStage.Backoff, "Launch failed: " + error);
                        Log(label + ": launch failed: " + error);
                    }
'@ @'
                    else
                    {
                        ScheduleRecoveryFailureLocked(current, DateTimeOffset.UtcNow, "Launch failed: " + error);
                    }
'@ 'launch failure schedule'

# Bind preserves the lease only for a genuinely fresh recovery-launched process.
$text=ReplaceRequired $text @'
            runtime.ProcessId = pid;
            // Never toggle Autobattle merely because 4RTools adopted an already-running client.
            // A fresh launch or a relog sequence explicitly arms the one-shot resume hotkey.
            runtime.ResumeSent = !freshLaunch;
            runtime.LoginLikeSince = runtime.GameplaySince = null;
'@ @'
            runtime.ProcessId = pid;
            // Never toggle Autobattle merely because 4RTools adopted an already-running client.
            // A fresh launch or a relog sequence explicitly arms the one-shot resume hotkey.
            runtime.ResumeSent = !freshLaunch;
            runtime.RecoveryOwned = freshLaunch || runtime.RecoveryOwned;
            if (freshLaunch) runtime.HasBeenOnline = false;
            runtime.LoginLikeSince = runtime.GameplaySince = null;
'@ 'bind lease preservation'

# QueueLogin is also serialized globally; no two clients can type/select screens in parallel.
$text=ReplaceRequired $text @'
        private void QueueLogin(Runtime runtime, bool freshLaunch, string reason)
        {
            if (runtime.ScriptRunning || !runtime.ProcessId.HasValue) return;
            if (string.IsNullOrWhiteSpace(runtime.Account.UserName) || string.IsNullOrWhiteSpace(runtime.Account.ProtectedPassword))
'@ @'
        private void QueueLogin(Runtime runtime, bool freshLaunch, string reason)
        {
            if (runtime.ScriptRunning || !runtime.ProcessId.HasValue) return;
            Runtime owner = OtherRecoveryOwner(runtime);
            if (owner != null)
            {
                SetStage(runtime, VanillaReconnectStage.WaitingForGameplay,
                    "Queued: waiting for " + owner.Account.Label + " recovery to finish before login input");
                return;
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (runtime.NextRecoveryAt.HasValue && runtime.NextRecoveryAt.Value > now)
            {
                SetStage(runtime, VanillaReconnectStage.Backoff, BackoffDetail(runtime, now));
                return;
            }
            if (string.IsNullOrWhiteSpace(runtime.Account.UserName) || string.IsNullOrWhiteSpace(runtime.Account.ProtectedPassword))
'@ 'queue login sequential gate'

$text=ReplaceRequired $text @'
            runtime.ScriptRunning = true;
            runtime.LastRecovery = DateTimeOffset.UtcNow;
            runtime.ResumeSent = false;
'@ @'
            runtime.ScriptRunning = true;
            runtime.RecoveryOwned = true;
            runtime.LastRecovery = now;
            runtime.ResumeSent = false;
'@ 'queue login acquire lease'

# LoginWorker failures close the failed recovery client and move to exponential backoff.
$text=ReplaceRequired $text @'
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
'@ @'
            catch (Exception ex) { error = ex.Message; }
            finally
            {
                bool closedAfterFailure = false;
                string closeEvidence = null;
                if (error != null) closedAfterFailure = TryCloseProcess(pid, out closeEvidence);
                lock (gate)
                {
                    Runtime runtime;
                    if (runtimes.TryGetValue(accountId, out runtime) && runtime.ProcessId == pid)
                    {
                        runtime.ScriptRunning = false;
                        runtime.LoginLikeSince = runtime.GameplaySince = null;
                        if (error == null)
                        {
                            SetStage(runtime, VanillaReconnectStage.WaitingForGameplay,
                                "Login sequence completed; waiting for gameplay before sending " + account.HotkeyText);
                        }
                        else
                        {
                            if (closedAfterFailure) runtime.ProcessId = null;
                            runtime.HasBeenOnline = false;
                            ScheduleRecoveryFailureLocked(runtime, DateTimeOffset.UtcNow,
                                "Login sequence failed: " + error + (string.IsNullOrEmpty(closeEvidence) ? "" : "; " + closeEvidence));
                        }
                    }
                }
                if (error == null) Log(account.Label + ": login sequence completed; no password was logged.");
                RaiseUpdated();
            }
'@ 'login worker failure recovery'

# Insert recovery helpers before WaitForWindow.
$helperAnchor='        private static void WaitForWindow(int pid, int timeoutMs)'
$helpers=@'
        private Runtime OtherRecoveryOwner(Runtime except)
        {
            return runtimes.Values.FirstOrDefault(runtime => !object.ReferenceEquals(runtime, except)
                && VanillaRecoveryPolicy.BlocksParallelRecovery(runtime.RecoveryOwned, runtime.ScriptRunning));
        }

        private void ResetRecoverySuccessLocked(Runtime runtime)
        {
            if (runtime.RecoveryFailures > 0 || runtime.NextRecoveryAt.HasValue || runtime.RecoveryOwned)
                Log(runtime.Account.Label + ": recovery succeeded; exponential retry state reset.");
            runtime.RecoveryFailures = 0;
            runtime.NextRecoveryAt = null;
            runtime.RecoveryOwned = false;
        }

        private void ScheduleRecoveryFailureLocked(Runtime runtime, DateTimeOffset now, string reason)
        {
            runtime.RecoveryFailures = Math.Min(30, runtime.RecoveryFailures + 1);
            int delay = VanillaRecoveryPolicy.RetryDelayMs(runtime.RecoveryFailures, settings.RetryBackoffMs, settings.MaxRetryBackoffMs);
            runtime.NextRecoveryAt = now.AddMilliseconds(delay);
            runtime.RecoveryOwned = false;
            runtime.ScriptRunning = false;
            SetStage(runtime, VanillaReconnectStage.Backoff, reason + "; retry in " + FormatDelay(delay)
                + " (failure " + runtime.RecoveryFailures + ", capped at 1 hour)");
            Log(runtime.Account.Label + ": " + reason + "; next recovery attempt in " + FormatDelay(delay)
                + ". Backoff doubles after each failed attempt and is capped at 1 hour.");
        }

        private string BackoffDetail(Runtime runtime, DateTimeOffset now)
        {
            if (!runtime.NextRecoveryAt.HasValue) return "Waiting for next recovery attempt";
            TimeSpan remaining = runtime.NextRecoveryAt.Value - now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            return "Backoff after failed recovery; next attempt in " + FormatDelay((int)Math.Ceiling(remaining.TotalMilliseconds))
                + " (failure " + runtime.RecoveryFailures + ", max interval 1 hour)";
        }

        private static string FormatDelay(int milliseconds)
        {
            TimeSpan value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
            if (value.TotalMinutes >= 60) return "1h";
            if (value.TotalMinutes >= 1) return Math.Ceiling(value.TotalMinutes).ToString("0") + "m";
            return Math.Max(1, Math.Ceiling(value.TotalSeconds)).ToString("0") + "s";
        }

        private void CloseForRecovery(Runtime runtime, Process process, DateTimeOffset now, string reason, bool failedAttempt)
        {
            int pid = runtime.ProcessId.GetValueOrDefault();
            string evidence;
            bool exited = TryCloseProcess(process, out evidence);
            runtime.ProcessId = exited ? (int?)null : pid;
            runtime.ResumeSent = false;
            runtime.Visual = VanillaVisualState.Unknown;
            runtime.LoginLikeSince = runtime.GameplaySince = null;
            runtime.ScriptRunning = false;
            runtime.RecoveryOwned = false;
            runtime.HasBeenOnline = false;
            if (failedAttempt)
            {
                ScheduleRecoveryFailureLocked(runtime, now, reason + (string.IsNullOrEmpty(evidence) ? "" : "; " + evidence));
            }
            else
            {
                runtime.NextRecoveryAt = now;
                SetStage(runtime, exited ? VanillaReconnectStage.WaitingForClient : VanillaReconnectStage.Backoff,
                    reason + "; client close " + (exited ? "completed" : "is still pending") + "; sequential relaunch queued");
                Log(runtime.Account.Label + ": " + reason + "; " + evidence + ". Relaunch will be serialized with other clients.");
            }
        }

        private static bool TryCloseProcess(int pid, out string evidence)
        {
            try
            {
                using (var process = Process.GetProcessById(pid)) return TryCloseProcess(process, out evidence);
            }
            catch (Exception ex)
            {
                evidence = "process already gone or unavailable: " + ex.Message;
                return true;
            }
        }

        private static bool TryCloseProcess(Process process, out string evidence)
        {
            try
            {
                process.Refresh();
                if (process.HasExited) { evidence = "process already exited"; return true; }
                bool requested = process.CloseMainWindow();
                if (requested && process.WaitForExit(3000)) { evidence = "normal window close succeeded"; return true; }
                process.Refresh();
                if (!process.HasExited)
                {
                    process.Kill();
                    if (process.WaitForExit(3000)) { evidence = "normal close did not finish; process was terminated"; return true; }
                }
                evidence = "process did not exit after close/terminate request";
                return process.HasExited;
            }
            catch (Exception ex)
            {
                evidence = "client close failed: " + ex.Message;
                try { process.Refresh(); return process.HasExited; } catch { return true; }
            }
        }

'@
if(-not $text.Contains($helperAnchor)){ throw 'Missing WaitForWindow helper anchor.' }
$text=$text.Replace($helperAnchor,$helpers+$helperAnchor)

# Existing-client adoption must never hold the exclusive recovery lease.
$text=ReplaceRequired $text @'
                    runtime.ResumeSent = true;
                    runtime.ScriptRunning = false;
                    runtime.Visual = VanillaVisualState.Unknown;
'@ @'
                    runtime.ResumeSent = true;
                    runtime.ScriptRunning = false;
                    runtime.RecoveryOwned = false;
                    runtime.Visual = VanillaVisualState.Unknown;
'@ 'adopt existing lease reset'

# Remove the artificial restart test and clarify sequential cold-start semantics.
$text=$text.Replace('AddButton(tests, "TEST STARTUP (clients closed)", TestStartup);','AddButton(tests, "TEST STARTUP (SEQUENTIAL)", TestStartup);')
$text=$text.Replace('            AddButton(tests, "TEST RESTART RECOVERY", TestRestartRecovery);' + [Environment]::NewLine,'')
$text=$text.Replace('            TipByText(this, "TEST STARTUP (clients closed)", "Full cold-start test: launcher -> GAME START -> proxy -> login -> server -> character -> resume hotkey.");','            TipByText(this, "TEST STARTUP (SEQUENTIAL)", "Full cold-start test. With multiple configured clients, 4RTools completes launcher -> proxy -> login -> server -> character -> resume hotkey for ONE client before starting the next.");')
$text=$text.Replace('            TipByText(this, "TEST RESTART RECOVERY", "Close detected Vanilla windows normally and verify the full relaunch/relogin recovery path.");' + [Environment]::NewLine,'')

$restartStart=$text.IndexOf('        private void TestRestartRecovery()')
$networkStart=$text.IndexOf('        private void ArmManualNetworkDropTest()', $restartStart)
if($restartStart -ge 0 -and $networkStart -gt $restartStart){ $text=$text.Substring(0,$restartStart)+$text.Substring($networkStart) }

# Update cold-start copy; the test still requires a clean start but now explicitly validates sequential behavior.
$text=$text.Replace('"The cold-start test requires the Vanilla clients to be closed first. Close them, then press TEST STARTUP again."', '"The sequential cold-start test requires the managed Vanilla clients to be closed first. It will then recover them strictly one at a time."')
$text=$text.Replace('BeginTest("STARTUP TEST: launching " + configured.Length + " configured client(s)...")', 'BeginTest("STARTUP TEST: recovering " + configured.Length + " configured client(s) strictly one at a time...")')

WriteText $path $text

# Extend reconnect regression tests for the sequential gate and exponential cap.
$path='Tests/VanillaReconnectRegressionTests.cs'
$text=ReadText $path
$text=ReplaceRequired $text @'
            Test("Legacy login anchors migrate to verified field centers", LoginAnchorMigration);
            Console.WriteLine("Reconnect regressions: {0} passed; {1} failed. No live process was controlled.", passed, failed);
'@ @'
            Test("Legacy login anchors migrate to verified field centers", LoginAnchorMigration);
            Test("Reconnect backoff doubles and caps at one hour", ExponentialBackoff);
            Test("Recovery ownership blocks parallel client workflows", SequentialRecoveryGate);
            Console.WriteLine("Reconnect regressions: {0} passed; {1} failed. No live process was controlled.", passed, failed);
'@ 'regression test registration'
$insert='        private static void PersistentDataMigration()'
$methods=@'
        private static void ExponentialBackoff()
        {
            Assert(VanillaRecoveryPolicy.RetryDelayMs(1, 30000, 3600000) == 30000, "First failure should wait 30 seconds.");
            Assert(VanillaRecoveryPolicy.RetryDelayMs(2, 30000, 3600000) == 60000, "Second failure should wait 60 seconds.");
            Assert(VanillaRecoveryPolicy.RetryDelayMs(3, 30000, 3600000) == 120000, "Third failure should wait 120 seconds.");
            Assert(VanillaRecoveryPolicy.RetryDelayMs(8, 30000, 3600000) == 3600000, "Retry interval must cap at one hour.");
            Assert(VanillaRecoveryPolicy.RetryDelayMs(20, 30000, 3600000) == 3600000, "Retry interval exceeded one-hour cap.");
            var defaults = VanillaReconnectSettings.CreateDefault();
            Assert(defaults.MaxRetryBackoffMs == 3600000, "Default maximum retry backoff is not one hour.");
        }

        private static void SequentialRecoveryGate()
        {
            Assert(!VanillaRecoveryPolicy.BlocksParallelRecovery(false, false), "Idle client unexpectedly blocks recovery.");
            Assert(VanillaRecoveryPolicy.BlocksParallelRecovery(true, false), "Recovery owner did not block parallel recovery.");
            Assert(VanillaRecoveryPolicy.BlocksParallelRecovery(false, true), "Active script did not block parallel recovery.");
        }

'@
if(-not $text.Contains($insert)){ throw 'Missing regression insertion anchor.' }
$text=$text.Replace($insert,$methods+$insert)
WriteText $path $text

# Add a softened/resampled server-dialog recognition test to protect DPI/blur tolerance.
$path='Tests/VanillaAuthPatternTests.cs'
$text=ReadText $path
$text=$text.Replace('            Test("Server pattern detects the single-server dialog across resolutions", ServerAcrossResolutions);',
'            Test("Server pattern detects the single-server dialog across resolutions", ServerAcrossResolutions);'+[Environment]::NewLine+'            Test("Server pattern tolerates softened rendering", ServerSoftened);')
$anchor='        private static void RejectBlank()'
$soft=@'
        private static void ServerSoftened()
        {
            using (Bitmap large = ServerImage(1920, 1080))
            using (Bitmap reduced = new Bitmap(1280, 720))
            using (Graphics graphics = Graphics.FromImage(reduced))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(large, new Rectangle(0, 0, reduced.Width, reduced.Height));
                VanillaServerLayout layout;
                string evidence;
                Assert(VanillaAuthPattern.TryDetectServerDialog(reduced, out layout, out evidence), "Softened server dialog failed: " + evidence);
            }
        }

'@
if(-not $text.Contains($anchor)){ throw 'Missing auth test reject anchor.' }
$text=$text.Replace($anchor,$soft+$anchor)
WriteText $path $text

Write-Host '0.6.9 sequential recovery/backoff patch applied.'

"""One-use, reviewed audit transport. Removed after the Windows validation gates pass."""
from pathlib import Path
import subprocess
import sys

mode = sys.argv[1]
assert mode in ('tests', 'fix')
assert subprocess.check_output(['git', 'branch', '--show-current']).decode().strip() == 'fix/autobattle-movement-verification'

def edit(path, old, new):
    if path.startswith('Tests/') != (mode == 'tests'):
        return
    p = Path(path)
    raw = p.read_bytes()
    text = raw.decode('utf-8').replace('\r\n', '\n')
    assert text.count(old) == 1, (path, old[:100], text.count(old))
    text = text.replace(old, new)
    p.write_bytes(text.replace('\n', '\r\n').encode() if b'\r\n' in raw else text.encode())

p = 'Tests/VanillaAutobattleResumeTests.cs'
edit(p, '            Test("X movement verifies the first attempt",', '''            Test("Resume profiles are loaded from the executable directory", ProfileDirectory);
            Test("STOP cancels diagnostic completions", DiagnosticStop);
            Test("Settings changes cancel diagnostic completions", DiagnosticSettingsChange);
            Test("Rejected diagnostic requests keep the current generation", DiagnosticRejectedRequest);
            Test("Resume completion rejects a replaced PID", OwnedCompletion);
            Test("Failed or interrupted startup cannot be adopted as healthy", SafeAdoption);
            Test("Successful explicit resume clears the failed latch", DiagnosticResult);
            Test("X movement verifies the first attempt",''')
edit(p, '        private static readonly BindingFlags PrivateInstance', '''        private static void ProfileDirectory()
        {
            Assert(VanillaReconnectSupervisor.AutobattleBuildProfileDirectory ==
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VanillaBuilds"), "Profiles came from user data.");
        }
        private static void DiagnosticStop()
        {
            RuntimeCase((supervisor, runtime) =>
            {
                Field(supervisor, "diagnosticGeneration").SetValue(supervisor, 7);
                supervisor.Stop();
                Assert((bool)Method("DiagnosticCancelled").Invoke(supervisor, new object[] { 7 }), "STOP retained an old diagnostic.");
            });
        }
        private static void DiagnosticSettingsChange()
        {
            RuntimeCase((supervisor, runtime) =>
            {
                Field(supervisor, "diagnosticGeneration").SetValue(supervisor, 7);
                supervisor.Apply(supervisor.Settings, false);
                Assert((bool)Method("DiagnosticCancelled").Invoke(supervisor, new object[] { 7 }), "Changed settings retained an old diagnostic.");
            });
        }
        private static void DiagnosticRejectedRequest()
        {
            RuntimeCase((supervisor, runtime) =>
            {
                Field(supervisor, "diagnosticGeneration").SetValue(supervisor, 7);
                try { supervisor.RunDiagnosticStep(supervisor.Settings.Accounts[0].Id, VanillaReconnectTestStep.ResumeHotkey); }
                catch (InvalidOperationException) { }
                Assert((int)Field(supervisor, "diagnosticGeneration").GetValue(supervisor) == 7,
                    "Rejected request cancelled the current diagnostic while retaining its lease.");
            });
        }
        private static FieldInfo Field(object owner, string name) { return owner.GetType().GetField(name, PrivateInstance); }
        private static MethodInfo Method(string name)
        {
            var result = typeof(VanillaReconnectSupervisor).GetMethod(name,
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert(result != null, "Missing guarded operation: " + name);
            return result;
        }
        private static void OwnedCompletion()
        {
            RuntimeCase((supervisor, runtime) =>
            {
                bool called = false;
                Func<bool> action = () => { called = true; return true; };
                Func<bool> active = () => false;
                Assert((bool)Method("RunOwnedClientStep").Invoke(supervisor, new object[] { runtime, 42, active, action }) && called,
                    "Active owner could not complete its step.");
                called = false;
                RuntimeField(runtime, "ProcessId", (int?)43);
                try { Method("RunOwnedClientStep").Invoke(supervisor, new object[] { runtime, 42, active, action }); }
                catch (TargetInvocationException ex) { Assert(ex.InnerException is OperationCanceledException, "Wrong cancellation error."); }
                Assert(!called, "Old worker touched the replacement client.");
                RuntimeField(runtime, "ProcessId", (int?)42);
                try { Method("RunOwnedClientStep").Invoke(supervisor, new object[] { runtime, 42, (Func<bool>)(() => true), action }); }
                catch (TargetInvocationException ex) { Assert(ex.InnerException is OperationCanceledException, "Wrong cancellation error."); }
                Assert(!called, "Cancelled worker completed an action.");
            });
        }
        private static void SafeAdoption()
        {
            var method = Method("CanAdoptExistingGameplayClient");
            foreach (bool sent in new[] { false, true })
                foreach (bool failed in new[] { false, true })
                    foreach (bool busy in new[] { false, true })
                        Assert((bool)method.Invoke(null, new object[] { sent, failed, busy }) == (sent && !failed && !busy),
                            "Unverified or failed client was treated as healthy.");
        }
        private static void DiagnosticResult()
        {
            RuntimeCase((supervisor, runtime) =>
            {
                RuntimeField(runtime, "ResumeVerificationFailed", true);
                Method("RecordDiagnosticResumeResult").Invoke(null, new object[] { runtime, true, null });
                Assert(RuntimeFlag(runtime, "ResumeSent") && !RuntimeFlag(runtime, "ResumeVerificationFailed"),
                    "A successful explicit retry retained its failed latch.");
                Method("RecordDiagnosticResumeResult").Invoke(null, new object[] { runtime, false, "No movement" });
                Assert(!RuntimeFlag(runtime, "ResumeSent") && RuntimeFlag(runtime, "ResumeVerificationFailed"),
                    "Failed explicit retry was presented as successful.");
            });
        }

        private static readonly BindingFlags PrivateInstance''')
p = 'Model/Vanilla/VanillaAutobattleResume.cs'
edit(p, '        private void QueueVerifiedResume(Runtime runtime)', '''        private bool RunOwnedClientStep(Runtime owner, int pid, Func<bool> cancelled, Func<bool> action)
        {
            lock (gate)
            {
                Runtime current;
                if (cancelled() || disposed || !runtimes.TryGetValue(owner.Account.Id, out current)
                    || !ReferenceEquals(owner, current) || current.ProcessId != pid || !current.Account.Enabled)
                    throw new OperationCanceledException("Client ownership changed; no window action performed.");
                return action();
            }
        }

        internal static bool CanAdoptExistingGameplayClient(bool resumeSent, bool failed, bool busy)
        {
            return resumeSent && !failed && !busy;
        }

        private static void RecordDiagnosticResumeResult(Runtime runtime, bool succeeded, string failure)
        {
            runtime.ResumeSent = succeeded;
            runtime.ResumeVerificationFailed = !succeeded;
            runtime.ResumeFailureDetail = succeeded ? null : "Autobattle verification failed: " + failure;
            if (succeeded) runtime.HasBeenOnline = true;
        }

        private void QueueVerifiedResume(Runtime runtime)''')
edit(p, '                    if (!KeepAssignedClientMinimized(account.Id))', '''                    if (!RunOwnedClientStep(runtime, pid,
                        () => !IsRunning || ResumeWorkerCancelled(runtime, pid, generation),
                        () => KeepAssignedClientMinimized(account.Id)))''')
p = 'Model/Vanilla/VanillaReconnect.cs'
edit(p, '''                Interlocked.Increment(ref resumeVerificationGeneration);
                foreach (var active in runtimes.Values.Where(r => r.Stage == VanillaReconnectStage.VerifyingAutobattle))''', '''                Interlocked.Increment(ref resumeVerificationGeneration);
                Interlocked.Increment(ref diagnosticGeneration);
                Interlocked.Increment(ref hardenedStartupGeneration);
                hardenedStartupRunning = false;
                foreach (var active in runtimes.Values.Where(r => r.ScriptRunning))''')
edit(p, 'Settings changed during autobattle verification; no further hotkeys sent', 'Settings changed during startup/recovery; no further input sent')
edit(p, '''                Interlocked.Increment(ref resumeVerificationGeneration);
                Interlocked.Increment(ref hardenedStartupGeneration);
                hardenedStartupRunning = false;
                running = false;''', '''                Interlocked.Increment(ref resumeVerificationGeneration);
                Interlocked.Increment(ref diagnosticGeneration);
                Interlocked.Increment(ref hardenedStartupGeneration);
                hardenedStartupRunning = false;
                running = false;''')
edit(p, '''                    runtime.ScriptRunning = false;
                    runtime.RecoveryOwned = false;
                    SetStage(runtime, VanillaReconnectStage.Stopped, "Supervisor stopped");''', '''                    if (runtime.ScriptRunning && !runtime.ResumeSent)
                    {
                        runtime.ResumeVerificationFailed = true;
                        runtime.ResumeFailureDetail = "Startup/recovery was stopped before verification completed";
                    }
                    runtime.ScriptRunning = false;
                    runtime.RecoveryOwned = false;
                    SetStage(runtime, VanillaReconnectStage.Stopped, "Supervisor stopped");''')
edit(p, '            ThreadPool.QueueUserWorkItem(_ => LoginWorker(runtime.Account.Id, pid, account, config, freshLaunch));', '''            int generation = Interlocked.Increment(ref resumeVerificationGeneration);
            runtime.ResumeOperationGeneration = generation;
            ThreadPool.QueueUserWorkItem(_ => LoginWorker(runtime, pid, account, config, freshLaunch, generation));''')
edit(p, '''        private void LoginWorker(string accountId, int pid, VanillaReconnectAccount account, VanillaReconnectSettings config, bool freshLaunch)
        {
            string error = null;''', '''        private void LoginWorker(Runtime owner, int pid, VanillaReconnectAccount account, VanillaReconnectSettings config, bool freshLaunch, int generation)
        {
            string accountId = account.Id;
            Func<bool> cancelled = () => !IsRunning || ResumeWorkerCancelled(owner, pid, generation);
            string error = null;''')
edit(p, '''                using (var input = new VanillaForegroundInput(pid))
                {
                    input.Activate();
                    if (freshLaunch)''', '''                using (var input = new VanillaForegroundInput(pid))
                {
                    input.CancellationRequested = cancelled;
                    input.Activate();
                    if (freshLaunch)''')
edit(p, '''                if (error != null) closedAfterFailure = TryCloseProcess(pid, out closeEvidence);
                lock (gate)
                {
                    Runtime runtime;
                    if (runtimes.TryGetValue(accountId, out runtime) && runtime.ProcessId == pid)
                    {
                        runtime.ScriptRunning = false;''', '''                lock (gate)
                {
                    Runtime runtime;
                    if (!cancelled() && runtimes.TryGetValue(accountId, out runtime) && ReferenceEquals(owner, runtime)
                        && runtime.ProcessId == pid)
                    {
                        if (error != null) closedAfterFailure = TryCloseProcess(pid, out closeEvidence);
                        runtime.ScriptRunning = false;''')
edit(p, '''            string label = runtime.Account.Label;
            runtime.LastLaunch = now;''', '''            string label = runtime.Account.Label;
            int generation = Interlocked.Increment(ref resumeVerificationGeneration);
            runtime.ResumeOperationGeneration = generation;
            runtime.LastLaunch = now;''')
edit(p, '                                aborted = disposed || !running;', '''                                Runtime current;
                                aborted = disposed || !running || generation != Volatile.Read(ref resumeVerificationGeneration)
                                    || !runtimes.TryGetValue(accountId, out current) || !ReferenceEquals(runtime, current)
                                    || current.ResumeOperationGeneration != generation || !current.ScriptRunning;''')
edit(p, '''                    if (!runtimes.TryGetValue(accountId, out current)) return;
                    current.ScriptRunning = false;''', '''                    if (generation != Volatile.Read(ref resumeVerificationGeneration)
                        || !runtimes.TryGetValue(accountId, out current) || !ReferenceEquals(runtime, current)
                        || current.ResumeOperationGeneration != generation || !current.ScriptRunning) return;
                    current.ScriptRunning = false;''')
edit(p, '                            int routeIndex = (int)config.Proxy;', '''                            VanillaProxyRoute route = VanillaAccountProxyPreferences.Get(account.Id, config.Proxy);
                            int routeIndex = (int)route;''')
edit(p, 'Log(account.Label + ": proxy " + config.Proxy + " selected from detected safe row', 'Log(account.Label + ": proxy " + route + " selected from detected safe row')
p = 'Model/Vanilla/VanillaStartupOrchestrator.cs'
edit(p, '                        existingPid = runtime.ProcessId.HasValue && IsAlive(runtime.ProcessId.Value) ? runtime.ProcessId : null;', '''                        existingPid = runtime.ProcessId.HasValue && IsAlive(runtime.ProcessId.Value) ? runtime.ProcessId : null;
                        if (existingPid.HasValue && !CanAdoptExistingGameplayClient(runtime.ResumeSent,
                            runtime.ResumeVerificationFailed, runtime.ScriptRunning))
                            throw new InvalidOperationException(account.Label + ": existing client has an unfinished or failed startup. Verify it with the Resume hotkey test before starting later clients.");''')
edit(p, '''                        using (var input = new VanillaForegroundInput(existingPid.Value))
                            WaitForGameplayStable(input, existingPid.Value, generation, 15000, account.Label + " existing client");''', '''                        using (var input = new VanillaForegroundInput(existingPid.Value))
                        {
                            input.CancellationRequested = () => StartupCancelled(generation);
                            WaitForGameplayStable(input, existingPid.Value, generation, 15000, account.Label + " existing client");
                        }''')
edit(p, '                        if (!KeepAssignedClientMinimized(account.Id))', '''                        if (!RunOwnedClientStep(runtime, existingPid.Value, () => StartupCancelled(generation),
                            () => KeepAssignedClientMinimized(account.Id)))''')
edit(p, '''                            if (StartupCancelled(generation)) throw new OperationCanceledException("Sequential startup cancelled.");
                            runtime.ResumeSent = true;''', '''                            Runtime current;
                            if (StartupCancelled(generation) || !runtimes.TryGetValue(account.Id, out current)
                                || !ReferenceEquals(current, runtime) || runtime.ProcessId != existingPid.Value)
                                throw new OperationCanceledException("Sequential startup client changed.");
                            runtime.ResumeSent = true;''')
edit(p, '                    Bind(runtime, pid.Value, true, "Sequential startup: launcher produced Vanilla client");', '''                    Runtime current;
                    if (StartupCancelled(generation) || !runtimes.TryGetValue(account.Id, out current)
                        || !ReferenceEquals(runtime, current) || runtime.ResumeOperationGeneration != resumeGeneration)
                        throw new OperationCanceledException("Sequential startup cancelled before client binding.");
                    Bind(runtime, pid.Value, true, "Sequential startup: launcher produced Vanilla client");''')
edit(p, '''                using (var input = new VanillaForegroundInput(pid.Value))
                {
                    SelectProxyWhenVisible''', '''                using (var input = new VanillaForegroundInput(pid.Value))
                {
                    input.CancellationRequested = () => StartupCancelled(generation)
                        || ResumeWorkerCancelled(runtime, pid.Value, resumeGeneration);
                    SelectProxyWhenVisible''')
edit(p, '                bool minimized = KeepAssignedClientMinimized(account.Id);', '''                bool minimized = RunOwnedClientStep(runtime, pid.Value,
                    () => StartupCancelled(generation) || ResumeWorkerCancelled(runtime, pid.Value, resumeGeneration),
                    () => KeepAssignedClientMinimized(account.Id));''')
p = 'Model/Vanilla/VanillaReconnectDiagnostics.cs'
edit(p, '''            int generation = Interlocked.Increment(ref diagnosticGeneration);
            lock (gate)''', '''            int generation;
            lock (gate)''')
edit(p, '''                runtime.ScriptRunning = true;
                account = runtime.Account.Clone();''', '''                generation = Interlocked.Increment(ref diagnosticGeneration);
                runtime.ScriptRunning = true;
                account = runtime.Account.Clone();''')
edit(p, '''                    using (var input = new VanillaForegroundInput(discoveredPid.Value))
                    {''', '''                    using (var input = new VanillaForegroundInput(discoveredPid.Value))
                    {
                        input.CancellationRequested = () => DiagnosticCancelled(generation);''')
edit(p, '                                    int routeIndex = (int)config.Proxy;', '''                                    VanillaProxyRoute route = VanillaAccountProxyPreferences.Get(account.Id, config.Proxy);
                                    int routeIndex = (int)route;''')
edit(p, 'Log("TEST " + account.Label + ": proxy " + config.Proxy + " selected from detected safe row', 'Log("TEST " + account.Label + ": proxy " + route + " selected from detected safe row')
edit(p, '''                        if (runtimes.TryGetValue(accountId, out runtime))
                        {
                            if (discoveredPid.HasValue) runtime.ProcessId = discoveredPid;''', '''                        if (!DiagnosticCancelled(generation) && runtimes.TryGetValue(accountId, out runtime)
                            && (step == VanillaReconnectTestStep.LauncherGameStart || runtime.ProcessId == discoveredPid))
                        {
                            if (discoveredPid.HasValue) runtime.ProcessId = discoveredPid;
                            if (step == VanillaReconnectTestStep.ResumeHotkey)
                                RecordDiagnosticResumeResult(runtime, error == null && stopped == null, error ?? stopped);''')
p = 'Model/Vanilla/VanillaForegroundInput.cs'
edit(p, '        private readonly Process process;', '''        private static readonly object ForegroundGate = new object();
        private readonly Process process;''')
edit(p, '        public void Activate()\n        {', '''        public void Activate()
        {
            lock (ForegroundGate) ActivateCore();
        }

        private void ActivateCore()
        {''')
edit(p, '''        public string ClickNormalizedWithDiagnostics(double x, double y, bool requireForeground = true)
        {''', '''        public string ClickNormalizedWithDiagnostics(double x, double y, bool requireForeground = true)
        {
            lock (ForegroundGate) return ClickCore(x, y, requireForeground);
        }

        private string ClickCore(double x, double y, bool requireForeground)
        {
            ThrowIfCancelled();''')
edit(p, '            var down = new[] { new INPUT { type = INPUT_MOUSE,', '''            VerifyForeground();
            uint hitPid;
            GetWindowThreadProcessId(hitAtClick, out hitPid);
            if (hitPid != (uint)process.Id)
                throw new InvalidOperationException("Mouse target no longer belongs to the intended client; no click sent.");
            var down = new[] { new INPUT { type = INPUT_MOUSE,''')
edit(p, '''        public void Press(Keys key)
        {
            Activate();
            VanillaDebugLog.Write("INPUT", "PID=" + process.Id + " KEY " + key + " press; foreground=" + DescribeWindow(GetForegroundWindow()) + ".");
            SendKey(key, false);
            Thread.Sleep(70);
            SendKey(key, true);
            Thread.Sleep(45);
        }''', '''        public void Press(Keys key)
        {
            lock (ForegroundGate)
            {
                Activate();
                VerifyForeground();
                VanillaDebugLog.Write("INPUT", "PID=" + process.Id + " KEY " + key + " press.");
                SendKey(key, false);
                try { Thread.Sleep(70); }
                finally { SendKey(key, true); }
                Thread.Sleep(45);
            }
        }''')
edit(p, '''        internal void ChordInVerifiedForeground(bool ctrl, bool alt, bool shift, Keys key)
        {''', '''        internal void ChordInVerifiedForeground(bool ctrl, bool alt, bool shift, Keys key)
        {
            lock (ForegroundGate) ChordCore(ctrl, alt, shift, key);
        }

        private void VerifyForeground()
        {
            ThrowIfCancelled();
            if (process.HasExited || window == IntPtr.Zero || GetForegroundWindow() != window
                || !IsUsableWindowForProcess(window, process.Id))
                throw new InvalidOperationException("The intended client no longer owns foreground focus; no input sent.");
        }

        private void ChordCore(bool ctrl, bool alt, bool shift, Keys key)
        {''')
edit(p, '''        public void TypeText(string text)
        {
            if (text == null) return;''', '''        public void TypeText(string text)
        {
            lock (ForegroundGate) TypeTextCore(text);
        }

        private void TypeTextCore(string text)
        {
            if (text == null) return;''')
edit(p, '''                SendUnicode(c, false);
                SendUnicode(c, true);''', '''                VerifyForeground();
                SendUnicode(c, false);
                SendUnicode(c, true);''')
p = 'AGENTS.md'
edit(p, '## Mandatory Git workflow', '''## Final integration and branch cleanup

Completed work belongs in remote `main`, not a leftover working branch. Verify
that `main` contains the final tested commit, then delete completed task branches
locally and remotely. Remove obsolete temporary workflows and patch payloads.
The normal finished state is only `main`; preserve valid concurrent work before
integrating or removing an older task branch. Never delete release tags.

A genuine external blocker may leave one clearly identified working branch.
Resume it on the next pass, solve or replace the failed approach, integrate the
result into `main`, and delete it. Do not accumulate unfinished branches or hide
a failed implementation by abandoning it on another branch.

## Mandatory Git workflow''')
subprocess.run(['git', '-c', 'core.whitespace=cr-at-eol', 'diff', '--check'], check=True)
print('Applied reviewed', mode, 'edits.')

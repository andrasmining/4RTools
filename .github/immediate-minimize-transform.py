from pathlib import Path


def read(path):
    return Path(path).read_text(encoding="utf-8")


def write(path, text):
    Path(path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path, old, new):
    text = read(path)
    count = text.count(old)
    assert count == 1, f"{path}: expected one literal match, got {count}"
    write(path, text.replace(old, new, 1))


def replace_count(path, old, new, expected):
    text = read(path)
    count = text.count(old)
    assert count == expected, f"{path}: expected {expected} literal matches, got {count}"
    write(path, text.replace(old, new))


modes = "Model/Vanilla/VanillaReconnectTestModes.cs"
replace_once(modes,
'''        public bool MinimizeAssignedClient(string accountId)
        {
            return MinimizeAssignedClientCore(accountId, true);
        }

        public bool KeepAssignedClientMinimized(string accountId)
        {
            return MinimizeAssignedClientCore(accountId, false);
        }

        internal static bool AutomaticMinimizeReady(TimeSpan visibleFor, TimeSpan cursorIdleFor)
        {
            return visibleFor >= TimeSpan.FromSeconds(AutomaticMinimizeIdleSeconds)
                && cursorIdleFor >= TimeSpan.FromSeconds(AutomaticMinimizeIdleSeconds);
        }
''',
'''        public bool MinimizeAssignedClient(string accountId)
        {
            return MinimizeAssignedClientCore(accountId, true, false);
        }

        public bool KeepAssignedClientMinimized(string accountId)
        {
            return MinimizeAssignedClientCore(accountId, false, false);
        }

        internal static bool AutomaticMinimizeReady(TimeSpan visibleFor, TimeSpan cursorIdleFor)
        {
            return AutomaticMinimizeReady(visibleFor, cursorIdleFor, false);
        }

        internal static bool AutomaticMinimizeReady(TimeSpan visibleFor, TimeSpan cursorIdleFor, bool verifiedMovement)
        {
            return verifiedMovement
                || (visibleFor >= TimeSpan.FromSeconds(AutomaticMinimizeIdleSeconds)
                    && cursorIdleFor >= TimeSpan.FromSeconds(AutomaticMinimizeIdleSeconds));
        }
''')

replace_once(modes,
'''        private bool WaitForOwnedClientSafeMinimize(Runtime owner, int pid, Func<bool> cancelled, string context)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (cancelled == null) throw new ArgumentNullException(nameof(cancelled));
            Log(context + ": client ready; automatic minimize is waiting for 60s with the client visible and no cursor movement.");
            while (true)
            {
                if (cancelled()) throw new OperationCanceledException(context + ": minimization wait cancelled.");
                lock (gate)
                {
                    Runtime current;
                    if (disposed || !runtimes.TryGetValue(owner.Account.Id, out current) || !ReferenceEquals(owner, current)
                        || current.ProcessId != pid || !current.Account.Enabled || CharacterOwnershipChanged(current, pid))
                        throw new OperationCanceledException(context + ": client ownership changed during minimization grace.");
                }
                if (KeepAssignedClientMinimized(owner.Account.Id))
                {
                    lock (gate)
                    {
                        Runtime current;
                        if (!runtimes.TryGetValue(owner.Account.Id, out current) || !ReferenceEquals(owner, current) || current.ProcessId != pid)
                            throw new OperationCanceledException(context + ": client changed as minimization completed.");
                    }
                    return true;
                }
                Thread.Sleep(250);
            }
        }
''',
'''        private bool WaitForOwnedClientSafeMinimize(Runtime owner, int pid, Func<bool> cancelled, string context, bool verifiedMovement)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (cancelled == null) throw new ArgumentNullException(nameof(cancelled));
            Log(verifiedMovement
                ? context + ": X/Y movement verified; minimizing the owned client immediately. Pause/stop supervision first if you need to interact with it."
                : context + ": client ready without a fresh recovery movement handshake; automatic minimize is waiting for 60s with the client visible and no cursor movement.");
            while (true)
            {
                if (cancelled()) throw new OperationCanceledException(context + ": minimization wait cancelled.");
                lock (gate)
                {
                    Runtime current;
                    if (disposed || !runtimes.TryGetValue(owner.Account.Id, out current) || !ReferenceEquals(owner, current)
                        || current.ProcessId != pid || !current.Account.Enabled || CharacterOwnershipChanged(current, pid))
                        throw new OperationCanceledException(context + ": client ownership changed during minimization.");
                }
                if (MinimizeAssignedClientCore(owner.Account.Id, false, verifiedMovement))
                {
                    lock (gate)
                    {
                        Runtime current;
                        if (!runtimes.TryGetValue(owner.Account.Id, out current) || !ReferenceEquals(owner, current) || current.ProcessId != pid)
                            throw new OperationCanceledException(context + ": client changed as minimization completed.");
                    }
                    return true;
                }
                Thread.Sleep(250);
            }
        }
''')

replace_once(modes,
'''        private bool MinimizeAssignedClientCore(string accountId, bool testLog)
''',
'''        private bool MinimizeAssignedClientCore(string accountId, bool testLog, bool verifiedMovement)
''')
replace_count(modes,
'''                        if (!testLog)
                        {
''',
'''                        if (!testLog && !verifiedMovement)
                        {
''', 1)
replace_count(modes,
'''                    if (!testLog)
                    {
''',
'''                    if (!testLog && !verifiedMovement)
                    {
''', 1)
replace_once(modes,
'''                    Log((testLog ? "TEST " : string.Empty) + label + ": Vanilla client PID " + pid
                        + (testLog ? " minimized and left running." : " minimized after 60s visible + cursor-idle grace."));
''',
'''                    Log((testLog ? "TEST " : string.Empty) + label + ": Vanilla client PID " + pid
                        + (testLog ? " minimized and left running."
                            : verifiedMovement ? " minimized immediately after verified X/Y movement."
                            : " minimized after 60s visible + cursor-idle grace."));
''')

reconnect = "Model/Vanilla/VanillaReconnect.cs"
replace_once(reconnect,
'''                    if (!WaitForOwnedClientSafeMinimize(owner, pid, cancelled, account.Label + ": recovery"))
''',
'''                    if (!WaitForOwnedClientSafeMinimize(owner, pid, cancelled, account.Label + ": recovery", true))
''')

startup = "Model/Vanilla/VanillaStartupOrchestrator.cs"
replace_once(startup,
'''                        if (!WaitForOwnedClientSafeMinimize(runtime, existingPid.Value, () => StartupCancelled(generation),
                            account.Label + ": existing client"))
''',
'''                        if (!WaitForOwnedClientSafeMinimize(runtime, existingPid.Value, () => StartupCancelled(generation),
                            account.Label + ": existing client", false))
''')
replace_once(startup,
'''                bool minimized = WaitForOwnedClientSafeMinimize(runtime, pid.Value,
                    () => StartupCancelled(generation) || ResumeWorkerCancelled(runtime, pid.Value, resumeGeneration),
                    account.Label + ": sequential startup");
''',
'''                bool minimized = WaitForOwnedClientSafeMinimize(runtime, pid.Value,
                    () => StartupCancelled(generation) || ResumeWorkerCancelled(runtime, pid.Value, resumeGeneration),
                    account.Label + ": sequential startup", true);
''')

autobattle = "Model/Vanilla/VanillaAutobattleResume.cs"
replace_once(autobattle,
'''                    if (!WaitForOwnedClientSafeMinimize(runtime, pid,
                        () => !IsRunning || ResumeWorkerCancelled(runtime, pid, generation), account.Label + ": autobattle recovery"))
''',
'''                    if (!WaitForOwnedClientSafeMinimize(runtime, pid,
                        () => !IsRunning || ResumeWorkerCancelled(runtime, pid, generation), account.Label + ": autobattle recovery", true))
''')

tests = "Tests/VanillaRecoveryWatchdogTests.cs"
replace_once(tests,
'''            Test("Automatic minimization requires 60s visible and 60s cursor idle", MinimizeGrace);
''',
'''            Test("Automatic minimization requires 60s visible and 60s cursor idle", MinimizeGrace);
            Test("Verified recovery movement bypasses the 60s minimize grace", VerifiedMovementMinimize);
''')
replace_once(tests,
'''        private static void WatchdogHotkeyFirst()
''',
'''        private static void VerifiedMovementMinimize()
        {
            Assert(!VanillaReconnectSupervisor.AutomaticMinimizeReady(TimeSpan.Zero, TimeSpan.Zero, false),
                "Ordinary visible clients must still respect the user-presence grace.");
            Assert(VanillaReconnectSupervisor.AutomaticMinimizeReady(TimeSpan.Zero, TimeSpan.Zero, true),
                "Fresh verified recovery movement must allow immediate minimization.");
        }
        private static void WatchdogHotkeyFirst()
''')

agents = "Model/Vanilla/AGENTS.md"
replace_once(agents,
'''Automatic minimization must respect active human use. If a managed Vanilla window
is restored/maximized/otherwise visible, do not minimize it immediately. Require
at least 60 seconds continuously visible AND at least 60 seconds without machine
cursor movement. Any cursor movement restarts the idle grace; unavailable cursor
state fails closed and defers minimization. Manual explicit minimize remains
immediate. Startup/recovery that owns a serialized gate may wait for this grace
rather than stealing a window from an active user.
''',
'''Automatic minimization normally respects active human use. For an adopted/already-running
client or ordinary steady-state visibility, require at least 60 seconds continuously visible
AND at least 60 seconds without machine cursor movement. Any cursor movement restarts the idle
grace; unavailable cursor state fails closed and defers minimization. Manual explicit minimize
remains immediate. The exception is a client that 4RTools itself has just launched/relogged and
whose configured Autobattle hotkey has been verified by fresh X/Y movement: minimize that owned
client immediately after movement proof and release the serialized recovery/startup gate without
a 60-second wait. If the user needs to interact with a client during supervised recovery, pause
or stop supervision before doing so.
''')

assembly = "Properties/AssemblyInfo.cs"
replace_once(assembly, '[assembly: AssemblyVersion("0.6.45.0")]', '[assembly: AssemblyVersion("0.6.46.0")]')
replace_once(assembly, '[assembly: AssemblyFileVersion("0.6.45.0")]', '[assembly: AssemblyFileVersion("0.6.46.0")]')

notes = '''# 4RTools Vanilla 0.6.46

## Immediate minimize after verified Autobattle movement

A recovery/restart client no longer waits through the 60-second visible/cursor-idle grace after
the configured Autobattle hotkey has already been proven by fresh X/Y movement. Once movement
verification succeeds, 4RTools immediately minimizes that same owned client, confirms it is
minimized, completes the recovery, and releases the serialized gate for the next queued client.

The 60-second user-presence grace is still retained for adopted/already-running clients and normal
steady-state minimization where there is no fresh restart-owned movement handshake. Manual explicit
minimize remains immediate. If you need to actively interact with a client while supervised recovery
is running, pause or stop supervision first; recovery automation otherwise owns the restarted client
through movement verification and minimization.

This applies consistently to normal recovery login, sequential cold startup, and the shared
restart-only ResumeHotkey recovery path. It does not add any new hotkey sends, process access, game
memory writes, coordinate clicks, or parallel recovery behavior.

## Validation limits

Release validation covers the new immediate-after-movement policy and the retained 60-second grace
for ordinary/adopted clients, full Debug/Release regressions, portable package/launch checks, native
test-owned process recovery, and mock-data UI validation. Public assets, source identity, and updater
discovery are verified after publication. The Windows runner cannot reproduce the user's live RDP
session or run Vanilla/Gepard, so live in-game behavior remains limited to the user's supplied log
showing successful X/Y movement followed by the unnecessary 60-second wait that this release removes.
'''
write("RELEASE-NOTES.md", notes)

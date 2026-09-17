from pathlib import Path


def read(path):
    return Path(path).read_text(encoding='utf-8')


def write(path, text):
    Path(path).write_text(text, encoding='utf-8', newline='\n')


def replace_once(path, old, new):
    text = read(path)
    count = text.count(old)
    assert count == 1, f'{path}: expected one literal match, got {count}'
    write(path, text.replace(old, new, 1))


foreground = 'Model/Vanilla/VanillaForegroundInput.cs'
old_helper = '''        internal static bool IsTransientBootstrapWindow(string className, string title)
        {
            string cls = className ?? string.Empty;
            string caption = title ?? string.Empty;
            return cls.IndexOf("Gepard_Splash", StringComparison.OrdinalIgnoreCase) >= 0
                || caption.IndexOf("GepardSplash", StringComparison.OrdinalIgnoreCase) >= 0;
        }
'''
new_helper = '''        internal static bool IsTransientBootstrapWindow(string className, string title)
        {
            string cls = className ?? string.Empty;
            string caption = title ?? string.Empty;
            // Vanilla creates a hidden 1x1 GDI+ hook/helper before the actual game window.
            // It contains "Vanilla MMO" in its caption but is never an interactive game surface.
            // Restoring it makes the helper visible in the taskbar, so reject it just like Gepard splash windows.
            return cls.IndexOf("Gepard_Splash", StringComparison.OrdinalIgnoreCase) >= 0
                || caption.IndexOf("GepardSplash", StringComparison.OrdinalIgnoreCase) >= 0
                || cls.IndexOf("GDI+ Hook Window Class", StringComparison.OrdinalIgnoreCase) >= 0
                || caption.StartsWith("GDI+ Window (", StringComparison.OrdinalIgnoreCase);
        }
'''
replace_once(foreground, old_helper, new_helper)

patcher = 'Model/Vanilla/VanillaPatcherLauncher.cs'
old_structs = '''        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
'''
new_structs = '''        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public UIntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
'''
replace_once(patcher, old_structs, new_structs)

old_imports = '''        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
'''
new_imports = '''        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr SetActiveWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG message, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
'''
replace_once(patcher, old_imports, new_imports)

old_constants = '''        private const uint BM_CLICK = 0x00F5;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
'''
new_constants = '''        private const uint BM_CLICK = 0x00F5;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const int SW_RESTORE = 9;
        internal const int LauncherActivationTimeoutMs = 3000;
'''
replace_once(patcher, old_constants, new_constants)

old_resolved = '''                                log?.Invoke("Launcher window resolved: " + windowDescription + "; foreground=" + DescribeWindow(GetForegroundWindow())
                                    + "; childControls=" + childInventory + ".");

                                clickAttempt++;
'''
new_resolved = '''                                log?.Invoke("Launcher window resolved: " + windowDescription + "; foreground=" + DescribeWindow(GetForegroundWindow())
                                    + "; childControls=" + childInventory + ".");

                                string activationEvidence;
                                if (!TryActivateLauncherWindow(patcherPid.Value, launcherHwnd, cancelled, out activationEvidence))
                                {
                                    log?.Invoke("Launcher is visible but Windows foreground activation is not ready; no GAME START action sent. "
                                        + activationEvidence);
                                    nextClick = DateTime.UtcNow.AddMilliseconds(1000);
                                    continue;
                                }
                                log?.Invoke("Launcher foreground verified before GAME START detection. " + activationEvidence);

                                clickAttempt++;
'''
replace_once(patcher, old_resolved, new_resolved)

old_visual_click = '''                                    string inputEvidence;
                                    using (var input = new VanillaForegroundInput(patcherPid.Value, launcherHwnd))
                                        inputEvidence = input.ClickNormalizedWithDiagnostics(secondX, secondY, requireForeground: false);
                                    log?.Invoke(string.Format(
                                        "GAME START attempt #{0}: one visually confirmed click only at normalized=({1:0.000},{2:0.000}); first=[{3}]; second=[{4}]; input=[{5}]. Waiting {6}ms before any retry.",
                                        clickAttempt, secondX, secondY, firstEvidence, secondEvidence, inputEvidence, retryMs));
'''
new_visual_click = '''                                    // The button location came from two stable launcher-client captures. Send the one
                                    // click directly to that verified launcher HWND instead of depending on global cursor focus.
                                    string inputEvidence = ClickTargetedWindowAtPoint(patcherPid.Value, secondX, secondY);
                                    log?.Invoke(string.Format(
                                        "GAME START attempt #{0}: one visually confirmed launcher-window message at normalized=({1:0.000},{2:0.000}); first=[{3}]; second=[{4}]; input=[{5}]. No cursor movement or foreground-dependent SendInput was used. Waiting {6}ms before any retry.",
                                        clickAttempt, secondX, secondY, firstEvidence, secondEvidence, inputEvidence, retryMs));
'''
replace_once(patcher, old_visual_click, new_visual_click)

old_targeted = '''        private static string ClickTargetedWindowAtPoint(int processId, double x, double y)
        {
            IntPtr main = ResolveLauncherWindow(processId);
            if (main == IntPtr.Zero) throw new InvalidOperationException("Launcher main window handle is zero.");
            RECT rect;
            if (!GetClientRect(main, out rect)) throw new InvalidOperationException("Cannot read launcher client rectangle; err=" + Marshal.GetLastWin32Error());
            int width = Math.Max(1, rect.Right - rect.Left), height = Math.Max(1, rect.Bottom - rect.Top);
            var clientPoint = new POINT
            {
                X = Math.Max(0, Math.Min(width - 1, (int)Math.Round(x * width))),
                Y = Math.Max(0, Math.Min(height - 1, (int)Math.Round(y * height)))
            };
            var screenPoint = clientPoint;
            if (!ClientToScreen(main, ref screenPoint)) throw new InvalidOperationException("Cannot map launcher click to screen; err=" + Marshal.GetLastWin32Error());
            IntPtr hit = WindowFromPoint(screenPoint);
            IntPtr target = hit != IntPtr.Zero && (hit == main || IsChild(main, hit)) ? hit : main;
            var targetClient = screenPoint;
            if (!ScreenToClient(target, ref targetClient)) throw new InvalidOperationException("Cannot map launcher click to hit window; err=" + Marshal.GetLastWin32Error());
            IntPtr packed = new IntPtr((targetClient.Y << 16) | (targetClient.X & 0xFFFF));
            bool moved = PostMessage(target, WM_MOUSEMOVE, IntPtr.Zero, packed);
            bool down = PostMessage(target, WM_LBUTTONDOWN, new IntPtr(1), packed);
            Thread.Sleep(100);
            bool up = PostMessage(target, WM_LBUTTONUP, IntPtr.Zero, packed);
            int error = (!moved || !down || !up) ? Marshal.GetLastWin32Error() : 0;
            return "main=" + DescribeWindow(main) + "; screenPoint=(" + screenPoint.X + "," + screenPoint.Y + "); hit=" + DescribeWindow(hit)
                + "; messageTarget=" + DescribeWindow(target) + "; targetClient=(" + targetClient.X + "," + targetClient.Y + ")"
                + "; PostMessage(move/down/up)=" + moved + "/" + down + "/" + up + "; err=" + error;
        }
'''
new_targeted = '''        private static string ClickTargetedWindowAtPoint(int processId, double x, double y)
        {
            IntPtr main = ResolveLauncherWindow(processId);
            if (main == IntPtr.Zero || !IsWindow(main) || !IsWindowVisible(main))
                throw new InvalidOperationException("Launcher main window is no longer available.");
            uint ownerPid;
            GetWindowThreadProcessId(main, out ownerPid);
            if (ownerPid != (uint)processId)
                throw new InvalidOperationException("Launcher HWND ownership changed; no GAME START message sent.");
            RECT rect;
            if (!GetClientRect(main, out rect)) throw new InvalidOperationException("Cannot read launcher client rectangle; err=" + Marshal.GetLastWin32Error());
            int width = Math.Max(1, rect.Right - rect.Left), height = Math.Max(1, rect.Bottom - rect.Top);
            if (width < 200 || height < 120) throw new InvalidOperationException("Launcher client is too small for verified GAME START input.");
            var clientPoint = new POINT
            {
                X = Math.Max(0, Math.Min(width - 1, (int)Math.Round(x * width))),
                Y = Math.Max(0, Math.Min(height - 1, (int)Math.Round(y * height)))
            };
            IntPtr packed = new IntPtr((clientPoint.Y << 16) | (clientPoint.X & 0xFFFF));
            bool moved = PostMessage(main, WM_MOUSEMOVE, IntPtr.Zero, packed);
            bool down = PostMessage(main, WM_LBUTTONDOWN, new IntPtr(1), packed);
            Thread.Sleep(100);
            bool up = PostMessage(main, WM_LBUTTONUP, IntPtr.Zero, packed);
            int error = (!moved || !down || !up) ? Marshal.GetLastWin32Error() : 0;
            if (!moved || !down || !up)
                throw new InvalidOperationException("Launcher rejected the verified GAME START window message; err=" + error + ".");
            return "main=" + DescribeWindow(main) + "; ownerPID=" + ownerPid
                + "; client=" + width + "x" + height + "; targetClient=(" + clientPoint.X + "," + clientPoint.Y + ")"
                + "; foregroundAtMessage=" + DescribeWindow(GetForegroundWindow())
                + "; PostMessage(move/down/up)=" + moved + "/" + down + "/" + up + "; err=" + error;
        }
'''
replace_once(patcher, old_targeted, new_targeted)

activation_method = '''
        internal static bool TryActivateLauncherWindow(int processId, IntPtr hwnd, Func<bool> cancelled, out string evidence)
        {
            evidence = "launcher activation not attempted";
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd))
            {
                evidence = "launcher HWND is missing or not visible";
                return false;
            }
            uint ownerPid;
            uint targetThread = GetWindowThreadProcessId(hwnd, out ownerPid);
            if (ownerPid != (uint)processId || targetThread == 0)
            {
                evidence = "launcher HWND ownership mismatch; expected PID=" + processId + ", actualPID=" + ownerPid;
                return false;
            }
            if (cancelled != null && cancelled()) throw new OperationCanceledException("Patcher launch cancelled.");

            IntPtr before = GetForegroundWindow();
            if (before == hwnd)
            {
                evidence = "already foreground; target=" + DescribeWindow(hwnd);
                return true;
            }

            // Windows foreground-lock rules can reject SetForegroundWindow when the desktop or
            // another application owns the input queue. Temporarily attach only the involved
            // GUI input queues, activate the verified launcher HWND, then detach immediately.
            MSG message;
            PeekMessage(out message, IntPtr.Zero, 0, 0, 0); // ensure this worker owns a message queue
            uint currentThread = GetCurrentThreadId();
            uint foregroundPid;
            uint foregroundThread = before == IntPtr.Zero ? 0 : GetWindowThreadProcessId(before, out foregroundPid);
            bool attachedTarget = false, attachedForeground = false;
            int targetAttachError = 0, foregroundAttachError = 0;
            bool top = false, foregroundRequested = false;
            try
            {
                if (currentThread != targetThread)
                {
                    attachedTarget = AttachThreadInput(currentThread, targetThread, true);
                    if (!attachedTarget) targetAttachError = Marshal.GetLastWin32Error();
                }
                if (foregroundThread != 0 && foregroundThread != currentThread && foregroundThread != targetThread)
                {
                    attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
                    if (!attachedForeground) foregroundAttachError = Marshal.GetLastWin32Error();
                }
                bool targetQueueReady = currentThread == targetThread || attachedTarget;
                bool foregroundQueueReady = foregroundThread == 0 || foregroundThread == currentThread
                    || foregroundThread == targetThread || attachedForeground;
                if (!targetQueueReady || !foregroundQueueReady)
                {
                    evidence = "foreground input queues could not be safely joined; currentThread=" + currentThread
                        + ", targetThread=" + targetThread + ", foregroundThread=" + foregroundThread
                        + ", attachTarget=" + attachedTarget + " err=" + targetAttachError
                        + ", attachForeground=" + attachedForeground + " err=" + foregroundAttachError
                        + ". No foreground request or GAME START action sent.";
                    return false;
                }

                if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
                top = BringWindowToTop(hwnd);
                SetActiveWindow(hwnd);
                SetFocus(hwnd);
                foregroundRequested = SetForegroundWindow(hwnd);

                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < LauncherActivationTimeoutMs)
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException("Patcher launch cancelled.");
                    if (!IsWindow(hwnd) || !IsWindowVisible(hwnd)) break;
                    if (GetForegroundWindow() == hwnd)
                    {
                        evidence = "foreground acquired in " + watch.ElapsedMilliseconds + "ms; target=" + DescribeWindow(hwnd)
                            + ", before=" + DescribeWindow(before) + ", BringWindowToTop=" + top
                            + ", SetForegroundWindow=" + foregroundRequested + ", attachedTarget=" + attachedTarget
                            + ", attachedForeground=" + attachedForeground + ".";
                        return true;
                    }
                    Thread.Sleep(50);
                }
            }
            finally
            {
                if (attachedForeground) AttachThreadInput(currentThread, foregroundThread, false);
                if (attachedTarget) AttachThreadInput(currentThread, targetThread, false);
            }

            evidence = "foreground activation timed out; target=" + DescribeWindow(hwnd)
                + ", before=" + DescribeWindow(before) + ", after=" + DescribeWindow(GetForegroundWindow())
                + ", BringWindowToTop=" + top + ", SetForegroundWindow=" + foregroundRequested
                + ", attachTarget=" + attachedTarget + " err=" + targetAttachError
                + ", attachForeground=" + attachedForeground + " err=" + foregroundAttachError + ".";
            return false;
        }
'''
marker = '''        internal static bool SameGameStartCandidate(double firstX, double firstY, double secondX, double secondY)
'''
replace_once(patcher, marker, activation_method + '\n' + marker)

regressions = 'Tests/VanillaReconnectRegressionTests.cs'
old_registration = '''            Test("Gepard splash is never an interactive input target", GepardSplashIsTransient);
            Test("Real Vanilla game window outranks generic windows", GameWindowCandidateRanking);
'''
new_registration = '''            Test("Gepard splash and GDI hook helpers are never interactive targets", BootstrapHelpersAreTransient);
            Test("Real Vanilla game window outranks generic windows", GameWindowCandidateRanking);
'''
replace_once(regressions, old_registration, new_registration)

old_test = '''        private static void GepardSplashIsTransient()
        {
            Assert(VanillaForegroundInput.IsTransientBootstrapWindow("Gepard_Splash_Class", "GepardSplash"),
                "Known Gepard splash must never receive automated input.");
            Assert(!VanillaForegroundInput.IsTransientBootstrapWindow(
                    "Vanilla MMO | Gepard Shield 3.0 (^-_-^)", "Vanilla MMO | Gepard Shield 3.0 (^-_-^)") ,
                "Actual Vanilla game window was incorrectly classified as a splash.");
            Assert(VanillaForegroundInput.WindowCandidateScore(true, true, 780, 327, true, false,
                    "Gepard_Splash_Class", "GepardSplash") == int.MinValue,
                "Transient splash remained an eligible input candidate.");
        }
'''
new_test = '''        private static void BootstrapHelpersAreTransient()
        {
            Assert(VanillaForegroundInput.IsTransientBootstrapWindow("Gepard_Splash_Class", "GepardSplash"),
                "Known Gepard splash must never receive automated input.");
            Assert(VanillaForegroundInput.IsTransientBootstrapWindow("GDI+ Hook Window Class", "GDI+ Window (Vanilla MMO.exe)"),
                "The hidden 1x1 GDI+ hook helper must never be restored, focused, or receive input.");
            Assert(!VanillaForegroundInput.IsKnownVanillaGameWindow("GDI+ Hook Window Class", "GDI+ Window (Vanilla MMO.exe)"),
                "GDI+ helper was still recognized as a real game window because its caption contains Vanilla MMO.");
            Assert(!VanillaForegroundInput.IsTransientBootstrapWindow(
                    "Vanilla MMO | Gepard Shield 3.0 (^-_-^)", "Vanilla MMO | Gepard Shield 3.0 (^-_-^)") ,
                "Actual Vanilla game window was incorrectly classified as a helper.");
            Assert(VanillaForegroundInput.WindowCandidateScore(true, true, 1, 1, true, false,
                    "GDI+ Hook Window Class", "GDI+ Window (Vanilla MMO.exe)") == int.MinValue,
                "GDI+ helper remained eligible for ShowWindow/focus automation.");
            Assert(VanillaForegroundInput.WindowCandidateScore(true, true, 780, 327, true, false,
                    "Gepard_Splash_Class", "GepardSplash") == int.MinValue,
                "Transient splash remained an eligible input candidate.");
        }
'''
replace_once(regressions, old_test, new_test)

patcher_tests = 'Tests/VanillaPatcherLauncherTests.cs'
old_pacing = '''            Equal(2500, (int)type.GetField("LauncherSettleMs", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue(),
                "Launcher must settle before the first GAME START action.");
'''
new_pacing = '''            Equal(2500, (int)type.GetField("LauncherSettleMs", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue(),
                "Launcher must settle before the first GAME START action.");
            Equal(3000, (int)type.GetField("LauncherActivationTimeoutMs", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue(),
                "Launcher foreground acquisition must be bounded and fail closed.");
'''
replace_once(patcher_tests, old_pacing, new_pacing)

assembly = 'Properties/AssemblyInfo.cs'
text = read(assembly)
assert text.count('0.6.44.0') == 2, 'Unexpected 0.6.44 version occurrences'
write(assembly, text.replace('0.6.44.0', '0.6.45.0'))

notes = '''# 4RTools Vanilla 0.6.45

## Launcher foreground recovery when the desktop owns focus

The supplied 0.6.44 live log showed the exact showstopper: after the affected client exited,
the Vanilla launcher was visible but Windows `Program Manager`/desktop remained foreground.
The old GAME START path called `SetForegroundWindow`, Windows applied its foreground-lock rule,
the launcher only flashed orange on the taskbar, and the foreground-dependent click was rejected.
The user had to click the launcher manually before GAME START rendered as the active yellow button.

Launcher startup now uses the verified launcher HWND and a bounded Win32 input-queue activation
step. It temporarily joins only the current/launcher/foreground GUI input queues, requests focus
for that exact launcher window, verifies that it really became foreground, and immediately detaches.
If ownership/focus cannot be proven, no GAME START action is sent and the bounded launcher loop
retries safely. There are no taskbar/desktop coordinates.

After two stable visual GAME START detections, the single activation is now sent as a direct
client-window mouse message to the verified launcher HWND at the image-detected client point.
It does not move the machine cursor and does not depend on a global SendInput click remaining in
foreground. The existing 2.5-second launcher settle, double 750ms visual confirmation and 15-second
post-activation retry spacing remain.

## GDI+ helper window is permanently non-interactive

Vanilla creates a hidden 1x1 top-level `GDI+ Hook Window Class` named
`GDI+ Window (Vanilla MMO.exe)` before the real game window. Because its caption contains
`Vanilla MMO`, the old resolver mistakenly scored it as a real game window and called
`ShowWindow(SW_RESTORE)`, which made the strange GDI+ taskbar window visible. That helper is now
classified with the other non-interactive bootstrap windows: it is never restored, focused,
selected for visual recognition, or sent input. 4RTools waits for the real Vanilla/Gepard window.

## Validation limits

Release validation covers Debug/Release regressions for GDI-helper exclusion and bounded launcher
activation, portable launch/package checks, native test-owned process recovery and mock-data UI.
Public assets, source identity and updater discovery are verified after publication. The build
runner cannot reproduce the user's Chrome Remote Desktop foreground-lock state or run live
Vanilla/Gepard; the supplied 0.6.44 screenshots/debug log are the live evidence for the repaired
window-state paths.
'''
write('RELEASE-NOTES.md', notes)

agents = 'Model/Vanilla/AGENTS.md'
text = read(agents)
addition = '''

## Launcher and helper-window ownership

- Never treat `GDI+ Hook Window Class` / `GDI+ Window (...)`, IME helpers, Gepard splash windows, or other tiny bootstrap/helper surfaces as an interactive Vanilla game window. In particular, never call ShowWindow/restore/focus on the 1x1 GDI+ helper just because its title contains `Vanilla MMO`.
- Launcher GAME START must be bound to a verified launcher HWND/PID. Foreground acquisition must be ownership-checked, bounded, and fail closed; never use taskbar/desktop coordinates to work around Windows foreground lock.
- When GAME START has been detected twice at a stable launcher-client position, prefer one direct owned-window client message to that verified launcher HWND over a global cursor/SendInput click. No blind coordinate fallback is authorized.
'''
if '## Launcher and helper-window ownership' not in text:
    write(agents, text.rstrip() + addition)

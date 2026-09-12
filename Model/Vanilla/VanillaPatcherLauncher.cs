using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Starts the user-configured Vanilla launcher. When that launcher is patcher.exe
    /// or Vanilla Launcher.exe, it drives only the launcher's visible GAME START button
    /// and waits for a new Vanilla MMO process. It never inspects or modifies Gepard
    /// or game memory.
    /// </summary>
    internal static class VanillaPatcherLauncher
    {
        internal const double DefaultGameStartX = 0.50;
        internal const double DefaultGameStartY = 0.765;
        internal const int DefaultStartTimeoutMs = 120000;
        internal const int DefaultRetryMs = 3000;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

        internal static bool IsPatcher(string executablePath)
        {
            string name = Path.GetFileName(executablePath);
            return string.Equals(name, "patcher.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Vanilla Launcher.exe", StringComparison.OrdinalIgnoreCase);
        }

        internal static int? Launch(
            string executablePath,
            string arguments,
            Action<string> log,
            Func<bool> cancelled,
            int timeoutMs = DefaultStartTimeoutMs,
            int retryMs = DefaultRetryMs,
            double gameStartX = DefaultGameStartX,
            double gameStartY = DefaultGameStartY)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                throw new FileNotFoundException("The configured Vanilla launcher does not exist.", executablePath);
            if (timeoutMs < 5000 || timeoutMs > 600000) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            if (retryMs < 500 || retryMs > 30000) throw new ArgumentOutOfRangeException(nameof(retryMs));
            if (gameStartX < 0 || gameStartX > 1 || gameStartY < 0 || gameStartY > 1)
                throw new ArgumentOutOfRangeException("GAME START coordinates must be normalized to 0..1.");

            var before = new HashSet<int>(GetVanillaProcessIds());
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = Path.GetDirectoryName(executablePath),
                UseShellExecute = true
            };

            Process launched = null;
            try
            {
                launched = Process.Start(startInfo);
                if (!IsPatcher(executablePath))
                {
                    log?.Invoke("Started configured Vanilla executable: " + Path.GetFileName(executablePath));
                    return launched == null ? (int?)null : launched.Id;
                }

                log?.Invoke("Started Vanilla launcher; waiting for GAME START to become usable.");
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                DateTime nextClick = DateTime.MinValue;
                string launcherName = Path.GetFileNameWithoutExtension(executablePath);

                while (DateTime.UtcNow < deadline)
                {
                    if (cancelled != null && cancelled())
                    {
                        log?.Invoke("Patcher launch cancelled.");
                        return null;
                    }

                    int? vanillaPid = FindNewVanillaProcess(before);
                    if (vanillaPid.HasValue)
                    {
                        log?.Invoke("Patcher started Vanilla MMO (PID " + vanillaPid.Value + ").");
                        return vanillaPid;
                    }

                    if (DateTime.UtcNow >= nextClick)
                    {
                        int? patcherPid = FindLauncherWindowProcessId(launched, launcherName);
                        if (patcherPid.HasValue)
                        {
                            try
                            {
                                using (var process = Process.GetProcessById(patcherPid.Value))
                                {
                                    process.Refresh();
                                    IntPtr hwnd = process.MainWindowHandle;
                                    if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Launcher main window is not ready.");
                                    ClickVisibleChild(hwnd, gameStartX, gameStartY);
                                }
                                log?.Invoke("Clicked launcher GAME START on the child control under the visible button; waiting for Vanilla/Gepard startup.");
                            }
                            catch (Exception ex)
                            {
                                log?.Invoke("Launcher window is not ready yet: " + ex.Message);
                            }
                            nextClick = DateTime.UtcNow.AddMilliseconds(retryMs);
                        }
                    }

                    Thread.Sleep(250);
                }

                throw new TimeoutException("Vanilla launcher did not start a new Vanilla MMO client within " + (timeoutMs / 1000) + " seconds.");
            }
            finally
            {
                if (launched != null) launched.Dispose();
            }
        }

        private static void ClickVisibleChild(IntPtr launcherWindow, double x, double y)
        {
            ShowWindow(launcherWindow, 9);
            BringWindowToTop(launcherWindow);
            SetForegroundWindow(launcherWindow);
            Thread.Sleep(120);

            RECT rect;
            if (!GetClientRect(launcherWindow, out rect))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read launcher client area.");
            int width = Math.Max(1, rect.Right - rect.Left);
            int height = Math.Max(1, rect.Bottom - rect.Top);
            var screen = new POINT
            {
                X = Math.Max(0, Math.Min(width - 1, (int)Math.Round(x * width))),
                Y = Math.Max(0, Math.Min(height - 1, (int)Math.Round(y * height)))
            };
            if (!ClientToScreen(launcherWindow, ref screen))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot map launcher GAME START coordinate.");

            IntPtr target = WindowFromPoint(screen);
            if (target == IntPtr.Zero) target = launcherWindow;
            var local = screen;
            if (!ScreenToClient(target, ref local))
            {
                target = launcherWindow;
                local.X = Math.Max(0, Math.Min(width - 1, (int)Math.Round(x * width)));
                local.Y = Math.Max(0, Math.Min(height - 1, (int)Math.Round(y * height)));
            }
            IntPtr point = new IntPtr((local.Y << 16) | (local.X & 0xFFFF));
            Post(target, 0x0200, IntPtr.Zero, point);
            Post(target, 0x0201, new IntPtr(1), point);
            Post(target, 0x0202, IntPtr.Zero, point);
        }

        private static void Post(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            if (!PostMessage(hwnd, msg, w, l))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Launcher rejected mouse input message 0x" + msg.ToString("X") + ".");
        }

        internal static string PreferPatcherBesideClient(string clientExecutablePath)
        {
            if (string.IsNullOrWhiteSpace(clientExecutablePath)) return clientExecutablePath;
            string directory = Path.GetDirectoryName(clientExecutablePath);
            if (string.IsNullOrWhiteSpace(directory)) return clientExecutablePath;
            string vanillaLauncher = Path.Combine(directory, "Vanilla Launcher.exe");
            if (File.Exists(vanillaLauncher)) return vanillaLauncher;
            string patcher = Path.Combine(directory, "patcher.exe");
            return File.Exists(patcher) ? patcher : clientExecutablePath;
        }

        private static int[] GetVanillaProcessIds()
        {
            var processes = Process.GetProcessesByName("Vanilla MMO");
            try { return processes.Select(p => p.Id).ToArray(); }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        private static int? FindNewVanillaProcess(HashSet<int> before)
        {
            foreach (int pid in GetVanillaProcessIds())
                if (!before.Contains(pid)) return pid;
            return null;
        }

        private static int? FindLauncherWindowProcessId(Process launched, string launcherName)
        {
            if (launched != null)
            {
                try
                {
                    if (!launched.HasExited)
                    {
                        launched.Refresh();
                        if (launched.MainWindowHandle != IntPtr.Zero) return launched.Id;
                    }
                }
                catch (InvalidOperationException) { }
            }

            Process[] candidates;
            try { candidates = Process.GetProcessesByName(launcherName); }
            catch { return null; }
            try
            {
                Process best = null;
                DateTime bestStart = DateTime.MinValue;
                foreach (var process in candidates)
                {
                    try
                    {
                        process.Refresh();
                        if (process.MainWindowHandle == IntPtr.Zero) continue;
                        DateTime started = process.StartTime;
                        if (best == null || started > bestStart)
                        {
                            best = process;
                            bestStart = started;
                        }
                    }
                    catch (Exception) { }
                }
                return best == null ? (int?)null : best.Id;
            }
            finally { foreach (var process in candidates) process.Dispose(); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Starts the user-configured Vanilla launcher. For patcher.exe / Vanilla Launcher.exe
    /// it visually locates the yellow GAME START control when possible, performs a normal
    /// foreground mouse click, and waits for a new Vanilla MMO process.
    /// It never inspects/modifies Gepard or game memory.
    /// </summary>
    internal static class VanillaPatcherLauncher
    {
        internal const double DefaultGameStartX = 0.50;
        internal const double DefaultGameStartY = 0.765;
        internal const int DefaultStartTimeoutMs = 120000;
        internal const int DefaultRetryMs = 8000;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

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

                log?.Invoke("Started Vanilla launcher; visually locating GAME START.");
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                DateTime nextClick = DateTime.MinValue;
                string launcherName = Path.GetFileNameWithoutExtension(executablePath);
                string launcherDirectory = Path.GetDirectoryName(executablePath);
                int fallbackAttempt = 0;
                double[] fallbackY = { gameStartY, 0.795, 0.825, 0.745 };

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
                        int? patcherPid = FindLauncherWindowProcessId(launched, launcherName, launcherDirectory);
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

        /// <summary>
        /// Finds the active yellow GAME START surface in the lower-center area of a launcher
        /// snapshot. The detector intentionally ignores the green WEBSITE/WIKI/etc. buttons.
        /// </summary>
        internal static bool TryFindGameStart(Bitmap bitmap, out double x, out double y, out string evidence)
        {
            x = DefaultGameStartX;
            y = DefaultGameStartY;
            evidence = "no yellow GAME START candidate";
            if (bitmap == null || bitmap.Width < 200 || bitmap.Height < 120) return false;

            int minScanX = (int)(bitmap.Width * 0.30);
            int maxScanX = (int)(bitmap.Width * 0.70);
            int minScanY = (int)(bitmap.Height * 0.63);
            int maxScanY = (int)(bitmap.Height * 0.90);
            int minX = bitmap.Width, minY = bitmap.Height, maxX = -1, maxY = -1, count = 0;

            for (int py = minScanY; py < maxScanY; py++)
            {
                for (int px = minScanX; px < maxScanX; px++)
                {
                    Color c = bitmap.GetPixel(px, py);
                    if (!IsGameStartYellow(c)) continue;
                    count++;
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                }
            }

            if (count < 90 || maxX <= minX || maxY <= minY) return false;
            double widthRatio = (double)(maxX - minX + 1) / bitmap.Width;
            double heightRatio = (double)(maxY - minY + 1) / bitmap.Height;
            double centerX = ((minX + maxX) / 2.0) / bitmap.Width;
            double centerY = ((minY + maxY) / 2.0) / bitmap.Height;

            if (widthRatio < 0.08 || heightRatio < 0.02 || centerX < 0.38 || centerX > 0.62 || centerY < 0.68 || centerY > 0.88)
            {
                evidence = string.Format("yellow candidate rejected: center=({0:0.000},{1:0.000}), size={2:0.000}x{3:0.000}, pixels={4}",
                    centerX, centerY, widthRatio, heightRatio, count);
                return false;
            }

            x = centerX;
            y = centerY;
            evidence = string.Format("visual yellow-button detector, box {0}x{1}, pixels={2}",
                maxX - minX + 1, maxY - minY + 1, count);
            return true;
        }

        private static bool IsGameStartYellow(Color c)
        {
            return c.R >= 220
                && c.G >= 135 && c.G <= 225
                && c.B <= 100
                && c.R - c.B >= 120
                && c.G - c.B >= 55;
        }

        private static bool TryFindGameStartOnWindow(int processId, out double x, out double y, out string evidence)
        {
            x = DefaultGameStartX;
            y = DefaultGameStartY;
            evidence = "window capture unavailable";
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    process.Refresh();
                    IntPtr hwnd = process.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) return false;
                    RECT rect;
                    if (!GetClientRect(hwnd, out rect)) return false;
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    if (width < 200 || height < 120) return false;

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
                }
            }
            catch (Exception ex)
            {
                evidence = "window capture failed: " + ex.Message;
                return false;
            }
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

        private static int? FindLauncherWindowProcessId(Process launched, string launcherName, string launcherDirectory)
        {
            var candidates = new Dictionary<int, Process>();
            Action<Process> add = process =>
            {
                if (process == null) return;
                if (!candidates.ContainsKey(process.Id)) candidates.Add(process.Id, process);
                else process.Dispose();
            };

            if (launched != null)
            {
                try { if (!launched.HasExited) add(Process.GetProcessById(launched.Id)); }
                catch { }
            }

            foreach (string processName in new[] { launcherName, "Vanilla Launcher", "patcher" }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Process[] found;
                try { found = Process.GetProcessesByName(processName); }
                catch { continue; }
                foreach (var process in found) add(process);
            }

            try
            {
                Process best = null;
                DateTime bestStart = DateTime.MinValue;
                foreach (var process in candidates.Values)
                {
                    try
                    {
                        process.Refresh();
                        if (process.MainWindowHandle == IntPtr.Zero) continue;
                        if (!string.IsNullOrWhiteSpace(launcherDirectory))
                        {
                            try
                            {
                                string candidateDirectory = Path.GetDirectoryName(process.MainModule.FileName);
                                if (!string.Equals(Path.GetFullPath(candidateDirectory), Path.GetFullPath(launcherDirectory), StringComparison.OrdinalIgnoreCase))
                                    continue;
                            }
                            catch { }
                        }
                        DateTime started = process.StartTime;
                        if (best == null || started > bestStart)
                        {
                            best = process;
                            bestStart = started;
                        }
                    }
                    catch { }
                }
                return best == null ? (int?)null : best.Id;
            }
            finally { foreach (var process in candidates.Values) process.Dispose(); }
        }
    }
}

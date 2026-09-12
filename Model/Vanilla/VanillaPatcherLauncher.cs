using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Starts the user-configured Vanilla launcher. For patcher.exe / Vanilla Launcher.exe
    /// it visually locates the yellow GAME START control when possible, performs ordinary
    /// Windows input, and waits for a new Vanilla MMO process.
    /// It never inspects/modifies Gepard or game memory.
    /// </summary>
    internal static class VanillaPatcherLauncher
    {
        internal const double DefaultGameStartX = 0.50;
        internal const double DefaultGameStartY = 0.765;
        internal const int DefaultStartTimeoutMs = 120000;
        internal const int DefaultRetryMs = 10000;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out UIntPtr result);

        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint BM_CLICK = 0x00F5;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

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
            double gameStartY = DefaultGameStartY,
            string debugDirectory = null)
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
                log?.Invoke("Launcher start requested: exe='" + Path.GetFileName(executablePath) + "', startedPID="
                    + (launched == null ? "none" : launched.Id.ToString()) + ", preExistingVanillaPIDs=[" + string.Join(",", before.OrderBy(v => v))
                    + "], timeout=" + timeoutMs + "ms, retry=" + retryMs + "ms.");
                if (!IsPatcher(executablePath))
                {
                    log?.Invoke("Started configured Vanilla executable directly.");
                    return launched == null ? (int?)null : launched.Id;
                }

                log?.Invoke("Started Vanilla launcher; locating GAME START.");
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                DateTime nextClick = DateTime.MinValue;
                string launcherName = Path.GetFileNameWithoutExtension(executablePath);
                string launcherDirectory = Path.GetDirectoryName(executablePath);
                int fallbackAttempt = 0;
                int clickAttempt = 0;
                double[] fallbackY = { gameStartY, 0.795, 0.825, 0.745 };
                bool sawLauncherWindow = false;
                DateTime? launcherWindowLostAt = null;
                int noWindowLogs = 0;

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
                            sawLauncherWindow = true;
                            launcherWindowLostAt = null;
                            try
                            {
                                IntPtr launcherHwnd = GetMainWindow(patcherPid.Value);
                                string windowDescription = DescribeWindow(launcherHwnd);
                                string childInventory;
                                IntPtr nativeGameStart;
                                bool nativeFound = TryFindNativeGameStart(launcherHwnd, out nativeGameStart, out childInventory);
                                log?.Invoke("Launcher window resolved: " + windowDescription + "; foreground=" + DescribeWindow(GetForegroundWindow())
                                    + "; childControls=" + childInventory + ".");

                                if (nativeFound)
                                {
                                    UIntPtr result;
                                    int beforeError = Marshal.GetLastWin32Error();
                                    IntPtr sent = SendMessageTimeout(nativeGameStart, BM_CLICK, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out result);
                                    int error = sent == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
                                    log?.Invoke("GAME START native control invoke: target=" + DescribeWindow(nativeGameStart)
                                        + "; SendMessageTimeout=" + (sent == IntPtr.Zero ? "FAILED" : "OK") + "; err=" + error + "; priorErr=" + beforeError + ".");
                                }

                                double clickX = gameStartX;
                                double clickY;
                                string detectorEvidence;
                                bool visuallyDetected = TryFindGameStartOnWindow(patcherPid.Value, debugDirectory, out clickX, out clickY, out detectorEvidence);
                                string evidence = detectorEvidence;
                                if (!visuallyDetected)
                                {
                                    clickY = fallbackY[fallbackAttempt % fallbackY.Length];
                                    fallbackAttempt++;
                                    evidence += "; fallback sweep used";
                                }

                                string strategy;
                                string inputEvidence;
                                if ((clickAttempt++ & 1) == 0)
                                {
                                    using (var input = new VanillaForegroundInput(patcherPid.Value))
                                        inputEvidence = input.ClickNormalizedWithDiagnostics(clickX, clickY, requireForeground: false);
                                    strategy = "screen-coordinate SendInput";
                                }
                                else
                                {
                                    inputEvidence = ClickTargetedWindowAtPoint(patcherPid.Value, clickX, clickY);
                                    strategy = "targeted hit-window message";
                                }

                                log?.Invoke(string.Format(
                                    "GAME START attempt #{0}: normalized=({1:0.000},{2:0.000}); strategy={3}; detector=[{4}]; input=[{5}]. Waiting for Vanilla/Gepard startup before retry.",
                                    clickAttempt, clickX, clickY, strategy, evidence, inputEvidence));
                            }
                            catch (Exception ex)
                            {
                                log?.Invoke("Launcher click attempt failed before completion: " + ex.GetType().Name + ": " + ex.Message);
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
                            noWindowLogs++;
                            if (noWindowLogs <= 5 || noWindowLogs % 5 == 0)
                                log?.Invoke("Launcher process exists but no usable launcher window is visible yet. "
                                    + DescribeLauncherCandidates(launched, launcherName, launcherDirectory));
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
            if (bitmap == null || bitmap.Width < 200 || bitmap.Height < 120)
            {
                evidence = bitmap == null ? "bitmap is null" : "bitmap too small: " + bitmap.Width + "x" + bitmap.Height;
                return false;
            }

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

            if (count < 90 || maxX <= minX || maxY <= minY)
            {
                evidence = "yellow detector found " + count + " matching pixels in bitmap " + bitmap.Width + "x" + bitmap.Height
                    + " scan=[" + minScanX + "," + minScanY + ".." + maxScanX + "," + maxScanY + "]";
                return false;
            }
            double widthRatio = (double)(maxX - minX + 1) / bitmap.Width;
            double heightRatio = (double)(maxY - minY + 1) / bitmap.Height;
            double centerX = ((minX + maxX) / 2.0) / bitmap.Width;
            double centerY = ((minY + maxY) / 2.0) / bitmap.Height;

            if (widthRatio < 0.08 || heightRatio < 0.02 || centerX < 0.38 || centerX > 0.62 || centerY < 0.68 || centerY > 0.88)
            {
                evidence = string.Format("yellow candidate rejected: center=({0:0.000},{1:0.000}), size={2:0.000}x{3:0.000}, pixels={4}, bitmap={5}x{6}",
                    centerX, centerY, widthRatio, heightRatio, count, bitmap.Width, bitmap.Height);
                return false;
            }

            x = centerX;
            y = centerY;
            evidence = string.Format("visual yellow-button detector, center=({0:0.000},{1:0.000}), box={2}x{3}, pixels={4}, bitmap={5}x{6}",
                centerX, centerY, maxX - minX + 1, maxY - minY + 1, count, bitmap.Width, bitmap.Height);
            return true;
        }

        private static bool IsGameStartYellow(Color c)
        {
            return c.R >= 220
                && c.G >= 135 && c.G <= 235
                && c.B <= 120
                && c.R - c.B >= 105
                && c.G - c.B >= 45;
        }

        private static bool TryFindGameStartOnWindow(int processId, string debugDirectory, out double x, out double y, out string evidence)
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
                    if (hwnd == IntPtr.Zero) { evidence = "main window handle is zero"; return false; }
                    RECT rect;
                    if (!GetClientRect(hwnd, out rect)) { evidence = "GetClientRect failed err=" + Marshal.GetLastWin32Error(); return false; }
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    if (width < 200 || height < 120) { evidence = "client too small: " + width + "x" + height; return false; }

                    string printEvidence = "PrintWindow not attempted";
                    using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                    {
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            IntPtr hdc = graphics.GetHdc();
                            bool captured;
                            int captureError;
                            try { captured = PrintWindow(hwnd, hdc, 1); captureError = captured ? 0 : Marshal.GetLastWin32Error(); }
                            finally { graphics.ReleaseHdc(hdc); }
                            SaveDebugBitmap(debugDirectory, "launcher-print-last.png", bitmap);
                            double printX, printY;
                            string detector = captured ? "detector not yet evaluated" : "PrintWindow failed before detector";
                            if (captured && TryFindGameStart(bitmap, out printX, out printY, out detector))
                            {
                                x = printX; y = printY;
                                evidence = "PrintWindow OK; " + detector + DebugCaptureSuffix(debugDirectory, "launcher-print-last.png");
                                return true;
                            }
                            printEvidence = "PrintWindow=" + captured + " err=" + captureError + "; detector=" + detector;
                        }

                        var origin = new POINT { X = 0, Y = 0 };
                        if (!ClientToScreen(hwnd, ref origin))
                        {
                            evidence = printEvidence + "; ClientToScreen failed err=" + Marshal.GetLastWin32Error();
                            return false;
                        }
                        using (var graphics = Graphics.FromImage(bitmap))
                            graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(width, height));
                        SaveDebugBitmap(debugDirectory, "launcher-screen-last.png", bitmap);
                        string screenEvidence;
                        if (!TryFindGameStart(bitmap, out x, out y, out screenEvidence))
                        {
                            evidence = printEvidence + "; visibleScreen detector=" + screenEvidence + "; origin=(" + origin.X + "," + origin.Y + ")"
                                + DebugCaptureSuffix(debugDirectory, "launcher-screen-last.png");
                            return false;
                        }
                        evidence = printEvidence + "; visibleScreen OK; " + screenEvidence + "; origin=(" + origin.X + "," + origin.Y + ")"
                            + DebugCaptureSuffix(debugDirectory, "launcher-screen-last.png");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                evidence = "window capture failed: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static string ClickTargetedWindowAtPoint(int processId, double x, double y)
        {
            IntPtr main = GetMainWindow(processId);
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

        private static bool TryFindNativeGameStart(IntPtr root, out IntPtr control, out string inventory)
        {
            IntPtr found = IntPtr.Zero;
            var rows = new List<string>();
            if (root == IntPtr.Zero) { control = IntPtr.Zero; inventory = "root=none"; return false; }
            EnumChildWindows(root, (hwnd, state) =>
            {
                string title = WindowTitle(hwnd);
                string cls = WindowClass(hwnd);
                if (rows.Count < 16) rows.Add("0x" + hwnd.ToInt64().ToString("X") + ":" + cls + ":'" + Clean(title) + "'");
                if (found == IntPtr.Zero && title.IndexOf("GAME START", StringComparison.OrdinalIgnoreCase) >= 0)
                    found = hwnd;
                return true;
            }, IntPtr.Zero);
            control = found;
            inventory = rows.Count == 0 ? "none" : string.Join(" | ", rows);
            return found != IntPtr.Zero;
        }

        private static IntPtr GetMainWindow(int processId)
        {
            using (var process = Process.GetProcessById(processId))
            {
                process.Refresh();
                return process.MainWindowHandle;
            }
        }

        private static void SaveDebugBitmap(string debugDirectory, string fileName, Bitmap bitmap)
        {
            if (string.IsNullOrWhiteSpace(debugDirectory) || bitmap == null) return;
            try
            {
                Directory.CreateDirectory(debugDirectory);
                bitmap.Save(Path.Combine(debugDirectory, fileName), ImageFormat.Png);
            }
            catch { }
        }

        private static string DebugCaptureSuffix(string debugDirectory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(debugDirectory)) return "";
            return "; capture='" + Path.Combine(debugDirectory, fileName) + "'";
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
            var candidates = GetLauncherCandidates(launched, launcherName);
            try
            {
                Process best = null;
                DateTime bestStart = DateTime.MinValue;
                foreach (var process in candidates.Values)
                {
                    try
                    {
                        process.Refresh();
                        if (process.MainWindowHandle == IntPtr.Zero || !IsWindowVisible(process.MainWindowHandle)) continue;
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

        private static Dictionary<int, Process> GetLauncherCandidates(Process launched, string launcherName)
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
            return candidates;
        }

        private static string DescribeLauncherCandidates(Process launched, string launcherName, string launcherDirectory)
        {
            var candidates = GetLauncherCandidates(launched, launcherName);
            try
            {
                if (candidates.Count == 0) return "candidateProcesses=none";
                var rows = new List<string>();
                foreach (var process in candidates.Values)
                {
                    try
                    {
                        process.Refresh();
                        string path = "?";
                        try { path = process.MainModule.FileName; } catch { }
                        rows.Add("PID=" + process.Id + " exited=" + process.HasExited + " hwnd=" + DescribeWindow(process.MainWindowHandle)
                            + " path='" + path + "' expectedDir='" + launcherDirectory + "'");
                    }
                    catch (Exception ex) { rows.Add("PID=" + process.Id + " inspectError=" + ex.Message); }
                }
                return "candidates=[" + string.Join(" || ", rows) + "]";
            }
            finally { foreach (var process in candidates.Values) process.Dispose(); }
        }

        private static string DescribeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "none";
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return "0x" + hwnd.ToInt64().ToString("X") + " pid=" + pid + " visible=" + IsWindowVisible(hwnd)
                + " class='" + Clean(WindowClass(hwnd)) + "' title='" + Clean(WindowTitle(hwnd)) + "'";
        }

        private static string WindowTitle(IntPtr hwnd)
        {
            var value = new StringBuilder(512);
            try { GetWindowText(hwnd, value, value.Capacity); } catch { }
            return value.ToString();
        }

        private static string WindowClass(IntPtr hwnd)
        {
            var value = new StringBuilder(256);
            try { GetClassName(hwnd, value, value.Capacity); } catch { }
            return value.ToString();
        }

        private static string Clean(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}

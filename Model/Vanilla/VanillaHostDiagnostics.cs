using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace _4RTools.Model.Vanilla
{
    internal static class VanillaHostDiagnostics
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);

        internal static string Build()
        {
            var text = new StringBuilder(8192);
            text.AppendLine("=== HOST / DISPLAY / SESSION DIAGNOSTICS ===");
            text.AppendLine("Machine=" + Safe(Environment.MachineName)
                + "; User=" + Safe(Environment.UserDomainName) + "\\" + Safe(Environment.UserName));
            text.AppendLine("OS=" + Environment.OSVersion + RegistryWindowsVersion());
            text.AppendLine("Process architecture=" + (Environment.Is64BitProcess ? "x64" : "x86")
                + "; OS architecture=" + (Environment.Is64BitOperatingSystem ? "x64" : "x86")
                + "; processors=" + Environment.ProcessorCount
                + "; CLR=" + Environment.Version);
            text.AppendLine("Culture=" + CultureInfo.CurrentCulture.Name
                + "; UI culture=" + CultureInfo.CurrentUICulture.Name
                + "; timezone=" + TimeZoneInfo.Local.Id);

            try
            {
                using (var current = Process.GetCurrentProcess())
                {
                    text.AppendLine("Session=processId " + current.Id
                        + "; sessionId=" + current.SessionId
                        + "; terminalServer=" + SystemInformation.TerminalServerSession
                        + "; SESSIONNAME=" + Safe(Environment.GetEnvironmentVariable("SESSIONNAME"))
                        + "; interactive=" + Environment.UserInteractive);
                }
            }
            catch (Exception ex) { text.AppendLine("Session diagnostics failed: " + ex.Message); }

            try
            {
                Rectangle virtualScreen = SystemInformation.VirtualScreen;
                Size primary = SystemInformation.PrimaryMonitorSize;
                float dpiX = 0, dpiY = 0;
                using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                {
                    dpiX = graphics.DpiX;
                    dpiY = graphics.DpiY;
                }
                text.AppendLine("Desktop=virtual " + Rect(virtualScreen)
                    + "; primary=" + primary.Width + "x" + primary.Height
                    + "; systemDpi=" + dpiX.ToString("0.##", CultureInfo.InvariantCulture)
                    + "x" + dpiY.ToString("0.##", CultureInfo.InvariantCulture));
                Screen[] screens = Screen.AllScreens;
                text.AppendLine("Screens=" + screens.Length);
                for (int i = 0; i < screens.Length; i++)
                {
                    Screen screen = screens[i];
                    text.AppendLine("Screen[" + i + "] device='" + Safe(screen.DeviceName)
                        + "'; primary=" + screen.Primary
                        + "; bounds=" + Rect(screen.Bounds)
                        + "; working=" + Rect(screen.WorkingArea)
                        + "; bpp=" + screen.BitsPerPixel);
                }
            }
            catch (Exception ex) { text.AppendLine("Display diagnostics failed: " + ex.Message); }

            Process[] vanilla = new Process[0];
            try
            {
                vanilla = Process.GetProcessesByName("Vanilla MMO");
                Array.Sort(vanilla, (a, b) => a.Id.CompareTo(b.Id));
                text.AppendLine("Vanilla processes=" + vanilla.Length);
                foreach (Process process in vanilla)
                {
                    try
                    {
                        process.Refresh();
                        string path = "unavailable";
                        try { path = process.MainModule == null ? "unavailable" : process.MainModule.FileName; }
                        catch (Exception ex) { path = "unavailable (" + ex.GetType().Name + ": " + ex.Message + ")"; }
                        string started = "unavailable";
                        try { started = process.StartTime.ToUniversalTime().ToString("O"); } catch { }
                        text.AppendLine("Vanilla PID=" + process.Id
                            + "; session=" + SafeSession(process)
                            + "; startedUtc=" + started
                            + "; mainWindow=" + Hex(process.MainWindowHandle)
                            + "; path='" + Safe(path) + "'.");
                        foreach (string window in WindowsForProcess(process.Id)) text.AppendLine("  " + window);
                    }
                    catch (Exception ex)
                    {
                        text.AppendLine("Vanilla PID=" + process.Id + " diagnostics failed: " + ex);
                    }
                }
            }
            catch (Exception ex) { text.AppendLine("Vanilla process enumeration failed: " + ex); }
            finally { foreach (Process process in vanilla) try { process.Dispose(); } catch { } }

            return text.ToString();
        }

        private static IEnumerable<string> WindowsForProcess(int processId)
        {
            var windows = new List<string>();
            try
            {
                EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
                {
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (pid != (uint)processId) return true;
                    RECT rect;
                    int width = 0, height = 0;
                    if (GetClientRect(hwnd, out rect))
                    {
                        width = Math.Max(0, rect.Right - rect.Left);
                        height = Math.Max(0, rect.Bottom - rect.Top);
                    }
                    windows.Add("window=" + Hex(hwnd)
                        + "; visible=" + IsWindowVisible(hwnd)
                        + "; iconic=" + IsIconic(hwnd)
                        + "; client=" + width + "x" + height
                        + "; class='" + Safe(WindowClass(hwnd))
                        + "'; title='" + Safe(WindowTitle(hwnd)) + "'.");
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { windows.Add("window enumeration failed: " + ex.Message); }
            if (windows.Count == 0) windows.Add("no top-level windows enumerated for PID " + processId + ".");
            return windows;
        }

        private static string RegistryWindowsVersion()
        {
            try
            {
                RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
                using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key == null) return "; registryVersion=unavailable";
                    string product = Convert.ToString(key.GetValue("ProductName"), CultureInfo.InvariantCulture);
                    string display = Convert.ToString(key.GetValue("DisplayVersion"), CultureInfo.InvariantCulture);
                    string release = Convert.ToString(key.GetValue("ReleaseId"), CultureInfo.InvariantCulture);
                    string build = Convert.ToString(key.GetValue("CurrentBuildNumber"), CultureInfo.InvariantCulture);
                    string ubr = Convert.ToString(key.GetValue("UBR"), CultureInfo.InvariantCulture);
                    return "; registryProduct='" + Safe(product) + "'; displayVersion='" + Safe(display)
                        + "'; releaseId='" + Safe(release) + "'; build=" + Safe(build) + "." + Safe(ubr);
                }
            }
            catch (Exception ex) { return "; registryVersion=unavailable (" + ex.GetType().Name + ": " + ex.Message + ")"; }
        }

        private static string SafeSession(Process process)
        {
            try { return process.SessionId.ToString(CultureInfo.InvariantCulture); }
            catch { return "unavailable"; }
        }

        private static string WindowTitle(IntPtr hwnd)
        {
            var text = new StringBuilder(512);
            try { GetWindowText(hwnd, text, text.Capacity); } catch { }
            return text.ToString();
        }

        private static string WindowClass(IntPtr hwnd)
        {
            var text = new StringBuilder(256);
            try { GetClassName(hwnd, text, text.Capacity); } catch { }
            return text.ToString();
        }

        private static string Rect(Rectangle value)
        {
            return value.X + "," + value.Y + " " + value.Width + "x" + value.Height;
        }

        private static string Hex(IntPtr value)
        {
            return value == IntPtr.Zero ? "none" : "0x" + value.ToInt64().ToString("X", CultureInfo.InvariantCulture);
        }

        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Replace("\r", " ").Replace("\n", " ").Replace("'", "''").Trim();
        }
    }
}

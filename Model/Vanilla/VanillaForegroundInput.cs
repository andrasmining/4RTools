using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Ordinary Windows foreground input for reconnect/login diagnostics.
    /// This does not inject into Vanilla/Gepard or modify game memory.
    /// </summary>
    internal sealed class VanillaForegroundInput : IDisposable
    {
        private readonly Process process;
        private readonly IntPtr preferredWindow;
        private IntPtr window;

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public INPUTUNION U; }
        [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public UIntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public UIntPtr dwExtraInfo;
        }

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

        public VanillaForegroundInput(int processId) : this(processId, IntPtr.Zero) { }

        public VanillaForegroundInput(int processId, IntPtr preferredWindow)
        {
            process = Process.GetProcessById(processId);
            this.preferredWindow = preferredWindow;
            RefreshWindow();
        }

        public void Activate()
        {
            RefreshWindow();
            for (int attempt = 0; attempt < 5; attempt++)
            {
                ShowWindow(window, 9);
                BringWindowToTop(window);
                SetForegroundWindow(window);
                Thread.Sleep(90);
                if (GetForegroundWindow() == window) return;
            }
            throw new InvalidOperationException("Windows did not give focus to the selected Vanilla window. No input was sent.");
        }

        public void ClickNormalized(double x, double y, bool requireForeground = true)
        {
            ClickNormalizedWithDiagnostics(x, y, requireForeground);
        }

        public string ClickNormalizedWithDiagnostics(double x, double y, bool requireForeground = true)
        {
            RefreshWindow();
            IntPtr foregroundBefore = GetForegroundWindow();
            if (requireForeground)
            {
                Activate();
            }
            else
            {
                // SetForegroundWindow is intentionally best-effort for the launcher. Windows can
                // deny focus when 4RTools was not the most recent input owner, while a normal
                // screen-coordinate mouse click can still be delivered to the visible launcher.
                ShowWindow(window, 9);
                BringWindowToTop(window);
                SetForegroundWindow(window);
                Thread.Sleep(120);
            }
            if (x < 0 || x > 1 || y < 0 || y > 1) throw new ArgumentOutOfRangeException("Normalized coordinates must be within 0..1.");

            RECT rect;
            if (!GetClientRect(window, out rect)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read Vanilla client area.");
            int width = Math.Max(1, rect.Right - rect.Left), height = Math.Max(1, rect.Bottom - rect.Top);
            var origin = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(window, ref origin)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot map Vanilla client origin.");
            var target = new POINT
            {
                X = Math.Max(0, Math.Min(width - 1, (int)Math.Round(x * width))),
                Y = Math.Max(0, Math.Min(height - 1, (int)Math.Round(y * height)))
            };
            var clientTarget = target;
            if (!ClientToScreen(window, ref target)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot map Vanilla client coordinate.");

            POINT previous;
            bool restore = GetCursorPos(out previous);
            IntPtr hitBeforeMove = WindowFromPoint(target);
            if (!SetCursorPos(target.X, target.Y)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the mouse position.");
            Thread.Sleep(120);
            POINT actual;
            GetCursorPos(out actual);
            IntPtr hitAtClick = WindowFromPoint(actual);

            var down = new[] { new INPUT { type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } } };
            uint downSent = SendInput(1, down, Marshal.SizeOf(typeof(INPUT)));
            int downError = downSent == 1 ? 0 : Marshal.GetLastWin32Error();
            Thread.Sleep(110);
            var up = new[] { new INPUT { type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } } };
            uint upSent = SendInput(1, up, Marshal.SizeOf(typeof(INPUT)));
            int upError = upSent == 1 ? 0 : Marshal.GetLastWin32Error();
            Thread.Sleep(130);

            IntPtr foregroundAfter = GetForegroundWindow();
            string diagnostics = string.Format(
                "main={0}; visible={1}; dpi={2}; client={3}x{4}; origin=({5},{6}); targetClient=({7},{8}); targetScreen=({9},{10}); cursorActual=({11},{12}); foregroundBefore={13}; foregroundAfter={14}; hitBefore={15}; hitAtClick={16}; SendInputDown={17}/1 err={18}; SendInputUp={19}/1 err={20}",
                DescribeWindow(window), IsWindowVisible(window), SafeDpi(window), width, height,
                origin.X, origin.Y, clientTarget.X, clientTarget.Y, target.X, target.Y, actual.X, actual.Y,
                DescribeWindow(foregroundBefore), DescribeWindow(foregroundAfter), DescribeWindow(hitBeforeMove), DescribeWindow(hitAtClick),
                downSent, downError, upSent, upError);

            if (restore) SetCursorPos(previous.X, previous.Y);
            if (downSent != 1 || upSent != 1)
                throw new Win32Exception(downError != 0 ? downError : upError, "Windows SendInput did not send the complete mouse click. " + diagnostics);
            return diagnostics;
        }

        internal Bitmap CaptureClientBitmap()
        {
            Activate();
            RECT rect;
            if (!GetClientRect(window, out rect)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read Vanilla client area for visual recognition.");
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width < 200 || height < 120) throw new InvalidOperationException("Vanilla client area is too small for visual recognition: " + width + "x" + height);
            var origin = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(window, ref origin)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot map Vanilla client for visual recognition.");
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(width, height));
            return bitmap;
        }
        public void Press(Keys key)
        {
            Activate();
            SendKey(key, false);
            Thread.Sleep(70);
            SendKey(key, true);
            Thread.Sleep(45);
        }

        public void Chord(bool ctrl, bool alt, bool shift, Keys key)
        {
            Activate();
            if (ctrl) SendKey(Keys.ControlKey, false);
            if (alt) SendKey(Keys.Menu, false);
            if (shift) SendKey(Keys.ShiftKey, false);
            Thread.Sleep(80);
            SendKey(key, false);
            Thread.Sleep(85);
            SendKey(key, true);
            Thread.Sleep(60);
            if (shift) SendKey(Keys.ShiftKey, true);
            if (alt) SendKey(Keys.Menu, true);
            if (ctrl) SendKey(Keys.ControlKey, true);
            Thread.Sleep(90);
        }

        public void SelectAll() { Chord(true, false, false, Keys.A); }

        public void ReplaceFocusedText(string text)
        {
            SelectAll();
            Press(Keys.Back);
            TypeText(text ?? string.Empty);
        }

        public void TypeText(string text)
        {
            if (text == null) return;
            Activate();
            foreach (char c in text)
            {
                SendUnicode(c, false);
                SendUnicode(c, true);
                Thread.Sleep(22);
            }
            Thread.Sleep(80);
        }

        private void SendKey(Keys key, bool up)
        {
            uint scan = MapVirtualKey((uint)key, 0);
            Send(new[]
            {
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new INPUTUNION
                    {
                        ki = new KEYBDINPUT { wVk = 0, wScan = (ushort)scan, dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0) }
                    }
                }
            });
        }

        private void SendUnicode(char c, bool up)
        {
            Send(new[]
            {
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new INPUTUNION
                    {
                        ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) }
                    }
                }
            });
        }

        private static void Send(INPUT[] inputs)
        {
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent != inputs.Length) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows SendInput did not send the complete input sequence.");
        }

        private void RefreshWindow()
        {
            if (process.HasExited) throw new InvalidOperationException("Vanilla client exited.");
            process.Refresh();
            window = preferredWindow != IntPtr.Zero && IsWindow(preferredWindow) ? preferredWindow : process.MainWindowHandle;
            if (window == IntPtr.Zero || !IsWindow(window)) throw new InvalidOperationException("Vanilla client window is not ready.");
        }

        private static uint SafeDpi(IntPtr hwnd)
        {
            try { return hwnd == IntPtr.Zero ? 0 : GetDpiForWindow(hwnd); }
            catch { return 0; }
        }

        private static string DescribeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "none";
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            var title = new StringBuilder(256);
            var cls = new StringBuilder(128);
            try { GetWindowText(hwnd, title, title.Capacity); } catch { }
            try { GetClassName(hwnd, cls, cls.Capacity); } catch { }
            return string.Format("0x{0:X} pid={1} class='{2}' title='{3}'", hwnd.ToInt64(), pid, Clean(cls.ToString()), Clean(title.ToString()));
        }

        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        public void Dispose() { process.Dispose(); }
    }
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
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

        public VanillaForegroundInput(int processId)
        {
            process = Process.GetProcessById(processId);
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
            RefreshWindow();
            if (requireForeground)
            {
                Activate();
            }
            else
            {
                // SetForegroundWindow is intentionally best-effort here. Windows may deny focus
                // when 4RTools did not most recently receive user input, even though the launcher
                // is already visible. A mouse click only needs the launcher restored and raised;
                // the real screen-coordinate click below can then reach GAME START without
                // requiring keyboard focus.
                ShowWindow(window, 9);
                BringWindowToTop(window);
                SetForegroundWindow(window);
                Thread.Sleep(120);
            }
            if (x < 0 || x > 1 || y < 0 || y > 1) throw new ArgumentOutOfRangeException("Normalized coordinates must be within 0..1.");
            RECT rect;
            if (!GetClientRect(window, out rect)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read Vanilla client area.");
            int width = Math.Max(1, rect.Right - rect.Left), height = Math.Max(1, rect.Bottom - rect.Top);
            var target = new POINT
            {
                X = Math.Max(0, Math.Min(width - 1, (int)Math.Round(x * width))),
                Y = Math.Max(0, Math.Min(height - 1, (int)Math.Round(y * height)))
            };
            if (!ClientToScreen(window, ref target)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot map Vanilla client coordinate.");
            POINT previous;
            bool restore = GetCursorPos(out previous);
            if (!SetCursorPos(target.X, target.Y)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the mouse position.");
            Thread.Sleep(120);
            Send(new[]
            {
                new INPUT { type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } }
            });
            Thread.Sleep(110);
            Send(new[]
            {
                new INPUT { type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } }
            });
            Thread.Sleep(130);
            if (restore) SetCursorPos(previous.X, previous.Y);
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
            window = process.MainWindowHandle;
            if (window == IntPtr.Zero || !IsWindow(window)) throw new InvalidOperationException("Vanilla client window is not ready.");
        }

        public void Dispose() { process.Dispose(); }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using _4RTools.Model.Vanilla.Automation;

namespace _4RTools.Utils
{
    /// <summary>Ordinary window messages to one selected window. Never uses global input or memory writes.</summary>
    public sealed class VanillaWindowInput : IInputSink, IDisposable
    {
        private readonly Process process;
        private readonly IReadOnlyProcessMemory memory;
        private readonly IntPtr window;
        private readonly HashSet<int> heldKeys = new HashSet<int>();
        private readonly Func<bool> permitted;
        private bool mouseHeld, disposed;
        private string failure;
        private IntPtr lastClick;

        public VanillaWindowInput(IReadOnlyProcessMemory memory, Func<bool> permitted)
        {
            this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
            this.permitted = permitted ?? throw new ArgumentNullException(nameof(permitted));
            process = Process.GetProcessById(memory.ProcessId);
            try
            {
                // Reuse the existing read-only source handle; do not request broader process rights.
                memory.EnsureAlive();
                if (!string.Equals(process.ProcessName, "Vanilla MMO", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The selected input target is not Vanilla MMO.");
                window = process.MainWindowHandle;
                CheckWindow();
            }
            catch { process.Dispose(); throw; }
        }

        public void Send(SequenceStep step)
        {
            if (disposed) throw new ObjectDisposedException(nameof(VanillaWindowInput));
            if (failure != null) throw new InvalidOperationException(failure);
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (!permitted()) throw new InvalidOperationException("Input stopped: the selected observation is no longer valid.");
            CheckWindow();
            if (step.Kind == SequenceStepKind.Wait) return;
            if (step.Kind == SequenceStepKind.Click)
            {
                RECT bounds;
                if (!GetClientRect(window, out bounds)) throw NativeFailure("GetClientRect", Marshal.GetLastWin32Error());
                if (step.X < 0 || step.Y < 0 || step.X >= bounds.Right || step.Y >= bounds.Bottom || step.X > 32767 || step.Y > 32767)
                    throw new InvalidOperationException("Click falls outside the selected client window.");
                lastClick = new IntPtr((step.Y << 16) | step.X);
                Post(0x0200, IntPtr.Zero, lastClick);
                Post(0x0201, new IntPtr(1), lastClick);
                mouseHeld = true;
                Post(0x0202, IntPtr.Zero, lastClick);
                mouseHeld = false;
                return;
            }
            if (step.Key < 8 || step.Key > 254) throw new ArgumentException("Invalid virtual key.");
            if (step.Kind == SequenceStepKind.PressKey || step.Kind == SequenceStepKind.KeyDown)
            {
                if (heldKeys.Contains(step.Key)) throw new InvalidOperationException("Key already held.");
                Key(step.Key, false);
                heldKeys.Add(step.Key);
            }
            if (step.Kind == SequenceStepKind.PressKey || step.Kind == SequenceStepKind.KeyUp)
            {
                if (!heldKeys.Contains(step.Key)) throw new InvalidOperationException("Key up has no matching key down.");
                Key(step.Key, true);
                heldKeys.Remove(step.Key);
            }
        }

        public void ReleaseAll()
        {
            // Cancellation may release only this sink's held input, to this same live window.
            // No fallback is attempted if the window disappeared or Windows rejected a message.
            if (heldKeys.Count == 0 && !mouseHeld) return;
            try
            {
                CheckWindow();
                foreach (int key in heldKeys) Key(key, true);
                if (mouseHeld) Post(0x0202, IntPtr.Zero, lastClick);
            }
            finally { heldKeys.Clear(); mouseHeld = false; }
        }

        private void Key(int key, bool up)
        {
            uint scan = MapVirtualKey((uint)key, 4);
            uint flags = 1U | ((scan & 0xFF) << 16);
            if ((scan & 0xFF00) != 0) flags |= 1U << 24;
            if (up) flags |= 0xC0000000U;
            Post(up ? 0x0101U : 0x0100U, new IntPtr(key), new IntPtr(unchecked((int)flags)));
        }

        private void Post(uint message, IntPtr key, IntPtr data)
        {
            CheckWindow();
            if (!PostMessage(window, message, key, data))
            {
                throw NativeFailure("PostMessage (0x" + message.ToString("X") + ")", Marshal.GetLastWin32Error());
            }
        }

        private Win32Exception NativeFailure(string operation, int code)
        {
            failure = operation + " failed with Win32 error " + code + ": " + new Win32Exception(code).Message + ". Input stopped; no retry.";
            return new Win32Exception(code, failure);
        }

        private void CheckWindow()
        {
            if (failure != null) throw new InvalidOperationException(failure);
            try
            {
                uint owner;
                memory.EnsureAlive();
                if (window == IntPtr.Zero || !IsWindow(window)
                    || GetWindowThreadProcessId(window, out owner) == 0 || owner != process.Id)
                    throw new InvalidOperationException("The original Vanilla process/window is no longer available.");
                process.Refresh();
                if (process.MainWindowHandle != window)
                    throw new InvalidOperationException("The Vanilla window changed; reconnect explicitly.");
            }
            catch (Exception ex) { failure = ex.Message; throw; }
        }

        public void Dispose()
        {
            if (disposed) return;
            try { ReleaseAll(); }
            finally { disposed = true; process.Dispose(); }
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr window, out RECT rectangle);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    }
}

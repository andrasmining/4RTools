using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    internal sealed partial class VanillaReconnectForm
    {
        private bool applyingEmbeddedBounds;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);

        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (applyingEmbeddedBounds) return;
            base.SetBoundsCore(x, y, width, height, specified);
            if (TopLevel || !IsHandleCreated || Parent == null || Dock != DockStyle.Fill
                || FormBorderStyle != FormBorderStyle.None || width <= 0 || height <= 0) return;
            if (Width == width && Height == height) return;

            // Framework Form.SetBoundsCore unconditionally caps normal forms at the desktop's
            // MaxWindowTrackSize, including non-top-level forms used as controls. The parent
            // has already provided valid Dock=Fill bounds. Apply those to this application's
            // own child HWND so RDP/multi-monitor viewport changes cannot leave a narrow pane.
            // No other process/window is touched and activation/z-order are unchanged.
            applyingEmbeddedBounds = true;
            try
            {
                if (!SetWindowPos(Handle, IntPtr.Zero, x, y, width, height, 0x0014))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resize the embedded recovery workspace.");
            }
            finally { applyingEmbeddedBounds = false; }
        }
    }
}

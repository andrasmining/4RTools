using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace _4RTools.Utils
{
    internal class Interop
    {
        // PINVOKES
        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        private static extern bool PostMessageNative(IntPtr hWnd, int Msg, Keys wParam, int lParam);

        public static bool PostMessage(Model.Client client, IntPtr hWnd, int message, Keys key, int data, bool automatic = false)
        {
            if (client == null) throw new InvalidOperationException("No client is selected for this action.");
            return client.SendWindowMessage(hWnd, message, key, data, automatic);
        }

        public static bool PostMessage(IntPtr hWnd, int message, Keys key, int data)
        {
            var client = Model.ClientSingleton.GetClient();
            return client != null && client.IsVanilla ? client.SendWindowMessage(hWnd, message, key, data)
                : PostLegacyMessage(hWnd, message, key, data);
        }

        internal static bool PostLegacyMessage(IntPtr hWnd, int message, Keys key, int data)
        {
            return PostMessageNative(hWnd, message, key, data);
        }

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);
    }
}

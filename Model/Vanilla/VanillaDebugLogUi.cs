using System;
using System.Drawing;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    internal sealed partial class VanillaReconnectForm
    {
        private bool fullDebugLogUiInstalled;

        internal void InstallFullDebugLogUi()
        {
            if (fullDebugLogUiInstalled || IsDisposed) return;
            Button reconnectCopy = FindButton(this, "COPY LOG");
            if (reconnectCopy == null || reconnectCopy.Parent == null) return;
            fullDebugLogUiInstalled = true;

            reconnectCopy.Text = "COPY RECONNECT LOG";
            help.SetToolTip(reconnectCopy, "Copy only the current reconnect/startup session log part.");

            var full = new Button
            {
                Text = "COPY FULL DEBUG LOG",
                AutoSize = true,
                Margin = new Padding(4)
            };
            full.Click += (s, e) => CopyFullDebugLog();
            reconnectCopy.Parent.Controls.Add(full);
            reconnectCopy.Parent.Controls.SetChildIndex(full,
                Math.Min(reconnectCopy.Parent.Controls.Count - 1, reconnectCopy.Parent.Controls.GetChildIndex(reconnectCopy) + 1));
            help.SetToolTip(full,
                "Copy one diagnostic bundle containing the memory-access debug log, all parts of this reconnect session, the Vanilla core log and update-error log. Passwords are never included.");

            var state = new Label
            {
                Text = "DEBUG LOG ON",
                AutoSize = true,
                ForeColor = Color.DarkGreen,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(12, 8, 0, 0)
            };
            reconnectCopy.Parent.Controls.Add(state);
            help.SetToolTip(state,
                "Always-on diagnostic logging is active for process access. It records process-access results, executable hashes and related protection-file metadata, but never passwords.");
        }

        private void CopyFullDebugLog()
        {
            try
            {
                string contents = VanillaMemoryAccessDiagnostics.BuildClipboardBundle(supervisor.LogPath);
                Clipboard.SetText(string.IsNullOrWhiteSpace(contents) ? "(full debug log is empty)" : contents);
                testState.Text = "Full debug log copied to clipboard.";
                testState.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Copy full debug log", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static Button FindButton(Control root, string text)
        {
            foreach (Control child in root.Controls)
            {
                var button = child as Button;
                if (button != null && string.Equals(button.Text, text, StringComparison.Ordinal)) return button;
                if (child.HasChildren)
                {
                    Button nested = FindButton(child, text);
                    if (nested != null) return nested;
                }
            }
            return null;
        }
    }
}

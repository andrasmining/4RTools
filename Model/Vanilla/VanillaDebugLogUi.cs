using System;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    internal sealed partial class VanillaReconnectForm
    {
        // Shared recursive lookup retained for runtime UI composition. Debug controls now live
        // exclusively in the top-level Vanilla header; there is no recovery-local log/copy UI.
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

using System.Drawing;
using System.Windows.Forms;

namespace _4RTools.Forms
{
    public partial class Container
    {
        private bool vanillaFleetSizingApplied;

        private void ApplyVanillaFleetSizing()
        {
            if (vanillaFleetSizingApplied || integratedFleetDashboard == null) return;
            vanillaFleetSizingApplied = true;

            const int dashboardHeight = 225;
            integratedFleetDashboard.MinimumSize = new Size(600, dashboardHeight);
            integratedFleetDashboard.Height = dashboardHeight;

            var host = integratedFleetDashboard.Parent as TableLayoutPanel;
            if (host != null && host.RowStyles.Count > 1)
            {
                host.RowStyles[1].SizeType = SizeType.Absolute;
                host.RowStyles[1].Height = dashboardHeight;
            }

            ConfigureFleetLabels(integratedFleetDashboard);
        }

        private static void ConfigureFleetLabels(Control root)
        {
            foreach (Control child in root.Controls)
            {
                var layout = child as TableLayoutPanel;
                if (layout != null && layout.Parent is GroupBox && layout.RowCount == 5)
                {
                    while (layout.RowStyles.Count < 5) layout.RowStyles.Add(new RowStyle());
                    layout.RowStyles[0].SizeType = SizeType.AutoSize;
                    layout.RowStyles[1].SizeType = SizeType.AutoSize;
                    layout.RowStyles[2].SizeType = SizeType.Absolute;
                    layout.RowStyles[2].Height = 10;
                    layout.RowStyles[3].SizeType = SizeType.Absolute;
                    layout.RowStyles[3].Height = 26;
                    layout.RowStyles[4].SizeType = SizeType.Percent;
                    layout.RowStyles[4].Height = 100;

                    foreach (Control item in layout.Controls)
                    {
                        var label = item as Label;
                        if (label == null) continue;
                        int row = layout.GetRow(label);
                        if (row != 3 && row != 4) continue;
                        label.AutoSize = false;
                        label.Dock = DockStyle.Fill;
                        label.AutoEllipsis = false;
                        label.TextAlign = ContentAlignment.TopLeft;
                    }
                }
                if (child.HasChildren) ConfigureFleetLabels(child);
            }
        }
    }
}

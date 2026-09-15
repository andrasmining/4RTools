using System.Drawing;
using System.Windows.Forms;
using _4RTools.Model.Vanilla;

namespace _4RTools.Forms
{
    public partial class Container
    {
        private bool vanillaFleetSizingApplied;
        private System.Windows.Forms.Timer vanillaMemoryAccessDiagnosticTimer;

        private void ApplyVanillaFleetSizing()
        {
            if (vanillaFleetSizingApplied || integratedFleetDashboard == null) return;
            vanillaFleetSizingApplied = true;

            UpdateVanillaFleetHeight();
            SizeChanged += (s, e) => UpdateVanillaFleetHeight();
            ConfigureFleetLabels(integratedFleetDashboard);
            StartMemoryAccessDiagnostics();
        }

        internal static int PreferredFleetDashboardHeight(int availableClientHeight)
        {
            if (availableClientHeight < 820) return 128;
            if (availableClientHeight < 950) return 138;
            return 145;
        }

        private void UpdateVanillaFleetHeight()
        {
            if (integratedFleetDashboard == null || integratedFleetDashboard.IsDisposed) return;
            int dashboardHeight = PreferredFleetDashboardHeight(ClientSize.Height);
            integratedFleetDashboard.MinimumSize = new Size(600, dashboardHeight);
            integratedFleetDashboard.Height = dashboardHeight;

            var host = integratedFleetDashboard.Parent as TableLayoutPanel;
            if (host != null && host.RowStyles.Count > 1)
            {
                host.RowStyles[1].SizeType = SizeType.Absolute;
                host.RowStyles[1].Height = dashboardHeight;
            }
        }

        private void StartMemoryAccessDiagnostics()
        {
            if (smokeTest || vanillaMemoryAccessDiagnosticTimer != null) return;
            vanillaMemoryAccessDiagnosticTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            vanillaMemoryAccessDiagnosticTimer.Tick += (s, e) => QueueMemoryAccessDiagnostics();
            vanillaMemoryAccessDiagnosticTimer.Start();
            QueueMemoryAccessDiagnostics();
            FormClosed += (s, e) =>
            {
                try { vanillaMemoryAccessDiagnosticTimer?.Stop(); } catch { }
                try { vanillaMemoryAccessDiagnosticTimer?.Dispose(); } catch { }
                vanillaMemoryAccessDiagnosticTimer = null;
            };
        }

        private static void QueueMemoryAccessDiagnostics()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                VanillaMemoryAccessDiagnostics.CaptureCurrentClients();
                VanillaTargetSecurityDiagnostics.CaptureCurrentClients();
            });
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
                    layout.RowStyles[2].Height = 8;
                    layout.RowStyles[3].SizeType = SizeType.Absolute;
                    layout.RowStyles[3].Height = 22;
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
                        label.AutoEllipsis = true;
                        label.TextAlign = ContentAlignment.TopLeft;
                    }
                }
                if (child.HasChildren) ConfigureFleetLabels(child);
            }
        }
    }
}

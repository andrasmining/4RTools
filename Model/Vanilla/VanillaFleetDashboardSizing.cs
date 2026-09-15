using System;
using System.Drawing;
using System.Windows.Forms;
using _4RTools.Model.Vanilla;

namespace _4RTools.Forms
{
    public partial class Container
    {
        private bool vanillaFleetSizingApplied;
        private System.Windows.Forms.Timer vanillaMemoryAccessDiagnosticTimer;
        private readonly ToolTip vanillaFleetHelp = new ToolTip { ShowAlways = true, AutoPopDelay = 30000 };

        private void ApplyVanillaFleetSizing()
        {
            if (vanillaFleetSizingApplied || integratedFleetDashboard == null) return;
            vanillaFleetSizingApplied = true;

            UpdateVanillaFleetHeight();
            SizeChanged += (s, e) =>
            {
                UpdateVanillaFleetHeight();
                CompactIntegratedHeader();
            };
            ConfigureFleetPresentation(integratedFleetDashboard);
            StartMemoryAccessDiagnostics();
        }

        internal static int PreferredFleetDashboardHeight(int availableClientHeight)
        {
            if (availableClientHeight < 760) return 96;
            if (availableClientHeight < 900) return 104;
            if (availableClientHeight < 1050) return 112;
            return 120;
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

        private void CompactIntegratedHeader()
        {
            if (integratedFleetDashboard == null || integratedFleetDashboard.IsDisposed) return;
            var host = integratedFleetDashboard.Parent as TableLayoutPanel;
            if (host == null || host.RowStyles.Count == 0) return;
            Control header = host.GetControlFromPosition(0, 0);
            if (header == null) return;

            int availableWidth = Math.Max(400, host.ClientSize.Width - host.Padding.Horizontal);
            int preferred = 30;
            foreach (Control child in header.Controls)
            {
                if (!child.Visible) continue;
                Size wanted = child.GetPreferredSize(new Size(availableWidth, 0));
                preferred = Math.Max(preferred, wanted.Height + child.Margin.Vertical + header.Padding.Vertical);
            }
            int maximum = availableWidth < 1100 ? 68 : 42;
            int height = Math.Max(32, Math.Min(maximum, preferred));

            header.AutoSize = false;
            header.Height = height;
            host.RowStyles[0].SizeType = SizeType.Absolute;
            host.RowStyles[0].Height = height;
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
                try { vanillaFleetHelp.Dispose(); } catch { }
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

        private void ConfigureFleetPresentation(Control root)
        {
            foreach (Control child in root.Controls)
            {
                var layout = child as TableLayoutPanel;
                if (layout != null && layout.RowCount == 2 && layout.ColumnCount == 2)
                {
                    foreach (Control item in layout.Controls)
                    {
                        if (layout.GetRow(item) != 1) continue;
                        Label helper = item as Label;
                        if (helper == null) continue;
                        helper.Visible = false;
                        helper.Margin = Padding.Empty;
                        vanillaFleetHelp.SetToolTip(root,
                            "The two cards show read-only values from the verified Vanilla build profile. Location and activity appear when their mappings are available. Observation errors are available on the affected client card.");
                    }
                    while (layout.RowStyles.Count < 2) layout.RowStyles.Add(new RowStyle());
                    layout.RowStyles[1].SizeType = SizeType.Absolute;
                    layout.RowStyles[1].Height = 0;
                }

                if (layout != null && layout.Parent is GroupBox && layout.RowCount == 5)
                {
                    while (layout.RowStyles.Count < 5) layout.RowStyles.Add(new RowStyle());
                    layout.RowStyles[0].SizeType = SizeType.AutoSize;
                    layout.RowStyles[1].SizeType = SizeType.AutoSize;
                    layout.RowStyles[2].SizeType = SizeType.Absolute;
                    layout.RowStyles[2].Height = 7;
                    layout.RowStyles[3].SizeType = SizeType.Absolute;
                    layout.RowStyles[3].Height = 20;
                    layout.RowStyles[4].SizeType = SizeType.Percent;
                    layout.RowStyles[4].Height = 100;

                    GroupBox card = layout.Parent as GroupBox;
                    if (card != null)
                    {
                        card.Padding = new Padding(7);
                        card.Margin = new Padding(3);
                        vanillaFleetHelp.SetToolTip(card,
                            "Read-only Vanilla client status. Hover the activity/error line for detailed observation errors.");
                    }

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
                if (child.HasChildren) ConfigureFleetPresentation(child);
            }
        }
    }
}

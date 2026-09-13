using System;
using System.Drawing;
using System.Windows.Forms;
using _4RTools.Model.Vanilla;

namespace _4RTools.Forms
{
    public partial class Container
    {
        private TabPage vanillaMemoryDiscoveryPage;
        private VanillaMemoryDiscoveryPanel integratedMemoryDiscovery;
        private bool memoryDiscoveryIntegrated;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            IntegrateMemoryDiscovery();
        }

        private void IntegrateMemoryDiscovery()
        {
            if (memoryDiscoveryIntegrated || vanillaWorkspace == null || integratedFleetMonitor == null) return;
            memoryDiscoveryIntegrated = true;
            vanillaMemoryDiscoveryPage = new TabPage("Memory finder") { Padding = new Padding(6), UseVisualStyleBackColor = true };
            int diagnosticsIndex = vanillaWorkspace.TabPages.IndexOf(vanillaDiagnosticsPage);
            if (diagnosticsIndex < 0) diagnosticsIndex = vanillaWorkspace.TabPages.Count;
            vanillaWorkspace.TabPages.Insert(diagnosticsIndex, vanillaMemoryDiscoveryPage);
            vanillaWorkspace.SelectedIndexChanged += (s, e) =>
            {
                if (vanillaWorkspace.SelectedTab == vanillaMemoryDiscoveryPage) EnsureMemoryDiscoveryEmbedded();
            };
        }

        private void EnsureMemoryDiscoveryEmbedded()
        {
            if (integratedMemoryDiscovery != null && !integratedMemoryDiscovery.IsDisposed) return;
            integratedMemoryDiscovery = new VanillaMemoryDiscoveryPanel(integratedFleetMonitor) { Dock = DockStyle.Fill };
            vanillaMemoryDiscoveryPage.Controls.Add(integratedMemoryDiscovery);
            integratedMemoryDiscovery.BringToFront();
        }
    }
}

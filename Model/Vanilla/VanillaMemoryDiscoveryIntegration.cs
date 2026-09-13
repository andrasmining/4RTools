using System;
using System.Drawing;
using System.Windows.Forms;
using _4RTools.Model.Vanilla;

namespace _4RTools.Forms
{
    public partial class Container
    {
        private TabPage vanillaMemoryDiscoveryPage, vanillaWeightAlertsPage;
        private VanillaMemoryDiscoveryPanel integratedMemoryDiscovery;
        private VanillaWeightAlertsPanel integratedWeightAlerts;
        private VanillaWeightAlertService integratedWeightAlertService;
        private bool memoryDiscoveryIntegrated, weightAlertsIntegrated;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            IntegrateMemoryDiscovery();
            IntegrateWeightAlerts();
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

        private void IntegrateWeightAlerts()
        {
            if (weightAlertsIntegrated || vanillaWorkspace == null) return;
            weightAlertsIntegrated = true;
            integratedWeightAlertService = new VanillaWeightAlertService(AppDomain.CurrentDomain.BaseDirectory);
            integratedWeightAlertService.Start();
            vanillaWeightAlertsPage = new TabPage("Alerts") { Padding = new Padding(6), UseVisualStyleBackColor = true };
            int insertAt = vanillaMemoryDiscoveryPage == null ? vanillaWorkspace.TabPages.IndexOf(vanillaDiagnosticsPage)
                : vanillaWorkspace.TabPages.IndexOf(vanillaMemoryDiscoveryPage);
            if (insertAt < 0) insertAt = vanillaWorkspace.TabPages.Count;
            vanillaWorkspace.TabPages.Insert(insertAt, vanillaWeightAlertsPage);
            vanillaWorkspace.SelectedIndexChanged += (s, e) =>
            {
                if (vanillaWorkspace.SelectedTab == vanillaWeightAlertsPage) EnsureWeightAlertsEmbedded();
            };
            FormClosed += (s, e) =>
            {
                try { integratedWeightAlerts?.Dispose(); } catch { }
                integratedWeightAlerts = null;
                try { integratedWeightAlertService?.Dispose(); } catch { }
                integratedWeightAlertService = null;
            };
        }

        private void EnsureMemoryDiscoveryEmbedded()
        {
            if (integratedMemoryDiscovery != null && !integratedMemoryDiscovery.IsDisposed) return;
            integratedMemoryDiscovery = new VanillaMemoryDiscoveryPanel(integratedFleetMonitor) { Dock = DockStyle.Fill };
            vanillaMemoryDiscoveryPage.Controls.Add(integratedMemoryDiscovery);
            integratedMemoryDiscovery.BringToFront();
        }

        private void EnsureWeightAlertsEmbedded()
        {
            if (integratedWeightAlerts != null && !integratedWeightAlerts.IsDisposed) return;
            integratedWeightAlerts = new VanillaWeightAlertsPanel(integratedWeightAlertService) { Dock = DockStyle.Fill };
            vanillaWeightAlertsPage.Controls.Add(integratedWeightAlerts);
            integratedWeightAlerts.BringToFront();
        }
    }
}

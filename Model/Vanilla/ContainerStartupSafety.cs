using System;
using System.Windows.Forms;
using _4RTools.Model;

namespace _4RTools.Forms
{
    public partial class Container
    {
        private bool freshStartupLoadInstalled;

        protected override void OnHandleCreated(EventArgs e)
        {
            if (!freshStartupLoadInstalled)
            {
                // The legacy Container_Load auto-selected the only running client. In the
                // Vanilla-first product that is both unnecessary and harmful: it immediately
                // opens a second read-only observation session and can surface an Access Denied
                // modal for an already-running protected client. Startup must only enumerate
                // the current process list. Selecting/attaching remains explicit in the legacy UI;
                // the primary Vanilla dashboard owns its own fresh live observation.
                Load -= Container_Load;
                Load += Container_LoadFreshEnumerationOnly;
                freshStartupLoadInstalled = true;
            }
            base.OnHandleCreated(e);
        }

        private void Container_LoadFreshEnumerationOnly(object sender, EventArgs e)
        {
            ProfileSingleton.Create("Default");
            refreshProcessList();
            refreshProfileList();
            profileCB.SelectedItem = "Default";
            profilesReady = true;

            // Deliberately do NOT auto-select processCB when exactly one client is running.
            // PIDs are never restored from configuration. Every startup/refresh enumerates the
            // live process table, and the legacy client connection is created only on an explicit
            // user selection or the dedicated validation path.
            if (!smokeTest) processCB.SelectedIndex = -1;
        }
    }
}

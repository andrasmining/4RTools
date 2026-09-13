using System;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    internal sealed partial class VanillaReconnectForm
    {
        private bool hardenedAutoStartInstalled;

        /// <summary>
        /// The integrated shell historically calls supervisor.Start() immediately after the
        /// embedded form is shown. Suppress that one legacy direct call in memory and queue the
        /// same persisted StartWith4RTools preference through the serialized UI-driven startup
        /// path instead. The saved preference remains true.
        /// </summary>
        internal void InstallHardenedAutoStartBridge()
        {
            if (hardenedAutoStartInstalled) return;
            hardenedAutoStartInstalled = true;

            VanillaReconnectSettings current = supervisor.Settings;
            if (!current.StartWith4RTools || supervisor.IsRunning || supervisor.IsHardenedStartupRunning) return;

            VanillaReconnectSettings temporary = current.Clone();
            temporary.StartWith4RTools = false;
            supervisor.Apply(temporary, false); // in-memory only; prevents the legacy immediate Start() below the Show() call.

            BeginInvoke((MethodInvoker)(() =>
            {
                if (IsDisposed) return;
                // The form controls/settings were loaded from the original persisted profile, so
                // StartSupervisorHardened restores StartWith4RTools=true when it saves settings.
                StartSupervisorHardened();
            }));
        }
    }
}

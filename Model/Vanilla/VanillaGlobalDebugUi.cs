using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace _4RTools.Forms
{
    public partial class Container
    {
        private bool globalDebugUiInstalled;
        private CheckBox globalDebugEnabled;
        private Button globalCopyDebug;
        private System.Windows.Forms.Timer globalDebugSnapshotTimer;
        private string lastDebugSnapshot;

        private void InstallGlobalDebugUi()
        {
            if (globalDebugUiInstalled) return;
            globalDebugUiInstalled = true;
            if (!smokeTest)
            {
                _4RTools.Model.Vanilla.VanillaDebugLog.Initialize();
                ShowInTaskbar = true;
            }

            Button anchor = FindControlByText<Button>(this, "OPEN DATA FOLDER");
            if (anchor != null && anchor.Parent != null)
            {
                Control parent = anchor.Parent;
                int index = parent.Controls.GetChildIndex(anchor);
                globalDebugEnabled = new CheckBox
                {
                    Text = "Debug log", AutoSize = true,
                    Checked = smokeTest || _4RTools.Model.Vanilla.VanillaDebugLog.Enabled,
                    Margin = new Padding(14, 9, 4, 0)
                };
                globalDebugEnabled.CheckedChanged += (s, e) =>
                {
                    if (smokeTest) return;
                    _4RTools.Model.Vanilla.VanillaDebugLog.SetEnabled(globalDebugEnabled.Checked);
                    integratedUpdateStatus.Text = "Version " + _4RTools.Model.Vanilla.VanillaUpdater.CurrentVersionText
                        + (globalDebugEnabled.Checked ? " - debug log ON." : " - debug log OFF.");
                };
                globalCopyDebug = new Button { Text = "COPY DEBUG LOG", AutoSize = true, Margin = new Padding(4) };
                globalCopyDebug.Click += (s, e) => CopyGlobalDebugLog();
                parent.Controls.Add(globalDebugEnabled);
                parent.Controls.SetChildIndex(globalDebugEnabled, Math.Min(parent.Controls.Count - 1, index + 1));
                parent.Controls.Add(globalCopyDebug);
                parent.Controls.SetChildIndex(globalCopyDebug, Math.Min(parent.Controls.Count - 1, index + 2));
            }
            CompactIntegratedHeader();
            // Smoke/UI tests must exercise the same visible controls but never activate timers,
            // memory access, update checks or clipboard bundles with real process information.
            if (smokeTest) return;

            if (integratedReconnectSupervisor != null)
            {
                integratedReconnectSupervisor.Logged += line => _4RTools.Model.Vanilla.VanillaDebugLog.Write("RECOVERY", line);
                integratedReconnectSupervisor.Updated += WriteRecoveryDebugSnapshot;
            }
            if (vanillaWorkspace != null)
                vanillaWorkspace.SelectedIndexChanged += (s, e) =>
                    _4RTools.Model.Vanilla.VanillaDebugLog.Write("UI", "Vanilla tab selected: " + (vanillaWorkspace.SelectedTab?.Text ?? "none") + ".");
            Application.ThreadException += (s, e) =>
                _4RTools.Model.Vanilla.VanillaDebugLog.Write("THREAD-EXCEPTION", e.Exception == null ? "unknown" : e.Exception.ToString());
            globalDebugSnapshotTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            globalDebugSnapshotTimer.Tick += (s, e) => WriteGlobalDebugSnapshot();
            globalDebugSnapshotTimer.Start();
            FormClosed += (s, e) =>
            {
                try { globalDebugSnapshotTimer?.Stop(); globalDebugSnapshotTimer?.Dispose(); } catch { }
                globalDebugSnapshotTimer = null;
            };
            WriteGlobalDebugSnapshot();
        }

        private void CopyGlobalDebugLog()
        {
            if (smokeTest) return;
            try
            {
                string reconnect = integratedReconnectSupervisor == null ? null : integratedReconnectSupervisor.LogPath;
                string contents = _4RTools.Model.Vanilla.VanillaDebugLog.BuildClipboardBundle(reconnect);
                Clipboard.SetDataObject(string.IsNullOrWhiteSpace(contents) ? "(debug log is empty)" : contents,
                    true, 20, 100);
                integratedUpdateStatus.Text = "Version " + _4RTools.Model.Vanilla.VanillaUpdater.CurrentVersionText + " - debug log copied.";
                _4RTools.Model.Vanilla.VanillaDebugLog.Write("UI", "Global debug bundle copied to clipboard.");
            }
            catch (Exception ex)
            {
                _4RTools.Model.Vanilla.VanillaDebugLog.Write("UI", "Copy debug log FAILED: " + ex);
                MessageBox.Show(this, ex.Message, "Copy debug log", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WriteRecoveryDebugSnapshot()
        {
            if (smokeTest || !_4RTools.Model.Vanilla.VanillaDebugLog.Enabled || integratedReconnectSupervisor == null) return;
            try
            {
                string snapshot = string.Join(" | ", integratedReconnectSupervisor.Statuses().Select(s =>
                    s.Label + ":pid=" + (s.ProcessId.HasValue ? s.ProcessId.Value.ToString() : "none")
                    + ",stage=" + s.Stage + ",screen=" + s.VisualState + ",detail=" + (s.Detail ?? "")));
                _4RTools.Model.Vanilla.VanillaDebugLog.Write("RECOVERY-STATE", snapshot);
            }
            catch (Exception ex) { _4RTools.Model.Vanilla.VanillaDebugLog.Write("RECOVERY-STATE", "snapshot failed: " + ex.Message); }
        }

        private void WriteGlobalDebugSnapshot()
        {
            if (smokeTest || !_4RTools.Model.Vanilla.VanillaDebugLog.Enabled) return;
            try
            {
                Process[] live = Process.GetProcessesByName("Vanilla MMO");
                string pids;
                try { pids = string.Join(",", live.Select(p => p.Id).OrderBy(id => id)); }
                finally { foreach (Process p in live) p.Dispose(); }
                string snapshot = "VanillaPIDs=[" + pids + "], recovery=" + (integratedReconnectSupervisor?.IsRunning == true ? "ON" : "OFF")
                    + ", startup=" + (integratedReconnectSupervisor?.IsHardenedStartupRunning == true ? "ON" : "OFF")
                    + ", selectedTab=" + (vanillaWorkspace?.SelectedTab?.Text ?? "none");
                if (string.Equals(snapshot, lastDebugSnapshot, StringComparison.Ordinal)) return;
                lastDebugSnapshot = snapshot;
                _4RTools.Model.Vanilla.VanillaDebugLog.Write("APP-STATE", snapshot);
            }
            catch (Exception ex) { _4RTools.Model.Vanilla.VanillaDebugLog.Write("APP-STATE", "snapshot failed: " + ex.Message); }
        }

        private static T FindControlByText<T>(Control root, string text) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var typed = child as T;
                if (typed != null && string.Equals(child.Text, text, StringComparison.Ordinal)) return typed;
                if (child.HasChildren)
                {
                    T nested = FindControlByText<T>(child, text);
                    if (nested != null) return nested;
                }
            }
            return null;
        }
    }
}

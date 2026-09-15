using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    public sealed partial class VanillaReconnectSupervisor
    {
        private bool accountProxyRoutingEnabled;
        private string lastProxyRouteLog;

        internal void EnableAccountProxyRouting()
        {
            lock (gate)
            {
                if (accountProxyRoutingEnabled) return;
                accountProxyRoutingEnabled = true;
            }
            Updated += SynchronizeActiveRecoveryProxy;
            SynchronizeActiveRecoveryProxy();
        }

        private void SynchronizeActiveRecoveryProxy()
        {
            string accountId = null;
            string label = null;
            VanillaProxyRoute route = VanillaProxyRoute.Tokyo;
            bool changed = false;
            lock (gate)
            {
                if (disposed) return;
                Runtime active = runtimes.Values.FirstOrDefault(r => r.RecoveryOwned || r.ScriptRunning);
                if (active == null) return;
                accountId = active.Account.Id;
                label = active.Account.Label;
                route = VanillaAccountProxyPreferences.Get(accountId, settings.Proxy);
                if (settings.Proxy != route)
                {
                    settings.Proxy = route;
                    changed = true;
                }
            }

            string key = accountId + "|" + route;
            if (changed || !string.Equals(lastProxyRouteLog, key, StringComparison.Ordinal))
            {
                lastProxyRouteLog = key;
                Log(label + ": account-level proxy route active: " + route + ".");
                VanillaDebugLog.Write("RECOVERY", label + ": account-level proxy route active: " + route + ".");
            }
        }
    }

    internal sealed partial class VanillaReconnectForm
    {
        private bool simplifiedRecoveryUiInstalled;
        private bool autosaveSuppress;
        private System.Windows.Forms.Timer autosaveTimer;
        private System.Windows.Forms.Timer saveToastTimer;
        private string pendingSaveMessage = "Saved";

        internal void InstallSimplifiedRecoveryUi()
        {
            if (simplifiedRecoveryUiInstalled || IsDisposed) return;
            simplifiedRecoveryUiInstalled = true;

            launchArgs.Text = string.Empty;
            launchArgs.Visible = false;
            proxy.Visible = false;
            maxClients.Visible = false;
            HideSiblingLabel(launchArgs, "Arguments");
            HideSiblingLabel(proxy, "Proxy");
            HideSiblingLabel(maxClients, "Clients");

            RemoveButton("Save");
            RemoveButton("OPEN LOG");
            RemoveButton("COPY LOG");
            RemoveButton("COPY RECONNECT LOG");
            RemoveButton("COPY FULL DEBUG LOG");
            RemoveButton("Run login now (selected)");

            InstallAccountProxyColumn();
            InstallMinimalAccountButtons();
            SynchronizeDerivedUiValues();
            HookAutosave();
            RefreshAccountProxyColumn();
            ShowSaveToast("Auto-save on", false);
        }

        private void InstallAccountProxyColumn()
        {
            // Freeze the former shared proxy value into each pre-existing account once. From this
            // point onward the proxy is an account setting and never follows another selected row.
            foreach (VanillaReconnectAccount account in settings.Accounts)
                VanillaAccountProxyPreferences.Ensure(account.Id, settings.Proxy);

            if (!accounts.Columns.Contains("AccountProxy"))
            {
                accounts.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "AccountProxy",
                    HeaderText = "Proxy",
                    ReadOnly = true,
                    FillWeight = 70
                });
            }
            help.SetToolTip(accounts,
                "Managed Vanilla accounts. Double-click or select Edit to change username, password, character slot, proxy and resume hotkey. Passwords use Windows DPAPI and are never written to logs.");
        }

        private void RefreshAccountProxyColumn()
        {
            if (!accounts.Columns.Contains("AccountProxy")) return;
            foreach (DataGridViewRow row in accounts.Rows)
            {
                string id = row.Tag as string;
                if (string.IsNullOrWhiteSpace(id)) continue;
                row.Cells["AccountProxy"].Value = VanillaAccountProxyPreferences.Ensure(id, settings.Proxy).ToString();
            }
        }

        private void HookAutosave()
        {
            autosaveTimer = new System.Windows.Forms.Timer { Interval = 350 };
            autosaveTimer.Tick += (s, e) => SaveAutomaticallyNow();
            saveToastTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            saveToastTimer.Tick += (s, e) =>
            {
                saveToastTimer.Stop();
                if (!testRunning) testState.Text = string.Empty;
            };

            launchPath.TextChanged += (s, e) => QueueAutoSave("Launcher saved");
            startWithApp.CheckedChanged += (s, e) => QueueAutoSave("Startup saved");
            autoRecover.CheckedChanged += (s, e) => QueueAutoSave("Auto relog saved");
            visualWatchdog.CheckedChanged += (s, e) => QueueAutoSave("Visual watchdog saved");
            accounts.RowsAdded += (s, e) => { SynchronizeDerivedUiValues(); QueueAutoSave("Account changes saved"); };
            accounts.RowsRemoved += (s, e) => { SynchronizeDerivedUiValues(); QueueAutoSave("Account changes saved"); };
            accounts.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0) EditAccountMinimal();
            };
            FormClosing += (s, e) =>
            {
                if (autosaveTimer != null && autosaveTimer.Enabled) SaveAutomaticallyNow();
            };
        }

        private void QueueAutoSave(string message)
        {
            if (autosaveSuppress || IsDisposed) return;
            pendingSaveMessage = string.IsNullOrWhiteSpace(message) ? "Saved" : message;
            SynchronizeDerivedUiValues();
            autosaveTimer.Stop();
            autosaveTimer.Start();
        }

        private void SaveAutomaticallyNow()
        {
            autosaveTimer.Stop();
            if (autosaveSuppress || IsDisposed) return;
            try
            {
                SynchronizeDerivedUiValues();
                ReadTop();
                settings.LaunchArguments = string.Empty;
                settings.MaxClients = Math.Max(1, settings.Accounts.Count(a => a.Enabled));
                VanillaReconnectAccount selected = SelectedAccount();
                if (selected != null) settings.Proxy = VanillaAccountProxyPreferences.Get(selected.Id, settings.Proxy);
                supervisor.Apply(settings, true);
                VanillaDebugLog.Write("SETTINGS", "Auto-saved recovery UI. enabledAccounts=" + settings.Accounts.Count(a => a.Enabled)
                    + ", launcher='" + settings.LaunchExecutable + "', autoRecover=" + settings.AutoRecover
                    + ", visualWatchdog=" + settings.VisualWatchdog + ".");
                ShowSaveToast(pendingSaveMessage, false);
            }
            catch (Exception ex)
            {
                VanillaDebugLog.Write("SETTINGS", "Auto-save FAILED: " + ex);
                ShowSaveToast("Save failed", true);
            }
        }

        internal void SynchronizeDerivedUiValues()
        {
            int enabledCount = settings == null || settings.Accounts == null ? 1 : Math.Max(1, settings.Accounts.Count(a => a.Enabled));
            autosaveSuppress = true;
            try
            {
                launchArgs.Text = string.Empty;
                maxClients.Value = Math.Max(maxClients.Minimum, Math.Min(maxClients.Maximum, enabledCount));
                if (settings != null)
                {
                    settings.LaunchArguments = string.Empty;
                    settings.MaxClients = enabledCount;
                }
            }
            finally { autosaveSuppress = false; }
        }

        private void ShowSaveToast(string message, bool error)
        {
            if (testRunning) return;
            testState.Text = (error ? "! " : "✓ ") + message;
            testState.ForeColor = error ? Color.DarkRed : Color.DarkGreen;
            if (saveToastTimer != null)
            {
                saveToastTimer.Stop();
                saveToastTimer.Start();
            }
        }

        private void HideSiblingLabel(Control control, string text)
        {
            if (control == null || control.Parent == null) return;
            foreach (Control sibling in control.Parent.Controls)
                if (sibling is Label && string.Equals(sibling.Text, text, StringComparison.OrdinalIgnoreCase)) sibling.Visible = false;
        }

        private void RemoveButton(string text)
        {
            Button button = FindButton(this, text);
            if (button == null || button.Parent == null) return;
            button.Parent.Controls.Remove(button);
            button.Dispose();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { autosaveTimer?.Stop(); autosaveTimer?.Dispose(); } catch { }
            try { saveToastTimer?.Stop(); saveToastTimer?.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }
}

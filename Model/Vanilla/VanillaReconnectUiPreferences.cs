using System;
using System.Collections.Generic;
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
        private bool refreshingAccountCatalogGrid;
        private System.Windows.Forms.Timer autosaveTimer;
        private System.Windows.Forms.Timer saveToastTimer;
        private string pendingSaveMessage = "Saved";
        private VanillaAccountCatalogStore accountCatalogStore;
        private List<VanillaReconnectAccount> accountCatalog;

        internal void InstallSimplifiedRecoveryUi()
        {
            if (simplifiedRecoveryUiInstalled || IsDisposed) return;
            simplifiedRecoveryUiInstalled = true;

            accountCatalogStore = new VanillaAccountCatalogStore();
            accountCatalog = accountCatalogStore.Load(settings.Accounts);
            VanillaAccountCatalogStore.NormalizeEnabledLimit(accountCatalog);
            SynchronizeSupervisorAccountsFromCatalog();

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

            InstallAccountColumns();
            InstallMinimalAccountButtons();
            SynchronizeDerivedUiValues();
            HookAutosave();
            RefreshAccountGridFromCatalog();
            supervisor.Updated += AccountRuntimeUpdated;
            ShowSaveToast("Auto-save on", false);
        }

        private void InstallAccountColumns()
        {
            foreach (VanillaReconnectAccount account in accountCatalog ?? Enumerable.Empty<VanillaReconnectAccount>())
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
            if (!accounts.Columns.Contains("RuntimePid"))
            {
                accounts.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "RuntimePid",
                    HeaderText = "PID",
                    ReadOnly = true,
                    FillWeight = 50
                });
            }
            if (!accounts.Columns.Contains("RuntimeStatus"))
            {
                accounts.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "RuntimeStatus",
                    HeaderText = "Status",
                    ReadOnly = true,
                    FillWeight = 110
                });
            }
            help.SetToolTip(accounts,
                "Saved Vanilla account profiles. Double-click or Edit to change credentials, slot, proxy and hotkey. Any number may be saved; at most two may be enabled. Runtime PID/status is shown here; hover Status for full detail.");
        }

        private void AccountRuntimeUpdated()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)RefreshAccountSupplementalColumns); } catch { }
                return;
            }
            RefreshAccountSupplementalColumns();
        }

        private void RefreshAccountGridFromCatalog()
        {
            if (IsDisposed || accounts.IsDisposed || accountCatalog == null) return;
            string selectedId = accounts.SelectedRows.Count > 0 ? accounts.SelectedRows[0].Tag as string : null;
            bool oldSuppress = autosaveSuppress;
            autosaveSuppress = true;
            refreshingAccountCatalogGrid = true;
            try
            {
                accounts.Rows.Clear();
                foreach (VanillaReconnectAccount account in accountCatalog)
                {
                    int row = accounts.Rows.Add(account.Enabled ? "Yes" : "No", account.Label, account.UserName,
                        account.CharacterSlot, account.HotkeyText,
                        string.IsNullOrWhiteSpace(account.ProtectedPassword) ? "Not set" : "Encrypted");
                    accounts.Rows[row].Tag = account.Id;
                    if (!string.IsNullOrWhiteSpace(selectedId)
                        && string.Equals(selectedId, account.Id, StringComparison.OrdinalIgnoreCase))
                        accounts.Rows[row].Selected = true;
                }
            }
            finally
            {
                refreshingAccountCatalogGrid = false;
                autosaveSuppress = oldSuppress;
            }
            RefreshAccountSupplementalColumnsCore();
        }

        private bool AccountGridMatchesCatalog()
        {
            if (accountCatalog == null || accounts.Rows.Count != accountCatalog.Count) return false;
            for (int i = 0; i < accountCatalog.Count; i++)
            {
                string id = accounts.Rows[i].Tag as string;
                if (!string.Equals(id, accountCatalog[i].Id, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private void RefreshAccountSupplementalColumns()
        {
            if (IsDisposed || accounts.IsDisposed || accountCatalog == null) return;
            if (!refreshingAccountCatalogGrid && !AccountGridMatchesCatalog())
            {
                RefreshAccountGridFromCatalog();
                return;
            }
            RefreshAccountSupplementalColumnsCore();
        }

        private void RefreshAccountSupplementalColumnsCore()
        {
            IReadOnlyList<VanillaReconnectStatus> statuses;
            try { statuses = supervisor.Statuses(); }
            catch { statuses = new List<VanillaReconnectStatus>(); }
            var byId = statuses.ToDictionary(s => s.AccountId, StringComparer.OrdinalIgnoreCase);

            foreach (DataGridViewRow row in accounts.Rows)
            {
                string id = row.Tag as string;
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (accounts.Columns.Contains("AccountProxy"))
                    row.Cells["AccountProxy"].Value = VanillaAccountProxyPreferences.Ensure(id, settings.Proxy).ToString();

                VanillaReconnectStatus runtime;
                if (!byId.TryGetValue(id, out runtime))
                {
                    if (accounts.Columns.Contains("RuntimePid")) row.Cells["RuntimePid"].Value = "—";
                    if (accounts.Columns.Contains("RuntimeStatus"))
                    {
                        row.Cells["RuntimeStatus"].Value = "Idle";
                        row.Cells["RuntimeStatus"].ToolTipText = "Saved profile is not currently part of the active supervisor configuration.";
                    }
                    continue;
                }

                if (accounts.Columns.Contains("RuntimePid"))
                    row.Cells["RuntimePid"].Value = runtime.ProcessId.HasValue ? runtime.ProcessId.Value.ToString() : "—";
                if (accounts.Columns.Contains("RuntimeStatus"))
                {
                    string compact = VanillaAutobattleStatus.Compact(runtime.Stage, runtime.Detail);
                    if (runtime.VisualState != VanillaVisualState.Unknown && runtime.Stage != VanillaReconnectStage.Stopped
                    && runtime.Stage != VanillaReconnectStage.VerifyingAutobattle)
                        compact += " · " + runtime.VisualState;
                    row.Cells["RuntimeStatus"].Value = compact;
                    row.Cells["RuntimeStatus"].ToolTipText = string.IsNullOrWhiteSpace(runtime.Detail)
                        ? compact
                        : compact + Environment.NewLine + runtime.Detail;
                }
            }
        }

        private VanillaReconnectAccount SelectedCatalogAccount()
        {
            if (accountCatalog == null || accounts.SelectedRows.Count == 0) return null;
            string id = accounts.SelectedRows[0].Tag as string;
            return accountCatalog.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private void SynchronizeSupervisorAccountsFromCatalog()
        {
            if (settings == null || accountCatalog == null || accountCatalog.Count == 0) return;
            var active = accountCatalog.Where(a => a.Enabled).Take(2).Select(a => a.Clone()).ToList();
            if (active.Count == 0)
            {
                VanillaReconnectAccount placeholder = accountCatalog[0].Clone();
                placeholder.Enabled = false;
                active.Add(placeholder);
            }
            settings.Accounts = active;
            settings.MaxClients = Math.Max(1, Math.Min(2, active.Count(a => a.Enabled)));
            VanillaReconnectAccount firstEnabled = active.FirstOrDefault(a => a.Enabled);
            if (firstEnabled != null)
                settings.Proxy = VanillaAccountProxyPreferences.Get(firstEnabled.Id, settings.Proxy);
        }

        private void PersistCatalogAndRefresh(string message)
        {
            if (accountCatalogStore == null || accountCatalog == null) return;
            VanillaAccountCatalogStore.NormalizeEnabledLimit(accountCatalog);
            accountCatalogStore.Save(accountCatalog);
            SynchronizeSupervisorAccountsFromCatalog();
            RefreshAccountGridFromCatalog();
            QueueAutoSave(message);
        }

        // Existing callers use this name after Add/Edit; keep it as a compatibility wrapper.
        private void RefreshAccountProxyColumn()
        {
            RefreshAccountGridFromCatalog();
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
                if (accountCatalogStore != null && accountCatalog != null)
                    accountCatalogStore.Save(accountCatalog);
                SynchronizeSupervisorAccountsFromCatalog();
                SynchronizeDerivedUiValues();
                ReadTop();
                settings.LaunchArguments = string.Empty;
                SynchronizeSupervisorAccountsFromCatalog();
                supervisor.Apply(settings, true);
                VanillaDebugLog.Write("SETTINGS", "Auto-saved recovery UI. profiles=" + (accountCatalog == null ? 0 : accountCatalog.Count)
                    + ", enabledAccounts=" + (accountCatalog == null ? 0 : accountCatalog.Count(a => a.Enabled))
                    + ", launcher='" + settings.LaunchExecutable + "', autoRecover=" + settings.AutoRecover
                    + ", visualWatchdog=" + settings.VisualWatchdog + ".");
                RefreshAccountSupplementalColumns();
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
            int rawEnabled = accountCatalog == null ? (settings == null || settings.Accounts == null ? 0 : settings.Accounts.Count(a => a.Enabled))
                : accountCatalog.Count(a => a.Enabled);
            int managedCount = Math.Max(1, Math.Min(2, rawEnabled));
            autosaveSuppress = true;
            try
            {
                launchArgs.Text = string.Empty;
                maxClients.Value = Math.Max(maxClients.Minimum, Math.Min(maxClients.Maximum, managedCount));
                if (settings != null)
                {
                    settings.LaunchArguments = string.Empty;
                    settings.MaxClients = managedCount;
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
            try { supervisor.Updated -= AccountRuntimeUpdated; } catch { }
            try { autosaveTimer?.Stop(); autosaveTimer?.Dispose(); } catch { }
            try { saveToastTimer?.Stop(); saveToastTimer?.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }
}

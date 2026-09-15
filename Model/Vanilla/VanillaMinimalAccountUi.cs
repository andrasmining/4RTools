using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    internal sealed partial class VanillaReconnectForm
    {
        private bool minimalAccountButtonsInstalled;

        internal void InstallMinimalAccountButtons()
        {
            if (minimalAccountButtonsInstalled) return;
            minimalAccountButtonsInstalled = true;

            ReplaceAccountButton("Add", AddAccountMinimal);
            ReplaceAccountButton("Edit", EditAccountMinimal);
            ReplaceAccountButton("Remove", RemoveAccountMinimal);
            RemoveButton("Run login now (selected)");
        }

        private void ReplaceAccountButton(string text, System.Action action)
        {
            Button existing = FindButton(this, text);
            if (existing == null || existing.Parent == null) return;
            Control parent = existing.Parent;
            int index = parent.Controls.GetChildIndex(existing);
            parent.Controls.Remove(existing);
            existing.Dispose();
            var replacement = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
            replacement.Click += (s, e) => action();
            parent.Controls.Add(replacement);
            parent.Controls.SetChildIndex(replacement, index);
            help.SetToolTip(replacement, text == "Add"
                ? "Add another saved Vanilla account profile. Any number may be stored; at most two may be enabled at once."
                : text == "Edit"
                    ? "Edit the selected account, including enabled state, proxy, character slot, password and resume hotkey."
                    : "Remove the selected saved account profile.");
        }

        private void AddAccountMinimal()
        {
            if (accountCatalog == null) return;
            var account = new VanillaReconnectAccount
            {
                Label = "Client " + (accountCatalog.Count + 1),
                Enabled = accountCatalog.Count(a => a.Enabled) < 2
            };
            VanillaProxyRoute route = settings.Proxy;
            using (var dialog = new VanillaMinimalAccountDialog(supervisor, account, route))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!CanAcceptEnabledState(dialog.Account, null)) return;
                accountCatalog.Add(dialog.Account);
                VanillaAccountProxyPreferences.Set(dialog.Account.Id, dialog.ProxyRoute);
                PersistCatalogAndRefresh("Account added");
            }
        }

        private void EditAccountMinimal()
        {
            VanillaReconnectAccount selected = SelectedCatalogAccount();
            if (selected == null) return;
            VanillaProxyRoute route = VanillaAccountProxyPreferences.Get(selected.Id, settings.Proxy);
            using (var dialog = new VanillaMinimalAccountDialog(supervisor, selected.Clone(), route))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!CanAcceptEnabledState(dialog.Account, selected.Id)) return;
                int index = accountCatalog.FindIndex(a => string.Equals(a.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
                if (index < 0) return;
                accountCatalog[index] = dialog.Account;
                VanillaAccountProxyPreferences.Set(dialog.Account.Id, dialog.ProxyRoute);
                settings.Proxy = dialog.ProxyRoute;
                PersistCatalogAndRefresh("Account saved");
            }
        }

        private bool CanAcceptEnabledState(VanillaReconnectAccount candidate, string replacingId)
        {
            if (candidate == null || !candidate.Enabled) return true;
            int otherEnabled = (accountCatalog ?? new List<VanillaReconnectAccount>()).Count(a => a.Enabled
                && (string.IsNullOrWhiteSpace(replacingId) || !string.Equals(a.Id, replacingId, StringComparison.OrdinalIgnoreCase)));
            if (otherEnabled < 2) return true;
            MessageBox.Show(this,
                "Only two Vanilla accounts can be enabled at the same time. Save this profile as disabled, or disable another profile first.",
                "Two active clients maximum", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private void RemoveAccountMinimal()
        {
            VanillaReconnectAccount selected = SelectedCatalogAccount();
            if (selected == null || accountCatalog == null) return;
            if (accountCatalog.Count <= 1)
            {
                MessageBox.Show(this, "Keep at least one account profile.", "Accounts", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            accountCatalog.RemoveAll(a => string.Equals(a.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
            VanillaAccountProxyPreferences.Remove(selected.Id);
            PersistCatalogAndRefresh("Account removed");
        }
    }

    internal sealed class VanillaMinimalAccountDialog : Form
    {
        private readonly VanillaReconnectSupervisor supervisor;
        private readonly CheckBox enabled = new CheckBox { Text = "Enabled", AutoSize = true };
        private readonly TextBox label = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox user = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox password = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        private readonly NumericUpDown slot = new NumericUpDown { Minimum = 1, Maximum = 15, Width = 80 };
        private readonly ComboBox proxy = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private readonly TextBox hotkey = new TextBox { Width = 160, ReadOnly = true };
        private readonly ToolTip help = new ToolTip { ShowAlways = true, AutoPopDelay = 30000 };
        private int key;
        private bool ctrl, alt, shift;

        public VanillaReconnectAccount Account { get; private set; }
        public VanillaProxyRoute ProxyRoute { get; private set; }

        internal VanillaMinimalAccountDialog(VanillaReconnectSupervisor supervisor, VanillaReconnectAccount account, VanillaProxyRoute proxyRoute)
        {
            this.supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
            Account = account ?? throw new ArgumentNullException(nameof(account));
            ProxyRoute = proxyRoute;
            Text = "Account";
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(470, 355);
            KeyPreview = true;
            Build();

            enabled.Checked = account.Enabled;
            label.Text = account.Label;
            user.Text = account.UserName;
            slot.Value = Math.Max(slot.Minimum, Math.Min(slot.Maximum, account.CharacterSlot));
            proxy.DataSource = Enum.GetValues(typeof(VanillaProxyRoute));
            proxy.SelectedItem = proxyRoute;
            key = account.ResumeKey;
            ctrl = account.ResumeCtrl;
            alt = account.ResumeAlt;
            shift = account.ResumeShift;
            UpdateHotkey();
            try { password.Text = supervisor.GetPassword(account); } catch { password.Text = string.Empty; }
            hotkey.KeyDown += CaptureHotkey;
        }

        private void Build()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 8 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 7; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            AddRow(table, 0, string.Empty, enabled);
            AddRow(table, 1, "Account", label);
            AddRow(table, 2, "Username", user);
            AddRow(table, 3, "Password", password);
            AddRow(table, 4, "Character slot", slot);
            AddRow(table, 5, "Proxy", proxy);
            AddRow(table, 6, "Resume hotkey", hotkey);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.Right };
            var save = new Button { Text = "Save", AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            save.Click += Save;
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            table.Controls.Add(buttons, 1, 7);
            Controls.Add(table);
            AcceptButton = save;
            CancelButton = cancel;

            help.SetToolTip(enabled, "Enabled profiles are supervised/started. At most two profiles may be enabled at once; any number may be saved.");
            help.SetToolTip(proxy, "Proxy route used for this account only.");
            help.SetToolTip(hotkey, "Click here and press the key combination used to resume Vanilla auto-battle after login.");
            help.SetToolTip(password, "Stored with Windows DPAPI for this Windows user and never written to logs.");
        }

        private static void AddRow(TableLayoutPanel table, int row, string caption, Control control)
        {
            if (!string.IsNullOrEmpty(caption))
                table.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, row);
            control.Margin = new Padding(0, 3, 0, 3);
            table.Controls.Add(control, 1, row);
        }

        private void CaptureHotkey(object sender, KeyEventArgs e)
        {
            Keys candidate = e.KeyCode;
            if (candidate == Keys.ControlKey || candidate == Keys.ShiftKey || candidate == Keys.Menu) return;
            key = (int)candidate;
            ctrl = e.Control;
            alt = e.Alt;
            shift = e.Shift;
            UpdateHotkey();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        private void UpdateHotkey()
        {
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            parts.Add(((Keys)key).ToString());
            hotkey.Text = string.Join("+", parts);
        }

        private void Save(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(label.Text)) throw new ArgumentException("Enter an account name.");
                if (string.IsNullOrWhiteSpace(user.Text)) throw new ArgumentException("Enter the Vanilla username.");
                if (string.IsNullOrEmpty(password.Text)) throw new ArgumentException("Enter the password.");
                if (!(proxy.SelectedItem is VanillaProxyRoute)) throw new ArgumentException("Select a proxy route.");

                Account.Enabled = enabled.Checked;
                Account.Label = label.Text.Trim();
                Account.UserName = user.Text.Trim();
                Account.ProtectedPassword = supervisor.ProtectPassword(password.Text);
                Account.CharacterSlot = (int)slot.Value;
                Account.ResumeKey = key;
                Account.ResumeCtrl = ctrl;
                Account.ResumeAlt = alt;
                Account.ResumeShift = shift;
                ProxyRoute = (VanillaProxyRoute)proxy.SelectedItem;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Account", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

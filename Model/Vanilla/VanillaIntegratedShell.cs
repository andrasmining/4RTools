using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using _4RTools.Model;
using _4RTools.Model.Vanilla;

namespace _4RTools.Forms
{
    public partial class Container
    {
        private VanillaReconnectSupervisor integratedReconnectSupervisor;
        private VanillaReconnectForm integratedReconnectView;
        private TabControl vanillaWorkspace;
        private TabPage vanillaRecoveryPage, vanillaRulesPage, vanillaDiagnosticsPage, vanillaAboutPage;
        private readonly Label integratedUpdateStatus = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 8, 0, 0) };
        private bool integratedVanillaReady, updateCheckRunning;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (integratedVanillaReady) return;
            integratedVanillaReady = true;
            VanillaAppData.InitializeAndMigrateLegacy(AppDomain.CurrentDomain.BaseDirectory);
            Text = "4RTools Vanilla " + VanillaUpdater.CurrentVersionText;
            ExpandForIntegratedWorkspace();
            BuildIntegratedVanillaWorkspace();
            if (!smokeTest) BeginInvoke((MethodInvoker)(() => CheckForUpdates(true)));
        }

        private void ExpandForIntegratedWorkspace()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int width = Math.Min(1380, Math.Max(1050, area.Width - 40));
            int height = Math.Min(940, Math.Max(720, area.Height - 40));
            MinimumSize = new Size(Math.Min(1080, width), Math.Min(720, height));
            Size = new Size(width, height);
            StartPosition = FormStartPosition.CenterScreen;
        }

        private void BuildIntegratedVanillaWorkspace()
        {
            integratedReconnectSupervisor = new VanillaReconnectSupervisor(VanillaAppData.RootDirectory);
            tabPageVanilla.Controls.Clear();
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), RowCount = 2, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 6) };
            header.Controls.Add(new Label { Text = "Vanilla workspace", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(4, 8, 14, 0) });
            AddIntegratedButton(header, "OPEN DATA FOLDER", OpenDataFolder);
            AddIntegratedButton(header, "CHECK FOR UPDATES", () => CheckForUpdates(false));
            integratedUpdateStatus.Text = "Version " + VanillaUpdater.CurrentVersionText + " â€” settings persist in Windows user data.";
            header.Controls.Add(integratedUpdateStatus);
            root.Controls.Add(header, 0, 0);

            vanillaWorkspace = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
            vanillaRecoveryPage = new TabPage("Recovery & relog") { Padding = new Padding(4) };
            vanillaRulesPage = new TabPage("Automation rules") { Padding = new Padding(4) };
            vanillaDiagnosticsPage = new TabPage("Diagnostics") { Padding = new Padding(4) };
            vanillaAboutPage = new TabPage("Data & updates") { Padding = new Padding(12) };
            vanillaWorkspace.TabPages.AddRange(new[] { vanillaRecoveryPage, vanillaRulesPage, vanillaDiagnosticsPage, vanillaAboutPage });
            vanillaWorkspace.SelectedIndexChanged += (s, e) =>
            {
                if (vanillaWorkspace.SelectedTab == vanillaRulesPage) EnsureAutomationEmbedded();
                if (vanillaWorkspace.SelectedTab == vanillaDiagnosticsPage) EnsureDiagnosticsEmbedded();
            };

            integratedReconnectView = new VanillaReconnectForm(integratedReconnectSupervisor);
            integratedReconnectView.PrepareForEmbeddedHost();
            vanillaRecoveryPage.Controls.Add(integratedReconnectView);
            integratedReconnectView.Show();

            BuildAboutPage();
            root.Controls.Add(vanillaWorkspace, 0, 1);
            tabPageVanilla.Controls.Add(root);
            vanillaWorkspace.SelectedTab = vanillaRecoveryPage;

            if (!smokeTest && integratedReconnectSupervisor.Settings.StartWith4RTools && !integratedReconnectSupervisor.IsRunning)
                integratedReconnectSupervisor.Start();
        }

        private void EnsureAutomationEmbedded()
        {
            if (vanillaExtras != null && !vanillaExtras.IsDisposed) return;
            vanillaExtras = new VanillaAutomationForm(vanillaSession, () => { vanillaWorkspace.SelectedTab = vanillaDiagnosticsPage; EnsureDiagnosticsEmbedded(); }, hosted: true, ownsSession: false);
            vanillaExtras.EmergencyStopRequested = () => ForceOff("Emergency stop");
            vanillaExtras.EmergencyKeyAllowed = key => key != (int)(Keys)Enum.Parse(typeof(Keys), ProfileSingleton.GetCurrent().UserPreferences.toggleStateKey);
            PrepareEmbeddedForm(vanillaExtras);
            vanillaRulesPage.Controls.Add(vanillaExtras);
            vanillaExtras.Show();
        }

        private void EnsureDiagnosticsEmbedded()
        {
            if (vanillaDiagnostics != null && !vanillaDiagnostics.IsDisposed) return;
            ForceOff("Diagnostics opened");
            vanillaDiagnostics = new VanillaDiagnosticsForm(subject);
            PrepareEmbeddedForm(vanillaDiagnostics);
            vanillaDiagnosticsPage.Controls.Add(vanillaDiagnostics);
            vanillaDiagnostics.Show();
        }

        private static void PrepareEmbeddedForm(Form form)
        {
            form.TopLevel = false;
            form.FormBorderStyle = FormBorderStyle.None;
            form.Dock = DockStyle.Fill;
            form.ShowInTaskbar = false;
            form.MinimumSize = Size.Empty;
        }

        private void BuildAboutPage()
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false };
            panel.Controls.Add(new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Text = "Persistent data" });
            panel.Controls.Add(PathLabel("Data root", VanillaAppData.RootDirectory));
            panel.Controls.Add(PathLabel("Original 4R profiles", VanillaAppData.StockProfilesDirectory));
            panel.Controls.Add(PathLabel("Vanilla rule profiles", VanillaAppData.VanillaProfilesDirectory));
            panel.Controls.Add(PathLabel("Recovery accounts/settings", VanillaAppData.ReconnectSettingsPath));
            panel.Controls.Add(PathLabel("Logs", VanillaAppData.LogsDirectory));
            panel.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(1050, 0), Margin = new Padding(3, 12, 3, 10), ForeColor = Color.DimGray,
                Text = "The versioned application folder no longer stores user profiles or recovery credentials. On first run this version migrates compatible data from the current folder and nearby older 4RTools-Vanilla-v* folders. Passwords remain Windows-DPAPI protected for this Windows user/PC."
            });
            var buttons = new FlowLayoutPanel { AutoSize = true };
            AddIntegratedButton(buttons, "OPEN DATA FOLDER", OpenDataFolder);
            AddIntegratedButton(buttons, "CHECK FOR UPDATES", () => CheckForUpdates(false));
            AddIntegratedButton(buttons, "OPEN GITHUB RELEASES", () => Process.Start("https://github.com/andrasmining/4RTools/releases"));
            panel.Controls.Add(buttons);
            panel.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(1050, 0), Margin = new Padding(3, 12, 3, 3),
                Text = "Updates are checked at every normal startup. A newer verified GitHub Release is offered for download; its ZIP checksum and packaged SHA256SUMS manifest are verified before 4RTools restarts into the new version."
            });
            vanillaAboutPage.Controls.Add(panel);
        }

        private static Label PathLabel(string caption, string path)
        {
            return new Label { AutoSize = true, MaximumSize = new Size(1080, 0), Text = caption + ":  " + path, Margin = new Padding(3, 6, 3, 0) };
        }

        private static void AddIntegratedButton(Control parent, string text, System.Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Margin = new Padding(4) };
            button.Click += (s, e) => action();
            parent.Controls.Add(button);
        }

        private void OpenDataFolder()
        {
            VanillaAppData.InitializeAndMigrateLegacy(AppDomain.CurrentDomain.BaseDirectory);
            Process.Start(VanillaAppData.RootDirectory);
        }

        private async void CheckForUpdates(bool startup)
        {
            if (updateCheckRunning || smokeTest) return;
            updateCheckRunning = true;
            integratedUpdateStatus.Text = "Checking GitHub Releasesâ€¦";
            try
            {
                VanillaUpdateInfo update = await VanillaUpdater.CheckAsync();
                if (update == null)
                {
                    integratedUpdateStatus.Text = "Version " + VanillaUpdater.CurrentVersionText + " â€” up to date.";
                    if (!startup) MessageBox.Show(this, "4RTools Vanilla " + VanillaUpdater.CurrentVersionText + " is the latest published release.", "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                integratedUpdateStatus.Text = "Update available: " + update.TagName;
                DialogResult answer = MessageBox.Show(this,
                    "4RTools Vanilla " + update.Version.ToString(3) + " is available. Download the verified GitHub Release and restart now?\n\nYour profiles and recovery settings are stored outside the application folder and will be preserved.",
                    "4RTools Vanilla update", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer != DialogResult.Yes) return;
                integratedUpdateStatus.Text = "Downloading and verifying " + update.TagName + "â€¦";
                string payload = await VanillaUpdater.DownloadAndStageAsync(update);
                integratedUpdateStatus.Text = "Update verified. Restartingâ€¦";
                VanillaUpdater.BeginApplyAndRestart(payload);
                BeginInvoke((MethodInvoker)Application.Exit);
            }
            catch (Exception ex)
            {
                integratedUpdateStatus.Text = startup ? "Update check unavailable â€” normal use continues." : "Update check failed.";
                if (!startup) MessageBox.Show(this, ex.Message, "Update check failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { updateCheckRunning = false; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { integratedReconnectSupervisor?.Dispose(); } catch { }
            integratedReconnectSupervisor = null;
            base.OnFormClosed(e);
        }
    }
}

namespace _4RTools.Model.Vanilla
{
    internal sealed partial class VanillaReconnectForm
    {
        internal void PrepareForEmbeddedHost()
        {
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            Dock = DockStyle.Fill;
            ShowInTaskbar = false;
            MinimumSize = Size.Empty;
            StartPosition = FormStartPosition.Manual;
        }
    }
}
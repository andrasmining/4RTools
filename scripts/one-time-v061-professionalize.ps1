Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
$nl = [Environment]::NewLine
function Read([string]$p) { [IO.File]::ReadAllText($p) }
function Write([string]$p,[string]$t) { $dir = Split-Path -Parent $p; if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }; [IO.File]::WriteAllText($p,$t,$utf8) }
function Replace-Exact([string]$path,[string]$old,[string]$new) { $t=Read $path; if(-not $t.Contains($old)){throw "Anchor not found in $path`n$old"}; Write $path ($t.Replace($old,$new)) }

Write 'Model/Vanilla/VanillaAppData.cs' @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Stable per-Windows-user storage. Release folders are disposable; user configuration is not.
    /// </summary>
    public static class VanillaAppData
    {
        public const string DataRootEnvironmentVariable = "FOURRTOOLS_DATA_ROOT";
        private const string ProductFolder = "4RTools Vanilla";

        public static string RootDirectory
        {
            get
            {
                string overridden = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
                if (!string.IsNullOrWhiteSpace(overridden)) return Path.GetFullPath(overridden);
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductFolder);
            }
        }

        public static string ProfilesDirectory { get { return Path.Combine(RootDirectory, "Profiles"); } }
        public static string StockProfilesDirectory { get { return Path.Combine(ProfilesDirectory, "Stock"); } }
        public static string VanillaProfilesDirectory { get { return Path.Combine(ProfilesDirectory, "Vanilla"); } }
        public static string ReconnectDirectory { get { return Path.Combine(RootDirectory, "VanillaReconnect"); } }
        public static string ReconnectSettingsPath { get { return Path.Combine(ReconnectDirectory, "reconnect.json"); } }
        public static string LogsDirectory { get { return Path.Combine(RootDirectory, "Logs"); } }
        public static string UpdatesDirectory { get { return Path.Combine(RootDirectory, "Updates"); } }
        public static string LocalServersPath { get { return Path.Combine(RootDirectory, "supported_servers.json"); } }

        public static void InitializeAndMigrateLegacy(string applicationDirectory)
        {
            string root = RootDirectory;
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(ProfilesDirectory);
            Directory.CreateDirectory(StockProfilesDirectory);
            Directory.CreateDirectory(VanillaProfilesDirectory);
            Directory.CreateDirectory(ReconnectDirectory);
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(UpdatesDirectory);

            foreach (var candidate in LegacyInstallCandidates(applicationDirectory))
            {
                bool currentInstall = SamePath(candidate, applicationDirectory);
                MigrateDirectory(Path.Combine(candidate, "Profile"), StockProfilesDirectory, "*.json", currentInstall);
                MigrateDirectory(Path.Combine(candidate, "Profiles", "Vanilla"), VanillaProfilesDirectory, "*.json", currentInstall);
                MigrateFile(Path.Combine(candidate, "VanillaReconnect", "reconnect.json"), ReconnectSettingsPath, currentInstall);
                MigrateFile(Path.Combine(candidate, "supported_servers.json"), LocalServersPath, currentInstall);
                MigrateDirectory(Path.Combine(candidate, "Logs"), LogsDirectory, "*.log", currentInstall);
                if (currentInstall) CleanupKnownLegacyDirectories(candidate);
            }
            CleanupOldUpdateStaging();
        }

        private static IEnumerable<string> LegacyInstallCandidates(string applicationDirectory)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string current = string.IsNullOrWhiteSpace(applicationDirectory) ? null : Path.GetFullPath(applicationDirectory);
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current) && seen.Add(current)) yield return current;
            if (string.IsNullOrWhiteSpace(current)) yield break;
            DirectoryInfo parent;
            try { parent = Directory.GetParent(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)); }
            catch { yield break; }
            if (parent == null || !parent.Exists) yield break;
            DirectoryInfo[] siblings;
            try { siblings = parent.GetDirectories("4RTools-Vanilla-v*"); }
            catch { yield break; }
            foreach (var sibling in siblings.OrderByDescending(d => d.LastWriteTimeUtc))
            {
                string path = sibling.FullName;
                if (seen.Add(path)) yield return path;
            }
        }

        private static void MigrateDirectory(string source, string destination, string pattern, bool removeSource)
        {
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source, pattern, SearchOption.TopDirectoryOnly))
            {
                string target = Path.Combine(destination, Path.GetFileName(file));
                try
                {
                    if (!File.Exists(target)) File.Copy(file, target, false);
                    if (removeSource && File.Exists(target) && FilesEqual(file, target)) File.Delete(file);
                }
                catch { }
            }
        }

        private static void MigrateFile(string source, string destination, bool removeSource)
        {
            if (!File.Exists(source)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (!File.Exists(destination)) File.Copy(source, destination, false);
                if (removeSource && File.Exists(destination) && FilesEqual(source, destination)) File.Delete(source);
            }
            catch { }
        }

        private static bool FilesEqual(string left, string right)
        {
            var a = new FileInfo(left); var b = new FileInfo(right);
            if (a.Length != b.Length) return false;
            using (var sha = SHA256.Create())
            using (var leftStream = File.OpenRead(left))
            using (var rightStream = File.OpenRead(right))
                return sha.ComputeHash(leftStream).SequenceEqual(sha.ComputeHash(rightStream));
        }

        public static void CleanupKnownLegacyDirectories(string applicationDirectory)
        {
            if (string.IsNullOrWhiteSpace(applicationDirectory)) return;
            string root = Path.GetFullPath(applicationDirectory);
            TryDeleteIfEmpty(Path.Combine(root, "Profile"));
            TryDeleteIfEmpty(Path.Combine(root, "Profiles", "Vanilla"));
            TryDeleteIfEmpty(Path.Combine(root, "Profiles"));
            TryDeleteIfEmpty(Path.Combine(root, "VanillaReconnect"));
            TryDeleteIfEmpty(Path.Combine(root, "Logs"));
        }

        private static void TryDeleteIfEmpty(string path)
        {
            try
            {
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, false);
            }
            catch { }
        }

        private static bool SamePath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static void CleanupOldUpdateStaging()
        {
            try
            {
                if (!Directory.Exists(UpdatesDirectory)) return;
                DateTime cutoff = DateTime.UtcNow.AddDays(-2);
                foreach (var dir in new DirectoryInfo(UpdatesDirectory).GetDirectories())
                    if (dir.LastWriteTimeUtc < cutoff) { try { dir.Delete(true); } catch { } }
            }
            catch { }
        }
    }
}
'@

Write 'Model/Vanilla/VanillaUpdater.cs' @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace _4RTools.Model.Vanilla
{
    public sealed class VanillaUpdateInfo
    {
        public Version Version { get; set; }
        public string TagName { get; set; }
        public string ReleaseUrl { get; set; }
        public string ZipUrl { get; set; }
        public string ChecksumUrl { get; set; }
        public string ZipName { get; set; }
    }

    /// <summary>Verified GitHub-release updater. User data lives outside the install directory.</summary>
    public static class VanillaUpdater
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/andrasmining/4RTools/releases/latest";
        private static readonly HttpClient Http = CreateClient();

        public static Version CurrentVersion
        {
            get
            {
                var value = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(value.Major, value.Minor, Math.Max(0, value.Build));
            }
        }
        public static string CurrentVersionText { get { return CurrentVersion.ToString(3); } }
        public static bool IsNewerVersion(Version candidate, Version current) { return candidate != null && current != null && candidate > current; }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("4RTools-Vanilla-Updater/" + CurrentVersionText);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        public static async Task<VanillaUpdateInfo> CheckAsync()
        {
            string json = await Http.GetStringAsync(LatestReleaseApi).ConfigureAwait(false);
            var root = JObject.Parse(json);
            string tag = (string)root["tag_name"];
            if (string.IsNullOrWhiteSpace(tag)) throw new InvalidDataException("GitHub returned a release without a tag.");
            Version version;
            if (!Version.TryParse(tag.Trim().TrimStart('v', 'V'), out version)) throw new InvalidDataException("Unsupported release tag: " + tag);
            version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
            if (!IsNewerVersion(version, CurrentVersion)) return null;

            string zipName = "4RTools-Vanilla-v" + version.ToString(3) + "-portable.zip";
            var assets = root["assets"] as JArray;
            if (assets == null) throw new InvalidDataException("GitHub release has no assets.");
            string zipUrl = AssetUrl(assets, zipName);
            string checksumUrl = AssetUrl(assets, zipName + ".sha256");
            return new VanillaUpdateInfo
            {
                Version = version,
                TagName = tag,
                ReleaseUrl = (string)root["html_url"],
                ZipUrl = zipUrl,
                ChecksumUrl = checksumUrl,
                ZipName = zipName
            };
        }

        private static string AssetUrl(JArray assets, string name)
        {
            foreach (var asset in assets.OfType<JObject>())
                if (string.Equals((string)asset["name"], name, StringComparison.OrdinalIgnoreCase))
                {
                    string url = (string)asset["browser_download_url"];
                    if (!string.IsNullOrWhiteSpace(url)) return url;
                }
            throw new InvalidDataException("Release asset is missing: " + name);
        }

        public static async Task<string> DownloadAndStageAsync(VanillaUpdateInfo info)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            VanillaAppData.InitializeAndMigrateLegacy(AppDomain.CurrentDomain.BaseDirectory);
            string stage = Path.Combine(VanillaAppData.UpdatesDirectory, "v" + info.Version.ToString(3) + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                byte[] zip = await Http.GetByteArrayAsync(info.ZipUrl).ConfigureAwait(false);
                string checksumText = await Http.GetStringAsync(info.ChecksumUrl).ConfigureAwait(false);
                string expected = checksumText.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(expected) || expected.Length != 64) throw new InvalidDataException("Release checksum is malformed.");
                string actual = Sha256(zip);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Downloaded update checksum does not match the GitHub release checksum.");

                string zipPath = Path.Combine(stage, info.ZipName);
                File.WriteAllBytes(zipPath, zip);
                string extractRoot = Path.Combine(stage, "payload");
                Directory.CreateDirectory(extractRoot);
                SafeExtract(zipPath, extractRoot);
                string[] executables = Directory.GetFiles(extractRoot, "4RTools-Vanilla.exe", SearchOption.AllDirectories);
                if (executables.Length != 1) throw new InvalidDataException("Update package must contain exactly one 4RTools-Vanilla.exe.");
                string payload = Path.GetDirectoryName(executables[0]);
                VerifyPayloadManifest(payload);
                var fileVersion = FileVersionInfo.GetVersionInfo(executables[0]);
                if (fileVersion.FileMajorPart != info.Version.Major || fileVersion.FileMinorPart != info.Version.Minor || fileVersion.FileBuildPart != info.Version.Build)
                    throw new InvalidDataException("Update executable version does not match release tag.");
                return payload;
            }
            catch
            {
                try { Directory.Delete(stage, true); } catch { }
                throw;
            }
        }

        public static void BeginApplyAndRestart(string payloadDirectory)
        {
            if (string.IsNullOrWhiteSpace(payloadDirectory)) throw new ArgumentException("Update payload is missing.");
            string stagedExe = Path.Combine(payloadDirectory, "4RTools-Vanilla.exe");
            if (!File.Exists(stagedExe)) throw new FileNotFoundException("Staged updater executable is missing.", stagedExe);
            int pid = Process.GetCurrentProcess().Id;
            string install = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var start = new ProcessStartInfo
            {
                FileName = stagedExe,
                Arguments = "--apply-update " + pid + " " + Quote(install) + " " + Quote(payloadDirectory),
                WorkingDirectory = payloadDirectory,
                UseShellExecute = true
            };
            Process.Start(start);
        }

        public static bool TryHandleApplyCommand(string[] args)
        {
            if (args == null || args.Length == 0 || !string.Equals(args[0], "--apply-update", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                if (args.Length != 4) throw new ArgumentException("Update helper arguments are invalid.");
                int parentPid;
                if (!int.TryParse(args[1], out parentPid) || parentPid <= 0) throw new ArgumentException("Update parent PID is invalid.");
                string install = Path.GetFullPath(args[2]);
                string payload = Path.GetFullPath(args[3]);
                WaitForExit(parentPid, 60000);
                CopyPayload(payload, install);
                VanillaAppData.InitializeAndMigrateLegacy(install);
                VanillaAppData.CleanupKnownLegacyDirectories(install);
                string destinationExe = Path.Combine(install, "4RTools-Vanilla.exe");
                if (!File.Exists(destinationExe)) throw new FileNotFoundException("Updated executable is missing after installation.", destinationExe);
                Process.Start(new ProcessStartInfo { FileName = destinationExe, WorkingDirectory = install, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(VanillaAppData.LogsDirectory);
                    File.AppendAllText(Path.Combine(VanillaAppData.LogsDirectory, "update-error.log"), DateTimeOffset.Now.ToString("u") + " " + ex + Environment.NewLine);
                }
                catch { }
                MessageBox.Show(ex.Message, "4RTools Vanilla update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return true;
        }

        private static void WaitForExit(int pid, int timeoutMs)
        {
            try
            {
                using (var process = Process.GetProcessById(pid))
                    if (!process.WaitForExit(timeoutMs)) throw new TimeoutException("The previous 4RTools process did not exit in time.");
            }
            catch (ArgumentException) { }
        }

        private static void CopyPayload(string payload, string install)
        {
            if (!Directory.Exists(payload)) throw new DirectoryNotFoundException("Update payload directory is missing.");
            Directory.CreateDirectory(install);
            string payloadRoot = Path.GetFullPath(payload).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string directory in Directory.GetDirectories(payload, "*", SearchOption.AllDirectories))
            {
                string relative = directory.Substring(payloadRoot.Length);
                Directory.CreateDirectory(Path.Combine(install, relative));
            }
            foreach (string file in Directory.GetFiles(payload, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(payloadRoot.Length);
                CopyFileWithRetry(file, Path.Combine(install, relative));
            }
        }

        private static void CopyFileWithRetry(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            Exception last = null;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try { File.Copy(source, destination, true); return; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { last = ex; Thread.Sleep(500); }
            }
            throw new IOException("Could not replace " + destination + ".", last);
        }

        private static void SafeExtract(string zipPath, string destination)
        {
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    string path = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update ZIP contains an unsafe path.");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(path); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var input = entry.Open())
                    using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                }
            }
        }

        private static void VerifyPayloadManifest(string payload)
        {
            string manifest = Path.Combine(payload, "SHA256SUMS.txt");
            if (!File.Exists(manifest)) throw new InvalidDataException("Update package checksum manifest is missing.");
            foreach (string raw in File.ReadAllLines(manifest))
            {
                string line = raw.Trim(); if (line.Length == 0) continue;
                int split = line.IndexOf("  ", StringComparison.Ordinal);
                if (split != 64) throw new InvalidDataException("Update payload checksum manifest is malformed.");
                string expected = line.Substring(0, 64);
                string relative = line.Substring(split + 2).Replace('/', Path.DirectorySeparatorChar);
                string path = Path.GetFullPath(Path.Combine(payload, relative));
                string root = Path.GetFullPath(payload).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new InvalidDataException("Update payload manifest references an invalid file.");
                string actual = Sha256(File.ReadAllBytes(path));
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update payload checksum failed for " + relative + ".");
            }
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        private static string Quote(string value) { return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\""; }
    }
}
'@

Write 'Model/Vanilla/VanillaIntegratedShell.cs' @'
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
            integratedUpdateStatus.Text = "Version " + VanillaUpdater.CurrentVersionText + " — settings persist in Windows user data.";
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
            Process.Start(new ProcessStartInfo { FileName = VanillaAppData.RootDirectory, UseShellExecute = true });
        }

        private async void CheckForUpdates(bool startup)
        {
            if (updateCheckRunning || smokeTest) return;
            updateCheckRunning = true;
            integratedUpdateStatus.Text = "Checking GitHub Releases…";
            try
            {
                VanillaUpdateInfo update = await VanillaUpdater.CheckAsync();
                if (update == null)
                {
                    integratedUpdateStatus.Text = "Version " + VanillaUpdater.CurrentVersionText + " — up to date.";
                    if (!startup) MessageBox.Show(this, "4RTools Vanilla " + VanillaUpdater.CurrentVersionText + " is the latest published release.", "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                integratedUpdateStatus.Text = "Update available: " + update.TagName;
                DialogResult answer = MessageBox.Show(this,
                    "4RTools Vanilla " + update.Version.ToString(3) + " is available. Download the verified GitHub Release and restart now?\n\nYour profiles and recovery settings are stored outside the application folder and will be preserved.",
                    "4RTools Vanilla update", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer != DialogResult.Yes) return;
                integratedUpdateStatus.Text = "Downloading and verifying " + update.TagName + "…";
                string payload = await VanillaUpdater.DownloadAndStageAsync(update);
                integratedUpdateStatus.Text = "Update verified. Restarting…";
                VanillaUpdater.BeginApplyAndRestart(payload);
                BeginInvoke((MethodInvoker)Application.Exit);
            }
            catch (Exception ex)
            {
                integratedUpdateStatus.Text = startup ? "Update check unavailable — normal use continues." : "Update check failed.";
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
'@

# Canonical persistent paths and fork update metadata.
Write 'Utils/AppConfig.cs' @'
using System;
using System.Collections.Generic;
using System.IO;
using _4RTools.Model.Vanilla;

namespace _4RTools.Utils
{
    internal class AppConfig
    {
        public static string Name = "4RTools Vanilla";
        public static string ProfileFolder = InitializeProfileFolder();
        public static string Website = "https://www.4rtools.com.br";
        public static string GithubLink = "https://github.com/andrasmining/4RTools";
        public static string DiscordLink = "https://discord.gg/AtZ2fJVtBz";
        public static string _4RClientsURL = "https://storage.googleapis.com/4rtools/supported_servers.json";
        public static string _4RAdvertiserUrl = "https://storage.googleapis.com/4rtools/advertisers.json";
        public static string _4RLatestVersionURL = "https://api.github.com/repos/andrasmining/4RTools/releases/latest";
        public static string _4RApiHost = "https://api.4rtools.com.br/api";
        public static string Version = "v0.6.1";

        private static string InitializeProfileFolder()
        {
            VanillaAppData.InitializeAndMigrateLegacy(AppDomain.CurrentDomain.BaseDirectory);
            return VanillaAppData.StockProfilesDirectory + Path.DirectorySeparatorChar;
        }
    }
}
'@

# Original local server definitions are user data too; also disable the legacy second-window reconnect bootstrap.
$p='Model/LocalServerManager.cs';$t=Read $p
$t=$t.Replace('private static readonly string localServerName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "supported_servers.json");','private static readonly string localServerName = Vanilla.VanillaAppData.LocalServersPath;')
$old=@'
        static LocalServerManager()
        {
            // Normal UI startup reaches this class before the main window is shown. The bootstrap
            // deliberately ignores developer/headless -- commands, so validation tools stay isolated.
            Vanilla.VanillaReconnectBootstrap.Initialize();
        }
'@
$new=@'
        static LocalServerManager()
        {
            Vanilla.VanillaAppData.InitializeAndMigrateLegacy(AppDomain.CurrentDomain.BaseDirectory);
        }
'@
if(-not $t.Contains($old)){throw 'LocalServerManager bootstrap anchor missing'}
$t=$t.Replace($old,$new);Write $p $t

# Extra-rule profiles use the same single persistent Profiles root.
$p='Model/Vanilla/Automation/AutomationProfileStore.cs';$t=Read $p
$old='            directory = Path.Combine(Path.GetFullPath(baseDirectory), "Profiles", "Vanilla");'
$new='            VanillaAppData.InitializeAndMigrateLegacy(AppDomain.CurrentDomain.BaseDirectory);'+$nl+'            directory = VanillaAppData.VanillaProfilesDirectory;'
if(-not $t.Contains($old)){throw 'AutomationProfileStore path anchor missing'}
$t=$t.Replace($old,$new);Write $p $t

# Vanilla rule logs are persistent too; packaged build profiles remain beside the executable.
$p='Model/Vanilla/Automation/VanillaAutomationSession.cs';$t=Read $p
$old='            logPath = Path.Combine(this.baseDirectory, "Logs", "vanilla.log");'
$new='            VanillaAppData.InitializeAndMigrateLegacy(this.baseDirectory);'+$nl+'            logPath = Path.Combine(VanillaAppData.LogsDirectory, "vanilla.log");'
if(-not $t.Contains($old)){throw 'VanillaAutomationSession log anchor missing'}
$t=$t.Replace($old,$new);Write $p $t

# The legacy reconnect bootstrap created a second window and tray icon. Keep API compatibility but make normal hosting single-window.
$p='Model/Vanilla/VanillaReconnect.cs';$t=Read $p
$old=@'
        public static void Initialize()
        {
            lock (Gate)
            {
                if (initialized) return;
                initialized = true;
                string[] args = Environment.GetCommandLineArgs();
                if (args.Skip(1).Any(a => a.StartsWith("--", StringComparison.OrdinalIgnoreCase))) return;
                Application.Idle += OnFirstIdle;
                Application.ApplicationExit += OnExit;
            }
        }
'@
$new=@'
        public static void Initialize()
        {
            // Kept for compatibility with older callers. Recovery is now embedded in the
            // main Container Vanilla tab and uses the original 4RTools tray icon.
        }
'@
if(-not $t.Contains($old)){throw 'VanillaReconnectBootstrap.Initialize anchor missing'}
$t=$t.Replace($old,$new);Write $p $t

# Self-update helper runs before normal initialization. Normal startup migrates legacy data before profiles are loaded.
$p='Program.cs';$t=Read $p
$old='        static void Main(string[] args)'+$nl+'        {'
$new='        static void Main(string[] args)'+$nl+'        {'+$nl+'            if (VanillaUpdater.TryHandleApplyCommand(args)) return;'+$nl+'            VanillaAppData.InitializeAndMigrateLegacy(AppDomain.CurrentDomain.BaseDirectory);'
if(-not $t.Contains($old)){throw 'Program Main anchor missing'}
$t=$t.Replace($old,$new)
$t=$t.Replace('Success = true, Version = "0.5.0", PointerBytes = IntPtr.Size,','Success = true, Version = VanillaUpdater.CurrentVersionText, PointerBytes = IntPtr.Size,')
Write $p $t

# Conservative patch release.
$p='Properties/AssemblyInfo.cs';$t=Read $p
$t=$t.Replace('AssemblyVersion("0.6.0.0")','AssemblyVersion("0.6.1.0")').Replace('AssemblyFileVersion("0.6.0.0")','AssemblyFileVersion("0.6.1.0")')
Write $p $t

# Portable releases must not carry user-data directories. The first 0.6.1 run migrates old folders into LocalAppData.
$p='scripts/build-release.ps1';$t=Read $p
$old=@'
New-Item -ItemType Directory -Path $stagingPath | Out-Null
foreach ($profileDirectory in @('Profile', 'Profiles')) {
    New-Item -ItemType Directory -Path (Join-Path $stagingPath $profileDirectory) | Out-Null
}
'@
$new=@'
New-Item -ItemType Directory -Path $stagingPath | Out-Null
'@
if(-not $t.Contains($old)){throw 'build-release profile directories anchor missing'}
$t=$t.Replace($old,$new)
$old='    $smokeReleasePath = Join-Path $smokeRoot $releaseName'
$new=$old+$nl+'    foreach ($legacyDataFolder in @(''Profile'', ''Profiles'', ''VanillaReconnect'', ''Logs'')) {'+$nl+'        if (Test-Path -LiteralPath (Join-Path $smokeReleasePath $legacyDataFolder)) { throw "Portable release must not contain user-data folder: $legacyDataFolder" }'+$nl+'    }'
if(-not $t.Contains($old)){throw 'smoke release path anchor missing'}
$t=$t.Replace($old,$new)
$t=$t.Replace('# Preserve previous release data, including any profiles created by a launch.','# Preserve previous release artifacts when -Replace is explicitly requested.')
Write $p $t

# Regression coverage for sibling-release migration and conservative updater versioning.
$p='Tests/VanillaReconnectRegressionTests.cs';$t=Read $p
$old='            Test("Vanilla Launcher.exe uses GAME START launcher mode", VanillaLauncherName);'
$new=$old+$nl+'            Test("Legacy version folders migrate into one persistent Profiles root", PersistentDataMigration);'+$nl+'            Test("Updater only accepts strictly newer semantic versions", UpdaterVersionComparison);'
if(-not $t.Contains($old)){throw 'Reconnect regression run anchor missing'}
$t=$t.Replace($old,$new)
$anchor='        private static string Temp()'
if(-not $t.Contains($anchor)){throw 'Reconnect regression Temp anchor missing'}
$methods=@'
        private static void PersistentDataMigration()
        {
            string root = Temp();
            string previous = Environment.GetEnvironmentVariable(VanillaAppData.DataRootEnvironmentVariable);
            try
            {
                string oldInstall = Path.Combine(root, "4RTools-Vanilla-v0.6.0");
                string newInstall = Path.Combine(root, "4RTools-Vanilla-v0.6.1");
                string data = Path.Combine(root, "user-data");
                Directory.CreateDirectory(Path.Combine(oldInstall, "Profile"));
                Directory.CreateDirectory(Path.Combine(oldInstall, "Profiles", "Vanilla"));
                Directory.CreateDirectory(Path.Combine(oldInstall, "VanillaReconnect"));
                Directory.CreateDirectory(newInstall);
                File.WriteAllText(Path.Combine(oldInstall, "Profile", "Hunter.json"), "{}");
                File.WriteAllText(Path.Combine(oldInstall, "Profiles", "Vanilla", "Farm.json"), "{}");
                File.WriteAllText(Path.Combine(oldInstall, "VanillaReconnect", "reconnect.json"), "{}");
                File.WriteAllText(Path.Combine(oldInstall, "supported_servers.json"), "[]");
                Environment.SetEnvironmentVariable(VanillaAppData.DataRootEnvironmentVariable, data);
                VanillaAppData.InitializeAndMigrateLegacy(newInstall);
                Assert(File.Exists(Path.Combine(data, "Profiles", "Stock", "Hunter.json")), "Stock profile did not migrate.");
                Assert(File.Exists(Path.Combine(data, "Profiles", "Vanilla", "Farm.json")), "Vanilla profile did not migrate.");
                Assert(File.Exists(Path.Combine(data, "VanillaReconnect", "reconnect.json")), "Reconnect settings did not migrate.");
                Assert(File.Exists(Path.Combine(data, "supported_servers.json")), "Local server settings did not migrate.");
                Assert(File.Exists(Path.Combine(oldInstall, "VanillaReconnect", "reconnect.json")), "Sibling release should remain a rollback backup.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(VanillaAppData.DataRootEnvironmentVariable, previous);
                Delete(root);
            }
        }
        private static void UpdaterVersionComparison()
        {
            Assert(VanillaUpdater.IsNewerVersion(new Version(0, 6, 2), new Version(0, 6, 1)), "Newer patch version was rejected.");
            Assert(!VanillaUpdater.IsNewerVersion(new Version(0, 6, 1), new Version(0, 6, 1)), "Equal version was treated as an update.");
            Assert(!VanillaUpdater.IsNewerVersion(new Version(0, 5, 9), new Version(0, 6, 1)), "Older version was treated as an update.");
        }
'@
$t=$t.Replace($anchor,$methods+$anchor);Write $p $t

Write 'packaging/README.txt' @'
4RTools Vanilla
==============

This is an independent extension of 4RTools, not an official upstream or
Vanilla MMO release. Copyright (c) 2022 4RTools. See LICENSE.

Vanilla's documentation lists 4R Tools as supported. This fork keeps Gepard
intact: it does not disable, bypass, patch, hide from, or interfere with
Gepard Shield. Vanilla-specific recovery uses ordinary window input and
read-only observation where applicable.

One application window
----------------------
Run 4RTools-Vanilla.exe. Vanilla is the first main feature tab and contains one
integrated workspace with Recovery & relog, Automation rules, Diagnostics, and
Data & updates. The old separate reconnect-manager window and second tray icon
are no longer used. Minimize the main 4RTools window to use its normal tray icon;
closing the main window exits the application.

Recovery quick start
--------------------
1. Set Launcher EXE to Vanilla Launcher.exe or patcher.exe.
2. Leave Proxy on Tokyo unless you intentionally use another route.
3. Configure one or two account profiles with username, password, character slot,
   and the resume hotkey used by Vanilla Autobattle.
4. Save and test the steps individually when calibrating a client/UI change.
5. Enable Auto relaunch/relogin. Enable Start supervisor with 4RTools when you
   want monitoring to begin automatically whenever 4RTools starts.

Passwords are protected with Windows DPAPI and are never written to logs. DPAPI
protection is tied to the current Windows user/machine, so passwords must still
be entered once on each PC.

Persistent configuration
------------------------
User configuration is deliberately outside the versioned application folder.
The default data root is:

  %LOCALAPPDATA%\4RTools Vanilla\

It contains one Profiles root:

  Profiles\Stock\          original 4RTools profiles
  Profiles\Vanilla\        Vanilla automation-rule profiles
  VanillaReconnect\        recovery accounts/settings (reconnect.json)
  Logs\                    reconnect/automation/update logs
  supported_servers.json   locally added original-4RTools server definitions

The release ZIP contains none of those user-data folders. On first run, 0.6.1
migrates compatible data from the current application directory and from nearby
older sibling folders named 4RTools-Vanilla-v*. Sibling version folders are kept
as rollback backups. This means you can unzip 0.6.1 beside 0.6.0 and retain the
same local configuration automatically on that Windows user/PC.

Automatic updates
-----------------
Every normal startup checks the latest GitHub Release for this fork. If a newer
release exists, 4RTools offers to download and restart into it. Before applying
an update it verifies the release ZIP SHA-256 file and every file listed in the
packaged SHA256SUMS.txt manifest. User data is outside the install directory, so
updating application files does not replace profiles, recovery accounts, or
DPAPI-protected passwords.

The Data & updates page shows the exact paths in use and provides Open Data
Folder and Check for Updates controls.

Requirements
------------
Windows 10 or Windows 11 with Microsoft .NET Framework 4.7.2 or a later 4.x
runtime. The application targets x86 and can run on x64 Windows. No Visual
Studio, Git, NuGet, SDK, or source tree is required to use it.

Release identity and integrity
------------------------------
VERSION.txt records the fork version, architecture, source commit, and build
validation. RELEASE-NOTES.md records feature and validation details.
SHA256SUMS.txt lists packaged payload hashes and the ZIP has an adjacent .sha256
file. Versioned ZIPs are published by verified GitHub Releases and are not
committed to the source tree.

The MIT license for 4RTools is in LICENSE. Embedded third-party dependencies
have their own licenses/notices in THIRD-PARTY-NOTICES.txt.
'@

Write 'RELEASE-NOTES.md' @'
# 4RTools Vanilla 0.6.1

This is deliberately a patch release while the Vanilla recovery workflow is still being calibrated live.

- Integrated the reconnect/recovery manager into the main 4RTools Vanilla tab; normal use is now one application window and the original 4RTools tray icon.
- Added a single persistent user-data root under `%LOCALAPPDATA%\4RTools Vanilla\` so profiles, recovery accounts, local server definitions, logs, and DPAPI-protected passwords survive replacing/updating the application folder.
- Merged the previous `Profile` / `Profiles` split into one persistent `Profiles` root with `Stock` and `Vanilla` subfolders.
- Added first-run migration from the current folder and sibling `4RTools-Vanilla-v*` folders, preserving sibling folders as rollback backups.
- Added startup GitHub Release checks and a checksum-verified self-updater. Updates verify both the published ZIP SHA-256 and the packaged `SHA256SUMS.txt` manifest before restart.
- Added Data & updates UI showing exact persistent paths plus manual update/data-folder controls.
- Portable release packages no longer contain user-data directories.

CI validates Release compilation, offline regression tests, packaging, and portable smoke launch. Real Vanilla/Gepard UI interaction still requires local live validation and is not claimed from CI.
'@

# Persist the user's new long-term product/release expectations in repository policy.
$p='AGENTS.md';$t=Read $p
$anchor='## Cleanliness, documentation, and final reporting'
if(-not $t.Contains($anchor)){throw 'AGENTS cleanliness anchor missing'}
$policy=@'
## Release/version and persistent-data policy

Use conservative pre-1.0 versioning while the Vanilla integration is still being
stabilized live. Prefer patch releases for iterative fixes and UX/reliability
improvements, minor releases for coherent larger milestones, and do not approach
or declare 1.0 merely because several internal changes landed. A 1.0 release
requires explicit product readiness and substantial live validation.

User configuration must survive application upgrades. Keep writable user data
outside versioned release folders under the stable per-user application-data
root, migrate compatible older schemas automatically, and preserve old data when
a safe migration cannot be proven. Release ZIPs must not contain mutable user
profile/recovery/log directories. Backwards-incompatible schema changes require
an explicit migration or a clear, intentional reset path rather than silent
corruption or accidental default replacement.

Normal end-user operation should be one 4RTools application/window. Integrate
Vanilla recovery, automation, diagnostics, data paths, and update status into the
main Vanilla workspace instead of creating competing top-level manager windows
or duplicate tray icons. The main 4RTools tray remains the application tray.

'@
$t=$t.Replace($anchor,$policy+$anchor);Write $p $t

Write-Host '0.6.1 professionalization changes applied.'

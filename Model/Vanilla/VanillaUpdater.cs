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
                Process.Start(new ProcessStartInfo { FileName = destinationExe, WorkingDirectory = install });
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
                catch (Exception ex) { last = ex; Thread.Sleep(500); }
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
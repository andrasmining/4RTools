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
            try { parent = new DirectoryInfo(current).Parent; }
            catch { yield break; }
            if (parent == null || !parent.Exists) yield break;
            DirectoryInfo[] siblings;
            try
            {
                siblings = parent.GetDirectories()
                    .Where(d => d.Name.StartsWith("4RTools-Vanilla-v", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .ToArray();
            }
            catch { yield break; }
            foreach (var sibling in siblings)
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
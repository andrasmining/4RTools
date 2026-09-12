using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace _4RTools.Model.Vanilla
{
    internal sealed class VanillaSessionLog
    {
        internal const long DefaultMaxFileBytes = 10L * 1024L * 1024L;
        internal const long DefaultMaxDirectoryBytes = 100L * 1024L * 1024L;
        private static readonly string ProcessSessionToken = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-p" + Process.GetCurrentProcess().Id;
        private readonly object gate = new object();
        private readonly string directory;
        private readonly string stem;
        private readonly long maxFileBytes;
        private readonly long maxDirectoryBytes;
        private int part = 1;
        private string currentPath;

        internal VanillaSessionLog(string baseDirectory)
            : this(baseDirectory, DefaultMaxFileBytes, DefaultMaxDirectoryBytes, ProcessSessionToken) { }

        internal VanillaSessionLog(string baseDirectory, long maxFileBytes, long maxDirectoryBytes, string sessionToken)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory)) throw new ArgumentNullException(nameof(baseDirectory));
            if (maxFileBytes < 1024) throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
            if (maxDirectoryBytes < maxFileBytes) throw new ArgumentOutOfRangeException(nameof(maxDirectoryBytes));
            if (string.IsNullOrWhiteSpace(sessionToken)) throw new ArgumentNullException(nameof(sessionToken));
            directory = Path.Combine(Path.GetFullPath(baseDirectory), "Logs");
            Directory.CreateDirectory(directory);
            ArchiveLegacyLog();
            stem = "reconnect-" + Sanitize(sessionToken);
            this.maxFileBytes = maxFileBytes;
            this.maxDirectoryBytes = maxDirectoryBytes;
            currentPath = PartPath(part);
            if (!File.Exists(currentPath))
                File.WriteAllText(currentPath, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 4RTools Vanilla session log started." + Environment.NewLine, Encoding.UTF8);
            Prune();
        }

        internal string CurrentPath { get { lock (gate) return currentPath; } }

        internal void WriteLine(string line)
        {
            if (line == null) return;
            lock (gate)
            {
                string payload = line + Environment.NewLine;
                int bytes = Encoding.UTF8.GetByteCount(payload);
                long currentLength = File.Exists(currentPath) ? new FileInfo(currentPath).Length : 0;
                if (currentLength > 0 && currentLength + bytes > maxFileBytes)
                {
                    part++;
                    currentPath = PartPath(part);
                    File.WriteAllText(currentPath, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        + " 4RTools Vanilla log rotated from previous part." + Environment.NewLine, Encoding.UTF8);
                }
                File.AppendAllText(currentPath, payload, Encoding.UTF8);
                if (part > 1 || new FileInfo(currentPath).Length > maxFileBytes / 2) Prune();
            }
        }

        private string PartPath(int value)
        {
            return Path.Combine(directory, stem + (value <= 1 ? "" : "-part" + value.ToString("00")) + ".log");
        }

        private void ArchiveLegacyLog()
        {
            string legacy = Path.Combine(directory, "reconnect.log");
            if (!File.Exists(legacy)) return;
            string stamp;
            try { stamp = File.GetLastWriteTime(legacy).ToString("yyyyMMdd-HHmmss"); }
            catch { stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss"); }
            string target = Path.Combine(directory, "reconnect-legacy-" + stamp + ".log");
            int suffix = 1;
            while (File.Exists(target)) target = Path.Combine(directory, "reconnect-legacy-" + stamp + "-" + (++suffix) + ".log");
            try { File.Move(legacy, target); } catch { }
        }

        private void Prune()
        {
            try
            {
                var files = new DirectoryInfo(directory).GetFiles("reconnect-*.log").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                long total = files.Sum(f => f.Length);
                for (int i = files.Count - 1; i >= 0 && (total > maxDirectoryBytes || files.Count > 20); i--)
                {
                    FileInfo file = files[i];
                    if (string.Equals(file.FullName, currentPath, StringComparison.OrdinalIgnoreCase)) continue;
                    long length = file.Length;
                    try { file.Delete(); total -= length; files.RemoveAt(i); } catch { }
                }
            }
            catch { }
        }

        private static string Sanitize(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
            return value.Trim();
        }
    }
}

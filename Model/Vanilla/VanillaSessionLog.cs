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
            this.maxFileBytes = maxFileBytes;
            this.maxDirectoryBytes = maxDirectoryBytes;
            ArchiveLegacyLog();
            stem = "reconnect-" + Sanitize(sessionToken);
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
                string remaining = line + Environment.NewLine;
                while (remaining.Length > 0)
                {
                    long currentLength = File.Exists(currentPath) ? new FileInfo(currentPath).Length : 0L;
                    if (currentLength >= maxFileBytes)
                    {
                        RotatePart();
                        currentLength = new FileInfo(currentPath).Length;
                    }

                    long available = maxFileBytes - currentLength;
                    int chars = VanillaLogRotation.PrefixLengthWithinBytes(remaining, available);
                    if (chars <= 0)
                    {
                        RotatePart();
                        continue;
                    }

                    string piece = remaining.Substring(0, chars);
                    File.AppendAllText(currentPath, piece, Encoding.UTF8);
                    remaining = remaining.Substring(chars);
                    if (remaining.Length > 0) RotatePart();
                }

                if (part > 1 || new FileInfo(currentPath).Length > maxFileBytes / 2) Prune();
            }
        }

        private void RotatePart()
        {
            part++;
            currentPath = PartPath(part);
            File.WriteAllText(currentPath, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")
                + " 4RTools Vanilla log rotated from previous part." + Environment.NewLine, Encoding.UTF8);
        }

        private string PartPath(int value)
        {
            return Path.Combine(directory, stem + (value <= 1 ? "" : "-part" + value.ToString("00")) + ".log");
        }

        private void ArchiveLegacyLog()
        {
            string legacy = Path.Combine(directory, "reconnect.log");
            if (!File.Exists(legacy)) return;
            try
            {
                VanillaLogRotation.StartNewSession(legacy, "reconnect-legacy",
                    maxFileBytes, maxDirectoryBytes, 20);
            }
            catch { }
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

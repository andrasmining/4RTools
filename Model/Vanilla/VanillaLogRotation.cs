using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Shared bounded file logging. Fixed-name live logs rotate into timestamped archives
    /// and no individual log file is allowed to grow beyond the configured maximum.
    /// </summary>
    internal static class VanillaLogRotation
    {
        internal const long DefaultMaxFileBytes = 10L * 1024L * 1024L;
        internal const long DefaultMaxFamilyBytes = 100L * 1024L * 1024L;
        internal const int DefaultMaxFamilyFiles = 20;

        private static readonly object Gate = new object();
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        internal static void StartNewSession(string currentPath, string archiveStem)
        {
            StartNewSession(currentPath, archiveStem, DefaultMaxFileBytes, DefaultMaxFamilyBytes, DefaultMaxFamilyFiles);
        }

        internal static void StartNewSession(string currentPath, string archiveStem,
            long maxFileBytes, long maxFamilyBytes, int maxFamilyFiles)
        {
            Validate(currentPath, archiveStem, maxFileBytes, maxFamilyBytes, maxFamilyFiles);
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(currentPath)));
                ArchiveCurrent(currentPath, archiveStem, maxFileBytes);
                Prune(currentPath, archiveStem, maxFamilyBytes, maxFamilyFiles);
            }
        }

        internal static void Append(string currentPath, string archiveStem, string text)
        {
            Append(currentPath, archiveStem, text, DefaultMaxFileBytes, DefaultMaxFamilyBytes, DefaultMaxFamilyFiles);
        }

        internal static void Append(string currentPath, string archiveStem, string text,
            long maxFileBytes, long maxFamilyBytes, int maxFamilyFiles)
        {
            if (text == null) return;
            Validate(currentPath, archiveStem, maxFileBytes, maxFamilyBytes, maxFamilyFiles);
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(currentPath)));
                string remaining = text;
                while (remaining.Length > 0)
                {
                    long length = File.Exists(currentPath) ? new FileInfo(currentPath).Length : 0L;
                    if (length >= maxFileBytes)
                    {
                        ArchiveCurrent(currentPath, archiveStem, maxFileBytes);
                        length = 0L;
                    }

                    long available = maxFileBytes - length;
                    int chars = LargestPrefixThatFits(remaining, available);
                    if (chars <= 0)
                    {
                        ArchiveCurrent(currentPath, archiveStem, maxFileBytes);
                        continue;
                    }

                    string piece = remaining.Substring(0, chars);
                    File.AppendAllText(currentPath, piece, Utf8);
                    remaining = remaining.Substring(chars);

                    if (remaining.Length > 0)
                        ArchiveCurrent(currentPath, archiveStem, maxFileBytes);
                }
                Prune(currentPath, archiveStem, maxFamilyBytes, maxFamilyFiles);
            }
        }

        private static int LargestPrefixThatFits(string text, long byteBudget)
        {
            if (string.IsNullOrEmpty(text) || byteBudget <= 0) return 0;
            if (Utf8.GetByteCount(text) <= byteBudget) return text.Length;

            int low = 1, high = text.Length, best = 0;
            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                int bytes = Utf8.GetByteCount(text.Substring(0, mid));
                if (bytes <= byteBudget)
                {
                    best = mid;
                    low = mid + 1;
                }
                else high = mid - 1;
            }

            // Do not split a UTF-16 surrogate pair.
            if (best > 0 && best < text.Length
                && char.IsHighSurrogate(text[best - 1]) && char.IsLowSurrogate(text[best]))
                best--;
            return best;
        }

        private static void ArchiveCurrent(string currentPath, string archiveStem, long maxFileBytes)
        {
            if (!File.Exists(currentPath)) return;
            var info = new FileInfo(currentPath);
            if (info.Length <= 0)
            {
                try { info.Delete(); } catch { }
                return;
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(currentPath));
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            if (info.Length <= maxFileBytes)
            {
                string target = UniqueArchivePath(directory, archiveStem, stamp, null);
                File.Move(currentPath, target);
                return;
            }

            SplitOversizedFile(currentPath, directory, archiveStem, stamp, maxFileBytes);
        }

        private static void SplitOversizedFile(string sourcePath, string directory, string archiveStem,
            string stamp, long maxFileBytes)
        {
            string tempDirectory = Path.Combine(directory, ".log-rotation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var parts = new List<string>();
            try
            {
                using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    int part = 1;
                    while (input.Position < input.Length)
                    {
                        string temp = Path.Combine(tempDirectory, "part-" + part.ToString("000") + ".tmp");
                        long remaining = Math.Min(maxFileBytes, input.Length - input.Position);
                        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[64 * 1024];
                            while (remaining > 0)
                            {
                                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                                if (read <= 0) break;
                                output.Write(buffer, 0, read);
                                remaining -= read;
                            }
                        }
                        parts.Add(temp);
                        part++;
                    }
                }

                for (int i = 0; i < parts.Count; i++)
                {
                    string target = UniqueArchivePath(directory, archiveStem, stamp, i + 1);
                    File.Move(parts[i], target);
                }
                File.Delete(sourcePath);
            }
            finally
            {
                try { if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true); } catch { }
            }
        }

        private static string UniqueArchivePath(string directory, string archiveStem, string stamp, int? part)
        {
            string suffix = part.HasValue ? "-part" + part.Value.ToString("00") : "";
            string path = Path.Combine(directory, archiveStem + "-" + stamp + suffix + ".log");
            int collision = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(directory, archiveStem + "-" + stamp + suffix
                    + "-" + (++collision).ToString("00") + ".log");
            }
            return path;
        }

        private static void Prune(string currentPath, string archiveStem, long maxFamilyBytes, int maxFamilyFiles)
        {
            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(currentPath));
                if (!Directory.Exists(directory)) return;
                string liveName = Path.GetFileName(currentPath);
                var files = new DirectoryInfo(directory).GetFiles(archiveStem + "*.log")
                    .Where(f => !string.Equals(f.Name, liveName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                long total = files.Sum(f => f.Length);
                for (int i = files.Count - 1;
                    i >= 0 && (files.Count > maxFamilyFiles || total > maxFamilyBytes);
                    i--)
                {
                    FileInfo file = files[i];
                    long length = file.Length;
                    try
                    {
                        file.Delete();
                        total -= length;
                        files.RemoveAt(i);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void Validate(string currentPath, string archiveStem,
            long maxFileBytes, long maxFamilyBytes, int maxFamilyFiles)
        {
            if (string.IsNullOrWhiteSpace(currentPath)) throw new ArgumentNullException(nameof(currentPath));
            if (string.IsNullOrWhiteSpace(archiveStem)) throw new ArgumentNullException(nameof(archiveStem));
            if (maxFileBytes < 1024) throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
            if (maxFamilyBytes < maxFileBytes) throw new ArgumentOutOfRangeException(nameof(maxFamilyBytes));
            if (maxFamilyFiles < 1) throw new ArgumentOutOfRangeException(nameof(maxFamilyFiles));
        }
    }
}

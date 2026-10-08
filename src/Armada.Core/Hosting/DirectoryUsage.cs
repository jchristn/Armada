namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Threading;

    /// <summary>
    /// Measures how much disk each item directly inside a directory uses (the Armada data directory, its backups).
    /// Symbolic links and junctions are not followed, and unreadable items are skipped.
    /// </summary>
    public static class DirectoryUsage
    {
        #region Public-Methods

        /// <summary>
        /// Measure each file and directory directly inside <paramref name="root"/>, largest first.
        /// </summary>
        /// <param name="root">Directory to measure.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entries; empty when the directory does not exist.</returns>
        public static List<DirectoryUsageEntry> Measure(string root, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(root)) throw new ArgumentNullException(nameof(root));
            List<DirectoryUsageEntry> entries = new List<DirectoryUsageEntry>();
            if (!Directory.Exists(root)) return entries;

            DirectoryInfo rootInfo = new DirectoryInfo(root);
            foreach (FileSystemInfo item in rootInfo.EnumerateFileSystemInfos())
            {
                token.ThrowIfCancellationRequested();
                DirectoryUsageEntry entry = new DirectoryUsageEntry
                {
                    Name = item.Name,
                    Path = item.FullName,
                    LastWriteUtc = item.LastWriteTimeUtc
                };

                if (item is DirectoryInfo directory && (item.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    entry.IsDirectory = true;
                    MeasureTree(directory, entry, token);
                }
                else if (item is FileInfo file)
                {
                    entry.SizeBytes = SafeLength(file);
                    entry.FileCount = 1;
                }
                else
                {
                    entry.IsDirectory = item is DirectoryInfo;
                }

                entries.Add(entry);
            }

            entries.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            return entries;
        }

        /// <summary>
        /// Format a byte count for display: 512 B, 4.2 KB, 96.0 MB, 1.3 GB.
        /// </summary>
        /// <param name="bytes">Byte count.</param>
        /// <returns>Text.</returns>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            string[] units = new string[] { "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = -1;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unit];
        }

        #endregion

        #region Private-Methods

        private static void MeasureTree(DirectoryInfo directory, DirectoryUsageEntry entry, CancellationToken token)
        {
            EnumerationOptions options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            try
            {
                foreach (FileInfo file in directory.EnumerateFiles("*", options))
                {
                    token.ThrowIfCancellationRequested();
                    entry.SizeBytes += SafeLength(file);
                    entry.FileCount++;
                }
            }
            catch (IOException)
            {
                // Directory removed while measuring: keep what was counted.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static long SafeLength(FileInfo file)
        {
            try
            {
                return file.Length;
            }
            catch (IOException)
            {
                return 0;
            }
        }

        #endregion
    }
}

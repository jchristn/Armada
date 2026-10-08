namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Writes settings files safely for an editor: the new text goes to a temporary file in the same directory and
    /// replaces the original in one rename (a crash never leaves half a file), and the previous version is kept as a
    /// timestamped backup next to it (settings.json.bak-20261007T183000Z), pruned to the newest few.
    /// </summary>
    public static class SettingsFileStore
    {
        #region Public-Members

        /// <summary>
        /// Marker between a settings file name and a backup's timestamp.
        /// </summary>
        public const string BackupMarker = ".bak-";

        /// <summary>
        /// Default number of backups kept per file.
        /// </summary>
        public const int DefaultBackupsKept = 5;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write <paramref name="content"/> to <paramref name="path"/> atomically, keeping the previous version as a
        /// backup.
        /// </summary>
        /// <param name="path">Settings file.</param>
        /// <param name="content">New content (UTF-8, no byte order mark).</param>
        /// <param name="backupsKept">Backups to keep (0 keeps none).</param>
        /// <param name="nowUtc">Timestamp for the backup name; null uses the current time.</param>
        /// <returns>The backup written, or null when there was no previous file, backups are off, or the file already
        /// held exactly this content (then nothing is written).</returns>
        /// <exception cref="IOException">The file could not be written.</exception>
        public static string? Save(string path, string content, int backupsKept = DefaultBackupsKept, DateTime? nowUtc = null)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (backupsKept < 0) throw new ArgumentOutOfRangeException(nameof(backupsKept));

            string full = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(full) ?? throw new IOException("No directory for " + full);
            Directory.CreateDirectory(directory);

            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            if (File.Exists(full) && ContentEquals(full, bytes)) return null;

            string temp = Path.Combine(directory, "." + Path.GetFileName(full) + ".tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                string? backup = null;
                if (File.Exists(full) && backupsKept > 0)
                {
                    backup = BackupPath(full, nowUtc ?? DateTime.UtcNow);
                    File.Copy(full, backup, true);
                }

                File.Move(temp, full, true);
                if (backupsKept > 0) Prune(full, backupsKept);
                return backup;
            }
            finally
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>
        /// Write <paramref name="content"/> over <paramref name="path"/> in place, keeping the previous version as a
        /// backup. Use this instead of <see cref="Save"/> for a file that may be a single-file bind mount (Docker), which
        /// cannot be replaced by a rename. Nothing is written when the file already holds exactly this content.
        /// </summary>
        /// <param name="path">Settings file.</param>
        /// <param name="content">New content (UTF-8, no byte order mark).</param>
        /// <param name="backupsKept">Backups to keep (0 keeps none).</param>
        /// <param name="nowUtc">Timestamp for the backup name; null uses the current time.</param>
        /// <returns>True when the file was written; false when it already held this content.</returns>
        /// <exception cref="IOException">The file could not be written.</exception>
        public static bool SaveInPlace(string path, string content, int backupsKept = DefaultBackupsKept, DateTime? nowUtc = null)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (backupsKept < 0) throw new ArgumentOutOfRangeException(nameof(backupsKept));

            string full = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(full) ?? throw new IOException("No directory for " + full);
            Directory.CreateDirectory(directory);

            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            bool exists = File.Exists(full);
            if (exists && ContentEquals(full, bytes)) return false;

            if (exists && backupsKept > 0) File.Copy(full, BackupPath(full, nowUtc ?? DateTime.UtcNow), true);

            using (FileStream stream = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (exists && backupsKept > 0) Prune(full, backupsKept);
            return true;
        }

        /// <summary>
        /// The backups of a settings file, newest first.
        /// </summary>
        /// <param name="path">Settings file.</param>
        /// <returns>Backup paths.</returns>
        public static List<string> ListBackups(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            string full = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(full);
            List<string> backups = new List<string>();
            if (String.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return backups;

            string prefix = Path.GetFileName(full) + BackupMarker;
            foreach (string file in Directory.EnumerateFiles(directory, prefix + "*"))
            {
                if (Path.GetFileName(file).StartsWith(prefix, StringComparison.Ordinal)) backups.Add(file);
            }

            // The timestamp sorts lexically; newest first.
            backups.Sort((a, b) => String.CompareOrdinal(b, a));
            return backups;
        }

        /// <summary>
        /// The backups of a settings file with their times and sizes, newest first.
        /// </summary>
        /// <param name="path">Settings file.</param>
        /// <returns>Backups.</returns>
        public static List<SettingsBackupEntry> ListBackupEntries(string path)
        {
            List<SettingsBackupEntry> entries = new List<SettingsBackupEntry>();
            string prefix = Path.GetFileName(Path.GetFullPath(path)) + BackupMarker;
            foreach (string backup in ListBackups(path))
            {
                try
                {
                    FileInfo info = new FileInfo(backup);
                    if (!info.Exists) continue;
                    string stamp = info.Name.Substring(prefix.Length);
                    DateTime taken;
                    if (!DateTime.TryParseExact(stamp, "yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out taken))
                    {
                        taken = info.LastWriteTimeUtc;
                    }

                    entries.Add(new SettingsBackupEntry
                    {
                        Path = info.FullName,
                        Name = info.Name,
                        TakenUtc = taken,
                        SizeBytes = info.Length
                    });
                }
                catch (IOException)
                {
                    // Pruned between listing and stat.
                }
            }

            return entries;
        }

        /// <summary>
        /// Put a backup back in place of the settings file. The restore is an ordinary <see cref="Save"/>: the current
        /// file is kept as a new backup first, so a restore can itself be undone.
        /// </summary>
        /// <param name="path">Settings file.</param>
        /// <param name="backupPath">One of the file's backups (see <see cref="ListBackups"/>).</param>
        /// <param name="backupsKept">Backups to keep.</param>
        /// <param name="nowUtc">Timestamp for the new backup's name; null uses the current time.</param>
        /// <returns>The backup of the replaced version, or null when there was none (or it already matched).</returns>
        /// <exception cref="ArgumentException">The backup is not one of this file's backups.</exception>
        /// <exception cref="IOException">The backup could not be read or the file could not be written.</exception>
        public static string? Restore(string path, string backupPath, int backupsKept = DefaultBackupsKept, DateTime? nowUtc = null)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (String.IsNullOrWhiteSpace(backupPath)) throw new ArgumentNullException(nameof(backupPath));

            string backup = Path.GetFullPath(backupPath);
            bool known = false;
            foreach (string candidate in ListBackups(path))
            {
                if (String.Equals(Path.GetFullPath(candidate), backup, StringComparison.Ordinal))
                {
                    known = true;
                    break;
                }
            }

            if (!known) throw new ArgumentException(backupPath + " is not a backup of " + path + ".", nameof(backupPath));

            string content = File.ReadAllText(backup, new UTF8Encoding(false));
            return Save(path, content, backupsKept, nowUtc);
        }

        /// <summary>
        /// The backup name for a file at a time: file.bak-yyyyMMddTHHmmssfffZ.
        /// </summary>
        /// <param name="path">Settings file.</param>
        /// <param name="utc">Timestamp.</param>
        /// <returns>Backup path.</returns>
        public static string BackupPath(string path, DateTime utc)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            return path + BackupMarker + utc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        }

        #endregion

        #region Private-Methods

        private static bool ContentEquals(string path, byte[] bytes)
        {
            try
            {
                byte[] existing = File.ReadAllBytes(path);
                return existing.AsSpan().SequenceEqual(bytes);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void Prune(string path, int keep)
        {
            List<string> backups = ListBackups(path);
            for (int i = keep; i < backups.Count; i++)
            {
                try
                {
                    File.Delete(backups[i]);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        #endregion
    }
}

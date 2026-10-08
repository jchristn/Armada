namespace Armada.Core.Hosting
{
    using System;

    /// <summary>
    /// One kept previous version of a settings file (see <see cref="SettingsFileStore"/>).
    /// </summary>
    public class SettingsBackupEntry
    {
        #region Public-Members

        /// <summary>
        /// Full path of the backup.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// File name of the backup.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// When the version was replaced (the time in the backup's name), UTC; the file's last write time when the name
        /// carries no readable time.
        /// </summary>
        public DateTime TakenUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        #endregion
    }
}

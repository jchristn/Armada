namespace Armada.Core.Hosting
{
    using System;

    /// <summary>
    /// Disk usage of one item directly inside a measured directory.
    /// </summary>
    public class DirectoryUsageEntry
    {
        #region Public-Members

        /// <summary>
        /// Item name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Full path.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// True for a directory.
        /// </summary>
        public bool IsDirectory { get; set; } = false;

        /// <summary>
        /// Total size in bytes (for a directory, of every file under it).
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Number of files (1 for a file).
        /// </summary>
        public long FileCount { get; set; } = 0;

        /// <summary>
        /// Last write time, UTC.
        /// </summary>
        public DateTime LastWriteUtc { get; set; } = DateTime.MinValue;

        #endregion
    }
}

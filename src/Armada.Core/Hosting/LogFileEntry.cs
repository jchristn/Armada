namespace Armada.Core.Hosting
{
    using System;

    /// <summary>
    /// One log file found under the Admiral's log directory.
    /// </summary>
    public class LogFileEntry
    {
        #region Public-Members

        /// <summary>
        /// Category the file belongs to.
        /// </summary>
        public LogCategoryEnum Category { get; set; } = LogCategoryEnum.Admiral;

        /// <summary>
        /// Full path.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// File name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Last write time, UTC.
        /// </summary>
        public DateTime LastWriteUtc { get; set; } = DateTime.MinValue;

        #endregion
    }
}

namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A mission or captain log tail.
    /// </summary>
    public class LogResult
    {
        #region Public-Members

        /// <summary>
        /// Log text.
        /// </summary>
        public string Log { get; set; } = "";

        /// <summary>
        /// Number of lines returned.
        /// </summary>
        public int Lines { get; set; } = 0;

        /// <summary>
        /// Total lines in the log.
        /// </summary>
        public int TotalLines { get; set; } = 0;

        /// <summary>
        /// Formatted entries when requested, or null.
        /// </summary>
        public List<FormattedLogEntry>? Entries { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public LogResult()
        {
        }

        #endregion
    }
}

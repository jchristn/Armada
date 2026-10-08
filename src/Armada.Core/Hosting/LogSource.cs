namespace Armada.Core.Hosting
{
    using System;

    /// <summary>
    /// One group of log files offered by the log browser (see <see cref="LogSourceCatalog"/>).
    /// </summary>
    public class LogSource
    {
        #region Public-Members

        /// <summary>
        /// Kind of source.
        /// </summary>
        public LogSourceEnum Kind { get; set; } = LogSourceEnum.Harbor;

        /// <summary>
        /// The Admiral's log group, for <see cref="LogSourceEnum.Admiral"/>; null otherwise.
        /// </summary>
        public LogCategoryEnum? AdmiralCategory { get; set; } = null;

        /// <summary>
        /// Name shown to the operator, for example "Harbor" or "Admiral: Missions".
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Directory holding the files.
        /// </summary>
        public string Directory { get; set; } = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The label (shown by list controls).
        /// </summary>
        /// <returns>Label.</returns>
        public override string ToString()
        {
            return Label;
        }

        #endregion
    }
}

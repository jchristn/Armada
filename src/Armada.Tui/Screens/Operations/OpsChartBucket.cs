namespace Armada.Tui.Screens.Operations
{
    using System;

    /// <summary>
    /// One bucket of an <see cref="OpsStackedBarChart"/>.
    /// </summary>
    public class OpsChartBucket
    {
        #region Public-Members

        /// <summary>
        /// X label.
        /// </summary>
        public string Label { get; set; } = "";

        /// <summary>
        /// Bucket start (UTC).
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Complete count.
        /// </summary>
        public int Complete { get; set; } = 0;

        /// <summary>
        /// Failed count.
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Other count.
        /// </summary>
        public int Other { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public OpsChartBucket()
        {
        }

        #endregion
    }
}

namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// A single model endpoint health-check probe: when it ran and whether it succeeded. A rolling series of
    /// these drives the health-history bar in the dashboard.
    /// </summary>
    public class ModelEndpointHealthRecord
    {
        #region Public-Members

        /// <summary>
        /// When the probe ran (UTC).
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the probe succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelEndpointHealthRecord()
        {
        }

        #endregion
    }
}

namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// The newest speed measurement of a model profile (RS-8.8), written by Armada when a probe run's Speed probe
    /// measures an output rate. Server-owned: a profile update cannot set it. Every value is null until a measurement
    /// exists.
    /// </summary>
    public class ModelProfileSpeed
    {
        #region Public-Members

        /// <summary>
        /// The slowest measured output rate in tokens per second, or null. A value that is not a positive number reads
        /// as null.
        /// </summary>
        public double? TokensPerSecond
        {
            get => _TokensPerSecond;
            set => _TokensPerSecond = value.HasValue && (Double.IsNaN(value.Value) || Double.IsInfinity(value.Value) || value.Value <= 0) ? null : value;
        }

        /// <summary>
        /// The slowest measured time to the first output in milliseconds, or null. A negative value reads as null.
        /// </summary>
        public long? FirstOutputMs
        {
            get => _FirstOutputMs;
            set => _FirstOutputMs = value.HasValue && value.Value < 0 ? null : value;
        }

        /// <summary>
        /// When it was measured, in UTC, or null.
        /// </summary>
        public DateTime? MeasuredUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private double? _TokensPerSecond = null;
        private long? _FirstOutputMs = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileSpeed()
        {
        }

        #endregion
    }
}

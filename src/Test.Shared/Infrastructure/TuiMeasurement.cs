namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One measurement captured from the <c>Armada.Tui</c> meter by <see cref="TuiMeasurementCapture"/>.
    /// </summary>
    public sealed class TuiMeasurement
    {
        #region Public-Members

        /// <summary>
        /// Instrument name.
        /// </summary>
        public string Instrument { get; }

        /// <summary>
        /// Measured value (counters and histograms alike, as a double).
        /// </summary>
        public double Value { get; }

        /// <summary>
        /// Tags rendered as strings. Never null.
        /// </summary>
        public Dictionary<string, string?> Tags { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="value">Value.</param>
        /// <param name="tags">Tags.</param>
        public TuiMeasurement(string instrument, double value, Dictionary<string, string?> tags)
        {
            Instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));
            Value = value;
            Tags = tags ?? new Dictionary<string, string?>();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A tag value, or null when absent.
        /// </summary>
        /// <param name="key">Tag key.</param>
        /// <returns>Value or null.</returns>
        public string? Tag(string key)
        {
            return Tags.TryGetValue(key, out string? value) ? value : null;
        }

        #endregion
    }
}

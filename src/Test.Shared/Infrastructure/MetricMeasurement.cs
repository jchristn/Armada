namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One measurement a test's meter listener observed: the instrument name, the value, and the tags as strings.
    /// </summary>
    public sealed class MetricMeasurement
    {
        /// <summary>
        /// Instrument name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Measured value.
        /// </summary>
        public double Value { get; }

        /// <summary>
        /// Tags, with null values as empty strings.
        /// </summary>
        public Dictionary<string, string> Tags { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Instrument name.</param>
        /// <param name="value">Measured value.</param>
        /// <param name="tags">Measurement tags.</param>
        public MetricMeasurement(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Name = name ?? "";
            Value = value;
            foreach (KeyValuePair<string, object?> tag in tags) Tags[tag.Key] = tag.Value?.ToString() ?? "";
        }
    }
}

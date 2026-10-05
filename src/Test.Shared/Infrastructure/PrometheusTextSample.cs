namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One sample line of the Prometheus text exposition format: metric name, labels, and value text.
    /// </summary>
    public class PrometheusTextSample
    {
        #region Public-Members

        /// <summary>
        /// Metric name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Labels (unescaped values).
        /// </summary>
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Value as written.
        /// </summary>
        public string Value { get; set; } = "";

        #endregion
    }
}

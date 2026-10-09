namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Outcome of a <see cref="SqliteStarvationProbe"/> run: for each target write, how many background writes both
    /// started after it and finished before it (overtakes), and the latencies of targets and background writes.
    /// </summary>
    public sealed class SqliteStarvationResult
    {
        #region Public-Members

        /// <summary>
        /// Overtakes of each target write, in trial order.
        /// </summary>
        public List<int> Overtakes { get; } = new List<int>();

        /// <summary>
        /// Latency of each target write in milliseconds, in trial order.
        /// </summary>
        public List<double> TargetLatenciesMs { get; } = new List<double>();

        /// <summary>
        /// Number of background writes completed during the run.
        /// </summary>
        public int WriterCount { get; set; } = 0;

        /// <summary>
        /// Slowest background write, in milliseconds.
        /// </summary>
        public double MaxWriterLatencyMs { get; set; } = 0;

        /// <summary>
        /// Median overtakes across trials.
        /// </summary>
        public int MedianOvertakes
        {
            get
            {
                if (Overtakes.Count == 0) return 0;
                List<int> sorted = Overtakes.OrderBy(o => o).ToList();
                return sorted[(sorted.Count - 1) / 2];
            }
        }

        /// <summary>
        /// Slowest target write, in milliseconds.
        /// </summary>
        public double MaxTargetLatencyMs
        {
            get { return TargetLatenciesMs.Count == 0 ? 0 : TargetLatenciesMs.Max(); }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// One-line description for assertion messages.
        /// </summary>
        /// <returns>Description.</returns>
        public string Describe()
        {
            return "overtakes [" + String.Join(",", Overtakes) + "], target ms ["
                + String.Join(",", TargetLatenciesMs.Select(l => l.ToString("F1"))) + "], "
                + WriterCount + " background writes, slowest " + MaxWriterLatencyMs.ToString("F1") + " ms";
        }

        #endregion
    }
}

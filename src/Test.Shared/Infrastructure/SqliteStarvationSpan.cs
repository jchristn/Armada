namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// One write measured by <see cref="SqliteStarvationProbe"/>: sequence stamps taken just before it started and
    /// just after it finished (from one shared counter, so spans of different writers can be ordered), and its latency.
    /// </summary>
    public sealed class SqliteStarvationSpan
    {
        #region Public-Members

        /// <summary>
        /// Sequence stamp taken before the write was issued.
        /// </summary>
        public long Start { get; }

        /// <summary>
        /// Sequence stamp taken after the write completed.
        /// </summary>
        public long End { get; }

        /// <summary>
        /// Wall-clock latency of the write, in milliseconds.
        /// </summary>
        public double LatencyMs { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="start">Start stamp.</param>
        /// <param name="end">End stamp.</param>
        /// <param name="latencyMs">Latency in milliseconds.</param>
        public SqliteStarvationSpan(long start, long end, double latencyMs)
        {
            Start = start;
            End = end;
            LatencyMs = latencyMs;
        }

        #endregion
    }
}

namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// One timing measurement from <see cref="TuiPerfProbe.Measure"/>. Immutable.
    /// </summary>
    public sealed class TuiPerfSample
    {
        #region Public-Members

        /// <summary>
        /// Mean milliseconds per run.
        /// </summary>
        public double MeanMs { get; }

        /// <summary>
        /// Slowest run in milliseconds.
        /// </summary>
        public double WorstMs { get; }

        /// <summary>
        /// Timed runs.
        /// </summary>
        public int Runs { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="meanMs">Mean milliseconds.</param>
        /// <param name="worstMs">Worst milliseconds.</param>
        /// <param name="runs">Runs.</param>
        public TuiPerfSample(double meanMs, double worstMs, int runs)
        {
            MeanMs = meanMs;
            WorstMs = worstMs;
            Runs = runs;
        }

        #endregion
    }
}

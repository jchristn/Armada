namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// One arm of the landing benchmark (serial or batched merge queue landing, RS-9.4), as written to
    /// <c>benchmarks/results/landing-batch.json</c>.
    /// </summary>
    public sealed class LandingBenchmarkArm
    {
        /// <summary>
        /// Arm name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// MergeQueueBatchSize used.
        /// </summary>
        public int BatchSize { get; set; } = 1;

        /// <summary>
        /// Entries enqueued.
        /// </summary>
        public int Entries { get; set; } = 0;

        /// <summary>
        /// Entries that landed.
        /// </summary>
        public int Landed { get; set; } = 0;

        /// <summary>
        /// Entries that failed.
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Verification runs.
        /// </summary>
        public int Checks { get; set; } = 0;

        /// <summary>
        /// Repository fetches.
        /// </summary>
        public long Fetches { get; set; } = 0;

        /// <summary>
        /// Simulated duration of one verification, in milliseconds.
        /// </summary>
        public int CheckMs { get; set; } = 0;

        /// <summary>
        /// Wall time of the processing pass, in milliseconds.
        /// </summary>
        public long ElapsedMs { get; set; } = 0;
    }
}

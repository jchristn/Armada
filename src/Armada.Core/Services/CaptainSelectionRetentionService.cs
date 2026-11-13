namespace Armada.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;

    /// <summary>
    /// Applies <c>UsageSelection.DecisionRetentionDays</c> to the selection ledger in bounded batches. Decisions with an
    /// active, unsettled or uncertain reservation, or whose mission is still pending or running, are kept regardless of age.
    /// </summary>
    public sealed class CaptainSelectionRetentionService
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Create the retention pass over the shared ledger.</summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="database"/> is null.</exception>
        public CaptainSelectionRetentionService(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Delete decisions evaluated before <paramref name="nowUtc"/> minus <paramref name="retentionDays"/>, at most
        /// <paramref name="maxBatches"/> pages of <paramref name="batchSize"/> per call so one pass cannot hold the
        /// database for long; the next pass continues where this one stopped.
        /// </summary>
        /// <param name="nowUtc">Current UTC time.</param>
        /// <param name="retentionDays">Retention in days (7 to 1825).</param>
        /// <param name="batchSize">Decisions per transaction (1 to 1000).</param>
        /// <param name="maxBatches">Upper bound on transactions per call.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Decisions deleted.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="nowUtc"/> is not UTC.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a bound is out of range.</exception>
        public async Task<int> PruneAsync(DateTime nowUtc, int retentionDays, int batchSize = 500, int maxBatches = 20, CancellationToken token = default)
        {
            if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC clock is required.", nameof(nowUtc));
            if (retentionDays < 7 || retentionDays > 1825) throw new ArgumentOutOfRangeException(nameof(retentionDays));
            if (batchSize < 1 || batchSize > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
            if (maxBatches < 1) throw new ArgumentOutOfRangeException(nameof(maxBatches));

            using CaptainSelectionStage stage = CaptainSelectionTelemetry.Stage("retention");
            DateTime cutoff = nowUtc.AddDays(-retentionDays);
            int total = 0;
            for (int batch = 0; batch < maxBatches; batch++)
            {
                int deleted = await _Database.CaptainSelections.DeleteExpiredAsync(cutoff, batchSize, token).ConfigureAwait(false);
                total += deleted;
                if (deleted < batchSize) break;
            }
            CaptainSelectionTelemetry.RetentionDeleted(total);
            stage.Complete(total > 0 ? "Deleted" : "Unchanged");
            return total;
        }

        #endregion
    }
}

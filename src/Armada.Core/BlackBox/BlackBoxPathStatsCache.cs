namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Snapshot of recovery path success rates across all tenants (the order of equally weighted paths is the only thing
    /// it changes, so nothing tenant-specific is exposed), refreshed from blackbox_recovery_outcomes by the insight worker.
    /// A path needs <see cref="BlackBoxInsightLimits.MinSettledRunsForRate"/> settled runs. Thread-safe.
    /// </summary>
    public class BlackBoxPathStatsCache : IBlackBoxPathStats
    {
        #region Public-Members

        /// <summary>
        /// Paths with a rate in the current snapshot.
        /// </summary>
        public int Count => _Rates.Count;

        /// <summary>
        /// When the snapshot was last refreshed, or null.
        /// </summary>
        public DateTime? RefreshedUtc { get; private set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private Dictionary<string, BlackBoxRecoveryStat> _Rates = new Dictionary<string, BlackBoxRecoveryStat>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public BlackBoxPathStatsCache(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool TryGetRate(string pathId, out double rate, out int settledRuns)
        {
            rate = 0;
            settledRuns = 0;
            if (String.IsNullOrEmpty(pathId)) return false;
            Dictionary<string, BlackBoxRecoveryStat> snapshot = _Rates;
            if (!snapshot.TryGetValue(pathId, out BlackBoxRecoveryStat? stat) || !stat.SuccessRate.HasValue) return false;
            rate = stat.SuccessRate.Value;
            settledRuns = (int)Math.Min(Int32.MaxValue, stat.Succeeded + stat.Failed);
            return true;
        }

        /// <summary>
        /// Reload the snapshot (the 1000 most-run paths).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task RefreshAsync(CancellationToken token = default)
        {
            EnumerationResult<BlackBoxRecoveryStat> page = await _Database.BlackBoxRecoveryOutcomes.EnumerateStatsAsync(null, null, null, null,
                new EnumerationQuery { PageNumber = 1, PageSize = BlackBoxInsightLimits.MaxCachedPathStats }, token).ConfigureAwait(false);
            Dictionary<string, BlackBoxRecoveryStat> next = new Dictionary<string, BlackBoxRecoveryStat>(StringComparer.Ordinal);
            foreach (IGrouping<string, BlackBoxRecoveryStat> g in page.Objects.GroupBy(s => s.PathId, StringComparer.Ordinal))
            {
                BlackBoxRecoveryStat merged = new BlackBoxRecoveryStat { PathId = g.Key, RuleId = g.First().RuleId };
                foreach (BlackBoxRecoveryStat s in g)
                {
                    merged.Runs += s.Runs;
                    merged.Succeeded += s.Succeeded;
                    merged.Failed += s.Failed;
                    merged.Pending += s.Pending;
                    merged.Expired += s.Expired;
                }
                long settled = merged.Succeeded + merged.Failed;
                if (settled < BlackBoxInsightLimits.MinSettledRunsForRate) continue;
                merged.SuccessRate = (double)merged.Succeeded / settled;
                next[g.Key] = merged;
            }
            _Rates = next;
            RefreshedUtc = DateTime.UtcNow;
        }

        #endregion
    }
}

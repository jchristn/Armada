namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Reads a tenant's recent stage outcomes and computes <see cref="PersonaOutcomeStats"/> for routing (RS-8.6). Results
    /// are cached per tenant and half-life for <see cref="CacheSeconds"/> seconds, so a dispatch pass never reads the
    /// outcome table more than once in that time.
    /// </summary>
    public class PersonaOutcomeStatsService
    {
        #region Public-Members

        /// <summary>
        /// Seconds a computed result is reused (300).
        /// </summary>
        public const int CacheSeconds = 300;

        /// <summary>
        /// Most days of outcomes read (120). The window is five half-lives, at most this.
        /// </summary>
        public const int MaxWindowDays = 120;

        #endregion

        #region Private-Members

        private const int PageSize = 1000;
        private const int MaxPages = 20;
        private readonly DatabaseDriver _Database;
        private readonly ConcurrentDictionary<string, CachedScores> _Cache = new ConcurrentDictionary<string, CachedScores>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">The database is null.</exception>
        public PersonaOutcomeStatsService(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The tenant's scores by <see cref="PersonaOutcomeStats.Key"/>, from the cache when fresh.
        /// </summary>
        /// <param name="tenantId">Tenant, or null for a tenantless installation.</param>
        /// <param name="halfLifeDays">Half-life in days.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Scores by key.</returns>
        public async Task<Dictionary<string, PersonaOutcomeScore>> ReadAsync(string? tenantId, int halfLifeDays, CancellationToken token = default)
        {
            string cacheKey = (tenantId ?? "") + "\n" + halfLifeDays;
            DateTime now = DateTime.UtcNow;
            if (_Cache.TryGetValue(cacheKey, out CachedScores? cached) && (now - cached.ComputedUtc).TotalSeconds < CacheSeconds) return cached.Scores;

            int days = Math.Min(MaxWindowDays, Math.Max(1, halfLifeDays) * 5);
            List<StageOutcome> outcomes = new List<StageOutcome>();
            StageOutcomeQuery query = new StageOutcomeQuery
            {
                TenantId = String.IsNullOrEmpty(tenantId) ? null : tenantId,
                IsEvaluation = false,
                FromUtc = now.AddDays(-days),
                ToUtc = now,
                PageSize = PageSize
            };
            for (int page = 1; page <= MaxPages; page++)
            {
                query.PageNumber = page;
                EnumerationResult<StageOutcome> result = await _Database.StageOutcomes.EnumerateAsync(query, token).ConfigureAwait(false);
                outcomes.AddRange(result.Objects);
                if (result.Objects.Count == 0 || outcomes.Count >= result.TotalRecords) break;
            }

            Dictionary<string, PersonaOutcomeScore> scores = PersonaOutcomeStats.Compute(outcomes, now, halfLifeDays);
            _Cache[cacheKey] = new CachedScores { ComputedUtc = now, Scores = scores };
            return scores;
        }

        /// <summary>
        /// Drop every cached result (tests, and after an administrator reclassifies outcomes).
        /// </summary>
        public void Invalidate()
        {
            _Cache.Clear();
        }

        #endregion

        #region Private-Types

        private sealed class CachedScores
        {
            public DateTime ComputedUtc { get; set; }
            public Dictionary<string, PersonaOutcomeScore> Scores { get; set; } = new Dictionary<string, PersonaOutcomeScore>(StringComparer.Ordinal);
        }

        #endregion
    }
}

namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Metrics;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Serves the Harbor metrics (GET /api/v1/harbors/{id}/metrics and the get_harbor_metrics MCP tool): reads what
    /// <see cref="HarborMetricsRecorder"/> recorded and the token usage attributed to the Harbor, and builds the series
    /// with <see cref="HarborMetricsBuilder"/>. A caller sees the metrics of any Harbor it can read (the same tenant
    /// scoping as reading the Harbor). Token usage follows the token-usage rules: an admin, a tenant admin, or the
    /// Harbor's owner sees every record on the Harbor within the tenant; another user sees only their own records.
    /// </summary>
    public class HarborMetricsService
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly HarborService _Harbors;
        private readonly HarborServerSettings _Settings;
        private readonly Func<DateTime> _Clock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="harbors">Harbor service (visibility).</param>
        /// <param name="settings">Harbor server settings, read live (the reconnect grace).</param>
        /// <param name="clock">UTC clock; defaults to <see cref="DateTime.UtcNow"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public HarborMetricsService(DatabaseDriver database, HarborService harbors, HarborServerSettings settings, Func<DateTime>? clock = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Harbors = harbors ?? throw new ArgumentNullException(nameof(harbors));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Clock = clock ?? (() => DateTime.UtcNow);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the metrics of a Harbor for a window, or null when the Harbor does not exist or the caller cannot read it.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="range">Window.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The metrics, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the auth context or Harbor id is missing.</exception>
        public async Task<HarborMetrics?> GetAsync(AuthContext auth, string harborId, HarborMetricsRangeEnum range, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));

            Harbor? harbor = await _Harbors.ReadAsync(auth, harborId, token).ConfigureAwait(false);
            if (harbor == null) return null;

            DateTime now = _Clock();
            TimeBucketLayout layout = HarborMetricsRanges.Layout(range, now);

            HarborMetricsInput input = new HarborMetricsInput
            {
                Harbor = harbor,
                Range = range,
                NowUtc = now,
                ReconnectGrace = TimeSpan.FromSeconds(_Settings.HeartbeatTimeoutSeconds),
                Jobs = await _Database.HarborJobs.EnumerateAsync(harbor.Id, layout.FromUtc, layout.ToUtc, token).ConfigureAwait(false),
                Samples = await _Database.HarborLinkSamples.EnumerateAsync(harbor.Id, layout.FromUtc, layout.ToUtc, token).ConfigureAwait(false),
                LatestSample = await _Database.HarborLinkSamples.ReadLatestAsync(harbor.Id, token).ConfigureAwait(false),
                Events = await _Database.HarborLinkEvents.EnumerateAsync(harbor.Id, layout.FromUtc, layout.ToUtc, token).ConfigureAwait(false),
                EventBeforeWindow = await _Database.HarborLinkEvents.ReadLatestBeforeAsync(harbor.Id, layout.FromUtc, token).ConfigureAwait(false),
                TokenRecords = await _Database.TokenUsage.EnumerateForSummaryAsync(BuildTokenQuery(auth, harbor, layout), token).ConfigureAwait(false)
            };

            return HarborMetricsBuilder.Build(input);
        }

        /// <summary>
        /// The token-usage query for a Harbor's metrics: the Harbor and window, scoped to what the caller may see.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="harbor">The Harbor.</param>
        /// <param name="layout">The window.</param>
        /// <returns>The query.</returns>
        public static TokenUsageQuery BuildTokenQuery(AuthContext auth, Harbor harbor, TimeBucketLayout layout)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (harbor == null) throw new ArgumentNullException(nameof(harbor));
            if (layout == null) throw new ArgumentNullException(nameof(layout));

            TokenUsageQuery query = new TokenUsageQuery
            {
                HarborId = harbor.Id,
                FromUtc = layout.FromUtc,
                ToUtc = layout.ToUtc
            };

            if (auth.IsAdmin) return query;
            query.TenantId = auth.TenantId;
            bool owner = !String.IsNullOrEmpty(auth.UserId) && String.Equals(harbor.UserId, auth.UserId, StringComparison.Ordinal);
            if (!auth.IsTenantAdmin && !owner) query.UserId = auth.UserId;
            return query;
        }

        #endregion
    }
}

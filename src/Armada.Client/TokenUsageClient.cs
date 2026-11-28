namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Token usage API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class TokenUsageClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal TokenUsageClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>getTokenUsage</c>: GET /api/v1/token-usage/summary.
        /// </summary>
        /// <param name="query">Range and filters, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getTokenUsage")]
        public Task<TokenUsageSummaryResult?> GetSummaryAsync(TokenUsageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<TokenUsageSummaryResult>("/api/v1/token-usage/summary" + ArmadaQueryString.FromObject(query), null, token);
        }

        /// <summary>
        /// POST /api/v1/token-usage/enumerate: one page of token-usage records visible to the caller.
        /// </summary>
        /// <param name="query">Paging (1-500, default 25) and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<TokenUsageRecord>?> EnumerateAsync(TokenUsageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<TokenUsageRecord>>("/api/v1/token-usage/enumerate", query ?? new TokenUsageQuery(), null, token);
        }

        /// <summary>Import tagged synthetic historical usage; requires global admin privileges and never calls a model.</summary>
        /// <param name="request">Bounded ordered batch.</param>
        /// <param name="token">Cancellation.</param>
        /// <returns>Import receipt.</returns>
        [DashboardExport("importSyntheticTokenUsage")]
        public Task<TokenUsageSyntheticResult?> ImportSyntheticAsync(TokenUsageSyntheticRequest request, CancellationToken token = default)
        {
            return _Transport.PostAsync<TokenUsageSyntheticResult>("/api/v1/token-usage/synthetic", request, null, token);
        }

        #endregion
    }
}

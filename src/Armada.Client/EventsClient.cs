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
    /// Events API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class EventsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal EventsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listEvents</c>: GET `/api/v1/events${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listEvents")]
        public Task<EnumerationResult<ArmadaEvent>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<ArmadaEvent>>($"/api/v1/events{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getEvent</c>: GET `/api/v1/events/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getEvent")]
        public Task<ArmadaEvent?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<ArmadaEvent>($"/api/v1/events/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteEventsBatch</c>: POST '/api/v1/events/delete/multiple'.
        /// </summary>
        /// <param name="ids">ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteEventsBatch")]
        public Task<BatchDeleteResult?> DeleteBatchAsync(List<string> ids, CancellationToken token = default)
        {
            return _Transport.PostAsync<BatchDeleteResult>("/api/v1/events/delete/multiple", new { Ids = ids }, null, token);
        }

        #endregion
    }
}

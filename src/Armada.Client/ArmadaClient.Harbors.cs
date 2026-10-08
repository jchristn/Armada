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
    /// Harbors API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listHarbors</c>: GET '/api/v1/harbors'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<List<Harbor>?> ListHarborsAsync(CancellationToken token = default)
        {
            return GetAsync<List<Harbor>>("/api/v1/harbors", null, token);
        }

        /// <summary>
        /// Dashboard <c>getHarbor</c>: GET `/api/v1/harbors/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Harbor?> GetHarborAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Harbor>($"/api/v1/harbors/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getHarborMetrics</c>: GET `/api/v1/harbors/${encodeURIComponent(id)}/metrics?range=${encodeURIComponent(range)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="range">Window: 1h, 24h (the default), or 7d.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<HarborMetrics?> GetHarborMetricsAsync(string id, string range = "24h", CancellationToken token = default)
        {
            return GetAsync<HarborMetrics>($"/api/v1/harbors/{E(id)}/metrics?range={E(range)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createHarbor</c>: POST '/api/v1/harbors'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Harbor?> CreateHarborAsync(Harbor data, CancellationToken token = default)
        {
            return PostAsync<Harbor>("/api/v1/harbors", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateHarbor</c>: PUT `/api/v1/harbors/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Harbor?> UpdateHarborAsync(string id, Harbor data, CancellationToken token = default)
        {
            return PutAsync<Harbor>($"/api/v1/harbors/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteHarbor</c>: DEL `/api/v1/harbors/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteHarborAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/harbors/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>enableHarbor</c>: POST `/api/v1/harbors/${encodeURIComponent(id)}/enable`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Harbor?> EnableHarborAsync(string id, CancellationToken token = default)
        {
            return PostAsync<Harbor>($"/api/v1/harbors/{E(id)}/enable", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>disableHarbor</c>: POST `/api/v1/harbors/${encodeURIComponent(id)}/disable`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Harbor?> DisableHarborAsync(string id, CancellationToken token = default)
        {
            return PostAsync<Harbor>($"/api/v1/harbors/{E(id)}/disable", null, null, token);
        }

        #endregion
    }
}

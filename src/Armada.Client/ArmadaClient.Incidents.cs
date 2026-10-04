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
    /// Incidents API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listIncidents</c>: GET `/api/v1/incidents${buildIncidentQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Incident>?> ListIncidentsAsync(IncidentQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Incident>>($"/api/v1/incidents{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateIncidents</c>: POST '/api/v1/incidents/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Incident>?> EnumerateIncidentsAsync(IncidentQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<Incident>>("/api/v1/incidents/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getIncident</c>: GET `/api/v1/incidents/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Incident?> GetIncidentAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Incident>($"/api/v1/incidents/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createIncident</c>: POST '/api/v1/incidents'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Incident?> CreateIncidentAsync(IncidentUpsertRequest data, CancellationToken token = default)
        {
            return PostAsync<Incident>("/api/v1/incidents", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateIncident</c>: PUT `/api/v1/incidents/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Incident?> UpdateIncidentAsync(string id, IncidentUpsertRequest data, CancellationToken token = default)
        {
            return PutAsync<Incident>($"/api/v1/incidents/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteIncident</c>: DEL `/api/v1/incidents/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteIncidentAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/incidents/{E(id)}", null, null, token);
        }

        #endregion
    }
}

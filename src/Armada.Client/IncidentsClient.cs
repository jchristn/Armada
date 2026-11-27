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
    public sealed class IncidentsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal IncidentsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listIncidents</c>: GET `/api/v1/incidents${buildIncidentQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listIncidents")]
        public Task<EnumerationResult<Incident>?> ListAsync(IncidentQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Incident>>($"/api/v1/incidents{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateIncidents</c>: POST '/api/v1/incidents/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateIncidents")]
        public Task<EnumerationResult<Incident>?> EnumerateAsync(IncidentQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<Incident>>("/api/v1/incidents/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getIncident</c>: GET `/api/v1/incidents/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getIncident")]
        public Task<Incident?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<Incident>($"/api/v1/incidents/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createIncident</c>: POST '/api/v1/incidents'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createIncident")]
        public Task<Incident?> CreateAsync(IncidentUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Incident>("/api/v1/incidents", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateIncident</c>: PUT `/api/v1/incidents/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateIncident")]
        public Task<Incident?> UpdateAsync(string id, IncidentUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Incident>($"/api/v1/incidents/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteIncident</c>: DEL `/api/v1/incidents/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteIncident")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/incidents/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}

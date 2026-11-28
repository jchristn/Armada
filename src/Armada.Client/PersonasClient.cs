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
    /// Personas API calls (dashboard <c>api/client.ts</c> parity).
    /// CaptainPriorities accepts Runtime ApiEndpoint with Endpoint set to an exact managed inference endpoint ID.
    /// </summary>
    public sealed class PersonasClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal PersonasClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listPersonas</c>: GET `/api/v1/personas${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listPersonas")]
        public Task<EnumerationResult<Persona>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Persona>>($"/api/v1/personas{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getPersona</c>: GET `/api/v1/personas/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getPersona")]
        public Task<Persona?> GetAsync(string name, CancellationToken token = default)
        {
            return _Transport.GetAsync<Persona>($"/api/v1/personas/{ArmadaQueryString.Escape(name)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createPersona</c>: POST '/api/v1/personas'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createPersona")]
        public Task<Persona?> CreateAsync(Persona data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Persona>("/api/v1/personas", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updatePersona</c>: PUT `/api/v1/personas/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updatePersona")]
        public Task<Persona?> UpdateAsync(string name, Persona data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Persona>($"/api/v1/personas/{ArmadaQueryString.Escape(name)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deletePersona</c>: DEL `/api/v1/personas/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deletePersona")]
        public Task DeleteAsync(string name, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/personas/{ArmadaQueryString.Escape(name)}", null, null, token);
        }

        #endregion
    }
}

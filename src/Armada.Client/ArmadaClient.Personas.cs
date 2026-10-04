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
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listPersonas</c>: GET `/api/v1/personas${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Persona>?> ListPersonasAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Persona>>($"/api/v1/personas{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getPersona</c>: GET `/api/v1/personas/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Persona?> GetPersonaAsync(string name, CancellationToken token = default)
        {
            return GetAsync<Persona>($"/api/v1/personas/{E(name)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createPersona</c>: POST '/api/v1/personas'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Persona?> CreatePersonaAsync(Persona data, CancellationToken token = default)
        {
            return PostAsync<Persona>("/api/v1/personas", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updatePersona</c>: PUT `/api/v1/personas/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Persona?> UpdatePersonaAsync(string name, Persona data, CancellationToken token = default)
        {
            return PutAsync<Persona>($"/api/v1/personas/{E(name)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deletePersona</c>: DEL `/api/v1/personas/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeletePersonaAsync(string name, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/personas/{E(name)}", null, null, token);
        }

        #endregion
    }
}

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
    /// Credentials API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class CredentialsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal CredentialsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listCredentials</c>: GET '/api/v1/credentials'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listCredentials")]
        public Task<EnumerationResult<Credential>?> ListAsync(CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Credential>>("/api/v1/credentials", null, token);
        }

        /// <summary>
        /// POST /api/v1/credentials/enumerate: one page of the credentials visible to the caller, bearer tokens redacted, with the total.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Credential>?> EnumerateAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<Credential>>("/api/v1/credentials/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>createCredential</c>: POST '/api/v1/credentials'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createCredential")]
        public Task<Credential?> CreateAsync(Credential data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Credential>("/api/v1/credentials", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateCredential</c>: PUT `/api/v1/credentials/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateCredential")]
        public Task<Credential?> UpdateAsync(string id, Credential data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Credential>($"/api/v1/credentials/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteCredential</c>: DEL `/api/v1/credentials/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteCredential")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/credentials/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}

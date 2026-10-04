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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listCredentials</c>: GET '/api/v1/credentials'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Credential>?> ListCredentialsAsync(CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Credential>>("/api/v1/credentials", null, token);
        }

        /// <summary>
        /// Dashboard <c>createCredential</c>: POST '/api/v1/credentials'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Credential?> CreateCredentialAsync(Credential data, CancellationToken token = default)
        {
            return PostAsync<Credential>("/api/v1/credentials", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateCredential</c>: PUT `/api/v1/credentials/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Credential?> UpdateCredentialAsync(string id, Credential data, CancellationToken token = default)
        {
            return PutAsync<Credential>($"/api/v1/credentials/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteCredential</c>: DEL `/api/v1/credentials/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteCredentialAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/credentials/{E(id)}", null, null, token);
        }

        #endregion
    }
}

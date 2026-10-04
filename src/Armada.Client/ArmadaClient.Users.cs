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
    /// Users API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listUsers</c>: GET '/api/v1/users'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<UserMaster>?> ListUsersAsync(CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<UserMaster>>("/api/v1/users", null, token);
        }

        /// <summary>
        /// Dashboard <c>createUser</c>: POST '/api/v1/users'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<UserMaster?> CreateUserAsync(UserUpsertRequest data, CancellationToken token = default)
        {
            return PostAsync<UserMaster>("/api/v1/users", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateUser</c>: PUT `/api/v1/users/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<UserMaster?> UpdateUserAsync(string id, UserUpsertRequest data, CancellationToken token = default)
        {
            return PutAsync<UserMaster>($"/api/v1/users/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteUser</c>: DEL `/api/v1/users/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteUserAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/users/{E(id)}", null, null, token);
        }

        #endregion
    }
}

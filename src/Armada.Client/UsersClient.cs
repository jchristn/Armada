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
    public sealed class UsersClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal UsersClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listUsers</c>: GET '/api/v1/users'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listUsers")]
        public Task<EnumerationResult<UserMaster>?> ListAsync(CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<UserMaster>>("/api/v1/users", null, token);
        }

        /// <summary>
        /// POST /api/v1/users/enumerate: one page of the users visible to the caller, redacted, with the total.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<UserMaster>?> EnumerateAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<UserMaster>>("/api/v1/users/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>createUser</c>: POST '/api/v1/users'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createUser")]
        public Task<UserMaster?> CreateAsync(UserUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<UserMaster>("/api/v1/users", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateUser</c>: PUT `/api/v1/users/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateUser")]
        public Task<UserMaster?> UpdateAsync(string id, UserUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PutAsync<UserMaster>($"/api/v1/users/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteUser</c>: DEL `/api/v1/users/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteUser")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/users/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}

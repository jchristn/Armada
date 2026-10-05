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
    /// Tenants API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listTenants</c>: GET '/api/v1/tenants'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<TenantMetadata>?> ListTenantsAsync(CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<TenantMetadata>>("/api/v1/tenants", null, token);
        }

        /// <summary>
        /// Dashboard <c>createTenant</c>: POST '/api/v1/tenants'. The server generates the seeded tenant admin's
        /// password and returns it once in <see cref="TenantCreateResult.AdminPassword"/>; pass a
        /// <see cref="TenantCreateRequest"/> with <see cref="TenantCreateRequest.AdminPassword"/> set to supply it instead
        /// (the body is serialized by its runtime type).
        /// </summary>
        /// <param name="data">Tenant, or a <see cref="TenantCreateRequest"/> carrying an admin password.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created tenant with the seeded admin's email and, when generated, its password (shown once).</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<TenantCreateResult?> CreateTenantAsync(TenantMetadata data, CancellationToken token = default)
        {
            return PostAsync<TenantCreateResult>("/api/v1/tenants", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateTenant</c>: PUT `/api/v1/tenants/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<TenantMetadata?> UpdateTenantAsync(string id, TenantMetadata data, CancellationToken token = default)
        {
            return PutAsync<TenantMetadata>($"/api/v1/tenants/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteTenant</c>: DEL `/api/v1/tenants/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteTenantAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/tenants/{E(id)}", null, null, token);
        }

        #endregion
    }
}

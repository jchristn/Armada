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
    public sealed class TenantsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal TenantsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listTenants</c>: GET '/api/v1/tenants'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listTenants")]
        public Task<EnumerationResult<TenantMetadata>?> ListAsync(CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<TenantMetadata>>("/api/v1/tenants", null, token);
        }

        /// <summary>
        /// POST /api/v1/tenants/enumerate: one page of the tenants (global admins), with the total.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<TenantMetadata>?> EnumerateAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<TenantMetadata>>("/api/v1/tenants/enumerate", query ?? new EnumerationQuery(), null, token);
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
        [DashboardExport("createTenant")]
        public Task<TenantCreateResult?> CreateAsync(TenantMetadata data, CancellationToken token = default)
        {
            return _Transport.PostAsync<TenantCreateResult>("/api/v1/tenants", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateTenant</c>: PUT `/api/v1/tenants/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateTenant")]
        public Task<TenantMetadata?> UpdateAsync(string id, TenantMetadata data, CancellationToken token = default)
        {
            return _Transport.PutAsync<TenantMetadata>($"/api/v1/tenants/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteTenant</c>: DEL `/api/v1/tenants/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteTenant")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/tenants/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}

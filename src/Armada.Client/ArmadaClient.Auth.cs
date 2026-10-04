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
    /// Auth API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>authenticate</c>: POST '/api/v1/authenticate'.
        /// </summary>
        /// <param name="req">req.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<AuthenticateResult?> AuthenticateAsync(AuthenticateRequest req, CancellationToken token = default)
        {
            return PostAsync<AuthenticateResult>("/api/v1/authenticate", req, null, token);
        }

        /// <summary>
        /// Dashboard <c>whoami</c>: GET '/api/v1/whoami'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WhoAmIResult?> WhoamiAsync(CancellationToken token = default)
        {
            return GetAsync<WhoAmIResult>("/api/v1/whoami", null, token);
        }

        /// <summary>
        /// Dashboard <c>changePassword</c>: PUT /api/v1/account/password. Required before a session that signed in with
        /// the default administrator password can call anything else.
        /// </summary>
        /// <param name="request">Current and new password.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The caller's identity after the change.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WhoAmIResult?> ChangePasswordAsync(PasswordChangeRequest request, CancellationToken token = default)
        {
            return PutAsync<WhoAmIResult>("/api/v1/account/password", request, null, token);
        }

        /// <summary>
        /// Dashboard <c>getProxySessionContext</c>: GET /proxy-api/v1/session/context. Returns null when not connected through Armada.Proxy (404) or the proxy session is not authenticated (401).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public async Task<ProxySessionContext?> GetProxySessionContextAsync(CancellationToken token = default)
        {
            try
            {
                return await GetAsync<ProxySessionContext>("/proxy-api/v1/session/context", null, token).ConfigureAwait(false);
            }
            catch (ArmadaApiException ex) when (ex.StatusCode == 404 || ex.StatusCode == 401)
            {
                return null;
            }
        }

        /// <summary>
        /// Dashboard <c>clearProxySessionInstance</c>: POST /proxy-api/v1/session/logout-instance (Switch Deployment).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task ClearProxySessionInstanceAsync(CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Post, "/proxy-api/v1/session/logout-instance", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>logoutProxy</c>: POST /proxy-api/v1/auth/logout. A 404 (not behind the proxy) is ignored.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public async Task LogoutProxyAsync(CancellationToken token = default)
        {
            try
            {
                await SendNoResultAsync(HttpMethod.Post, "/proxy-api/v1/auth/logout", null, null, token).ConfigureAwait(false);
            }
            catch (ArmadaApiException ex) when (ex.StatusCode == 404)
            {
                // Not connected through the proxy.
            }
        }

        /// <summary>
        /// Dashboard <c>lookupTenants</c>: POST '/api/v1/tenants/lookup'.
        /// </summary>
        /// <param name="email">email.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<TenantLookupResult?> LookupTenantsAsync(string email, CancellationToken token = default)
        {
            return PostAsync<TenantLookupResult>("/api/v1/tenants/lookup", new { Email = email }, null, token);
        }

        #endregion
    }
}

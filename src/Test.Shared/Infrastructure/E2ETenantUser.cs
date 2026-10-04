namespace Test.Shared.Infrastructure
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// A tenant, a user in it, and a bearer credential for that user, provisioned through the REST API by an admin
    /// client. Used by end-to-end suites that need a second, isolated identity.
    /// </summary>
    public sealed class E2ETenantUser
    {
        #region Public-Members

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// User identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Bearer token of the user's credential.
        /// </summary>
        public string BearerToken { get; set; } = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Provision a tenant (or reuse one) with a new user and credential.
        /// </summary>
        /// <param name="adminClient">Client authenticated as a global admin.</param>
        /// <param name="label">Short label used in generated names.</param>
        /// <param name="isTenantAdmin">Whether the user is a tenant admin.</param>
        /// <param name="existingTenantId">Optional existing tenant to add the user to.</param>
        /// <returns>The provisioned identity.</returns>
        public static async Task<E2ETenantUser> CreateAsync(HttpClient adminClient, string label, bool isTenantAdmin = true, string? existingTenantId = null)
        {
            if (adminClient == null) throw new ArgumentNullException(nameof(adminClient));

            string tenantId = existingTenantId ?? String.Empty;
            if (String.IsNullOrEmpty(tenantId))
            {
                string tenantName = "ws-" + label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage tenantResp = await adminClient.PostAsync("/api/v1/tenants", JsonHelper.ToJsonContent(new { Name = tenantName })).ConfigureAwait(false);
                tenantResp.EnsureSuccessStatusCode();
                TenantMetadata tenant = await JsonHelper.DeserializeAsync<TenantMetadata>(tenantResp).ConfigureAwait(false);
                tenantId = tenant.Id;
            }

            string email = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@ws.armada";
            HttpResponseMessage userResp = await adminClient.PostAsync("/api/v1/users", JsonHelper.ToJsonContent(new
            {
                TenantId = tenantId,
                Email = email,
                PasswordSha256 = UserMaster.ComputePasswordHash("testpass"),
                IsTenantAdmin = isTenantAdmin
            })).ConfigureAwait(false);
            userResp.EnsureSuccessStatusCode();
            UserMaster user = await JsonHelper.DeserializeAsync<UserMaster>(userResp).ConfigureAwait(false);

            HttpResponseMessage credResp = await adminClient.PostAsync("/api/v1/credentials", JsonHelper.ToJsonContent(new
            {
                TenantId = tenantId,
                UserId = user.Id,
                Name = label + "-cred"
            })).ConfigureAwait(false);
            credResp.EnsureSuccessStatusCode();
            Credential credential = await JsonHelper.DeserializeAsync<Credential>(credResp).ConfigureAwait(false);

            E2ETenantUser result = new E2ETenantUser();
            result.TenantId = tenantId;
            result.UserId = user.Id;
            result.BearerToken = credential.BearerToken;
            return result;
        }

        /// <summary>
        /// Create an HTTP client authenticated with this identity's bearer token.
        /// </summary>
        /// <param name="baseUrl">REST base URL.</param>
        /// <returns>A new client; the caller disposes it.</returns>
        public HttpClient CreateClient(string baseUrl)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        }

        #endregion
    }
}

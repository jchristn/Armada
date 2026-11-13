namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// CLI tool permission calls: requests (<c>/api/v1/cli-permissions/requests</c>), rules
    /// (<c>/api/v1/cli-permissions/rules</c>), and the captain and Ask thread policy overrides (dashboard
    /// <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// POST /api/v1/cli-permissions/requests/enumerate: one page of the CLI permission requests the caller may see,
        /// newest first, with the total, filtered by status, mission, thread, captain, or vessel.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<CliPermissionRequest>?> ListCliPermissionRequestsAsync(CliPermissionRequestEnumerateRequest? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<CliPermissionRequest>>("/api/v1/cli-permissions/requests/enumerate", query ?? new CliPermissionRequestEnumerateRequest(), null, token);
        }

        /// <summary>
        /// GET /api/v1/cli-permissions/requests/{id}.
        /// </summary>
        /// <param name="id">Request id (cpr_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The request.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<CliPermissionRequest?> GetCliPermissionRequestAsync(string id, CancellationToken token = default)
        {
            return GetAsync<CliPermissionRequest>($"/api/v1/cli-permissions/requests/{E(id)}", null, token);
        }

        /// <summary>
        /// POST /api/v1/cli-permissions/requests/{id}/decide: allow once, allow and remember (creates an allow rule), or
        /// deny a pending request. 403 when the caller may not decide it; 409 when it is no longer pending.
        /// </summary>
        /// <param name="id">Request id (cpr_ prefix).</param>
        /// <param name="decision">Decision.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The decided request.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="decision"/> is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<CliPermissionRequest?> DecideCliPermissionRequestAsync(string id, CliPermissionDecisionRequest decision, CancellationToken token = default)
        {
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            return PostAsync<CliPermissionRequest>($"/api/v1/cli-permissions/requests/{E(id)}/decide", decision, null, token);
        }

        /// <summary>
        /// GET /api/v1/cli-permissions/rules: rules visible to the caller (own tenant plus all-tenant rules), filtered by
        /// scope, vessel, or captain.
        /// </summary>
        /// <param name="query">Filters, or null for every visible rule.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rules.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<List<CliPermissionRule>?> ListCliPermissionRulesAsync(CliPermissionRuleQuery? query = null, CancellationToken token = default)
        {
            return EnumerateAllAsync<CliPermissionRule>($"/api/v1/cli-permissions/rules/enumerate{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateCliPermissionRules</c>: POST /api/v1/cli-permissions/rules/enumerate, one page of the rules
        /// visible to the caller with the exact total.
        /// </summary>
        /// <param name="query">Filters (scope, vessel, captain), or null.</param>
        /// <param name="pageNumber">One-based page number.</param>
        /// <param name="pageSize">Page size, 1 to 1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<CliPermissionRule>?> EnumerateCliPermissionRulesAsync(CliPermissionRuleQuery? query = null, int pageNumber = 1, int pageSize = 10, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<CliPermissionRule>>($"/api/v1/cli-permissions/rules/enumerate{ArmadaQueryString.FromObject(query)}", new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, null, token);
        }

        /// <summary>
        /// GET /api/v1/cli-permissions/rules/{id}.
        /// </summary>
        /// <param name="id">Rule id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rule.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<CliPermissionRule?> GetCliPermissionRuleAsync(string id, CancellationToken token = default)
        {
            return GetAsync<CliPermissionRule>($"/api/v1/cli-permissions/rules/{E(id)}", null, token);
        }

        /// <summary>
        /// POST /api/v1/cli-permissions/rules: create an allow or deny rule (global admin for any tenant or every
        /// tenant; tenant admin for the own tenant).
        /// </summary>
        /// <param name="rule">Rule (Pattern, Action, Scope, VesselId, CaptainId, TenantId, Description).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created rule.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<CliPermissionRule?> CreateCliPermissionRuleAsync(CliPermissionRule rule, CancellationToken token = default)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            return PostAsync<CliPermissionRule>("/api/v1/cli-permissions/rules", rule, null, token);
        }

        /// <summary>
        /// PUT /api/v1/cli-permissions/rules/{id}: change a rule's pattern, action, or description (scope and target
        /// are fixed).
        /// </summary>
        /// <param name="id">Rule id.</param>
        /// <param name="rule">Rule with the new values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated rule.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<CliPermissionRule?> UpdateCliPermissionRuleAsync(string id, CliPermissionRule rule, CancellationToken token = default)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            return PutAsync<CliPermissionRule>($"/api/v1/cli-permissions/rules/{E(id)}", rule, null, token);
        }

        /// <summary>
        /// DELETE /api/v1/cli-permissions/rules/{id}.
        /// </summary>
        /// <param name="id">Rule id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteCliPermissionRuleAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/cli-permissions/rules/{E(id)}", null, null, token);
        }

        /// <summary>
        /// PUT /api/v1/captains/{id}/cli-permission-policy: set (or clear with null) a captain's CLI tool permission
        /// policy. Global admin or tenant admin of the captain's tenant.
        /// </summary>
        /// <param name="captainId">Captain id.</param>
        /// <param name="policy">Policy, or null to inherit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated captain.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Captain?> SetCaptainCliPermissionPolicyAsync(string captainId, CliPermissionPolicyEnum? policy, CancellationToken token = default)
        {
            CliPermissionPolicyUpdateRequest body = new CliPermissionPolicyUpdateRequest();
            body.Policy = policy;
            return PutAsync<Captain>($"/api/v1/captains/{E(captainId)}/cli-permission-policy", body, null, token);
        }

        /// <summary>
        /// PUT /api/v1/ask/threads/{id}/cli-permission-policy: set (or clear with null) an Ask thread's CLI tool
        /// permission override. Owner only; Bypass requires an admin.
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="policy">Policy, or null to inherit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated thread (with its CLI permission resolution).</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<AskThread?> SetAskThreadCliPermissionPolicyAsync(string threadId, CliPermissionPolicyEnum? policy, CancellationToken token = default)
        {
            CliPermissionPolicyUpdateRequest body = new CliPermissionPolicyUpdateRequest();
            body.Policy = policy;
            return PutAsync<AskThread>($"/api/v1/ask/threads/{E(threadId)}/cli-permission-policy", body, null, token);
        }

        #endregion
    }
}

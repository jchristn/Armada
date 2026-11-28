namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Stage outcome and failure-cause rule calls: outcomes (<c>/api/v1/stage-outcomes</c>) and rules
    /// (<c>/api/v1/failure-cause-rules</c>) (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class StageOutcomesClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal StageOutcomesClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/stage-outcomes: one page of the stage outcomes visible to the caller, newest first.
        /// </summary>
        /// <param name="query">Filters and paging, or null for the first page of everything visible.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listStageOutcomes")]
        public Task<EnumerationResult<StageOutcome>?> ListAsync(StageOutcomeQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<StageOutcome>>($"/api/v1/stage-outcomes{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// POST /api/v1/stage-outcomes/enumerate: one page of the stage outcomes visible to the caller, newest first, with the total.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<StageOutcome>?> EnumerateAsync(StageOutcomeQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<StageOutcome>>("/api/v1/stage-outcomes/enumerate", query ?? new StageOutcomeQuery(), null, token);
        }

        /// <summary>
        /// GET /api/v1/stage-outcomes/{id}.
        /// </summary>
        /// <param name="id">Outcome id (sto_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The outcome.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getStageOutcome")]
        public Task<StageOutcome?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<StageOutcome>($"/api/v1/stage-outcomes/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// PUT /api/v1/stage-outcomes/{id}/cause: classify an outcome by hand, or with a null cause hand it back to the
        /// rules. 400 for cause None.
        /// </summary>
        /// <param name="id">Outcome id (sto_ prefix).</param>
        /// <param name="cause">Cause, or null to hand the outcome back to the rules.</param>
        /// <param name="note">Optional note.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated outcome.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("classifyStageOutcome")]
        public Task<StageOutcome?> ClassifyAsync(string id, FailureCauseEnum? cause, string? note = null, CancellationToken token = default)
        {
            StageOutcomeClassifyRequest body = new StageOutcomeClassifyRequest();
            body.Cause = cause;
            body.Note = note;
            return _Transport.PutAsync<StageOutcome>($"/api/v1/stage-outcomes/{ArmadaQueryString.Escape(id)}/cause", body, null, token);
        }

        /// <summary>
        /// POST /api/v1/stage-outcomes/reclassify: re-apply the rules to every outcome matching the filter (paging is
        /// ignored; manual causes are kept). Global admin or tenant admin.
        /// </summary>
        /// <param name="filter">Filter, or null for every outcome in the caller's scope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was examined and changed.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("reclassifyStageOutcomes")]
        public Task<StageOutcomeReclassifyResult?> ReclassifyAsync(StageOutcomeQuery? filter = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<StageOutcomeReclassifyResult>("/api/v1/stage-outcomes/reclassify", filter ?? new StageOutcomeQuery(), null, token);
        }

        /// <summary>
        /// POST /api/v1/stage-outcomes/{id}/rule: an unsaved failure-cause rule drafted from the outcome's cause and
        /// signals; edit it and pass it to <see cref="StageOutcomesClient.CreateFailureCauseRuleAsync"/>. 409 when the outcome has no cause.
        /// </summary>
        /// <param name="id">Outcome id (sto_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The draft.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("draftFailureCauseRule")]
        public Task<FailureCauseRule?> DraftFailureCauseRuleAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<FailureCauseRule>($"/api/v1/stage-outcomes/{ArmadaQueryString.Escape(id)}/rule", null, null, token);
        }

        /// <summary>
        /// GET /api/v1/failure-cause-rules: one page of the rules visible to the caller (own tenant plus all-tenant
        /// rules) in evaluation order.
        /// </summary>
        /// <param name="pageNumber">Page number, or null for 1.</param>
        /// <param name="pageSize">Page size (1-1000), or null for the server default.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listFailureCauseRules")]
        public Task<EnumerationResult<FailureCauseRule>?> ListFailureCauseRulesAsync(int? pageNumber = null, int? pageSize = null, CancellationToken token = default)
        {
            List<KeyValuePair<string, string?>> pairs = new List<KeyValuePair<string, string?>>();
            if (pageNumber.HasValue) pairs.Add(new KeyValuePair<string, string?>("pageNumber", pageNumber.Value.ToString(CultureInfo.InvariantCulture)));
            if (pageSize.HasValue) pairs.Add(new KeyValuePair<string, string?>("pageSize", pageSize.Value.ToString(CultureInfo.InvariantCulture)));
            return _Transport.GetAsync<EnumerationResult<FailureCauseRule>>($"/api/v1/failure-cause-rules{ArmadaQueryString.FromPairs(pairs)}", null, token);
        }

        /// <summary>
        /// POST /api/v1/failure-cause-rules/enumerate: one page of the failure-cause rules visible to the caller, in evaluation order, with the total.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<FailureCauseRule>?> EnumerateFailureCauseRulesAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<FailureCauseRule>>("/api/v1/failure-cause-rules/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// GET /api/v1/failure-cause-rules/{id}.
        /// </summary>
        /// <param name="id">Rule id (fcr_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rule.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getFailureCauseRule")]
        public Task<FailureCauseRule?> GetFailureCauseRuleAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<FailureCauseRule>($"/api/v1/failure-cause-rules/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// POST /api/v1/failure-cause-rules: create a rule (global admins may set TenantId or omit it for every tenant;
        /// anyone else creates a rule for their own tenant). 400 for an invalid rule; 409 for a duplicate name.
        /// </summary>
        /// <param name="rule">Rule (Name, Description, Priority, Enabled, Match, Cause, TenantId).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created rule.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createFailureCauseRule")]
        public Task<FailureCauseRule?> CreateFailureCauseRuleAsync(FailureCauseRule rule, CancellationToken token = default)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            return _Transport.PostAsync<FailureCauseRule>("/api/v1/failure-cause-rules", rule, null, token);
        }

        /// <summary>
        /// PUT /api/v1/failure-cause-rules/{id}: replace a rule's name, description, priority, enabled, match and cause.
        /// Rules for every tenant and built-in rules need a global admin (403).
        /// </summary>
        /// <param name="id">Rule id (fcr_ prefix).</param>
        /// <param name="rule">Rule with the new values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated rule.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateFailureCauseRule")]
        public Task<FailureCauseRule?> UpdateFailureCauseRuleAsync(string id, FailureCauseRule rule, CancellationToken token = default)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            return _Transport.PutAsync<FailureCauseRule>($"/api/v1/failure-cause-rules/{ArmadaQueryString.Escape(id)}", rule, null, token);
        }

        /// <summary>
        /// DELETE /api/v1/failure-cause-rules/{id}. A deleted built-in rule is restored when the Admiral next starts.
        /// </summary>
        /// <param name="id">Rule id (fcr_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteFailureCauseRule")]
        public Task DeleteFailureCauseRuleAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/failure-cause-rules/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}

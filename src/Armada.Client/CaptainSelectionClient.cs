namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>Typed usage-selection history and read-only previews; settings use the shared settings contract.</summary>
    public sealed class CaptainSelectionClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal CaptainSelectionClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        /// <summary>Authorize or revoke recovery for a Pending mission with an audited administrator reason and revision.</summary>
        [DashboardExport("authorizeCaptainSelectionRecovery")]
        public Task<UsageSelectionRecovery?> AuthorizeRecoveryAsync(string missionId, UsageSelectionRecoveryRequest request, CancellationToken token = default)
            => _Transport.PostAsync<UsageSelectionRecovery>("/api/v1/missions/" + Uri.EscapeDataString(missionId) + "/captain-selection/recovery", request, null, token);

        /// <summary>Read current attempt separately from the mission's latest retained decision.</summary>
        [DashboardExport("getCaptainSelection")]
        public Task<CaptainSelectionSummary?> GetAsync(string missionId, CancellationToken token = default)
            => _Transport.GetAsync<CaptainSelectionSummary>("/api/v1/missions/" + Uri.EscapeDataString(missionId) + "/captain-selection", null, token);

        /// <summary>Read a scoped decision page, with filters evaluated before counts.</summary>
        [DashboardExport("getCaptainSelectionHistory")]
        public Task<EnumerationResult<CaptainSelectionDecisionView>?> GetHistoryAsync(string missionId, CaptainSelectionHistoryQuery? query = null, CancellationToken token = default)
        {
            query ??= new CaptainSelectionHistoryQuery();
            List<string> parts = new List<string> { "pageNumber=" + query.PageNumber.ToString(CultureInfo.InvariantCulture), "pageSize=" + query.PageSize.ToString(CultureInfo.InvariantCulture) };
            if (query.Trigger != null) parts.Add("trigger=" + Uri.EscapeDataString(query.Trigger));
            if (query.Outcome != null) parts.Add("outcome=" + Uri.EscapeDataString(query.Outcome));
            if (query.FromUtc.HasValue) parts.Add("from=" + Uri.EscapeDataString(query.FromUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            if (query.ToUtc.HasValue) parts.Add("to=" + Uri.EscapeDataString(query.ToUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            return _Transport.GetAsync<EnumerationResult<CaptainSelectionDecisionView>>("/api/v1/missions/" + Uri.EscapeDataString(missionId) + "/captain-selection/history?" + String.Join("&", parts), null, token);
        }

        /// <summary>Preview current routing/evidence without captures, history writes, claims or launches.</summary>
        [DashboardExport("previewCaptainSelection")]
        public Task<CaptainSelectionPreview?> PreviewAsync(string missionId, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
            => _Transport.PostAsync<CaptainSelectionPreview>("/api/v1/missions/" + Uri.EscapeDataString(missionId) + "/captain-selection/preview?" + SelectionPaging(pageNumber, pageSize, null), new object(), null, token);

        /// <summary>Read retained immutable decision detail. Explicit cross-tenant scope requires a global administrator.</summary>
        [DashboardExport("getCaptainSelectionDecision")]
        public Task<CaptainSelectionDecisionDetail?> GetDecisionAsync(string decisionId, string? tenantId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
            => _Transport.GetAsync<CaptainSelectionDecisionDetail>("/api/v1/captain-selection-decisions/" + Uri.EscapeDataString(decisionId) + "?" + SelectionPaging(pageNumber, pageSize, tenantId), null, token);

        /// <summary>Page visible candidate explanations, excluding internal account identities.</summary>
        [DashboardExport("getCaptainSelectionCandidates")]
        public Task<EnumerationResult<CaptainSelectionCandidateView>?> GetCandidatesAsync(string decisionId, string? tenantId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
            => _Transport.GetAsync<EnumerationResult<CaptainSelectionCandidateView>>("/api/v1/captain-selection-decisions/" + Uri.EscapeDataString(decisionId) + "/candidates?" + SelectionPaging(pageNumber, pageSize, tenantId), null, token);

        private static string SelectionPaging(int pageNumber, int pageSize, string? tenantId)
            => "pageNumber=" + pageNumber.ToString(CultureInfo.InvariantCulture) + "&pageSize=" + pageSize.ToString(CultureInfo.InvariantCulture)
                + (tenantId == null ? "" : "&tenantId=" + Uri.EscapeDataString(tenantId));
    }
}

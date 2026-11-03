namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>Typed usage-selection history and read-only previews; settings use the shared settings contract.</summary>
    public partial class ArmadaClient
    {
        /// <summary>Read current attempt separately from the mission's latest retained decision.</summary>
        public Task<CaptainSelectionSummary?> GetCaptainSelectionAsync(string missionId, CancellationToken token = default)
            => GetAsync<CaptainSelectionSummary>("/api/v1/missions/" + Uri.EscapeDataString(missionId) + "/captain-selection", null, token);

        /// <summary>Read a scoped decision page, with filters evaluated before counts.</summary>
        public Task<EnumerationResult<CaptainSelectionDecisionView>?> GetCaptainSelectionHistoryAsync(string missionId, CaptainSelectionHistoryQuery? query = null, CancellationToken token = default)
        {
            query ??= new CaptainSelectionHistoryQuery();
            List<string> parts = new List<string> { "pageNumber=" + query.PageNumber.ToString(CultureInfo.InvariantCulture), "pageSize=" + query.PageSize.ToString(CultureInfo.InvariantCulture) };
            if (query.Trigger != null) parts.Add("trigger=" + Uri.EscapeDataString(query.Trigger));
            if (query.Outcome != null) parts.Add("outcome=" + Uri.EscapeDataString(query.Outcome));
            if (query.FromUtc.HasValue) parts.Add("from=" + Uri.EscapeDataString(query.FromUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            if (query.ToUtc.HasValue) parts.Add("to=" + Uri.EscapeDataString(query.ToUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            return GetAsync<EnumerationResult<CaptainSelectionDecisionView>>("/api/v1/missions/" + Uri.EscapeDataString(missionId) + "/captain-selection/history?" + String.Join("&", parts), null, token);
        }

        /// <summary>Preview current routing/evidence without captures, history writes, claims or launches.</summary>
        public Task<CaptainSelectionPreview?> PreviewCaptainSelectionAsync(string missionId, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
            => PostAsync<CaptainSelectionPreview>("/api/v1/missions/" + Uri.EscapeDataString(missionId) + "/captain-selection/preview?" + SelectionPaging(pageNumber, pageSize, null), new object(), null, token);

        /// <summary>Read retained immutable decision detail. Explicit cross-tenant scope requires a global administrator.</summary>
        public Task<CaptainSelectionDecisionDetail?> GetCaptainSelectionDecisionAsync(string decisionId, string? tenantId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
            => GetAsync<CaptainSelectionDecisionDetail>("/api/v1/captain-selection-decisions/" + Uri.EscapeDataString(decisionId) + "?" + SelectionPaging(pageNumber, pageSize, tenantId), null, token);

        /// <summary>Page visible candidate explanations, excluding internal account identities.</summary>
        public Task<EnumerationResult<CaptainSelectionCandidateView>?> GetCaptainSelectionCandidatesAsync(string decisionId, string? tenantId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
            => GetAsync<EnumerationResult<CaptainSelectionCandidateView>>("/api/v1/captain-selection-decisions/" + Uri.EscapeDataString(decisionId) + "/candidates?" + SelectionPaging(pageNumber, pageSize, tenantId), null, token);

        private static string SelectionPaging(int pageNumber, int pageSize, string? tenantId)
            => "pageNumber=" + pageNumber.ToString(CultureInfo.InvariantCulture) + "&pageSize=" + pageSize.ToString(CultureInfo.InvariantCulture)
                + (tenantId == null ? "" : "&tenantId=" + Uri.EscapeDataString(tenantId));
    }
}

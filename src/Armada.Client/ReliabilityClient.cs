namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Core.Models;

    /// <summary>
    /// Pipeline reliability calls: a mission's attempt history, the reliability summary per model and persona, and the
    /// acceptance gate reports (<c>/api/v1/missions/{id}/attempts</c>, <c>/api/v1/reliability</c>).
    /// </summary>
    public sealed class ReliabilityClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal ReliabilityClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/missions/{id}/attempts: the mission's recovery counters, escalation state and every recorded
        /// attempt, oldest first.
        /// </summary>
        /// <param name="missionId">Mission id (msn_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The attempt history.</returns>
        /// <exception cref="ArgumentNullException">Thrown when missionId is null or empty.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<MissionAttemptHistory?> GetMissionAttemptsAsync(string missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            return _Transport.GetAsync<MissionAttemptHistory>($"/api/v1/missions/{ArmadaQueryString.Escape(missionId)}/attempts", null, token);
        }

        /// <summary>
        /// GET /api/v1/reliability/summary: reliability per (model, persona) over a window, sorted and paged on the server.
        /// </summary>
        /// <param name="query">Filters, sort and paging, or null for the last 14 days sorted by attempts.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The summary.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ReliabilitySummary?> GetSummaryAsync(ReliabilitySummaryQuery? query = null, CancellationToken token = default)
        {
            ReliabilitySummaryQuery source = query ?? new ReliabilitySummaryQuery();
            List<string> parts = new List<string>();
            if (!String.IsNullOrWhiteSpace(source.TenantId)) parts.Add("tenantId=" + Uri.EscapeDataString(source.TenantId));
            if (!String.IsNullOrWhiteSpace(source.Model)) parts.Add("model=" + Uri.EscapeDataString(source.Model));
            if (!String.IsNullOrWhiteSpace(source.Persona)) parts.Add("persona=" + Uri.EscapeDataString(source.Persona));
            if (!String.IsNullOrWhiteSpace(source.VesselId)) parts.Add("vesselId=" + Uri.EscapeDataString(source.VesselId));
            if (source.IncludeEvaluation) parts.Add("includeEvaluation=true");
            if (source.FromUtc.HasValue) parts.Add("fromUtc=" + Uri.EscapeDataString(source.FromUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)));
            if (source.ToUtc.HasValue) parts.Add("toUtc=" + Uri.EscapeDataString(source.ToUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)));
            parts.Add("sort=" + source.Sort.ToString());
            parts.Add("descending=" + (source.Descending ? "true" : "false"));
            parts.Add("pageNumber=" + source.PageNumber.ToString(CultureInfo.InvariantCulture));
            parts.Add("pageSize=" + source.PageSize.ToString(CultureInfo.InvariantCulture));
            return _Transport.GetAsync<ReliabilitySummary>("/api/v1/reliability/summary?" + String.Join("&", parts), null, token);
        }

        /// <summary>
        /// POST /api/v1/reliability/gate-reports: store an acceptance gate report (global admins).
        /// </summary>
        /// <param name="report">Report.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored report with its id.</returns>
        /// <exception cref="ArgumentNullException">Thrown when report is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<GateReport?> UploadGateReportAsync(GateReport report, CancellationToken token = default)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            return _Transport.PostAsync<GateReport>("/api/v1/reliability/gate-reports", report, null, token);
        }

        /// <summary>
        /// GET /api/v1/reliability/gate-reports: stored gate reports, newest first (global and tenant admins).
        /// </summary>
        /// <param name="pageNumber">Page number, or null for 1.</param>
        /// <param name="pageSize">Page size (1 to 100), or null for 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of reports.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<GateReport>?> ListGateReportsAsync(int? pageNumber = null, int? pageSize = null, CancellationToken token = default)
        {
            List<string> parts = new List<string>();
            if (pageNumber.HasValue) parts.Add("pageNumber=" + pageNumber.Value.ToString(CultureInfo.InvariantCulture));
            if (pageSize.HasValue) parts.Add("pageSize=" + pageSize.Value.ToString(CultureInfo.InvariantCulture));
            string query = parts.Count == 0 ? String.Empty : "?" + String.Join("&", parts);
            return _Transport.GetAsync<EnumerationResult<GateReport>>("/api/v1/reliability/gate-reports" + query, null, token);
        }

        /// <summary>
        /// GET /api/v1/reliability/gate-reports/{id}: one stored gate report; "latest" returns the newest.
        /// </summary>
        /// <param name="id">Report id, or "latest".</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<GateReport?> GetGateReportAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return _Transport.GetAsync<GateReport>($"/api/v1/reliability/gate-reports/{ArmadaQueryString.Escape(id)}", null, token);
        }

        #endregion
    }
}

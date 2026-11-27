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
    /// VesselHealth API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class VesselHealthClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal VesselHealthClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>enumerateVesselHealth</c>: POST '/api/v1/vessel-health/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateVesselHealth")]
        public Task<EnumerationResult<VesselHealth>?> EnumerateAsync(VesselHealthEnumerateRequest query, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<VesselHealth>>("/api/v1/vessel-health/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselHealthSummary</c>: GET '/api/v1/vessel-health/summary'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVesselHealthSummary")]
        public Task<VesselHealthSummary?> GetSummaryAsync(CancellationToken token = default)
        {
            return _Transport.GetAsync<VesselHealthSummary>("/api/v1/vessel-health/summary", null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselHealth</c>: GET `/api/v1/vessels/${encodeURIComponent(vesselId)}/health`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVesselHealth")]
        public Task<VesselHealthDetail?> GetAsync(string vesselId, CancellationToken token = default)
        {
            return _Transport.GetAsync<VesselHealthDetail>($"/api/v1/vessels/{ArmadaQueryString.Escape(vesselId)}/health", null, token);
        }

        /// <summary>
        /// Dashboard <c>listVesselDependencies</c>: POST `/api/v1/vessels/${encodeURIComponent(vesselId)}/health/dependencies/enumerate`.
        /// One page of the vessel's outdated or vulnerable dependency rows (the health detail carries only DependencyCount).
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="query">Paging (page size 1 to 1000); null is the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listVesselDependencies")]
        public Task<EnumerationResult<VesselDependency>?> ListDependenciesAsync(string vesselId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<VesselDependency>>($"/api/v1/vessels/{ArmadaQueryString.Escape(vesselId)}/health/dependencies/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>evaluateVesselHealth</c>: POST '/api/v1/vessel-health/evaluate'. A 409 (an evaluation is already
        /// running) is returned like a success with <c>AlreadyRunning</c> set, as on the dashboard.
        /// </summary>
        /// <param name="body">body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("evaluateVesselHealth")]
        public Task<VesselHealthEvaluationStart?> EvaluateAsync(VesselHealthEvaluateRequest? body = null, CancellationToken token = default)
        {
            ArmadaRequestOptions options = new ArmadaRequestOptions();
            options.AcceptStatuses.Add(409);
            return _Transport.PostAsync<VesselHealthEvaluationStart>("/api/v1/vessel-health/evaluate", body, options, token);
        }

        /// <summary>
        /// Dashboard <c>setVesselHealthOverride</c>: PUT /api/v1/vessels/{vesselId}/health/overrides/{criterion}.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="criterion">Criterion (or Overall).</param>
        /// <param name="status">Override status.</param>
        /// <param name="note">Note, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("setVesselHealthOverride")]
        public Task<VesselHealthDetail?> SetOverrideAsync(string vesselId, VesselHealthCriterionEnum criterion, VesselHealthStatusEnum status, string? note = null, CancellationToken token = default)
        {
            return _Transport.PutAsync<VesselHealthDetail>($"/api/v1/vessels/{ArmadaQueryString.Escape(vesselId)}/health/overrides/{ArmadaQueryString.Escape(criterion.ToString())}", new VesselHealthOverrideRequest { Status = status, Note = note }, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteVesselHealthOverride</c>: DEL `/api/v1/vessels/${encodeURIComponent(vesselId)}/health/overrides/${encodeURIComponent(criterion)}`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="criterion">criterion.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteVesselHealthOverride")]
        public Task<VesselHealthDetail?> DeleteOverrideAsync(string vesselId, VesselHealthCriterionEnum criterion, CancellationToken token = default)
        {
            return _Transport.DeleteAsync<VesselHealthDetail>($"/api/v1/vessels/{ArmadaQueryString.Escape(vesselId)}/health/overrides/{ArmadaQueryString.Escape(criterion.ToString())}", null, token);
        }

        #endregion
    }
}

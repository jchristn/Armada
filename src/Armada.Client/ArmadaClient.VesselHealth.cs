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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>enumerateVesselHealth</c>: POST '/api/v1/vessel-health/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<VesselHealth>?> EnumerateVesselHealthAsync(VesselHealthEnumerateRequest query, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<VesselHealth>>("/api/v1/vessel-health/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselHealthSummary</c>: GET '/api/v1/vessel-health/summary'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselHealthSummary?> GetVesselHealthSummaryAsync(CancellationToken token = default)
        {
            return GetAsync<VesselHealthSummary>("/api/v1/vessel-health/summary", null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselHealth</c>: GET `/api/v1/vessels/${encodeURIComponent(vesselId)}/health`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselHealthDetail?> GetVesselHealthAsync(string vesselId, CancellationToken token = default)
        {
            return GetAsync<VesselHealthDetail>($"/api/v1/vessels/{E(vesselId)}/health", null, token);
        }

        /// <summary>
        /// Dashboard <c>evaluateVesselHealth</c>: POST '/api/v1/vessel-health/evaluate'.
        /// </summary>
        /// <param name="body">body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselHealthEvaluationStart?> EvaluateVesselHealthAsync(VesselHealthEvaluateRequest? body = null, CancellationToken token = default)
        {
            return PostAsync<VesselHealthEvaluationStart>("/api/v1/vessel-health/evaluate", body, null, token);
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
        public Task<VesselHealthDetail?> SetVesselHealthOverrideAsync(string vesselId, VesselHealthCriterionEnum criterion, VesselHealthStatusEnum status, string? note = null, CancellationToken token = default)
        {
            return PutAsync<VesselHealthDetail>($"/api/v1/vessels/{E(vesselId)}/health/overrides/{E(criterion.ToString())}", new VesselHealthOverrideRequest { Status = status, Note = note }, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteVesselHealthOverride</c>: DEL `/api/v1/vessels/${encodeURIComponent(vesselId)}/health/overrides/${encodeURIComponent(criterion)}`.
        /// </summary>
        /// <param name="vesselId">vesselId.</param>
        /// <param name="criterion">criterion.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselHealthDetail?> DeleteVesselHealthOverrideAsync(string vesselId, VesselHealthCriterionEnum criterion, CancellationToken token = default)
        {
            return DeleteAsync<VesselHealthDetail>($"/api/v1/vessels/{E(vesselId)}/health/overrides/{E(criterion.ToString())}", null, token);
        }

        /// <summary>
        /// Dashboard <c>listMuxEndpoints</c>: GET /api/v1/runtimes/mux/endpoints.
        /// </summary>
        /// <param name="configDirectory">Mux config directory, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<MuxEndpointListResult?> ListMuxEndpointsAsync(string? configDirectory = null, CancellationToken token = default)
        {
            return GetAsync<MuxEndpointListResult>("/api/v1/runtimes/mux/endpoints" + (String.IsNullOrEmpty(configDirectory) ? "" : "?configDirectory=" + E(configDirectory)), null, token);
        }

        /// <summary>
        /// Dashboard <c>getMuxEndpoint</c>: GET /api/v1/runtimes/mux/endpoints/{name}.
        /// </summary>
        /// <param name="name">Endpoint name.</param>
        /// <param name="configDirectory">Mux config directory, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<MuxEndpointShowResult?> GetMuxEndpointAsync(string name, string? configDirectory = null, CancellationToken token = default)
        {
            return GetAsync<MuxEndpointShowResult>($"/api/v1/runtimes/mux/endpoints/{E(name)}" + (String.IsNullOrEmpty(configDirectory) ? "" : "?configDirectory=" + E(configDirectory)), null, token);
        }

        /// <summary>
        /// Dashboard <c>restartCaptain</c>: restart a captain by deleting and recreating it with the same persisted configuration (GET, DELETE, POST).
        /// </summary>
        /// <param name="id">Captain id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public async Task<Captain?> RestartCaptainAsync(string id, CancellationToken token = default)
        {
            Captain? captain = await GetCaptainAsync(id, token).ConfigureAwait(false);
            if (captain == null) throw new ArmadaApiException("Captain not found: " + id, 404, "not_found", null, null, "GET", "/api/v1/captains/" + id, null, null);
            await DeleteCaptainAsync(id, token).ConfigureAwait(false);
            Captain replacement = new Captain();
            replacement.Name = captain.Name;
            replacement.Runtime = captain.Runtime;
            replacement.SystemInstructions = captain.SystemInstructions;
            replacement.Model = captain.Model;
            replacement.AllowedPersonas = captain.AllowedPersonas;
            replacement.PreferredPersona = captain.PreferredPersona;
            replacement.RuntimeOptionsJson = captain.RuntimeOptionsJson;
            return await CreateCaptainAsync(replacement, token).ConfigureAwait(false);
        }

        #endregion
    }
}

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
    /// Captains API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class CaptainsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal CaptainsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listCaptains</c>: GET `/api/v1/captains${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listCaptains")]
        public Task<EnumerationResult<Captain>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Captain>>($"/api/v1/captains{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getCaptain</c>: GET `/api/v1/captains/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getCaptain")]
        public Task<Captain?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<Captain>($"/api/v1/captains/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getCaptainTools</c>: GET `/api/v1/captains/${id}/tools`, the captain's tool sources and counts with
        /// the first page (10) of its tools. Read further pages with <see cref="EnumerateToolsAsync"/>.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getCaptainTools")]
        public Task<CaptainToolAccessResult?> GetToolsAsync(string id, CancellationToken token = default)
        {
            return EnumerateToolsAsync(id, 1, 10, null, false, token);
        }

        /// <summary>
        /// GET `/api/v1/captains/${id}/tools` with paging: the captain's tool sources and counts with one page of its tools.
        /// </summary>
        /// <param name="id">Captain id.</param>
        /// <param name="pageNumber">One-based page of tools.</param>
        /// <param name="pageSize">Tools per page, 1 to 1000.</param>
        /// <param name="sourceKind">Only tools of this source kind, or null for all.</param>
        /// <param name="refresh">True to probe the runtime again instead of reusing the recent snapshot.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<CaptainToolAccessResult?> EnumerateToolsAsync(string id, int pageNumber, int pageSize, CaptainToolSourceKindEnum? sourceKind = null, bool refresh = false, CancellationToken token = default)
        {
            string query = "?pageNumber=" + pageNumber + "&pageSize=" + pageSize
                + (sourceKind.HasValue ? "&sourceKind=" + sourceKind.Value : "")
                + (refresh ? "&refresh=true" : "");
            return _Transport.GetAsync<CaptainToolAccessResult>($"/api/v1/captains/{ArmadaQueryString.Escape(id)}/tools" + query, ArmadaRequestOptions.WithTimeout(120000), token);
        }

        /// <summary>
        /// Dashboard <c>createCaptain</c>: POST '/api/v1/captains'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createCaptain")]
        public Task<Captain?> CreateAsync(Captain data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Captain>("/api/v1/captains", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateCaptain</c>: PUT `/api/v1/captains/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateCaptain")]
        public Task<Captain?> UpdateAsync(string id, Captain data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Captain>($"/api/v1/captains/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteCaptain</c>: DEL `/api/v1/captains/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteCaptain")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/captains/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>getCaptainLog</c>: GET /api/v1/captains/{id}/log.
        /// </summary>
        /// <param name="id">Captain id.</param>
        /// <param name="lines">Lines to return. Default 500.</param>
        /// <param name="formatted">Return readable formatted entries. Default false.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getCaptainLog")]
        public Task<LogResult?> GetLogAsync(string id, int lines = 500, bool formatted = false, CancellationToken token = default)
        {
            return _Transport.GetAsync<LogResult>($"/api/v1/captains/{ArmadaQueryString.Escape(id)}/log?lines={Math.Clamp(lines, 1, 100000)}" + (formatted ? "&formatted=true" : ""), null, token);
        }

        /// <summary>
        /// Dashboard <c>stopCaptain</c>: POST `/api/v1/captains/${id}/stop`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("stopCaptain")]
        public Task StopAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Post, $"/api/v1/captains/{ArmadaQueryString.Escape(id)}/stop", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>unquarantineCaptain</c>: POST `/api/v1/captains/${id}/unquarantine`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The captain. When it was not quarantined the server replies <c>{ Status: "not_quarantined", CaptainId }</c>
        /// instead of the captain; the captain is then read with <see cref="GetAsync"/>.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("unquarantineCaptain")]
        public async Task<Captain?> UnquarantineAsync(string id, CancellationToken token = default)
        {
            ArmadaRawJson? raw = await _Transport.PostAsync<ArmadaRawJson>($"/api/v1/captains/{ArmadaQueryString.Escape(id)}/unquarantine", null, null, token).ConfigureAwait(false);
            if (raw == null || String.IsNullOrWhiteSpace(raw.Json)) return null;
            UnquarantineReply? reply = ArmadaJson.Deserialize<UnquarantineReply>(raw.Json);
            if (reply != null && reply.Status == "not_quarantined") return await GetAsync(reply.CaptainId ?? id, token).ConfigureAwait(false);
            return ArmadaJson.Deserialize<Captain>(raw.Json);
        }

        /// <summary>
        /// Dashboard <c>recallCaptain</c>: POST /api/v1/captains/{id}/stop. The stop route recalls the captain (kills its
        /// process and returns it to Idle); the same call as <see cref="StopAsync"/>.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("recallCaptain")]
        public Task RecallAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Post, $"/api/v1/captains/{ArmadaQueryString.Escape(id)}/stop", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>stopAllCaptains</c>: POST '/api/v1/captains/stop-all'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("stopAllCaptains")]
        public Task StopAllAsync(CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Post, "/api/v1/captains/stop-all", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>restartCaptain</c>: restart a captain by deleting and recreating it with the same persisted configuration (GET, DELETE, POST).
        /// </summary>
        /// <param name="id">Captain id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("restartCaptain")]
        public async Task<Captain?> RestartAsync(string id, CancellationToken token = default)
        {
            Captain? captain = await GetAsync(id, token).ConfigureAwait(false);
            if (captain == null) throw new ArmadaApiException("Captain not found: " + id, 404, "not_found", null, null, "GET", "/api/v1/captains/" + id, null, null);
            await DeleteAsync(id, token).ConfigureAwait(false);
            Captain replacement = new Captain();
            replacement.Name = captain.Name;
            replacement.Runtime = captain.Runtime;
            replacement.SystemInstructions = captain.SystemInstructions;
            replacement.Model = captain.Model;
            replacement.AllowedPersonas = captain.AllowedPersonas;
            replacement.PreferredPersona = captain.PreferredPersona;
            replacement.RuntimeOptionsJson = captain.RuntimeOptionsJson;
            return await CreateAsync(replacement, token).ConfigureAwait(false);
        }

        #endregion
    }
}

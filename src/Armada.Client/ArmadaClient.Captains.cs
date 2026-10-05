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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listCaptains</c>: GET `/api/v1/captains${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Captain>?> ListCaptainsAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Captain>>($"/api/v1/captains{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getCaptain</c>: GET `/api/v1/captains/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Captain?> GetCaptainAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Captain>($"/api/v1/captains/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getCaptainTools</c>: GET `/api/v1/captains/${id}/tools`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<CaptainToolAccessResult?> GetCaptainToolsAsync(string id, CancellationToken token = default)
        {
            return GetAsync<CaptainToolAccessResult>($"/api/v1/captains/{E(id)}/tools", ArmadaRequestOptions.WithTimeout(120000), token);
        }

        /// <summary>
        /// Dashboard <c>createCaptain</c>: POST '/api/v1/captains'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Captain?> CreateCaptainAsync(Captain data, CancellationToken token = default)
        {
            return PostAsync<Captain>("/api/v1/captains", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateCaptain</c>: PUT `/api/v1/captains/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Captain?> UpdateCaptainAsync(string id, Captain data, CancellationToken token = default)
        {
            return PutAsync<Captain>($"/api/v1/captains/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteCaptain</c>: DEL `/api/v1/captains/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteCaptainAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/captains/{E(id)}", null, null, token);
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
        public Task<LogResult?> GetCaptainLogAsync(string id, int lines = 500, bool formatted = false, CancellationToken token = default)
        {
            return GetAsync<LogResult>($"/api/v1/captains/{E(id)}/log?lines={Math.Clamp(lines, 1, 100000)}" + (formatted ? "&formatted=true" : ""), null, token);
        }

        /// <summary>
        /// Dashboard <c>stopCaptain</c>: POST `/api/v1/captains/${id}/stop`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task StopCaptainAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Post, $"/api/v1/captains/{E(id)}/stop", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>unquarantineCaptain</c>: POST `/api/v1/captains/${id}/unquarantine`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The captain. When it was not quarantined the server replies <c>{ Status: "not_quarantined", CaptainId }</c>
        /// instead of the captain; the captain is then read with <see cref="GetCaptainAsync"/>.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public async Task<Captain?> UnquarantineCaptainAsync(string id, CancellationToken token = default)
        {
            ArmadaRawJson? raw = await PostAsync<ArmadaRawJson>($"/api/v1/captains/{E(id)}/unquarantine", null, null, token).ConfigureAwait(false);
            if (raw == null || String.IsNullOrWhiteSpace(raw.Json)) return null;
            UnquarantineReply? reply = ArmadaJson.Deserialize<UnquarantineReply>(raw.Json);
            if (reply != null && reply.Status == "not_quarantined") return await GetCaptainAsync(reply.CaptainId ?? id, token).ConfigureAwait(false);
            return ArmadaJson.Deserialize<Captain>(raw.Json);
        }

        /// <summary>
        /// Dashboard <c>recallCaptain</c>. The dashboard posts to <c>/api/v1/captains/{id}/recall</c>, which the server does
        /// not have; the server's stop route recalls the captain (kills its process and returns it to Idle), so this calls
        /// POST /api/v1/captains/{id}/stop, the same call as <see cref="StopCaptainAsync"/>.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task RecallCaptainAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Post, $"/api/v1/captains/{E(id)}/stop", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>stopAllCaptains</c>: POST '/api/v1/captains/stop-all'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task StopAllCaptainsAsync(CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Post, "/api/v1/captains/stop-all", null, null, token);
        }

        #endregion
    }
}

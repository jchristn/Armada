namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Mission tree, wait and concurrency report calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>getVoyageTree</c>: GET `/api/v1/voyages/${id}/tree`.
        /// </summary>
        /// <param name="id">Voyage ID.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The voyage's logical missions with their stages and children.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VoyageTree?> GetVoyageTreeAsync(string id, CancellationToken token = default)
        {
            return GetAsync<VoyageTree>($"/api/v1/voyages/{E(id)}/tree", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMissionWaits</c>: GET `/api/v1/missions/${id}/waits`.
        /// </summary>
        /// <param name="id">Mission ID.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="pageNumber">Page number (default 1).</param>
        /// <param name="pageSize">Page size (1-1000, default 100).</param>
        /// <returns>One page of the mission's recorded wait intervals, oldest first.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<MissionWaitInterval>?> GetMissionWaitsAsync(string id, int pageNumber = 1, int pageSize = 100, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<MissionWaitInterval>>($"/api/v1/missions/{E(id)}/waits?pageNumber={pageNumber}&pageSize={pageSize}", null, token);
        }

        /// <summary>
        /// POST `/api/v1/missions/${id}/waits/enumerate`: one page of the mission's wait intervals with paging and order
        /// (CreatedAscending for oldest first, CreatedDescending for newest first) in the body.
        /// </summary>
        /// <param name="id">Mission ID.</param>
        /// <param name="query">Paging and order; null reads the first 100, oldest first.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of the mission's wait intervals.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<MissionWaitInterval>?> EnumerateMissionWaitsAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<MissionWaitInterval>>($"/api/v1/missions/{E(id)}/waits/enumerate",
                query ?? new EnumerationQuery { PageSize = 100, Order = Armada.Core.Enums.EnumerationOrderEnum.CreatedAscending }, null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselConcurrency</c>: GET `/api/v1/vessels/${id}/concurrency`.
        /// </summary>
        /// <param name="id">Vessel ID.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The vessel's slots, waiting missions and recent slot wait.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselConcurrency?> GetVesselConcurrencyAsync(string id, CancellationToken token = default)
        {
            return GetAsync<VesselConcurrency>($"/api/v1/vessels/{E(id)}/concurrency", null, token);
        }

        /// <summary>
        /// Dashboard <c>getConcurrencyMetrics</c>: GET `/api/v1/metrics/concurrency`.
        /// </summary>
        /// <param name="fromUtc">Window start; the server defaults to 24 hours before the end.</param>
        /// <param name="toUtc">Window end; the server defaults to now.</param>
        /// <param name="vesselId">Optional vessel filter.</param>
        /// <param name="captainId">Optional captain filter: waits of missions the captain went on to run, plus open waits of missions pinned to it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Wait aggregates over the window.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ConcurrencyMetrics?> GetConcurrencyMetricsAsync(DateTime? fromUtc = null, DateTime? toUtc = null, string? vesselId = null, string? captainId = null, CancellationToken token = default)
        {
            List<string> parts = new List<string>();
            if (fromUtc.HasValue) parts.Add("from=" + E(fromUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)));
            if (toUtc.HasValue) parts.Add("to=" + E(toUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)));
            if (!String.IsNullOrEmpty(vesselId)) parts.Add("vesselId=" + E(vesselId));
            if (!String.IsNullOrEmpty(captainId)) parts.Add("captainId=" + E(captainId));
            return GetAsync<ConcurrencyMetrics>("/api/v1/metrics/concurrency" + (parts.Count == 0 ? "" : "?" + String.Join("&", parts)), null, token);
        }

        #endregion
    }
}

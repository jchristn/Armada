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
    /// Handoff artifact calls (<c>/api/v1/missions/{id}/handoff-artifacts</c>, <c>/api/v1/handoff-artifacts/{id}</c>).
    /// </summary>
    public sealed class HandoffArtifactsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal HandoffArtifactsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/missions/{id}/handoff-artifacts: one page of a mission's handoff artifacts, newest first, without
        /// output and diff text.
        /// </summary>
        /// <param name="missionId">Mission id (msn_ prefix).</param>
        /// <param name="pageNumber">Page number, or null for the first page.</param>
        /// <param name="pageSize">Page size (1-1000), or null for the server default (25).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<HandoffArtifact>?> ListForMissionAsync(string missionId, int? pageNumber = null, int? pageSize = null, CancellationToken token = default)
        {
            List<KeyValuePair<string, string?>> pairs = new List<KeyValuePair<string, string?>>();
            if (pageNumber.HasValue) pairs.Add(new KeyValuePair<string, string?>("pageNumber", pageNumber.Value.ToString(CultureInfo.InvariantCulture)));
            if (pageSize.HasValue) pairs.Add(new KeyValuePair<string, string?>("pageSize", pageSize.Value.ToString(CultureInfo.InvariantCulture)));
            return _Transport.GetAsync<EnumerationResult<HandoffArtifact>>($"/api/v1/missions/{ArmadaQueryString.Escape(missionId)}/handoff-artifacts{ArmadaQueryString.FromPairs(pairs)}", null, token);
        }

        /// <summary>
        /// POST /api/v1/missions/{id}/handoff-artifacts/enumerate: one page of a mission's handoff artifacts, newest first.
        /// </summary>
        /// <param name="missionId">Mission id (msn_ prefix).</param>
        /// <param name="query">Paging, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<HandoffArtifact>?> EnumerateForMissionAsync(string missionId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<HandoffArtifact>>($"/api/v1/missions/{ArmadaQueryString.Escape(missionId)}/handoff-artifacts/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// GET /api/v1/handoff-artifacts/{id}: the complete artifact, with output and diff.
        /// </summary>
        /// <param name="id">Artifact id (hoa_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<HandoffArtifact?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<HandoffArtifact>($"/api/v1/handoff-artifacts/{ArmadaQueryString.Escape(id)}", null, token);
        }

        #endregion
    }
}

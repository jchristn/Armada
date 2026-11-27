namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Core.Models;

    /// <summary>
    /// Stage submission calls (<c>/api/v1/missions/{id}/submissions</c>).
    /// </summary>
    public sealed class StageSubmissionsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal StageSubmissionsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/missions/{id}/submissions: the plans, verdicts and results a mission's captains submitted through the
        /// submit tools, newest first.
        /// </summary>
        /// <param name="missionId">Mission id (msn_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The submissions.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listMissionSubmissions")]
        public Task<List<StageSubmission>?> ListForMissionAsync(string missionId, CancellationToken token = default)
        {
            return _Transport.GetAsync<List<StageSubmission>>($"/api/v1/missions/{ArmadaQueryString.Escape(missionId)}/submissions", null, token);
        }

        #endregion
    }
}

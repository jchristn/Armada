namespace Armada.Client
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Stage submission calls (<c>/api/v1/missions/{id}/submissions</c>).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// GET /api/v1/missions/{id}/submissions: the plans, verdicts and results a mission's captains submitted through the
        /// submit tools, newest first.
        /// </summary>
        /// <param name="missionId">Mission id (msn_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The submissions.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<List<StageSubmission>?> ListMissionSubmissionsAsync(string missionId, CancellationToken token = default)
        {
            return GetAsync<List<StageSubmission>>($"/api/v1/missions/{E(missionId)}/submissions", null, token);
        }

        #endregion
    }
}

namespace Armada.Client
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Core.Models;

    /// <summary>
    /// Black box prevention, forensics and operations API calls: overlap preview, vessel reservations, brief lint,
    /// configuration doctor, attempt diff, nearby successes, combined timeline, voyage cascade graph and recovery
    /// statistics.
    /// </summary>
    public sealed class BlackBoxInsightsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal BlackBoxInsightsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/vessels/{vesselId}/overlap-preview: the overlaps a mission would have on a vessel (active missions,
        /// hotspots, reservations) and its brief lint findings. Nothing is dispatched.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="request">The mission (title, description, dependsOnMissionId, excludeMissionId).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The preview.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("previewMissionOverlap")]
        public Task<MissionOverlapPreview?> PreviewMissionOverlapAsync(string vesselId, MissionOverlapPreviewRequest request, CancellationToken token = default)
        {
            return _Transport.PostAsync<MissionOverlapPreview>($"/api/v1/vessels/{ArmadaQueryString.Escape(vesselId)}/overlap-preview", request, null, token);
        }

        /// <summary>
        /// POST /api/v1/vessels/{vesselId}/reservations/enumerate: one page of the vessel's soft directory reservations.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="query">Paging (page size 1 to 1000); null is the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listVesselReservations")]
        public Task<VesselReservationPage?> ListVesselReservationsAsync(string vesselId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<VesselReservationPage>($"/api/v1/vessels/{ArmadaQueryString.Escape(vesselId)}/reservations/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// POST /api/v1/missions/lint-brief: lint a mission brief (warnings only).
        /// </summary>
        /// <param name="request">Title, description and optional vessel and dependency.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The lint result.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("lintMissionBrief")]
        public Task<BriefLintResult?> LintMissionBriefAsync(BriefLintRequest request, CancellationToken token = default)
        {
            return _Transport.PostAsync<BriefLintResult>("/api/v1/missions/lint-brief", request, null, token);
        }

        /// <summary>
        /// POST /api/v1/vessels/{vesselId}/blackbox-doctor/enumerate: one page of the vessel's recurring configuration
        /// recommendations.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="query">Window (windowDays) and paging; null is 30 days, first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVesselBlackBoxDoctor")]
        public Task<BlackBoxDoctorPage?> GetVesselDoctorAsync(string vesselId, BlackBoxDoctorRequest? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<BlackBoxDoctorPage>($"/api/v1/vessels/{ArmadaQueryString.Escape(vesselId)}/blackbox-doctor/enumerate", query ?? new BlackBoxDoctorRequest(), null, token);
        }

        /// <summary>
        /// POST /api/v1/blackbox/recovery-stats/enumerate: one page of recovery path statistics.
        /// </summary>
        /// <param name="query">Rule and vessel filters and paging; null is everything, first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getBlackBoxRecoveryStats")]
        public Task<EnumerationResult<BlackBoxRecoveryStat>?> GetRecoveryStatsAsync(BlackBoxRecoveryStatsRequest? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<BlackBoxRecoveryStat>>("/api/v1/blackbox/recovery-stats/enumerate", query ?? new BlackBoxRecoveryStatsRequest(), null, token);
        }

        /// <summary>
        /// GET /api/v1/blackbox/{entryId}/diff/{otherEntryId}: what changed between two black box entries.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="otherEntryId">Other entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The diff.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("diffBlackBoxEntries")]
        public Task<BlackBoxAttemptDiff?> DiffEntriesAsync(string entryId, string otherEntryId, CancellationToken token = default)
        {
            return _Transport.GetAsync<BlackBoxAttemptDiff>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/diff/{ArmadaQueryString.Escape(otherEntryId)}", null, token);
        }

        /// <summary>
        /// POST /api/v1/blackbox/{entryId}/nearby-successes/enumerate: one page of the successful landings near a failure.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="query">Paging; null is the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listBlackBoxNearbySuccesses")]
        public Task<EnumerationResult<BlackBoxNearbySuccess>?> ListNearbySuccessesAsync(string entryId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<BlackBoxNearbySuccess>>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/nearby-successes/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// POST /api/v1/blackbox/{entryId}/timeline/enumerate: one page of an entry's combined timeline, in the
        /// query's Order (a null query is oldest first).
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="query">Order and paging; null is oldest first, first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getBlackBoxTimeline")]
        public Task<BlackBoxTimelinePage?> GetTimelineAsync(string entryId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            EnumerationQuery q = query ?? new EnumerationQuery { Order = Armada.Core.Enums.EnumerationOrderEnum.CreatedAscending };
            return _Transport.PostAsync<BlackBoxTimelinePage>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/timeline/enumerate", q, null, token);
        }

        /// <summary>
        /// GET /api/v1/voyages/{voyageId}/blackbox/cascade: the voyage's cascade graph.
        /// </summary>
        /// <param name="voyageId">Voyage id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The graph.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVoyageBlackBoxCascade")]
        public Task<BlackBoxCascadeGraph?> GetVoyageCascadeAsync(string voyageId, CancellationToken token = default)
        {
            return _Transport.GetAsync<BlackBoxCascadeGraph>($"/api/v1/voyages/{ArmadaQueryString.Escape(voyageId)}/blackbox/cascade", null, token);
        }

        #endregion
    }
}

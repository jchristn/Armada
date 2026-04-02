namespace Armada.Core.Services
{
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Service for voyage lifecycle management.
    /// </summary>
    public class VoyageService : IVoyageService
    {
        #region Private-Members

        private string _Header = "[VoyageService] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        public VoyageService(LoggingModule logging, DatabaseDriver database)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<Voyage>> CheckCompletionsAsync(CancellationToken token = default)
        {
            // CheckCompletionsAsync is a background/system method (called from Admiral loop).
            // It scans all tenants' voyages, so unscoped calls are appropriate here.
            List<Voyage> completedVoyages = new List<Voyage>();
            List<Voyage> activeVoyages = await _Database.Voyages.EnumerateByStatusAsync(VoyageStatusEnum.InProgress, token).ConfigureAwait(false);
            List<Voyage> openVoyages = await _Database.Voyages.EnumerateByStatusAsync(VoyageStatusEnum.Open, token).ConfigureAwait(false);
            activeVoyages.AddRange(openVoyages);

            foreach (Voyage voyage in activeVoyages)
            {
                List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(voyage.Id, token).ConfigureAwait(false);
                if (missions.Count == 0) continue;

                bool anyActive = missions.Any(m =>
                    m.Status == MissionStatusEnum.Pending ||
                    m.Status == MissionStatusEnum.Assigned ||
                    m.Status == MissionStatusEnum.InProgress ||
                    m.Status == MissionStatusEnum.Testing ||
                    m.Status == MissionStatusEnum.Review ||
                    m.Status == MissionStatusEnum.PullRequestOpen);

                if (anyActive)
                {
                    continue;
                }

                // A mission is terminal if it is complete, failed, cancelled, landing-failed,
                // or an intermediate pipeline stage (WorkProduced) that already handed off.
                bool allDone = missions.All(m =>
                    m.Status == MissionStatusEnum.Complete ||
                    m.Status == MissionStatusEnum.Failed ||
                    m.Status == MissionStatusEnum.Cancelled ||
                    m.Status == MissionStatusEnum.LandingFailed ||
                    m.Status == MissionStatusEnum.WorkProduced);

                if (allDone)
                {
                    bool anyFailed = missions.Any(m =>
                        m.Status == MissionStatusEnum.Failed ||
                        m.Status == MissionStatusEnum.LandingFailed);

                    voyage.Status = anyFailed ? VoyageStatusEnum.Failed : VoyageStatusEnum.Complete;
                    voyage.CompletedUtc = DateTime.UtcNow;
                    voyage.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "voyage " + voyage.Id + " reached terminal status " + voyage.Status);
                    completedVoyages.Add(voyage);
                }
            }

            return completedVoyages;
        }

        /// <inheritdoc />
        public async Task<VoyageProgress?> GetProgressAsync(string voyageId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(voyageId)) return null;

            Voyage? voyage = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Voyages.ReadAsync(tenantId, voyageId, token).ConfigureAwait(false)
                : await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (voyage == null) return null;

            List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(voyage.Id, token).ConfigureAwait(false);

            VoyageProgress progress = new VoyageProgress
            {
                Voyage = voyage,
                TotalMissions = missions.Count,
                CompletedMissions = missions.Count(m => m.Status == MissionStatusEnum.Complete),
                FailedMissions = missions.Count(m => m.Status == MissionStatusEnum.Failed || m.Status == MissionStatusEnum.LandingFailed),
                InProgressMissions = missions.Count(m => CountsAsActiveMission(m.Status))
            };

            return progress;
        }

        #endregion

        #region Private-Methods

        private static bool CountsAsActiveMission(MissionStatusEnum status)
        {
            return status == MissionStatusEnum.Assigned ||
                status == MissionStatusEnum.InProgress ||
                status == MissionStatusEnum.Testing ||
                status == MissionStatusEnum.Review ||
                status == MissionStatusEnum.PullRequestOpen;
        }

        #endregion
    }
}

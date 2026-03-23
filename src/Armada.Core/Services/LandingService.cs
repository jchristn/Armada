namespace Armada.Core.Services
{
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Service for managing mission landing operations including retries and dedicated worktree merges.
    /// </summary>
    public class LandingService : ILandingService
    {
        #region Public-Members

        /// <summary>
        /// Delegate invoked to perform the actual landing (push/PR/merge) for a mission.
        /// Wired by ArmadaServer to route through the existing HandleMissionCompleteAsync logic.
        /// </summary>
        public Func<Mission, Dock, Task>? OnPerformLanding { get; set; }

        #endregion

        #region Private-Members

        private string _Header = "[LandingService] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private IGitService _Git;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="git">Git service.</param>
        public LandingService(
            LoggingModule logging,
            DatabaseDriver database,
            ArmadaSettings settings,
            IGitService git)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Git = git ?? throw new ArgumentNullException(nameof(git));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<bool> RetryLandingAsync(string missionId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

            Mission? mission = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Missions.ReadAsync(tenantId, missionId, token).ConfigureAwait(false)
                : await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (mission == null)
            {
                _Logging.Warn(_Header + "mission " + missionId + " not found");
                return false;
            }

            if (mission.Status != MissionStatusEnum.LandingFailed)
            {
                _Logging.Warn(_Header + "mission " + missionId + " is in status " + mission.Status + ", not LandingFailed -- cannot retry");
                return false;
            }

            if (String.IsNullOrEmpty(mission.VesselId))
            {
                _Logging.Warn(_Header + "mission " + missionId + " has no vessel -- cannot retry landing");
                return false;
            }

            Vessel? vessel = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Vessels.ReadAsync(tenantId, mission.VesselId, token).ConfigureAwait(false)
                : await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null)
            {
                _Logging.Warn(_Header + "vessel " + mission.VesselId + " not found -- cannot retry landing for mission " + missionId);
                return false;
            }

            if (String.IsNullOrEmpty(vessel.LocalPath))
            {
                _Logging.Warn(_Header + "vessel " + vessel.Id + " has no LocalPath -- cannot retry landing for mission " + missionId);
                return false;
            }

            if (String.IsNullOrEmpty(mission.BranchName))
            {
                _Logging.Warn(_Header + "mission " + missionId + " has no branch name -- cannot retry landing");
                return false;
            }

            // Emit retry event
            try
            {
                ArmadaEvent retryEvent = new ArmadaEvent("mission.landing_retry", "Retrying landing: " + mission.Title);
                retryEvent.EntityType = "mission";
                retryEvent.EntityId = mission.Id;
                retryEvent.MissionId = mission.Id;
                retryEvent.VesselId = mission.VesselId;
                retryEvent.VoyageId = mission.VoyageId;
                await _Database.Events.CreateAsync(retryEvent, token).ConfigureAwait(false);
            }
            catch { }

            // Attempt rebase of mission branch onto current target branch
            string repoPath = vessel.LocalPath;
            string targetBranch = vessel.DefaultBranch;
            string missionBranch = mission.BranchName;

            try
            {
                // Fetch latest from remote
                await _Git.FetchAsync(repoPath, token).ConfigureAwait(false);

                // Check if branch still exists
                bool branchExists = await _Git.BranchExistsAsync(repoPath, missionBranch, token).ConfigureAwait(false);
                if (!branchExists)
                {
                    _Logging.Warn(_Header + "branch " + missionBranch + " no longer exists -- cannot retry landing for mission " + missionId);
                    return false;
                }

                _Logging.Info(_Header + "retrying landing for mission " + missionId + " branch " + missionBranch);

                // Transition back to WorkProduced for landing attempt
                mission.Status = MissionStatusEnum.WorkProduced;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                // Try to find the dock or create a temporary context for landing
                Dock? dock = null;
                if (!String.IsNullOrEmpty(mission.DockId))
                {
                    dock = !String.IsNullOrEmpty(tenantId)
                        ? await _Database.Docks.ReadAsync(tenantId, mission.DockId, token).ConfigureAwait(false)
                        : await _Database.Docks.ReadAsync(mission.DockId, token).ConfigureAwait(false);
                }

                // If no dock, create a minimal one for the landing handler
                if (dock == null)
                {
                    dock = new Dock(vessel.Id);
                    dock.BranchName = missionBranch;
                    dock.WorktreePath = vessel.WorkingDirectory ?? vessel.LocalPath;
                    dock.Active = false; // Not a real provisioned dock
                }

                // Invoke the landing handler if available
                if (OnPerformLanding != null)
                {
                    await OnPerformLanding.Invoke(mission, dock).ConfigureAwait(false);
                    _Logging.Info(_Header + "landing retry completed for mission " + missionId);

                    // Re-read mission to get updated status from landing handler
                    mission = !String.IsNullOrEmpty(tenantId)
                        ? await _Database.Missions.ReadAsync(tenantId, missionId, token).ConfigureAwait(false)
                        : await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
                    return mission != null && mission.Status == MissionStatusEnum.Complete;
                }
                else
                {
                    _Logging.Warn(_Header + "no landing handler configured -- cannot retry landing for mission " + missionId);
                    mission.Status = MissionStatusEnum.LandingFailed;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "landing retry failed for mission " + missionId + ": " + ex.Message);

                // Ensure mission goes back to LandingFailed
                try
                {
                    mission = !String.IsNullOrEmpty(tenantId)
                        ? await _Database.Missions.ReadAsync(tenantId, missionId, token).ConfigureAwait(false)
                        : await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
                    if (mission != null && mission.Status != MissionStatusEnum.LandingFailed)
                    {
                        mission.Status = MissionStatusEnum.LandingFailed;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                    }
                }
                catch { }

                return false;
            }
        }

        #endregion
    }
}

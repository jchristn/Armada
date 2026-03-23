namespace Armada.Core.Services
{
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Service for captain lifecycle management.
    /// </summary>
    public class CaptainService : ICaptainService
    {
        #region Public-Members

        /// <inheritdoc />
        public Func<Captain, Task>? OnStopAgent { get; set; }

        /// <inheritdoc />
        public Func<Captain, Mission, Dock, Task<int>>? OnLaunchAgent { get; set; }

        #endregion

        #region Private-Members

        private string _Header = "[CaptainService] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private IGitService _Git;
        private IDockService _Docks;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="git">Git service.</param>
        /// <param name="docks">Dock service.</param>
        public CaptainService(
            LoggingModule logging,
            DatabaseDriver database,
            ArmadaSettings settings,
            IGitService git,
            IDockService docks)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Git = git ?? throw new ArgumentNullException(nameof(git));
            _Docks = docks ?? throw new ArgumentNullException(nameof(docks));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task RecallAsync(string captainId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));

            Captain? captain = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Captains.ReadAsync(tenantId, captainId, token).ConfigureAwait(false)
                : await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
            if (captain == null) throw new InvalidOperationException("Captain not found: " + captainId);

            _Logging.Info(_Header + "recalling captain " + captainId);

            // Stop agent process
            if (OnStopAgent != null && captain.ProcessId.HasValue)
            {
                try
                {
                    await OnStopAgent.Invoke(captain).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error stopping agent for captain " + captainId + ": " + ex.Message);
                }
            }

            // Update state
            await _Database.Captains.UpdateStateAsync(captainId, CaptainStateEnum.Stopping, token).ConfigureAwait(false);

            // Mark the current mission as failed
            if (!String.IsNullOrEmpty(captain.CurrentMissionId))
            {
                Mission? currentMission = !String.IsNullOrEmpty(tenantId)
                    ? await _Database.Missions.ReadAsync(tenantId, captain.CurrentMissionId, token).ConfigureAwait(false)
                    : await _Database.Missions.ReadAsync(captain.CurrentMissionId, token).ConfigureAwait(false);
                if (currentMission != null &&
                    (currentMission.Status == MissionStatusEnum.InProgress ||
                     currentMission.Status == MissionStatusEnum.Assigned))
                {
                    currentMission.Status = MissionStatusEnum.Failed;
                    currentMission.ProcessId = null;
                    currentMission.CompletedUtc = DateTime.UtcNow;
                    currentMission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(currentMission, token).ConfigureAwait(false);
                }
            }

            // Reclaim the dock worktree
            if (!String.IsNullOrEmpty(captain.CurrentDockId))
            {
                try
                {
                    await _Docks.ReclaimAsync(captain.CurrentDockId, tenantId, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error reclaiming dock " + captain.CurrentDockId + " for captain " + captainId + ": " + ex.Message);
                }
            }

            // Clear captain assignment
            captain.State = CaptainStateEnum.Idle;
            captain.CurrentMissionId = null;
            captain.CurrentDockId = null;
            captain.ProcessId = null;
            captain.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task TryRecoverAsync(Captain captain, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            captain.RecoveryAttempts++;
            _Logging.Info(_Header + "auto-recovery attempt " + captain.RecoveryAttempts + "/" + _Settings.MaxRecoveryAttempts + " for captain " + captain.Id);

            // Reload mission and dock info
            Mission? mission = !String.IsNullOrEmpty(captain.CurrentMissionId)
                ? await _Database.Missions.ReadAsync(captain.CurrentMissionId, token).ConfigureAwait(false)
                : null;

            Dock? dock = !String.IsNullOrEmpty(captain.CurrentDockId)
                ? await _Database.Docks.ReadAsync(captain.CurrentDockId, token).ConfigureAwait(false)
                : null;

            if (mission == null || dock == null)
            {
                _Logging.Warn(_Header + "cannot recover captain " + captain.Id + ": mission or dock not found");
                await _Database.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Stalled, token).ConfigureAwait(false);
                return;
            }

            // Repair the worktree if needed
            try
            {
                await _Git.RepairWorktreeAsync(dock.WorktreePath!, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "worktree repair failed for captain " + captain.Id + ": " + ex.Message);
            }

            // Get vessel for context regeneration
            Vessel? vessel = !String.IsNullOrEmpty(mission.VesselId)
                ? await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false)
                : null;

            // Update captain state for re-launch
            captain.State = CaptainStateEnum.Working;
            captain.LastHeartbeatUtc = DateTime.UtcNow;
            captain.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);

            // Re-launch agent process
            if (OnLaunchAgent != null)
            {
                try
                {
                    int processId = await OnLaunchAgent.Invoke(captain, mission, dock).ConfigureAwait(false);
                    captain.ProcessId = processId;
                    await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);

                    mission.ProcessId = processId;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);

                    Signal signal = new Signal(SignalTypeEnum.Assignment, "Auto-recovery attempt " + captain.RecoveryAttempts + " for mission: " + mission.Title);
                    signal.ToCaptainId = captain.Id;
                    await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);

                    _Logging.Info(_Header + "recovered captain " + captain.Id + " with process " + processId);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "recovery launch failed for captain " + captain.Id + ": " + ex.Message);
                    await _Database.Captains.UpdateStateAsync(captain.Id, CaptainStateEnum.Stalled, token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task ReleaseAsync(Captain captain, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            captain.State = CaptainStateEnum.Idle;
            captain.CurrentMissionId = null;
            captain.CurrentDockId = null;
            captain.ProcessId = null;
            captain.RecoveryAttempts = 0;
            captain.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);

            _Logging.Info(_Header + "released captain " + captain.Id);
        }

        #endregion
    }
}

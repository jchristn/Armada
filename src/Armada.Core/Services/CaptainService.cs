namespace Armada.Core.Services
{
    using System.Text.Json;
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

        /// <summary>
        /// Where docks live. Recovery checks a Harbor-hosted dock on its Harbor through this; without it every dock is
        /// checked on this machine.
        /// </summary>
        public DockHostResolver? DockHosts { get; set; }

        #endregion

        #region Private-Members

        private const int _WipPreserveTimeoutMs = 60000;
        private string _Header = "[CaptainService] ";
        // Captains whose recovery relaunch was refused while their dock's Harbor was offline, keyed to that Harbor.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _AwaitingHarbor = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
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
        public Task RecallAsync(string captainId, string? tenantId = null, CancellationToken token = default)
        {
            return RecallAsync(captainId, tenantId, null, token);
        }

        /// <inheritdoc />
        public async Task RecallAsync(string captainId, string? tenantId, OperatorStopContext? stop, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            OperatorStopContext stopContext = stop ?? new OperatorStopContext();

            Captain? captain = !String.IsNullOrEmpty(tenantId)
                ? await _Database.Captains.ReadAsync(tenantId, captainId, token).ConfigureAwait(false)
                : await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
            if (captain == null) throw new KeyNotFoundException("Captain not found: " + captainId);

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
                    _Logging.Warn(_Header + "error stopping agent for captain " + captainId + ": " + ex.ToString());
                    const string reason = "Recall deferred: the previous job has not confirmed its exit. Captain, mission and dock ownership were retained. Remediation: reconnect the owning Harbor and confirm the job has stopped before retrying.";
                    if (!String.IsNullOrEmpty(captain.CurrentMissionId))
                    {
                        Mission? held = await _Database.Missions.ReadAsync(captain.CurrentMissionId, token).ConfigureAwait(false);
                        if (held != null)
                        {
                            held.FailureReason = reason;
                            held.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.Missions.UpdateAsync(held, token).ConfigureAwait(false);
                        }
                    }
                    throw new InvalidOperationException(reason, ex);
                }
            }

            // Update state
            await _Database.Captains.UpdateStateAsync(captainId, CaptainStateEnum.Stopping, token).ConfigureAwait(false);

            // An operator stop cancels the current mission (it is not a failure of the work), records who stopped it and
            // how, and preserves uncommitted work on the mission branch before the dock is reclaimed.
            if (!String.IsNullOrEmpty(captain.CurrentMissionId))
            {
                Mission? currentMission = !String.IsNullOrEmpty(tenantId)
                    ? await _Database.Missions.ReadAsync(tenantId, captain.CurrentMissionId, token).ConfigureAwait(false)
                    : await _Database.Missions.ReadAsync(captain.CurrentMissionId, token).ConfigureAwait(false);
                if (currentMission != null &&
                    (currentMission.Status == MissionStatusEnum.InProgress ||
                     currentMission.Status == MissionStatusEnum.Assigned))
                {
                    await RecordOperatorStopAsync(captain, currentMission, stopContext, token).ConfigureAwait(false);
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
                    _Logging.Warn(_Header + "error reclaiming dock " + captain.CurrentDockId + " for captain " + captainId + ": " + ex.ToString());
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
        public async Task<int> RetryRelaunchesAwaitingHarborAsync(CancellationToken token = default)
        {
            if (_AwaitingHarbor.IsEmpty || DockHosts?.Harbors == null) return 0;
            int relaunched = 0;
            foreach (KeyValuePair<string, string> waiting in _AwaitingHarbor.ToArray())
            {
                if (!DockHosts.Harbors.IsConnected(waiting.Value)) continue;
                if (!_AwaitingHarbor.TryRemove(waiting.Key, out _)) continue;
                Captain? captain = await _Database.Captains.ReadAsync(waiting.Key, token).ConfigureAwait(false);
                // Only a captain still holding its mission with no process is waiting; anything else has moved on.
                if (captain == null || captain.State != CaptainStateEnum.Working || captain.ProcessId.HasValue || String.IsNullOrEmpty(captain.CurrentMissionId)) continue;
                if (captain.RecoveryAttempts >= _Settings.MaxRecoveryAttempts) continue;
                _Logging.Info(_Header + "Harbor " + waiting.Value + " is back; relaunching captain " + captain.Id + " now rather than at the next stall check");
                await TryRecoverAsync(captain, token).ConfigureAwait(false);
                relaunched++;
            }
            return relaunched;
        }

        /// <inheritdoc />
        public async Task TryRecoverAsync(Captain captain, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            _AwaitingHarbor.TryRemove(captain.Id, out _);

            try
            {
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
                    string missingReason = "Auto-recovery failed because the mission or dock could not be reloaded.";
                    _Logging.Warn(_Header + "cannot recover captain " + captain.Id + ": mission or dock not found -- failing mission and releasing to idle");
                    await FinalizeRecoveryFailureAsync(captain, mission, missingReason, MissionFailureKindEnum.Infra, token).ConfigureAwait(false);
                    return;
                }

                Voyage? voyage = !String.IsNullOrEmpty(mission.VoyageId)
                    ? await _Database.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false)
                    : null;

                // Reload after the dock and voyage reads. Landing can advance the mission while
                // recovery is waiting on those stores, and the current status owns dock retention.
                Mission? latestMission = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                if (latestMission != null) mission = latestMission;

                if (mission.Status == MissionStatusEnum.Verifying) return; // A queued/running check owns this captain.

                bool voyageCancelled = voyage != null && voyage.Status == VoyageStatusEnum.Cancelled;
                bool missionRecoverable =
                    mission.Status == MissionStatusEnum.Assigned ||
                    mission.Status == MissionStatusEnum.InProgress ||
                    mission.Status == MissionStatusEnum.Testing ||
                    mission.Status == MissionStatusEnum.Review;

                if (voyageCancelled || !missionRecoverable)
                {
                    MissionStatusEnum observedStatus = mission.Status;
                    if (voyageCancelled && mission.Status != MissionStatusEnum.Cancelled)
                    {
                        mission.Status = MissionStatusEnum.Cancelled;
                        mission.CompletedUtc = DateTime.UtcNow;
                    }

                    mission.ProcessId = null;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    if (!await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { observedStatus }, token).ConfigureAwait(false))
                    {
                        Mission? reloadedMission = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                        if (reloadedMission != null) mission = reloadedMission;
                    }

                    bool missionStillNeedsDock = mission.Status == MissionStatusEnum.WorkProduced
                        || mission.Status == MissionStatusEnum.PullRequestOpen
                        || mission.Status == MissionStatusEnum.LandingFailed;
                    if (!missionStillNeedsDock)
                    {
                        try
                        {
                            await _Docks.ReclaimAsync(dock.Id, token: token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _Logging.Warn(_Header + "error reclaiming dock " + dock.Id + " while skipping recovery for captain " + captain.Id + ": " + ex.ToString());
                        }
                    }

                    await ReleaseAsync(captain, token: token).ConfigureAwait(false);
                    _Logging.Debug(_Header + "skipping auto-recovery for captain " + captain.Id +
                        " because mission " + mission.Id + " is " + mission.Status +
                        (voyageCancelled ? " and voyage " + mission.VoyageId + " is Cancelled" : String.Empty));
                    return;
                }

                if (String.IsNullOrEmpty(dock.WorktreePath))
                {
                    string worktreeReason = "Auto-recovery failed because dock " + dock.Id + " has no worktree path.";
                    _Logging.Warn(_Header + "cannot recover captain " + captain.Id + ": dock " + dock.Id + " has no worktree path -- failing mission and releasing to idle");
                    await FinalizeRecoveryFailureAsync(captain, mission, worktreeReason, MissionFailureKindEnum.Infra, token).ConfigureAwait(false);
                    return;
                }

                // A Harbor-hosted dock is checked (and repaired) on its Harbor. While that Harbor is offline the dock
                // cannot be checked; the relaunch below decides (it waits for the Harbor rather than moving the dock).
                bool harborDock = DockHosts != null && DockHostResolver.IsHarborDock(dock);
                bool harborOffline = harborDock && (DockHosts!.Harbors == null || !DockHosts.Harbors.IsConnected(dock.HarborId!));
                IGitService dockGit = harborDock && !harborOffline ? DockHosts!.ForDock(dock).Git : _Git;
                bool worktreeAccessible = harborOffline;
                try
                {
                    if (!harborOffline) worktreeAccessible = await dockGit.IsRepositoryAsync(dock.WorktreePath, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "worktree accessibility check failed for captain " + captain.Id + ": " + ex.ToString());
                }

                // Only attempt destructive repair when the worktree is no longer usable.
                // Normal agent recovery must preserve the mission's uncommitted changes.
                if (!worktreeAccessible)
                {
                    try
                    {
                        await dockGit.RepairWorktreeAsync(dock.WorktreePath, token).ConfigureAwait(false);
                        worktreeAccessible = await dockGit.IsRepositoryAsync(dock.WorktreePath, token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "worktree repair failed for captain " + captain.Id + ": " + ex.ToString());
                    }
                }

                if (!worktreeAccessible)
                {
                    string inaccessibleReason = "Auto-recovery failed because dock " + dock.Id + " is not a usable git worktree.";
                    _Logging.Warn(_Header + "cannot recover captain " + captain.Id + ": dock " + dock.Id + " is not a usable git worktree -- failing mission and releasing to idle");
                    await FinalizeRecoveryFailureAsync(captain, mission, inaccessibleReason, MissionFailureKindEnum.Infra, token).ConfigureAwait(false);
                    return;
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

                // Count every attempted recovery launch durably, including retries after a Harbor reconnect.
                // Reattaching an existing Harbor process never enters this path and must not increment it.
                if (OnLaunchAgent != null)
                {
                    mission.RedispatchAttempts++;
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                    try
                    {
                        int processId = await OnLaunchAgent.Invoke(captain, mission, dock).ConfigureAwait(false);

                        // A fast agent can exit (and its handler move the mission on and release the captain) before
                        // this returns: record the relaunch only while the captain still holds the mission and the
                        // mission is still in the state it was relaunched from.
                        if (await _Database.Captains.TrySetProcessAsync(captain.Id, mission.Id, processId, token).ConfigureAwait(false))
                            captain.ProcessId = processId;

                        MissionStatusEnum relaunchedFrom = mission.Status;
                        mission.ProcessId = processId;
                        mission.Status = MissionStatusEnum.InProgress;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        if (!await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { relaunchedFrom, MissionStatusEnum.Assigned, MissionStatusEnum.InProgress }, token).ConfigureAwait(false))
                            _Logging.Info(_Header + "mission " + mission.Id + " moved on before relaunch process " + processId + " was recorded; keeping its newer state");

                        Signal signal = new Signal(SignalTypeEnum.Assignment, "Auto-recovery attempt " + captain.RecoveryAttempts + " for mission: " + mission.Title);
                        signal.ToCaptainId = captain.Id;
                        await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);

                        _Logging.Info(_Header + "recovered captain " + captain.Id + " with process " + processId);
                    }
                    catch (HarborLaunchUnavailableException refused) when (captain.RecoveryAttempts < _Settings.MaxRecoveryAttempts)
                    {
                        // requireHarborForLaunch refused the relaunch because the user's Harbor is unavailable. Spend
                        // this attempt and wait: the captain stays Working with no process, and the next stall check
                        // retries recovery, so a Harbor that reconnects within the remaining attempts gets the mission back.
                        _Logging.Warn(_Header + "recovery relaunch for captain " + captain.Id + " refused (attempt " + captain.RecoveryAttempts + "/" + _Settings.MaxRecoveryAttempts + "); retrying when the Harbor reconnects, or at the next stall check: " + refused.Message);
                        if (!String.IsNullOrEmpty(dock.HarborId)) _AwaitingHarbor[captain.Id] = dock.HarborId!;
                        captain.ProcessId = null;
                        captain.LastHeartbeatUtc = DateTime.UtcNow;
                        captain.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);

                        mission.ProcessId = null;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "recovery launch failed for captain " + captain.Id + ": " + ex.ToString());
                        string launchReason = "Auto-recovery failed while relaunching the agent: " + ex.Message;
                        MissionFailureKindEnum launchKind = ex is HarborDockNotFoundException
                            ? MissionFailureKindEnum.Infra
                            : MissionFailureKindEnum.StallRecoveryExhausted;
                        await FinalizeRecoveryFailureAsync(captain, mission, launchReason, launchKind, token).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "unhandled error in TryRecoverAsync for captain " + captain.Id + ": " + ex.ToString());
                try
                {
                    Mission? mission = !String.IsNullOrEmpty(captain.CurrentMissionId)
                        ? await _Database.Missions.ReadAsync(captain.CurrentMissionId, token).ConfigureAwait(false)
                        : null;
                    string unexpectedReason = "Auto-recovery failed unexpectedly: " + ex.Message;
                    await FinalizeRecoveryFailureAsync(captain, mission, unexpectedReason, MissionFailureKindEnum.StallRecoveryExhausted, token).ConfigureAwait(false);
                }
                catch { }
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

        #region Private-Methods

        private async Task RecordOperatorStopAsync(Captain captain, Mission mission, OperatorStopContext stop, CancellationToken token)
        {
            OperatorStopEventPayload payload = new OperatorStopEventPayload
            {
                Source = stop.Source,
                UserId = stop.UserId,
                StoppedUtc = stop.RequestedUtc,
                PreviousStatus = mission.Status.ToString()
            };

            string? dockId = !String.IsNullOrEmpty(captain.CurrentDockId) ? captain.CurrentDockId : mission.DockId;
            Dock? dock = null;
            if (!String.IsNullOrEmpty(dockId))
            {
                try
                {
                    dock = await _Database.Docks.ReadAsync(dockId, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    _Logging.Warn(_Header + "could not read dock " + dockId + " of stopped mission " + mission.Id + ": " + ex.Message);
                }
            }

            if (dock != null)
            {
                payload.DockId = dock.Id;
                payload.BranchName = !String.IsNullOrEmpty(dock.BranchName) ? dock.BranchName : mission.BranchName;
                await PreserveWorkInProgressAsync(captain, mission, dock, payload, token).ConfigureAwait(false);
            }

            string reason = stop.Describe() + ".";
            if (!String.IsNullOrEmpty(payload.WipCommitHash))
            {
                reason += " Uncommitted work was preserved as WIP commit " + payload.WipCommitHash
                    + (String.IsNullOrEmpty(payload.BranchName) ? "" : " on branch " + payload.BranchName)
                    + (payload.WipPushed ? " (pushed)." : " (not pushed).");
            }
            else if (!String.IsNullOrEmpty(payload.WipError))
            {
                reason += " Uncommitted work could not be preserved: " + payload.WipError;
            }

            mission.Status = MissionStatusEnum.Cancelled;
            mission.FailureKind = MissionFailureKindEnum.OperatorAction;
            mission.FailureReason = reason;
            mission.ProcessId = null;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            _Logging.Info(_Header + "mission " + mission.Id + " cancelled: " + reason);

            try
            {
                ArmadaEvent evt = new ArmadaEvent("mission.stopped_by_operator", "Mission " + mission.Id + ": " + reason);
                evt.TenantId = mission.TenantId;
                // The event is owned (and scoped) like the mission; the operator who stopped it is in the message and payload.
                evt.UserId = mission.UserId;
                evt.EntityType = "mission";
                evt.EntityId = mission.Id;
                evt.CaptainId = captain.Id;
                evt.MissionId = mission.Id;
                evt.VesselId = mission.VesselId;
                evt.VoyageId = mission.VoyageId;
                evt.Payload = JsonSerializer.Serialize(payload);
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "error emitting mission.stopped_by_operator for " + mission.Id + ": " + ex.Message);
            }
        }

        private async Task PreserveWorkInProgressAsync(Captain captain, Mission mission, Dock dock, OperatorStopEventPayload payload, CancellationToken token)
        {
            // A judge reports through its verdict and an architect through its plan: neither should change the branch.
            if (PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge) || PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Architect)) return;
            if (String.IsNullOrEmpty(dock.WorktreePath)) return;

            bool harborDock = DockHostResolver.IsHarborDock(dock);
            if (!harborDock && !Directory.Exists(dock.WorktreePath)) return;

            // Best effort and bounded: preserving work never blocks or fails the stop.
            using (CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                bounded.CancelAfter(_WipPreserveTimeoutMs);
                try
                {
                    IGitService git = harborDock && DockHosts != null ? DockHosts.ForDock(dock).Git : _Git;
                    if (harborDock && DockHosts == null) throw new InvalidOperationException("the dock is on Harbor " + dock.HarborId + " and Harbor delegation is not available");

                    GitWorkingTreeStatus status = await git.GetWorkingTreeStatusAsync(dock.WorktreePath!, bounded.Token).ConfigureAwait(false);
                    if (status.IsClean) return;

                    string message = "WIP: preserved on operator stop\n\n"
                        + mission.Title + "\n\n"
                        + "Armada committed the changes " + captain.Name + " left uncommitted when " + payload.Source + " stopped mission " + mission.Id + ".\n"
                        + "\nArmada-Mission-Id: " + mission.Id
                        + "\nArmada-Voyage-Id: " + (mission.VoyageId ?? "")
                        + "\nArmada-Captain-Id: " + captain.Id
                        + "\nArmada-Vessel-Id: " + (mission.VesselId ?? dock.VesselId ?? "");
                    if (!await git.CommitAllAsync(dock.WorktreePath!, message, bounded.Token).ConfigureAwait(false)) return;

                    payload.WipCommitHash = await git.GetHeadCommitHashAsync(dock.WorktreePath!, bounded.Token).ConfigureAwait(false) ?? "(unknown)";
                    _Logging.Info(_Header + "preserved uncommitted work of stopped mission " + mission.Id + " as " + payload.WipCommitHash + " in dock " + dock.Id);

                    try
                    {
                        await git.PushBranchAsync(dock.WorktreePath!, token: bounded.Token).ConfigureAwait(false);
                        payload.WipPushed = true;
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                    {
                        _Logging.Warn(_Header + "could not push WIP commit of stopped mission " + mission.Id + ": " + ex.Message);
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    payload.WipError = ex is OperationCanceledException ? "timed out" : ex.Message;
                    _Logging.Warn(_Header + "could not preserve uncommitted work of stopped mission " + mission.Id + " in dock " + dock.Id + ": " + payload.WipError);
                }
            }
        }

        /// <summary>
        /// Mark the mission as failed and return the captain to Idle when auto-recovery cannot continue.
        /// </summary>
        private async Task FinalizeRecoveryFailureAsync(Captain captain, Mission? mission, string reason, MissionFailureKindEnum failureKind, CancellationToken token)
        {
            if (mission != null &&
                !MissionStateMachine.IsTerminalOrPostWork(mission.Status))
            {
                mission.Status = MissionStatusEnum.Failed;
                mission.FailureReason = reason;
                mission.FailureKind = failureKind;
                mission.ProcessId = null;
                mission.CompletedUtc = DateTime.UtcNow;
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            }

            // Failed stall recovery must retain edits, including when the replacement launch throws.
            if (failureKind != MissionFailureKindEnum.StallRecoveryExhausted && !String.IsNullOrEmpty(captain.CurrentDockId))
            {
                try
                {
                    await _Docks.ReclaimAsync(captain.CurrentDockId, captain.TenantId, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error reclaiming dock " + captain.CurrentDockId + " during recovery failure cleanup: " + ex.ToString());
                }
            }

            captain.CurrentMissionId = null;
            captain.CurrentDockId = null;
            captain.ProcessId = null;
            captain.State = CaptainStateEnum.Idle;
            captain.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Captains.UpdateAsync(captain, token).ConfigureAwait(false);

            if (mission != null)
            {
                Signal signal = new Signal(SignalTypeEnum.Error, "Mission " + mission.Id + " failed during auto-recovery: " + reason);
                signal.FromCaptainId = captain.Id;
                await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}

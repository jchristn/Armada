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

        /// <summary>
        /// Resolves the host a dock lives on, so retries can confirm that a Harbor-hosted worktree still exists.
        /// When null, only docks on the Admiral's host are checked.
        /// </summary>
        public DockHostResolver? DockHosts { get; set; } = null;

        /// <summary>
        /// Dock service used to recreate a missing dock worktree from the mission branch before retrying. When null, a
        /// missing worktree fails the retry as before.
        /// </summary>
        public IDockService? Docks { get; set; } = null;

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

            DateTime retryVersion = mission.LastUpdateUtc;
            MissionStatusEnum retryStatus = mission.Status;
            async Task<bool> SaveRetryAsync()
            {
                mission.LastUpdateUtc = DateTime.UtcNow;
                bool saved = await _Database.Missions.TryUpdateIfUnchangedAsync(mission, retryVersion, new[] { retryStatus }, token).ConfigureAwait(false);
                if (saved) { retryVersion = mission.LastUpdateUtc; retryStatus = mission.Status; }
                return saved;
            }

            // Landable directly: work produced but not yet landed, a prior landing that failed, or a mission
            // sitting in Review that carries no explicit review gate (requiresReview=false) -- such a mission
            // has no Approve/Deny decision to make, so landing is the way to graduate it out of Review. A
            // Review mission that DOES require review must still be resolved via Approve/Deny, not landed here.
            bool landableReview = mission.Status == MissionStatusEnum.Review && !mission.RequiresReview;
            if (mission.Status != MissionStatusEnum.LandingFailed
                && mission.Status != MissionStatusEnum.WorkProduced
                && !landableReview)
            {
                _Logging.Warn(_Header + "mission " + missionId + " is in status " + mission.Status
                    + " (requiresReview=" + mission.RequiresReview + ") -- not landable via retry");
                return false;
            }

            if (String.IsNullOrEmpty(mission.VesselId))
            {
                _Logging.Warn(_Header + "mission " + missionId + " has no vessel -- cannot retry landing");
                return false;
            }

            // Use the mission's own TenantId for vessel lookup (more reliable than caller's tenantId)
            string? lookupTenantId = !String.IsNullOrEmpty(mission.TenantId) ? mission.TenantId : tenantId;
            Vessel? vessel = !String.IsNullOrEmpty(lookupTenantId)
                ? await _Database.Vessels.ReadAsync(lookupTenantId, mission.VesselId, token).ConfigureAwait(false)
                : await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            // Fall back to unscoped read if tenant-scoped read fails
            if (vessel == null)
                vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null)
            {
                _Logging.Warn(_Header + "vessel " + mission.VesselId + " not found -- cannot retry landing for mission " + missionId);
                return false;
            }

            Dock? dock = String.IsNullOrEmpty(mission.DockId) ? null : await _Database.Docks.ReadAsync(mission.DockId, token).ConfigureAwait(false);
            string? owningRepository = DockHostResolver.IsHarborDock(dock) ? dock!.RepositoryPath : vessel.LocalPath;
            if (String.IsNullOrEmpty(owningRepository))
            {
                mission.FailureKind = MissionFailureKindEnum.Infra;
                mission.FailureReason = "Landing retry cannot resolve the repository on the dock's owning host.";
                if (!await SaveRetryAsync().ConfigureAwait(false)) return false;
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
            string repoPath = owningRepository;
            IGitService git = _Git;
            string targetBranch = vessel.DefaultBranch;
            string missionBranch = mission.BranchName;

            try
            {
                if (DockHostResolver.IsHarborDock(dock))
                    git = (DockHosts ?? throw new InvalidOperationException("The dock's Harbor host is unavailable; reconnect it and retry.")).ForDock(dock).Git;
                // Fetch on the same host that owns the branch; a Harbor path never runs on the Admiral.
                await git.FetchAsync(repoPath, token).ConfigureAwait(false);

                // Check if branch still exists
                bool branchExists = await git.BranchExistsAsync(repoPath, missionBranch, token).ConfigureAwait(false);
                if (!branchExists)
                {
                    _Logging.Warn(_Header + "branch " + missionBranch + " no longer exists -- cannot retry landing for mission " + missionId);
                    mission.Status = MissionStatusEnum.LandingFailed;
                    mission.FailureKind = MissionFailureKindEnum.Infra;
                    mission.FailureReason = "Source branch '" + missionBranch + "' is missing in " + repoPath + ". Recover the preserved commit or branch before retrying landing.";
                    await SaveRetryAsync().ConfigureAwait(false);
                    return false;
                }

                _Logging.Debug(_Header + "retrying landing for mission " + missionId + " branch " + missionBranch);

                DockRestoreResult? restored = null;
                if (dock != null && !String.IsNullOrEmpty(dock.WorktreePath) && Docks != null
                    && !await DockWorktreeExistsAsync(dock, token).ConfigureAwait(false))
                {
                    restored = await Docks.RestoreWorktreeAsync(dock.Id, "retry landing of mission " + mission.Id, true, token).ConfigureAwait(false);
                    if (!restored.Restored)
                        _Logging.Warn(_Header + "could not recreate the worktree of dock " + dock.Id + " for mission " + missionId + ": " + restored.Reason);
                }

                if (dock != null && !String.IsNullOrEmpty(dock.WorktreePath) && restored?.Restored != true
                    && !await DockWorktreeExistsAsync(dock, token).ConfigureAwait(false))
                {
                    string where = DockHostResolver.IsHarborDock(dock) ? " on Harbor " + dock.HarborId : "";
                    _Logging.Warn(_Header + "dock " + dock.Id + " worktree " + dock.WorktreePath + where + " is missing -- cannot retry landing for mission " + missionId);
                    mission.Status = MissionStatusEnum.LandingFailed;
                    mission.FailureKind = MissionFailureKindEnum.Infra;
                    mission.FailureReason = "Dock missing: the worktree " + dock.WorktreePath + where + " for dock " + dock.Id
                        + " no longer exists, so landing cannot be retried from it. Branch '" + missionBranch
                        + "' is still in the repository; merge it manually or restart the mission."
                        + (restored != null && !String.IsNullOrEmpty(restored.Reason) ? " Recreating the worktree failed: " + restored.Reason + "." : "");
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    if (!await SaveRetryAsync().ConfigureAwait(false)) return false;
                    return false;
                }

                // Transition back to WorkProduced for landing attempt
                mission.Status = MissionStatusEnum.WorkProduced;
                mission.LastUpdateUtc = DateTime.UtcNow;
                if (!await SaveRetryAsync().ConfigureAwait(false)) return false;

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
                    _Logging.Debug(_Header + "landing retry completed for mission " + missionId);

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
                    mission.FailureKind = MissionFailureKindEnum.Infra;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    if (!await SaveRetryAsync().ConfigureAwait(false)) return false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "landing retry failed for mission " + missionId + ": " + ex.ToString());

                // Capture any merge-conflict file list so the operator sees exactly what to fix. A merge git reported as
                // conflicted carries its files (read before the merge was aborted); otherwise the list is read from the
                // worktree where the merge ran, never from the owning repository, which is not where landing merges.
                string failureReason = "Landing retry failed: " + ex.Message;
                MissionFailureKindEnum failureKind = LandingFailureClassifier.Classify(ex);
                IReadOnlyList<string> conflicts = ConflictedFilesFrom(ex);
                if (conflicts.Count == 0)
                    conflicts = await ReadMergeWorktreeConflictsAsync(git, dock, vessel).ConfigureAwait(false);
                if (conflicts.Count > 0)
                {
                    failureReason = "Landing retry failed with merge conflicts in: " + String.Join(", ", conflicts);
                    failureKind = MissionFailureKindEnum.LandingConflict;
                }

                // Ensure mission goes back to LandingFailed with the captured reason
                try
                {
                    if (mission != null)
                    {
                        mission.Status = MissionStatusEnum.LandingFailed;
                        mission.FailureReason = failureReason;
                        mission.FailureKind = failureKind;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        if (!await SaveRetryAsync().ConfigureAwait(false)) return false;
                    }
                }
                catch { }

                return false;
            }
        }

        #endregion

        #region Private-Methods

        private static IReadOnlyList<string> ConflictedFilesFrom(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is GitMergeConflictException conflict && conflict.ConflictedFiles.Count > 0) return conflict.ConflictedFiles;
            }
            return new List<string>();
        }

        private async Task<IReadOnlyList<string>> ReadMergeWorktreeConflictsAsync(IGitService git, Dock? dock, Vessel vessel)
        {
            // Landing merges in the dock worktree (target integration) and, for a local merge, in the target checkout.
            List<string> candidates = new List<string>();
            bool harborDock = DockHostResolver.IsHarborDock(dock);
            if (!String.IsNullOrEmpty(dock?.WorktreePath)) candidates.Add(dock!.WorktreePath!);
            string? targetCheckout = harborDock ? dock!.CheckoutPath : vessel.WorkingDirectory;
            if (!String.IsNullOrEmpty(targetCheckout) && !candidates.Contains(targetCheckout)) candidates.Add(targetCheckout);

            foreach (string candidate in candidates)
            {
                try
                {
                    // Not cancellable: the failure is being recorded even when the retry was cancelled.
                    IReadOnlyList<string> conflicts = await git.GetConflictedFilesAsync(candidate, CancellationToken.None).ConfigureAwait(false);
                    if (conflicts.Count > 0) return conflicts;
                }
                catch (Exception ex)
                {
                    _Logging.Debug(_Header + "unable to list conflicted files in " + candidate + ": " + ex.Message);
                }
            }
            return new List<string>();
        }

        private async Task<bool> DockWorktreeExistsAsync(Dock dock, CancellationToken token)
        {
            if (DockHostResolver.IsHarborDock(dock))
            {
                if (DockHosts == null) return true;
                try
                {
                    return await DockHosts.ForDock(dock).Files.DirectoryExistsAsync(dock.WorktreePath!, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // An unreachable Harbor is not proof the dock is gone; let the landing attempt report it.
                    _Logging.Warn(_Header + "unable to check dock " + dock.Id + " on Harbor " + dock.HarborId + ": " + ex.Message);
                    return true;
                }
            }
            return Directory.Exists(dock.WorktreePath);
        }

        #endregion
    }
}

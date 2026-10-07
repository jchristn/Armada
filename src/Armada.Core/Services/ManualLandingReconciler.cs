namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Moves WorkProduced missions to Complete once their work has been merged into the target branch by hand. Applies to
    /// missions whose effective landing mode is None (Armada never lands them itself, so nothing else would ever complete
    /// them). Merged is decided by git's exit codes (merge-base --is-ancestor), never by reading command output text.
    /// </summary>
    public class ManualLandingReconciler
    {
        #region Private-Members

        private readonly string _Header = "[ManualLandingReconciler] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly IGitService _Git;
        private readonly LoggingModule _Logging;
        private readonly int _MaxPerCycle = 25;

        #endregion

        #region Public-Members

        /// <summary>
        /// Raised after a mission is reconciled to Complete (for events and dashboard broadcasts).
        /// </summary>
        public Func<Mission, Task>? OnMissionReconciled { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings (Admiral default landing mode, repos directory).</param>
        /// <param name="git">Git service.</param>
        /// <param name="logging">Logging module.</param>
        public ManualLandingReconciler(DatabaseDriver database, ArmadaSettings settings, IGitService git, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Git = git ?? throw new ArgumentNullException(nameof(git));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reconcile WorkProduced manual-landing missions, optionally only those of one vessel. Bounded per call.
        /// </summary>
        /// <param name="vesselId">Only this vessel's missions, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of missions moved to Complete.</returns>
        public async Task<int> ReconcileAsync(string? vesselId = null, CancellationToken token = default)
        {
            List<Mission> candidates = String.IsNullOrEmpty(vesselId)
                ? await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.WorkProduced, token).ConfigureAwait(false)
                : (await _Database.Missions.EnumerateByVesselAsync(vesselId, token).ConfigureAwait(false))
                    .Where(m => m.Status == MissionStatusEnum.WorkProduced)
                    .ToList();
            if (candidates.Count == 0) return 0;

            HashSet<string> inMergeQueue = await MergeQueueMissionActivity.GetMissionIdsWithActiveEntriesAsync(_Database, token).ConfigureAwait(false);
            int reconciled = 0;
            int examined = 0;
            foreach (Mission mission in candidates.OrderBy(m => m.LastUpdateUtc))
            {
                if (examined >= _MaxPerCycle) break;
                if (String.IsNullOrEmpty(mission.BranchName) || String.IsNullOrEmpty(mission.VesselId)) continue;
                if (inMergeQueue.Contains(mission.Id)) continue;
                examined++;

                try
                {
                    if (await ReconcileMissionAsync(mission, token).ConfigureAwait(false)) reconciled++;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error reconciling mission " + mission.Id + ": " + ex.Message);
                }
            }

            return reconciled;
        }

        /// <summary>
        /// Reconcile one mission: when it is WorkProduced, its effective landing mode is None, and its work is contained
        /// in the vessel's target branch, mark it Complete.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the mission was moved to Complete.</returns>
        public async Task<bool> ReconcileMissionAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (mission.Status != MissionStatusEnum.WorkProduced) return false;
            if (String.IsNullOrEmpty(mission.BranchName) || String.IsNullOrEmpty(mission.VesselId)) return false;

            Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            if (vessel == null) return false;

            Voyage? voyage = String.IsNullOrEmpty(mission.VoyageId) ? null : await _Database.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false);
            LandingModeEnum effectiveMode = voyage?.LandingMode ?? vessel.LandingMode ?? _Settings.LandingMode ?? LandingModeEnum.MergeAndPush;
            if (effectiveMode != LandingModeEnum.None) return false;

            string? repoPath = ResolveRepoPath(vessel);
            if (repoPath == null) return false;

            string target = String.IsNullOrWhiteSpace(vessel.DefaultBranch) ? "main" : vessel.DefaultBranch!;
            string source = !String.IsNullOrEmpty(mission.CommitHash) ? mission.CommitHash! : "refs/heads/" + mission.BranchName;

            bool? merged = await _Git.IsAncestorAsync(repoPath, source, "refs/heads/" + target, token).ConfigureAwait(false);
            if (merged != true)
            {
                // A repository that tracks the remote (a working clone) may only have the merge on origin.
                bool? mergedOnRemote = await _Git.IsAncestorAsync(repoPath, source, "refs/remotes/origin/" + target, token).ConfigureAwait(false);
                if (mergedOnRemote != true) return false;
            }

            // Re-read so a concurrent status change (for example a landing retry) wins.
            Mission? current = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
            if (current == null || current.Status != MissionStatusEnum.WorkProduced) return false;

            current.Status = MissionStatusEnum.Complete;
            current.CompletedUtc = DateTime.UtcNow;
            current.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Missions.UpdateAsync(current, token).ConfigureAwait(false);
            _Logging.Info(_Header + "mission " + current.Id + " branch " + current.BranchName + " is merged into " + target + "; status set to Complete");

            if (OnMissionReconciled != null)
            {
                try
                {
                    await OnMissionReconciled.Invoke(current).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "OnMissionReconciled callback failed for " + current.Id + ": " + ex.Message);
                }
            }

            return true;
        }

        #endregion

        #region Private-Methods

        private string? ResolveRepoPath(Vessel vessel)
        {
            // Same order as the vessel branch routes (Manage Branches merges there): the bare repository, then the
            // operator's working clone, then the conventional repos directory location.
            if (!String.IsNullOrEmpty(vessel.LocalPath) && Directory.Exists(vessel.LocalPath)) return vessel.LocalPath;
            if (!String.IsNullOrEmpty(vessel.WorkingDirectory) && Directory.Exists(vessel.WorkingDirectory)) return vessel.WorkingDirectory;
            if (!String.IsNullOrEmpty(vessel.Name))
            {
                string candidate = Path.Combine(_Settings.ReposDirectory, vessel.Name + ".git");
                if (Directory.Exists(candidate)) return candidate;
            }

            return null;
        }

        #endregion
    }
}

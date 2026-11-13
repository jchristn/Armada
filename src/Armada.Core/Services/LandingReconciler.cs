namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Moves stale LandingFailed missions to Complete once their work is proven to be on the vessel's remote default
    /// branch: the mission's own commit is an ancestor of <c>origin/&lt;default branch&gt;</c>, or a commit on that branch
    /// carries the mission's <c>Armada-Mission-Id</c> trailer. A landing that failed for a reason that later cleared (a
    /// dirty developer checkout, an unpushed local commit) and was then published by another path is otherwise stranded
    /// as LandingFailed. Content-equivalent commits without the trailer are deliberately not matched. Git runs where the
    /// vessel's repository lives (the Admiral host or a Harbor); a vessel whose repository cannot be reached or fetched
    /// is skipped, so a mission is never completed without proof. Each sweep reads one bounded page of LandingFailed
    /// missions and fetches each vessel at most once.
    /// </summary>
    public class LandingReconciler
    {
        #region Public-Members

        /// <summary>
        /// Event type recorded when a mission is reconciled.
        /// </summary>
        public const string EventType = "mission.landing_reconciled";

        /// <summary>
        /// The commit trailer key that names the mission a commit belongs to.
        /// </summary>
        public const string MissionIdTrailerKey = "Armada-Mission-Id";

        /// <summary>
        /// Maximum number of LandingFailed missions examined per sweep (1 to 1000, default 25).
        /// </summary>
        public int MaxPerCycle
        {
            get => _MaxPerCycle;
            set
            {
                if (value < 1 || value > 1000) throw new ArgumentOutOfRangeException(nameof(MaxPerCycle), "MaxPerCycle must be between 1 and 1000.");
                _MaxPerCycle = value;
            }
        }

        /// <summary>
        /// Finds the vessel's checkout on a connected Harbor when the Admiral has no repository for it (split mode). When
        /// null, only Admiral-side repositories are checked.
        /// </summary>
        public VesselHostResolver? Hosts { get; set; } = null;

        /// <summary>
        /// Raised after a mission is reconciled to Complete, with the recorded <see cref="EventType"/> event (for status
        /// events and dashboard broadcasts).
        /// </summary>
        public Func<Mission, ArmadaEvent, Task>? OnMissionReconciled { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[LandingReconciler] ";
        private readonly DatabaseDriver _Database;
        private readonly IGitService _Git;
        private readonly LoggingModule _Logging;
        private int _MaxPerCycle = 25;
        private int _NextPage = 1;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="git">Git service for repositories on the Admiral host.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public LandingReconciler(DatabaseDriver database, IGitService git, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Git = git ?? throw new ArgumentNullException(nameof(git));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one sweep: examine the next page of LandingFailed missions (oldest first, wrapping to the first page after
        /// the last) and complete each whose work is proven to be on its vessel's remote default branch.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of missions moved to Complete.</returns>
        public async Task<int> ReconcileAsync(CancellationToken token = default)
        {
            if (!await _Gate.WaitAsync(0, token).ConfigureAwait(false)) return 0;
            try
            {
                List<MissionSummary> batch = await ReadBatchAsync(token).ConfigureAwait(false);
                if (batch.Count == 0) return 0;

                Dictionary<string, LandingReconcileTarget> targets = new Dictionary<string, LandingReconcileTarget>(StringComparer.Ordinal);
                int reconciled = 0;
                foreach (MissionSummary summary in batch)
                {
                    token.ThrowIfCancellationRequested();
                    if (String.IsNullOrEmpty(summary.VesselId)) continue;

                    try
                    {
                        Mission? mission = await _Database.Missions.ReadAsync(summary.Id, token).ConfigureAwait(false);
                        if (mission == null || mission.Status != MissionStatusEnum.LandingFailed) continue;

                        if (!targets.TryGetValue(summary.VesselId!, out LandingReconcileTarget? target))
                        {
                            target = await ResolveTargetAsync(summary.VesselId!, mission.UserId, token).ConfigureAwait(false);
                            targets[summary.VesselId!] = target;
                        }

                        if (!target.Available)
                        {
                            _Logging.Debug(_Header + "mission " + mission.Id + " not checked: " + (target.Reason ?? "repository unavailable"));
                            continue;
                        }

                        if (await ReconcileMissionAsync(mission, target, token).ConfigureAwait(false)) reconciled++;
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                    {
                        _Logging.Warn(_Header + "error reconciling mission " + summary.Id + ": " + ex.Message);
                    }
                }

                return reconciled;
            }
            finally
            {
                _Gate.Release();
            }
        }

        #endregion

        #region Private-Methods

        private async Task<List<MissionSummary>> ReadBatchAsync(CancellationToken token)
        {
            EnumerationQuery query = new EnumerationQuery
            {
                Status = MissionStatusEnum.LandingFailed.ToString(),
                PageSize = _MaxPerCycle,
                PageNumber = _NextPage,
                Order = EnumerationOrderEnum.CreatedAscending
            };

            EnumerationResult<MissionSummary> page = await _Database.Missions.EnumerateSummariesAsync(query, token).ConfigureAwait(false);
            if (page.Objects.Count == 0 && query.PageNumber > 1)
            {
                query.PageNumber = 1;
                page = await _Database.Missions.EnumerateSummariesAsync(query, token).ConfigureAwait(false);
            }

            // The next sweep reads the next page, wrapping after the last, so a page of missions that can never be proven
            // does not starve the rest. Missions reconciled in this sweep shift later rows forward by up to a page; those
            // rows are reached again on the next wrap.
            _NextPage = page.TotalPages > query.PageNumber ? query.PageNumber + 1 : 1;
            return page.Objects;
        }

        private async Task<LandingReconcileTarget> ResolveTargetAsync(string vesselId, string? userId, CancellationToken token)
        {
            LandingReconcileTarget target = new LandingReconcileTarget();
            Vessel? vessel = await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (vessel == null)
            {
                target.Reason = "vessel " + vesselId + " not found";
                return target;
            }

            string branch = String.IsNullOrWhiteSpace(vessel.DefaultBranch) ? "main" : vessel.DefaultBranch!.Trim();
            target.TargetRef = "refs/remotes/origin/" + branch;

            // Armada's own bare repository first, then the operator's working clone, both on the Admiral host; otherwise
            // the vessel's checkout on a connected Harbor.
            if (!String.IsNullOrEmpty(vessel.LocalPath) && Directory.Exists(vessel.LocalPath))
            {
                target.Git = _Git;
                target.RepositoryPath = vessel.LocalPath;
            }
            else if (!String.IsNullOrEmpty(vessel.WorkingDirectory) && Directory.Exists(vessel.WorkingDirectory))
            {
                target.Git = _Git;
                target.RepositoryPath = vessel.WorkingDirectory;
            }
            else if (Hosts != null)
            {
                VesselHostResolution resolution = await Hosts.TryResolveAsync(vessel, userId, token).ConfigureAwait(false);
                if (resolution.Host == null)
                {
                    target.Reason = resolution.Message ?? "no host has a checkout of vessel " + vessel.Name;
                    return target;
                }

                target.Git = resolution.Host.Git;
                target.RepositoryPath = resolution.Host.WorkingDirectory;
                target.HarborId = resolution.Host.HarborId;
            }
            else
            {
                target.Reason = "vessel " + vessel.Name + " has no repository on the Admiral host and no Harbor resolver is configured";
                return target;
            }

            // Fetch once per vessel per sweep so origin/<branch> reflects the remote. A repository that cannot be fetched
            // (a dropped Harbor, an unreachable remote) is skipped rather than judged on stale refs.
            try
            {
                if (await target.Git.IsBareRepositoryAsync(target.RepositoryPath!, token).ConfigureAwait(false))
                    await target.Git.FetchAsync(target.RepositoryPath!, token).ConfigureAwait(false);
                else
                    await target.Git.FetchRemotesAsync(target.RepositoryPath!, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                target.Reason = "fetch failed in " + target.RepositoryPath + ": " + ex.Message;
                return target;
            }

            target.Available = true;
            return target;
        }

        private async Task<bool> ReconcileMissionAsync(Mission mission, LandingReconcileTarget target, CancellationToken token)
        {
            IGitService git = target.Git!;
            string repoPath = target.RepositoryPath!;
            string? evidence = null;
            LandingEvidenceKindEnum kind = LandingEvidenceKindEnum.CommitAncestry;

            if (!String.IsNullOrWhiteSpace(mission.CommitHash))
            {
                bool? contained = await git.IsAncestorAsync(repoPath, mission.CommitHash!.Trim(), target.TargetRef, token).ConfigureAwait(false);
                if (contained == true) evidence = mission.CommitHash.Trim();
            }

            if (evidence == null)
            {
                // Bounded by the mission's creation (with a day of slack for clock skew): its trailer cannot predate it.
                IReadOnlyList<string> trailered = await git.FindCommitsByTrailerAsync(
                    repoPath, target.TargetRef, MissionIdTrailerKey, mission.Id, mission.CreatedUtc.AddDays(-1), 1, token).ConfigureAwait(false);
                if (trailered.Count > 0 && !String.IsNullOrWhiteSpace(trailered[0]))
                {
                    evidence = trailered[0].Trim();
                    kind = LandingEvidenceKindEnum.MissionTrailer;
                }
            }

            if (evidence == null) return false;
            if (!MissionStateMachine.IsValidTransition(mission.Status, MissionStatusEnum.Complete)) return false;

            string targetName = target.TargetRef.StartsWith("refs/remotes/", StringComparison.Ordinal)
                ? target.TargetRef.Substring("refs/remotes/".Length)
                : target.TargetRef;
            string? previousReason = mission.FailureReason;

            mission.Status = MissionStatusEnum.Complete;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.LastUpdateUtc = DateTime.UtcNow;
            mission.FailureReason = null;
            mission.FailureKind = null;
            bool written = await _Database.Missions.TryUpdateIfStatusAsync(mission, new[] { MissionStatusEnum.LandingFailed }, token).ConfigureAwait(false);
            if (!written) return false;

            string how = kind == LandingEvidenceKindEnum.CommitAncestry
                ? "its commit " + evidence + " is on " + targetName
                : "commit " + evidence + " on " + targetName + " carries its " + MissionIdTrailerKey + " trailer";
            _Logging.Info(_Header + "mission " + mission.Id + " reconciled from LandingFailed to Complete: " + how);

            LandingReconciliationEvidence payload = new LandingReconciliationEvidence
            {
                MissionId = mission.Id,
                VesselId = mission.VesselId,
                EvidenceCommit = evidence,
                EvidenceKind = kind,
                TargetRef = target.TargetRef,
                HarborId = target.HarborId,
                PreviousFailureReason = previousReason,
                ReconciledUtc = DateTime.UtcNow
            };

            ArmadaEvent evt = new ArmadaEvent(EventType, "Landing reconciled (" + how + "): " + mission.Title);
            evt.TenantId = mission.TenantId;
            evt.UserId = mission.UserId;
            evt.EntityType = "mission";
            evt.EntityId = mission.Id;
            evt.MissionId = mission.Id;
            evt.VesselId = mission.VesselId;
            evt.VoyageId = mission.VoyageId;
            evt.Payload = JsonSerializer.Serialize(payload);
            try
            {
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not record " + EventType + " for mission " + mission.Id + ": " + ex.Message);
            }

            if (OnMissionReconciled != null)
            {
                try
                {
                    await OnMissionReconciled.Invoke(mission, evt).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "OnMissionReconciled callback failed for " + mission.Id + ": " + ex.Message);
                }
            }

            return true;
        }

        #endregion
    }
}

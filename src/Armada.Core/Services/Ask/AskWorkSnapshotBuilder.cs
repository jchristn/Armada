namespace Armada.Core.Services.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Builds the live data of an Ask Armada work card (<see cref="AskWorkSnapshot"/>) for a tracked voyage, mission,
    /// fleet action run, job, or vessel import batch, reading only rows of the tracked item's tenant. Also computes the
    /// snapshot hash used to detect changes.
    /// </summary>
    public class AskWorkSnapshotBuilder
    {
        #region Public-Members

        /// <summary>
        /// Maximum mission rows included for a fleet action run's Mission targets. Default 50, minimum 0, maximum 500.
        /// </summary>
        public int MaxLinkedMissionRows
        {
            get => _MaxLinkedMissionRows;
            set => _MaxLinkedMissionRows = value < 0 ? 0 : (value > 500 ? 500 : value);
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private int _MaxLinkedMissionRows = 50;

        private static readonly JsonSerializerOptions _HashOptions = new JsonSerializerOptions { WriteIndented = false };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public AskWorkSnapshotBuilder(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the snapshot of a tracked work item.
        /// </summary>
        /// <param name="work">Tracked work row.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The snapshot; Found is false when the entity no longer exists.</returns>
        /// <exception cref="ArgumentNullException">Thrown when work is null.</exception>
        public async Task<AskWorkSnapshot> BuildAsync(AskTrackedWork work, CancellationToken token = default)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            string tenantId = work.TenantId ?? Constants.DefaultTenantId;

            AskWorkSnapshot snapshot = new AskWorkSnapshot();
            snapshot.TrackedWorkId = work.Id;
            snapshot.ThreadId = work.ThreadId;
            snapshot.EntityType = work.EntityType;
            snapshot.EntityId = work.EntityId;
            snapshot.Title = work.Title;

            switch (work.EntityType)
            {
                case AskTrackedEntityTypeEnum.Voyage:
                    await BuildVoyageAsync(tenantId, snapshot, token).ConfigureAwait(false);
                    break;
                case AskTrackedEntityTypeEnum.Mission:
                    await BuildMissionAsync(tenantId, snapshot, token).ConfigureAwait(false);
                    break;
                case AskTrackedEntityTypeEnum.FleetActionRun:
                    await BuildFleetActionRunAsync(tenantId, snapshot, token).ConfigureAwait(false);
                    break;
                case AskTrackedEntityTypeEnum.Job:
                    await BuildJobAsync(tenantId, snapshot, token).ConfigureAwait(false);
                    break;
                case AskTrackedEntityTypeEnum.VesselImportBatch:
                    await BuildImportBatchAsync(tenantId, snapshot, token).ConfigureAwait(false);
                    break;
            }

            if (!snapshot.Found)
            {
                snapshot.State = AskTrackedWorkStateEnum.Cancelled;
                if (String.IsNullOrEmpty(snapshot.ErrorText)) snapshot.ErrorText = work.EntityType + " " + work.EntityId + " no longer exists.";
            }

            snapshot.CapturedUtc = DateTime.UtcNow;
            snapshot.Progress = snapshot.TotalCount > 0 ? (int)Math.Round(100.0 * (snapshot.CompletedCount + snapshot.FailedCount) / snapshot.TotalCount) : (snapshot.State == AskTrackedWorkStateEnum.Active ? 0 : 100);
            return snapshot;
        }

        /// <summary>
        /// Deterministic hash of a snapshot's content (SHA-256 of its JSON, hex).
        /// </summary>
        /// <param name="snapshot">Snapshot.</param>
        /// <returns>Lowercase hex hash.</returns>
        /// <exception cref="ArgumentNullException">Thrown when snapshot is null.</exception>
        public static string ComputeHash(AskWorkSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            DateTime? captured = snapshot.CapturedUtc;
            snapshot.CapturedUtc = null;
            string json;
            try { json = JsonSerializer.Serialize(snapshot, _HashOptions); }
            finally { snapshot.CapturedUtc = captured; }
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Coarse state of a voyage status.
        /// </summary>
        /// <param name="status">Voyage status.</param>
        /// <returns>The state.</returns>
        public static AskTrackedWorkStateEnum StateOf(VoyageStatusEnum status)
        {
            switch (status)
            {
                case VoyageStatusEnum.Complete: return AskTrackedWorkStateEnum.Succeeded;
                case VoyageStatusEnum.Failed: return AskTrackedWorkStateEnum.Failed;
                case VoyageStatusEnum.Cancelled: return AskTrackedWorkStateEnum.Cancelled;
                default: return AskTrackedWorkStateEnum.Active;
            }
        }

        /// <summary>
        /// Coarse state of a mission status. LandingFailed counts as failed.
        /// </summary>
        /// <param name="status">Mission status.</param>
        /// <returns>The state.</returns>
        public static AskTrackedWorkStateEnum StateOf(MissionStatusEnum status)
        {
            switch (status)
            {
                case MissionStatusEnum.Complete: return AskTrackedWorkStateEnum.Succeeded;
                case MissionStatusEnum.Failed:
                case MissionStatusEnum.LandingFailed: return AskTrackedWorkStateEnum.Failed;
                case MissionStatusEnum.Cancelled: return AskTrackedWorkStateEnum.Cancelled;
                default: return AskTrackedWorkStateEnum.Active;
            }
        }

        #endregion

        #region Private-Methods

        private async Task BuildVoyageAsync(string tenantId, AskWorkSnapshot snapshot, CancellationToken token)
        {
            Voyage? voyage = await _Database.Voyages.ReadAsync(tenantId, snapshot.EntityId, token).ConfigureAwait(false);
            if (voyage == null)
            {
                snapshot.Found = false;
                return;
            }

            snapshot.Title = voyage.Title;
            snapshot.Status = voyage.Status.ToString();
            snapshot.State = StateOf(voyage.Status);
            snapshot.CompletedUtc = voyage.CompletedUtc;

            List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(tenantId, voyage.Id, token).ConfigureAwait(false);
            Dictionary<string, string?> captainNames = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (Mission mission in missions.OrderBy(m => m.CreatedUtc).ThenBy(m => m.Id, StringComparer.Ordinal))
            {
                snapshot.Missions.Add(await BuildMissionRowAsync(tenantId, mission, captainNames, token).ConfigureAwait(false));
            }

            ApplyMissionCounts(snapshot);

            // Armada can mark a voyage Complete while a mission is still landing (pull request open, merge queue). Keep
            // following the voyage until every mission has settled, so landing outcomes still reach the thread.
            if (snapshot.State != AskTrackedWorkStateEnum.Active
                && snapshot.State != AskTrackedWorkStateEnum.Cancelled
                && snapshot.Missions.Any(m => SettledState(m) == AskTrackedWorkStateEnum.Active))
            {
                snapshot.State = AskTrackedWorkStateEnum.Active;
                snapshot.CompletedUtc = null;
            }

            snapshot.StartedUtc = snapshot.Missions.Where(m => m.StartedUtc.HasValue).Select(m => m.StartedUtc).Min();
            if (snapshot.State == AskTrackedWorkStateEnum.Failed && String.IsNullOrEmpty(snapshot.ErrorText))
                snapshot.ErrorText = snapshot.Missions.Select(m => m.FailureReason).FirstOrDefault(r => !String.IsNullOrEmpty(r));
        }

        private async Task BuildMissionAsync(string tenantId, AskWorkSnapshot snapshot, CancellationToken token)
        {
            Mission? mission = await _Database.Missions.ReadAsync(tenantId, snapshot.EntityId, token).ConfigureAwait(false);
            if (mission == null)
            {
                snapshot.Found = false;
                return;
            }

            AskWorkMissionSnapshot row = await BuildMissionRowAsync(tenantId, mission, new Dictionary<string, string?>(StringComparer.Ordinal), token).ConfigureAwait(false);
            snapshot.Title = mission.Title;
            snapshot.Status = mission.Status.ToString();
            snapshot.Missions.Add(row);
            snapshot.State = SettledState(row);
            snapshot.StartedUtc = mission.StartedUtc;
            snapshot.CompletedUtc = mission.CompletedUtc;
            snapshot.ErrorText = mission.FailureReason;
            ApplyMissionCounts(snapshot);
        }

        private async Task BuildFleetActionRunAsync(string tenantId, AskWorkSnapshot snapshot, CancellationToken token)
        {
            FleetActionRun? run = await _Database.FleetActionRuns.ReadAsync(tenantId, snapshot.EntityId, token).ConfigureAwait(false);
            if (run == null)
            {
                snapshot.Found = false;
                return;
            }

            snapshot.Status = run.Status.ToString();
            snapshot.StartedUtc = run.StartedUtc;
            snapshot.CompletedUtc = run.CompletedUtc;
            switch (run.Status)
            {
                case FleetActionRunStatusEnum.Completed: snapshot.State = AskTrackedWorkStateEnum.Succeeded; break;
                case FleetActionRunStatusEnum.CompletedWithFailures:
                case FleetActionRunStatusEnum.Failed: snapshot.State = AskTrackedWorkStateEnum.Failed; break;
                case FleetActionRunStatusEnum.Cancelled: snapshot.State = AskTrackedWorkStateEnum.Cancelled; break;
                default: snapshot.State = AskTrackedWorkStateEnum.Active; break;
            }

            List<FleetActionRunTarget> targets = await _Database.FleetActionRunTargets.ReadAllByRunAsync(run.Id, token).ConfigureAwait(false);
            Dictionary<string, string?> captainNames = new Dictionary<string, string?>(StringComparer.Ordinal);
            int linkedMissionRows = 0;
            foreach (FleetActionRunTarget target in targets.Where(t => String.Equals(t.TenantId ?? tenantId, tenantId, StringComparison.Ordinal)).OrderBy(t => t.VesselName, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id, StringComparer.Ordinal))
            {
                AskWorkTargetSnapshot row = new AskWorkTargetSnapshot();
                row.Id = target.Id;
                row.VesselId = target.VesselId;
                row.VesselName = target.VesselName;
                row.Status = target.Status.ToString();
                row.Reason = !String.IsNullOrEmpty(target.FailureReason) ? target.FailureReason : target.SkipReason;
                row.VoyageId = target.VoyageId;
                row.ExitCode = target.ExitCode;
                snapshot.Targets.Add(row);
                Count(snapshot.Counts, row.Status);

                switch (target.Status)
                {
                    case FleetActionTargetStatusEnum.Succeeded:
                    case FleetActionTargetStatusEnum.Skipped:
                        snapshot.CompletedCount++;
                        break;
                    case FleetActionTargetStatusEnum.Failed:
                    case FleetActionTargetStatusEnum.TimedOut:
                    case FleetActionTargetStatusEnum.Cancelled:
                        snapshot.FailedCount++;
                        break;
                    default:
                        snapshot.ActiveCount++;
                        break;
                }

                if (!String.IsNullOrEmpty(target.VoyageId) && linkedMissionRows < _MaxLinkedMissionRows)
                {
                    List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(tenantId, target.VoyageId!, token).ConfigureAwait(false);
                    row.MissionId = missions.OrderBy(m => m.CreatedUtc).Select(m => m.Id).FirstOrDefault();
                    foreach (Mission mission in missions.OrderBy(m => m.CreatedUtc).ThenBy(m => m.Id, StringComparer.Ordinal))
                    {
                        if (linkedMissionRows >= _MaxLinkedMissionRows) break;
                        snapshot.Missions.Add(await BuildMissionRowAsync(tenantId, mission, captainNames, token).ConfigureAwait(false));
                        linkedMissionRows++;
                    }
                }
            }

            snapshot.TotalCount = snapshot.Targets.Count;
            snapshot.Counts = Sorted(snapshot.Counts);
            if (snapshot.State == AskTrackedWorkStateEnum.Failed)
                snapshot.ErrorText = snapshot.Targets.Select(t => t.Reason).FirstOrDefault(r => !String.IsNullOrEmpty(r));
        }

        private async Task BuildJobAsync(string tenantId, AskWorkSnapshot snapshot, CancellationToken token)
        {
            Job? job = await _Database.Jobs.ReadAsync(tenantId, snapshot.EntityId, token).ConfigureAwait(false);
            if (job == null)
            {
                snapshot.Found = false;
                return;
            }

            if (!String.IsNullOrEmpty(job.Name)) snapshot.Title = job.Name;
            snapshot.Status = job.Status.ToString();
            snapshot.StartedUtc = job.StartedUtc;
            snapshot.CompletedUtc = job.CompletedUtc;
            snapshot.ErrorText = job.ErrorReason;
            snapshot.TotalCount = 1;
            switch (job.Status)
            {
                case JobStatusEnum.Succeeded: snapshot.State = AskTrackedWorkStateEnum.Succeeded; snapshot.CompletedCount = 1; break;
                case JobStatusEnum.Failed: snapshot.State = AskTrackedWorkStateEnum.Failed; snapshot.FailedCount = 1; break;
                case JobStatusEnum.Cancelled: snapshot.State = AskTrackedWorkStateEnum.Cancelled; snapshot.FailedCount = 1; break;
                default: snapshot.State = AskTrackedWorkStateEnum.Active; snapshot.ActiveCount = 1; break;
            }

            snapshot.Counts[job.Status.ToString()] = 1;
            snapshot.Progress = job.Progress;
        }

        private async Task BuildImportBatchAsync(string tenantId, AskWorkSnapshot snapshot, CancellationToken token)
        {
            VesselImportBatch? batch = await _Database.VesselImportBatches.ReadAsync(tenantId, snapshot.EntityId, token).ConfigureAwait(false);
            if (batch == null)
            {
                snapshot.Found = false;
                return;
            }

            snapshot.Status = batch.Status.ToString();
            snapshot.CompletedUtc = batch.CompletedUtc;
            snapshot.StartedUtc = batch.CreatedUtc;
            snapshot.ErrorText = batch.ErrorMessage;
            switch (batch.Status)
            {
                case VesselImportBatchStatusEnum.Discovered:
                case VesselImportBatchStatusEnum.Completed: snapshot.State = AskTrackedWorkStateEnum.Succeeded; break;
                case VesselImportBatchStatusEnum.CompletedWithFailures:
                case VesselImportBatchStatusEnum.Failed: snapshot.State = AskTrackedWorkStateEnum.Failed; break;
                default: snapshot.State = AskTrackedWorkStateEnum.Active; break;
            }

            List<VesselImportItem> items = await _Database.VesselImportItems.EnumerateByBatchAsync(tenantId, batch.Id, token).ConfigureAwait(false);
            snapshot.TotalCount = items.Count;
            foreach (VesselImportItem item in items)
            {
                Count(snapshot.Counts, item.Outcome.ToString());
                switch (item.Outcome)
                {
                    case VesselImportOutcomeEnum.Created:
                    case VesselImportOutcomeEnum.SkippedExisting:
                    case VesselImportOutcomeEnum.SkippedNotSelected:
                        snapshot.CompletedCount++;
                        break;
                    case VesselImportOutcomeEnum.Failed:
                        snapshot.FailedCount++;
                        break;
                    default:
                        snapshot.ActiveCount++;
                        break;
                }
            }

            snapshot.Counts = Sorted(snapshot.Counts);
        }

        private async Task<AskWorkMissionSnapshot> BuildMissionRowAsync(string tenantId, Mission mission, Dictionary<string, string?> captainNames, CancellationToken token)
        {
            AskWorkMissionSnapshot row = new AskWorkMissionSnapshot();
            row.Id = mission.Id;
            row.Title = mission.Title;
            row.Status = mission.Status.ToString();
            row.VoyageId = mission.VoyageId;
            row.VesselId = mission.VesselId;
            row.Persona = mission.Persona;
            row.PipelineStage = mission.Persona;
            row.CaptainId = mission.CaptainId;
            row.BranchName = mission.BranchName;
            row.PrUrl = mission.PrUrl;
            row.FailureReason = mission.FailureReason;
            row.StartedUtc = mission.StartedUtc;
            row.CompletedUtc = mission.CompletedUtc;

            if (!String.IsNullOrEmpty(mission.CaptainId))
            {
                if (!captainNames.TryGetValue(mission.CaptainId!, out string? name))
                {
                    Captain? captain = await _Database.Captains.ReadAsync(tenantId, mission.CaptainId!, token).ConfigureAwait(false);
                    name = captain?.Name;
                    captainNames[mission.CaptainId!] = name;
                }

                row.CaptainName = name;
            }

            CheckRunQuery checkQuery = new CheckRunQuery();
            checkQuery.TenantId = tenantId;
            checkQuery.MissionId = mission.Id;
            checkQuery.PageNumber = 1;
            checkQuery.PageSize = 1;
            EnumerationResult<CheckRun> checks = await _Database.CheckRuns.EnumerateAsync(checkQuery, token).ConfigureAwait(false);
            CheckRun? latestCheck = checks.Objects.FirstOrDefault();
            row.CheckRunId = latestCheck?.Id;
            row.CheckRunStatus = latestCheck?.Status.ToString();

            EnumerationQuery mergeQuery = new EnumerationQuery();
            mergeQuery.MissionId = mission.Id;
            mergeQuery.PageNumber = 1;
            mergeQuery.PageSize = 1;
            EnumerationResult<MergeEntry> merges = await _Database.MergeEntries.EnumerateAsync(tenantId, mergeQuery, token).ConfigureAwait(false);
            MergeEntry? merge = merges.Objects.FirstOrDefault();
            row.MergeEntryId = merge?.Id;
            row.MergeQueueStatus = merge?.Status.ToString();
            row.MergeStatus = row.MergeQueueStatus;

            if (mission.Status == MissionStatusEnum.LandingFailed) row.LandingOutcome = "LandingFailed";
            else if (mission.Status == MissionStatusEnum.PullRequestOpen) row.LandingOutcome = "PullRequestOpen";
            else if (mission.Status == MissionStatusEnum.Complete && merge != null && merge.Status == MergeStatusEnum.Landed) row.LandingOutcome = "Landed";
            else if (mission.Status == MissionStatusEnum.Complete && !String.IsNullOrEmpty(mission.PrUrl)) row.LandingOutcome = "PullRequestMerged";
            else if (mission.Status == MissionStatusEnum.Complete) row.LandingOutcome = "Landed";
            return row;
        }

        /// <summary>
        /// Whether a mission row has settled: Complete succeeded; Failed, LandingFailed, and Cancelled failed; WorkProduced
        /// counts as succeeded (work produced, nothing landing) unless its merge-queue entry is still queued, testing, or
        /// passed-but-not-landed; everything else (including an open pull request) is still active.
        /// </summary>
        /// <param name="row">Mission row.</param>
        /// <returns>The settled state, or Active.</returns>
        public static AskTrackedWorkStateEnum SettledState(AskWorkMissionSnapshot row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            MissionStatusEnum status;
            if (!Enum.TryParse<MissionStatusEnum>(row.Status, out status)) return AskTrackedWorkStateEnum.Active;
            if (status == MissionStatusEnum.WorkProduced)
            {
                bool landing = row.MergeQueueStatus == MergeStatusEnum.Queued.ToString()
                    || row.MergeQueueStatus == MergeStatusEnum.Testing.ToString()
                    || row.MergeQueueStatus == MergeStatusEnum.Passed.ToString();
                return landing ? AskTrackedWorkStateEnum.Active : AskTrackedWorkStateEnum.Succeeded;
            }

            return StateOf(status);
        }

        private static void ApplyMissionCounts(AskWorkSnapshot snapshot)
        {
            snapshot.TotalCount = snapshot.Missions.Count;
            snapshot.CompletedCount = 0;
            snapshot.FailedCount = 0;
            snapshot.ActiveCount = 0;
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (AskWorkMissionSnapshot row in snapshot.Missions)
            {
                Count(counts, row.Status);
                AskTrackedWorkStateEnum state = SettledState(row);
                if (state == AskTrackedWorkStateEnum.Succeeded) snapshot.CompletedCount++;
                else if (state == AskTrackedWorkStateEnum.Active) snapshot.ActiveCount++;
                else snapshot.FailedCount++;
            }

            snapshot.Counts = Sorted(counts);
        }

        private static void Count(Dictionary<string, int> counts, string key)
        {
            if (String.IsNullOrEmpty(key)) return;
            counts.TryGetValue(key, out int current);
            counts[key] = current + 1;
        }

        private static Dictionary<string, int> Sorted(Dictionary<string, int> counts)
        {
            Dictionary<string, int> sorted = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> kvp in counts.OrderBy(k => k.Key, StringComparer.Ordinal)) sorted[kvp.Key] = kvp.Value;
            return sorted;
        }

        #endregion
    }
}

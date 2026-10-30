namespace Armada.Core.Services
{
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Globalization;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;


    /// <summary>
    /// Merge queue that processes vessel+target-branch groups in parallel, while
    /// entries within each group are processed sequentially one at a time.  Each
    /// successful merge is landed immediately so the next entry in the same group
    /// merges against the up-to-date target branch, eliminating the cascade
    /// failures that occur with batch-style processing.
    /// </summary>
    public class MergeQueueService : IMergeQueueService
    {
        #region Public-Members

        /// <summary>
        /// Where docks live. An entry whose mission ran in a Harbor dock is processed on that Harbor, in the repository
        /// that holds its branch. Without it every entry is processed in the Admiral's own clone of the vessel.
        /// </summary>
        public DockHostResolver? DockHosts { get; set; } = null;

        /// <summary>Optional executor for local final checks; git still runs on the entry's owning host.</summary>
        public IHostCommandExecutor? LocalVerificationCommands { get; set; }

        /// <summary>
        /// Raised after the queue moves a linked mission to Complete (the entry landed) or LandingFailed (it could not),
        /// once the mission row and its mission.completed / mission.landing_failed event are stored. The Admiral uses it
        /// for the same WebSocket broadcasts the landing handler sends. Exceptions it throws are logged and ignored.
        /// </summary>
        public Func<Mission, Task>? OnMissionReconciled { get; set; } = null;

        /// <summary>
        /// Black box capture for missions the queue moves to LandingFailed, or null.
        /// </summary>
        public IBlackBoxCapture? BlackBox { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Header = "[MergeQueue] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private IGitService _Git;

        private bool _Processing = false;

        /// <summary>
        /// Hard ceiling on any single merge-queue git or test subprocess. A hung operation is
        /// killed and surfaced as a failure rather than wedging the queue (and its _Processing
        /// guard) indefinitely.
        /// </summary>
        private static readonly TimeSpan _OperationTimeout = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Identity of this Admiral instance for the cross-instance merge-queue processing lease.
        /// </summary>
        private readonly string _InstanceId = "mq-" + Guid.NewGuid().ToString("N");

        /// <summary>
        /// Name of the durable lease that serializes queue processing across instances.
        /// </summary>
        private const string _ProcessLeaseName = "merge-queue:process";
        private readonly object _ProcessLock = new object();

        /// <summary>
        /// Prefix of the temporary integration branches (armada/merge-queue/mq_{entryId}_{yyyyMMddHHmmss}).
        /// </summary>
        private const string _IntegrationBranchPrefix = "armada/merge-queue/";

        /// <summary>
        /// Integration branches this instance is using right now; never pruned as leftovers.
        /// </summary>
        private readonly ConcurrentDictionary<string, byte> _ActiveIntegrationBranches = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="git">Git service.</param>
        public MergeQueueService(LoggingModule logging, DatabaseDriver database, ArmadaSettings settings, IGitService git)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Git = git ?? throw new ArgumentNullException(nameof(git));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<MergeEntry> EnqueueAsync(MergeEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            entry.Status = MergeStatusEnum.Queued;
            entry.CreatedUtc = DateTime.UtcNow;
            entry.LastUpdateUtc = DateTime.UtcNow;
            await _Database.MergeEntries.CreateAsync(entry, token).ConfigureAwait(false);

            _Logging.Info(_Header + "enqueued " + entry.Id + " branch " + entry.BranchName + " -> " + entry.TargetBranch);
            return entry;
        }

        /// <inheritdoc />
        public async Task ProcessQueueAsync(CancellationToken token = default)
        {
            // ProcessQueueAsync is a background/system method (called from Admiral loop).
            // It processes all tenants' entries, so unscoped calls are appropriate here.
            lock (_ProcessLock)
            {
                if (_Processing) return;
                _Processing = true;
            }

            bool leaseAcquired = false;
            try
            {
                // Cross-instance guard: only one Admiral processes the queue at a time. The lease
                // TTL guarantees a crashed holder does not block the queue forever, and takeover is
                // automatic once it expires.
                leaseAcquired = await _Database.CoordinationLeases
                    .TryAcquireAsync(_ProcessLeaseName, _InstanceId, TimeSpan.FromMinutes(15), null, token)
                    .ConfigureAwait(false);
                if (!leaseAcquired)
                {
                    _Logging.Debug(_Header + "merge queue already being processed by another instance -- skipping");
                    return;
                }

                // Get all queued entries ordered by priority then created_utc
                List<MergeEntry> queued = await _Database.MergeEntries.EnumerateByStatusAsync(MergeStatusEnum.Queued, token).ConfigureAwait(false);

                if (queued.Count == 0) return;

                // Group by vessel + target branch (independent repos can process independently)
                IEnumerable<IGrouping<string, MergeEntry>> groups = queued.GroupBy(
                    e => (e.VesselId ?? "default") + ":" + e.TargetBranch);

                List<Task> groupTasks = new List<Task>();
                MergeQueuePass pass = new MergeQueuePass(DateTime.UtcNow);

                foreach (IGrouping<string, MergeEntry> group in groups)
                {
                    // Order by priority (lower = higher priority) then by creation time ascending
                    List<MergeEntry> entries = group
                        .OrderBy(e => e.Priority)
                        .ThenBy(e => e.CreatedUtc)
                        .ToList();

                    groupTasks.Add(ProcessGroupSafeAsync(entries, pass, token));
                }

                await Task.WhenAll(groupTasks).ConfigureAwait(false);
            }
            finally
            {
                if (leaseAcquired)
                {
                    try
                    {
                        await _Database.CoordinationLeases.ReleaseAsync(_ProcessLeaseName, _InstanceId, token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "error releasing merge-queue processing lease: " + ex.ToString());
                    }
                }
                lock (_ProcessLock) { _Processing = false; }
            }
        }

        /// <inheritdoc />
        public async Task CancelAsync(string entryId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));

            MergeEntry? entry = !String.IsNullOrEmpty(tenantId)
                ? await _Database.MergeEntries.ReadAsync(tenantId, entryId, token).ConfigureAwait(false)
                : await _Database.MergeEntries.ReadAsync(entryId, token).ConfigureAwait(false);
            if (entry != null)
            {
                entry.Status = MergeStatusEnum.Cancelled;
                entry.LastUpdateUtc = DateTime.UtcNow;
                entry.CompletedUtc = DateTime.UtcNow;
                await _Database.MergeEntries.UpdateAsync(entry, token).ConfigureAwait(false);
                _Logging.Info(_Header + "cancelled " + entryId);
            }
        }

        /// <inheritdoc />
        public async Task<List<MergeEntry>> ListAsync(string? tenantId = null, CancellationToken token = default)
        {
            List<MergeEntry> results = !String.IsNullOrEmpty(tenantId)
                ? await _Database.MergeEntries.EnumerateAsync(tenantId, token).ConfigureAwait(false)
                : await _Database.MergeEntries.EnumerateAsync(token).ConfigureAwait(false);
            return results;
        }

        /// <inheritdoc />
        public async Task<MergeEntry?> ProcessSingleAsync(string entryId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));

            MergeEntry? entry = !String.IsNullOrEmpty(tenantId)
                ? await _Database.MergeEntries.ReadAsync(tenantId, entryId, token).ConfigureAwait(false)
                : await _Database.MergeEntries.ReadAsync(entryId, token).ConfigureAwait(false);
            if (entry == null) return null;
            if (entry.Status != MergeStatusEnum.Queued) return null;

            _Logging.Debug(_Header + "processing single entry " + entryId);
            await ProcessEntryAsync(entry, new MergeQueuePass(DateTime.UtcNow), token).ConfigureAwait(false);

            // Re-read from DB to get updated state
            return !String.IsNullOrEmpty(tenantId)
                ? await _Database.MergeEntries.ReadAsync(tenantId, entryId, token).ConfigureAwait(false)
                : await _Database.MergeEntries.ReadAsync(entryId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<MergeEntry?> GetAsync(string entryId, string? tenantId = null, CancellationToken token = default)
        {
            MergeEntry? entry = !String.IsNullOrEmpty(tenantId)
                ? await _Database.MergeEntries.ReadAsync(tenantId, entryId, token).ConfigureAwait(false)
                : await _Database.MergeEntries.ReadAsync(entryId, token).ConfigureAwait(false);
            return entry;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string entryId, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));

            MergeEntry? entry = !String.IsNullOrEmpty(tenantId)
                ? await _Database.MergeEntries.ReadAsync(tenantId, entryId, token).ConfigureAwait(false)
                : await _Database.MergeEntries.ReadAsync(entryId, token).ConfigureAwait(false);
            if (entry == null) return false;

            // Only allow deletion of terminal entries
            if (entry.Status != MergeStatusEnum.Cancelled &&
                entry.Status != MergeStatusEnum.Landed &&
                entry.Status != MergeStatusEnum.Failed)
            {
                _Logging.Warn(_Header + "cannot delete " + entryId + " in non-terminal status " + entry.Status);
                return false;
            }

            // Clean up the entry's branch only when its work is in the target branch. A Failed entry (and a Cancelled one
            // that never landed) keeps its branch locally and on the remote: in a Harbor's repository, which is often the
            // user's own checkout, it can be the only copy of the mission's work.
            await CleanupEntryBranchAsync(entry, token).ConfigureAwait(false);

            if (!String.IsNullOrEmpty(tenantId))
                await _Database.MergeEntries.DeleteAsync(tenantId, entryId, token).ConfigureAwait(false);
            else
                await _Database.MergeEntries.DeleteAsync(entryId, token).ConfigureAwait(false);

            _Logging.Info(_Header + "deleted " + entryId);
            return true;
        }

        /// <inheritdoc />
        public async Task<MergeQueuePurgeResult> DeleteMultipleAsync(List<string> entryIds, string? tenantId = null, CancellationToken token = default)
        {
            if (entryIds == null) throw new ArgumentNullException(nameof(entryIds));
            CollectionLimits.RequireRequestItems(entryIds, "EntryIds");

            MergeQueuePurgeResult result = new MergeQueuePurgeResult();

            foreach (string entryId in entryIds)
            {
                if (String.IsNullOrEmpty(entryId))
                {
                    result.Skipped.Add(new MergeQueuePurgeSkipped(entryId ?? "", "Empty entry ID"));
                    continue;
                }

                MergeEntry? entry = !String.IsNullOrEmpty(tenantId)
                    ? await _Database.MergeEntries.ReadAsync(tenantId, entryId, token).ConfigureAwait(false)
                    : await _Database.MergeEntries.ReadAsync(entryId, token).ConfigureAwait(false);
                if (entry == null)
                {
                    result.Skipped.Add(new MergeQueuePurgeSkipped(entryId, "Not found"));
                    continue;
                }

                bool deleted = await DeleteAsync(entryId, tenantId, token).ConfigureAwait(false);
                if (deleted)
                {
                    result.EntriesPurged++;
                }
                else
                {
                    result.Skipped.Add(new MergeQueuePurgeSkipped(entryId, "Not in terminal state (status: " + entry.Status + ")"));
                }
            }

            _Logging.Info(_Header + "batch purge: " + result.EntriesPurged + " purged, " + result.Skipped.Count + " skipped");
            return result;
        }

        /// <inheritdoc />
        public async Task<int> PurgeTerminalAsync(string? vesselId = null, MergeStatusEnum? status = null, string? tenantId = null, CancellationToken token = default)
        {
            List<MergeStatusEnum> terminalStatuses = new List<MergeStatusEnum>
            {
                MergeStatusEnum.Landed,
                MergeStatusEnum.Failed,
                MergeStatusEnum.Cancelled
            };

            if (status != null && !terminalStatuses.Contains(status.Value))
            {
                _Logging.Warn(_Header + "purge requested for non-terminal status " + status.Value);
                return 0;
            }

            List<MergeStatusEnum> statusesToPurge = status != null
                ? new List<MergeStatusEnum> { status.Value }
                : terminalStatuses;

            List<MergeEntry> candidates = new List<MergeEntry>();
            foreach (MergeStatusEnum s in statusesToPurge)
            {
                // PurgeTerminalAsync enumerates by status (no tenant-scoped overload for EnumerateByStatusAsync).
                // Use unscoped enumeration and filter by tenantId in-memory if needed.
                List<MergeEntry> entries = await _Database.MergeEntries.EnumerateByStatusAsync(s, token).ConfigureAwait(false);
                candidates.AddRange(entries);
            }

            if (!String.IsNullOrEmpty(vesselId))
            {
                candidates = candidates.Where(e => e.VesselId == vesselId).ToList();
            }

            int deleted = 0;
            foreach (MergeEntry entry in candidates)
            {
                bool result = await DeleteAsync(entry.Id, tenantId, token).ConfigureAwait(false);
                if (result) deleted++;
            }

            _Logging.Info(_Header + "purged " + deleted + " terminal entries");
            return deleted;
        }

        /// <summary>
        /// Why an entry should not be enqueued because its work has already landed: its mission records a landing commit, or its
        /// branch is already contained in the target branch in the repository that holds it. Null when it may be enqueued
        /// (including when the repository cannot be checked right now, for example while its Harbor is disconnected).
        /// </summary>
        /// <param name="entry">Entry about to be enqueued.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A message saying why the entry is refused, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        public async Task<string?> GetAlreadyLandedReasonAsync(MergeEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            // API-key/global-admin calls use the system tenant, which must not hide the referenced mission.
            string? tenantId = entry.TenantId == Constants.SystemTenantId ? null : entry.TenantId;
            Mission? mission = await ReadMissionAsync(entry.MissionId, tenantId, token).ConfigureAwait(false);
            if (mission != null && mission.Status == MissionStatusEnum.Complete && !String.IsNullOrWhiteSpace(mission.CommitHash))
            {
                return "Mission " + mission.Id + " (" + mission.Title + ") already landed: it records landing commit " + mission.CommitHash + ", so there is nothing to merge."
                    + " Dispatch a new mission for further changes.";
            }

            string? mismatch = mission?.Status == MissionStatusEnum.Complete
                ? "Mission " + mission.Id + " has a status mismatch: Complete without a recorded landing commit or confirmed target-branch containment. Verify landing and correct the mission state before retrying."
                : null;
            if (String.IsNullOrEmpty(entry.BranchName) || String.IsNullOrEmpty(entry.TargetBranch)) return mismatch;

            try
            {
                Dock? dock = await FindBranchDockAsync(entry, mission, token).ConfigureAwait(false);
                if (IsWaitingOnHarbor(dock)) return mismatch;
                MergeQueueHost? host = await ResolveHostAsync(entry, dock, token).ConfigureAwait(false);
                if (host == null) return mismatch;
                bool? merged = await IsBranchMergedAsync(host, entry.BranchName, entry.TargetBranch, false, token).ConfigureAwait(false);
                if (merged == true)
                {
                    return "Branch " + entry.BranchName + " is already contained in " + entry.TargetBranch
                        + ", so there is nothing to merge.";
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "could not check whether " + entry.BranchName + " already landed: " + ex.Message);
            }

            return mismatch;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Wraps <see cref="ProcessGroupAsync"/> in a try-catch so that an
        /// unexpected exception in one group does not cancel other groups.
        /// </summary>
        private async Task ProcessGroupSafeAsync(List<MergeEntry> entries, MergeQueuePass pass, CancellationToken token)
        {
            try
            {
                await ProcessGroupAsync(entries, pass, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "group processing error: " + ex.ToString());
            }
        }

        /// <summary>
        /// Process a group of entries that share the same vessel and target branch.
        /// Entries are processed one at a time in priority/creation order.  Each
        /// successful merge is landed immediately so the next entry merges against
        /// the updated target.
        /// </summary>
        private async Task ProcessGroupAsync(List<MergeEntry> entries, MergeQueuePass pass, CancellationToken token)
        {
            if (entries.Count == 0) return;

            MergeEntry first = entries[0];
            _Logging.Debug(_Header + "processing " + entries.Count + " entries for " + first.TargetBranch + " on vessel " + (first.VesselId ?? "default"));

            // Each entry resolves its own host: a vessel's queue can hold entries from missions that ran on this machine
            // and on different Harbors.
            foreach (MergeEntry entry in entries)
            {
                await ProcessEntryAsync(entry, pass, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Process a single merge entry: fetch, merge, test, land.
        /// If any step fails the entry is marked Failed (and its mission LandingFailed) and processing continues to the
        /// next entry in the group. An entry whose branch is on a Harbor that is not connected is left Queued, with the
        /// reason in its TestOutput, and retried on a later pass.
        /// </summary>
        private async Task ProcessEntryAsync(MergeEntry entry, MergeQueuePass pass, CancellationToken token)
        {
            MergeQueueHost? host;
            try
            {
                Mission? mission = await ReadMissionAsync(entry.MissionId, entry.TenantId, token).ConfigureAwait(false);
                Dock? dock = await FindBranchDockAsync(entry, mission, token).ConfigureAwait(false);
                if (IsWaitingOnHarbor(dock))
                {
                    string waitReason = await HarborWaitReasonAsync(entry, dock!.HarborId!, dock.RepositoryPath, token).ConfigureAwait(false);
                    await WaitForHarborAsync(entry, waitReason, token).ConfigureAwait(false);
                    return;
                }

                host = await ResolveHostAsync(entry, dock, token).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                await FailUnprocessedAsync(entry, ex.Message, token).ConfigureAwait(false);
                return;
            }

            if (host == null)
            {
                await FailUnprocessedAsync(entry, "Unable to resolve repository path for vessel " + entry.VesselId, token).ConfigureAwait(false);
                return;
            }

            await PruneLeftoverIntegrationsAsync(host, pass, token).ConfigureAwait(false);
            await ProcessEntryAsync(entry, host, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Leave an entry Queued while the Harbor holding its branch is away, recording why on the entry. Logged and
        /// stored only when the reason changes, so a long outage does not log on every pass.
        /// </summary>
        private async Task WaitForHarborAsync(MergeEntry entry, string reason, CancellationToken token)
        {
            if (entry.Status == MergeStatusEnum.Queued && String.Equals(entry.TestOutput, reason, StringComparison.Ordinal)) return;

            _Logging.Info(_Header + "entry " + entry.Id + " stays queued: " + reason);
            entry.Status = MergeStatusEnum.Queued;
            entry.TestOutput = reason;
            entry.TestExitCode = null;
            entry.TestStartedUtc = null;
            entry.CompletedUtc = null;
            if (!await _Database.MergeEntries.TryUpdateIfUnchangedAsync(entry, entry.LastUpdateUtc, token).ConfigureAwait(false)) return;
        }

        private async Task FailUnprocessedAsync(MergeEntry entry, string reason, CancellationToken token)
        {
            _Logging.Warn(_Header + "cannot process " + entry.Id + ": " + reason);
            entry.Status = MergeStatusEnum.Failed;
            entry.TestOutput = reason;
            entry.CompletedUtc = DateTime.UtcNow;
            if (!await _Database.MergeEntries.TryUpdateIfUnchangedAsync(entry, entry.LastUpdateUtc, token).ConfigureAwait(false)) return;
            MergeQueueFailure failure = NewFailure(entry, MergeQueueFailureStepEnum.Resolution, MissionFailureKindEnum.Infra,
                "Merge queue entry " + entry.Id + " could not be processed: " + reason, null);
            await ReconcileMissionStatusAsync(entry.MissionId, MissionStatusEnum.LandingFailed, failure.Reason, token, entry.TenantId, failure).ConfigureAwait(false);
        }

        /// <summary>
        /// Core single-entry processing with a known repo path.
        /// </summary>
        private async Task ProcessEntryAsync(MergeEntry entry, MergeQueueHost host, CancellationToken token)
        {
            string repoPath = host.RepoPath;
            string entryTag = entry.Id + " branch " + entry.BranchName;
            _Logging.Debug(_Header + "processing " + entryTag);

            // Mark as testing (and clear a waiting reason left by an earlier pass)
            entry.Status = MergeStatusEnum.Testing;
            entry.TestOutput = null;
            entry.TestExitCode = null;
            entry.TestStartedUtc = DateTime.UtcNow;
            if (!await _Database.MergeEntries.TryUpdateIfUnchangedAsync(entry, entry.LastUpdateUtc, token).ConfigureAwait(false)) return;

            // Each entry gets its own temporary worktree so the merge is always
            // attempted against the current state of the target branch.
            string worktreeId = "mq_" + entry.Id + "_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            string integrationBranch = _IntegrationBranchPrefix + worktreeId;
            string integrationPath = host.IntegrationPath(worktreeId);
            _ActiveIntegrationBranches.TryAdd(integrationBranch, 0);
            MergeQueueFailureStepEnum step = MergeQueueFailureStepEnum.Preparation;
            string? targetCommit = null;

            try
            {
                if (host.IsHarbor)
                {
                    // The Harbor's repository is often the user's own checkout: leave its branches alone and build the
                    // integration worktree straight from the remote's target branch.
                    await RunGitAsync(host, repoPath, token, "fetch", "origin").ConfigureAwait(false);
                    await RunGitAsync(host, repoPath, token, "worktree", "add", "-b", integrationBranch, integrationPath, "origin/" + entry.TargetBranch).ConfigureAwait(false);
                }
                else
                {
                    // Fetch latest so the target branch ref is up to date
                    await _Git.FetchAsync(repoPath, token).ConfigureAwait(false);

                    // Create a temporary worktree from the current target branch
                    await _Git.CreateWorktreeAsync(repoPath, integrationPath, integrationBranch, entry.TargetBranch, token).ConfigureAwait(false);
                }

                // Record the target commit this entry integrates against, then merge the entry's branch.
                targetCommit = await ResolveHeadAsync(host, integrationPath).ConfigureAwait(false);
                step = MergeQueueFailureStepEnum.Merge;
                MergeQueueMergeResult merge = await MergeBranchAsync(host, integrationPath, entry.BranchName, token).ConfigureAwait(false);
                if (!merge.Merged)
                {
                    MissionFailureKindEnum mergeKind = MergeQueueFailureClassifier.ClassifyMerge(merge.ConflictedFiles.Count);
                    string files = merge.ConflictedFiles.Count > 0 ? " in: " + String.Join(", ", merge.ConflictedFiles) : "";
                    string mergeReason = mergeKind == MissionFailureKindEnum.LandingConflict
                        ? "merge conflict with " + entry.TargetBranch + files
                        : "merging into " + entry.TargetBranch + " failed: " + (merge.Message ?? "git merge failed");
                    _Logging.Warn(_Header + mergeReason + " for " + entryTag);
                    entry.Status = MergeStatusEnum.Failed;
                    entry.TestOutput = mergeKind == MissionFailureKindEnum.LandingConflict
                        ? "Merge conflict with " + entry.TargetBranch + files
                        : "Merge failed with " + entry.TargetBranch + ": " + (merge.Message ?? "git merge failed");
                    entry.CompletedUtc = DateTime.UtcNow;
                    if (!await _Database.MergeEntries.TryUpdateIfUnchangedAsync(entry, entry.LastUpdateUtc, token).ConfigureAwait(false)) return;
                    await CleanupWorktreeAsync(host, repoPath, integrationPath, integrationBranch, token).ConfigureAwait(false);
                    MergeQueueFailure mergeFailure = NewFailure(entry, MergeQueueFailureStepEnum.Merge, mergeKind,
                        "Merge queue entry " + entry.Id + ": " + mergeReason, targetCommit);
                    mergeFailure.ConflictedFiles = new List<string>(merge.ConflictedFiles);
                    await ReconcileMissionStatusAsync(entry.MissionId, MissionStatusEnum.LandingFailed, mergeFailure.Reason, token, entry.TenantId, mergeFailure).ConfigureAwait(false);
                    return;
                }
                step = MergeQueueFailureStepEnum.Verification;

                // Verify the integration HEAD, rather than the original mission branch, and record success as well as failure.
                string? explicitCommand = entry.TestCommand ?? _Settings.MergeQueueTestCommand;
                Vessel? vessel = String.IsNullOrEmpty(entry.VesselId) ? null : await _Database.Vessels.ReadAsync(entry.VesselId, token).ConfigureAwait(false);
                WorkflowProfile? profile = vessel == null ? null : await new WorkflowProfileService(_Database, _Logging)
                    .ResolveForVesselAsync(new AuthContext { IsAuthenticated = true, IsAdmin = true, TenantId = vessel.TenantId }, vessel, null, token).ConfigureAwait(false);
                List<string> checks = !String.IsNullOrWhiteSpace(explicitCommand) ? new() { explicitCommand } : LandingVerification.Commands(profile, vessel);
                // The Armada integration gate also applies when an operator supplies a shorter command.
                if (LandingVerification.Commands(null, vessel).Contains(LandingVerification.ArmadaCommand) && !checks.Contains(LandingVerification.ArmadaCommand))
                    checks.Add(LandingVerification.ArmadaCommand);
                IGitService hostGit = host.Commands == null ? new GitService(_Logging) : new GitService(_Logging, host.Commands);
                string verifiedCommit = await LandingVerification.VerifyAsync(_Database, hostGit,
                    host.Commands ?? LocalVerificationCommands ?? new LocalHostCommandExecutor(), integrationPath, host.IsWindows, checks,
                    Math.Min(10, _Settings.StageCheckTimeoutMinutes), entry: entry, token: token).ConfigureAwait(false);

                step = MergeQueueFailureStepEnum.Publication;
                await hostGit.ValidatePublishedAncestryAsync(integrationPath, host.RepoPath, entry.TargetBranch, verifiedCommit, token).ConfigureAwait(false);
                // Publish the verified SHA itself; a mutable branch ref is not verification evidence.
                await LandEntryAsync(entry, host, verifiedCommit, targetCommit, token).ConfigureAwait(false);

                // Cleanup
                await CleanupWorktreeAsync(host, repoPath, integrationPath, integrationBranch, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (HarborGone(host))
                {
                    // The Harbor went away mid-run: the work is not lost and nothing is wrong with the branch. Queue the
                    // entry again; the integration worktree left on the Harbor is pruned on the next pass there.
                    _Logging.Warn(_Header + "Harbor " + host.HarborId + " disconnected while processing " + entryTag + ": " + ex.Message);
                    await WaitForHarborAsync(entry, await HarborWaitReasonAsync(entry, host.HarborId!, host.RepoPath, token).ConfigureAwait(false), token).ConfigureAwait(false);
                    return;
                }

                _Logging.Warn(_Header + "error processing " + entryTag + ": " + ex.ToString());
                entry.Status = MergeStatusEnum.Failed;
                entry.TestOutput = "Queue processing error: " + ex.Message;
                entry.CompletedUtc = DateTime.UtcNow;
                if (!await _Database.MergeEntries.TryUpdateIfUnchangedAsync(entry, entry.LastUpdateUtc, token).ConfigureAwait(false)) return;

                // Best-effort cleanup
                await CleanupWorktreeAsync(host, repoPath, integrationPath, integrationBranch, token).ConfigureAwait(false);
                MergeQueueFailure failure = NewFailure(entry, step, MergeQueueFailureClassifier.Classify(step, ex),
                    "Merge queue entry " + entry.Id + ": " + ex.Message, targetCommit);
                await ReconcileMissionStatusAsync(entry.MissionId, MissionStatusEnum.LandingFailed, failure.Reason, token, entry.TenantId, failure, ex).ConfigureAwait(false);
            }
            finally
            {
                _ActiveIntegrationBranches.TryRemove(integrationBranch, out byte _);
            }
        }

        /// <summary>
        /// Land a single entry by pushing the integration branch to the target.
        /// </summary>
        private async Task LandEntryAsync(MergeEntry entry, MergeQueueHost host, string verifiedCommit, string? targetCommit, CancellationToken token)
        {
            MergeEntry? currentEntry = await _Database.MergeEntries.ReadAsync(entry.Id, token).ConfigureAwait(false);
            if (currentEntry?.Status != MergeStatusEnum.Testing || currentEntry.LastUpdateUtc != entry.LastUpdateUtc) return;
            if (!String.IsNullOrEmpty(entry.MissionId))
            {
                Mission? current = await _Database.Missions.ReadAsync(entry.MissionId, token).ConfigureAwait(false);
                if (current == null || (current.Status != MissionStatusEnum.WorkProduced && current.Status != MissionStatusEnum.LandingFailed))
                    throw new InvalidOperationException("Mission is no longer awaiting this landing; publication was refused.");
                DateTime version = current.LastUpdateUtc;
                MissionStatusEnum status = current.Status;
                current.CommitHash = verifiedCommit;
                current.LastUpdateUtc = DateTime.UtcNow;
                if (!await _Database.Missions.TryUpdateIfUnchangedAsync(current, version, new[] { status }, token).ConfigureAwait(false))
                    throw new InvalidOperationException("Mission changed before verified publication; retry against its current state.");
            }
            try
            {
                await RunGitAsync(host, host.RepoPath, token, "push", "origin", verifiedCommit + ":refs/heads/" + entry.TargetBranch).ConfigureAwait(false);
            }
            catch (Exception ex) when (HarborGone(host))
            {
                // Whether the push reached the remote is unknown. Processing the entry again is safe: an already merged
                // branch merges as a no-op and the push then changes nothing.
                _Logging.Warn(_Header + "Harbor " + host.HarborId + " disconnected while landing " + entry.Id + ": " + ex.Message);
                await WaitForHarborAsync(entry, await HarborWaitReasonAsync(entry, host.HarborId!, host.RepoPath, token).ConfigureAwait(false), token).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "failed to land " + entry.Id + ": " + ex.ToString());
                entry.Status = MergeStatusEnum.Failed;
                entry.TestOutput = "Landing failed: " + ex.Message;
                entry.CompletedUtc = DateTime.UtcNow;
                if (!await _Database.MergeEntries.TryUpdateIfUnchangedAsync(entry, entry.LastUpdateUtc, token).ConfigureAwait(false)) return;
                ArmadaMetrics.MergeEntriesProcessed.Add(1);

                // Reconcile linked mission to LandingFailed: a refused push is an environment failure, not a conflict.
                MergeQueueFailure failure = NewFailure(entry, MergeQueueFailureStepEnum.Publication, MissionFailureKindEnum.Infra,
                    "Merge queue landing failed: " + ex.Message, targetCommit);
                await ReconcileMissionStatusAsync(entry.MissionId, MissionStatusEnum.LandingFailed, failure.Reason, token, entry.TenantId, failure, ex).ConfigureAwait(false);
                return;
            }

            entry.Status = MergeStatusEnum.Landed;
            entry.CompletedUtc = DateTime.UtcNow;
            if (!await _Database.MergeEntries.TryUpdateIfUnchangedAsync(entry, entry.LastUpdateUtc, token).ConfigureAwait(false)) return;
            ArmadaMetrics.MergeEntriesProcessed.Add(1);
            _Logging.Info(_Header + "landed " + entry.Id + " branch " + entry.BranchName);

            // Reconcile linked mission to Complete
            await ReconcileMissionStatusAsync(entry.MissionId, MissionStatusEnum.Complete,
                "Landed via merge queue entry " + entry.Id, token, entry.TenantId).ConfigureAwait(false);
        }

        /// <summary>
        /// Reconcile the linked mission status after a merge queue entry reaches a terminal state, recording the same
        /// mission.completed / mission.landing_failed event the landing handler records and raising
        /// <see cref="OnMissionReconciled"/>. A LandingFailed reconciliation records the failure's kind on the mission and
        /// the structured failure as the event payload.
        /// </summary>
        private async Task ReconcileMissionStatusAsync(string? missionId, MissionStatusEnum targetStatus, string reason, CancellationToken token, string? tenantId = null, MergeQueueFailure? failure = null, Exception? exception = null)
        {
            if (String.IsNullOrEmpty(missionId)) return;

            Mission? mission = null;
            try
            {
                mission = !String.IsNullOrEmpty(tenantId)
                    ? await _Database.Missions.ReadAsync(tenantId, missionId, token).ConfigureAwait(false)
                    : await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
                if (mission == null) return;

                // Only update if the mission is not already in a terminal state
                if (mission.Status == MissionStatusEnum.Complete ||
                    mission.Status == MissionStatusEnum.Failed ||
                    mission.Status == MissionStatusEnum.Cancelled)
                {
                    return;
                }

                DateTime version = mission.LastUpdateUtc;
                MissionStatusEnum previous = mission.Status;
                mission.Status = targetStatus;
                mission.LastUpdateUtc = DateTime.UtcNow;
                if (targetStatus == MissionStatusEnum.Complete)
                {
                    mission.CompletedUtc = DateTime.UtcNow;
                    mission.FailureKind = null;
                    mission.FailureReason = null;
                }
                if (targetStatus == MissionStatusEnum.LandingFailed)
                {
                    mission.FailureReason = reason;
                    mission.FailureKind = failure?.Kind ?? MissionFailureKindEnum.Infra;
                }
                if (!await _Database.Missions.TryUpdateIfUnchangedAsync(mission, version, new[] { previous }, token).ConfigureAwait(false)) return;
                _Logging.Info(_Header + "reconciled mission " + missionId + " to " + targetStatus + ": " + reason);
                if (targetStatus == MissionStatusEnum.LandingFailed)
                {
                    await BlackBox.RecordMissionFailureAsync(mission, previous, BlackBoxSiteFacts.MergeQueueStepOf(failure), context =>
                    {
                        context.MergeEntryId = failure?.MergeEntryId;
                        BlackBoxSiteFacts.ApplyException(context, exception);
                        if (failure != null)
                        {
                            context.GitFacts ??= new BlackBoxGitFacts();
                            foreach (string file in failure.ConflictedFiles)
                                if (!context.GitFacts.ConflictedFiles.Contains(file)) context.GitFacts.ConflictedFiles.Add(file);
                            context.GitFacts.TargetTipAfter ??= failure.TargetCommit;
                            if (failure.Step == MergeQueueFailureStepEnum.Merge) context.GitFacts.GitOperation ??= "merge --no-ff " + failure.BranchName;
                            context.Note = "Merge queue entry " + failure.MergeEntryId + " (" + failure.BranchName + " into " + failure.TargetBranch + ") failed at " + failure.Step + ".";
                        }
                    }, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "failed to reconcile mission " + missionId + " to " + targetStatus + ": " + ex.ToString());
                return;
            }

            bool completed = targetStatus == MissionStatusEnum.Complete;
            try
            {
                ArmadaEvent evt = new ArmadaEvent(
                    completed ? "mission.completed" : "mission.landing_failed",
                    completed ? "Mission completed: " + mission.Title : "Landing failed: " + mission.Title);
                evt.TenantId = mission.TenantId;
                evt.UserId = mission.UserId;
                evt.EntityType = "mission";
                evt.EntityId = mission.Id;
                evt.CaptainId = mission.CaptainId;
                evt.MissionId = mission.Id;
                evt.VesselId = mission.VesselId;
                evt.VoyageId = mission.VoyageId;
                if (!completed && failure != null) evt.Payload = JsonSerializer.Serialize(failure);
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error recording " + (completed ? "mission.completed" : "mission.landing_failed") + " event for " + mission.Id + ": " + ex.Message);
            }

            if (OnMissionReconciled != null)
            {
                try
                {
                    await OnMissionReconciled.Invoke(mission).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "OnMissionReconciled callback failed for " + mission.Id + ": " + ex.Message);
                }
            }
        }

        private async Task<MergeQueueMergeResult> MergeBranchAsync(MergeQueueHost host, string worktreePath, string branchName, CancellationToken token)
        {
            try
            {
                await RunGitAsync(host, worktreePath, token, "merge", "--no-ff", branchName).ConfigureAwait(false);
                return new MergeQueueMergeResult { Merged = true };
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (InvalidOperationException ex) when (host.IsHarbor && ex.Message.Contains("not something we can merge", StringComparison.OrdinalIgnoreCase))
            {
                // The branch was pushed and then removed locally: merge the pushed copy.
                try
                {
                    await RunGitAsync(host, worktreePath, token, "merge", "--no-ff", "origin/" + branchName).ConfigureAwait(false);
                    return new MergeQueueMergeResult { Merged = true };
                }
                catch (Exception ex2) when (ex2 is not OperationCanceledException)
                {
                    return await ConcludeFailedMergeAsync(host, worktreePath, ex2.Message, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await ConcludeFailedMergeAsync(host, worktreePath, ex.Message, token).ConfigureAwait(false);
            }
        }

        private async Task<MergeQueueMergeResult> ConcludeFailedMergeAsync(MergeQueueHost host, string worktreePath, string message, CancellationToken token)
        {
            // Read the conflicted files before the merge is resolved or aborted: afterwards git no longer reports them.
            MergeQueueMergeResult result = new MergeQueueMergeResult();
            result.Message = message;
            result.ConflictedFiles = await ReadConflictedFilesAsync(host, worktreePath).ConfigureAwait(false);
            if (await ChangelogLandingConflict.TryResolveAsync(host.Commands ?? new LocalHostCommandExecutor(), worktreePath, host.IsWindows, token).ConfigureAwait(false))
                return new MergeQueueMergeResult { Merged = true };
            try { await RunGitAsync(host, worktreePath, token, "merge", "--abort").ConfigureAwait(false); }
            catch { }
            return result;
        }

        private async Task<List<string>> ReadConflictedFilesAsync(MergeQueueHost host, string worktreePath)
        {
            try
            {
                HostCommandResult listing = await RunGitCaptureAsync(host, worktreePath, CancellationToken.None, "diff", "--name-only", "-z", "--diff-filter=U").ConfigureAwait(false);
                if (listing.ExitCode == 0) return GitMachineOutputParser.ParsePathListZ(listing.StandardOutput);
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "unable to list conflicted files in " + worktreePath + ": " + ex.Message);
            }
            return new List<string>();
        }

        private async Task<string?> ResolveHeadAsync(MergeQueueHost host, string worktreePath)
        {
            try
            {
                HostCommandResult head = await RunGitCaptureAsync(host, worktreePath, CancellationToken.None, "rev-parse", "HEAD").ConfigureAwait(false);
                string sha = (head.StandardOutput ?? "").Trim();
                return head.ExitCode == 0 && sha.Length > 0 ? sha : null;
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "unable to resolve the integration base in " + worktreePath + ": " + ex.Message);
                return null;
            }
        }

        private static MergeQueueFailure NewFailure(MergeEntry entry, MergeQueueFailureStepEnum step, MissionFailureKindEnum kind, string reason, string? targetCommit)
        {
            MergeQueueFailure failure = new MergeQueueFailure();
            failure.MergeEntryId = entry.Id;
            failure.MissionId = entry.MissionId;
            failure.BranchName = entry.BranchName;
            failure.TargetBranch = entry.TargetBranch;
            failure.TargetCommit = targetCommit;
            failure.Step = step;
            failure.Kind = kind;
            failure.Reason = reason;
            return failure;
        }

        /// <summary>
        /// Run git on the host and return its result without treating a nonzero exit as an error.
        /// </summary>
        /// <exception cref="TimeoutException">Thrown when git does not finish in time.</exception>
        private async Task<HostCommandResult> RunGitCaptureAsync(MergeQueueHost host, string workingDir, CancellationToken token, params string[] args)
        {
            string display = String.Join(" ", args);
            if (host.IsHarbor)
            {
                HostCommandResult result = await host.Commands!.RunAsync(new HostCommandRequest
                {
                    Executable = "git",
                    WorkingDirectory = workingDir,
                    Arguments = new List<string>(args),
                    TimeoutMs = (int)_OperationTimeout.TotalMilliseconds,
                    Label = "Merge queue: git " + (args.Length > 0 ? args[0] : "")
                }, token).ConfigureAwait(false);
                if (result.TimedOut)
                    throw new TimeoutException("git " + display + " timed out after " + _OperationTimeout.TotalSeconds.ToString("0") + "s on Harbor " + host.HarborId);
                return result;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            GitProcessEnvironment.Apply(startInfo);
            foreach (string arg in args) startInfo.ArgumentList.Add(arg);

            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(_OperationTimeout);
                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    try
                    {
                        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
                        await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                        HostCommandResult local = new HostCommandResult();
                        local.ExitCode = process.ExitCode;
                        local.StandardOutput = await stdoutTask.ConfigureAwait(false);
                        local.StandardError = await stderrTask.ConfigureAwait(false);
                        return local;
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                        throw new TimeoutException("git " + display + " timed out after " +
                            _OperationTimeout.TotalSeconds.ToString("0") + "s and was killed");
                    }
                }
            }
        }

        private async Task RunGitAsync(MergeQueueHost host, string workingDir, CancellationToken token, params string[] args)
        {
            if (!host.IsHarbor)
            {
                await RunGitAsync(workingDir, token, args).ConfigureAwait(false);
                return;
            }

            HostCommandResult result = await host.Commands!.RunAsync(new HostCommandRequest
            {
                Executable = "git",
                WorkingDirectory = workingDir,
                Arguments = new List<string>(args),
                TimeoutMs = (int)_OperationTimeout.TotalMilliseconds,
                Label = "Merge queue: git " + (args.Length > 0 ? args[0] : "")
            }, token).ConfigureAwait(false);
            string display = String.Join(" ", args);
            if (result.TimedOut)
                throw new TimeoutException("git " + display + " timed out after " + _OperationTimeout.TotalSeconds.ToString("0") + "s on Harbor " + host.HarborId);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("git " + display + " failed on Harbor " + host.HarborId + ": " + (result.StandardError ?? "").Trim());
        }

        private async Task RunGitAsync(string workingDir, CancellationToken token, params string[] args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            GitProcessEnvironment.Apply(startInfo);

            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            string argsDisplay = String.Join(" ", args);

            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(_OperationTimeout);
                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    try
                    {
                        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
                        await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                        string stderr = await stderrTask.ConfigureAwait(false);

                        if (process.ExitCode != 0)
                        {
                            throw new InvalidOperationException("git " + argsDisplay + " failed: " + stderr);
                        }
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                        throw new TimeoutException("git " + argsDisplay + " timed out after " +
                            _OperationTimeout.TotalSeconds.ToString("0") + "s and was killed");
                    }
                }
            }
        }

        private async Task CleanupWorktreeAsync(MergeQueueHost host, string repoPath, string worktreePath, string integrationBranch, CancellationToken token)
        {
            if (!host.IsHarbor)
            {
                try
                {
                    await _Git.RemoveWorktreeAsync(worktreePath, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "cleanup error for " + worktreePath + ": " + ex.ToString());
                }

                return;
            }

            try
            {
                await RunGitAsync(host, repoPath, token, "worktree", "remove", "--force", worktreePath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "cleanup error for " + worktreePath + " on Harbor " + host.HarborId + ": " + ex.Message);
            }

            try
            {
                await RunGitAsync(host, repoPath, token, "branch", "-D", integrationBranch).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "integration branch " + integrationBranch + " not deleted on Harbor " + host.HarborId + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Where an entry is processed: the Harbor whose dock produced the branch, else the Admiral's clone. Null when
        /// the vessel cannot be found. Callers check <see cref="IsWaitingOnHarbor"/> first.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the Admiral has no clone of the vessel.</exception>
        private async Task<MergeQueueHost?> ResolveHostAsync(MergeEntry entry, Dock? dock, CancellationToken token)
        {
            if (dock != null && DockHosts != null && DockHostResolver.IsHarborDock(dock))
            {
                string harborId = dock.HarborId!;
                if (DockHosts.Harbors == null || !DockHosts.Harbors.IsConnected(harborId))
                    throw new InvalidOperationException(await HarborWaitReasonAsync(entry, harborId, dock.RepositoryPath, token).ConfigureAwait(false));

                Harbor? harbor = await _Database.Harbors.ReadAsync(harborId, token).ConfigureAwait(false);
                DockHost dockHost = DockHosts.ForDock(dock);
                string worktreePath = dock.WorktreePath ?? dock.RepositoryPath!;
                int cut = Math.Max(worktreePath.LastIndexOf('/'), worktreePath.LastIndexOf('\\'));
                string integrationRoot = cut > 0 ? worktreePath.Substring(0, cut) : dock.RepositoryPath!;
                bool windows = FleetActionShellCommandBuilder.IsWindowsPlatform(harbor?.OsPlatform);
                return new MergeQueueHost(dock.RepositoryPath!, integrationRoot, harborId, dockHost.Commands, windows);
            }

            string? repoPath = await GetRepoPathAsync(entry, token).ConfigureAwait(false);
            if (repoPath == null) return null;
            if (!Directory.Exists(repoPath))
                throw new InvalidOperationException("The Admiral has no clone of this vessel at " + repoPath
                    + " (missions on it ran elsewhere, or none has run on this server yet), so the merge queue cannot process branch "
                    + entry.BranchName + " here.");
            return new MergeQueueHost(repoPath, Path.Combine(_Settings.DocksDirectory, "_merge-queue"), null, null, OperatingSystem.IsWindows());
        }

        /// <summary>
        /// The dock that made the entry's branch: the most recent dock of the vessel on that branch (so a manual entry
        /// without a mission, or a mission whose DockId was cleared, still finds the Harbor that holds the branch), else
        /// the mission's own dock.
        /// </summary>
        private async Task<Dock?> FindBranchDockAsync(MergeEntry entry, Mission? mission, CancellationToken token)
        {
            string? vesselId = !String.IsNullOrEmpty(entry.VesselId) ? entry.VesselId : mission?.VesselId;
            if (!String.IsNullOrEmpty(vesselId) && !String.IsNullOrEmpty(entry.BranchName))
            {
                List<Dock> docks = await _Database.Docks.EnumerateByVesselAsync(vesselId, token).ConfigureAwait(false);
                Dock? latest = null;
                foreach (Dock candidate in docks)
                {
                    if (!String.Equals(candidate.BranchName, entry.BranchName, StringComparison.Ordinal)) continue;
                    if (latest == null || candidate.CreatedUtc >= latest.CreatedUtc) latest = candidate;
                }

                if (latest != null) return latest;
            }

            if (mission == null || String.IsNullOrEmpty(mission.DockId)) return null;
            return await _Database.Docks.ReadAsync(mission.DockId, token).ConfigureAwait(false);
        }

        private async Task<Mission?> ReadMissionAsync(string? missionId, string? tenantId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(missionId)) return null;
            return !String.IsNullOrEmpty(tenantId)
                ? await _Database.Missions.ReadAsync(tenantId, missionId, token).ConfigureAwait(false)
                : await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Whether the branch is on a Harbor that is not connected right now (the entry then waits for it).
        /// </summary>
        private bool IsWaitingOnHarbor(Dock? dock)
        {
            if (dock == null || DockHosts == null || !DockHostResolver.IsHarborDock(dock)) return false;
            return DockHosts.Harbors == null || !DockHosts.Harbors.IsConnected(dock.HarborId!);
        }

        /// <summary>
        /// Whether a Harbor host has disconnected (always false for the Admiral's own host).
        /// </summary>
        private bool HarborGone(MergeQueueHost host)
        {
            if (!host.IsHarbor) return false;
            return DockHosts?.Harbors == null || !DockHosts.Harbors.IsConnected(host.HarborId!);
        }

        private async Task<string> HarborWaitReasonAsync(MergeEntry entry, string harborId, string? repoPath, CancellationToken token)
        {
            string harborName = harborId;
            try
            {
                Harbor? harbor = await _Database.Harbors.ReadAsync(harborId, token).ConfigureAwait(false);
                if (harbor != null && !String.IsNullOrWhiteSpace(harbor.Name)) harborName = harbor.Name;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "could not read Harbor " + harborId + ": " + ex.Message);
            }

            return "Waiting for Harbor " + harborName + " to reconnect: branch " + entry.BranchName + " is in its repository "
                + (repoPath ?? "(unknown)") + ", and that Harbor is not connected. Start Harbor on that computer and connect it;"
                + " the queue will retry this entry on its next pass.";
        }

        /// <summary>
        /// Delete the entry's branch (local and remote) when its work is in the target branch: always for a Landed entry
        /// unless git shows the branch is not merged, and for a Cancelled entry only when git shows it is merged. Failed
        /// entries keep their branch. Best effort: problems are logged and the entry is still deleted.
        /// </summary>
        private async Task CleanupEntryBranchAsync(MergeEntry entry, CancellationToken token)
        {
            if (String.IsNullOrEmpty(entry.BranchName)) return;
            if (entry.Status != MergeStatusEnum.Landed && entry.Status != MergeStatusEnum.Cancelled)
            {
                _Logging.Info(_Header + "keeping branch " + entry.BranchName + " of " + entry.Status + " entry " + entry.Id
                    + ": its work has not landed, and the branch may be the only copy of it");
                return;
            }

            MergeQueueHost? host;
            try
            {
                Mission? mission = await ReadMissionAsync(entry.MissionId, entry.TenantId, token).ConfigureAwait(false);
                Dock? dock = await FindBranchDockAsync(entry, mission, token).ConfigureAwait(false);
                if (IsWaitingOnHarbor(dock))
                {
                    _Logging.Info(_Header + "keeping branch " + entry.BranchName + " of entry " + entry.Id + ": Harbor " + dock!.HarborId + " is not connected");
                    return;
                }

                host = await ResolveHostAsync(entry, dock, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "branch cleanup for " + entry.Id + " skipped: " + ex.Message);
                return;
            }

            if (host == null) return;

            bool? merged = null;
            try
            {
                merged = await IsBranchMergedAsync(host, entry.BranchName, entry.TargetBranch, true, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "could not check whether " + entry.BranchName + " is merged: " + ex.Message);
            }

            bool delete = entry.Status == MergeStatusEnum.Landed ? merged != false : merged == true;
            if (!delete)
            {
                _Logging.Info(_Header + "keeping branch " + entry.BranchName + " of " + entry.Status + " entry " + entry.Id
                    + ": it is not merged into " + entry.TargetBranch);
                return;
            }

            try
            {
                await RunGitAsync(host, host.RepoPath, token, "push", "origin", "--delete", entry.BranchName).ConfigureAwait(false);
                _Logging.Debug(_Header + "deleted remote branch " + entry.BranchName);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "remote branch delete for " + entry.BranchName + " skipped: " + ex.Message);
            }

            try
            {
                await RunGitAsync(host, host.RepoPath, token, "branch", "-D", entry.BranchName).ConfigureAwait(false);
                _Logging.Debug(_Header + "deleted local branch " + entry.BranchName);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "local branch delete for " + entry.BranchName + " skipped: " + ex.Message);
            }
        }

        /// <summary>
        /// Whether a branch is contained in the target branch in the host's repository (checked against both the
        /// remote-tracking and the local target). Null when the branch or the target cannot be found there.
        /// </summary>
        private async Task<bool?> IsBranchMergedAsync(MergeQueueHost host, string branchName, string targetBranch, bool fetch, CancellationToken token)
        {
            if (fetch)
            {
                HostCommandResult fetched = await RunGitCaptureAsync(host, host.RepoPath, token, "fetch", "--quiet", "origin").ConfigureAwait(false);
                if (!fetched.Success) _Logging.Debug(_Header + "fetch before merged check failed in " + host.RepoPath + ": " + fetched.StandardError.Trim());
            }

            string? branchRef = null;
            foreach (string candidate in new string[] { "refs/heads/" + branchName, "refs/remotes/origin/" + branchName })
            {
                if (await RefResolvesAsync(host, candidate, token).ConfigureAwait(false))
                {
                    branchRef = candidate;
                    break;
                }
            }

            if (branchRef == null) return null;

            bool anyTarget = false;
            foreach (string targetRef in new string[] { "refs/remotes/origin/" + targetBranch, "refs/heads/" + targetBranch })
            {
                if (!await RefResolvesAsync(host, targetRef, token).ConfigureAwait(false)) continue;
                anyTarget = true;
                HostCommandResult ancestor = await RunGitCaptureAsync(host, host.RepoPath, token, "merge-base", "--is-ancestor", branchRef, targetRef).ConfigureAwait(false);
                if (ancestor.ExitCode == 0) return true;
            }

            return anyTarget ? false : (bool?)null;
        }

        private async Task<bool> RefResolvesAsync(MergeQueueHost host, string gitRef, CancellationToken token)
        {
            HostCommandResult result = await RunGitCaptureAsync(host, host.RepoPath, token, "rev-parse", "--verify", "--quiet", gitRef + "^{commit}").ConfigureAwait(false);
            return result.Success;
        }

        /// <summary>
        /// Remove integration worktrees and armada/merge-queue/* branches an earlier run left on this host (for example
        /// when its Harbor disconnected mid-run, so the cleanup could not run): once per host per pass, skipping branches
        /// in use by this instance, made during this pass, or belonging to an entry another instance is testing now.
        /// </summary>
        private async Task PruneLeftoverIntegrationsAsync(MergeQueueHost host, MergeQueuePass pass, CancellationToken token)
        {
            if (!pass.TryClaimPrune((host.HarborId ?? "admiral") + "|" + host.RepoPath)) return;

            try
            {
                await RunGitCaptureAsync(host, host.RepoPath, token, "worktree", "prune").ConfigureAwait(false);

                HostCommandResult listed = await RunGitCaptureAsync(host, host.RepoPath, token, "worktree", "list", "--porcelain").ConfigureAwait(false);
                if (!listed.Success) return;

                HashSet<string> stillCheckedOut = new HashSet<string>(StringComparer.Ordinal);
                foreach (GitWorktreeEntry worktree in GitMachineOutputParser.ParseWorktreeList(listed.StandardOutput, false))
                {
                    string? branch = BranchFromRef(worktree.BranchRef);
                    if (branch == null || !branch.StartsWith(_IntegrationBranchPrefix, StringComparison.Ordinal)) continue;
                    if (!await IsLeftoverAsync(branch, pass, token).ConfigureAwait(false))
                    {
                        stillCheckedOut.Add(branch);
                        continue;
                    }

                    HostCommandResult removed = await RunGitCaptureAsync(host, host.RepoPath, token, "worktree", "remove", "--force", worktree.Path).ConfigureAwait(false);
                    if (removed.Success) _Logging.Info(_Header + "removed leftover integration worktree " + worktree.Path + (host.IsHarbor ? " on Harbor " + host.HarborId : ""));
                    else stillCheckedOut.Add(branch);
                }

                HostCommandResult branches = await RunGitCaptureAsync(host, host.RepoPath, token, "for-each-ref", "--format=%(refname)", "refs/heads/" + _IntegrationBranchPrefix).ConfigureAwait(false);
                if (!branches.Success) return;

                foreach (string line in branches.StandardOutput.Replace("\r\n", "\n").Split('\n'))
                {
                    string? branch = BranchFromRef(line.Trim());
                    if (branch == null || stillCheckedOut.Contains(branch)) continue;
                    if (!await IsLeftoverAsync(branch, pass, token).ConfigureAwait(false)) continue;

                    HostCommandResult deleted = await RunGitCaptureAsync(host, host.RepoPath, token, "branch", "-D", branch).ConfigureAwait(false);
                    if (deleted.Success) _Logging.Info(_Header + "deleted leftover integration branch " + branch + (host.IsHarbor ? " on Harbor " + host.HarborId : ""));
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not prune leftover integration worktrees in " + host.RepoPath + ": " + ex.Message);
            }
        }

        private async Task<bool> IsLeftoverAsync(string integrationBranch, MergeQueuePass pass, CancellationToken token)
        {
            if (_ActiveIntegrationBranches.ContainsKey(integrationBranch)) return false;

            // armada/merge-queue/mq_{entryId}_{yyyyMMddHHmmss}
            string name = integrationBranch.Substring(_IntegrationBranchPrefix.Length);
            int cut = name.LastIndexOf('_');
            if (!name.StartsWith("mq_", StringComparison.Ordinal) || cut <= 3) return false;
            if (!DateTime.TryParseExact(name.Substring(cut + 1), "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime createdUtc))
            {
                return false;
            }

            if (createdUtc >= pass.StartedUtc.AddSeconds(-1)) return false;

            string entryId = name.Substring(3, cut - 3);
            MergeEntry? owner = await _Database.MergeEntries.ReadAsync(entryId, token).ConfigureAwait(false);
            if (owner != null && owner.Status == MergeStatusEnum.Testing && owner.TestStartedUtc.HasValue
                && owner.TestStartedUtc.Value > DateTime.UtcNow - _OperationTimeout - _OperationTimeout)
            {
                // Another instance may be testing it right now.
                return false;
            }

            return true;
        }

        private static string? BranchFromRef(string? gitRef)
        {
            if (String.IsNullOrEmpty(gitRef) || !gitRef.StartsWith("refs/heads/", StringComparison.Ordinal)) return null;
            return gitRef.Substring("refs/heads/".Length);
        }

        private async Task<string?> GetRepoPathAsync(MergeEntry entry, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(entry.VesselId))
            {
                Vessel? vessel = !String.IsNullOrEmpty(entry.TenantId)
                    ? await _Database.Vessels.ReadAsync(entry.TenantId, entry.VesselId, token).ConfigureAwait(false)
                    : await _Database.Vessels.ReadAsync(entry.VesselId, token).ConfigureAwait(false);
                if (vessel == null)
                {
                    _Logging.Warn(_Header + "vessel not found for vessel ID " + entry.VesselId);
                    return null;
                }
                if (!String.IsNullOrEmpty(vessel.LocalPath))
                    return vessel.LocalPath;

                // Fallback to default path, same as DockService
                string defaultPath = Path.Combine(_Settings.ReposDirectory, vessel.Name + ".git");
                _Logging.Warn(_Header + "vessel LocalPath is empty for vessel " + vessel.Name + ", falling back to default: " + defaultPath);
                return defaultPath;
            }
            return _Settings.ReposDirectory;
        }

        private string TruncateOutput(string output)
        {
            if (String.IsNullOrEmpty(output)) return "";
            if (output.Length > 4096) return output.Substring(0, 4096) + "\n... (truncated)";
            return output;
        }

        #endregion
    }
}

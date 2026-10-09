namespace Armada.Server.Ask
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Ask;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Watches every Active item tracked by an Ask Armada thread. Reacts to the hub's entity change notifications
    /// (mission, voyage, captain, check run, merge entry) and sweeps every Ask.TrackerIntervalSeconds so nothing is
    /// missed. For each item it builds the work snapshot, compares its hash with the stored one, and on change pushes
    /// ask.work to the owner, updates the row, and posts a WorkUpdate message per milestone (worded by the thread's
    /// captain when idle, otherwise a deterministic sentence). The final milestone of a voyage, mission, or fleet action
    /// run that succeeded or failed is never narrated: it carries the deterministic outcome block (see
    /// <see cref="AskWorkOutcomeFormatter"/>) and is then handed to <see cref="ReportResult"/> for the captain's report.
    /// The sweep also expires pending proposals.
    /// </summary>
    /// <remarks>Thread safety: refreshes of one tracked item are serialized by a per-item lock; milestone messages of one
    /// thread are posted in order on a per-thread chain.</remarks>
    public class AskWorkTracker
    {
        #region Public-Members

        /// <summary>
        /// Called after each sweep to expire due proposals, or null.
        /// </summary>
        public Func<CancellationToken, Task<int>>? ExpireProposals { get; set; } = null;

        /// <summary>
        /// Narrates a milestone through the thread's captain: (thread, deterministic text, snapshot, token) returning the
        /// captain's wording or null. Null disables narration.
        /// </summary>
        public Func<AskThread, string, AskWorkSnapshot, CancellationToken, Task<string?>>? Narrate { get; set; } = null;

        /// <summary>
        /// Called after the final milestone of work with an outcome was posted (see
        /// <see cref="AskWorkOutcomeFormatter.HasOutcome"/>), to schedule the captain's report, or null.
        /// </summary>
        public Func<AskReportRequest, Task>? ReportResult { get; set; } = null;

        /// <summary>
        /// Builds the results of finished work.
        /// </summary>
        public AskWorkResultBuilder Results => _Results;

        #endregion

        #region Private-Members

        private readonly string _Header = "[AskWorkTracker] ";
        private readonly DatabaseDriver _Database;
        private readonly AskThreadService _Threads;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly AskWorkResultBuilder _Results;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _ItemLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, AskWorkSnapshot> _LastSnapshots = new ConcurrentDictionary<string, AskWorkSnapshot>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Task> _ThreadChains = new ConcurrentDictionary<string, Task>(StringComparer.Ordinal);
        private readonly object _ChainLock = new object();
        private CancellationTokenSource? _Cts;
        private Task? _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="threads">Thread service.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public AskWorkTracker(DatabaseDriver database, AskThreadService threads, ArmadaSettings settings, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Threads = threads ?? throw new ArgumentNullException(nameof(threads));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Results = new AskWorkResultBuilder(database);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the sweep loop.
        /// </summary>
        /// <param name="token">Server cancellation token.</param>
        public void Start(CancellationToken token)
        {
            if (_Loop != null) return;
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _Loop = Task.Run(() => LoopAsync(_Cts.Token));
        }

        /// <summary>
        /// Stop the sweep loop.
        /// </summary>
        public void Stop()
        {
            try { _Cts?.Cancel(); }
            catch { }
        }

        /// <summary>
        /// Refresh every Active tracked item once, then expire due proposals.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of items whose snapshot changed.</returns>
        public async Task<int> SweepAsync(CancellationToken token = default)
        {
            int changed = 0;
            List<AskTrackedWork> active = await _Database.AskTrackedWork.EnumerateActiveAsync(token).ConfigureAwait(false);
            foreach (AskTrackedWork work in active)
            {
                token.ThrowIfCancellationRequested();
                if (await RefreshAsync(work, token).ConfigureAwait(false)) changed++;
            }

            Func<CancellationToken, Task<int>>? expire = ExpireProposals;
            if (expire != null)
            {
                try { await expire(token).ConfigureAwait(false); }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { _Logging.Warn(_Header + "proposal expiry failed: " + ex.Message); }
            }

            return changed;
        }

        /// <summary>
        /// React to an entity change notification by refreshing the Active items that track it (a mission change also
        /// refreshes its voyage's items).
        /// </summary>
        /// <param name="entityType">Entity type (mission, voyage, captain, check-run, merge-entry).</param>
        /// <param name="entityId">Entity identifier.</param>
        public void OnEntityChanged(string entityType, string entityId)
        {
            if (String.IsNullOrEmpty(entityId)) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleEntityChangedAsync(entityType, entityId, _Cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _Logging.Debug(_Header + "change handling for " + entityType + " " + entityId + " failed: " + ex.Message);
                }
            });
        }

        /// <summary>
        /// Snapshot a tracked item now (used right after an action links it) and announce it.
        /// </summary>
        /// <param name="work">Tracked work.</param>
        /// <returns>Task.</returns>
        public async Task OnWorkLinkedAsync(AskTrackedWork work)
        {
            if (work == null) return;
            try
            {
                if (work.State != AskTrackedWorkStateEnum.Active)
                {
                    // A refresh-only link (cancel_*) of finished work, or work that finished before: just re-announce.
                    AskWorkSnapshot snapshot = await _Threads.Snapshots.BuildAsync(work).ConfigureAwait(false);
                    _Threads.AnnounceWork(work, snapshot);
                    return;
                }

                await RefreshAsync(work, CancellationToken.None, true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "initial snapshot of " + work.EntityType + " " + work.EntityId + " failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Refresh one tracked item: build its snapshot and, when the hash changed, update the row, announce ask.work, and
        /// post milestone messages.
        /// </summary>
        /// <param name="work">Tracked work row.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="forceAnnounce">Announce ask.work even when nothing changed.</param>
        /// <returns>True when the snapshot changed.</returns>
        public async Task<bool> RefreshAsync(AskTrackedWork work, CancellationToken token = default, bool forceAnnounce = false)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            SemaphoreSlim gate = _ItemLocks.GetOrAdd(work.Id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                AskTrackedWork current = await _Database.AskTrackedWork.ReadAsync(work.TenantId!, work.Id, token).ConfigureAwait(false) ?? work;
                AskWorkSnapshot snapshot = await _Threads.Snapshots.BuildAsync(current, token).ConfigureAwait(false);
                string hash = AskWorkSnapshotBuilder.ComputeHash(snapshot);
                bool changed = !String.Equals(hash, current.SnapshotHash, StringComparison.Ordinal);

                _LastSnapshots.TryGetValue(current.Id, out AskWorkSnapshot? previous);
                if (!changed)
                {
                    _LastSnapshots[current.Id] = snapshot;
                    if (forceAnnounce) _Threads.AnnounceWork(current, snapshot);
                    return false;
                }

                if (previous == null && String.IsNullOrEmpty(current.SnapshotHash))
                {
                    // First snapshot ever for this row: compare against "nothing happened yet" so work that started (or
                    // finished missions) before the first refresh still produces its milestones exactly once.
                    previous = new AskWorkSnapshot();
                    previous.EntityType = snapshot.EntityType;
                    previous.EntityId = snapshot.EntityId;
                    previous.Title = snapshot.Title;
                    previous.State = AskTrackedWorkStateEnum.Active;
                    previous.Status = snapshot.EntityType == AskTrackedEntityTypeEnum.Job ? JobStatusEnum.Queued.ToString()
                        : (snapshot.EntityType == AskTrackedEntityTypeEnum.FleetActionRun ? FleetActionRunStatusEnum.Pending.ToString() : null);
                }

                AskTrackedWorkStateEnum previousState = current.State;
                List<AskMilestone> milestones = AskMilestoneDetector.Detect(previous, snapshot, previousState);

                current.Title = Clip(snapshot.Title, 250);
                current.Status = snapshot.Status;
                current.State = snapshot.State;
                current.SnapshotHash = hash;
                current.LastChangeUtc = DateTime.UtcNow;
                if (snapshot.State != AskTrackedWorkStateEnum.Active && current.CompletedUtc == null) current.CompletedUtc = DateTime.UtcNow;
                current = await _Database.AskTrackedWork.UpdateAsync(current, token).ConfigureAwait(false);
                _LastSnapshots[current.Id] = snapshot;
                if (current.State != AskTrackedWorkStateEnum.Active) _LastSnapshots.TryRemove(current.Id, out _);

                _Threads.AnnounceWork(current, snapshot);
                if (milestones.Count > 0) QueueMilestones(current, snapshot, milestones);
                if (previousState != current.State) await _Threads.EmitThreadAsync(current.ThreadId, token).ConfigureAwait(false);
                return true;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Wait until the milestone messages queued for a thread have been posted (tests and shutdown).
        /// </summary>
        /// <param name="threadId">Thread identifier.</param>
        /// <returns>Task.</returns>
        public Task WaitForMilestonesAsync(string threadId)
        {
            return _ThreadChains.TryGetValue(threadId, out Task? chain) ? chain : Task.CompletedTask;
        }

        #endregion

        #region Private-Methods

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_Settings.Ask.TrackerIntervalSeconds), token).ConfigureAwait(false);
                    await SweepAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "sweep error: " + ex.Message);
                }
            }
        }

        private async Task HandleEntityChangedAsync(string entityType, string entityId, CancellationToken token)
        {
            List<AskTrackedWork> targets = new List<AskTrackedWork>();
            string type = (entityType ?? String.Empty).ToLowerInvariant();
            if (type == "mission")
            {
                targets.AddRange(await _Database.AskTrackedWork.EnumerateActiveByEntityAsync(AskTrackedEntityTypeEnum.Mission, entityId, token).ConfigureAwait(false));
                Mission? mission = await _Database.Missions.ReadAsync(entityId, token).ConfigureAwait(false);
                if (mission != null && !String.IsNullOrEmpty(mission.VoyageId))
                    targets.AddRange(await _Database.AskTrackedWork.EnumerateActiveByEntityAsync(AskTrackedEntityTypeEnum.Voyage, mission.VoyageId!, token).ConfigureAwait(false));
            }
            else if (type == "voyage")
            {
                targets.AddRange(await _Database.AskTrackedWork.EnumerateActiveByEntityAsync(AskTrackedEntityTypeEnum.Voyage, entityId, token).ConfigureAwait(false));
            }
            else
            {
                // Captain, check-run, and merge-entry changes affect mission rows of any card; the periodic sweep is cheap
                // enough to cover them without a reverse index.
                return;
            }

            foreach (AskTrackedWork work in targets.GroupBy(w => w.Id).Select(g => g.First()))
            {
                await RefreshAsync(work, token).ConfigureAwait(false);
            }
        }

        private void QueueMilestones(AskTrackedWork work, AskWorkSnapshot snapshot, List<AskMilestone> milestones)
        {
            lock (_ChainLock)
            {
                Task previous = _ThreadChains.TryGetValue(work.ThreadId, out Task? chain) ? chain : Task.CompletedTask;
                Task next = previous.ContinueWith(_ => PostMilestonesAsync(work, snapshot, milestones), TaskScheduler.Default).Unwrap();
                _ThreadChains[work.ThreadId] = next;
            }
        }

        private async Task PostMilestonesAsync(AskTrackedWork work, AskWorkSnapshot snapshot, List<AskMilestone> milestones)
        {
            try
            {
                AskThread? thread = await _Threads.ReadThreadInternalAsync(work.ThreadId).ConfigureAwait(false);
                if (thread == null) return;

                foreach (AskMilestone milestone in milestones)
                {
                    string text = milestone.Text;
                    string? captainId = null;
                    bool withOutcome = milestone.Terminal && AskWorkOutcomeFormatter.HasOutcome(snapshot);
                    AskWorkResult? result = null;
                    if (withOutcome)
                    {
                        // The final milestone states the outcome in typed, deterministic terms; the captain's voice is its
                        // report (a separate turn), so this one is not narrated.
                        try { result = await _Results.BuildAsync(work, snapshot, _Cts?.Token ?? CancellationToken.None).ConfigureAwait(false); }
                        catch (Exception ex) when (!(ex is OperationCanceledException)) { _Logging.Warn(_Header + "result of " + work.Id + " could not be built: " + ex.Message); }
                        text = AskWorkOutcomeFormatter.AppendOutcome(milestone.Text, snapshot, result);
                    }

                    Func<AskThread, string, AskWorkSnapshot, CancellationToken, Task<string?>>? narrate = withOutcome ? null : Narrate;
                    if (narrate != null)
                    {
                        string? narrated = null;
                        try { narrated = await narrate(thread, milestone.Text, snapshot, _Cts?.Token ?? CancellationToken.None).ConfigureAwait(false); }
                        catch (Exception ex) when (!(ex is OperationCanceledException)) { _Logging.Debug(_Header + "narration failed: " + ex.Message); }
                        if (!String.IsNullOrWhiteSpace(narrated))
                        {
                            text = narrated!;
                            captainId = thread.CaptainId;
                        }
                    }

                    AskMessage message = new AskMessage();
                    message.Role = captainId != null ? AskMessageRoleEnum.Assistant : AskMessageRoleEnum.System;
                    message.Kind = AskMessageKindEnum.WorkUpdate;
                    message.ContentText = text;
                    message.TrackedWorkId = work.Id;
                    message.CaptainId = captainId;
                    message = await _Threads.AppendMessageAsync(thread, message, true).ConfigureAwait(false);

                    Func<AskReportRequest, Task>? report = ReportResult;
                    if (withOutcome && report != null)
                    {
                        try { await report(new AskReportRequest(thread, work, snapshot, result, message.Sequence)).ConfigureAwait(false); }
                        catch (Exception ex) when (!(ex is OperationCanceledException)) { _Logging.Warn(_Header + "report of " + work.Id + " could not be scheduled: " + ex.Message); }
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "posting milestones for " + work.Id + " failed: " + ex.Message);
            }
        }

        private static string Clip(string? text, int max)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            return text.Length <= max ? text : text.Substring(0, max);
        }

        #endregion
    }
}

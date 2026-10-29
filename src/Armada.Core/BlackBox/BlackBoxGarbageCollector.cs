namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Black box garbage collection (docs/BLACKBOX.md, "Retention and garbage collection"). Each run: (1) finishes deletes
    /// left in the Deleting state, (2) expires entries past expires_utc that are not pinned and not protected by
    /// KeepLatestPerMission, (3) enforces quotas (maxEntriesPerVessel evicts the oldest unpinned non-current entries of
    /// a vessel; maxTotalBytes evicts the oldest unpinned resolved entries, then the oldest unpinned), (4) cascades
    /// (entries of deleted vessels with deleteWithVessel, of purged missions with deleteWithMission), (5) sweeps orphans
    /// (external keys under the key prefix with no object row, older than orphanGraceHours) and checks a page of entries
    /// for bytes deleted out of band (marking them ObjectMissing), then (6) emits blackbox.gc_completed. Deletes are
    /// two-phase: mark Deleting (hidden from readers), delete external bytes, then delete the rows; a store failure leaves
    /// the entry Deleting for the next run. A dry run reports candidates and deletes nothing. Also implements the
    /// lifecycle hooks (mission, vessel and tenant deletion, vessel retention changes) and the expires_utc recompute.
    /// Thread-safe (one run or cascade at a time).
    /// </summary>
    public class BlackBoxGarbageCollector : IBlackBoxLifecycleHooks
    {
        #region Public-Members

        /// <summary>
        /// The last run's report, or null.
        /// </summary>
        public BlackBoxGcReport? LastReport { get; private set; } = null;

        /// <summary>
        /// When the next scheduled run is due (UTC), or null when not scheduled. Set by the background host.
        /// </summary>
        public DateTime? NextRunUtc { get; set; } = null;

        /// <summary>
        /// The clock (UTC) used for expiry and the orphan grace period; tests move it forward.
        /// </summary>
        public Func<DateTime> Clock
        {
            get => _Clock;
            set => _Clock = value ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// Most keys the orphan sweep reads per target per run.
        /// </summary>
        public int MaxOrphanKeysPerRun { get; set; } = 100000;

        #endregion

        #region Private-Members

        private const string ReasonExpired = "Expired";
        private const string ReasonEvicted = "Evicted";
        private const string ReasonCascade = "Cascade";
        private const string ReasonRetry = "Retry";
        private const string ReasonTenant = "TenantDeleted";
        private const int MaxRounds = 1000;

        private readonly string _Header = "[BlackBoxGarbageCollector] ";
        private readonly DatabaseDriver _Database;
        private readonly BlackBoxSettings _Settings;
        private readonly BlackBoxStoreFactory _Stores;
        private readonly IBlackBoxEventSink _Events;
        private readonly LoggingModule _Logging;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private readonly object _RecomputeLock = new object();
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private string? _MissingCursor = null;
        private Task _Recompute = Task.CompletedTask;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Black box settings.</param>
        /// <param name="stores">Store factory.</param>
        /// <param name="events">Event sink.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxGarbageCollector(DatabaseDriver database, BlackBoxSettings settings, BlackBoxStoreFactory stores, IBlackBoxEventSink events, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Stores = stores ?? throw new ArgumentNullException(nameof(stores));
            _Events = events ?? throw new ArgumentNullException(nameof(events));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run garbage collection.
        /// </summary>
        /// <param name="dryRun">Report candidates and delete nothing.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        public async Task<BlackBoxGcReport> RunAsync(bool dryRun = false, CancellationToken token = default)
        {
            BlackBoxGcReport report = new BlackBoxGcReport { DryRun = dryRun, StartedUtc = DateTime.UtcNow };
            Stopwatch sw = Stopwatch.StartNew();
            HashSet<string> counted = new HashSet<string>(StringComparer.Ordinal);
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!dryRun) await RetryDeletingAsync(report, token).ConfigureAwait(false);
                await ExpireAsync(dryRun, report, counted, token).ConfigureAwait(false);
                await EnforceQuotasAsync(dryRun, report, counted, token).ConfigureAwait(false);
                await CascadeAsync(dryRun, report, counted, token).ConfigureAwait(false);
                await SweepOrphansAsync(dryRun, report, token).ConfigureAwait(false);
                await CheckMissingAsync(dryRun, report, token).ConfigureAwait(false);
            }
            finally
            {
                _Gate.Release();
            }
            report.FinishedUtc = DateTime.UtcNow;
            report.DurationMs = sw.ElapsedMilliseconds;
            if (!dryRun)
            {
                LastReport = report;
                await RefreshBytesMetricAsync(token).ConfigureAwait(false);
                await _Events.EmitAsync(BlackBoxEventTypes.GcCompleted, "Black box garbage collection: " + report.Expired + " expired, " + report.Evicted + " evicted, " + report.Cascaded + " cascaded, "
                    + report.OrphansRemoved + " orphans removed, " + report.ObjectsMissing + " objects missing, " + report.Failures + " failures",
                    null, null, null, null, null, report, token).ConfigureAwait(false);
                if (report.Expired + report.Evicted + report.Cascaded + report.OrphansRemoved + report.ObjectsMissing + report.Failures > 0)
                {
                    _Logging.Info(_Header + "expired " + report.Expired + ", evicted " + report.Evicted + ", cascaded " + report.Cascaded + ", orphans " + report.OrphansRemoved
                        + ", missing " + report.ObjectsMissing + ", failures " + report.Failures + ", freed " + report.BytesFreed + " bytes");
                }
            }
            return report;
        }

        /// <summary>
        /// Delete one entry in two phases: mark it Deleting, delete its external object bytes, then delete its rows
        /// (objects, files, links, notes, entry). A store failure leaves it Deleting (hidden, retried by the next run).
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="reason">Why (Manual, Purge, Expired, Evicted, Cascade, TenantDeleted, Retry), for the blackbox.deleted event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        public async Task<BlackBoxEntryDeleteResult> DeleteEntryAsync(BlackBoxEntry entry, string reason, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            BlackBoxEntryDeleteResult result = new BlackBoxEntryDeleteResult();
            if (entry.State != BlackBoxStateEnum.Deleting)
            {
                await _Database.BlackBoxEntries.UpdateStateAsync(entry.Id, BlackBoxStateEnum.Deleting, null, null, null, token).ConfigureAwait(false);
                entry.State = BlackBoxStateEnum.Deleting;
            }

            List<BlackBoxObjectRef> external = new List<BlackBoxObjectRef>();
            int page = 1;
            while (true)
            {
                EnumerationResult<BlackBoxObject> objects = await _Database.BlackBoxObjects.EnumerateAsync(entry.Id, new EnumerationQuery { PageNumber = page, PageSize = 100 }, token).ConfigureAwait(false);
                foreach (BlackBoxObject obj in objects.Objects.Where(o => o.StorageKey != null)) external.Add(BlackBoxObjectRepository.RefOf(entry, obj));
                if (page >= objects.TotalPages) break;
                page++;
            }
            for (int start = 0; start < external.Count && !result.Pending; start += Armada.Core.Services.CollectionLimits.MaxRequestItems)
            {
                try
                {
                    IBlackBoxStore store = _Stores.Get(entry.StorageTarget);
                    BlackBoxStoreDeleteResult deleted = await store.DeleteManyAsync(new BlackBoxObjectRefSet { References = external.Skip(start).Take(Armada.Core.Services.CollectionLimits.MaxRequestItems).ToList() }, token).ConfigureAwait(false);
                    if (deleted.Failed > 0)
                    {
                        result.Pending = true;
                        result.Error = deleted.FirstError ?? "store delete failed";
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    result.Pending = true;
                    result.Error = e.Message;
                }
            }
            if (result.Pending)
            {
                await _Database.BlackBoxEntries.UpdateStateAsync(entry.Id, BlackBoxStateEnum.Deleting, BlackBoxLimits.Clip("Store delete failed: " + result.Error, BlackBoxLimits.MaxShortTextChars), null, null, token).ConfigureAwait(false);
                return result;
            }

            result.Deleted = await _Database.BlackBoxEntries.DeleteCascadeAsync(entry.Id, token).ConfigureAwait(false);
            result.BytesFreed = entry.TotalBytes;
            if (result.Deleted)
            {
                await _Events.EmitAsync(BlackBoxEventTypes.Deleted, "Black box entry deleted (" + reason + "): " + entry.Id, entry.TenantId, entry.Id, entry.MissionId, entry.VoyageId, entry.VesselId,
                    new BlackBoxDeletedPayload { EntryId = entry.Id, MissionId = entry.MissionId, VoyageId = entry.VoyageId, Reason = reason }, token).ConfigureAwait(false);
            }
            return result;
        }

        /// <summary>
        /// Delete the entries that cascade with a subject now (instead of waiting for the next run). MissionDeleted and
        /// VesselDeleted follow deleteWithMission and deleteWithVessel and only touch entries whose subject row is gone;
        /// TenantDeleted always deletes every entry of the tenant, pinned ones included.
        /// </summary>
        /// <param name="reason">Cascade.</param>
        /// <param name="subjectId">Mission, vessel or tenant id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report (Cascaded, BytesFreed, Failures).</returns>
        /// <exception cref="ArgumentNullException">Thrown when subjectId is null or empty.</exception>
        public async Task<BlackBoxGcReport> CascadeAsync(BlackBoxCascadeReasonEnum reason, string subjectId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(subjectId)) throw new ArgumentNullException(nameof(subjectId));
            BlackBoxGcReport report = new BlackBoxGcReport { StartedUtc = DateTime.UtcNow };
            if (reason == BlackBoxCascadeReasonEnum.MissionDeleted && !_Settings.Retention.DeleteWithMission) return report;
            if (reason == BlackBoxCascadeReasonEnum.VesselDeleted && !_Settings.Retention.DeleteWithVessel) return report;
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await CascadeOneAsync(reason, subjectId, false, report, new HashSet<string>(StringComparer.Ordinal), token).ConfigureAwait(false);
            }
            finally
            {
                _Gate.Release();
            }
            report.FinishedUtc = DateTime.UtcNow;
            return report;
        }

        /// <inheritdoc />
        public async Task OnMissionDeletedAsync(string missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) return;
            try
            {
                await CascadeAsync(BlackBoxCascadeReasonEnum.MissionDeleted, missionId, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logging.Warn(_Header + "cascade for deleted mission " + missionId + " failed (the next run retries): " + e.Message);
            }
        }

        /// <inheritdoc />
        public async Task OnVesselDeletedAsync(string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(vesselId)) return;
            try
            {
                await CascadeAsync(BlackBoxCascadeReasonEnum.VesselDeleted, vesselId, token).ConfigureAwait(false);
                // Deleting a vessel deletes its missions too; their entries follow deleteWithMission.
                if (_Settings.Retention.DeleteWithMission) await CascadeAllMissionsAsync(token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logging.Warn(_Header + "cascade for deleted vessel " + vesselId + " failed (the next run retries): " + e.Message);
            }
        }

        private async Task CascadeAllMissionsAsync(CancellationToken token)
        {
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await CascadeOneAsync(BlackBoxCascadeReasonEnum.MissionDeleted, null, false, new BlackBoxGcReport(), new HashSet<string>(StringComparer.Ordinal), token).ConfigureAwait(false);
            }
            finally
            {
                _Gate.Release();
            }
        }

        /// <inheritdoc />
        public async Task OnTenantDeletedAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) return;
            try
            {
                await CascadeAsync(BlackBoxCascadeReasonEnum.TenantDeleted, tenantId, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logging.Warn(_Header + "black box cleanup for deleted tenant " + tenantId + " failed: " + e.Message);
            }
        }

        /// <inheritdoc />
        public Task OnVesselUpdatedAsync(string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(vesselId)) return Task.CompletedTask;
            StartRecompute(vesselId);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Start a background expires_utc recompute (after a retention policy change), unless one is running.
        /// </summary>
        /// <param name="vesselId">Only this vessel's entries, or null for all.</param>
        /// <returns>The recompute task (completes with the number of entries whose expiry changed).</returns>
        public Task StartRecompute(string? vesselId = null)
        {
            lock (_RecomputeLock)
            {
                if (!_Recompute.IsCompleted && vesselId == null) return _Recompute;
                Task previous = _Recompute;
                _Recompute = Task.Run(async () =>
                {
                    try { await previous.ConfigureAwait(false); } catch (Exception) { }
                    try
                    {
                        int changed = await RecomputeExpiryAsync(vesselId).ConfigureAwait(false);
                        if (changed > 0) _Logging.Info(_Header + "recomputed expiry of " + changed + " entr" + (changed == 1 ? "y" : "ies") + (vesselId != null ? " of " + vesselId : String.Empty));
                    }
                    catch (Exception e)
                    {
                        _Logging.Warn(_Header + "expiry recompute failed: " + e.Message);
                    }
                });
                return _Recompute;
            }
        }

        /// <summary>
        /// Recompute expires_utc for every entry (or one vessel's) from the current policy and vessel overrides.
        /// </summary>
        /// <param name="vesselId">Only this vessel's entries, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries whose expiry changed.</returns>
        public async Task<int> RecomputeExpiryAsync(string? vesselId = null, CancellationToken token = default)
        {
            Dictionary<string, int?> vesselDays = new Dictionary<string, int?>(StringComparer.Ordinal);
            int changed = 0;
            string? cursor = null;
            for (int round = 0; round < 100000; round++)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<BlackBoxEntry> page = await _Database.BlackBoxEntries.ScanAsync(new BlackBoxScanQuery { AfterId = cursor, VesselId = vesselId, PageSize = 200 }, token).ConfigureAwait(false);
                if (page.Objects.Count == 0) break;
                foreach (BlackBoxEntry entry in page.Objects)
                {
                    int? days = null;
                    if (!String.IsNullOrEmpty(entry.VesselId))
                    {
                        if (!vesselDays.TryGetValue(entry.VesselId, out days))
                        {
                            Vessel? vessel = await _Database.Vessels.ReadAsync(entry.VesselId, token).ConfigureAwait(false);
                            days = vessel?.BlackBoxRetentionDays;
                            vesselDays[entry.VesselId] = days;
                        }
                    }
                    DateTime? expiry = BlackBoxRetentionPolicy.ComputeExpiry(entry, _Settings.Retention, days);
                    if (!SameInstant(expiry, entry.ExpiresUtc))
                    {
                        if (await _Database.BlackBoxEntries.UpdateExpiresAsync(entry.Id, expiry, token).ConfigureAwait(false)) changed++;
                    }
                }
                cursor = page.Objects[page.Objects.Count - 1].Id;
            }
            return changed;
        }

        #endregion

        #region Private-Methods

        private async Task RetryDeletingAsync(BlackBoxGcReport report, CancellationToken token)
        {
            int batch = _Settings.Retention.GcBatchSize;
            EnumerationResult<BlackBoxEntry> deleting = await _Database.BlackBoxEntries.EnumerateDeletingAsync(new EnumerationQuery { PageSize = batch }, token).ConfigureAwait(false);
            foreach (BlackBoxEntry entry in deleting.Objects)
            {
                BlackBoxEntryDeleteResult result = await DeleteEntryAsync(entry, ReasonRetry, token).ConfigureAwait(false);
                Count(report, entry, result, ReasonRetry);
            }
        }

        private async Task ExpireAsync(bool dryRun, BlackBoxGcReport report, HashSet<string> counted, CancellationToken token)
        {
            int batch = _Settings.Retention.GcBatchSize;
            DateTime now = Clock();
            int page = 1;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int round = 0; round < MaxRounds; round++)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<BlackBoxEntry> expired = await _Database.BlackBoxEntries.EnumerateExpiredAsync(now, _Settings.Retention.KeepLatestPerMission, new EnumerationQuery { PageNumber = page, PageSize = batch }, token).ConfigureAwait(false);
                List<BlackBoxEntry> fresh = expired.Objects.Where(e => seen.Add(e.Id)).ToList();
                if (fresh.Count == 0) break;
                foreach (BlackBoxEntry entry in fresh) await DeleteOrCountAsync(entry, ReasonExpired, dryRun, report, counted, token).ConfigureAwait(false);
                if (dryRun) page++;
            }
        }

        private async Task EnforceQuotasAsync(bool dryRun, BlackBoxGcReport report, HashSet<string> counted, CancellationToken token)
        {
            BlackBoxRetentionSettings policy = _Settings.Retention;
            int batch = policy.GcBatchSize;

            // Per-vessel count: evict the oldest unpinned, non-current entries above maxEntriesPerVessel.
            HashSet<string> vesselsDone = new HashSet<string>(StringComparer.Ordinal);
            for (int round = 0; round < MaxRounds; round++)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<string> over = await _Database.BlackBoxEntries.EnumerateVesselsOverQuotaAsync(policy.MaxEntriesPerVessel, new EnumerationQuery { PageSize = 100 }, token).ConfigureAwait(false);
                List<string> todo = over.Objects.Where(v => vesselsDone.Add(v)).ToList();
                if (todo.Count == 0) break;
                foreach (string vesselId in todo)
                {
                    long count = await _Database.BlackBoxEntries.CountAsync(new BlackBoxEnumerateRequest { VesselId = vesselId }, token).ConfigureAwait(false);
                    long excess = count - policy.MaxEntriesPerVessel;
                    int candidatePage = 1;
                    while (excess > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        EnumerationResult<BlackBoxEntry> candidates = await _Database.BlackBoxEntries.EnumerateEvictionCandidatesAsync(new BlackBoxEvictionQuery
                        {
                            VesselId = vesselId,
                            ExcludeCurrent = true,
                            PageNumber = dryRun ? candidatePage : 1,
                            PageSize = dryRun ? batch : (int)Math.Min(batch, excess)
                        }, token).ConfigureAwait(false);
                        List<BlackBoxEntry> fresh = candidates.Objects.Where(e => !counted.Contains(e.Id)).ToList();
                        if (fresh.Count == 0) break;
                        int progressed = 0;
                        foreach (BlackBoxEntry entry in fresh)
                        {
                            if (excess <= 0) break;
                            if (await DeleteOrCountAsync(entry, ReasonEvicted, dryRun, report, counted, token).ConfigureAwait(false)) { excess--; progressed++; }
                        }
                        if (progressed == 0) break;
                        candidatePage++;
                    }
                }
            }

            // Global bytes: oldest unpinned resolved entries first, then the oldest unpinned.
            long total = await _Database.BlackBoxEntries.SumBytesAsync(null, null, token).ConfigureAwait(false);
            if (dryRun) total -= report.BytesFreed;
            long overBytes = total - policy.MaxTotalBytes;
            foreach (bool resolvedOnly in new[] { true, false })
            {
                int candidatePage = 1;
                for (int round = 0; round < MaxRounds && overBytes > 0; round++)
                {
                    token.ThrowIfCancellationRequested();
                    EnumerationResult<BlackBoxEntry> candidates = await _Database.BlackBoxEntries.EnumerateEvictionCandidatesAsync(new BlackBoxEvictionQuery
                    {
                        ResolvedOnly = resolvedOnly,
                        KeepLatestPerMission = policy.KeepLatestPerMission,
                        PageNumber = candidatePage,
                        PageSize = batch
                    }, token).ConfigureAwait(false);
                    if (candidates.Objects.Count == 0) break;
                    List<BlackBoxEntry> fresh = candidates.Objects.Where(e => !counted.Contains(e.Id)).ToList();
                    int progressed = 0;
                    foreach (BlackBoxEntry entry in fresh)
                    {
                        if (overBytes <= 0) break;
                        if (await DeleteOrCountAsync(entry, ReasonEvicted, dryRun, report, counted, token).ConfigureAwait(false))
                        {
                            overBytes -= entry.TotalBytes;
                            progressed++;
                        }
                    }
                    // A real run deletes what it read (a failed delete leaves the entry Deleting, which the query skips), so
                    // page 1 is the next page; a dry run deletes nothing and moves on.
                    if (dryRun) candidatePage++;
                    else if (progressed == 0) break;
                }
            }
        }

        private async Task CascadeAsync(bool dryRun, BlackBoxGcReport report, HashSet<string> counted, CancellationToken token)
        {
            if (_Settings.Retention.DeleteWithVessel) await CascadeOneAsync(BlackBoxCascadeReasonEnum.VesselDeleted, null, dryRun, report, counted, token).ConfigureAwait(false);
            if (_Settings.Retention.DeleteWithMission) await CascadeOneAsync(BlackBoxCascadeReasonEnum.MissionDeleted, null, dryRun, report, counted, token).ConfigureAwait(false);
        }

        private async Task CascadeOneAsync(BlackBoxCascadeReasonEnum reason, string? subjectId, bool dryRun, BlackBoxGcReport report, HashSet<string> counted, CancellationToken token)
        {
            int batch = _Settings.Retention.GcBatchSize;
            string label = reason == BlackBoxCascadeReasonEnum.TenantDeleted ? ReasonTenant : ReasonCascade;
            int page = 1;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int round = 0; round < MaxRounds; round++)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<BlackBoxEntry> candidates = await _Database.BlackBoxEntries.EnumerateCascadeCandidatesAsync(reason, subjectId, new EnumerationQuery { PageNumber = page, PageSize = batch }, token).ConfigureAwait(false);
                List<BlackBoxEntry> fresh = candidates.Objects.Where(e => seen.Add(e.Id)).ToList();
                if (fresh.Count == 0) break;
                foreach (BlackBoxEntry entry in fresh) await DeleteOrCountAsync(entry, label, dryRun, report, counted, token).ConfigureAwait(false);
                if (dryRun) page++;
            }
        }

        private async Task SweepOrphansAsync(bool dryRun, BlackBoxGcReport report, CancellationToken token)
        {
            HashSet<BlackBoxStorageTargetEnum> targets = new HashSet<BlackBoxStorageTargetEnum>();
            if (_Settings.Storage.Target != BlackBoxStorageTargetEnum.Database && _Settings.Storage.Target != BlackBoxStorageTargetEnum.Unknown) targets.Add(_Settings.Storage.Target);
            foreach (BlackBoxStorageTargetUsage usage in await _Database.BlackBoxEntries.SumBytesByTargetAsync(null, token).ConfigureAwait(false))
            {
                if (usage.Target != BlackBoxStorageTargetEnum.Database && usage.Target != BlackBoxStorageTargetEnum.Unknown && usage.Entries > 0) targets.Add(usage.Target);
            }
            DateTime cutoff = Clock().AddHours(-_Settings.Retention.OrphanGraceHours);
            string prefix = _Settings.Storage.KeyPrefix.Trim('/') + "/";
            foreach (BlackBoxStorageTargetEnum target in targets)
            {
                IBlackBoxStore store;
                try
                {
                    store = _Stores.Get(target);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Fail(report, target + " orphan sweep: " + e.Message);
                    continue;
                }
                try
                {
                    List<BlackBoxStoredKey> batch = new List<BlackBoxStoredKey>();
                    int read = 0;
                    await foreach (BlackBoxStoredKey key in store.EnumerateAsync(prefix, token).ConfigureAwait(false))
                    {
                        batch.Add(key);
                        read++;
                        if (batch.Count >= 200)
                        {
                            await SweepBatchAsync(store, batch, cutoff, dryRun, report, token).ConfigureAwait(false);
                            batch.Clear();
                        }
                        if (read >= MaxOrphanKeysPerRun) break;
                    }
                    if (batch.Count > 0) await SweepBatchAsync(store, batch, cutoff, dryRun, report, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Fail(report, target + " orphan sweep: " + e.Message);
                }
            }
        }

        private async Task SweepBatchAsync(IBlackBoxStore store, List<BlackBoxStoredKey> keys, DateTime cutoff, bool dryRun, BlackBoxGcReport report, CancellationToken token)
        {
            HashSet<string> referenced = new HashSet<string>(await _Database.BlackBoxObjects.FilterReferencedStorageKeysAsync(keys.Select(k => k.Key).ToList(), token).ConfigureAwait(false), StringComparer.Ordinal);
            List<BlackBoxObjectRef> orphans = new List<BlackBoxObjectRef>();
            foreach (BlackBoxStoredKey key in keys)
            {
                if (referenced.Contains(key.Key)) continue;
                // Keep a key of unknown age, and any key younger than the grace period (a write may be in flight).
                if (!key.LastModifiedUtc.HasValue || key.LastModifiedUtc.Value > cutoff) continue;
                orphans.Add(new BlackBoxObjectRef { Target = store.Target, Name = key.Key, StorageKey = key.Key });
            }
            if (orphans.Count == 0) return;
            if (dryRun)
            {
                report.OrphansRemoved += orphans.Count;
                return;
            }
            BlackBoxStoreDeleteResult deleted = await store.DeleteManyAsync(new BlackBoxObjectRefSet { References = orphans }, token).ConfigureAwait(false);
            report.OrphansRemoved += deleted.Deleted;
            if (deleted.Failed > 0) Fail(report, store.Target + " orphan delete: " + (deleted.FirstError ?? "failed"));
            foreach (BlackBoxObjectRef orphan in orphans.Take(10)) _Logging.Debug(_Header + "orphan " + orphan.StorageKey + " in " + store.Target);
        }

        private async Task CheckMissingAsync(bool dryRun, BlackBoxGcReport report, CancellationToken token)
        {
            EnumerationResult<BlackBoxEntry> page = await _Database.BlackBoxEntries.ScanAsync(new BlackBoxScanQuery { AfterId = _MissingCursor, PageSize = _Settings.Retention.GcBatchSize }, token).ConfigureAwait(false);
            if (page.Objects.Count == 0)
            {
                _MissingCursor = null;
                return;
            }
            foreach (BlackBoxEntry entry in page.Objects)
            {
                token.ThrowIfCancellationRequested();
                int missing = 0;
                int objectPage = 1;
                try
                {
                    while (true)
                    {
                        EnumerationResult<BlackBoxObject> objects = await _Database.BlackBoxObjects.EnumerateAsync(entry.Id, new EnumerationQuery { PageNumber = objectPage, PageSize = 100 }, token).ConfigureAwait(false);
                        foreach (BlackBoxObject obj in objects.Objects.Where(o => !o.IsMissing))
                        {
                            BlackBoxObjectRef reference = BlackBoxObjectRepository.RefOf(entry, obj);
                            if (await _Stores.Get(reference.Target).ExistsAsync(reference, token).ConfigureAwait(false)) continue;
                            missing++;
                            if (!dryRun) await _Database.BlackBoxObjects.SetMissingAsync(obj.Id, true, token).ConfigureAwait(false);
                        }
                        if (objectPage >= objects.TotalPages) break;
                        objectPage++;
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Fail(report, entry.Id + " missing-object check: " + e.Message);
                    continue;
                }
                if (missing == 0) continue;
                report.ObjectsMissing += missing;
                if (!dryRun && entry.State != BlackBoxStateEnum.ObjectMissing)
                    await _Database.BlackBoxEntries.UpdateStateAsync(entry.Id, BlackBoxStateEnum.ObjectMissing, missing + " object(s) were deleted from the " + entry.StorageTarget + " store out of band.", null, null, token).ConfigureAwait(false);
                _Logging.Warn(_Header + missing + " object(s) of " + entry.Id + " are missing from the " + entry.StorageTarget + " store");
            }
            _MissingCursor = page.Objects.Count < _Settings.Retention.GcBatchSize ? null : page.Objects[page.Objects.Count - 1].Id;
        }

        private async Task<bool> DeleteOrCountAsync(BlackBoxEntry entry, string reason, bool dryRun, BlackBoxGcReport report, HashSet<string> counted, CancellationToken token)
        {
            if (!counted.Add(entry.Id)) return false;
            if (dryRun)
            {
                Tally(report, reason);
                report.BytesFreed += entry.TotalBytes;
                Sample(report, entry.Id);
                return true;
            }
            BlackBoxEntryDeleteResult result = await DeleteEntryAsync(entry, reason, token).ConfigureAwait(false);
            Count(report, entry, result, reason);
            return result.Deleted;
        }

        private async Task RefreshBytesMetricAsync(CancellationToken token)
        {
            try
            {
                BlackBoxMetrics.SetBytes(await _Database.BlackBoxEntries.SumBytesByTargetAsync(null, token).ConfigureAwait(false));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logging.Debug(_Header + "bytes metric not refreshed: " + e.Message);
            }
        }

        private static void Count(BlackBoxGcReport report, BlackBoxEntry entry, BlackBoxEntryDeleteResult result, string reason)
        {
            if (result.Deleted)
            {
                Tally(report, reason);
                report.BytesFreed += result.BytesFreed;
                Sample(report, entry.Id);
            }
            else
            {
                Fail(report, entry.Id + ": " + (result.Error ?? "not deleted"));
            }
        }

        private static void Tally(BlackBoxGcReport report, string reason)
        {
            switch (reason)
            {
                case ReasonRetry: report.RetriedDeletes++; break;
                case ReasonEvicted: report.Evicted++; break;
                case ReasonCascade: report.Cascaded++; break;
                case ReasonTenant: report.Cascaded++; break;
                default: report.Expired++; break;
            }
        }

        private static void Fail(BlackBoxGcReport report, string error)
        {
            report.Failures++;
            if (report.Errors.Count < BlackBoxLimits.MaxErrors) report.Errors.Add(BlackBoxLimits.Clip(error, BlackBoxLimits.MaxShortTextChars)!);
        }

        private static void Sample(BlackBoxGcReport report, string id)
        {
            if (report.CandidateEntryIds.Count < BlackBoxLimits.MaxSampleIds) report.CandidateEntryIds.Add(id);
        }

        private static bool SameInstant(DateTime? a, DateTime? b)
        {
            if (!a.HasValue || !b.HasValue) return a.HasValue == b.HasValue;
            return Math.Abs((a.Value.ToUniversalTime() - b.Value.ToUniversalTime()).TotalSeconds) < 1;
        }

        #endregion
    }
}

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
    /// Black box garbage collection (docs/BLACKBOX.md, "Retention"). Each run: (1) finishes deletes left in the Deleting
    /// state, (2) expires entries past expires_utc that are not pinned and not protected by KeepLatestPerMission,
    /// (3) quotas, (4) cascades and (5) the orphan sweep, then (6) emits blackbox.gc_completed. Deletes are two-phase: mark
    /// Deleting (hidden from readers), delete external bytes, then delete the rows; a store failure leaves the entry
    /// Deleting for the next run. A dry run reports candidates and deletes nothing. Thread-safe (one run at a time).
    /// TODO(blackbox-wave2: gc) per-vessel and global quotas, vessel and mission cascades, orphan sweep for external targets.
    /// </summary>
    public class BlackBoxGarbageCollector
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

        #endregion

        #region Private-Members

        private readonly string _Header = "[BlackBoxGarbageCollector] ";
        private readonly DatabaseDriver _Database;
        private readonly BlackBoxSettings _Settings;
        private readonly BlackBoxStoreFactory _Stores;
        private readonly IBlackBoxEventSink _Events;
        private readonly LoggingModule _Logging;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);

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
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!dryRun) await RetryDeletingAsync(report, token).ConfigureAwait(false);
                await ExpireAsync(dryRun, report, token).ConfigureAwait(false);
                await EnforceQuotasAsync(dryRun, report, token).ConfigureAwait(false);
                await CascadeAsync(dryRun, report, token).ConfigureAwait(false);
                await SweepOrphansAsync(dryRun, report, token).ConfigureAwait(false);
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
                await _Events.EmitAsync(BlackBoxEventTypes.GcCompleted, "Black box garbage collection: " + report.Expired + " expired, " + report.Failures + " failures",
                    null, null, null, null, null, report, token).ConfigureAwait(false);
                if (report.Expired > 0 || report.Failures > 0) _Logging.Info(_Header + "expired " + report.Expired + ", failures " + report.Failures + ", freed " + report.BytesFreed + " bytes");
            }
            return report;
        }

        /// <summary>
        /// Delete one entry in two phases: mark it Deleting, delete its external object bytes, then delete its rows
        /// (objects, files, links, notes, entry). A store failure leaves it Deleting (hidden, retried by the next run).
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="reason">Why (Manual, Purge, Expired, Evicted, Cascade), for the blackbox.deleted event.</param>
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
            if (external.Count > 0)
            {
                try
                {
                    IBlackBoxStore store = _Stores.Get(entry.StorageTarget);
                    BlackBoxStoreDeleteResult deleted = await store.DeleteManyAsync(new BlackBoxObjectRefSet { References = external.Take(Armada.Core.Services.CollectionLimits.MaxRequestItems).ToList() }, token).ConfigureAwait(false);
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
                await _Database.BlackBoxEntries.UpdateStateAsync(entry.Id, BlackBoxStateEnum.Deleting, "Store delete failed: " + result.Error, null, null, token).ConfigureAwait(false);
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

        #endregion

        #region Private-Methods

        private async Task RetryDeletingAsync(BlackBoxGcReport report, CancellationToken token)
        {
            int batch = _Settings.Retention.GcBatchSize;
            EnumerationResult<BlackBoxEntry> deleting = await _Database.BlackBoxEntries.EnumerateDeletingAsync(new EnumerationQuery { PageSize = batch }, token).ConfigureAwait(false);
            foreach (BlackBoxEntry entry in deleting.Objects)
            {
                BlackBoxEntryDeleteResult result = await DeleteEntryAsync(entry, "Retry", token).ConfigureAwait(false);
                Count(report, entry, result, true);
            }
        }

        private async Task ExpireAsync(bool dryRun, BlackBoxGcReport report, CancellationToken token)
        {
            int batch = _Settings.Retention.GcBatchSize;
            DateTime now = DateTime.UtcNow;
            int page = 1;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int round = 0; round < 1000; round++)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<BlackBoxEntry> expired = await _Database.BlackBoxEntries.EnumerateExpiredAsync(now, _Settings.Retention.KeepLatestPerMission, new EnumerationQuery { PageNumber = page, PageSize = batch }, token).ConfigureAwait(false);
                List<BlackBoxEntry> fresh = expired.Objects.Where(e => seen.Add(e.Id)).ToList();
                if (fresh.Count == 0) break;
                foreach (BlackBoxEntry entry in fresh)
                {
                    if (dryRun)
                    {
                        report.Expired++;
                        report.BytesFreed += entry.TotalBytes;
                        Sample(report, entry.Id);
                        continue;
                    }
                    BlackBoxEntryDeleteResult result = await DeleteEntryAsync(entry, "Expired", token).ConfigureAwait(false);
                    Count(report, entry, result, false);
                }
                if (dryRun) page++;
            }
        }

        private Task EnforceQuotasAsync(bool dryRun, BlackBoxGcReport report, CancellationToken token)
        {
            // TODO(blackbox-wave2: gc): evict above Retention.MaxEntriesPerVessel (oldest unpinned, non-current first) and
            // above Retention.MaxTotalBytes (oldest unpinned resolved first, then oldest unpinned), counting into report.Evicted.
            return Task.CompletedTask;
        }

        private Task CascadeAsync(bool dryRun, BlackBoxGcReport report, CancellationToken token)
        {
            // TODO(blackbox-wave2: gc): delete entries of deleted vessels (Retention.DeleteWithVessel) and purged missions
            // (Retention.DeleteWithMission), counting into report.Cascaded.
            return Task.CompletedTask;
        }

        private Task SweepOrphansAsync(bool dryRun, BlackBoxGcReport report, CancellationToken token)
        {
            // TODO(blackbox-wave2: gc): for external targets, enumerate keys under Storage.KeyPrefix, delete keys with no row
            // older than Retention.OrphanGraceHours (report.OrphansRemoved), and mark rows whose bytes are gone ObjectMissing
            // (report.ObjectsMissing). The Database target has no orphans: its bytes live in the rows.
            return Task.CompletedTask;
        }

        private static void Count(BlackBoxGcReport report, BlackBoxEntry entry, BlackBoxEntryDeleteResult result, bool retry)
        {
            if (result.Deleted)
            {
                if (retry) report.RetriedDeletes++;
                else report.Expired++;
                report.BytesFreed += result.BytesFreed;
                Sample(report, entry.Id);
            }
            else
            {
                report.Failures++;
                if (result.Error != null && report.Errors.Count < BlackBoxLimits.MaxErrors) report.Errors.Add(BlackBoxLimits.Clip(entry.Id + ": " + result.Error, BlackBoxLimits.MaxShortTextChars)!);
            }
        }

        private static void Sample(BlackBoxGcReport report, string id)
        {
            if (report.CandidateEntryIds.Count < BlackBoxLimits.MaxSampleIds) report.CandidateEntryIds.Add(id);
        }

        #endregion
    }
}

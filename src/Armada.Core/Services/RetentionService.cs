namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Applies <see cref="RetentionSettings"/> (V1 readiness W3.4): archives and optionally deletes inactive Ask
    /// threads, deletes old finished background jobs (always keeping the newest of each kind and name per tenant), and deletes
    /// old finished vessel import batches. Works on every database provider through the driver interfaces. The
    /// Admiral calls <see cref="PruneAsync"/> on the health-check loop's slow cadence. Settings are read on every
    /// call, so changes apply live. Not designed for concurrent calls.
    /// </summary>
    public class RetentionService
    {
        #region Private-Members

        private const int _BatchSize = 500;
        private const int _MaxBatchesPerPass = 200;

        private string _Header = "[RetentionService] ";
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private LoggingModule _Logging;
        private Func<DateTime> _Clock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings; <see cref="ArmadaSettings.Retention"/> is read on every pass.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="clock">UTC clock; defaults to <see cref="DateTime.UtcNow"/>.</param>
        /// <exception cref="ArgumentNullException">When database, settings, or logging is null.</exception>
        public RetentionService(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging, Func<DateTime>? clock = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Clock = clock ?? (() => DateTime.UtcNow);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run every retention rule once. A failing rule is logged and does not stop the others.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts of what was archived and deleted.</returns>
        public async Task<RetentionResult> PruneAsync(CancellationToken token = default)
        {
            RetentionResult result = new RetentionResult();

            try { result.AskThreadsDeleted = await DeleteInactiveAskThreadsAsync(token).ConfigureAwait(false); }
            catch (Exception ex) when (!(ex is OperationCanceledException)) { _Logging.Warn(_Header + "Ask thread deletion error: " + ex.Message); }

            try { result.AskThreadsArchived = await ArchiveInactiveAskThreadsAsync(token).ConfigureAwait(false); }
            catch (Exception ex) when (!(ex is OperationCanceledException)) { _Logging.Warn(_Header + "Ask thread archiving error: " + ex.Message); }

            try { result.JobsDeleted = await PruneJobsAsync(token).ConfigureAwait(false); }
            catch (Exception ex) when (!(ex is OperationCanceledException)) { _Logging.Warn(_Header + "job pruning error: " + ex.Message); }

            try { result.ImportBatchesDeleted = await PruneImportBatchesAsync(token).ConfigureAwait(false); }
            catch (Exception ex) when (!(ex is OperationCanceledException)) { _Logging.Warn(_Header + "import batch pruning error: " + ex.Message); }

            if (result.Total > 0)
            {
                _Logging.Info(_Header + "archived " + result.AskThreadsArchived + " and deleted " + result.AskThreadsDeleted + " Ask thread(s); deleted " +
                    result.JobsDeleted + " job(s) and " + result.ImportBatchesDeleted + " import batch(es)");
            }

            return result;
        }

        /// <summary>
        /// Archive unpinned Ask threads inactive for <see cref="RetentionSettings.AskThreadArchiveAfterDays"/> days.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Threads archived.</returns>
        public async Task<int> ArchiveInactiveAskThreadsAsync(CancellationToken token = default)
        {
            int days = _Settings.Retention.AskThreadArchiveAfterDays;
            if (days <= 0) return 0;

            DateTime cutoff = _Clock().AddDays(-days);
            int archived = 0;
            for (int batch = 0; batch < _MaxBatchesPerPass; batch++)
            {
                token.ThrowIfCancellationRequested();
                List<AskThread> threads = await _Database.AskThreads.EnumerateInactiveAsync(cutoff, false, _BatchSize, token).ConfigureAwait(false);
                if (threads.Count == 0) break;
                foreach (AskThread thread in threads)
                {
                    thread.Archived = true;
                    await _Database.AskThreads.UpdateAsync(thread, token).ConfigureAwait(false);
                    archived++;
                }

                if (threads.Count < _BatchSize) break;
            }

            return archived;
        }

        /// <summary>
        /// Delete unpinned Ask threads inactive for <see cref="RetentionSettings.AskThreadDeleteAfterDays"/> days,
        /// with their messages, tool calls, proposals, and tracked work.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Threads deleted.</returns>
        public async Task<int> DeleteInactiveAskThreadsAsync(CancellationToken token = default)
        {
            int days = _Settings.Retention.AskThreadDeleteAfterDays;
            if (days <= 0) return 0;

            DateTime cutoff = _Clock().AddDays(-days);
            int deleted = 0;
            for (int batch = 0; batch < _MaxBatchesPerPass; batch++)
            {
                token.ThrowIfCancellationRequested();
                List<AskThread> threads = await _Database.AskThreads.EnumerateInactiveAsync(cutoff, true, _BatchSize, token).ConfigureAwait(false);
                if (threads.Count == 0) break;
                int deletedThisBatch = 0;
                foreach (AskThread thread in threads)
                {
                    if (String.IsNullOrEmpty(thread.TenantId) || String.IsNullOrEmpty(thread.UserId)) continue;
                    if (await _Database.AskThreads.DeleteAsync(thread.TenantId!, thread.UserId!, thread.Id, token).ConfigureAwait(false))
                    {
                        deleted++;
                        deletedThisBatch++;
                    }
                }

                // Stop when nothing could be deleted (avoids re-reading the same undeletable rows) or the page was short.
                if (deletedThisBatch == 0 || threads.Count < _BatchSize) break;
            }

            return deleted;
        }

        /// <summary>
        /// Delete finished background jobs that completed more than <see cref="RetentionSettings.JobRetentionDays"/>
        /// days ago, keeping the newest finished job of each kind and name per tenant.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Jobs deleted.</returns>
        public async Task<int> PruneJobsAsync(CancellationToken token = default)
        {
            int days = _Settings.Retention.JobRetentionDays;
            if (days <= 0) return 0;

            DateTime cutoff = _Clock().AddDays(-days);
            List<Job> jobs = await _Database.Jobs.EnumerateAsync(token).ConfigureAwait(false);
            List<Job> finished = jobs.Where(j => JobStateMachine.IsTerminal(j.Status)).ToList();

            HashSet<string> newestPerKind = new HashSet<string>(
                finished
                    .GroupBy(j => (j.TenantId ?? "") + "|" + j.Kind + "|" + j.Name)
                    .Select(g => g.OrderByDescending(FinishedUtc).ThenByDescending(j => j.Id, StringComparer.Ordinal).First().Id),
                StringComparer.Ordinal);

            int deleted = 0;
            foreach (Job job in finished)
            {
                token.ThrowIfCancellationRequested();
                if (newestPerKind.Contains(job.Id)) continue;
                if (FinishedUtc(job) >= cutoff) continue;
                await _Database.Jobs.DeleteAsync(job.Id, token).ConfigureAwait(false);
                deleted++;
            }

            return deleted;
        }

        /// <summary>
        /// Delete finished vessel import batches last updated more than
        /// <see cref="RetentionSettings.ImportBatchRetentionDays"/> days ago, with their items and recommendations.
        /// Batches whose fleet categorization is still pending or running are kept.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Batches deleted.</returns>
        public async Task<int> PruneImportBatchesAsync(CancellationToken token = default)
        {
            int days = _Settings.Retention.ImportBatchRetentionDays;
            if (days <= 0) return 0;

            DateTime cutoff = _Clock().AddDays(-days);
            List<TenantMetadata> tenants = await _Database.Tenants.EnumerateAsync(token).ConfigureAwait(false);
            int deleted = 0;

            foreach (TenantMetadata tenant in tenants)
            {
                if (String.IsNullOrEmpty(tenant.Id)) continue;
                List<string> expired = new List<string>();
                int pageNumber = 1;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    EnumerationQuery query = new EnumerationQuery { PageNumber = pageNumber, PageSize = 100, CreatedBefore = cutoff };
                    EnumerationResult<VesselImportBatch> page = await _Database.VesselImportBatches.EnumerateAsync(tenant.Id, query, token).ConfigureAwait(false);
                    foreach (VesselImportBatch batch in page.Objects)
                    {
                        if (!IsFinished(batch)) continue;
                        DateTime last = batch.CompletedUtc.HasValue && batch.CompletedUtc.Value > batch.LastUpdateUtc ? batch.CompletedUtc.Value : batch.LastUpdateUtc;
                        if (last < cutoff) expired.Add(batch.Id);
                    }

                    if (page.Objects.Count < query.PageSize || pageNumber >= page.TotalPages) break;
                    pageNumber++;
                }

                foreach (string batchId in expired)
                {
                    await _Database.VesselImportBatches.DeleteAsync(tenant.Id, batchId, token).ConfigureAwait(false);
                    deleted++;
                }
            }

            return deleted;
        }

        #endregion

        #region Private-Methods

        private static DateTime FinishedUtc(Job job)
        {
            return job.CompletedUtc ?? job.LastUpdateUtc;
        }

        private static bool IsFinished(VesselImportBatch batch)
        {
            bool statusFinished = batch.Status == VesselImportBatchStatusEnum.Completed
                || batch.Status == VesselImportBatchStatusEnum.CompletedWithFailures
                || batch.Status == VesselImportBatchStatusEnum.Failed;
            bool categorizing = batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Pending
                || batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Running;
            return statusFinished && !categorizing;
        }

        #endregion
    }
}

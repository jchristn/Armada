namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
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
    /// Bulk vessel onboarding: persists discovery results as batches and imports selected candidates, inline or as a
    /// background job. Every vessel is created through <see cref="IVesselService"/> with RepoUrl set to the origin URL
    /// (or the local path when there is no origin), WorkingDirectory set to the discovered path, and LocalPath left
    /// unset, so removing the vessel can never delete the operator's checkout. Thread-safe; concurrent imports of the
    /// same batch are rejected.
    /// </summary>
    public class VesselImportService : IVesselImportService
    {
        #region Public-Members

        /// <summary>
        /// How many items a background import processes between progress updates and cancellation checks.
        /// Default 10, minimum 1, maximum 1000.
        /// </summary>
        public int ProgressInterval
        {
            get => _ProgressInterval;
            set => _ProgressInterval = Math.Clamp(value, 1, 1000);
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[VesselImportService] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly IVesselDiscoveryService _Discovery;
        private readonly IVesselService _Vessels;
        private readonly JobService _Jobs;
        private readonly LoggingModule _Logging;
        private readonly HashSet<string> _ActiveBatches = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _ActiveLock = new object();
        private int _ProgressInterval = 10;
        private static readonly JsonSerializerOptions _ResultJsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings; Import is read live.</param>
        /// <param name="discovery">Discovery engine.</param>
        /// <param name="vessels">Shared vessel creation service.</param>
        /// <param name="jobs">Job service for background imports.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public VesselImportService(
            DatabaseDriver database,
            ArmadaSettings settings,
            IVesselDiscoveryService discovery,
            IVesselService vessels,
            JobService jobs,
            LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _Vessels = vessels ?? throw new ArgumentNullException(nameof(vessels));
            _Jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<VesselImportDiscoverResponse> DiscoverAsync(string tenantId, string? userId, VesselDiscoveryRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (request == null) throw new ArgumentNullException(nameof(request));

            VesselDiscoveryResult discovered = await _Discovery.DiscoverAsync(tenantId, request, token).ConfigureAwait(false);

            VesselImportBatch batch = new VesselImportBatch();
            batch.TenantId = tenantId;
            batch.UserId = userId;
            batch.Status = VesselImportBatchStatusEnum.Discovered;
            batch.HarborId = request.HarborId;
            batch.RequestedPathCount = request.Directories.Count(p => !String.IsNullOrWhiteSpace(p)) + request.Roots.Count(p => !String.IsNullOrWhiteSpace(p));
            batch.CandidateCount = discovered.Candidates.Count;
            batch = await _Database.VesselImportBatches.CreateAsync(batch, token).ConfigureAwait(false);

            List<VesselImportItem> items = new List<VesselImportItem>();
            foreach (VesselImportCandidate candidate in discovered.Candidates)
            {
                VesselImportItem item = new VesselImportItem();
                item.TenantId = tenantId;
                item.BatchId = batch.Id;
                item.Path = candidate.Path;
                item.ProposedName = candidate.ProposedName;
                item.RemoteUrl = candidate.RemoteUrl;
                item.DefaultBranch = candidate.DefaultBranch;
                item.CandidateStatus = candidate.Status;
                item.ExistingVesselId = candidate.ExistingVesselId;
                item.Outcome = VesselImportOutcomeEnum.Pending;
                items.Add(item);
            }

            if (items.Count > 0) items = await _Database.VesselImportItems.CreateManyAsync(items, token).ConfigureAwait(false);
            _Logging.Info(_Header + "discovered " + items.Count + " candidates for batch " + batch.Id + (discovered.Truncated ? " (truncated)" : ""));

            VesselImportDiscoverResponse response = new VesselImportDiscoverResponse();
            response.BatchId = batch.Id;
            response.Batch = batch;
            response.Candidates = items;
            response.Truncated = discovered.Truncated;
            response.Hints = discovered.Hints;
            return response;
        }

        /// <inheritdoc />
        public async Task<VesselImportResponse> ImportAsync(string tenantId, string? userId, VesselImportRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.BatchId)) throw new ArgumentException("batchId is required.", nameof(request));
            List<string> requested = request.Paths.Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
            if (requested.Count == 0) throw new ArgumentException("paths is required and must not be empty.", nameof(request));

            VesselImportBatch? batch = await _Database.VesselImportBatches.ReadAsync(tenantId, request.BatchId, token).ConfigureAwait(false);
            if (batch == null) throw new KeyNotFoundException("Import batch not found: " + request.BatchId);

            if (!String.IsNullOrWhiteSpace(request.FleetId))
            {
                Fleet? fleet = await _Database.Fleets.ReadAsync(tenantId, request.FleetId, token).ConfigureAwait(false);
                if (fleet == null) throw new ArgumentException("Fleet not found: " + request.FleetId, nameof(request));
            }

            List<VesselImportItem> items = await _Database.VesselImportItems.EnumerateByBatchAsync(tenantId, batch.Id, token).ConfigureAwait(false);
            Dictionary<string, VesselImportItem> byPath = new Dictionary<string, VesselImportItem>(VesselImportPaths.PathComparer);
            foreach (VesselImportItem item in items) byPath.TryAdd(item.Path, item);

            HashSet<string> selected = new HashSet<string>(StringComparer.Ordinal);
            List<string> unknown = new List<string>();
            foreach (string path in requested)
            {
                if (byPath.TryGetValue(path, out VesselImportItem? match)) selected.Add(match.Id);
                else unknown.Add(path);
            }

            if (unknown.Count > 0)
            {
                throw new ArgumentException(
                    unknown.Count + " path(s) are not candidates in batch " + batch.Id + ": " + String.Join(", ", unknown.Take(5)) + (unknown.Count > 5 ? ", ..." : ""),
                    nameof(request));
            }

            lock (_ActiveLock)
            {
                if (!_ActiveBatches.Add(batch.Id)) throw new InvalidOperationException("Import batch " + batch.Id + " is already being imported.");
            }

            try
            {
                batch.Status = VesselImportBatchStatusEnum.Importing;
                batch.FleetId = String.IsNullOrWhiteSpace(request.FleetId) ? null : request.FleetId;
                batch.CompletedUtc = null;

                VesselImportResponse response = new VesselImportResponse();
                response.BatchId = batch.Id;

                if (selected.Count <= _Settings.Import.InlineBatchLimit)
                {
                    batch.JobId = null;
                    batch = await _Database.VesselImportBatches.UpdateAsync(batch, token).ConfigureAwait(false);
                    try
                    {
                        await ExecuteAsync(batch, items, selected, request, userId, null, token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        await MarkBatchFailedAsync(batch, ex).ConfigureAwait(false);
                        throw;
                    }

                    response.RunsInBackground = false;
                    response.Batch = batch;
                    response.Items = items;
                    return response;
                }

                Job job = await _Jobs.EnqueueAsync("Vessel import " + batch.Id + " (" + selected.Count + " vessels)", JobKindEnum.Generic, tenantId, userId, token).ConfigureAwait(false);
                batch.JobId = job.Id;
                batch = await _Database.VesselImportBatches.UpdateAsync(batch, token).ConfigureAwait(false);

                VesselImportBatch backgroundBatch = batch;
                _ = Task.Run(() => RunJobAsync(job, backgroundBatch, items, selected, request, userId));

                response.RunsInBackground = true;
                response.JobId = job.Id;
                response.Batch = batch;
                return response;
            }
            catch
            {
                ReleaseBatch(batch.Id);
                throw;
            }
            finally
            {
                if (batch.JobId == null) ReleaseBatch(batch.Id);
            }
        }

        /// <inheritdoc />
        public async Task<VesselImportBatchDetail?> ReadBatchAsync(string tenantId, string batchId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(batchId)) return null;

            VesselImportBatch? batch = await _Database.VesselImportBatches.ReadAsync(tenantId, batchId, token).ConfigureAwait(false);
            if (batch == null) return null;

            VesselImportBatchDetail detail = new VesselImportBatchDetail();
            detail.Batch = batch;
            detail.Items = await _Database.VesselImportItems.EnumerateByBatchAsync(tenantId, batchId, token).ConfigureAwait(false);
            return detail;
        }

        /// <inheritdoc />
        public Task<EnumerationResult<VesselImportBatch>> EnumerateBatchesAsync(string tenantId, EnumerationQuery? query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return _Database.VesselImportBatches.EnumerateAsync(tenantId, query ?? new EnumerationQuery(), token);
        }

        /// <inheritdoc />
        public Task<VesselBrowseResult> BrowseAsync(string? path, CancellationToken token = default)
        {
            return _Discovery.BrowseAsync(path, token);
        }

        #endregion

        #region Private-Methods

        private void ReleaseBatch(string batchId)
        {
            lock (_ActiveLock)
            {
                _ActiveBatches.Remove(batchId);
            }
        }

        private async Task RunJobAsync(Job job, VesselImportBatch batch, List<VesselImportItem> items, HashSet<string> selected, VesselImportRequest request, string? userId)
        {
            try
            {
                job.Status = JobStatusEnum.Running;
                job.StartedUtc = DateTime.UtcNow;
                job.LastUpdateUtc = DateTime.UtcNow;
                job = await _Database.Jobs.UpdateAsync(job).ConfigureAwait(false);

                bool completed = await ExecuteAsync(batch, items, selected, request, userId, job, CancellationToken.None).ConfigureAwait(false);

                Job? latest = await _Database.Jobs.ReadAsync(job.Id).ConfigureAwait(false);
                if (latest != null && latest.Status == JobStatusEnum.Running)
                {
                    latest.Status = completed ? JobStatusEnum.Succeeded : JobStatusEnum.Cancelled;
                    latest.Progress = 100;
                    latest.ResultJson = JsonSerializer.Serialize(new VesselImportJobSummary
                    {
                        BatchId = batch.Id,
                        CreatedCount = batch.CreatedCount,
                        SkippedCount = batch.SkippedCount,
                        FailedCount = batch.FailedCount
                    }, _ResultJsonOptions);
                    latest.CompletedUtc = DateTime.UtcNow;
                    latest.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Jobs.UpdateAsync(latest).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "background import of batch " + batch.Id + " failed: " + ex.Message);
                await MarkBatchFailedAsync(batch, ex).ConfigureAwait(false);
                try
                {
                    Job? latest = await _Database.Jobs.ReadAsync(job.Id).ConfigureAwait(false);
                    if (latest != null && (latest.Status == JobStatusEnum.Running || latest.Status == JobStatusEnum.Queued))
                    {
                        latest.Status = JobStatusEnum.Failed;
                        latest.ErrorReason = ex.Message;
                        latest.CompletedUtc = DateTime.UtcNow;
                        latest.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Jobs.UpdateAsync(latest).ConfigureAwait(false);
                    }
                }
                catch (Exception jobEx)
                {
                    _Logging.Warn(_Header + "could not mark job " + job.Id + " failed: " + jobEx.Message);
                }
            }
            finally
            {
                ReleaseBatch(batch.Id);
            }
        }

        private async Task MarkBatchFailedAsync(VesselImportBatch batch, Exception ex)
        {
            try
            {
                batch.Status = VesselImportBatchStatusEnum.Failed;
                batch.CompletedUtc = DateTime.UtcNow;
                await _Database.VesselImportBatches.UpdateAsync(batch).ConfigureAwait(false);
            }
            catch (Exception updateEx)
            {
                _Logging.Warn(_Header + "could not mark batch " + batch.Id + " failed after '" + ex.Message + "': " + updateEx.Message);
            }
        }

        /// <summary>
        /// Process every item of the batch. Returns false when a background job was cancelled part way.
        /// </summary>
        private async Task<bool> ExecuteAsync(
            VesselImportBatch batch,
            List<VesselImportItem> items,
            HashSet<string> selected,
            VesselImportRequest request,
            string? userId,
            Job? job,
            CancellationToken token)
        {
            string tenantId = batch.TenantId!;
            List<Vessel> vessels = await _Database.Vessels.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            VesselMatchIndex index = new VesselMatchIndex(vessels);

            int created = 0;
            int skipped = 0;
            int failed = 0;
            int processed = 0;
            bool cancelled = false;

            foreach (VesselImportItem item in items)
            {
                token.ThrowIfCancellationRequested();

                if (job != null && processed > 0 && processed % _ProgressInterval == 0)
                {
                    Job? latest = await _Database.Jobs.ReadAsync(job.Id, token).ConfigureAwait(false);
                    if (latest != null && latest.Status == JobStatusEnum.Cancelled) cancelled = true;
                    else if (latest != null)
                    {
                        latest.Progress = (int)Math.Round(processed * 100.0 / Math.Max(1, items.Count));
                        latest.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Jobs.UpdateAsync(latest, token).ConfigureAwait(false);
                    }
                }

                processed++;

                if (cancelled)
                {
                    if (selected.Contains(item.Id))
                    {
                        item.Outcome = VesselImportOutcomeEnum.Pending;
                        item.OutcomeReason = VesselImportCodes.Cancelled;
                        item.OutcomeMessage = "The import job was cancelled before this candidate was processed.";
                        await _Database.VesselImportItems.UpdateAsync(item, token).ConfigureAwait(false);
                    }

                    continue;
                }

                await ProcessItemAsync(item, selected.Contains(item.Id), request, tenantId, userId, index, token).ConfigureAwait(false);
                ArmadaMetrics.VesselImportItems.Add(1, new KeyValuePair<string, object?>("outcome", item.Outcome.ToString()));
                if (item.Outcome == VesselImportOutcomeEnum.Created) created++;
                else if (item.Outcome == VesselImportOutcomeEnum.Failed) failed++;
                else skipped++;

                await _Database.VesselImportItems.UpdateAsync(item, token).ConfigureAwait(false);
            }

            batch.CreatedCount = created;
            batch.SkippedCount = skipped;
            batch.FailedCount = failed;
            batch.Status = cancelled
                ? VesselImportBatchStatusEnum.Failed
                : (failed > 0 ? VesselImportBatchStatusEnum.CompletedWithFailures : VesselImportBatchStatusEnum.Completed);
            batch.CompletedUtc = DateTime.UtcNow;
            VesselImportBatch updated = await _Database.VesselImportBatches.UpdateAsync(batch, token).ConfigureAwait(false);
            batch.LastUpdateUtc = updated.LastUpdateUtc;

            _Logging.Info(_Header + "batch " + batch.Id + " imported: " + created + " created, " + skipped + " skipped, " + failed + " failed" + (cancelled ? " (cancelled)" : ""));
            return !cancelled;
        }

        private async Task ProcessItemAsync(
            VesselImportItem item,
            bool isSelected,
            VesselImportRequest request,
            string tenantId,
            string? userId,
            VesselMatchIndex index,
            CancellationToken token)
        {
            item.OutcomeMessage = null;
            bool isRepository = item.CandidateStatus == VesselImportCandidateStatusEnum.New
                || item.CandidateStatus == VesselImportCandidateStatusEnum.AlreadyOnboarded
                || item.CandidateStatus == VesselImportCandidateStatusEnum.Worktree;
            string? existing = isRepository ? index.Match(item.Path, item.RemoteUrl) : null;

            if (!isSelected)
            {
                if (existing != null)
                {
                    item.ExistingVesselId = existing;
                    item.Outcome = VesselImportOutcomeEnum.SkippedExisting;
                    item.OutcomeReason = VesselImportCodes.VesselAlreadyExists;
                }
                else
                {
                    item.Outcome = VesselImportOutcomeEnum.SkippedNotSelected;
                    item.OutcomeReason = VesselImportCodes.NotSelected;
                }

                return;
            }

            if (!isRepository)
            {
                item.Outcome = VesselImportOutcomeEnum.Failed;
                item.OutcomeReason = VesselImportCodes.NotImportable;
                item.OutcomeMessage = "Candidates with status " + item.CandidateStatus + " cannot be imported.";
                return;
            }

            if (existing != null)
            {
                item.ExistingVesselId = existing;
                item.Outcome = VesselImportOutcomeEnum.SkippedExisting;
                item.OutcomeReason = VesselImportCodes.VesselAlreadyExists;
                return;
            }

            if (!Directory.Exists(item.Path))
            {
                item.Outcome = VesselImportOutcomeEnum.Failed;
                item.OutcomeReason = VesselImportCodes.PathMissing;
                item.OutcomeMessage = "Directory no longer exists: " + item.Path;
                return;
            }

            try
            {
                Vessel vessel = new Vessel();
                vessel.TenantId = tenantId;
                vessel.UserId = userId;
                vessel.Name = index.IsNameTaken(item.ProposedName)
                    ? index.ReserveName(VesselMatchIndex.FolderName(item.Path))
                    : index.ReserveName(item.ProposedName);
                vessel.RepoUrl = String.IsNullOrWhiteSpace(item.RemoteUrl) ? item.Path : item.RemoteUrl;
                vessel.WorkingDirectory = item.Path;
                vessel.LocalPath = null;
                vessel.DefaultBranch = String.IsNullOrWhiteSpace(item.DefaultBranch) ? "main" : item.DefaultBranch!;
                vessel.FleetId = String.IsNullOrWhiteSpace(request.FleetId) ? null : request.FleetId;
                if (request.Defaults != null)
                {
                    vessel.DefaultPipelineId = String.IsNullOrWhiteSpace(request.Defaults.DefaultPipelineId) ? null : request.Defaults.DefaultPipelineId;
                    vessel.LandingMode = request.Defaults.LandingMode;
                }

                vessel = await _Vessels.CreateAsync(vessel, false, token).ConfigureAwait(false);
                index.Add(vessel);

                item.VesselId = vessel.Id;
                item.ExistingVesselId = null;
                item.Outcome = VesselImportOutcomeEnum.Created;
                item.OutcomeReason = null;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Warn(_Header + "creating a vessel for " + item.Path + " failed: " + ex.Message);
                item.Outcome = VesselImportOutcomeEnum.Failed;
                item.OutcomeReason = VesselImportCodes.CreateFailed;
                item.OutcomeMessage = ex.Message;
            }
        }

        #endregion
    }
}

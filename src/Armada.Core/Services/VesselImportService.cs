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

        /// <summary>
        /// How often a background discovery checks its job for cancellation and records a heartbeat, in
        /// milliseconds. Default 1000, minimum 50, maximum 60000.
        /// </summary>
        public int DiscoveryPollIntervalMs
        {
            get => _DiscoveryPollIntervalMs;
            set => _DiscoveryPollIntervalMs = Math.Clamp(value, 50, 60000);
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
        private readonly IFleetCategorizationService? _Categorization;
        private readonly HashSet<string> _ActiveBatches = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _ActiveLock = new object();
        private int _ProgressInterval = 10;
        private int _DiscoveryPollIntervalMs = 1000;
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
        /// <param name="categorization">Fleet categorization service, or null to reject categorization requests.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument other than categorization is null.</exception>
        public VesselImportService(
            DatabaseDriver database,
            ArmadaSettings settings,
            IVesselDiscoveryService discovery,
            IVesselService vessels,
            JobService jobs,
            LoggingModule logging,
            IFleetCategorizationService? categorization = null)
        {
            _Categorization = categorization;
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

            if (request.RunInBackground)
            {
                _Discovery.ValidateRequest(request);

                VesselImportBatch pending = NewBatch(tenantId, userId, request);
                pending.Status = VesselImportBatchStatusEnum.Discovering;
                pending = await _Database.VesselImportBatches.CreateAsync(pending, token).ConfigureAwait(false);

                Job job = await _Jobs.EnqueueAsync(
                    "Vessel discovery " + pending.Id + " (" + pending.RequestedPathCount + " paths)",
                    JobKindEnum.VesselDiscovery, tenantId, userId, token).ConfigureAwait(false);
                pending.DiscoveryJobId = job.Id;
                pending = await _Database.VesselImportBatches.UpdateAsync(pending, token).ConfigureAwait(false);

                VesselImportBatch backgroundBatch = pending;
                _ = Task.Run(() => RunDiscoveryJobAsync(job, backgroundBatch, request));

                VesselImportDiscoverResponse accepted = new VesselImportDiscoverResponse();
                accepted.BatchId = pending.Id;
                accepted.Batch = pending;
                accepted.JobId = job.Id;
                accepted.RunsInBackground = true;
                return accepted;
            }

            VesselDiscoveryResult discovered = await _Discovery.DiscoverAsync(tenantId, request, token).ConfigureAwait(false);

            VesselImportBatch batch = NewBatch(tenantId, userId, request);
            batch.Status = VesselImportBatchStatusEnum.Discovered;
            batch.CandidateCount = discovered.Candidates.Count;
            batch.Truncated = discovered.Truncated;
            batch = await _Database.VesselImportBatches.CreateAsync(batch, token).ConfigureAwait(false);

            List<VesselImportItem> items = await PersistCandidatesAsync(batch, discovered, token).ConfigureAwait(false);
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

            if (batch.Status == VesselImportBatchStatusEnum.Discovering)
                throw new InvalidOperationException("Import batch " + batch.Id + " is still discovering; import after discovery finishes.");
            if (batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Pending || batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Running)
                throw new InvalidOperationException("Import batch " + batch.Id + " has a fleet categorization in progress.");

            bool categorize = request.Categorization != null && request.Categorization.Enabled;
            if (categorize)
            {
                if (_Categorization == null) throw new ArgumentException("Fleet categorization is not available on this Admiral.", nameof(request));
                await _Categorization.ValidateRequestAsync(tenantId, request.Categorization, token).ConfigureAwait(false);
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
                batch.ErrorMessage = null;
                if (categorize) batch = await _Categorization!.ScheduleAsync(batch, request.Categorization!, token).ConfigureAwait(false);
                else if (batch.CategorizationStatus != VesselImportCategorizationStatusEnum.None)
                {
                    batch.CategorizationStatus = VesselImportCategorizationStatusEnum.None;
                    batch.CategorizationError = null;
                }

                VesselImportResponse response = new VesselImportResponse();
                response.BatchId = batch.Id;

                if (selected.Count <= _Settings.Import.InlineBatchLimit)
                {
                    batch.JobId = null;
                    batch = await _Database.VesselImportBatches.UpdateAsync(batch, token).ConfigureAwait(false);
                    try
                    {
                        await ExecuteAsync(batch, items, selected, request, userId, null, false, token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        await MarkBatchFailedAsync(batch, ex).ConfigureAwait(false);
                        throw;
                    }

                    if (categorize) batch = await StartCategorizationAsync(batch, userId).ConfigureAwait(false);

                    response.RunsInBackground = false;
                    response.Batch = batch;
                    response.Items = items;
                    return response;
                }

                Job job = await _Jobs.EnqueueAsync("Vessel import " + batch.Id + " (" + selected.Count + " vessels)", JobKindEnum.VesselImport, tenantId, userId, token).ConfigureAwait(false);
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
            detail.Hints = BuildHints(batch, detail.Items);
            detail.FleetRecommendations = await _Database.VesselImportFleetRecommendations.EnumerateByBatchAsync(tenantId, batchId, token).ConfigureAwait(false);
            return detail;
        }

        /// <inheritdoc />
        public Task<EnumerationResult<VesselImportBatch>> EnumerateBatchesAsync(string tenantId, EnumerationQuery? query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return _Database.VesselImportBatches.EnumerateAsync(tenantId, query ?? new EnumerationQuery(), token);
        }

        /// <inheritdoc />
        public async Task RecoverAsync(CancellationToken token = default)
        {
            List<VesselImportBatch> batches = await _Database.VesselImportBatches.EnumerateInProgressAsync(token).ConfigureAwait(false);
            foreach (VesselImportBatch batch in batches)
            {
                if (batch.Status == VesselImportBatchStatusEnum.Discovering)
                {
                    _Logging.Warn(_Header + "failing discovery of batch " + batch.Id + " orphaned by an Admiral restart");
                    batch.Status = VesselImportBatchStatusEnum.Failed;
                    batch.ErrorMessage = "The Admiral restarted while discovery was running. Discover again.";
                    batch.CompletedUtc = DateTime.UtcNow;
                    await _Database.VesselImportBatches.UpdateAsync(batch, token).ConfigureAwait(false);
                    if (!String.IsNullOrEmpty(batch.DiscoveryJobId)) await FailJobAsync(batch.DiscoveryJobId!, "Admiral restarted while the job was running").ConfigureAwait(false);
                }
                else if (batch.Status == VesselImportBatchStatusEnum.Importing)
                {
                    // A background import (large selection) was cut off by a restart. Vessels already created stay;
                    // importing the batch again is idempotent and finishes the rest.
                    _Logging.Warn(_Header + "failing import of batch " + batch.Id + " orphaned by an Admiral restart");
                    batch.Status = VesselImportBatchStatusEnum.Failed;
                    batch.ErrorMessage = "The Admiral restarted while the import was running. Vessels already created were kept; import again to finish.";
                    batch.CompletedUtc = DateTime.UtcNow;
                    if (batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Pending)
                    {
                        batch.CategorizationStatus = VesselImportCategorizationStatusEnum.Failed;
                        batch.CategorizationError = "The import was interrupted, so fleet categorization did not run.";
                        batch.CategorizationCompletedUtc = DateTime.UtcNow;
                    }
                    await _Database.VesselImportBatches.UpdateAsync(batch, token).ConfigureAwait(false);
                    if (!String.IsNullOrEmpty(batch.JobId)) await FailJobAsync(batch.JobId!, "Admiral restarted while the job was running").ConfigureAwait(false);
                }
            }

            if (_Categorization != null) await _Categorization.RecoverAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Rebuild the advisory discovery hints for a persisted batch: CandidateLimitReached when discovery was
        /// truncated, and PathNotVisibleToAdmiral when every candidate is NotFound.
        /// </summary>
        /// <param name="batch">Batch.</param>
        /// <param name="items">Batch items.</param>
        /// <returns>Hints. Never null.</returns>
        public static List<VesselImportHint> BuildHints(VesselImportBatch batch, List<VesselImportItem> items)
        {
            List<VesselImportHint> hints = new List<VesselImportHint>();
            if (batch == null || items == null) return hints;
            if (batch.Truncated)
            {
                hints.Add(new VesselImportHint(VesselImportCodes.CandidateLimitReached,
                    "Discovery stopped at the candidate limit. Narrow the roots or lower the depth to see the rest."));
            }

            if (items.Count > 0 && items.All(i => i.CandidateStatus == VesselImportCandidateStatusEnum.NotFound))
            {
                hints.Add(new VesselImportHint(VesselImportCodes.PathNotVisibleToAdmiral,
                    "None of the requested paths exist on the Admiral host. If the Admiral runs in a container, mount the directories into it or run discovery through a Harbor."));
            }

            return hints;
        }

        /// <inheritdoc />
        public Task<VesselBrowseResult> BrowseAsync(string? path, CancellationToken token = default)
        {
            return _Discovery.BrowseAsync(path, token);
        }

        #endregion

        #region Private-Methods

        private static VesselImportBatch NewBatch(string tenantId, string? userId, VesselDiscoveryRequest request)
        {
            VesselImportBatch batch = new VesselImportBatch();
            batch.TenantId = tenantId;
            batch.UserId = userId;
            batch.HarborId = request.HarborId;
            batch.RequestedPathCount = request.Directories.Count(p => !String.IsNullOrWhiteSpace(p)) + request.Roots.Count(p => !String.IsNullOrWhiteSpace(p));
            return batch;
        }

        private async Task<List<VesselImportItem>> PersistCandidatesAsync(VesselImportBatch batch, VesselDiscoveryResult discovered, CancellationToken token)
        {
            string tenantId = batch.TenantId!;
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
            return items;
        }

        private async Task<VesselImportBatch> StartCategorizationAsync(VesselImportBatch batch, string? userId)
        {
            try
            {
                return await _Categorization!.StartAsync(batch.TenantId!, batch.Id, userId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not start fleet categorization for batch " + batch.Id + ": " + ex.Message);
                VesselImportBatch? latest = await _Database.VesselImportBatches.ReadAsync(batch.TenantId!, batch.Id).ConfigureAwait(false);
                VesselImportBatch target = latest ?? batch;
                target.CategorizationStatus = VesselImportCategorizationStatusEnum.Failed;
                target.CategorizationError = ex.Message;
                target.CategorizationCompletedUtc = DateTime.UtcNow;
                return await _Database.VesselImportBatches.UpdateAsync(target).ConfigureAwait(false);
            }
        }

        private async Task RunDiscoveryJobAsync(Job job, VesselImportBatch batch, VesselDiscoveryRequest request)
        {
            string? resultJson = null;
            string? failureMessage = null;

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                Task? monitor = null;
                try
                {
                    // Conditional on Queued: a job cancelled before this worker started stays Cancelled.
                    if (!await _Jobs.TryStartAsync(job, 10).ConfigureAwait(false))
                    {
                        cts.Cancel();
                        throw new OperationCanceledException(cts.Token);
                    }

                    monitor = MonitorDiscoveryJobAsync(job.Id, cts);

                    VesselDiscoveryResult discovered = await _Discovery.DiscoverAsync(batch.TenantId!, request, cts.Token).ConfigureAwait(false);
                    cts.Token.ThrowIfCancellationRequested();

                    List<VesselImportItem> items = await PersistCandidatesAsync(batch, discovered, CancellationToken.None).ConfigureAwait(false);
                    batch.Status = VesselImportBatchStatusEnum.Discovered;
                    batch.CandidateCount = items.Count;
                    batch.Truncated = discovered.Truncated;
                    batch.ErrorMessage = null;
                    await _Database.VesselImportBatches.UpdateAsync(batch).ConfigureAwait(false);

                    resultJson = JsonSerializer.Serialize(new VesselDiscoveryJobSummary
                    {
                        BatchId = batch.Id,
                        CandidateCount = items.Count,
                        Truncated = discovered.Truncated
                    }, _ResultJsonOptions);

                    _Logging.Info(_Header + "background discovery found " + items.Count + " candidates for batch " + batch.Id);
                }
                catch (Exception ex)
                {
                    bool cancelled = cts.IsCancellationRequested && ex is OperationCanceledException;
                    string message = cancelled ? "Discovery was cancelled." : ex.Message;
                    _Logging.Warn(_Header + "background discovery of batch " + batch.Id + " failed: " + message);
                    try
                    {
                        batch.Status = VesselImportBatchStatusEnum.Failed;
                        batch.ErrorMessage = message;
                        batch.CompletedUtc = DateTime.UtcNow;
                        await _Database.VesselImportBatches.UpdateAsync(batch).ConfigureAwait(false);
                    }
                    catch (Exception updateEx)
                    {
                        _Logging.Warn(_Header + "could not mark batch " + batch.Id + " failed: " + updateEx.Message);
                    }

                    failureMessage = message;
                }
                finally
                {
                    // Stop the heartbeat before writing the job's terminal state. Both are conditional on the stored
                    // status, so neither can undo a cancel, and the first terminal status wins.
                    cts.Cancel();
                    if (monitor != null)
                    {
                        try { await monitor.ConfigureAwait(false); }
                        catch (Exception) { }
                    }

                    if (resultJson != null) await CompleteDiscoveryJobAsync(job.Id, resultJson).ConfigureAwait(false);
                    else if (failureMessage != null) await FailJobAsync(job.Id, failureMessage).ConfigureAwait(false);
                }
            }
        }

        private async Task CompleteDiscoveryJobAsync(string jobId, string resultJson)
        {
            try
            {
                await _Jobs.TryFinishAsync(jobId, JobStatusEnum.Succeeded, resultJson, null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not mark discovery job " + jobId + " succeeded: " + ex.Message);
            }
        }

        private async Task MonitorDiscoveryJobAsync(string jobId, CancellationTokenSource cts)
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_DiscoveryPollIntervalMs, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    // The heartbeat never changes the job's status; false means it is no longer Running (cancelled).
                    if (!await _Jobs.HeartbeatAsync(jobId).ConfigureAwait(false))
                    {
                        cts.Cancel();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "heartbeat for discovery job " + jobId + " failed: " + ex.Message);
                }
            }
        }

        private async Task FailJobAsync(string jobId, string message)
        {
            try
            {
                await _Jobs.TryFinishAsync(jobId, JobStatusEnum.Failed, null, message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not mark job " + jobId + " failed: " + ex.Message);
            }
        }

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
                // Conditional on Queued: a job cancelled before this worker started stays Cancelled, and every
                // selected item is recorded as cancelled instead of imported.
                bool started = await _Jobs.TryStartAsync(job).ConfigureAwait(false);

                bool completed = await ExecuteAsync(batch, items, selected, request, userId, job, !started, CancellationToken.None).ConfigureAwait(false);

                // First terminal status wins: a job cancelled while the import ran stays Cancelled.
                string summary = JsonSerializer.Serialize(new VesselImportJobSummary
                {
                    BatchId = batch.Id,
                    CreatedCount = batch.CreatedCount,
                    SkippedCount = batch.SkippedCount,
                    FailedCount = batch.FailedCount
                }, _ResultJsonOptions);
                await _Jobs.TryFinishAsync(job.Id, completed ? JobStatusEnum.Succeeded : JobStatusEnum.Cancelled, summary, null).ConfigureAwait(false);

                if (completed && request.Categorization != null && request.Categorization.Enabled)
                {
                    ReleaseBatch(batch.Id);
                    await StartCategorizationAsync(batch, userId).ConfigureAwait(false);
                }
                else if (!completed && batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Pending)
                {
                    batch.CategorizationStatus = VesselImportCategorizationStatusEnum.Failed;
                    batch.CategorizationError = "The import was cancelled, so fleet categorization did not run.";
                    batch.CategorizationCompletedUtc = DateTime.UtcNow;
                    await _Database.VesselImportBatches.UpdateAsync(batch).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "background import of batch " + batch.Id + " failed: " + ex.Message);
                await MarkBatchFailedAsync(batch, ex).ConfigureAwait(false);
                try
                {
                    await _Jobs.TryFinishAsync(job.Id, JobStatusEnum.Failed, null, ex.Message).ConfigureAwait(false);
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
                batch.ErrorMessage = ex.Message;
                batch.CompletedUtc = DateTime.UtcNow;
                if (batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Pending)
                {
                    batch.CategorizationStatus = VesselImportCategorizationStatusEnum.Failed;
                    batch.CategorizationError = "The import failed, so fleet categorization did not run.";
                    batch.CategorizationCompletedUtc = DateTime.UtcNow;
                }

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
            bool cancelledBeforeStart,
            CancellationToken token)
        {
            string tenantId = batch.TenantId!;
            List<Vessel> vessels = await _Database.Vessels.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            VesselMatchIndex index = new VesselMatchIndex(vessels);

            int created = 0;
            int skipped = 0;
            int failed = 0;
            int processed = 0;
            bool cancelled = cancelledBeforeStart;

            foreach (VesselImportItem item in items)
            {
                token.ThrowIfCancellationRequested();

                if (job != null && !cancelled && processed > 0 && processed % _ProgressInterval == 0)
                {
                    // The progress heartbeat never changes the job's status; false means the job is no longer Running
                    // (cancelled), so the remaining items are not imported.
                    int progress = (int)Math.Round(processed * 100.0 / Math.Max(1, items.Count));
                    if (!await _Jobs.HeartbeatAsync(job.Id, progress, token).ConfigureAwait(false)) cancelled = true;
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
            item.Selected = isSelected;
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

namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Entry point for vessel health: enumeration, summary, per-vessel detail, overrides, and evaluation jobs. An
    /// evaluation runs as a background <see cref="Job"/> of kind Report, evaluating up to RepositoryHealth.MaxConcurrency
    /// vessels at a time with per-vessel failures isolated. At most one evaluation job runs per tenant; a second request
    /// reports the running job instead of starting another. Cancelling the job (POST /api/v1/jobs/{id}/cancel) stops the
    /// remaining vessels. Thread-safe.
    /// </summary>
    public class VesselHealthService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Name given to every vessel health evaluation job.
        /// </summary>
        public const string JobName = "Vessel health evaluation";

        /// <summary>
        /// Clock used for scheduling decisions. Defaults to DateTime.UtcNow; replaceable in tests.
        /// </summary>
        public Func<DateTime> Clock
        {
            get => _Clock;
            set => _Clock = value ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// How often a running job checks whether it was cancelled through the jobs API. Default 5 seconds, minimum
        /// 100 milliseconds.
        /// </summary>
        public TimeSpan CancellationPollInterval
        {
            get => _CancellationPollInterval;
            set => _CancellationPollInterval = value < TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : value;
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[VesselHealthService] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly VesselHealthEvaluator _Evaluator;
        private readonly JobService _Jobs;
        private readonly LoggingModule _Logging;
        private readonly SemaphoreSlim _StartLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, VesselHealthRunningEvaluation> _Running = new ConcurrentDictionary<string, VesselHealthRunningEvaluation>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, DateTime> _LastScheduledUtc = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly CancellationTokenSource _Shutdown = new CancellationTokenSource();
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private TimeSpan _CancellationPollInterval = TimeSpan.FromSeconds(5);
        private bool _Disposed = false;

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Live settings.</param>
        /// <param name="evaluator">Vessel health evaluator.</param>
        /// <param name="jobs">Job service.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public VesselHealthService(DatabaseDriver database, ArmadaSettings settings, VesselHealthEvaluator evaluator, JobService jobs, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            _Jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enumerate vessels with their health in a tenant (filtered, sorted, and paged in SQL).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">Filter, sort, and paging request (null uses defaults).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId is null or empty.</exception>
        public async Task<EnumerationResult<VesselHealth>> EnumerateAsync(string tenantId, VesselHealthEnumerateRequest? request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            Stopwatch watch = Stopwatch.StartNew();
            EnumerationResult<VesselHealth> result = await _Database.VesselHealth.EnumerateAsync(tenantId, request ?? new VesselHealthEnumerateRequest(), token).ConfigureAwait(false);
            result.TotalMs = watch.Elapsed.TotalMilliseconds;
            return result;
        }

        /// <summary>
        /// KPI counts for a tenant, based on effective (override-aware) statuses.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The summary.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId is null or empty.</exception>
        public async Task<VesselHealthSummary> GetSummaryAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await _Database.VesselHealth.CountByOverallStatusAsync(tenantId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a vessel's full health (row, findings, dependencies, overrides).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The detail, or null when the vessel does not exist in the tenant.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or vesselId is null or empty.</exception>
        public async Task<VesselHealthDetail?> GetDetailAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            Vessel? vessel = await _Database.Vessels.ReadAsync(tenantId, vesselId, token).ConfigureAwait(false);
            if (vessel == null) return null;

            VesselHealthDetail detail = new VesselHealthDetail();
            VesselHealth? health = await _Database.VesselHealth.ReadByVesselAsync(tenantId, vesselId, token).ConfigureAwait(false);
            if (health == null)
            {
                health = new VesselHealth();
                health.Id = null;
                health.TenantId = tenantId;
                health.VesselId = vessel.Id;
                health.VesselName = vessel.Name;
                health.FleetId = vessel.FleetId;
                if (!String.IsNullOrEmpty(vessel.FleetId))
                {
                    Fleet? fleet = await _Database.Fleets.ReadAsync(tenantId, vessel.FleetId, token).ConfigureAwait(false);
                    health.FleetName = fleet?.Name;
                }
            }

            detail.Health = health;
            detail.Findings = await _Database.VesselHealthFindings.ReadByVesselAsync(tenantId, vesselId, token).ConfigureAwait(false);
            detail.Dependencies = await _Database.VesselDependencies.ReadByVesselAsync(tenantId, vesselId, token).ConfigureAwait(false);
            detail.Overrides = await _Database.VesselHealthOverrides.ReadByVesselAsync(tenantId, vesselId, token).ConfigureAwait(false);
            return detail;
        }

        /// <summary>
        /// Set (insert or replace) a manual override and immediately recompute the vessel's effective status columns
        /// from its stored findings (no re-evaluation). A vessel never evaluated keeps no health row; the override
        /// applies at its first evaluation.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="criterion">Criterion to override (Overall overrides the rollup).</param>
        /// <param name="status">Overriding status.</param>
        /// <param name="note">Operator note, or null.</param>
        /// <param name="userId">User setting the override, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated detail, or null when the vessel does not exist in the tenant.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or vesselId is null or empty.</exception>
        public async Task<VesselHealthDetail?> SetOverrideAsync(
            string tenantId,
            string vesselId,
            VesselHealthCriterionEnum criterion,
            VesselHealthStatusEnum status,
            string? note,
            string? userId,
            CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            if (!await _Database.Vessels.ExistsAsync(tenantId, vesselId, token).ConfigureAwait(false)) return null;

            VesselHealthOverride item = new VesselHealthOverride();
            item.TenantId = tenantId;
            item.VesselId = vesselId;
            item.UserId = userId;
            item.Criterion = criterion;
            item.Status = status;
            item.Note = note;
            await _Database.VesselHealthOverrides.UpsertAsync(item, token).ConfigureAwait(false);
            await RecomputeEffectiveAsync(tenantId, vesselId, token).ConfigureAwait(false);
            return await GetDetailAsync(tenantId, vesselId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove a manual override and immediately recompute the vessel's effective status columns.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="criterion">Criterion whose override to remove.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated detail, or null when the vessel does not exist in the tenant.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or vesselId is null or empty.</exception>
        public async Task<VesselHealthDetail?> DeleteOverrideAsync(string tenantId, string vesselId, VesselHealthCriterionEnum criterion, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            if (!await _Database.Vessels.ExistsAsync(tenantId, vesselId, token).ConfigureAwait(false)) return null;

            await _Database.VesselHealthOverrides.DeleteAsync(tenantId, vesselId, criterion, token).ConfigureAwait(false);
            await RecomputeEffectiveAsync(tenantId, vesselId, token).ConfigureAwait(false);
            return await GetDetailAsync(tenantId, vesselId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Recompute a vessel's effective status columns and overall status from its stored findings and overrides,
        /// using the live ScoredCriteria. Does nothing when the vessel has no health row.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated row, or null when the vessel has no health row.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or vesselId is null or empty.</exception>
        public async Task<VesselHealth?> RecomputeEffectiveAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            VesselHealth? health = await _Database.VesselHealth.ReadByVesselAsync(tenantId, vesselId, token).ConfigureAwait(false);
            if (health == null) return null;
            List<VesselHealthFinding> findings = await _Database.VesselHealthFindings.ReadByVesselAsync(tenantId, vesselId, token).ConfigureAwait(false);
            List<VesselHealthOverride> overrides = await _Database.VesselHealthOverrides.ReadByVesselAsync(tenantId, vesselId, token).ConfigureAwait(false);
            VesselHealthRollup.ApplyEffectiveStatuses(health, findings, overrides, _Settings.RepositoryHealth.ScoredCriteria);
            return await _Database.VesselHealth.UpsertAsync(health, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Get the identifier of the evaluation job running for a tenant in this process, or null.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <returns>The job identifier, or null.</returns>
        public string? GetRunningJobId(string tenantId)
        {
            if (String.IsNullOrEmpty(tenantId)) return null;
            return _Running.TryGetValue(tenantId, out VesselHealthRunningEvaluation? running) ? running.JobId : null;
        }

        /// <summary>
        /// Wait until the evaluation job running for a tenant (if any) finishes.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when no job is running for the tenant.</returns>
        public async Task WaitForIdleAsync(string tenantId, CancellationToken token = default)
        {
            while (!String.IsNullOrEmpty(tenantId) && _Running.TryGetValue(tenantId, out VesselHealthRunningEvaluation? running))
            {
                try
                {
                    await running.Completion.WaitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                }

                await Task.Yield();
                if (_Running.TryGetValue(tenantId, out VesselHealthRunningEvaluation? next) && ReferenceEquals(next, running))
                    await Task.Delay(10, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Start an evaluation job for a tenant, unless one is already running (then the running job is reported with
        /// AlreadyRunning = true and nothing is started).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User starting the job, or null.</param>
        /// <param name="request">Which vessels to evaluate and whether to force dependency checks (null means all
        /// active vessels).</param>
        /// <param name="scheduled">True when started by the scheduler (never forces dependency checks).</param>
        /// <param name="token">Cancellation token for the start request (the job itself runs in the background).</param>
        /// <returns>The start outcome.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId is null or empty.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when a requested vessel or fleet does not exist in the tenant.</exception>
        /// <exception cref="ObjectDisposedException">Thrown after the service is disposed.</exception>
        public async Task<VesselHealthEvaluationStart> StartEvaluationAsync(
            string tenantId,
            string? userId,
            VesselHealthEvaluateRequest? request,
            bool scheduled,
            CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (_Disposed) throw new ObjectDisposedException(nameof(VesselHealthService));
            request = request ?? new VesselHealthEvaluateRequest();
            bool force = !scheduled && (request.Force ?? true);

            await _StartLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Running.TryGetValue(tenantId, out VesselHealthRunningEvaluation? existing))
                    return new VesselHealthEvaluationStart { JobId = existing.JobId, AlreadyRunning = true };

                await FailOrphanedJobsAsync(tenantId, token).ConfigureAwait(false);

                List<Vessel> vessels = await ResolveVesselsAsync(tenantId, request, token).ConfigureAwait(false);
                Job job = await _Jobs.EnqueueAsync(JobName, JobKindEnum.Report, tenantId, userId, token).ConfigureAwait(false);

                VesselHealthRunningEvaluation running = new VesselHealthRunningEvaluation();
                running.TenantId = tenantId;
                running.JobId = job.Id;
                running.StartedUtc = _Clock();
                running.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_Shutdown.Token);
                _Running[tenantId] = running;
                running.Completion = Task.Run(() => RunJobAsync(running, job, vessels, force, scheduled));

                _Logging.Info(_Header + "started job " + job.Id + " for tenant " + tenantId + " (" + vessels.Count + " vessels, force " + force + ", scheduled " + scheduled + ")");
                return new VesselHealthEvaluationStart { JobId = job.Id, AlreadyRunning = false, VesselCount = vessels.Count };
            }
            finally
            {
                _StartLock.Release();
            }
        }

        /// <summary>
        /// Scheduler step, called from the Admiral health loop. When RepositoryHealth.IntervalMinutes is above 0, starts
        /// an evaluation of every active vessel for each tenant whose last evaluation job is at least that old (the last
        /// job time is read from the jobs table, so a restart does not re-trigger an evaluation). Tenants with a running
        /// job or without active vessels are skipped.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of jobs started.</returns>
        public async Task<int> RunScheduleAsync(CancellationToken token = default)
        {
            int interval = _Settings.RepositoryHealth.IntervalMinutes;
            if (interval <= 0 || _Disposed) return 0;

            DateTime now = _Clock();
            int started = 0;
            List<TenantMetadata> tenants = await _Database.Tenants.EnumerateAsync(token).ConfigureAwait(false);
            foreach (TenantMetadata tenant in tenants)
            {
                token.ThrowIfCancellationRequested();
                if (String.IsNullOrEmpty(tenant.Id)) continue;
                if (_Running.ContainsKey(tenant.Id)) continue;

                DateTime? last = await GetLastScheduledAsync(tenant.Id, token).ConfigureAwait(false);
                if (last.HasValue && now - last.Value < TimeSpan.FromMinutes(interval)) continue;

                List<Vessel> vessels = await _Database.Vessels.EnumerateAsync(tenant.Id, token).ConfigureAwait(false);
                if (!vessels.Any(v => v.Active))
                {
                    _LastScheduledUtc[tenant.Id] = now;
                    continue;
                }

                VesselHealthEvaluationStart start = await StartEvaluationAsync(tenant.Id, null, null, true, token).ConfigureAwait(false);
                _LastScheduledUtc[tenant.Id] = now;
                if (!start.AlreadyRunning) started++;
            }

            return started;
        }

        /// <summary>
        /// Cancel running jobs and release resources.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            try { _Shutdown.Cancel(); } catch (ObjectDisposedException) { }
        }

        #endregion

        #region Private-Methods

        private async Task<DateTime?> GetLastScheduledAsync(string tenantId, CancellationToken token)
        {
            if (_LastScheduledUtc.TryGetValue(tenantId, out DateTime cached)) return cached;
            JobQuery query = new JobQuery();
            query.TenantId = tenantId;
            query.Kind = JobKindEnum.Report;
            query.PageSize = 200;
            List<Job> jobs = (await _Database.Jobs.EnumeratePageAsync(query, token).ConfigureAwait(false)).Objects;
            Job? latest = jobs
                .Where(j => j.Kind == JobKindEnum.Report && String.Equals(j.Name, JobName, StringComparison.Ordinal))
                .OrderByDescending(j => j.CreatedUtc)
                .FirstOrDefault();
            if (latest == null) return null;
            _LastScheduledUtc[tenantId] = latest.CreatedUtc;
            return latest.CreatedUtc;
        }

        private async Task FailOrphanedJobsAsync(string tenantId, CancellationToken token)
        {
            JobQuery query = new JobQuery();
            query.TenantId = tenantId;
            query.Kind = JobKindEnum.Report;
            query.Statuses.Add(JobStatusEnum.Queued);
            query.Statuses.Add(JobStatusEnum.Running);
            query.PageSize = 1000;
            List<Job> jobs = (await _Database.Jobs.EnumeratePageAsync(query, token).ConfigureAwait(false)).Objects;
            foreach (Job job in jobs)
            {
                if (job.Kind != JobKindEnum.Report || !String.Equals(job.Name, JobName, StringComparison.Ordinal)) continue;
                if (job.Status != JobStatusEnum.Queued && job.Status != JobStatusEnum.Running) continue;
                job.Status = JobStatusEnum.Failed;
                job.ErrorReason = "evaluation was interrupted (the Admiral restarted before it finished)";
                job.CompletedUtc = DateTime.UtcNow;
                job.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Jobs.UpdateAsync(job, token).ConfigureAwait(false);
                _Logging.Warn(_Header + "failed orphaned evaluation job " + job.Id);
            }
        }

        private async Task<List<Vessel>> ResolveVesselsAsync(string tenantId, VesselHealthEvaluateRequest request, CancellationToken token)
        {
            List<string> ids = (request.VesselIds ?? new List<string>())
                .Where(id => !String.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (ids.Count > 0)
            {
                List<Vessel> selected = new List<Vessel>();
                foreach (string id in ids)
                {
                    Vessel? vessel = await _Database.Vessels.ReadAsync(tenantId, id, token).ConfigureAwait(false);
                    if (vessel == null) throw new KeyNotFoundException("Vessel " + id + " was not found.");
                    selected.Add(vessel);
                }

                return selected;
            }

            if (!String.IsNullOrWhiteSpace(request.FleetId))
            {
                Fleet? fleet = await _Database.Fleets.ReadAsync(tenantId, request.FleetId.Trim(), token).ConfigureAwait(false);
                if (fleet == null) throw new KeyNotFoundException("Fleet " + request.FleetId + " was not found.");
                List<Vessel> fleetVessels = await _Database.Vessels.EnumerateByFleetAsync(tenantId, fleet.Id, token).ConfigureAwait(false);
                return fleetVessels.Where(v => v.Active).ToList();
            }

            List<Vessel> all = await _Database.Vessels.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            return all.Where(v => v.Active).ToList();
        }

        private async Task RunJobAsync(VesselHealthRunningEvaluation running, Job job, List<Vessel> vessels, bool force, bool scheduled)
        {
            CancellationToken token = running.Cancellation.Token;
            using Activity? root = VesselHealthTelemetry.StartSpan("vessel_health.job");
            root?.SetTag("armada.job_id", job.Id);
            root?.SetTag("armada.health.vessel_count", vessels.Count);

            VesselHealthJobResult summary = new VesselHealthJobResult { Requested = vessels.Count, Force = force, Scheduled = scheduled };
            SemaphoreSlim jobUpdateLock = new SemaphoreSlim(1, 1);
            Task monitor = Task.CompletedTask;
            CancellationTokenSource monitorStop = new CancellationTokenSource();
            int evaluated = 0;
            int failed = 0;
            int done = 0;

            try
            {
                job.Status = JobStatusEnum.Running;
                job.StartedUtc = DateTime.UtcNow;
                job.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Jobs.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);

                monitor = MonitorCancellationAsync(running, monitorStop.Token);

                int concurrency = _Settings.RepositoryHealth.MaxConcurrency;
                using (SemaphoreSlim gate = new SemaphoreSlim(concurrency, concurrency))
                {
                    List<Task> tasks = new List<Task>();
                    foreach (Vessel vessel in vessels)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                await gate.WaitAsync(token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                VesselHealthTelemetry.RecordEvaluation("Cancelled");
                                return;
                            }

                            try
                            {
                                await _Evaluator.EvaluateAsync(vessel, force, token).ConfigureAwait(false);
                                Interlocked.Increment(ref evaluated);
                                VesselHealthTelemetry.RecordEvaluation("Succeeded");
                            }
                            catch (OperationCanceledException) when (token.IsCancellationRequested)
                            {
                                VesselHealthTelemetry.RecordEvaluation("Cancelled");
                                return;
                            }
                            catch (Exception ex)
                            {
                                Interlocked.Increment(ref failed);
                                VesselHealthTelemetry.RecordEvaluation("Failed");
                                _Logging.Warn(_Header + "evaluation of vessel " + vessel.Id + " failed in job " + job.Id + ": " + ex.Message);
                            }
                            finally
                            {
                                gate.Release();
                            }

                            int completed = Interlocked.Increment(ref done);
                            await ReportProgressAsync(job, jobUpdateLock, completed, vessels.Count).ConfigureAwait(false);
                        }));
                    }

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }

                summary.Evaluated = evaluated;
                summary.Failed = failed;
                await FinishJobAsync(job, jobUpdateLock, summary, token.IsCancellationRequested).ConfigureAwait(false);
                root?.SetTag("armada.health.evaluated", evaluated);
                root?.SetTag("armada.health.failed", failed);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "evaluation job " + job.Id + " failed: " + ex.Message);
                try
                {
                    Job? current = await _Database.Jobs.ReadAsync(job.Id, CancellationToken.None).ConfigureAwait(false);
                    if (current != null && current.Status != JobStatusEnum.Cancelled)
                    {
                        current.Status = JobStatusEnum.Failed;
                        current.ErrorReason = ex.Message;
                        current.CompletedUtc = DateTime.UtcNow;
                        current.LastUpdateUtc = DateTime.UtcNow;
                        await _Database.Jobs.UpdateAsync(current, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception updateEx)
                {
                    _Logging.Warn(_Header + "could not record failure of job " + job.Id + ": " + updateEx.Message);
                }
            }
            finally
            {
                try { monitorStop.Cancel(); } catch (ObjectDisposedException) { }
                try { await monitor.ConfigureAwait(false); } catch (OperationCanceledException) { }
                monitorStop.Dispose();
                jobUpdateLock.Dispose();
                _Running.TryRemove(new KeyValuePair<string, VesselHealthRunningEvaluation>(running.TenantId, running));
                running.Cancellation.Dispose();
            }
        }

        private async Task MonitorCancellationAsync(VesselHealthRunningEvaluation running, CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_CancellationPollInterval, stop).ConfigureAwait(false);
                    Job? current = await _Database.Jobs.ReadAsync(running.JobId, stop).ConfigureAwait(false);
                    if (current != null && current.Status == JobStatusEnum.Cancelled)
                    {
                        _Logging.Info(_Header + "job " + running.JobId + " was cancelled; stopping remaining vessels");
                        running.Cancellation.Cancel();
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _Logging.Debug(_Header + "cancellation poll error for job " + running.JobId + ": " + ex.Message);
                }
            }
        }

        private async Task ReportProgressAsync(Job job, SemaphoreSlim jobUpdateLock, int completed, int total)
        {
            try
            {
                await jobUpdateLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    Job? current = await _Database.Jobs.ReadAsync(job.Id, CancellationToken.None).ConfigureAwait(false);
                    if (current == null || current.Status != JobStatusEnum.Running) return;
                    current.Progress = total == 0 ? 100 : (int)Math.Floor(completed * 100.0 / total);
                    current.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Jobs.UpdateAsync(current, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    jobUpdateLock.Release();
                }
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "progress update failed for job " + job.Id + ": " + ex.Message);
            }
        }

        private async Task FinishJobAsync(Job job, SemaphoreSlim jobUpdateLock, VesselHealthJobResult summary, bool cancelled)
        {
            await jobUpdateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Job current = await _Database.Jobs.ReadAsync(job.Id, CancellationToken.None).ConfigureAwait(false) ?? job;
                current.ResultJson = JsonSerializer.Serialize(summary, _JsonOptions);
                current.LastUpdateUtc = DateTime.UtcNow;
                if (current.Status != JobStatusEnum.Cancelled)
                {
                    if (cancelled)
                    {
                        current.Status = JobStatusEnum.Cancelled;
                    }
                    else
                    {
                        current.Status = JobStatusEnum.Succeeded;
                        current.Progress = 100;
                    }

                    current.CompletedUtc = DateTime.UtcNow;
                }

                await _Database.Jobs.UpdateAsync(current, CancellationToken.None).ConfigureAwait(false);
                _Logging.Info(_Header + "job " + job.Id + " finished: " + summary.Evaluated + " evaluated, " + summary.Failed + " failed of " + summary.Requested);
            }
            finally
            {
                jobUpdateLock.Release();
            }
        }

        #endregion
    }
}

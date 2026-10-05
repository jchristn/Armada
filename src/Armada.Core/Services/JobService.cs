namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Manages request-independent background jobs: enqueue, cancel, and a maintenance pass (invoked from
    /// the Admiral health loop) that reaps jobs left Running by a worker that died so they do not hang in a
    /// non-terminal state forever. Job execution handlers register with a coordinator layer above this;
    /// this service owns the entity lifecycle and the stale-Running safety net.
    /// </summary>
    public class JobService
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[JobService] ";
        private int _StaleRunningMinutes = 30;
        private const int _MaxTransitionAttempts = 4;

        #endregion

        #region Public-Members

        /// <summary>
        /// Minutes a job may stay Running without a heartbeat/update before the maintenance pass fails it as
        /// a dead worker. Clamped to a minimum of 1.
        /// </summary>
        public int StaleRunningMinutes
        {
            get => _StaleRunningMinutes;
            set => _StaleRunningMinutes = value < 1 ? 1 : value;
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        public JobService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enqueue a new background job in the Queued state.
        /// </summary>
        /// <param name="name">Job name.</param>
        /// <param name="kind">Job kind.</param>
        /// <param name="tenantId">Owning tenant, or null.</param>
        /// <param name="userId">Owning user, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created job.</returns>
        public async Task<Job> EnqueueAsync(string name, JobKindEnum kind, string? tenantId, string? userId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

            Job job = new Job(name, kind)
            {
                TenantId = tenantId,
                UserId = userId,
                Status = JobStatusEnum.Queued,
                CreatedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow,
            };
            job = await _Database.Jobs.CreateAsync(job, token).ConfigureAwait(false);
            _Logging.Info(_Header + "enqueued job " + job.Id + " (" + kind + "): " + name);
            return job;
        }

        /// <summary>
        /// Cancel a job. A terminal job cannot be cancelled. The transition is conditional on the stored status, so a
        /// job that finished between the caller's read and this call is not overwritten (first terminal status wins),
        /// and a worker heartbeat can never undo the cancellation.
        /// </summary>
        /// <param name="job">The job to cancel.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated job.</returns>
        /// <exception cref="InvalidOperationException">The job is already in a terminal status.</exception>
        /// <exception cref="KeyNotFoundException">The job no longer exists.</exception>
        public async Task<Job> CancelAsync(Job job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (!JobStateMachine.CanTransition(job.Status, JobStatusEnum.Cancelled))
                throw new InvalidOperationException("Job " + job.Id + " cannot be cancelled from status " + job.Status + ".");

            for (int attempt = 0; attempt < _MaxTransitionAttempts; attempt++)
            {
                Job? current = await _Database.Jobs.ReadAsync(job.Id, token).ConfigureAwait(false);
                if (current == null) throw new KeyNotFoundException("Job " + job.Id + " was not found.");
                if (!JobStateMachine.CanTransition(current.Status, JobStatusEnum.Cancelled))
                    throw new InvalidOperationException("Job " + job.Id + " cannot be cancelled from status " + current.Status + ".");

                JobStatusEnum from = current.Status;
                current.Status = JobStatusEnum.Cancelled;
                current.CompletedUtc = DateTime.UtcNow;
                current.LastUpdateUtc = DateTime.UtcNow;
                if (await _Database.Jobs.TryUpdateIfStatusAsync(current, new JobStatusEnum[] { from }, token).ConfigureAwait(false))
                {
                    job.Status = current.Status;
                    job.CompletedUtc = current.CompletedUtc;
                    job.LastUpdateUtc = current.LastUpdateUtc;
                    _Logging.Info(_Header + "cancelled job " + current.Id);
                    return current;
                }
            }

            throw new InvalidOperationException("Job " + job.Id + " changed status concurrently and could not be cancelled.");
        }

        /// <summary>
        /// Move a Queued job to Running. Does nothing when the job is no longer Queued (for example it was cancelled
        /// before its worker started); the worker must then not run.
        /// </summary>
        /// <param name="job">The job; on success its status, start time, progress, and last-update time are updated.</param>
        /// <param name="progress">Initial progress (0 to 100).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the job is now Running for this worker; false when it was not Queued.</returns>
        public async Task<bool> TryStartAsync(Job job, int progress = 0, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            Job? current = await _Database.Jobs.ReadAsync(job.Id, token).ConfigureAwait(false);
            if (current == null || current.Status != JobStatusEnum.Queued) return false;

            current.Status = JobStatusEnum.Running;
            current.StartedUtc = DateTime.UtcNow;
            current.LastUpdateUtc = current.StartedUtc.Value;
            current.Progress = Math.Clamp(progress, 0, 100);
            if (!await _Database.Jobs.TryUpdateIfStatusAsync(current, new JobStatusEnum[] { JobStatusEnum.Queued }, token).ConfigureAwait(false))
                return false;

            job.Status = current.Status;
            job.StartedUtc = current.StartedUtc;
            job.LastUpdateUtc = current.LastUpdateUtc;
            job.Progress = current.Progress;
            return true;
        }

        /// <summary>
        /// Record a worker heartbeat: refresh the job's last-update time and raise its progress to at least
        /// <paramref name="minimumProgress"/>. Never changes the job's status. A false result means the job is no
        /// longer Running (cancelled, failed by the stale-job reaper, or deleted) and the worker should stop.
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        /// <param name="minimumProgress">Progress floor (0 to 100); progress is never lowered.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the job is still Running.</returns>
        public async Task<bool> HeartbeatAsync(string jobId, int minimumProgress = 0, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(jobId)) throw new ArgumentNullException(nameof(jobId));
            return await _Database.Jobs.TryHeartbeatAsync(jobId, minimumProgress, DateTime.UtcNow, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Move a Queued or Running job to a terminal status. The first terminal status wins: when the job is already
        /// terminal (for example cancelled while the worker was finishing), nothing is written.
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        /// <param name="status">Terminal status: Succeeded, Failed, or Cancelled.</param>
        /// <param name="resultJson">Result payload, or null to keep the stored one.</param>
        /// <param name="errorReason">Error reason, or null to keep the stored one.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call made the job terminal; false when it was missing or already terminal.</returns>
        public async Task<bool> TryFinishAsync(string jobId, JobStatusEnum status, string? resultJson, string? errorReason, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(jobId)) throw new ArgumentNullException(nameof(jobId));
            if (!JobStateMachine.IsTerminal(status)) throw new ArgumentException("Status " + status + " is not terminal.", nameof(status));

            for (int attempt = 0; attempt < _MaxTransitionAttempts; attempt++)
            {
                Job? current = await _Database.Jobs.ReadAsync(jobId, token).ConfigureAwait(false);
                if (current == null || JobStateMachine.IsTerminal(current.Status)) return false;

                JobStatusEnum from = current.Status;
                current.Status = status;
                if (status == JobStatusEnum.Succeeded) current.Progress = 100;
                if (resultJson != null) current.ResultJson = resultJson;
                if (errorReason != null) current.ErrorReason = errorReason;
                current.CompletedUtc = DateTime.UtcNow;
                current.LastUpdateUtc = current.CompletedUtc.Value;
                if (await _Database.Jobs.TryUpdateIfStatusAsync(current, new JobStatusEnum[] { from }, token).ConfigureAwait(false))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Fail any job stuck in Running past the stale threshold (its worker likely died), so it reaches a
        /// terminal status instead of hanging. Invoked periodically from the Admiral health loop.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public async Task MaintainAsync(CancellationToken token = default)
        {
            // Only Running jobs can go stale; read just those instead of the whole (unbounded) jobs table.
            JobQuery query = new JobQuery();
            query.Statuses.Add(JobStatusEnum.Running);
            query.PageSize = 1000;
            List<Job> jobs = (await _Database.Jobs.EnumeratePageAsync(query, token).ConfigureAwait(false)).Objects;
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-_StaleRunningMinutes);
            foreach (Job job in jobs)
            {
                if (job.Status != JobStatusEnum.Running) continue;
                if (job.LastUpdateUtc > cutoff) continue;

                // Conditional on Running, so a job its worker finished (or that was cancelled) after the read above is
                // left alone.
                job.Status = JobStatusEnum.Failed;
                job.ErrorReason = "job worker did not report within " + _StaleRunningMinutes + " minutes";
                job.CompletedUtc = DateTime.UtcNow;
                job.LastUpdateUtc = DateTime.UtcNow;
                if (await _Database.Jobs.TryUpdateIfStatusAsync(job, new JobStatusEnum[] { JobStatusEnum.Running }, token).ConfigureAwait(false))
                    _Logging.Warn(_Header + "failed stale running job " + job.Id);
            }
        }

        #endregion
    }
}

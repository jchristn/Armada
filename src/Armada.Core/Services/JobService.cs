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
        /// Cancel a job. A terminal job cannot be cancelled.
        /// </summary>
        /// <param name="job">The job to cancel.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated job.</returns>
        public async Task<Job> CancelAsync(Job job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (!JobStateMachine.CanTransition(job.Status, JobStatusEnum.Cancelled))
                throw new InvalidOperationException("Job " + job.Id + " cannot be cancelled from status " + job.Status + ".");

            job.Status = JobStatusEnum.Cancelled;
            job.CompletedUtc = DateTime.UtcNow;
            job.LastUpdateUtc = DateTime.UtcNow;
            job = await _Database.Jobs.UpdateAsync(job, token).ConfigureAwait(false);
            _Logging.Info(_Header + "cancelled job " + job.Id);
            return job;
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

                job.Status = JobStatusEnum.Failed;
                job.ErrorReason = "job worker did not report within " + _StaleRunningMinutes + " minutes";
                job.CompletedUtc = DateTime.UtcNow;
                job.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Jobs.UpdateAsync(job, token).ConfigureAwait(false);
                _Logging.Warn(_Header + "failed stale running job " + job.Id);
            }
        }

        #endregion
    }
}

namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Lists the jobs the Admiral recorded on a Harbor and stops one by ID, for jobs no mission or captain still points
    /// at (for example an agent left running after its mission was cancelled). Visibility follows the Harbor's.
    /// </summary>
    public class HarborJobControlService
    {
        #region Private-Members

        private const int _GracefulStopMs = 10000;
        private readonly HarborService _Harbors;
        private readonly DatabaseDriver _Database;
        private readonly HarborConnectionManager _Connections;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="harbors">Harbor service, for visibility.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="connections">Harbor connection manager, to deliver the stop.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public HarborJobControlService(HarborService harbors, DatabaseDriver database, HarborConnectionManager connections)
        {
            _Harbors = harbors ?? throw new ArgumentNullException(nameof(harbors));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Connections = connections ?? throw new ArgumentNullException(nameof(connections));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// One page of a Harbor's recorded jobs, newest launch first (Order CreatedAscending for oldest first), counted and
        /// cut in the database.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="openOnly">Only jobs that have not ended.</param>
        /// <param name="query">Paging; CreatedAfter and CreatedBefore bound the launch time when openOnly is false (default: the last 7 days).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page, or null when the Harbor is not visible to the caller.</returns>
        /// <exception cref="ArgumentNullException">Thrown when auth, harborId or query is null.</exception>
        public async Task<EnumerationResult<HarborJobRecord>?> EnumerateJobsAsync(AuthContext auth, string harborId, bool openOnly, EnumerationQuery query, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (query == null) throw new ArgumentNullException(nameof(query));

            Harbor? harbor = await _Harbors.ReadAsync(auth, harborId, token).ConfigureAwait(false);
            if (harbor == null) return null;

            // Counted and cut in the database. DateTime.MaxValue is not a portable SQL timestamp, so "no upper bound" is a
            // far-future instant instead.
            DateTime fromUtc = query.CreatedAfter ?? DateTime.UtcNow.AddDays(-7);
            DateTime toUtc = query.CreatedBefore ?? new DateTime(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return await _Database.HarborJobs.EnumeratePageAsync(harbor.Id, openOnly, fromUtc, toUtc, query, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Ask a Harbor to stop one of its jobs. The Admiral stops tracking the job at once; the Harbor stops the process
        /// and reports the exit, which closes the job record.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="jobId">Job identifier, as listed by <see cref="EnumerateJobsAsync"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What happened.</returns>
        /// <exception cref="ArgumentNullException">Thrown when auth, harborId or jobId is null.</exception>
        public async Task<HarborJobStopResult> StopJobAsync(AuthContext auth, string harborId, string jobId, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (String.IsNullOrWhiteSpace(jobId)) throw new ArgumentNullException(nameof(jobId));

            Harbor? harbor = await _Harbors.ReadAsync(auth, harborId, token).ConfigureAwait(false);
            HarborJobRecord? job = harbor == null ? null : await _Database.HarborJobs.ReadByJobIdAsync(jobId, token).ConfigureAwait(false);
            if (harbor == null || job == null || !String.Equals(job.HarborId, harbor.Id, StringComparison.Ordinal))
                return new HarborJobStopResult { Status = HarborJobStopStatusEnum.NotFound, Message = "No job " + jobId + " on Harbor " + harborId + "." };

            if (job.EndedUtc.HasValue)
                return new HarborJobStopResult { Status = HarborJobStopStatusEnum.AlreadyEnded, Job = job, Message = "The job already ended." };

            if (!_Connections.IsConnected(harbor.Id))
                return new HarborJobStopResult
                {
                    Status = HarborJobStopStatusEnum.HarborNotConnected,
                    Job = job,
                    Message = "Harbor " + harbor.Id + " is not connected. It stops jobs this Admiral no longer owns when it reconnects."
                };

            await _Connections.KillJobAsync(harbor.Id, job.JobId, _GracefulStopMs, token).ConfigureAwait(false);
            job.StopRequested = true;
            job.LastUpdateUtc = DateTime.UtcNow;
            job = await _Database.HarborJobs.UpdateAsync(job, token).ConfigureAwait(false);
            return new HarborJobStopResult { Status = HarborJobStopStatusEnum.Requested, Job = job, Message = "Stop sent to Harbor " + harbor.Id + "." };
        }

        #endregion
    }
}

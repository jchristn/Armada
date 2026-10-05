namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Job persistence decorator that runs a test hook immediately before every job write reaches the database. A test
    /// uses it to land a concurrent change (for example a cancel) between a worker's read and its write, deterministically
    /// and without sleeps.
    /// </summary>
    public sealed class HookedJobMethods : IJobMethods
    {
        #region Public-Members

        /// <summary>
        /// Invoked before each write with the job id and the status the write sets, or null for a write that does not
        /// set a status (a heartbeat). Awaited before the write proceeds. Null disables the hook.
        /// </summary>
        public Func<string, JobStatusEnum?, Task>? BeforeWriteAsync { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly IJobMethods _Inner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="inner">Real job methods.</param>
        public HookedJobMethods(IJobMethods inner)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<Job> CreateAsync(Job job, CancellationToken token = default)
        {
            return _Inner.CreateAsync(job, token);
        }

        /// <inheritdoc />
        public async Task<Job> UpdateAsync(Job job, CancellationToken token = default)
        {
            await InvokeHookAsync(job.Id, job.Status).ConfigureAwait(false);
            return await _Inner.UpdateAsync(job, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> TryUpdateIfStatusAsync(Job job, IReadOnlyCollection<JobStatusEnum> expectedStatuses, CancellationToken token = default)
        {
            await InvokeHookAsync(job.Id, job.Status).ConfigureAwait(false);
            return await _Inner.TryUpdateIfStatusAsync(job, expectedStatuses, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> TryHeartbeatAsync(string id, int minimumProgress, DateTime lastUpdateUtc, CancellationToken token = default)
        {
            await InvokeHookAsync(id, null).ConfigureAwait(false);
            return await _Inner.TryHeartbeatAsync(id, minimumProgress, lastUpdateUtc, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<Job?> ReadAsync(string id, CancellationToken token = default)
        {
            return _Inner.ReadAsync(id, token);
        }

        /// <inheritdoc />
        public Task<Job?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            return _Inner.ReadAsync(tenantId, id, token);
        }

        /// <inheritdoc />
        public Task<Job?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            return _Inner.ReadAsync(tenantId, userId, id, token);
        }

        /// <inheritdoc />
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Inner.DeleteAsync(id, token);
        }

        /// <inheritdoc />
        public Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            return _Inner.DeleteAsync(tenantId, id, token);
        }

        /// <inheritdoc />
        public Task<List<Job>> EnumerateAsync(CancellationToken token = default)
        {
            return _Inner.EnumerateAsync(token);
        }

        /// <inheritdoc />
        public Task<List<Job>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            return _Inner.EnumerateAsync(tenantId, token);
        }

        /// <inheritdoc />
        public Task<List<Job>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            return _Inner.EnumerateAsync(tenantId, userId, token);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<Job>> EnumeratePageAsync(JobQuery query, CancellationToken token = default)
        {
            return _Inner.EnumeratePageAsync(query, token);
        }

        /// <inheritdoc />
        public Task<bool> ExistsAnyAsync(CancellationToken token = default)
        {
            return _Inner.ExistsAnyAsync(token);
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            return _Inner.ExistsAsync(id, token);
        }

        #endregion

        #region Private-Methods

        private async Task InvokeHookAsync(string jobId, JobStatusEnum? status)
        {
            Func<string, JobStatusEnum?, Task>? hook = BeforeWriteAsync;
            if (hook != null) await hook(jobId, status).ConfigureAwait(false);
        }

        #endregion
    }
}

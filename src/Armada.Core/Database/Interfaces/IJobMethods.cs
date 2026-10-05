namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for background jobs.
    /// </summary>
    public interface IJobMethods
    {
        /// <summary>
        /// Creates a new job.
        /// </summary>
        Task<Job> CreateAsync(Job job, CancellationToken token = default);

        /// <summary>
        /// Updates an existing job, rewriting every column unconditionally. Not for status changes or heartbeats of a
        /// job another party may change concurrently: use <see cref="TryUpdateIfStatusAsync"/> and
        /// <see cref="TryHeartbeatAsync"/>, which cannot overwrite a cancellation or a terminal status.
        /// </summary>
        Task<Job> UpdateAsync(Job job, CancellationToken token = default);

        /// <summary>
        /// Writes a job's lifecycle fields (status, progress, result, error reason, and the started, completed, and
        /// last-update times) only when the stored status is one of <paramref name="expectedStatuses"/>, as a single
        /// conditional statement. Identity, ownership, name, kind, and creation time are never written. This is how
        /// status transitions are made: a transition whose precondition no longer holds (for example a job that was
        /// cancelled after the caller read it) does not apply, so a terminal status is never overwritten.
        /// </summary>
        /// <param name="job">The job carrying the values to write.</param>
        /// <param name="expectedStatuses">The statuses the stored row must have for the write to apply.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the row was updated; false when the job does not exist or its status was not expected.</returns>
        Task<bool> TryUpdateIfStatusAsync(Job job, IReadOnlyCollection<JobStatusEnum> expectedStatuses, CancellationToken token = default);

        /// <summary>
        /// Records a worker heartbeat on a Running job: sets the last-update time and raises progress to at least
        /// <paramref name="minimumProgress"/>. Never changes the status or any other field, and does nothing when the
        /// job is not Running.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="minimumProgress">Progress floor (0 to 100); progress is never lowered.</param>
        /// <param name="lastUpdateUtc">Heartbeat time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the heartbeat was recorded; false when the job does not exist or is no longer Running.</returns>
        Task<bool> TryHeartbeatAsync(string id, int minimumProgress, DateTime lastUpdateUtc, CancellationToken token = default);

        /// <summary>
        /// Reads a job by its identifier.
        /// </summary>
        Task<Job?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Reads a job for a specific tenant.
        /// </summary>
        Task<Job?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Reads a job for a specific tenant and user.
        /// </summary>
        Task<Job?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Deletes a job by its identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Deletes a job for a specific tenant.
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerates all jobs (newest first).
        /// </summary>
        Task<List<Job>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerates jobs for a specific tenant (newest first).
        /// </summary>
        Task<List<Job>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerates jobs for a specific tenant and user (newest first).
        /// </summary>
        Task<List<Job>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate one page of jobs matching a query, newest first, with the total count.
        /// </summary>
        /// <param name="query">Filter and page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page and total.</returns>
        Task<EnumerationResult<Job>> EnumeratePageAsync(JobQuery query, CancellationToken token = default);

        /// <summary>
        /// Whether any job exists.
        /// </summary>
        Task<bool> ExistsAnyAsync(CancellationToken token = default);

        /// <summary>
        /// Whether a job with the given id exists.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}

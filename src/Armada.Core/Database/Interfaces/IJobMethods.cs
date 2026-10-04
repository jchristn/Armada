namespace Armada.Core.Database.Interfaces
{
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
        /// Updates an existing job.
        /// </summary>
        Task<Job> UpdateAsync(Job job, CancellationToken token = default);

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

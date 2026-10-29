namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for black box storage migration jobs (blackbox_migrations, migration 104).
    /// </summary>
    public interface IBlackBoxMigrationMethods
    {
        /// <summary>
        /// Insert a job.
        /// </summary>
        /// <param name="job">Job.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job.</returns>
        /// <exception cref="ArgumentNullException">Thrown when job is null.</exception>
        Task<BlackBoxStorageMigrationJob> CreateAsync(BlackBoxStorageMigrationJob job, CancellationToken token = default);

        /// <summary>
        /// Read a job.
        /// </summary>
        /// <param name="id">Job id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<BlackBoxStorageMigrationJob?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Save a job's state and progress.
        /// </summary>
        /// <param name="job">Job.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the row was updated.</returns>
        /// <exception cref="ArgumentNullException">Thrown when job is null.</exception>
        Task<bool> UpdateAsync(BlackBoxStorageMigrationJob job, CancellationToken token = default);

        /// <summary>
        /// Jobs that have not finished (Pending or Running), oldest first: the ones to resume after a restart.
        /// </summary>
        /// <param name="query">Page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of jobs.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<BlackBoxStorageMigrationJob>> EnumerateActiveAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// All jobs, newest first.
        /// </summary>
        /// <param name="query">Page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of jobs.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<BlackBoxStorageMigrationJob>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);
    }
}

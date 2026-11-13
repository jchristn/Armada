namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for normalized objectives/backlog entries.
    /// </summary>
    public interface IObjectiveMethods
    {
        /// <summary>
        /// Creates a new objective or backlog entry.
        /// </summary>
        Task<Objective> CreateAsync(Objective objective, CancellationToken token = default);

        /// <summary>
        /// Updates an existing objective or backlog entry.
        /// </summary>
        Task<Objective> UpdateAsync(Objective objective, CancellationToken token = default);

        /// <summary>
        /// Reads an objective by its identifier.
        /// </summary>
        Task<Objective?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Reads an objective for a specific tenant.
        /// </summary>
        Task<Objective?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Reads an objective for a specific tenant and user.
        /// </summary>
        Task<Objective?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Deletes an objective by its identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Deletes an objective for a specific tenant.
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerates all objectives.
        /// </summary>
        Task<List<Objective>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerates objectives for a specific tenant.
        /// </summary>
        Task<List<Objective>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerates objectives for a specific tenant and user.
        /// </summary>
        Task<List<Objective>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Determines whether any objectives exist.
        /// </summary>
        Task<bool> ExistsAnyAsync(CancellationToken token = default);

        /// <summary>
        /// Determines whether an objective exists.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read one page of objectives matching a query, most recently updated first; the id breaks ties. Scope and
        /// every filter apply in the database before the count, so TotalRecords is exact for the caller.
        /// </summary>
        /// <param name="tenantId">Only this tenant's objectives, or null for every tenant.</param>
        /// <param name="userId">Only this user's objectives, or null for every user.</param>
        /// <param name="query">Filters and paging (page size 1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of objectives.</returns>
        Task<EnumerationResult<Objective>> EnumeratePageAsync(string? tenantId, string? userId, ObjectiveQuery query, CancellationToken token = default);

        /// <summary>
        /// Read the highest backlog rank in scope.
        /// </summary>
        /// <param name="tenantId">Only this tenant's objectives, or null for every tenant.</param>
        /// <param name="userId">Only this user's objectives, or null for every user.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The highest rank, or null when there are no objectives in scope.</returns>
        Task<int?> ReadMaxRankAsync(string? tenantId, string? userId, CancellationToken token = default);
    }
}

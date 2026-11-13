namespace Armada.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for incidents (incidents). Each row holds the current state of one incident; the list is paged and
    /// counted in the database.
    /// </summary>
    public interface IIncidentMethods
    {
        /// <summary>
        /// Create a row.
        /// </summary>
        /// <param name="incident">Incident.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored incident.</returns>
        Task<Incident> CreateAsync(Incident incident, CancellationToken token = default);

        /// <summary>
        /// Replace a row.
        /// </summary>
        /// <param name="incident">Incident.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored incident.</returns>
        Task<Incident> UpdateAsync(Incident incident, CancellationToken token = default);

        /// <summary>
        /// Read one row, optionally limited to a tenant and user (from the scope's TenantId and UserId).
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="scope">Scope (TenantId, UserId), or null for no limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The incident, or null.</returns>
        Task<Incident?> ReadAsync(string id, IncidentQuery? scope = null, CancellationToken token = default);

        /// <summary>
        /// Delete one row.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate one page of rows matching the query's filters and scope, ordered by last update (newest
        /// first) then id, with an exact total from a COUNT over the same filter.
        /// </summary>
        /// <param name="query">Filters, scope, and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        Task<EnumerationResult<Incident>> EnumerateAsync(IncidentQuery query, CancellationToken token = default);

        /// <summary>
        /// Number of rows in the table (used to decide whether legacy event snapshots still need importing).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row count.</returns>
        Task<long> CountAsync(CancellationToken token = default);
    }
}

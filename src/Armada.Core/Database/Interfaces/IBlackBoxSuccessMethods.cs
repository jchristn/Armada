namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for the black box success flight recorder (blackbox_successes).
    /// </summary>
    public interface IBlackBoxSuccessMethods
    {
        /// <summary>
        /// Insert a success record.
        /// </summary>
        /// <param name="success">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when success is null.</exception>
        Task<BlackBoxSuccess> CreateAsync(BlackBoxSuccess success, CancellationToken token = default);

        /// <summary>
        /// Read a success record by id.
        /// </summary>
        /// <param name="id">Record id.</param>
        /// <param name="tenantId">Tenant scope, or null for any tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<BlackBoxSuccess?> ReadAsync(string id, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// The newest success record of a mission, or null.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when missionId is null or empty.</exception>
        Task<BlackBoxSuccess?> ReadLatestByMissionAsync(string missionId, CancellationToken token = default);

        /// <summary>
        /// One page of a vessel's success records that landed in a window, newest first (id breaks ties), paged and
        /// counted in the database.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="tenantId">Tenant scope, or null for any tenant.</param>
        /// <param name="userId">Owner scope, or null for any owner.</param>
        /// <param name="fromUtc">Inclusive window start.</param>
        /// <param name="toUtc">Inclusive window end.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vesselId is null or empty or query is null.</exception>
        Task<EnumerationResult<BlackBoxSuccess>> EnumerateByVesselAsync(string vesselId, string? tenantId, string? userId, DateTime fromUtc, DateTime toUtc, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Count success records (all tenants).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The count.</returns>
        Task<long> CountAsync(CancellationToken token = default);

        /// <summary>
        /// Delete records whose expiry is before a time.
        /// </summary>
        /// <param name="beforeUtc">Cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteExpiredAsync(DateTime beforeUtc, CancellationToken token = default);
    }
}

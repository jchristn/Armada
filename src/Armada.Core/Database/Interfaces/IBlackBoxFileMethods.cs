namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for black box file rows (blackbox_files), the cross-entry query surface for hot spots.
    /// </summary>
    public interface IBlackBoxFileMethods
    {
        /// <summary>
        /// Insert file rows in one transaction (at most 1000 per call).
        /// </summary>
        /// <param name="files">Rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows inserted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when files is null.</exception>
        /// <exception cref="Armada.Core.Services.CollectionLimitExceededException">Thrown for more than 1000 rows.</exception>
        Task<int> CreateManyAsync(List<BlackBoxFile> files, CancellationToken token = default);

        /// <summary>
        /// Enumerate an entry's file rows, ordered by path then id.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="role">Role filter, or null for all.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty, or query is null.</exception>
        Task<EnumerationResult<BlackBoxFile>> EnumerateAsync(string entryId, BlackBoxFileRoleEnum? role, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Aggregate file rows by vessel and directory (hot spots), paged in SQL: file count descending, vessel, directory.
        /// TenantId, VesselId, CreatedAfter and CreatedBefore of the request filter the rows; entries in the Deleting state
        /// are excluded.
        /// </summary>
        /// <param name="query">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        Task<EnumerationResult<BlackBoxHotspot>> EnumerateHotspotsAsync(BlackBoxHotspotEnumerateRequest query, CancellationToken token = default);

        /// <summary>
        /// Delete every file row of an entry.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty.</exception>
        Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);
    }
}

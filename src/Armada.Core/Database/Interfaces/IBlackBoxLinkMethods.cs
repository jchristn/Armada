namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for black box link rows (blackbox_links): missions related to an entry.
    /// </summary>
    public interface IBlackBoxLinkMethods
    {
        /// <summary>
        /// Insert link rows in one transaction (at most 1000 per call).
        /// </summary>
        /// <param name="links">Rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows inserted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when links is null.</exception>
        /// <exception cref="Armada.Core.Services.CollectionLimitExceededException">Thrown for more than 1000 rows.</exception>
        Task<int> CreateManyAsync(List<BlackBoxLink> links, CancellationToken token = default);

        /// <summary>
        /// Enumerate an entry's links, ordered by creation then id.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty, or query is null.</exception>
        Task<EnumerationResult<BlackBoxLink>> EnumerateAsync(string entryId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate links that point at a mission (entries the mission is related to), newest first by default.
        /// </summary>
        /// <param name="missionId">Related mission id.</param>
        /// <param name="tenantId">Tenant scope, or null for all.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when missionId is null or empty, or query is null.</exception>
        Task<EnumerationResult<BlackBoxLink>> EnumerateByMissionAsync(string missionId, string? tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Delete every link row of an entry.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty.</exception>
        Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);
    }
}

namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for fleet recommendations attached to a vessel import batch. Each recommendation is a row in
    /// vessel_import_fleet_recommendations with its vessels as ordered child rows in
    /// vessel_import_fleet_recommendation_vessels. Every method is tenant-scoped.
    /// </summary>
    public interface IVesselImportFleetRecommendationMethods
    {
        /// <summary>
        /// Replace every recommendation of a batch with the given list, in one transaction. Each recommendation's
        /// TenantId, BatchId, and missing Id are filled in, and its VesselIds are stored as child rows.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="batchId">Import batch identifier.</param>
        /// <param name="recommendations">New recommendations; an empty list clears the batch.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored recommendations.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        Task<List<VesselImportFleetRecommendation>> ReplaceForBatchAsync(string tenantId, string batchId, List<VesselImportFleetRecommendation> recommendations, CancellationToken token = default);

        /// <summary>
        /// Read the recommendations of a batch ordered by sort order, each with its vessel identifiers.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="batchId">Import batch identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Recommendations; empty when there are none.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        Task<List<VesselImportFleetRecommendation>> EnumerateByBatchAsync(string tenantId, string batchId, CancellationToken token = default);

        /// <summary>
        /// Record the fleet a recommendation was applied to.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Recommendation identifier (vfr_ prefix).</param>
        /// <param name="fleetId">Fleet identifier, or null to clear.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or id is null or empty.</exception>
        Task UpdateAppliedFleetAsync(string tenantId, string id, string? fleetId, CancellationToken token = default);

        /// <summary>
        /// Delete every recommendation (and vessel link) of a batch.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="batchId">Import batch identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        Task DeleteByBatchAsync(string tenantId, string batchId, CancellationToken token = default);
    }
}

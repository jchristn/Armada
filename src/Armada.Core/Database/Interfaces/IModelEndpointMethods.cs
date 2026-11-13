namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for managed model endpoints (embedding and inference).
    /// </summary>
    public interface IModelEndpointMethods
    {
        /// <summary>
        /// Creates a new model endpoint.
        /// </summary>
        Task<ModelEndpoint> CreateAsync(ModelEndpoint endpoint, CancellationToken token = default);

        /// <summary>
        /// Updates an existing model endpoint.
        /// </summary>
        Task<ModelEndpoint> UpdateAsync(ModelEndpoint endpoint, CancellationToken token = default);

        /// <summary>
        /// Reads a model endpoint by its identifier.
        /// </summary>
        Task<ModelEndpoint?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Reads a model endpoint for a specific tenant.
        /// </summary>
        Task<ModelEndpoint?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Reads a model endpoint for a specific tenant and user.
        /// </summary>
        Task<ModelEndpoint?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Deletes a model endpoint by its identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Deletes a model endpoint for a specific tenant.
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerates all model endpoints (newest first).
        /// </summary>
        Task<List<ModelEndpoint>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerates model endpoints for a specific tenant (newest first).
        /// </summary>
        Task<List<ModelEndpoint>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerates model endpoints for a specific tenant and user (newest first).
        /// </summary>
        Task<List<ModelEndpoint>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Whether any model endpoint exists.
        /// </summary>
        Task<bool> ExistsAnyAsync(CancellationToken token = default);

        /// <summary>
        /// Whether a model endpoint with the given id exists.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// One page of model endpoints visible through <paramref name="filter"/>, counted and cut in the data source (COUNT plus
        /// LIMIT/OFFSET over the same filter), ordered by creation time in the query's direction with the id as the
        /// tie-break. CreatedAfter and CreatedBefore apply.
        /// </summary>
        /// <param name="query">Paging, order, and created-time filters.</param>
        /// <param name="filter">Caller visibility.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page with exact totals.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when query or filter is null.</exception>
        Task<EnumerationResult<ModelEndpoint>> EnumeratePageAsync(EnumerationQuery query, ScopedEnumerationFilter filter, CancellationToken token = default);
    }
}

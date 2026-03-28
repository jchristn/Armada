namespace Armada.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for pipelines and their stages.
    /// </summary>
    public interface IPipelineMethods
    {
        /// <summary>
        /// Create a pipeline with its stages.
        /// </summary>
        Task<Pipeline> CreateAsync(Pipeline pipeline, CancellationToken token = default);

        /// <summary>
        /// Read a pipeline by identifier, including its stages.
        /// </summary>
        Task<Pipeline?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a pipeline by name, including its stages.
        /// </summary>
        Task<Pipeline?> ReadByNameAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Read a pipeline by tenant and name, including its stages.
        /// </summary>
        Task<Pipeline?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default);

        /// <summary>
        /// Update a pipeline and its stages.
        /// </summary>
        Task<Pipeline> UpdateAsync(Pipeline pipeline, CancellationToken token = default);

        /// <summary>
        /// Delete a pipeline and its stages by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all pipelines, including their stages.
        /// </summary>
        Task<List<Pipeline>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate pipelines with pagination and filtering, including their stages.
        /// </summary>
        Task<EnumerationResult<Pipeline>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Check if a pipeline exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check if a pipeline exists by name.
        /// </summary>
        Task<bool> ExistsByNameAsync(string name, CancellationToken token = default);
    }
}

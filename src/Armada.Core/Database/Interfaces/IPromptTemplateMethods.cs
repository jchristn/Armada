namespace Armada.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for prompt templates.
    /// </summary>
    public interface IPromptTemplateMethods
    {
        /// <summary>
        /// Create a prompt template.
        /// </summary>
        Task<PromptTemplate> CreateAsync(PromptTemplate template, CancellationToken token = default);

        /// <summary>
        /// Read a prompt template by identifier.
        /// </summary>
        Task<PromptTemplate?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a prompt template by name.
        /// </summary>
        Task<PromptTemplate?> ReadByNameAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Read a prompt template by tenant and name.
        /// </summary>
        Task<PromptTemplate?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default);

        /// <summary>
        /// Update a prompt template.
        /// </summary>
        Task<PromptTemplate> UpdateAsync(PromptTemplate template, CancellationToken token = default);

        /// <summary>
        /// Delete a prompt template by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all prompt templates.
        /// </summary>
        Task<List<PromptTemplate>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate prompt templates with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<PromptTemplate>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Check if a prompt template exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check if a prompt template exists by name.
        /// </summary>
        Task<bool> ExistsByNameAsync(string name, CancellationToken token = default);
    }
}

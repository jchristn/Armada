namespace Armada.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for personas.
    /// </summary>
    public interface IPersonaMethods
    {
        /// <summary>
        /// Create a persona.
        /// </summary>
        Task<Persona> CreateAsync(Persona persona, CancellationToken token = default);

        /// <summary>
        /// Read a persona by identifier.
        /// </summary>
        Task<Persona?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a persona by name.
        /// </summary>
        Task<Persona?> ReadByNameAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Read a persona by tenant and name.
        /// </summary>
        Task<Persona?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default);

        /// <summary>
        /// Update a persona.
        /// </summary>
        Task<Persona> UpdateAsync(Persona persona, CancellationToken token = default);

        /// <summary>
        /// Delete a persona by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all personas.
        /// </summary>
        Task<List<Persona>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate personas with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<Persona>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Check if a persona exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check if a persona exists by name.
        /// </summary>
        Task<bool> ExistsByNameAsync(string name, CancellationToken token = default);

        /// <summary>
        /// One page of personas visible through <paramref name="filter"/>, counted and cut in the data source (COUNT plus
        /// LIMIT/OFFSET over the same filter), ordered by creation time in the query's direction with the id as the
        /// tie-break. CreatedAfter and CreatedBefore apply.
        /// </summary>
        /// <param name="query">Paging, order, and created-time filters.</param>
        /// <param name="filter">Caller visibility.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page with exact totals.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when query or filter is null.</exception>
        Task<EnumerationResult<Persona>> EnumeratePageAsync(EnumerationQuery query, ScopedEnumerationFilter filter, CancellationToken token = default);
    }
}

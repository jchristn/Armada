namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for black box notes (blackbox_notes).
    /// </summary>
    public interface IBlackBoxNoteMethods
    {
        /// <summary>
        /// Insert a note.
        /// </summary>
        /// <param name="note">Note.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The note.</returns>
        /// <exception cref="ArgumentNullException">Thrown when note is null.</exception>
        Task<BlackBoxNote> CreateAsync(BlackBoxNote note, CancellationToken token = default);

        /// <summary>
        /// Read a note by id.
        /// </summary>
        /// <param name="id">Note id.</param>
        /// <param name="tenantId">Tenant scope, or null for any tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The note, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<BlackBoxNote?> ReadAsync(string id, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Enumerate an entry's notes, ordered by creation then id per Order (CreatedDescending by default).
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="query">Paging and order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty, or query is null.</exception>
        Task<EnumerationResult<BlackBoxNote>> EnumerateAsync(string entryId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Count an entry's notes.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The count.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty.</exception>
        Task<long> CountByEntryAsync(string entryId, CancellationToken token = default);

        /// <summary>
        /// Delete a note.
        /// </summary>
        /// <param name="id">Note id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete every note of an entry.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty.</exception>
        Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);
    }
}

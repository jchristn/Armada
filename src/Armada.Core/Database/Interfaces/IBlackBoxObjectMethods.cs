namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for black box objects (blackbox_objects): metadata rows for every target, and the bytes
    /// themselves (the data BLOB column) for the Database target. Names are unique per entry.
    /// </summary>
    public interface IBlackBoxObjectMethods
    {
        /// <summary>
        /// Insert an object, or replace the existing object with the same entry and name, in one transaction. Data is
        /// written when set.
        /// </summary>
        /// <param name="obj">Object.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The object.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        Task<BlackBoxObject> UpsertAsync(BlackBoxObject obj, CancellationToken token = default);

        /// <summary>
        /// Read an object of an entry by name.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="name">Object name.</param>
        /// <param name="includeData">Whether to read the data column.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The object, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId or name is null or empty.</exception>
        Task<BlackBoxObject?> ReadAsync(string entryId, string name, bool includeData = false, CancellationToken token = default);

        /// <summary>
        /// Read an object by id.
        /// </summary>
        /// <param name="id">Object id.</param>
        /// <param name="includeData">Whether to read the data column.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The object, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<BlackBoxObject?> ReadByIdAsync(string id, bool includeData = false, CancellationToken token = default);

        /// <summary>
        /// Read a byte range of an object's data column, cut in SQL. Returns fewer bytes at the end, an empty array past
        /// the end, and null when the row or its data is missing.
        /// </summary>
        /// <param name="id">Object id.</param>
        /// <param name="offset">Zero-based offset.</param>
        /// <param name="length">Bytes to read (at least 1).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The bytes, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when offset is negative or length below 1.</exception>
        Task<byte[]?> ReadDataRangeAsync(string id, long offset, int length, CancellationToken token = default);

        /// <summary>
        /// Write (or, with null, clear) an object's data column.
        /// </summary>
        /// <param name="id">Object id.</param>
        /// <param name="data">Bytes, or null to clear.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> WriteDataAsync(string id, byte[]? data, CancellationToken token = default);

        /// <summary>
        /// Set or clear an object's missing flag.
        /// </summary>
        /// <param name="id">Object id.</param>
        /// <param name="missing">Missing.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> SetMissingAsync(string id, bool missing, CancellationToken token = default);

        /// <summary>
        /// Enumerate an entry's objects (metadata only), ordered by name then id.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="query">Paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty, or query is null.</exception>
        Task<EnumerationResult<BlackBoxObject>> EnumerateAsync(string entryId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Delete every object row of an entry.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty.</exception>
        Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);
    }
}

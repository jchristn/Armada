namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// A black box storage target: where object bytes live. The index rows (blackbox_entries and the blackbox_objects
    /// metadata) are always in the database; a store only holds bytes, already encoded by <see cref="BlackBoxObjectCodec"/>.
    /// Implementations: <see cref="DatabaseBlackBoxStore"/> (default) and, later, a Blobject-backed store for Disk, S3,
    /// AzureBlob, GoogleCloud, Cifs and Nfs (created by <see cref="BlackBoxStoreFactory"/>). Implementations must be
    /// thread-safe.
    /// </summary>
    public interface IBlackBoxStore
    {
        /// <summary>
        /// Target this store writes.
        /// </summary>
        BlackBoxStorageTargetEnum Target { get; }

        /// <summary>
        /// Write an object's bytes (replacing any existing bytes at the reference).
        /// </summary>
        /// <param name="write">Reference and bytes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when write is null.</exception>
        /// <exception cref="BlackBoxStoreUnavailableException">Thrown when the store cannot be written.</exception>
        Task WriteAsync(BlackBoxObjectWrite write, CancellationToken token = default);

        /// <summary>
        /// Open a stream over an object's stored (encoded) bytes, from offset for length bytes (null to the end).
        /// </summary>
        /// <param name="reference">Reference.</param>
        /// <param name="offset">Zero-based offset into the stored bytes.</param>
        /// <param name="length">Bytes to read, or null to the end.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A readable stream; the caller disposes it.</returns>
        /// <exception cref="ArgumentNullException">Thrown when reference is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when offset is negative or length below 1.</exception>
        /// <exception cref="BlackBoxObjectMissingException">Thrown when the bytes are missing.</exception>
        Task<Stream> OpenReadAsync(BlackBoxObjectRef reference, long offset, long? length, CancellationToken token = default);

        /// <summary>
        /// Whether an object's bytes exist.
        /// </summary>
        /// <param name="reference">Reference.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the bytes exist.</returns>
        /// <exception cref="ArgumentNullException">Thrown when reference is null.</exception>
        Task<bool> ExistsAsync(BlackBoxObjectRef reference, CancellationToken token = default);

        /// <summary>
        /// Delete an object's bytes. Deleting bytes that do not exist succeeds.
        /// </summary>
        /// <param name="reference">Reference.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when reference is null.</exception>
        /// <exception cref="BlackBoxStoreUnavailableException">Thrown when the store cannot be written.</exception>
        Task DeleteAsync(BlackBoxObjectRef reference, CancellationToken token = default);

        /// <summary>
        /// Delete several objects' bytes; failures are reported, not thrown.
        /// </summary>
        /// <param name="references">References (at most 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when references is null.</exception>
        Task<BlackBoxStoreDeleteResult> DeleteManyAsync(BlackBoxObjectRefSet references, CancellationToken token = default);

        /// <summary>
        /// Enumerate stored keys under a prefix (for the orphan sweep). For Database the prefix is an entry id and keys are
        /// {entryId}/{objectName}; for external targets it is a key prefix (BlackBoxKeyLayout).
        /// </summary>
        /// <param name="prefix">Prefix.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Keys, read from the store in bounded pages.</returns>
        IAsyncEnumerable<BlackBoxStoredKey> EnumerateAsync(string prefix, CancellationToken token = default);

        /// <summary>
        /// Check that the store is usable (a doctor check).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Health; never throws.</returns>
        Task<BlackBoxStoreHealth> CheckHealthAsync(CancellationToken token = default);
    }
}

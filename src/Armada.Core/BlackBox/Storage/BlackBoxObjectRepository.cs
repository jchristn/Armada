namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Writes and reads black box objects: the metadata row in blackbox_objects plus the bytes in the entry's store.
    /// Encodes with <see cref="BlackBoxObjectCodec"/> and verifies the SHA-256 on every whole read (and every read of a
    /// gzip object, which is always read whole). Thread-safe.
    /// </summary>
    public class BlackBoxObjectRepository
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly BlackBoxStoreFactory _Stores;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="stores">Store factory.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxObjectRepository(DatabaseDriver database, BlackBoxStoreFactory stores)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Stores = stores ?? throw new ArgumentNullException(nameof(stores));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write (or replace) an object of an entry in the entry's storage target.
        /// </summary>
        /// <param name="entry">Entry (its StorageTarget and StoragePrefix choose where bytes go).</param>
        /// <param name="name">Object name.</param>
        /// <param name="contentType">Content type.</param>
        /// <param name="raw">Uncompressed bytes.</param>
        /// <param name="compress">Whether to gzip.</param>
        /// <param name="forceDatabase">Store the bytes in the database whatever the entry's target (capture uses it so
        /// record.json is never lost when an external store fails). An object without a StorageKey lives in the database.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored object metadata.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry, name or raw is null.</exception>
        /// <exception cref="BlackBoxStoreUnavailableException">Thrown when the store write fails (the row is marked missing).</exception>
        /// <exception cref="NotSupportedException">Thrown when the entry's target is not implemented.</exception>
        public async Task<BlackBoxObject> WriteAsync(BlackBoxEntry entry, string name, string contentType, byte[] raw, bool compress, bool forceDatabase = false, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            BlackBoxEncodedObject encoded = BlackBoxObjectCodec.Encode(raw, compress);
            bool isDatabase = forceDatabase || entry.StorageTarget == BlackBoxStorageTargetEnum.Database;
            BlackBoxObject obj = new BlackBoxObject
            {
                EntryId = entry.Id,
                TenantId = entry.TenantId,
                Name = BlackBoxLimits.Clip(name, 255)!,
                ContentType = String.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType,
                ContentEncoding = encoded.ContentEncoding,
                SizeBytes = encoded.Data.LongLength,
                UncompressedBytes = encoded.UncompressedBytes,
                Sha256 = encoded.Sha256,
                StorageKey = isDatabase ? null : BlackBoxKeyLayout.BuildKey(entry.StoragePrefix ?? entry.Id, name),
                CreatedUtc = DateTime.UtcNow,
                Data = isDatabase ? encoded.Data : null
            };
            await _Database.BlackBoxObjects.UpsertAsync(obj, token).ConfigureAwait(false);
            obj.Data = null;
            if (!isDatabase)
            {
                IBlackBoxStore store = _Stores.Get(entry.StorageTarget);
                try
                {
                    await store.WriteAsync(new BlackBoxObjectWrite
                    {
                        Reference = RefOf(entry, obj),
                        ContentType = obj.ContentType,
                        ContentEncoding = obj.ContentEncoding,
                        Data = encoded.Data,
                        Sha256 = encoded.Sha256
                    }, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    await _Database.BlackBoxObjects.SetMissingAsync(obj.Id, true, token).ConfigureAwait(false);
                    obj.IsMissing = true;
                    if (e is BlackBoxStoreUnavailableException) throw;
                    throw new BlackBoxStoreUnavailableException("The " + entry.StorageTarget + " store could not write " + name + ": " + e.Message, e);
                }
            }
            return obj;
        }

        /// <summary>
        /// Read an object, whole (length null) or a range of its uncompressed bytes.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="name">Object name.</param>
        /// <param name="offset">Zero-based offset into the uncompressed object.</param>
        /// <param name="length">Bytes to read, or null for the rest.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The bytes read.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry or name is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when offset is negative or length below 1.</exception>
        /// <exception cref="BlackBoxNotFoundException">Thrown when the object does not exist.</exception>
        /// <exception cref="BlackBoxObjectMissingException">Thrown when its bytes are missing.</exception>
        /// <exception cref="BlackBoxObjectCorruptException">Thrown when the SHA-256 does not match.</exception>
        public async Task<BlackBoxObjectReadResult> ReadAsync(BlackBoxEntry entry, string name, long offset = 0, long? length = null, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Offset must not be negative.");
            if (length.HasValue && length.Value < 1) throw new ArgumentOutOfRangeException(nameof(length), "Length must be at least 1.");
            BlackBoxObject? obj = await _Database.BlackBoxObjects.ReadAsync(entry.Id, name, false, token).ConfigureAwait(false);
            if (obj == null) throw new BlackBoxNotFoundException("Black box object " + name + " not found on entry " + entry.Id + ".");
            if (obj.IsMissing) throw new BlackBoxObjectMissingException("The bytes of " + name + " are missing from the " + (obj.StorageKey == null ? BlackBoxStorageTargetEnum.Database : entry.StorageTarget) + " store.");
            BlackBoxObjectRef reference = RefOf(entry, obj);
            IBlackBoxStore store = _Stores.Get(reference.Target);
            bool gzip = String.Equals(obj.ContentEncoding, BlackBoxObjectCodec.Gzip, StringComparison.OrdinalIgnoreCase);
            bool whole = gzip || (offset == 0 && (!length.HasValue || length.Value >= obj.UncompressedBytes));

            BlackBoxObjectReadResult result = new BlackBoxObjectReadResult { Object = obj, Offset = offset, TotalBytes = obj.UncompressedBytes };
            if (whole)
            {
                byte[] stored = await ReadAllAsync(store, reference, 0, null, token).ConfigureAwait(false);
                string sha = BlackBoxObjectCodec.Sha256Hex(stored);
                if (obj.Sha256 != null && !String.Equals(sha, obj.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new BlackBoxObjectCorruptException("The bytes of " + name + " on entry " + entry.Id + " do not match their SHA-256.");
                byte[] raw = BlackBoxObjectCodec.Decode(stored, obj.ContentEncoding);
                result.TotalBytes = raw.LongLength;
                result.Verified = obj.Sha256 != null;
                result.Data = Slice(raw, offset, length);
            }
            else
            {
                result.Data = await ReadAllAsync(store, reference, offset, length, token).ConfigureAwait(false);
            }
            return result;
        }

        /// <summary>
        /// The store reference of an object.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="obj">Object.</param>
        /// <returns>The reference.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static BlackBoxObjectRef RefOf(BlackBoxEntry entry, BlackBoxObject obj)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            return new BlackBoxObjectRef { Target = obj.StorageKey == null ? BlackBoxStorageTargetEnum.Database : entry.StorageTarget, EntryId = entry.Id, ObjectId = obj.Id, Name = obj.Name, StorageKey = obj.StorageKey };
        }

        #endregion

        #region Private-Methods

        private static async Task<byte[]> ReadAllAsync(IBlackBoxStore store, BlackBoxObjectRef reference, long offset, long? length, CancellationToken token)
        {
            using (Stream stream = await store.OpenReadAsync(reference, offset, length, token).ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, token).ConfigureAwait(false);
                return buffer.ToArray();
            }
        }

        private static byte[] Slice(byte[] raw, long offset, long? length)
        {
            if (offset >= raw.LongLength) return Array.Empty<byte>();
            long available = raw.LongLength - offset;
            long take = length.HasValue ? Math.Min(length.Value, available) : available;
            byte[] slice = new byte[take];
            Array.Copy(raw, offset, slice, 0, take);
            return slice;
        }

        #endregion
    }
}

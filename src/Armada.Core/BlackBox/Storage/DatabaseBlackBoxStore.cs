namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The default black box store: object bytes in the blackbox_objects.data BLOB column of the Armada database (SQLite
    /// BLOB, PostgreSQL bytea, SQL Server varbinary(max), MySQL LONGBLOB). Works on every provider and is included in
    /// backups. The object's metadata row must exist before its bytes are written (BlackBoxObjectRepository does that).
    /// Ranged reads are cut in SQL. Thread-safe.
    /// </summary>
    public class DatabaseBlackBoxStore : IBlackBoxStore
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public DatabaseBlackBoxStore(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public BlackBoxStorageTargetEnum Target => BlackBoxStorageTargetEnum.Database;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task WriteAsync(BlackBoxObjectWrite write, CancellationToken token = default)
        {
            if (write == null) throw new ArgumentNullException(nameof(write));
            if (write.Reference == null || String.IsNullOrEmpty(write.Reference.ObjectId)) throw new ArgumentException("The write needs an object id.", nameof(write));
            bool changed;
            try
            {
                changed = await _Database.BlackBoxObjects.WriteDataAsync(write.Reference.ObjectId, write.Data ?? Array.Empty<byte>(), token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new BlackBoxStoreUnavailableException("The database store could not write " + write.Reference.Name + ": " + e.Message, e);
            }
            if (!changed) throw new BlackBoxObjectMissingException("Object row " + write.Reference.ObjectId + " does not exist; write its metadata first.");
        }

        /// <inheritdoc />
        public async Task<Stream> OpenReadAsync(BlackBoxObjectRef reference, long offset, long? length, CancellationToken token = default)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Offset must not be negative.");
            if (length.HasValue && length.Value < 1) throw new ArgumentOutOfRangeException(nameof(length), "Length must be at least 1.");
            byte[]? data;
            if (length.HasValue)
            {
                int bounded = length.Value > Int32.MaxValue ? Int32.MaxValue : (int)length.Value;
                data = await _Database.BlackBoxObjects.ReadDataRangeAsync(reference.ObjectId, offset, bounded, token).ConfigureAwait(false);
            }
            else
            {
                BlackBoxObject? obj = await _Database.BlackBoxObjects.ReadByIdAsync(reference.ObjectId, true, token).ConfigureAwait(false);
                data = obj?.Data;
                if (data != null && offset > 0) data = offset >= data.LongLength ? Array.Empty<byte>() : data.AsSpan((int)offset).ToArray();
            }
            if (data == null) throw new BlackBoxObjectMissingException("The bytes of " + reference.Name + " are missing from the database store.");
            return new MemoryStream(data, false);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(BlackBoxObjectRef reference, CancellationToken token = default)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            byte[]? data = await _Database.BlackBoxObjects.ReadDataRangeAsync(reference.ObjectId, 0, 1, token).ConfigureAwait(false);
            return data != null;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(BlackBoxObjectRef reference, CancellationToken token = default)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            try
            {
                await _Database.BlackBoxObjects.WriteDataAsync(reference.ObjectId, null, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new BlackBoxStoreUnavailableException("The database store could not delete " + reference.Name + ": " + e.Message, e);
            }
        }

        /// <inheritdoc />
        public async Task<BlackBoxStoreDeleteResult> DeleteManyAsync(BlackBoxObjectRefSet references, CancellationToken token = default)
        {
            if (references == null) throw new ArgumentNullException(nameof(references));
            BlackBoxStoreDeleteResult result = new BlackBoxStoreDeleteResult();
            result.Requested = references.References.Count;
            foreach (BlackBoxObjectRef reference in references.References)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await DeleteAsync(reference, token).ConfigureAwait(false);
                    result.Deleted++;
                }
                catch (BlackBoxStoreUnavailableException e)
                {
                    result.Failed++;
                    result.FailedNames.Add(reference.Name);
                    if (result.FirstError == null) result.FirstError = e.Message;
                }
            }
            return result;
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<BlackBoxStoredKey> EnumerateAsync(string prefix, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(prefix)) yield break;
            string entryId = prefix.Split('/')[0];
            int page = 1;
            while (true)
            {
                EnumerationQuery query = new EnumerationQuery { PageNumber = page, PageSize = 100 };
                EnumerationResult<BlackBoxObject> objects = await _Database.BlackBoxObjects.EnumerateAsync(entryId, query, token).ConfigureAwait(false);
                foreach (BlackBoxObject obj in objects.Objects)
                {
                    if (obj.IsMissing) continue;
                    yield return new BlackBoxStoredKey { Key = obj.EntryId + "/" + obj.Name, SizeBytes = obj.SizeBytes, LastModifiedUtc = obj.CreatedUtc };
                }
                if (page >= objects.TotalPages) yield break;
                page++;
            }
        }

        /// <inheritdoc />
        public async Task<BlackBoxStoreHealth> CheckHealthAsync(CancellationToken token = default)
        {
            Stopwatch sw = Stopwatch.StartNew();
            BlackBoxStoreHealth health = new BlackBoxStoreHealth { Target = Target, CheckedUtc = DateTime.UtcNow };
            try
            {
                await _Database.BlackBoxObjects.ReadByIdAsync("bbo_health_check", false, token).ConfigureAwait(false);
                health.Healthy = true;
                health.Message = "Database store reachable.";
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                health.Healthy = false;
                health.Message = "Database store unreachable: " + e.Message;
            }
            health.LatencyMs = sw.ElapsedMilliseconds;
            return health;
        }

        #endregion
    }
}

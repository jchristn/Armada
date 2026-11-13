namespace Armada.Core.Database.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;

    /// <summary>
    /// Black box object persistence shared by the four providers (metadata rows and the Database target's BLOB column).
    /// Thread-safe.
    /// </summary>
    public abstract class BlackBoxObjectMethodsBase : IBlackBoxObjectMethods
    {
        #region Private-Members

        private const string _MetaColumns = "id, entry_id, tenant_id, name, content_type, content_encoding, size_bytes, uncompressed_bytes, sha256, storage_key, missing, created_utc";
        private readonly BlackBoxSqlDialect _Sql;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sql">Provider dialect.</param>
        /// <exception cref="ArgumentNullException">Thrown when sql is null.</exception>
        protected BlackBoxObjectMethodsBase(BlackBoxSqlDialect sql)
        {
            _Sql = sql ?? throw new ArgumentNullException(nameof(sql));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<BlackBoxObject> UpsertAsync(BlackBoxObject obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (String.IsNullOrEmpty(obj.EntryId)) throw new ArgumentException("EntryId is required.", nameof(obj));
            if (String.IsNullOrEmpty(obj.Name)) throw new ArgumentException("Name is required.", nameof(obj));
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_objects WHERE entry_id = @entry_id AND name = @name;", cmd =>
                {
                    _Sql.Add(cmd, "@entry_id", obj.EntryId);
                    _Sql.Add(cmd, "@name", obj.Name);
                }, token).ConfigureAwait(false);
                await _Sql.ExecuteAsync(conn, tx,
                    "INSERT INTO blackbox_objects (" + _MetaColumns + ", data) VALUES (@id, @entry_id, @tenant_id, @name, @content_type, @content_encoding, @size_bytes, @uncompressed_bytes, @sha256, @storage_key, @missing, @created_utc, @data);",
                    cmd =>
                    {
                        _Sql.Add(cmd, "@id", obj.Id);
                        _Sql.Add(cmd, "@entry_id", obj.EntryId);
                        _Sql.Add(cmd, "@tenant_id", obj.TenantId);
                        _Sql.Add(cmd, "@name", obj.Name);
                        _Sql.Add(cmd, "@content_type", obj.ContentType);
                        _Sql.Add(cmd, "@content_encoding", obj.ContentEncoding);
                        _Sql.Add(cmd, "@size_bytes", obj.SizeBytes);
                        _Sql.Add(cmd, "@uncompressed_bytes", obj.UncompressedBytes);
                        _Sql.Add(cmd, "@sha256", obj.Sha256);
                        _Sql.Add(cmd, "@storage_key", obj.StorageKey);
                        _Sql.Add(cmd, "@missing", obj.IsMissing);
                        _Sql.AddDate(cmd, "@created_utc", obj.CreatedUtc);
                        _Sql.AddBlob(cmd, "@data", obj.Data);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<BlackBoxObject?> ReadAsync(string entryId, string name, bool includeData = false, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            List<BlackBoxObject> rows = await _Sql.QueryAsync("SELECT " + _MetaColumns + (includeData ? ", data" : String.Empty) + " FROM blackbox_objects WHERE entry_id = @entry_id AND name = @name;",
                cmd => { _Sql.Add(cmd, "@entry_id", entryId); _Sql.Add(cmd, "@name", name); },
                r => FromReader(r, includeData), token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<BlackBoxObject?> ReadByIdAsync(string id, bool includeData = false, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<BlackBoxObject> rows = await _Sql.QueryAsync("SELECT " + _MetaColumns + (includeData ? ", data" : String.Empty) + " FROM blackbox_objects WHERE id = @id;",
                cmd => _Sql.Add(cmd, "@id", id), r => FromReader(r, includeData), token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<byte[]?> ReadDataRangeAsync(string id, long offset, int length, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Offset must not be negative.");
            if (length < 1) throw new ArgumentOutOfRangeException(nameof(length), "Length must be at least 1.");
            List<byte[]?> rows = await _Sql.QueryAsync(
                "SELECT CASE WHEN data IS NULL THEN 0 ELSE 1 END AS has_data, " + _Sql.BlobSlice("data", "@slice_offset", "@slice_length") + " AS slice FROM blackbox_objects WHERE id = @id;",
                cmd =>
                {
                    _Sql.Add(cmd, "@id", id);
                    _Sql.Add(cmd, "@slice_offset", offset + 1);
                    _Sql.Add(cmd, "@slice_length", length);
                },
                r => BlackBoxSqlDialect.ReadBool(r["has_data"], false) ? (BlackBoxSqlDialect.ReadBlob(r["slice"]) ?? Array.Empty<byte>()) : null,
                token).ConfigureAwait(false);
            return rows.Count == 0 ? null : rows[0];
        }

        /// <inheritdoc />
        public async Task<bool> WriteDataAsync(string id, byte[]? data, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int changed = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                changed = await _Sql.ExecuteAsync(conn, tx, "UPDATE blackbox_objects SET data = @data WHERE id = @id;", cmd =>
                {
                    _Sql.Add(cmd, "@id", id);
                    _Sql.AddBlob(cmd, "@data", data);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return changed > 0;
        }

        /// <inheritdoc />
        public async Task<bool> SetMissingAsync(string id, bool missing, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int changed = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                changed = await _Sql.ExecuteAsync(conn, tx, "UPDATE blackbox_objects SET missing = @missing WHERE id = @id;", cmd =>
                {
                    _Sql.Add(cmd, "@id", id);
                    _Sql.Add(cmd, "@missing", missing);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return changed > 0;
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxObject>> EnumerateAsync(string entryId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            return BlackBoxSqlPaging.PageAsync(_Sql, "SELECT COUNT(*) FROM blackbox_objects WHERE entry_id = @entry_id",
                "SELECT " + _MetaColumns + " FROM blackbox_objects WHERE entry_id = @entry_id ORDER BY name ASC, id ASC",
                cmd => _Sql.Add(cmd, "@entry_id", entryId), query.PageNumber, query.PageSize, r => FromReader(r, false), token);
        }

        /// <inheritdoc />
        public async Task<List<string>> FilterReferencedStorageKeysAsync(List<string> storageKeys, CancellationToken token = default)
        {
            if (storageKeys == null) throw new ArgumentNullException(nameof(storageKeys));
            if (storageKeys.Count > Armada.Core.Services.CollectionLimits.MaxRequestItems) throw new ArgumentOutOfRangeException(nameof(storageKeys), "At most " + Armada.Core.Services.CollectionLimits.MaxRequestItems + " keys per call.");
            List<string> distinct = storageKeys.Where(k => !String.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal).ToList();
            List<string> found = new List<string>();
            for (int start = 0; start < distinct.Count; start += 100)
            {
                List<string> batch = distinct.Skip(start).Take(100).ToList();
                string names = String.Join(", ", batch.Select((k, i) => "@k" + i));
                List<string> rows = await _Sql.QueryAsync("SELECT DISTINCT storage_key FROM blackbox_objects WHERE storage_key IN (" + names + ");",
                    cmd => { for (int i = 0; i < batch.Count; i++) _Sql.Add(cmd, "@k" + i, batch[i]); },
                    r => r["storage_key"].ToString()!, token).ConfigureAwait(false);
                found.AddRange(rows);
            }
            return found;
        }

        /// <inheritdoc />
        public async Task<bool> SetLocationAsync(string id, string? storageKey, byte[]? data, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int changed = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                changed = await _Sql.ExecuteAsync(conn, tx, "UPDATE blackbox_objects SET storage_key = @storage_key, data = @data, missing = @missing WHERE id = @id;", cmd =>
                {
                    _Sql.Add(cmd, "@id", id);
                    _Sql.Add(cmd, "@storage_key", storageKey);
                    _Sql.AddBlob(cmd, "@data", data);
                    _Sql.Add(cmd, "@missing", false);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return changed > 0;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            int deleted = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                deleted = await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_objects WHERE entry_id = @entry_id;", cmd => _Sql.Add(cmd, "@entry_id", entryId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private BlackBoxObject FromReader(DbDataReader r, bool includeData)
        {
            BlackBoxObject o = new BlackBoxObject();
            o.Id = r["id"].ToString()!;
            o.EntryId = r["entry_id"].ToString()!;
            o.TenantId = BlackBoxSqlDialect.ReadString(r["tenant_id"]);
            o.Name = r["name"].ToString()!;
            o.ContentType = BlackBoxSqlDialect.ReadString(r["content_type"]) ?? "application/octet-stream";
            o.ContentEncoding = BlackBoxSqlDialect.ReadString(r["content_encoding"]) ?? "identity";
            o.SizeBytes = BlackBoxSqlDialect.ReadLong(r["size_bytes"], 0);
            o.UncompressedBytes = BlackBoxSqlDialect.ReadLong(r["uncompressed_bytes"], 0);
            o.Sha256 = BlackBoxSqlDialect.ReadString(r["sha256"]);
            o.StorageKey = BlackBoxSqlDialect.ReadString(r["storage_key"]);
            o.IsMissing = BlackBoxSqlDialect.ReadBool(r["missing"], false);
            o.CreatedUtc = _Sql.ReadDate(r["created_utc"]) ?? DateTime.UtcNow;
            if (includeData) o.Data = BlackBoxSqlDialect.ReadBlob(r["data"]);
            return o;
        }

        #endregion
    }
}

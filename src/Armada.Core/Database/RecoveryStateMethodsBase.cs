namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database.BlackBox;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;

    /// <summary>
    /// Recovery state persistence shared by the four providers. Thread-safe.
    /// </summary>
    public abstract class RecoveryStateMethodsBase : IRecoveryStateMethods
    {
        private readonly BlackBoxSqlDialect _Sql;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sql">Provider dialect.</param>
        /// <exception cref="ArgumentNullException">Thrown when sql is null.</exception>
        protected RecoveryStateMethodsBase(BlackBoxSqlDialect sql)
        {
            _Sql = sql ?? throw new ArgumentNullException(nameof(sql));
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the kind or key is empty.</exception>
        public Task UpsertAsync(RecoveryStateEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (String.IsNullOrEmpty(entry.Kind) || String.IsNullOrEmpty(entry.Key)) throw new ArgumentException("Kind and key are required.", nameof(entry));
            return _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                await _Sql.ExecuteAsync(conn, tx, "DELETE FROM recovery_state WHERE kind = @kind AND state_key = @state_key;", cmd => BindKey(cmd, entry.Kind, entry.Key), token).ConfigureAwait(false);
                await _Sql.ExecuteAsync(conn, tx,
                    "INSERT INTO recovery_state (kind, state_key, payload_json, expires_utc, last_update_utc) VALUES (@kind, @state_key, @payload_json, @expires_utc, @last_update_utc);",
                    cmd =>
                    {
                        BindKey(cmd, entry.Kind, entry.Key);
                        _Sql.Add(cmd, "@payload_json", entry.PayloadJson ?? "{}");
                        _Sql.AddDate(cmd, "@expires_utc", entry.ExpiresUtc);
                        _Sql.AddDate(cmd, "@last_update_utc", entry.LastUpdateUtc);
                    }, token).ConfigureAwait(false);
            }, token);
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when the kind or key is empty.</exception>
        public async Task<RecoveryStateEntry?> ReadAsync(string kind, string key, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(kind)) throw new ArgumentNullException(nameof(kind));
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            List<RecoveryStateEntry> rows = await _Sql.QueryAsync("SELECT * FROM recovery_state WHERE kind = @kind AND state_key = @state_key;",
                cmd => BindKey(cmd, kind, key), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when the kind is empty.</exception>
        public Task<List<RecoveryStateEntry>> EnumerateAsync(string kind, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(kind)) throw new ArgumentNullException(nameof(kind));
            return _Sql.QueryAsync("SELECT * FROM recovery_state WHERE kind = @kind AND expires_utc > @now ORDER BY state_key;",
                cmd =>
                {
                    _Sql.Add(cmd, "@kind", kind);
                    _Sql.AddDate(cmd, "@now", DateTime.UtcNow);
                }, FromReader, token);
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when the kind or key is empty.</exception>
        public Task DeleteAsync(string kind, string key, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(kind)) throw new ArgumentNullException(nameof(kind));
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            return _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                await _Sql.ExecuteAsync(conn, tx, "DELETE FROM recovery_state WHERE kind = @kind AND state_key = @state_key;", cmd => BindKey(cmd, kind, key), token).ConfigureAwait(false);
            }, token);
        }

        /// <inheritdoc />
        public async Task<int> PurgeExpiredAsync(CancellationToken token = default)
        {
            int removed = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                removed = await _Sql.ExecuteAsync(conn, tx, "DELETE FROM recovery_state WHERE expires_utc <= @now;", cmd => _Sql.AddDate(cmd, "@now", DateTime.UtcNow), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return removed;
        }

        private void BindKey(DbCommand cmd, string kind, string key)
        {
            _Sql.Add(cmd, "@kind", kind);
            _Sql.Add(cmd, "@state_key", key);
        }

        private RecoveryStateEntry FromReader(DbDataReader reader)
        {
            RecoveryStateEntry entry = new RecoveryStateEntry();
            entry.Kind = reader["kind"].ToString() ?? "";
            entry.Key = reader["state_key"].ToString() ?? "";
            entry.PayloadJson = reader["payload_json"].ToString() ?? "{}";
            entry.ExpiresUtc = _Sql.ReadDate(reader["expires_utc"]) ?? DateTime.UtcNow;
            entry.LastUpdateUtc = _Sql.ReadDate(reader["last_update_utc"]) ?? DateTime.UtcNow;
            return entry;
        }
    }
}

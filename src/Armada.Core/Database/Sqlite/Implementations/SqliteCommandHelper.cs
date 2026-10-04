namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    /// <summary>
    /// Provider-specific command plumbing shared by the SQLite implementations of the vessel import, fleet action,
    /// and vessel health methods: transactional writes (under the driver's write lock where the provider needs one), typed parameter binding,
    /// typed column reads, and paging syntax.
    /// </summary>
    internal static class SqliteCommandHelper
    {
        #region Internal-Methods

        /// <summary>
        /// Run a unit of work inside a transaction. The driver's write lock is held for the duration so concurrent
        /// in-process writers queue instead of contending for the SQLite database lock.
        /// </summary>
        internal static async Task WriteAsync(
            string connectionString,
            SemaphoreSlim? writeLock,
            Func<SqliteConnection, SqliteTransaction, Task> work,
            CancellationToken token)
        {
            if (writeLock != null) await writeLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using (SqliteConnection conn = new SqliteConnection(connectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteTransaction tx = (SqliteTransaction)await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                    {
                        await work(conn, tx).ConfigureAwait(false);
                        await tx.CommitAsync(token).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (writeLock != null) writeLock.Release();
            }
        }

        /// <summary>
        /// Execute a non-query command inside a transaction.
        /// </summary>
        internal static async Task<int> ExecuteAsync(SqliteConnection conn, SqliteTransaction tx, string sql, Action<SqliteCommand>? bind, CancellationToken token)
        {
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                bind?.Invoke(cmd);
                return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Execute a query inside a transaction and return the first column of the first row, or null.
        /// </summary>
        internal static async Task<object?> ScalarAsync(SqliteConnection conn, SqliteTransaction? tx, string sql, Action<SqliteCommand>? bind, CancellationToken token)
        {
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = sql;
                bind?.Invoke(cmd);
                object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                if (result == null || result == DBNull.Value) return null;
                return result;
            }
        }

        /// <summary>
        /// Run a query on a new connection and map every row.
        /// </summary>
        internal static async Task<List<T>> QueryAsync<T>(string connectionString, string sql, Action<SqliteCommand>? bind, Func<SqliteDataReader, T> map, CancellationToken token)
        {
            using (SqliteConnection conn = new SqliteConnection(connectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await QueryAsync(conn, sql, bind, map, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Run a query on an open connection and map every row.
        /// </summary>
        internal static async Task<List<T>> QueryAsync<T>(SqliteConnection conn, string sql, Action<SqliteCommand>? bind, Func<SqliteDataReader, T> map, CancellationToken token)
        {
            return await QueryAsync(conn, null, sql, bind, map, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Run a query on an open connection, optionally inside a transaction, and map every row.
        /// </summary>
        internal static async Task<List<T>> QueryAsync<T>(SqliteConnection conn, SqliteTransaction? tx, string sql, Action<SqliteCommand>? bind, Func<SqliteDataReader, T> map, CancellationToken token)
        {
            List<T> results = new List<T>();
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = sql;
                bind?.Invoke(cmd);
                using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                        results.Add(map(reader));
                }
            }

            return results;
        }

        /// <summary>
        /// Run a count query on an open connection.
        /// </summary>
        internal static async Task<long> CountAsync(SqliteConnection conn, string sql, Action<SqliteCommand>? bind, CancellationToken token)
        {
            object? result = await ScalarAsync(conn, null, sql, bind, token).ConfigureAwait(false);
            return result == null ? 0 : Convert.ToInt64(result);
        }

        /// <summary>
        /// Bind a value, mapping null to DBNull.
        /// </summary>
        internal static void Add(SqliteCommand cmd, string name, object? value)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        /// <summary>
        /// Bind a UTC timestamp. Unspecified kinds are treated as UTC.
        /// </summary>
        internal static void AddDate(SqliteCommand cmd, string name, DateTime value)
        {
            cmd.Parameters.AddWithValue(name, SqliteDatabaseDriver.ToIso8601(NormalizeUtc(value)));
        }

        /// <summary>
        /// Bind a nullable UTC timestamp. Unspecified kinds are treated as UTC.
        /// </summary>
        internal static void AddDate(SqliteCommand cmd, string name, DateTime? value)
        {
            if (value.HasValue) AddDate(cmd, name, value.Value);
            else cmd.Parameters.AddWithValue(name, DBNull.Value);
        }

        /// <summary>
        /// Read a non-null UTC timestamp column.
        /// </summary>
        internal static DateTime ReadDate(object value)
        {
            DateTime? parsed = ReadNullableDate(value);
            return parsed ?? DateTime.UtcNow;
        }

        /// <summary>
        /// Read a nullable UTC timestamp column.
        /// </summary>
        internal static DateTime? ReadNullableDate(object value)
        {
            return SqliteDatabaseDriver.FromIso8601Nullable(value);
        }

        /// <summary>
        /// Read a nullable string column; empty strings read as null.
        /// </summary>
        internal static string? ReadString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return String.IsNullOrEmpty(str) ? null : str;
        }

        /// <summary>
        /// Read a boolean column, with a fallback for null.
        /// </summary>
        internal static bool ReadBool(object value, bool fallback)
        {
            if (value == null || value == DBNull.Value) return fallback;
            return Convert.ToInt64(value) != 0;
        }

        /// <summary>
        /// Read a nullable boolean column.
        /// </summary>
        internal static bool? ReadNullableBool(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt64(value) != 0;
        }

        /// <summary>
        /// Read an integer column, with a fallback for null.
        /// </summary>
        internal static int ReadInt(object value, int fallback)
        {
            if (value == null || value == DBNull.Value) return fallback;
            return Convert.ToInt32(value);
        }

        /// <summary>
        /// Read a nullable integer column.
        /// </summary>
        internal static int? ReadNullableInt(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt32(value);
        }

        /// <summary>
        /// Read a nullable 64-bit integer column.
        /// </summary>
        internal static long? ReadNullableLong(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt64(value);
        }

        /// <summary>
        /// Parse an enum stored by name, with a fallback for null or unrecognized values.
        /// </summary>
        internal static TEnum ReadEnum<TEnum>(object value, TEnum fallback) where TEnum : struct
        {
            if (value == null || value == DBNull.Value) return fallback;
            return Enum.TryParse<TEnum>(value.ToString(), true, out TEnum parsed) ? parsed : fallback;
        }

        /// <summary>
        /// Paging clause appended after ORDER BY.
        /// </summary>
        internal static string Page(int offset, int limit)
        {
            return " LIMIT " + limit + " OFFSET " + offset;
        }

        #endregion

        #region Private-Methods

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return value;
        }

        #endregion
    }
}

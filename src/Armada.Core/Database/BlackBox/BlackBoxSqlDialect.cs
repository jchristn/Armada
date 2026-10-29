namespace Armada.Core.Database.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Provider plumbing the shared black box SQL needs: connections, transactional writes (under the provider's write
    /// gate or lock), timestamp and BLOB binding, boolean literals, paging and BLOB slicing. One subclass per provider
    /// (SQLite, PostgreSQL, SQL Server, MySQL); the black box method implementations share their SQL through it.
    /// Thread-safe when the subclass is.
    /// </summary>
    public abstract class BlackBoxSqlDialect
    {
        #region Public-Members

        /// <summary>
        /// Provider name (Sqlite, Postgresql, SqlServer, Mysql).
        /// </summary>
        public abstract string Provider { get; }

        /// <summary>
        /// SQL literal for true in a boolean column.
        /// </summary>
        public virtual string True => "1";

        /// <summary>
        /// SQL literal for false in a boolean column.
        /// </summary>
        public virtual string False => "0";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create an unopened connection.
        /// </summary>
        /// <returns>The connection.</returns>
        public abstract DbConnection CreateConnection();

        /// <summary>
        /// Run a unit of work inside a transaction, under the provider's write gate or lock.
        /// </summary>
        /// <param name="work">Work.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public abstract Task WriteAsync(Func<DbConnection, DbTransaction, Task> work, CancellationToken token);

        /// <summary>
        /// Bind a nullable UTC timestamp in the provider's convention.
        /// </summary>
        /// <param name="cmd">Command.</param>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Value.</param>
        public abstract void AddDate(DbCommand cmd, string name, DateTime? value);

        /// <summary>
        /// Read a nullable UTC timestamp column.
        /// </summary>
        /// <param name="value">Column value.</param>
        /// <returns>The timestamp (UTC), or null.</returns>
        public abstract DateTime? ReadDate(object? value);

        /// <summary>
        /// Paging clause appended after ORDER BY.
        /// </summary>
        /// <param name="offset">Rows to skip.</param>
        /// <param name="limit">Rows to return.</param>
        /// <returns>SQL.</returns>
        public abstract string Page(int offset, int limit);

        /// <summary>
        /// SQL for the first <paramref name="length"/> characters of a text column.
        /// </summary>
        /// <param name="column">Column expression.</param>
        /// <param name="length">Characters (at least 1).</param>
        /// <returns>SQL expression.</returns>
        public virtual string TextPrefix(string column, int length)
        {
            return "SUBSTRING(" + column + ", 1, " + Math.Max(1, length).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// SQL expression for a slice of a BLOB column. The offset parameter is 1-based.
        /// </summary>
        /// <param name="column">Column.</param>
        /// <param name="offsetParameter">1-based offset parameter name.</param>
        /// <param name="lengthParameter">Length parameter name.</param>
        /// <returns>SQL.</returns>
        public virtual string BlobSlice(string column, string offsetParameter, string lengthParameter)
        {
            return "SUBSTRING(" + column + ", " + offsetParameter + ", " + lengthParameter + ")";
        }

        /// <summary>
        /// Bind a value, mapping null to DBNull.
        /// </summary>
        /// <param name="cmd">Command.</param>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Value.</param>
        public virtual void Add(DbCommand cmd, string name, object? value)
        {
            DbParameter parameter = cmd.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(parameter);
        }

        /// <summary>
        /// Bind a nullable binary value (typed, so a null binds as a binary null).
        /// </summary>
        /// <param name="cmd">Command.</param>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Value.</param>
        public virtual void AddBlob(DbCommand cmd, string name, byte[]? value)
        {
            DbParameter parameter = cmd.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = DbType.Binary;
            parameter.Value = (object?)value ?? DBNull.Value;
            cmd.Parameters.Add(parameter);
        }

        /// <summary>
        /// Execute a non-query inside a transaction.
        /// </summary>
        /// <param name="conn">Connection.</param>
        /// <param name="tx">Transaction.</param>
        /// <param name="sql">SQL.</param>
        /// <param name="bind">Binder, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows affected.</returns>
        public async Task<int> ExecuteAsync(DbConnection conn, DbTransaction tx, string sql, Action<DbCommand>? bind, CancellationToken token)
        {
            using (DbCommand cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                bind?.Invoke(cmd);
                return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Run a query and map every row.
        /// </summary>
        /// <typeparam name="T">Row type.</typeparam>
        /// <param name="conn">Open connection.</param>
        /// <param name="tx">Transaction, or null.</param>
        /// <param name="sql">SQL.</param>
        /// <param name="bind">Binder, or null.</param>
        /// <param name="map">Row mapper.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        public async Task<List<T>> QueryAsync<T>(DbConnection conn, DbTransaction? tx, string sql, Action<DbCommand>? bind, Func<DbDataReader, T> map, CancellationToken token)
        {
            List<T> rows = new List<T>();
            using (DbCommand cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = sql;
                bind?.Invoke(cmd);
                using (DbDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        rows.Add(map(reader));
                    }
                }
            }
            return rows;
        }

        /// <summary>
        /// Open a connection, run a query and map every row.
        /// </summary>
        /// <typeparam name="T">Row type.</typeparam>
        /// <param name="sql">SQL.</param>
        /// <param name="bind">Binder, or null.</param>
        /// <param name="map">Row mapper.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        public async Task<List<T>> QueryAsync<T>(string sql, Action<DbCommand>? bind, Func<DbDataReader, T> map, CancellationToken token)
        {
            using (DbConnection conn = CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await QueryAsync(conn, null, sql, bind, map, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Run a scalar query and read it as a 64-bit integer (null reads as 0).
        /// </summary>
        /// <param name="conn">Open connection.</param>
        /// <param name="tx">Transaction, or null.</param>
        /// <param name="sql">SQL.</param>
        /// <param name="bind">Binder, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The value.</returns>
        public async Task<long> ScalarLongAsync(DbConnection conn, DbTransaction? tx, string sql, Action<DbCommand>? bind, CancellationToken token)
        {
            using (DbCommand cmd = conn.CreateCommand())
            {
                if (tx != null) cmd.Transaction = tx;
                cmd.CommandText = sql;
                bind?.Invoke(cmd);
                object? value = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                return ReadLong(value, 0);
            }
        }

        /// <summary>
        /// Open a connection and run a scalar query as a 64-bit integer.
        /// </summary>
        /// <param name="sql">SQL.</param>
        /// <param name="bind">Binder, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The value.</returns>
        public async Task<long> ScalarLongAsync(string sql, Action<DbCommand>? bind, CancellationToken token)
        {
            using (DbConnection conn = CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await ScalarLongAsync(conn, null, sql, bind, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Read a nullable string column; empty reads as null.
        /// </summary>
        /// <param name="value">Column value.</param>
        /// <returns>The string, or null.</returns>
        public static string? ReadString(object? value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
            return str.Length == 0 ? null : str;
        }

        /// <summary>
        /// Read a boolean column.
        /// </summary>
        /// <param name="value">Column value.</param>
        /// <param name="fallback">Value for null.</param>
        /// <returns>The value.</returns>
        public static bool ReadBool(object? value, bool fallback)
        {
            if (value == null || value == DBNull.Value) return fallback;
            if (value is bool b) return b;
            return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
        }

        /// <summary>
        /// Read a 64-bit integer column.
        /// </summary>
        /// <param name="value">Column value.</param>
        /// <param name="fallback">Value for null.</param>
        /// <returns>The value.</returns>
        public static long ReadLong(object? value, long fallback)
        {
            if (value == null || value == DBNull.Value) return fallback;
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a nullable 32-bit integer column.
        /// </summary>
        /// <param name="value">Column value.</param>
        /// <returns>The value, or null.</returns>
        public static int? ReadNullableInt(object? value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read an enum stored by name; unknown or null values read as the fallback.
        /// </summary>
        /// <typeparam name="TEnum">Enum type.</typeparam>
        /// <param name="value">Column value.</param>
        /// <param name="fallback">Fallback.</param>
        /// <returns>The value.</returns>
        public static TEnum ReadEnum<TEnum>(object? value, TEnum fallback) where TEnum : struct
        {
            string? str = ReadString(value);
            if (str == null) return fallback;
            return Enum.TryParse<TEnum>(str, true, out TEnum parsed) && Enum.IsDefined(typeof(TEnum), parsed) ? parsed : fallback;
        }

        /// <summary>
        /// Read a nullable enum stored by name; unknown values read as null.
        /// </summary>
        /// <typeparam name="TEnum">Enum type.</typeparam>
        /// <param name="value">Column value.</param>
        /// <returns>The value, or null.</returns>
        public static TEnum? ReadNullableEnum<TEnum>(object? value) where TEnum : struct
        {
            string? str = ReadString(value);
            if (str == null) return null;
            return Enum.TryParse<TEnum>(str, true, out TEnum parsed) && Enum.IsDefined(typeof(TEnum), parsed) ? parsed : (TEnum?)null;
        }

        /// <summary>
        /// Read a nullable binary column.
        /// </summary>
        /// <param name="value">Column value.</param>
        /// <returns>The bytes, or null.</returns>
        public static byte[]? ReadBlob(object? value)
        {
            if (value == null || value == DBNull.Value) return null;
            if (value is byte[] bytes) return bytes;
            if (value is string str) return System.Text.Encoding.UTF8.GetBytes(str);
            throw new InvalidCastException("Unexpected BLOB column type " + value.GetType().FullName + ".");
        }

        /// <summary>
        /// Escape a value for a LIKE pattern with ESCAPE '!'.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>The escaped value.</returns>
        public static string EscapeLike(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![");
        }

        /// <summary>
        /// Normalize page bounds: size 1..1000, number at least 1.
        /// </summary>
        /// <param name="pageNumber">Requested page number.</param>
        /// <param name="pageSize">Requested page size.</param>
        /// <param name="number">Normalized page number.</param>
        /// <param name="size">Normalized page size.</param>
        /// <param name="offset">Row offset.</param>
        public static void NormalizePage(int pageNumber, int pageSize, out int number, out int size, out int offset)
        {
            size = pageSize < 1 ? 1 : (pageSize > 1000 ? 1000 : pageSize);
            number = pageNumber < 1 ? 1 : pageNumber;
            long rawOffset = ((long)number - 1) * size;
            offset = rawOffset > Int32.MaxValue ? Int32.MaxValue : (int)rawOffset;
        }

        #endregion
    }
}

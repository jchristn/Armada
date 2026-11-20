namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Microsoft.Data.SqlClient;
    using MySqlConnector;
    using Npgsql;
    using NpgsqlTypes;

    /// <summary>
    /// Provider details for <see cref="DataExpiryService"/>: the connection, the stored type of every column it compares
    /// (read from the live schema, so text and native timestamp columns and integer and boolean flags bind correctly on
    /// every provider and on upgraded databases), identifier quoting, and id-list parameters. Not thread safe; one
    /// instance serves one purge at a time.
    /// </summary>
    internal sealed class DataExpiryDialect
    {
        #region Private-Members

        private const string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";
        private readonly DatabaseTypeEnum _Type;
        private readonly string _ConnectionString;
        private Dictionary<string, string> _Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="type">Database provider.</param>
        /// <param name="connectionString">Connection string for that provider.</param>
        /// <exception cref="ArgumentNullException">Thrown when the connection string is null or empty.</exception>
        internal DataExpiryDialect(DatabaseTypeEnum type, string connectionString)
        {
            if (String.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            _Type = type;
            _ConnectionString = connectionString;
        }

        #endregion

        #region Internal-Members

        /// <summary>
        /// Database provider.
        /// </summary>
        internal DatabaseTypeEnum Type => _Type;

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Create an unopened connection for the provider.
        /// </summary>
        /// <returns>The connection.</returns>
        internal DbConnection CreateConnection()
        {
            switch (_Type)
            {
                case DatabaseTypeEnum.Postgresql: return new NpgsqlConnection(_ConnectionString);
                case DatabaseTypeEnum.Mysql: return new MySqlConnection(_ConnectionString);
                case DatabaseTypeEnum.SqlServer: return new SqlConnection(_ConnectionString);
                default: return new SqliteProviderConnection(_ConnectionString);
            }
        }

        /// <summary>
        /// Read the stored type of every column in the current schema.
        /// </summary>
        /// <param name="conn">Open connection.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        internal async Task LoadColumnsAsync(DbConnection conn, CancellationToken token)
        {
            string sql;
            switch (_Type)
            {
                case DatabaseTypeEnum.Postgresql:
                    sql = "SELECT table_name, column_name, data_type FROM information_schema.columns WHERE table_schema = current_schema();";
                    break;
                case DatabaseTypeEnum.Mysql:
                    sql = "SELECT table_name, column_name, data_type FROM information_schema.columns WHERE table_schema = DATABASE();";
                    break;
                case DatabaseTypeEnum.SqlServer:
                    sql = "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = SCHEMA_NAME();";
                    break;
                default:
                    sql = "SELECT m.name, p.name, p.type FROM sqlite_master m JOIN pragma_table_info(m.name) p WHERE m.type = 'table';";
                    break;
            }

            Dictionary<string, string> columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (DbCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                using (DbDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        string table = Text(reader, 0);
                        string column = Text(reader, 1);
                        string type = Text(reader, 2);
                        columns[table + "." + column] = type.ToLowerInvariant();
                    }
                }
            }

            _Columns = columns;
        }

        /// <summary>
        /// True when the schema has the column.
        /// </summary>
        /// <param name="table">Table name.</param>
        /// <param name="column">Column name.</param>
        /// <returns>True when present.</returns>
        internal bool HasColumn(string table, string column)
        {
            return _Columns.ContainsKey(table + "." + column);
        }

        /// <summary>
        /// Quote an identifier that is a reserved word on some provider (for example signals.read).
        /// </summary>
        /// <param name="identifier">Identifier.</param>
        /// <returns>The quoted identifier.</returns>
        internal string Quote(string identifier)
        {
            switch (_Type)
            {
                case DatabaseTypeEnum.Mysql: return "`" + identifier + "`";
                case DatabaseTypeEnum.SqlServer: return "[" + identifier + "]";
                default: return "\"" + identifier + "\"";
            }
        }

        /// <summary>
        /// Bind a UTC timestamp compared with <paramref name="table"/>.<paramref name="column"/>: ISO 8601 text for a
        /// text column, a native timestamp otherwise.
        /// </summary>
        /// <param name="cmd">Command.</param>
        /// <param name="name">Parameter name.</param>
        /// <param name="table">Table whose column the value is compared with.</param>
        /// <param name="column">Column the value is compared with.</param>
        /// <param name="utc">UTC timestamp.</param>
        internal void AddTimestamp(DbCommand cmd, string name, string table, string column, DateTime utc)
        {
            DateTime value = UtcClock.ToUtc(utc);
            string type = ColumnType(table, column);
            if (_Type == DatabaseTypeEnum.Sqlite || IsText(type))
            {
                Add(cmd, name, UtcClock.Format(value, _Iso8601Format));
                return;
            }

            if (_Type == DatabaseTypeEnum.Postgresql)
            {
                bool withZone = type.Contains("with time zone", StringComparison.Ordinal);
                NpgsqlParameter parameter = new NpgsqlParameter(name, withZone ? NpgsqlDbType.TimestampTz : NpgsqlDbType.Timestamp);
                parameter.Value = withZone ? value : DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
                cmd.Parameters.Add(parameter);
                return;
            }

            Add(cmd, name, DateTime.SpecifyKind(value, DateTimeKind.Unspecified));
        }

        /// <summary>
        /// Bind a flag compared with <paramref name="table"/>.<paramref name="column"/>: a boolean for a boolean column,
        /// 1 or 0 otherwise.
        /// </summary>
        /// <param name="cmd">Command.</param>
        /// <param name="name">Parameter name.</param>
        /// <param name="table">Table whose column the value is compared with.</param>
        /// <param name="column">Column the value is compared with.</param>
        /// <param name="value">Flag.</param>
        internal void AddFlag(DbCommand cmd, string name, string table, string column, bool value)
        {
            string type = ColumnType(table, column);
            if (type == "boolean" || type == "bool") Add(cmd, name, value);
            else Add(cmd, name, value ? 1 : 0);
        }

        /// <summary>
        /// Append an IN list of id parameters ("@p0, @p1, ...") and bind them.
        /// </summary>
        /// <param name="cmd">Command.</param>
        /// <param name="prefix">Parameter name prefix, unique within the command.</param>
        /// <param name="ids">Ids.</param>
        /// <returns>The comma-separated parameter list.</returns>
        internal static string AddIdList(DbCommand cmd, string prefix, IReadOnlyList<string> ids)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                string name = "@" + prefix + i.ToString(CultureInfo.InvariantCulture);
                if (i > 0) sb.Append(", ");
                sb.Append(name);
                Add(cmd, name, ids[i]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Bind a parameter.
        /// </summary>
        /// <param name="cmd">Command.</param>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Value; null binds DBNull.</param>
        internal static void Add(DbCommand cmd, string name, object? value)
        {
            DbParameter parameter = cmd.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(parameter);
        }

        #endregion

        #region Private-Methods

        private string ColumnType(string table, string column)
        {
            if (_Columns.TryGetValue(table + "." + column, out string? type)) return type;
            throw new InvalidOperationException("Data expiry compares " + table + "." + column + ", which the " + _Type + " schema does not have.");
        }

        private static string Text(DbDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return String.Empty;
            object value = reader.GetValue(ordinal);
            if (value is byte[] bytes) return Encoding.UTF8.GetString(bytes);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
        }

        private static bool IsText(string type)
        {
            return type.Contains("char", StringComparison.Ordinal)
                || type.Contains("text", StringComparison.Ordinal)
                || type == "string";
        }

        #endregion
    }
}

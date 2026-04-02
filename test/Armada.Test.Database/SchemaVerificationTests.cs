namespace Armada.Test.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Microsoft.Data.Sqlite;
    using Microsoft.Data.SqlClient;
    using MySqlConnector;
    using Npgsql;

    internal class SchemaVerificationTests
    {
        private readonly DatabaseSettings _Settings;

        public SchemaVerificationTests(DatabaseSettings settings)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task VerifyAsync(CancellationToken token = default)
        {
            await using DbConnection conn = CreateConnection();
            await conn.OpenAsync(token).ConfigureAwait(false);

            DatabaseAssert.True(await TableExistsAsync(conn, "schema_migrations", token).ConfigureAwait(false), "schema_migrations table missing");
            DatabaseAssert.True(await GetMaxSchemaVersionAsync(conn, token).ConfigureAwait(false) >= 27, "Expected schema version >= 27");

            await AssertColumnAsync(conn, "tenants", "is_protected", token).ConfigureAwait(false);
            await AssertColumnAsync(conn, "users", "is_protected", token).ConfigureAwait(false);
            await AssertColumnAsync(conn, "users", "is_tenant_admin", token).ConfigureAwait(false);
            await AssertColumnAsync(conn, "credentials", "is_protected", token).ConfigureAwait(false);
            await AssertColumnAsync(conn, "captains", "model", token).ConfigureAwait(false);
            await AssertColumnAsync(conn, "missions", "total_runtime_ms", token).ConfigureAwait(false);

            foreach (string table in new[] { "fleets", "vessels", "captains", "voyages", "missions", "docks", "signals", "events", "merge_entries" })
            {
                await AssertColumnAsync(conn, table, "tenant_id", token).ConfigureAwait(false);
                await AssertColumnAsync(conn, table, "user_id", token).ConfigureAwait(false);
            }

            foreach (string indexName in new[] {
                "idx_users_tenant_email",
                "idx_credentials_tenant_user",
                "idx_fleets_tenant_user",
                "idx_vessels_tenant_user",
                "idx_missions_tenant_user",
                "idx_signals_tenant_user",
                "idx_events_tenant_user",
                "idx_merge_entries_tenant_user"
            })
            {
                DatabaseAssert.True(await IndexExistsAsync(conn, indexName, token).ConfigureAwait(false), "Missing index " + indexName);
            }
        }

        private DbConnection CreateConnection()
        {
            switch (_Settings.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SqliteConnection(_Settings.GetConnectionString());
                case DatabaseTypeEnum.Postgresql:
                    return new NpgsqlConnection(_Settings.GetConnectionString());
                case DatabaseTypeEnum.Mysql:
                    return new MySqlConnection(_Settings.GetConnectionString());
                case DatabaseTypeEnum.SqlServer:
                    return new SqlConnection(_Settings.GetConnectionString());
                default:
                    throw new NotSupportedException("Unsupported database type: " + _Settings.Type.ToString());
            }
        }

        private async Task AssertColumnAsync(DbConnection conn, string tableName, string columnName, CancellationToken token)
        {
            DatabaseAssert.True(await ColumnExistsAsync(conn, tableName, columnName, token).ConfigureAwait(false), tableName + "." + columnName + " missing");
        }

        private async Task<bool> TableExistsAsync(DbConnection conn, string tableName, CancellationToken token)
        {
            switch (_Settings.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    return await ScalarCountAsync(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;", ("@name", tableName), token).ConfigureAwait(false) > 0;
                case DatabaseTypeEnum.Postgresql:
                    return await ScalarCountAsync(conn, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = CURRENT_SCHEMA() AND table_name = @name;", ("@name", tableName), token).ConfigureAwait(false) > 0;
                case DatabaseTypeEnum.Mysql:
                    return await ScalarCountAsync(conn, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @name;", ("@name", tableName), token).ConfigureAwait(false) > 0;
                case DatabaseTypeEnum.SqlServer:
                    return await ScalarCountAsync(conn, "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @name;", ("@name", tableName), token).ConfigureAwait(false) > 0;
                default:
                    return false;
            }
        }

        private async Task<bool> ColumnExistsAsync(DbConnection conn, string tableName, string columnName, CancellationToken token)
        {
            switch (_Settings.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    using (DbCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA table_info(" + tableName + ");";
                        using (DbDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                            {
                                if (String.Equals(reader["name"].ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                                    return true;
                            }
                        }
                    }
                    return false;
                case DatabaseTypeEnum.Postgresql:
                    return await ScalarCountAsync(conn, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = CURRENT_SCHEMA() AND table_name = @table AND column_name = @column;", ("@table", tableName), ("@column", columnName), token).ConfigureAwait(false) > 0;
                case DatabaseTypeEnum.Mysql:
                    return await ScalarCountAsync(conn, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @table AND column_name = @column;", ("@table", tableName), ("@column", columnName), token).ConfigureAwait(false) > 0;
                case DatabaseTypeEnum.SqlServer:
                    return await ScalarCountAsync(conn, "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND COLUMN_NAME = @column;", ("@table", tableName), ("@column", columnName), token).ConfigureAwait(false) > 0;
                default:
                    return false;
            }
        }

        private async Task<bool> IndexExistsAsync(DbConnection conn, string indexName, CancellationToken token)
        {
            switch (_Settings.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    return await ScalarCountAsync(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = @name;", ("@name", indexName), token).ConfigureAwait(false) > 0;
                case DatabaseTypeEnum.Postgresql:
                    return await ScalarCountAsync(conn, "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = CURRENT_SCHEMA() AND indexname = @name;", ("@name", indexName), token).ConfigureAwait(false) > 0;
                case DatabaseTypeEnum.Mysql:
                    return await ScalarCountAsync(conn, "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND index_name = @name;", ("@name", indexName), token).ConfigureAwait(false) > 0;
                case DatabaseTypeEnum.SqlServer:
                    return await ScalarCountAsync(conn, "SELECT COUNT(*) FROM sys.indexes WHERE name = @name;", ("@name", indexName), token).ConfigureAwait(false) > 0;
                default:
                    return false;
            }
        }

        private async Task<long> GetMaxSchemaVersionAsync(DbConnection conn, CancellationToken token)
        {
            using (DbCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
                object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                return Convert.ToInt64(result);
            }
        }

        private async Task<long> ScalarCountAsync(DbConnection conn, string sql, (string name, object value) parameter, CancellationToken token)
        {
            return await ScalarCountAsync(conn, sql, new[] { parameter }, token).ConfigureAwait(false);
        }

        private async Task<long> ScalarCountAsync(DbConnection conn, string sql, (string name, object value) parameter1, (string name, object value) parameter2, CancellationToken token)
        {
            return await ScalarCountAsync(conn, sql, new[] { parameter1, parameter2 }, token).ConfigureAwait(false);
        }

        private async Task<long> ScalarCountAsync(DbConnection conn, string sql, IEnumerable<(string name, object value)> parameters, CancellationToken token)
        {
            using (DbCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                foreach ((string name, object value) parameter in parameters)
                {
                    DbParameter dbParameter = cmd.CreateParameter();
                    dbParameter.ParameterName = parameter.name;
                    dbParameter.Value = parameter.value;
                    cmd.Parameters.Add(dbParameter);
                }

                object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                return Convert.ToInt64(result);
            }
        }
    }
}

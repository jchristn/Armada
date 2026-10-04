namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQL Server implementation of fleet action definition persistence. Deleting a built-in action is a soft
    /// delete (Active = false); deleting a user-defined action removes the row.
    /// </summary>
    public class FleetActionMethods : IFleetActionMethods
    {
        #region Private-Members

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public FleetActionMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
            _WriteLock = null;
        }
        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<FleetAction> CreateAsync(FleetAction action, CancellationToken token = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (String.IsNullOrEmpty(action.Id)) action.Id = Constants.IdGenerator.GenerateKSortable(Constants.FleetActionIdPrefix, 24);
            action.LastUpdateUtc = DateTime.UtcNow;

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO fleet_actions
                    (id, tenant_id, user_id, name, description, kind, command_text, prompt_template, pipeline_id, persona, timeout_seconds, default_concurrency, requires_clean_working_tree, is_built_in, built_in_key, active, created_utc, last_update_utc)
                    VALUES
                    (@id, @tenant_id, @user_id, @name, @description, @kind, @command_text, @prompt_template, @pipeline_id, @persona, @timeout_seconds, @default_concurrency, @requires_clean_working_tree, @is_built_in, @built_in_key, @active, @created_utc, @last_update_utc);",
                    cmd => Bind(cmd, action), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return action;
        }

        /// <inheritdoc />
        public async Task<FleetAction?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<FleetAction> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_actions WHERE id = @id;",
                cmd => SqlServerCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetAction?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<FleetAction> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_actions WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@id", id);
                },
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetAction?> ReadByBuiltInKeyAsync(string tenantId, string builtInKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(builtInKey)) throw new ArgumentNullException(nameof(builtInKey));
            List<FleetAction> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_actions WHERE tenant_id = @tenant_id AND built_in_key = @built_in_key ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@built_in_key", builtInKey);
                },
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetAction> UpdateAsync(FleetAction action, CancellationToken token = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (String.IsNullOrEmpty(action.Id)) throw new ArgumentException("Action identifier is required.", nameof(action));
            action.LastUpdateUtc = DateTime.UtcNow;

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"UPDATE fleet_actions SET
                    tenant_id = @tenant_id, user_id = @user_id, name = @name, description = @description, kind = @kind,
                    command_text = @command_text, prompt_template = @prompt_template, pipeline_id = @pipeline_id,
                    persona = @persona, timeout_seconds = @timeout_seconds, default_concurrency = @default_concurrency,
                    requires_clean_working_tree = @requires_clean_working_tree, is_built_in = @is_built_in,
                    built_in_key = @built_in_key, active = @active, created_utc = @created_utc,
                    last_update_utc = @last_update_utc
                    WHERE id = @id;", cmd => Bind(cmd, action), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return action;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await DeleteInternalAsync(null, id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await DeleteInternalAsync(tenantId, id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<FleetAction>> EnumerateAsync(string tenantId, EnumerationQuery query, bool includeInactive = false, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) query = new EnumerationQuery();

            List<string> conditions = new List<string> { "tenant_id = @tenant_id" };
            if (!includeInactive) conditions.Add("active = @active");
            if (query.CreatedAfter.HasValue) conditions.Add("created_utc > @created_after");
            if (query.CreatedBefore.HasValue) conditions.Add("created_utc < @created_before");

            Action<SqlCommand> bind = cmd =>
            {
                SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                if (!includeInactive) SqlServerCommandHelper.Add(cmd, "@active", true);
                if (query.CreatedAfter.HasValue) SqlServerCommandHelper.AddDate(cmd, "@created_after", query.CreatedAfter.Value);
                if (query.CreatedBefore.HasValue) SqlServerCommandHelper.AddDate(cmd, "@created_before", query.CreatedBefore.Value);
            };

            string where = " WHERE " + String.Join(" AND ", conditions);
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM fleet_actions" + where + ";", bind, token).ConfigureAwait(false);
                List<FleetAction> rows = await SqlServerCommandHelper.QueryAsync(conn,
                    "SELECT * FROM fleet_actions" + where + " ORDER BY created_utc " + direction + ", id " + direction + SqlServerCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);
                return EnumerationResult<FleetAction>.Create(query, rows, total);
            }
        }

        #endregion

        #region Private-Methods

        private async Task DeleteInternalAsync(string? tenantId, string id, CancellationToken token)
        {
            string scope = tenantId != null ? " AND tenant_id = @tenant_id" : String.Empty;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                Action<SqlCommand> bind = cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@id", id);
                    if (tenantId != null) SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@is_built_in", true);
                };

                await SqlServerCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE fleet_actions SET active = @inactive, last_update_utc = @now WHERE id = @id" + scope + " AND is_built_in = @is_built_in;",
                    cmd =>
                    {
                        bind(cmd);
                        SqlServerCommandHelper.Add(cmd, "@inactive", false);
                        SqlServerCommandHelper.AddDate(cmd, "@now", DateTime.UtcNow);
                    }, token).ConfigureAwait(false);

                await SqlServerCommandHelper.ExecuteAsync(conn, tx,
                    "DELETE FROM fleet_actions WHERE id = @id" + scope + " AND is_built_in <> @is_built_in;",
                    bind, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        private static void Bind(SqlCommand cmd, FleetAction action)
        {
            SqlServerCommandHelper.Add(cmd, "@id", action.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", action.TenantId);
            SqlServerCommandHelper.Add(cmd, "@user_id", action.UserId);
            SqlServerCommandHelper.Add(cmd, "@name", action.Name);
            SqlServerCommandHelper.Add(cmd, "@description", action.Description);
            SqlServerCommandHelper.Add(cmd, "@kind", action.Kind.ToString());
            SqlServerCommandHelper.Add(cmd, "@command_text", action.CommandText);
            SqlServerCommandHelper.Add(cmd, "@prompt_template", action.PromptTemplate);
            SqlServerCommandHelper.Add(cmd, "@pipeline_id", action.PipelineId);
            SqlServerCommandHelper.Add(cmd, "@persona", action.Persona);
            SqlServerCommandHelper.Add(cmd, "@timeout_seconds", action.TimeoutSeconds);
            SqlServerCommandHelper.Add(cmd, "@default_concurrency", action.DefaultConcurrency);
            SqlServerCommandHelper.Add(cmd, "@requires_clean_working_tree", action.RequiresCleanWorkingTree);
            SqlServerCommandHelper.Add(cmd, "@is_built_in", action.IsBuiltIn);
            SqlServerCommandHelper.Add(cmd, "@built_in_key", action.BuiltInKey);
            SqlServerCommandHelper.Add(cmd, "@active", action.Active);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", action.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", action.LastUpdateUtc);
        }

        private static FleetAction FromReader(SqlDataReader reader)
        {
            FleetAction action = new FleetAction();
            action.Id = reader["id"].ToString()!;
            action.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            action.UserId = SqlServerCommandHelper.ReadString(reader["user_id"]);
            action.Name = SqlServerCommandHelper.ReadString(reader["name"]) ?? String.Empty;
            action.Description = SqlServerCommandHelper.ReadString(reader["description"]);
            action.Kind = SqlServerCommandHelper.ReadEnum(reader["kind"], FleetActionKindEnum.Command);
            action.CommandText = SqlServerCommandHelper.ReadString(reader["command_text"]);
            action.PromptTemplate = SqlServerCommandHelper.ReadString(reader["prompt_template"]);
            action.PipelineId = SqlServerCommandHelper.ReadString(reader["pipeline_id"]);
            action.Persona = SqlServerCommandHelper.ReadString(reader["persona"]);
            action.TimeoutSeconds = SqlServerCommandHelper.ReadInt(reader["timeout_seconds"], 300);
            action.DefaultConcurrency = SqlServerCommandHelper.ReadInt(reader["default_concurrency"], 4);
            action.RequiresCleanWorkingTree = SqlServerCommandHelper.ReadBool(reader["requires_clean_working_tree"], true);
            action.IsBuiltIn = SqlServerCommandHelper.ReadBool(reader["is_built_in"], false);
            action.BuiltInKey = SqlServerCommandHelper.ReadString(reader["built_in_key"]);
            action.Active = SqlServerCommandHelper.ReadBool(reader["active"], true);
            action.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            action.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return action;
        }

        #endregion
    }
}

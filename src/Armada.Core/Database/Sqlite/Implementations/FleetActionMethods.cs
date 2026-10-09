namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of fleet action definition persistence. Deleting a built-in action is a soft
    /// delete (Active = false); deleting a user-defined action removes the row.
    /// </summary>
    public class FleetActionMethods : IFleetActionMethods
    {
        #region Private-Members

        private readonly string _ConnectionString;
        private readonly SqliteWriteGate _WriteGate;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQLite database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public FleetActionMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
            _WriteGate = driver.WriteGate;
        }
        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<FleetAction> CreateAsync(FleetAction action, CancellationToken token = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (String.IsNullOrEmpty(action.Id)) action.Id = Constants.IdGenerator.GenerateKSortable(Constants.FleetActionIdPrefix, 24);
            action.LastUpdateUtc = DateTime.UtcNow;

            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO fleet_actions
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
            List<FleetAction> results = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_actions WHERE id = @id;",
                cmd => SqliteCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetAction?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<FleetAction> results = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_actions WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@id", id);
                },
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetAction?> ReadByBuiltInKeyAsync(string tenantId, string builtInKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(builtInKey)) throw new ArgumentNullException(nameof(builtInKey));
            List<FleetAction> results = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_actions WHERE tenant_id = @tenant_id AND built_in_key = @built_in_key ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@built_in_key", builtInKey);
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

            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, @"UPDATE fleet_actions SET
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

            Action<SqliteCommand> bind = cmd =>
            {
                SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                if (!includeInactive) SqliteCommandHelper.Add(cmd, "@active", true);
                if (query.CreatedAfter.HasValue) SqliteCommandHelper.AddDate(cmd, "@created_after", query.CreatedAfter.Value);
                if (query.CreatedBefore.HasValue) SqliteCommandHelper.AddDate(cmd, "@created_before", query.CreatedBefore.Value);
            };

            string where = " WHERE " + String.Join(" AND ", conditions);
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (SqliteConnection conn = new SqliteProviderConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqliteCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM fleet_actions" + where + ";", bind, token).ConfigureAwait(false);
                List<FleetAction> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM fleet_actions" + where + " ORDER BY created_utc " + direction + ", id " + direction + SqliteCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);
                return EnumerationResult<FleetAction>.Create(query, rows, total);
            }
        }

        #endregion

        #region Private-Methods

        private async Task DeleteInternalAsync(string? tenantId, string id, CancellationToken token)
        {
            string scope = tenantId != null ? " AND tenant_id = @tenant_id" : String.Empty;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                Action<SqliteCommand> bind = cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@id", id);
                    if (tenantId != null) SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@is_built_in", true);
                };

                await SqliteCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE fleet_actions SET active = @inactive, last_update_utc = @now WHERE id = @id" + scope + " AND is_built_in = @is_built_in;",
                    cmd =>
                    {
                        bind(cmd);
                        SqliteCommandHelper.Add(cmd, "@inactive", false);
                        SqliteCommandHelper.AddDate(cmd, "@now", DateTime.UtcNow);
                    }, token).ConfigureAwait(false);

                await SqliteCommandHelper.ExecuteAsync(conn, tx,
                    "DELETE FROM fleet_actions WHERE id = @id" + scope + " AND is_built_in <> @is_built_in;",
                    bind, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        private static void Bind(SqliteCommand cmd, FleetAction action)
        {
            SqliteCommandHelper.Add(cmd, "@id", action.Id);
            SqliteCommandHelper.Add(cmd, "@tenant_id", action.TenantId);
            SqliteCommandHelper.Add(cmd, "@user_id", action.UserId);
            SqliteCommandHelper.Add(cmd, "@name", action.Name);
            SqliteCommandHelper.Add(cmd, "@description", action.Description);
            SqliteCommandHelper.Add(cmd, "@kind", action.Kind.ToString());
            SqliteCommandHelper.Add(cmd, "@command_text", action.CommandText);
            SqliteCommandHelper.Add(cmd, "@prompt_template", action.PromptTemplate);
            SqliteCommandHelper.Add(cmd, "@pipeline_id", action.PipelineId);
            SqliteCommandHelper.Add(cmd, "@persona", action.Persona);
            SqliteCommandHelper.Add(cmd, "@timeout_seconds", action.TimeoutSeconds);
            SqliteCommandHelper.Add(cmd, "@default_concurrency", action.DefaultConcurrency);
            SqliteCommandHelper.Add(cmd, "@requires_clean_working_tree", action.RequiresCleanWorkingTree);
            SqliteCommandHelper.Add(cmd, "@is_built_in", action.IsBuiltIn);
            SqliteCommandHelper.Add(cmd, "@built_in_key", action.BuiltInKey);
            SqliteCommandHelper.Add(cmd, "@active", action.Active);
            SqliteCommandHelper.AddDate(cmd, "@created_utc", action.CreatedUtc);
            SqliteCommandHelper.AddDate(cmd, "@last_update_utc", action.LastUpdateUtc);
        }

        private static FleetAction FromReader(SqliteDataReader reader)
        {
            FleetAction action = new FleetAction();
            action.Id = reader["id"].ToString()!;
            action.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            action.UserId = SqliteCommandHelper.ReadString(reader["user_id"]);
            action.Name = SqliteCommandHelper.ReadString(reader["name"]) ?? String.Empty;
            action.Description = SqliteCommandHelper.ReadString(reader["description"]);
            action.Kind = SqliteCommandHelper.ReadEnum(reader["kind"], FleetActionKindEnum.Command);
            action.CommandText = SqliteCommandHelper.ReadString(reader["command_text"]);
            action.PromptTemplate = SqliteCommandHelper.ReadString(reader["prompt_template"]);
            action.PipelineId = SqliteCommandHelper.ReadString(reader["pipeline_id"]);
            action.Persona = SqliteCommandHelper.ReadString(reader["persona"]);
            action.TimeoutSeconds = SqliteCommandHelper.ReadInt(reader["timeout_seconds"], 300);
            action.DefaultConcurrency = SqliteCommandHelper.ReadInt(reader["default_concurrency"], 4);
            action.RequiresCleanWorkingTree = SqliteCommandHelper.ReadBool(reader["requires_clean_working_tree"], true);
            action.IsBuiltIn = SqliteCommandHelper.ReadBool(reader["is_built_in"], false);
            action.BuiltInKey = SqliteCommandHelper.ReadString(reader["built_in_key"]);
            action.Active = SqliteCommandHelper.ReadBool(reader["active"], true);
            action.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            action.LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"]);
            return action;
        }

        #endregion
    }
}

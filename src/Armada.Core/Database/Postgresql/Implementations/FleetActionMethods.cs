namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of fleet action definition persistence. Deleting a built-in action is a soft
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
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public FleetActionMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO fleet_actions
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
            List<FleetAction> results = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_actions WHERE id = @id;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetAction?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<FleetAction> results = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_actions WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@id", id);
                },
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetAction?> ReadByBuiltInKeyAsync(string tenantId, string builtInKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(builtInKey)) throw new ArgumentNullException(nameof(builtInKey));
            List<FleetAction> results = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_actions WHERE tenant_id = @tenant_id AND built_in_key = @built_in_key ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@built_in_key", builtInKey);
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

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, @"UPDATE fleet_actions SET
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

            Action<NpgsqlCommand> bind = cmd =>
            {
                PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                if (!includeInactive) PostgresqlCommandHelper.Add(cmd, "@active", true);
                if (query.CreatedAfter.HasValue) PostgresqlCommandHelper.AddDate(cmd, "@created_after", query.CreatedAfter.Value);
                if (query.CreatedBefore.HasValue) PostgresqlCommandHelper.AddDate(cmd, "@created_before", query.CreatedBefore.Value);
            };

            string where = " WHERE " + String.Join(" AND ", conditions);
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM fleet_actions" + where + ";", bind, token).ConfigureAwait(false);
                List<FleetAction> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM fleet_actions" + where + " ORDER BY created_utc " + direction + ", id " + direction + PostgresqlCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);
                return EnumerationResult<FleetAction>.Create(query, rows, total);
            }
        }

        #endregion

        #region Private-Methods

        private async Task DeleteInternalAsync(string? tenantId, string id, CancellationToken token)
        {
            string scope = tenantId != null ? " AND tenant_id = @tenant_id" : String.Empty;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                Action<NpgsqlCommand> bind = cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@id", id);
                    if (tenantId != null) PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@is_built_in", true);
                };

                await PostgresqlCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE fleet_actions SET active = @inactive, last_update_utc = @now WHERE id = @id" + scope + " AND is_built_in = @is_built_in;",
                    cmd =>
                    {
                        bind(cmd);
                        PostgresqlCommandHelper.Add(cmd, "@inactive", false);
                        PostgresqlCommandHelper.AddDate(cmd, "@now", DateTime.UtcNow);
                    }, token).ConfigureAwait(false);

                await PostgresqlCommandHelper.ExecuteAsync(conn, tx,
                    "DELETE FROM fleet_actions WHERE id = @id" + scope + " AND is_built_in <> @is_built_in;",
                    bind, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        private static void Bind(NpgsqlCommand cmd, FleetAction action)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", action.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", action.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@user_id", action.UserId);
            PostgresqlCommandHelper.Add(cmd, "@name", action.Name);
            PostgresqlCommandHelper.Add(cmd, "@description", action.Description);
            PostgresqlCommandHelper.Add(cmd, "@kind", action.Kind.ToString());
            PostgresqlCommandHelper.Add(cmd, "@command_text", action.CommandText);
            PostgresqlCommandHelper.Add(cmd, "@prompt_template", action.PromptTemplate);
            PostgresqlCommandHelper.Add(cmd, "@pipeline_id", action.PipelineId);
            PostgresqlCommandHelper.Add(cmd, "@persona", action.Persona);
            PostgresqlCommandHelper.Add(cmd, "@timeout_seconds", action.TimeoutSeconds);
            PostgresqlCommandHelper.Add(cmd, "@default_concurrency", action.DefaultConcurrency);
            PostgresqlCommandHelper.Add(cmd, "@requires_clean_working_tree", action.RequiresCleanWorkingTree);
            PostgresqlCommandHelper.Add(cmd, "@is_built_in", action.IsBuiltIn);
            PostgresqlCommandHelper.Add(cmd, "@built_in_key", action.BuiltInKey);
            PostgresqlCommandHelper.Add(cmd, "@active", action.Active);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", action.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", action.LastUpdateUtc);
        }

        private static FleetAction FromReader(NpgsqlDataReader reader)
        {
            FleetAction action = new FleetAction();
            action.Id = reader["id"].ToString()!;
            action.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            action.UserId = PostgresqlCommandHelper.ReadString(reader["user_id"]);
            action.Name = PostgresqlCommandHelper.ReadString(reader["name"]) ?? String.Empty;
            action.Description = PostgresqlCommandHelper.ReadString(reader["description"]);
            action.Kind = PostgresqlCommandHelper.ReadEnum(reader["kind"], FleetActionKindEnum.Command);
            action.CommandText = PostgresqlCommandHelper.ReadString(reader["command_text"]);
            action.PromptTemplate = PostgresqlCommandHelper.ReadString(reader["prompt_template"]);
            action.PipelineId = PostgresqlCommandHelper.ReadString(reader["pipeline_id"]);
            action.Persona = PostgresqlCommandHelper.ReadString(reader["persona"]);
            action.TimeoutSeconds = PostgresqlCommandHelper.ReadInt(reader["timeout_seconds"], 300);
            action.DefaultConcurrency = PostgresqlCommandHelper.ReadInt(reader["default_concurrency"], 4);
            action.RequiresCleanWorkingTree = PostgresqlCommandHelper.ReadBool(reader["requires_clean_working_tree"], true);
            action.IsBuiltIn = PostgresqlCommandHelper.ReadBool(reader["is_built_in"], false);
            action.BuiltInKey = PostgresqlCommandHelper.ReadString(reader["built_in_key"]);
            action.Active = PostgresqlCommandHelper.ReadBool(reader["active"], true);
            action.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            action.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return action;
        }

        #endregion
    }
}

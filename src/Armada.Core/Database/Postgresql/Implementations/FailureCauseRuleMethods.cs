namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of failure-cause rule persistence.
    /// </summary>
    public class FailureCauseRuleMethods : IFailureCauseRuleMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO failure_cause_rules
            (id, tenant_id, name, description, priority, enabled, is_built_in, match_json, cause, created_by_user_id, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @name, @description, @priority, @enabled, @is_built_in, @match_json, @cause, @created_by_user_id, @created_utc, @last_update_utc);";

        private static readonly string _Update = @"UPDATE failure_cause_rules SET
            name = @name, description = @description, priority = @priority, enabled = @enabled, match_json = @match_json,
            cause = @cause, created_by_user_id = @created_by_user_id, last_update_utc = @last_update_utc
            WHERE id = @id;";

        private static readonly JsonSerializerOptions _Json = CreateJsonOptions();

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
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public FailureCauseRuleMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<FailureCauseRule> CreateAsync(FailureCauseRule rule, CancellationToken token = default)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            rule.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, rule, true), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return rule;
        }

        /// <inheritdoc />
        public async Task<FailureCauseRule?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<FailureCauseRule> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM failure_cause_rules WHERE id = @id;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<FailureCauseRule> UpdateAsync(FailureCauseRule rule, CancellationToken token = default)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            rule.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, rule, false), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return rule;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM failure_cause_rules WHERE id = @id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted > 0;
        }

        /// <inheritdoc />
        public async Task<List<FailureCauseRule>> EnumerateAsync(string? tenantId, CancellationToken token = default)
        {
            string sql = tenantId != null
                ? "SELECT * FROM failure_cause_rules WHERE (tenant_id = @tenant_id OR tenant_id IS NULL) ORDER BY priority ASC, name ASC, id ASC;"
                : "SELECT * FROM failure_cause_rules ORDER BY priority ASC, name ASC, id ASC;";
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString, sql, cmd =>
            {
                if (tenantId != null) PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
            }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<FailureCauseRule>> EnumeratePageAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            int pageSize = query.PageSize < 1 ? 1 : (query.PageSize > 1000 ? 1000 : query.PageSize);
            int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            int offset = (pageNumber - 1) * pageSize;
            string where = tenantId != null ? " WHERE (tenant_id = @tenant_id OR tenant_id IS NULL)" : String.Empty;
            Action<NpgsqlCommand> bind = cmd =>
            {
                if (tenantId != null) PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
            };

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM failure_cause_rules" + where + ";", bind, token).ConfigureAwait(false);
                List<FailureCauseRule> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM failure_cause_rules" + where + " ORDER BY priority ASC, name ASC, id ASC" + PostgresqlCommandHelper.Page(offset, pageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<FailureCauseRule> result = new EnumerationResult<FailureCauseRule>();
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / pageSize);
                result.Objects = rows;
                return result;
            }
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions CreateJsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNameCaseInsensitive = true;
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private static string SerializeMatch(FailureCauseRuleMatch? match)
        {
            return JsonSerializer.Serialize(match ?? new FailureCauseRuleMatch(), _Json);
        }

        private static FailureCauseRuleMatch DeserializeMatch(object value)
        {
            string? json = PostgresqlCommandHelper.ReadString(value);
            if (String.IsNullOrEmpty(json)) return new FailureCauseRuleMatch();
            try
            {
                FailureCauseRuleMatch? match = JsonSerializer.Deserialize<FailureCauseRuleMatch>(json, _Json);
                return match ?? new FailureCauseRuleMatch();
            }
            catch (JsonException)
            {
                return new FailureCauseRuleMatch();
            }
        }

        private static void Bind(NpgsqlCommand cmd, FailureCauseRule rule, bool includeCreateColumns)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", rule.Id);
            if (includeCreateColumns)
            {
                PostgresqlCommandHelper.Add(cmd, "@tenant_id", rule.TenantId);
                PostgresqlCommandHelper.Add(cmd, "@is_built_in", rule.IsBuiltIn);
                PostgresqlCommandHelper.AddDate(cmd, "@created_utc", rule.CreatedUtc);
            }

            PostgresqlCommandHelper.Add(cmd, "@name", rule.Name);
            PostgresqlCommandHelper.Add(cmd, "@description", rule.Description);
            PostgresqlCommandHelper.Add(cmd, "@priority", rule.Priority);
            PostgresqlCommandHelper.Add(cmd, "@enabled", rule.Enabled);
            PostgresqlCommandHelper.Add(cmd, "@match_json", SerializeMatch(rule.Match));
            PostgresqlCommandHelper.Add(cmd, "@cause", rule.Cause.ToString());
            PostgresqlCommandHelper.Add(cmd, "@created_by_user_id", rule.CreatedByUserId);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", rule.LastUpdateUtc);
        }

        private static FailureCauseRule FromReader(NpgsqlDataReader reader)
        {
            FailureCauseRule rule = new FailureCauseRule();
            rule.Id = reader["id"].ToString()!;
            rule.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            rule.Name = reader["name"]?.ToString() ?? String.Empty;
            rule.Description = PostgresqlCommandHelper.ReadString(reader["description"]);
            rule.Priority = PostgresqlCommandHelper.ReadInt(reader["priority"], 100);
            rule.Enabled = PostgresqlCommandHelper.ReadBool(reader["enabled"], true);
            rule.IsBuiltIn = PostgresqlCommandHelper.ReadBool(reader["is_built_in"], false);
            rule.Match = DeserializeMatch(reader["match_json"]);
            rule.Cause = PostgresqlCommandHelper.ReadEnum(reader["cause"], FailureCauseEnum.Unknown);
            rule.CreatedByUserId = PostgresqlCommandHelper.ReadString(reader["created_by_user_id"]);
            rule.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            rule.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return rule;
        }

        #endregion
    }
}

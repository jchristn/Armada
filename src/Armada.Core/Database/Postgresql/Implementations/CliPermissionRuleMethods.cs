namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
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
    /// PostgreSQL implementation of CLI permission rule persistence.
    /// </summary>
    public class CliPermissionRuleMethods : ICliPermissionRuleMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO cli_permission_rules
            (id, tenant_id, scope, vessel_id, captain_id, pattern, action, description, created_by_user_id, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @scope, @vessel_id, @captain_id, @pattern, @action, @description, @created_by_user_id, @created_utc, @last_update_utc);";

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
        public CliPermissionRuleMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<CliPermissionRule> CreateAsync(CliPermissionRule rule, CancellationToken token = default)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            rule.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, rule), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return rule;
        }

        /// <inheritdoc />
        public async Task<CliPermissionRule?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<CliPermissionRule> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM cli_permission_rules WHERE id = @id;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<CliPermissionRule> UpdateAsync(CliPermissionRule rule, CancellationToken token = default)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            rule.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE cli_permission_rules SET pattern = @pattern, action = @action, description = @description, last_update_utc = @last_update_utc WHERE id = @id;",
                    cmd =>
                    {
                        PostgresqlCommandHelper.Add(cmd, "@pattern", rule.Pattern);
                        PostgresqlCommandHelper.Add(cmd, "@action", rule.Action.ToString());
                        PostgresqlCommandHelper.Add(cmd, "@description", rule.Description);
                        PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", rule.LastUpdateUtc);
                        PostgresqlCommandHelper.Add(cmd, "@id", rule.Id);
                    }, token).ConfigureAwait(false);
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
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM cli_permission_rules WHERE id = @id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted > 0;
        }

        /// <inheritdoc />
        public async Task<List<CliPermissionRule>> EnumerateAsync(CliPermissionRuleQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            StringBuilder sql = new StringBuilder("SELECT * FROM cli_permission_rules WHERE 1 = 1");
            if (query.TenantId != null) sql.Append(" AND (tenant_id = @tenant_id OR tenant_id IS NULL)");
            if (query.Scope.HasValue) sql.Append(" AND scope = @scope");
            if (query.VesselId != null) sql.Append(" AND vessel_id = @vessel_id");
            if (query.CaptainId != null) sql.Append(" AND captain_id = @captain_id");
            sql.Append(" ORDER BY created_utc DESC, id DESC;");

            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString, sql.ToString(), cmd =>
            {
                if (query.TenantId != null) PostgresqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.Scope.HasValue) PostgresqlCommandHelper.Add(cmd, "@scope", query.Scope.Value.ToString());
                if (query.VesselId != null) PostgresqlCommandHelper.Add(cmd, "@vessel_id", query.VesselId);
                if (query.CaptainId != null) PostgresqlCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
            }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<CliPermissionRule>> EnumerateApplicableAsync(string? tenantId, string? captainId, string? vesselId, CancellationToken token = default)
        {
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                @"SELECT * FROM cli_permission_rules
                  WHERE (tenant_id IS NULL OR tenant_id = @tenant_id)
                    AND (scope = @global OR (scope = @vessel AND vessel_id = @vessel_id) OR (scope = @captain AND captain_id = @captain_id))
                  ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId ?? String.Empty);
                    PostgresqlCommandHelper.Add(cmd, "@global", CliPermissionRuleScopeEnum.Global.ToString());
                    PostgresqlCommandHelper.Add(cmd, "@vessel", CliPermissionRuleScopeEnum.Vessel.ToString());
                    PostgresqlCommandHelper.Add(cmd, "@captain", CliPermissionRuleScopeEnum.Captain.ToString());
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", vesselId ?? String.Empty);
                    PostgresqlCommandHelper.Add(cmd, "@captain_id", captainId ?? String.Empty);
                }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, CliPermissionRule rule)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", rule.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", rule.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@scope", rule.Scope.ToString());
            PostgresqlCommandHelper.Add(cmd, "@vessel_id", rule.VesselId);
            PostgresqlCommandHelper.Add(cmd, "@captain_id", rule.CaptainId);
            PostgresqlCommandHelper.Add(cmd, "@pattern", rule.Pattern);
            PostgresqlCommandHelper.Add(cmd, "@action", rule.Action.ToString());
            PostgresqlCommandHelper.Add(cmd, "@description", rule.Description);
            PostgresqlCommandHelper.Add(cmd, "@created_by_user_id", rule.CreatedByUserId);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", rule.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", rule.LastUpdateUtc);
        }

        private static CliPermissionRule FromReader(NpgsqlDataReader reader)
        {
            CliPermissionRule rule = new CliPermissionRule();
            rule.Id = reader["id"].ToString()!;
            rule.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            rule.Scope = PostgresqlCommandHelper.ReadEnum(reader["scope"], CliPermissionRuleScopeEnum.Global);
            rule.VesselId = PostgresqlCommandHelper.ReadString(reader["vessel_id"]);
            rule.CaptainId = PostgresqlCommandHelper.ReadString(reader["captain_id"]);
            rule.Pattern = reader["pattern"]?.ToString() ?? String.Empty;
            rule.Action = PostgresqlCommandHelper.ReadEnum(reader["action"], CliPermissionRuleActionEnum.Deny);
            rule.Description = PostgresqlCommandHelper.ReadString(reader["description"]);
            rule.CreatedByUserId = PostgresqlCommandHelper.ReadString(reader["created_by_user_id"]);
            rule.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            rule.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return rule;
        }

        #endregion
    }
}

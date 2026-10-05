namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of CLI permission rule persistence.
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
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public CliPermissionRuleMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<CliPermissionRule> CreateAsync(CliPermissionRule rule, CancellationToken token = default)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            rule.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, rule), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return rule;
        }

        /// <inheritdoc />
        public async Task<CliPermissionRule?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<CliPermissionRule> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM cli_permission_rules WHERE id = @id;",
                cmd => MysqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<CliPermissionRule> UpdateAsync(CliPermissionRule rule, CancellationToken token = default)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            rule.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE cli_permission_rules SET pattern = @pattern, action = @action, description = @description, last_update_utc = @last_update_utc WHERE id = @id;",
                    cmd =>
                    {
                        MysqlCommandHelper.Add(cmd, "@pattern", rule.Pattern);
                        MysqlCommandHelper.Add(cmd, "@action", rule.Action.ToString());
                        MysqlCommandHelper.Add(cmd, "@description", rule.Description);
                        MysqlCommandHelper.AddDate(cmd, "@last_update_utc", rule.LastUpdateUtc);
                        MysqlCommandHelper.Add(cmd, "@id", rule.Id);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return rule;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM cli_permission_rules WHERE id = @id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
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

            return await MysqlCommandHelper.QueryAsync(_ConnectionString, sql.ToString(), cmd =>
            {
                if (query.TenantId != null) MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.Scope.HasValue) MysqlCommandHelper.Add(cmd, "@scope", query.Scope.Value.ToString());
                if (query.VesselId != null) MysqlCommandHelper.Add(cmd, "@vessel_id", query.VesselId);
                if (query.CaptainId != null) MysqlCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
            }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<CliPermissionRule>> EnumerateApplicableAsync(string? tenantId, string? captainId, string? vesselId, CancellationToken token = default)
        {
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                @"SELECT * FROM cli_permission_rules
                  WHERE (tenant_id IS NULL OR tenant_id = @tenant_id)
                    AND (scope = @global OR (scope = @vessel AND vessel_id = @vessel_id) OR (scope = @captain AND captain_id = @captain_id))
                  ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId ?? String.Empty);
                    MysqlCommandHelper.Add(cmd, "@global", CliPermissionRuleScopeEnum.Global.ToString());
                    MysqlCommandHelper.Add(cmd, "@vessel", CliPermissionRuleScopeEnum.Vessel.ToString());
                    MysqlCommandHelper.Add(cmd, "@captain", CliPermissionRuleScopeEnum.Captain.ToString());
                    MysqlCommandHelper.Add(cmd, "@vessel_id", vesselId ?? String.Empty);
                    MysqlCommandHelper.Add(cmd, "@captain_id", captainId ?? String.Empty);
                }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, CliPermissionRule rule)
        {
            MysqlCommandHelper.Add(cmd, "@id", rule.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", rule.TenantId);
            MysqlCommandHelper.Add(cmd, "@scope", rule.Scope.ToString());
            MysqlCommandHelper.Add(cmd, "@vessel_id", rule.VesselId);
            MysqlCommandHelper.Add(cmd, "@captain_id", rule.CaptainId);
            MysqlCommandHelper.Add(cmd, "@pattern", rule.Pattern);
            MysqlCommandHelper.Add(cmd, "@action", rule.Action.ToString());
            MysqlCommandHelper.Add(cmd, "@description", rule.Description);
            MysqlCommandHelper.Add(cmd, "@created_by_user_id", rule.CreatedByUserId);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", rule.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", rule.LastUpdateUtc);
        }

        private static CliPermissionRule FromReader(MySqlDataReader reader)
        {
            CliPermissionRule rule = new CliPermissionRule();
            rule.Id = reader["id"].ToString()!;
            rule.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            rule.Scope = MysqlCommandHelper.ReadEnum(reader["scope"], CliPermissionRuleScopeEnum.Global);
            rule.VesselId = MysqlCommandHelper.ReadString(reader["vessel_id"]);
            rule.CaptainId = MysqlCommandHelper.ReadString(reader["captain_id"]);
            rule.Pattern = reader["pattern"]?.ToString() ?? String.Empty;
            rule.Action = MysqlCommandHelper.ReadEnum(reader["action"], CliPermissionRuleActionEnum.Deny);
            rule.Description = MysqlCommandHelper.ReadString(reader["description"]);
            rule.CreatedByUserId = MysqlCommandHelper.ReadString(reader["created_by_user_id"]);
            rule.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            rule.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return rule;
        }

        #endregion
    }
}

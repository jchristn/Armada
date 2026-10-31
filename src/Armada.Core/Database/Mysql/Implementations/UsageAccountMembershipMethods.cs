namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Database.Mysql.Queries;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// MySQL implementation of usage account membership persistence: effective-dated attribution periods
    /// connecting captains to the account group they captured through. Closing a membership only stamps
    /// its effective-to time; history rows are never rewritten.
    /// </summary>
    public class UsageAccountMembershipMethods : IUsageAccountMembershipMethods
    {
        #region Private-Members

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public UsageAccountMembershipMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<UsageAccountMembership> CreateAsync(UsageAccountMembership membership, CancellationToken token = default)
        {
            if (membership == null) throw new ArgumentNullException(nameof(membership));
            if (String.IsNullOrEmpty(membership.TenantId)) throw new ArgumentNullException(nameof(membership), "Membership tenant id is required.");
            if (String.IsNullOrEmpty(membership.CaptainId)) throw new ArgumentNullException(nameof(membership), "Membership captain id is required.");
            if (String.IsNullOrEmpty(membership.HostId)) throw new ArgumentNullException(nameof(membership), "Membership host id is required.");
            if (String.IsNullOrEmpty(membership.AccountKey)) throw new ArgumentNullException(nameof(membership), "Membership account key is required.");
            membership.LastUpdateUtc = DateTime.UtcNow;

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.MembershipInsert, cmd => BindMembership(cmd, membership), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return membership;
        }

        /// <inheritdoc />
        public async Task<UsageAccountMembership?> ReadAsync(string id, string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            List<UsageAccountMembership> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.MembershipSelectById,
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@id", id);
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<List<UsageAccountMembership>> ReadActiveAsync(string tenantId, string captainId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));

            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM usage_account_memberships WHERE tenant_id = @tenant_id AND captain_id = @captain_id AND effective_to_utc IS NULL ORDER BY effective_from_utc DESC, id DESC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@captain_id", captainId);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> CloseAsync(string tenantId, string captainId, DateTime effectiveToUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));

            int closed = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                closed = await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.MembershipCloseOpen, cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@captain_id", captainId);
                    MysqlCommandHelper.AddDate(cmd, "@effective_to_utc", effectiveToUtc);
                    MysqlCommandHelper.AddDate(cmd, "@last_update_utc", DateTime.UtcNow);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return closed;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<UsageAccountMembership>> EnumerateAsync(UsageMembershipQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            int pageSize = query.PageSize < 1 ? 1 : (query.PageSize > 1000 ? 1000 : query.PageSize);
            int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            int offset = (pageNumber - 1) * pageSize;

            List<string> conditions = new List<string>();
            Action<MySqlCommand> bind = cmd => { };

            if (query.TenantId != null)
            {
                conditions.Add("tenant_id = @tenant_id");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
            }
            if (query.CaptainId != null)
            {
                conditions.Add("captain_id = @captain_id");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
            }
            if (query.HostId != null)
            {
                conditions.Add("host_id = @host_id");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@host_id", query.HostId);
            }
            if (query.Runtime.HasValue)
            {
                conditions.Add("runtime = @runtime");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@runtime", query.Runtime.Value.ToString());
            }
            if (query.AccountKey != null)
            {
                conditions.Add("account_key = @account_key");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@account_key", query.AccountKey);
            }
            if (query.OpenOnly)
            {
                conditions.Add("effective_to_utc IS NULL");
            }
            if (query.EffectiveAtUtc.HasValue)
            {
                conditions.Add("effective_from_utc <= @effective_at");
                conditions.Add("(effective_to_utc IS NULL OR effective_to_utc > @effective_at)");
                bind += cmd => MysqlCommandHelper.AddDate(cmd, "@effective_at", query.EffectiveAtUtc.Value);
            }

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM usage_account_memberships" + where + ";", bind, token).ConfigureAwait(false);
                List<UsageAccountMembership> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM usage_account_memberships" + where + " ORDER BY effective_from_utc DESC, id DESC" + MysqlCommandHelper.Page(offset, pageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<UsageAccountMembership> result = new EnumerationResult<UsageAccountMembership>();
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / pageSize);
                result.Objects = rows;
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.MembershipDeleteByTenant, cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void BindMembership(MySqlCommand cmd, UsageAccountMembership membership)
        {
            MysqlCommandHelper.Add(cmd, "@id", membership.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", membership.TenantId);
            MysqlCommandHelper.Add(cmd, "@captain_id", membership.CaptainId);
            MysqlCommandHelper.Add(cmd, "@host_id", membership.HostId);
            MysqlCommandHelper.Add(cmd, "@runtime", membership.Runtime.ToString());
            MysqlCommandHelper.Add(cmd, "@provider", String.IsNullOrEmpty(membership.Provider) ? null : membership.Provider);
            MysqlCommandHelper.Add(cmd, "@account_key", membership.AccountKey);
            MysqlCommandHelper.Add(cmd, "@configuration_revision", String.IsNullOrEmpty(membership.ConfigurationRevision) ? null : membership.ConfigurationRevision);
            MysqlCommandHelper.AddDate(cmd, "@effective_from_utc", membership.EffectiveFromUtc);
            MysqlCommandHelper.AddDate(cmd, "@effective_to_utc", membership.EffectiveToUtc);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", membership.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", membership.LastUpdateUtc);
        }

        private static UsageAccountMembership FromReader(MySqlDataReader reader)
        {
            UsageAccountMembership membership = new UsageAccountMembership();
            membership.Id = reader["id"].ToString()!;
            membership.TenantId = reader["tenant_id"].ToString()!;
            membership.CaptainId = reader["captain_id"].ToString()!;
            membership.HostId = reader["host_id"].ToString()!;
            membership.Runtime = MysqlCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            membership.Provider = MysqlCommandHelper.ReadString(reader["provider"]);
            membership.AccountKey = reader["account_key"].ToString()!;
            membership.ConfigurationRevision = MysqlCommandHelper.ReadString(reader["configuration_revision"]);
            membership.EffectiveFromUtc = MysqlCommandHelper.ReadDate(reader["effective_from_utc"]);
            membership.EffectiveToUtc = MysqlCommandHelper.ReadNullableDate(reader["effective_to_utc"]);
            membership.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            membership.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return membership;
        }

        #endregion
    }
}
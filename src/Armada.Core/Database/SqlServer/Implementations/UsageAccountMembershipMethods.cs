namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Database.SqlServer.Queries;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQL Server implementation of usage account membership persistence: effective-dated attribution periods
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public UsageAccountMembershipMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<UsageAccountMembership> CreateAsync(UsageAccountMembership membership, CancellationToken token = default)
        {
            if (membership == null) throw new ArgumentNullException(nameof(membership));
            if (String.IsNullOrEmpty(membership.TenantId)) throw new ArgumentNullException(nameof(membership), "Membership tenant id is required.");
            if (String.IsNullOrEmpty(membership.CaptainId)) throw new ArgumentNullException(nameof(membership), "Membership captain id is required.");
            if (String.IsNullOrEmpty(membership.HostId)) throw new ArgumentNullException(nameof(membership), "Membership host id is required.");
            if (String.IsNullOrEmpty(membership.AccountKey)) throw new ArgumentNullException(nameof(membership), "Membership account key is required.");
            membership.LastUpdateUtc = DateTime.UtcNow;

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.MembershipInsert, cmd => BindMembership(cmd, membership), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return membership;
        }

        /// <inheritdoc />
        public async Task<UsageAccountMembership?> ReadAsync(string id, string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            List<UsageAccountMembership> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.MembershipSelectById,
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@id", id);
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<List<UsageAccountMembership>> ReadActiveAsync(string tenantId, string captainId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));

            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM usage_account_memberships WHERE tenant_id = @tenant_id AND captain_id = @captain_id AND effective_to_utc IS NULL ORDER BY effective_from_utc DESC, id DESC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@captain_id", captainId);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> CloseAsync(string tenantId, string captainId, DateTime effectiveToUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));

            int closed = 0;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                closed = await SqlServerCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.MembershipCloseOpen, cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@captain_id", captainId);
                    SqlServerCommandHelper.AddDate(cmd, "@effective_to_utc", effectiveToUtc);
                    SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", DateTime.UtcNow);
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
            Action<SqlCommand> bind = cmd => { };

            if (query.TenantId != null)
            {
                conditions.Add("tenant_id = @tenant_id");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
            }
            if (query.CaptainId != null)
            {
                conditions.Add("captain_id = @captain_id");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
            }
            if (query.HostId != null)
            {
                conditions.Add("host_id = @host_id");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@host_id", query.HostId);
            }
            if (query.Runtime.HasValue)
            {
                conditions.Add("runtime = @runtime");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@runtime", query.Runtime.Value.ToString());
            }
            if (query.AccountKey != null)
            {
                conditions.Add("account_key = @account_key");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@account_key", query.AccountKey);
            }
            if (query.OpenOnly)
            {
                conditions.Add("effective_to_utc IS NULL");
            }
            if (query.EffectiveAtUtc.HasValue)
            {
                conditions.Add("effective_from_utc <= @effective_at");
                conditions.Add("(effective_to_utc IS NULL OR effective_to_utc > @effective_at)");
                bind += cmd => SqlServerCommandHelper.AddDate(cmd, "@effective_at", query.EffectiveAtUtc.Value);
            }

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM usage_account_memberships" + where + ";", bind, token).ConfigureAwait(false);
                List<UsageAccountMembership> rows = await SqlServerCommandHelper.QueryAsync(conn,
                    "SELECT * FROM usage_account_memberships" + where + " ORDER BY effective_from_utc DESC, id DESC" + SqlServerCommandHelper.Page(offset, pageSize) + ";",
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
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                deleted = await SqlServerCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.MembershipDeleteByTenant, cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void BindMembership(SqlCommand cmd, UsageAccountMembership membership)
        {
            SqlServerCommandHelper.Add(cmd, "@id", membership.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", membership.TenantId);
            SqlServerCommandHelper.Add(cmd, "@captain_id", membership.CaptainId);
            SqlServerCommandHelper.Add(cmd, "@host_id", membership.HostId);
            SqlServerCommandHelper.Add(cmd, "@runtime", membership.Runtime.ToString());
            SqlServerCommandHelper.Add(cmd, "@provider", String.IsNullOrEmpty(membership.Provider) ? null : membership.Provider);
            SqlServerCommandHelper.Add(cmd, "@account_key", membership.AccountKey);
            SqlServerCommandHelper.Add(cmd, "@configuration_revision", String.IsNullOrEmpty(membership.ConfigurationRevision) ? null : membership.ConfigurationRevision);
            SqlServerCommandHelper.AddDate(cmd, "@effective_from_utc", membership.EffectiveFromUtc);
            SqlServerCommandHelper.AddDate(cmd, "@effective_to_utc", membership.EffectiveToUtc);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", membership.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", membership.LastUpdateUtc);
        }

        private static UsageAccountMembership FromReader(SqlDataReader reader)
        {
            UsageAccountMembership membership = new UsageAccountMembership();
            membership.Id = reader["id"].ToString()!;
            membership.TenantId = reader["tenant_id"].ToString()!;
            membership.CaptainId = reader["captain_id"].ToString()!;
            membership.HostId = reader["host_id"].ToString()!;
            membership.Runtime = SqlServerCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            membership.Provider = SqlServerCommandHelper.ReadString(reader["provider"]);
            membership.AccountKey = reader["account_key"].ToString()!;
            membership.ConfigurationRevision = SqlServerCommandHelper.ReadString(reader["configuration_revision"]);
            membership.EffectiveFromUtc = SqlServerCommandHelper.ReadDate(reader["effective_from_utc"]);
            membership.EffectiveToUtc = SqlServerCommandHelper.ReadNullableDate(reader["effective_to_utc"]);
            membership.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            membership.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return membership;
        }

        #endregion
    }
}
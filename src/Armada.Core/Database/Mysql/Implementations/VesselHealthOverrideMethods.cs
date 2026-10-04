namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of manual vessel health override persistence (unique per tenant, vessel, and
    /// criterion).
    /// </summary>
    public class VesselHealthOverrideMethods : IVesselHealthOverrideMethods
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
        public VesselHealthOverrideMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }
        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<VesselHealthOverride> UpsertAsync(VesselHealthOverride healthOverride, CancellationToken token = default)
        {
            if (healthOverride == null) throw new ArgumentNullException(nameof(healthOverride));
            if (String.IsNullOrEmpty(healthOverride.VesselId)) throw new ArgumentException("VesselId is required.", nameof(healthOverride));
            if (String.IsNullOrEmpty(healthOverride.TenantId)) throw new ArgumentException("TenantId is required.", nameof(healthOverride));
            healthOverride.LastUpdateUtc = DateTime.UtcNow;

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                List<VesselHealthOverride> existing = await MysqlCommandHelper.QueryAsync(conn, tx,
                    "SELECT * FROM vessel_health_overrides WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id AND criterion = @criterion;",
                    cmd => Bind(cmd, healthOverride),
                    FromReader, token).ConfigureAwait(false);

                if (existing.Count > 0)
                {
                    healthOverride.Id = existing[0].Id;
                    healthOverride.CreatedUtc = existing[0].CreatedUtc;
                    await MysqlCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_health_overrides SET
                        user_id = @user_id, status = @status, note = @note, last_update_utc = @last_update_utc
                        WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id AND criterion = @criterion;",
                        cmd => Bind(cmd, healthOverride), token).ConfigureAwait(false);
                }
                else
                {
                    if (String.IsNullOrEmpty(healthOverride.Id)) healthOverride.Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselHealthOverrideIdPrefix, 24);
                    await MysqlCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO vessel_health_overrides
                        (id, tenant_id, vessel_id, user_id, criterion, status, note, created_utc, last_update_utc)
                        VALUES
                        (@id, @tenant_id, @vessel_id, @user_id, @criterion, @status, @note, @created_utc, @last_update_utc);",
                        cmd => Bind(cmd, healthOverride), token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);

            return healthOverride;
        }

        /// <inheritdoc />
        public async Task<List<VesselHealthOverride>> ReadByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_health_overrides WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id ORDER BY criterion ASC, id ASC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string vesselId, VesselHealthCriterionEnum criterion, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_overrides WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id AND criterion = @criterion;", cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                    MysqlCommandHelper.Add(cmd, "@criterion", criterion.ToString());
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_overrides WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, VesselHealthOverride healthOverride)
        {
            MysqlCommandHelper.Add(cmd, "@id", healthOverride.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", healthOverride.TenantId);
            MysqlCommandHelper.Add(cmd, "@vessel_id", healthOverride.VesselId);
            MysqlCommandHelper.Add(cmd, "@user_id", healthOverride.UserId);
            MysqlCommandHelper.Add(cmd, "@criterion", healthOverride.Criterion.ToString());
            MysqlCommandHelper.Add(cmd, "@status", healthOverride.Status.ToString());
            MysqlCommandHelper.Add(cmd, "@note", healthOverride.Note);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", healthOverride.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", healthOverride.LastUpdateUtc);
        }

        private static VesselHealthOverride FromReader(MySqlDataReader reader)
        {
            VesselHealthOverride healthOverride = new VesselHealthOverride();
            healthOverride.Id = reader["id"].ToString()!;
            healthOverride.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            healthOverride.VesselId = MysqlCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            healthOverride.UserId = MysqlCommandHelper.ReadString(reader["user_id"]);
            healthOverride.Criterion = MysqlCommandHelper.ReadEnum(reader["criterion"], VesselHealthCriterionEnum.Overall);
            healthOverride.Status = MysqlCommandHelper.ReadEnum(reader["status"], VesselHealthStatusEnum.Unknown);
            healthOverride.Note = MysqlCommandHelper.ReadString(reader["note"]);
            healthOverride.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            healthOverride.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return healthOverride;
        }

        #endregion
    }
}

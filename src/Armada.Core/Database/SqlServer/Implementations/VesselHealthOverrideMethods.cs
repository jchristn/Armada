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
    /// SQL Server implementation of manual vessel health override persistence (unique per tenant, vessel, and
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public VesselHealthOverrideMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<VesselHealthOverride> UpsertAsync(VesselHealthOverride healthOverride, CancellationToken token = default)
        {
            if (healthOverride == null) throw new ArgumentNullException(nameof(healthOverride));
            if (String.IsNullOrEmpty(healthOverride.VesselId)) throw new ArgumentException("VesselId is required.", nameof(healthOverride));
            if (String.IsNullOrEmpty(healthOverride.TenantId)) throw new ArgumentException("TenantId is required.", nameof(healthOverride));
            healthOverride.LastUpdateUtc = DateTime.UtcNow;

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                List<VesselHealthOverride> existing = await SqlServerCommandHelper.QueryAsync(conn, tx,
                    "SELECT * FROM vessel_health_overrides WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id AND criterion = @criterion;",
                    cmd => Bind(cmd, healthOverride),
                    FromReader, token).ConfigureAwait(false);

                if (existing.Count > 0)
                {
                    healthOverride.Id = existing[0].Id;
                    healthOverride.CreatedUtc = existing[0].CreatedUtc;
                    await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_health_overrides SET
                        user_id = @user_id, status = @status, note = @note, last_update_utc = @last_update_utc
                        WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id AND criterion = @criterion;",
                        cmd => Bind(cmd, healthOverride), token).ConfigureAwait(false);
                }
                else
                {
                    if (String.IsNullOrEmpty(healthOverride.Id)) healthOverride.Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselHealthOverrideIdPrefix, 24);
                    await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO vessel_health_overrides
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
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_health_overrides WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id ORDER BY criterion ASC, id ASC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string vesselId, VesselHealthCriterionEnum criterion, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_overrides WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id AND criterion = @criterion;", cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@vessel_id", vesselId);
                    SqlServerCommandHelper.Add(cmd, "@criterion", criterion.ToString());
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_overrides WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, VesselHealthOverride healthOverride)
        {
            SqlServerCommandHelper.Add(cmd, "@id", healthOverride.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", healthOverride.TenantId);
            SqlServerCommandHelper.Add(cmd, "@vessel_id", healthOverride.VesselId);
            SqlServerCommandHelper.Add(cmd, "@user_id", healthOverride.UserId);
            SqlServerCommandHelper.Add(cmd, "@criterion", healthOverride.Criterion.ToString());
            SqlServerCommandHelper.Add(cmd, "@status", healthOverride.Status.ToString());
            SqlServerCommandHelper.Add(cmd, "@note", healthOverride.Note);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", healthOverride.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", healthOverride.LastUpdateUtc);
        }

        private static VesselHealthOverride FromReader(SqlDataReader reader)
        {
            VesselHealthOverride healthOverride = new VesselHealthOverride();
            healthOverride.Id = reader["id"].ToString()!;
            healthOverride.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            healthOverride.VesselId = SqlServerCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            healthOverride.UserId = SqlServerCommandHelper.ReadString(reader["user_id"]);
            healthOverride.Criterion = SqlServerCommandHelper.ReadEnum(reader["criterion"], VesselHealthCriterionEnum.Overall);
            healthOverride.Status = SqlServerCommandHelper.ReadEnum(reader["status"], VesselHealthStatusEnum.Unknown);
            healthOverride.Note = SqlServerCommandHelper.ReadString(reader["note"]);
            healthOverride.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            healthOverride.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return healthOverride;
        }

        #endregion
    }
}

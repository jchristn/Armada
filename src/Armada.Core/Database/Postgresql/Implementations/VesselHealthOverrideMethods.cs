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
    /// PostgreSQL implementation of manual vessel health override persistence (unique per tenant, vessel, and
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
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public VesselHealthOverrideMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                List<VesselHealthOverride> existing = await PostgresqlCommandHelper.QueryAsync(conn, tx,
                    "SELECT * FROM vessel_health_overrides WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id AND criterion = @criterion;",
                    cmd => Bind(cmd, healthOverride),
                    FromReader, token).ConfigureAwait(false);

                if (existing.Count > 0)
                {
                    healthOverride.Id = existing[0].Id;
                    healthOverride.CreatedUtc = existing[0].CreatedUtc;
                    await PostgresqlCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_health_overrides SET
                        user_id = @user_id, status = @status, note = @note, last_update_utc = @last_update_utc
                        WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id AND criterion = @criterion;",
                        cmd => Bind(cmd, healthOverride), token).ConfigureAwait(false);
                }
                else
                {
                    if (String.IsNullOrEmpty(healthOverride.Id)) healthOverride.Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselHealthOverrideIdPrefix, 24);
                    await PostgresqlCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO vessel_health_overrides
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
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_health_overrides WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id ORDER BY criterion ASC, id ASC;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string vesselId, VesselHealthCriterionEnum criterion, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_overrides WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id AND criterion = @criterion;", cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                    PostgresqlCommandHelper.Add(cmd, "@criterion", criterion.ToString());
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_overrides WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, VesselHealthOverride healthOverride)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", healthOverride.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", healthOverride.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@vessel_id", healthOverride.VesselId);
            PostgresqlCommandHelper.Add(cmd, "@user_id", healthOverride.UserId);
            PostgresqlCommandHelper.Add(cmd, "@criterion", healthOverride.Criterion.ToString());
            PostgresqlCommandHelper.Add(cmd, "@status", healthOverride.Status.ToString());
            PostgresqlCommandHelper.Add(cmd, "@note", healthOverride.Note);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", healthOverride.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", healthOverride.LastUpdateUtc);
        }

        private static VesselHealthOverride FromReader(NpgsqlDataReader reader)
        {
            VesselHealthOverride healthOverride = new VesselHealthOverride();
            healthOverride.Id = reader["id"].ToString()!;
            healthOverride.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            healthOverride.VesselId = PostgresqlCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            healthOverride.UserId = PostgresqlCommandHelper.ReadString(reader["user_id"]);
            healthOverride.Criterion = PostgresqlCommandHelper.ReadEnum(reader["criterion"], VesselHealthCriterionEnum.Overall);
            healthOverride.Status = PostgresqlCommandHelper.ReadEnum(reader["status"], VesselHealthStatusEnum.Unknown);
            healthOverride.Note = PostgresqlCommandHelper.ReadString(reader["note"]);
            healthOverride.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            healthOverride.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return healthOverride;
        }

        #endregion
    }
}

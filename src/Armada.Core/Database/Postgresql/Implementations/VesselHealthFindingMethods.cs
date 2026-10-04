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
    /// PostgreSQL implementation of vessel health finding persistence.
    /// </summary>
    public class VesselHealthFindingMethods : IVesselHealthFindingMethods
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
        public VesselHealthFindingMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task ReplaceForVesselAsync(string tenantId, string vesselId, List<VesselHealthFinding> findings, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            if (findings == null) throw new ArgumentNullException(nameof(findings));

            DateTime now = DateTime.UtcNow;
            foreach (VesselHealthFinding finding in findings)
            {
                if (finding == null) throw new ArgumentException("Findings must not contain null entries.", nameof(findings));
                if (String.IsNullOrEmpty(finding.Id)) finding.Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselHealthFindingIdPrefix, 24);
                finding.TenantId = tenantId;
                finding.VesselId = vesselId;
                finding.LastUpdateUtc = now;
            }

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_findings WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);

                foreach (VesselHealthFinding finding in findings)
                {
                    token.ThrowIfCancellationRequested();
                    await PostgresqlCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO vessel_health_findings
                        (id, tenant_id, vessel_id, criterion, status, detail_code, value_a, value_b, evaluated_utc, created_utc, last_update_utc)
                        VALUES
                        (@id, @tenant_id, @vessel_id, @criterion, @status, @detail_code, @value_a, @value_b, @evaluated_utc, @created_utc, @last_update_utc);",
                        cmd => Bind(cmd, finding), token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<VesselHealthFinding>> ReadByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_health_findings WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id ORDER BY criterion ASC, id ASC;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_findings WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, VesselHealthFinding finding)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", finding.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", finding.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@vessel_id", finding.VesselId);
            PostgresqlCommandHelper.Add(cmd, "@criterion", finding.Criterion.ToString());
            PostgresqlCommandHelper.Add(cmd, "@status", finding.Status.ToString());
            PostgresqlCommandHelper.Add(cmd, "@detail_code", finding.DetailCode);
            PostgresqlCommandHelper.Add(cmd, "@value_a", finding.ValueA);
            PostgresqlCommandHelper.Add(cmd, "@value_b", finding.ValueB);
            PostgresqlCommandHelper.AddDate(cmd, "@evaluated_utc", finding.EvaluatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", finding.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", finding.LastUpdateUtc);
        }

        private static VesselHealthFinding FromReader(NpgsqlDataReader reader)
        {
            VesselHealthFinding finding = new VesselHealthFinding();
            finding.Id = reader["id"].ToString()!;
            finding.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            finding.VesselId = PostgresqlCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            finding.Criterion = PostgresqlCommandHelper.ReadEnum(reader["criterion"], VesselHealthCriterionEnum.Overall);
            finding.Status = PostgresqlCommandHelper.ReadEnum(reader["status"], VesselHealthStatusEnum.Unknown);
            finding.DetailCode = PostgresqlCommandHelper.ReadString(reader["detail_code"]);
            finding.ValueA = PostgresqlCommandHelper.ReadNullableLong(reader["value_a"]);
            finding.ValueB = PostgresqlCommandHelper.ReadNullableLong(reader["value_b"]);
            finding.EvaluatedUtc = PostgresqlCommandHelper.ReadDate(reader["evaluated_utc"]);
            finding.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            finding.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return finding;
        }

        #endregion
    }
}

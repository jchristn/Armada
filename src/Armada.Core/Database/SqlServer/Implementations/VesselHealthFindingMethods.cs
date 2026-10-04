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
    /// SQL Server implementation of vessel health finding persistence.
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public VesselHealthFindingMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_findings WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);

                foreach (VesselHealthFinding finding in findings)
                {
                    token.ThrowIfCancellationRequested();
                    await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO vessel_health_findings
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
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_health_findings WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id ORDER BY criterion ASC, id ASC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_findings WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, VesselHealthFinding finding)
        {
            SqlServerCommandHelper.Add(cmd, "@id", finding.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", finding.TenantId);
            SqlServerCommandHelper.Add(cmd, "@vessel_id", finding.VesselId);
            SqlServerCommandHelper.Add(cmd, "@criterion", finding.Criterion.ToString());
            SqlServerCommandHelper.Add(cmd, "@status", finding.Status.ToString());
            SqlServerCommandHelper.Add(cmd, "@detail_code", finding.DetailCode);
            SqlServerCommandHelper.Add(cmd, "@value_a", finding.ValueA);
            SqlServerCommandHelper.Add(cmd, "@value_b", finding.ValueB);
            SqlServerCommandHelper.AddDate(cmd, "@evaluated_utc", finding.EvaluatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", finding.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", finding.LastUpdateUtc);
        }

        private static VesselHealthFinding FromReader(SqlDataReader reader)
        {
            VesselHealthFinding finding = new VesselHealthFinding();
            finding.Id = reader["id"].ToString()!;
            finding.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            finding.VesselId = SqlServerCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            finding.Criterion = SqlServerCommandHelper.ReadEnum(reader["criterion"], VesselHealthCriterionEnum.Overall);
            finding.Status = SqlServerCommandHelper.ReadEnum(reader["status"], VesselHealthStatusEnum.Unknown);
            finding.DetailCode = SqlServerCommandHelper.ReadString(reader["detail_code"]);
            finding.ValueA = SqlServerCommandHelper.ReadNullableLong(reader["value_a"]);
            finding.ValueB = SqlServerCommandHelper.ReadNullableLong(reader["value_b"]);
            finding.EvaluatedUtc = SqlServerCommandHelper.ReadDate(reader["evaluated_utc"]);
            finding.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            finding.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return finding;
        }

        #endregion
    }
}

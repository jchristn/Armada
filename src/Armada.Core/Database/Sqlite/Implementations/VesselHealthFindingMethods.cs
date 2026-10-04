namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of vessel health finding persistence.
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
        /// <param name="driver">SQLite database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public VesselHealthFindingMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
            _WriteLock = driver.WriteLock;
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

            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_findings WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);

                foreach (VesselHealthFinding finding in findings)
                {
                    token.ThrowIfCancellationRequested();
                    await SqliteCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO vessel_health_findings
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
            return await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_health_findings WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id ORDER BY criterion ASC, id ASC;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_findings WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqliteCommand cmd, VesselHealthFinding finding)
        {
            SqliteCommandHelper.Add(cmd, "@id", finding.Id);
            SqliteCommandHelper.Add(cmd, "@tenant_id", finding.TenantId);
            SqliteCommandHelper.Add(cmd, "@vessel_id", finding.VesselId);
            SqliteCommandHelper.Add(cmd, "@criterion", finding.Criterion.ToString());
            SqliteCommandHelper.Add(cmd, "@status", finding.Status.ToString());
            SqliteCommandHelper.Add(cmd, "@detail_code", finding.DetailCode);
            SqliteCommandHelper.Add(cmd, "@value_a", finding.ValueA);
            SqliteCommandHelper.Add(cmd, "@value_b", finding.ValueB);
            SqliteCommandHelper.AddDate(cmd, "@evaluated_utc", finding.EvaluatedUtc);
            SqliteCommandHelper.AddDate(cmd, "@created_utc", finding.CreatedUtc);
            SqliteCommandHelper.AddDate(cmd, "@last_update_utc", finding.LastUpdateUtc);
        }

        private static VesselHealthFinding FromReader(SqliteDataReader reader)
        {
            VesselHealthFinding finding = new VesselHealthFinding();
            finding.Id = reader["id"].ToString()!;
            finding.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            finding.VesselId = SqliteCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            finding.Criterion = SqliteCommandHelper.ReadEnum(reader["criterion"], VesselHealthCriterionEnum.Overall);
            finding.Status = SqliteCommandHelper.ReadEnum(reader["status"], VesselHealthStatusEnum.Unknown);
            finding.DetailCode = SqliteCommandHelper.ReadString(reader["detail_code"]);
            finding.ValueA = SqliteCommandHelper.ReadNullableLong(reader["value_a"]);
            finding.ValueB = SqliteCommandHelper.ReadNullableLong(reader["value_b"]);
            finding.EvaluatedUtc = SqliteCommandHelper.ReadDate(reader["evaluated_utc"]);
            finding.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            finding.LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"]);
            return finding;
        }

        #endregion
    }
}

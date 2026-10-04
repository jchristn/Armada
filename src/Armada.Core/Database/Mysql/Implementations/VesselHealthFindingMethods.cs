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
    /// MySQL implementation of vessel health finding persistence.
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
        /// <param name="connectionString">MySQL connection string.</param>
        public VesselHealthFindingMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
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

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_findings WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);

                foreach (VesselHealthFinding finding in findings)
                {
                    token.ThrowIfCancellationRequested();
                    await MysqlCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO vessel_health_findings
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
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_health_findings WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id ORDER BY criterion ASC, id ASC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health_findings WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, VesselHealthFinding finding)
        {
            MysqlCommandHelper.Add(cmd, "@id", finding.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", finding.TenantId);
            MysqlCommandHelper.Add(cmd, "@vessel_id", finding.VesselId);
            MysqlCommandHelper.Add(cmd, "@criterion", finding.Criterion.ToString());
            MysqlCommandHelper.Add(cmd, "@status", finding.Status.ToString());
            MysqlCommandHelper.Add(cmd, "@detail_code", finding.DetailCode);
            MysqlCommandHelper.Add(cmd, "@value_a", finding.ValueA);
            MysqlCommandHelper.Add(cmd, "@value_b", finding.ValueB);
            MysqlCommandHelper.AddDate(cmd, "@evaluated_utc", finding.EvaluatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", finding.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", finding.LastUpdateUtc);
        }

        private static VesselHealthFinding FromReader(MySqlDataReader reader)
        {
            VesselHealthFinding finding = new VesselHealthFinding();
            finding.Id = reader["id"].ToString()!;
            finding.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            finding.VesselId = MysqlCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            finding.Criterion = MysqlCommandHelper.ReadEnum(reader["criterion"], VesselHealthCriterionEnum.Overall);
            finding.Status = MysqlCommandHelper.ReadEnum(reader["status"], VesselHealthStatusEnum.Unknown);
            finding.DetailCode = MysqlCommandHelper.ReadString(reader["detail_code"]);
            finding.ValueA = MysqlCommandHelper.ReadNullableLong(reader["value_a"]);
            finding.ValueB = MysqlCommandHelper.ReadNullableLong(reader["value_b"]);
            finding.EvaluatedUtc = MysqlCommandHelper.ReadDate(reader["evaluated_utc"]);
            finding.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            finding.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return finding;
        }

        #endregion
    }
}

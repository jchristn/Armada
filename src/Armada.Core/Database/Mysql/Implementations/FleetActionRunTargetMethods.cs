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
    /// MySQL implementation of fleet action run target persistence.
    /// </summary>
    public class FleetActionRunTargetMethods : IFleetActionRunTargetMethods
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
        public FleetActionRunTargetMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }
        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<FleetActionRunTarget> CreateAsync(FleetActionRunTarget target, CancellationToken token = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (String.IsNullOrEmpty(target.Id)) target.Id = Constants.IdGenerator.GenerateKSortable(Constants.FleetActionRunTargetIdPrefix, 24);
            target.LastUpdateUtc = DateTime.UtcNow;

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO fleet_action_run_targets
                    (id, tenant_id, run_id, vessel_id, vessel_name, status, skip_reason, failure_reason, rendered_text, exit_code, output_text, error_text, output_truncated, voyage_id, started_utc, completed_utc, duration_ms, created_utc, last_update_utc)
                    VALUES
                    (@id, @tenant_id, @run_id, @vessel_id, @vessel_name, @status, @skip_reason, @failure_reason, @rendered_text, @exit_code, @output_text, @error_text, @output_truncated, @voyage_id, @started_utc, @completed_utc, @duration_ms, @created_utc, @last_update_utc);",
                    cmd => Bind(cmd, target), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return target;
        }

        /// <inheritdoc />
        public async Task<FleetActionRunTarget?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<FleetActionRunTarget> results = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_action_run_targets WHERE id = @id;",
                cmd => MysqlCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetActionRunTarget?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<FleetActionRunTarget> results = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_action_run_targets WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@id", id);
                },
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetActionRunTarget> UpdateAsync(FleetActionRunTarget target, CancellationToken token = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (String.IsNullOrEmpty(target.Id)) throw new ArgumentException("Target identifier is required.", nameof(target));
            target.LastUpdateUtc = DateTime.UtcNow;

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, @"UPDATE fleet_action_run_targets SET
                    tenant_id = @tenant_id, run_id = @run_id, vessel_id = @vessel_id, vessel_name = @vessel_name,
                    status = @status, skip_reason = @skip_reason, failure_reason = @failure_reason,
                    rendered_text = @rendered_text, exit_code = @exit_code, output_text = @output_text,
                    error_text = @error_text, output_truncated = @output_truncated, voyage_id = @voyage_id,
                    started_utc = @started_utc, completed_utc = @completed_utc, duration_ms = @duration_ms,
                    created_utc = @created_utc, last_update_utc = @last_update_utc
                    WHERE id = @id;", cmd => Bind(cmd, target), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return target;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<FleetActionRunTarget>> EnumerateByRunAsync(string tenantId, string runId, FleetActionTargetStatusEnum? status, int pageNumber, int pageSize, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));

            EnumerationQuery query = new EnumerationQuery();
            query.PageNumber = pageNumber;
            query.PageSize = pageSize;

            string where = " WHERE tenant_id = @tenant_id AND run_id = @run_id" + (status.HasValue ? " AND status = @status" : String.Empty);
            Action<MySqlCommand> bind = cmd =>
            {
                MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                MysqlCommandHelper.Add(cmd, "@run_id", runId);
                if (status.HasValue) MysqlCommandHelper.Add(cmd, "@status", status.Value.ToString());
            };

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM fleet_action_run_targets" + where + ";", bind, token).ConfigureAwait(false);
                List<FleetActionRunTarget> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM fleet_action_run_targets" + where + " ORDER BY vessel_name ASC, id ASC" + MysqlCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);
                return EnumerationResult<FleetActionRunTarget>.Create(query, rows, total);
            }
        }

        /// <inheritdoc />
        public async Task<List<FleetActionRunTarget>> ReadAllByRunAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_action_run_targets WHERE run_id = @run_id ORDER BY vessel_name ASC, id ASC;",
                cmd => MysqlCommandHelper.Add(cmd, "@run_id", runId),
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByRunAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM fleet_action_run_targets WHERE run_id = @run_id;", cmd => MysqlCommandHelper.Add(cmd, "@run_id", runId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, FleetActionRunTarget target)
        {
            MysqlCommandHelper.Add(cmd, "@id", target.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", target.TenantId);
            MysqlCommandHelper.Add(cmd, "@run_id", target.RunId);
            MysqlCommandHelper.Add(cmd, "@vessel_id", target.VesselId);
            MysqlCommandHelper.Add(cmd, "@vessel_name", target.VesselName);
            MysqlCommandHelper.Add(cmd, "@status", target.Status.ToString());
            MysqlCommandHelper.Add(cmd, "@skip_reason", target.SkipReason);
            MysqlCommandHelper.Add(cmd, "@failure_reason", target.FailureReason);
            MysqlCommandHelper.Add(cmd, "@rendered_text", target.RenderedText);
            MysqlCommandHelper.Add(cmd, "@exit_code", target.ExitCode);
            MysqlCommandHelper.Add(cmd, "@output_text", target.OutputText);
            MysqlCommandHelper.Add(cmd, "@error_text", target.ErrorText);
            MysqlCommandHelper.Add(cmd, "@output_truncated", target.OutputTruncated);
            MysqlCommandHelper.Add(cmd, "@voyage_id", target.VoyageId);
            MysqlCommandHelper.AddDate(cmd, "@started_utc", target.StartedUtc);
            MysqlCommandHelper.AddDate(cmd, "@completed_utc", target.CompletedUtc);
            MysqlCommandHelper.Add(cmd, "@duration_ms", target.DurationMs);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", target.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", target.LastUpdateUtc);
        }

        private static FleetActionRunTarget FromReader(MySqlDataReader reader)
        {
            FleetActionRunTarget target = new FleetActionRunTarget();
            target.Id = reader["id"].ToString()!;
            target.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            target.RunId = MysqlCommandHelper.ReadString(reader["run_id"]);
            target.VesselId = MysqlCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            target.VesselName = MysqlCommandHelper.ReadString(reader["vessel_name"]) ?? String.Empty;
            target.Status = MysqlCommandHelper.ReadEnum(reader["status"], FleetActionTargetStatusEnum.Pending);
            target.SkipReason = MysqlCommandHelper.ReadString(reader["skip_reason"]);
            target.FailureReason = MysqlCommandHelper.ReadString(reader["failure_reason"]);
            target.RenderedText = MysqlCommandHelper.ReadString(reader["rendered_text"]);
            target.ExitCode = MysqlCommandHelper.ReadNullableInt(reader["exit_code"]);
            target.OutputText = MysqlCommandHelper.ReadString(reader["output_text"]);
            target.ErrorText = MysqlCommandHelper.ReadString(reader["error_text"]);
            target.OutputTruncated = MysqlCommandHelper.ReadBool(reader["output_truncated"], false);
            target.VoyageId = MysqlCommandHelper.ReadString(reader["voyage_id"]);
            target.StartedUtc = MysqlCommandHelper.ReadNullableDate(reader["started_utc"]);
            target.CompletedUtc = MysqlCommandHelper.ReadNullableDate(reader["completed_utc"]);
            target.DurationMs = MysqlCommandHelper.ReadNullableLong(reader["duration_ms"]);
            target.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            target.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return target;
        }

        #endregion
    }
}

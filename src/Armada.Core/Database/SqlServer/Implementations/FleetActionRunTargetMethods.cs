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
    /// SQL Server implementation of fleet action run target persistence.
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public FleetActionRunTargetMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<FleetActionRunTarget> CreateAsync(FleetActionRunTarget target, CancellationToken token = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (String.IsNullOrEmpty(target.Id)) target.Id = Constants.IdGenerator.GenerateKSortable(Constants.FleetActionRunTargetIdPrefix, 24);
            target.LastUpdateUtc = DateTime.UtcNow;

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO fleet_action_run_targets
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
            List<FleetActionRunTarget> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_action_run_targets WHERE id = @id;",
                cmd => SqlServerCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetActionRunTarget?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<FleetActionRunTarget> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_action_run_targets WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@id", id);
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

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"UPDATE fleet_action_run_targets SET
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
            Action<SqlCommand> bind = cmd =>
            {
                SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                SqlServerCommandHelper.Add(cmd, "@run_id", runId);
                if (status.HasValue) SqlServerCommandHelper.Add(cmd, "@status", status.Value.ToString());
            };

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM fleet_action_run_targets" + where + ";", bind, token).ConfigureAwait(false);
                List<FleetActionRunTarget> rows = await SqlServerCommandHelper.QueryAsync(conn,
                    "SELECT * FROM fleet_action_run_targets" + where + " ORDER BY vessel_name ASC, id ASC" + SqlServerCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);
                return EnumerationResult<FleetActionRunTarget>.Create(query, rows, total);
            }
        }

        /// <inheritdoc />
        public async Task<List<FleetActionRunTarget>> ReadAllByRunAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_action_run_targets WHERE run_id = @run_id ORDER BY vessel_name ASC, id ASC;",
                cmd => SqlServerCommandHelper.Add(cmd, "@run_id", runId),
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByRunAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM fleet_action_run_targets WHERE run_id = @run_id;", cmd => SqlServerCommandHelper.Add(cmd, "@run_id", runId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, FleetActionRunTarget target)
        {
            SqlServerCommandHelper.Add(cmd, "@id", target.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", target.TenantId);
            SqlServerCommandHelper.Add(cmd, "@run_id", target.RunId);
            SqlServerCommandHelper.Add(cmd, "@vessel_id", target.VesselId);
            SqlServerCommandHelper.Add(cmd, "@vessel_name", target.VesselName);
            SqlServerCommandHelper.Add(cmd, "@status", target.Status.ToString());
            SqlServerCommandHelper.Add(cmd, "@skip_reason", target.SkipReason);
            SqlServerCommandHelper.Add(cmd, "@failure_reason", target.FailureReason);
            SqlServerCommandHelper.Add(cmd, "@rendered_text", target.RenderedText);
            SqlServerCommandHelper.Add(cmd, "@exit_code", target.ExitCode);
            SqlServerCommandHelper.Add(cmd, "@output_text", target.OutputText);
            SqlServerCommandHelper.Add(cmd, "@error_text", target.ErrorText);
            SqlServerCommandHelper.Add(cmd, "@output_truncated", target.OutputTruncated);
            SqlServerCommandHelper.Add(cmd, "@voyage_id", target.VoyageId);
            SqlServerCommandHelper.AddDate(cmd, "@started_utc", target.StartedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@completed_utc", target.CompletedUtc);
            SqlServerCommandHelper.Add(cmd, "@duration_ms", target.DurationMs);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", target.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", target.LastUpdateUtc);
        }

        private static FleetActionRunTarget FromReader(SqlDataReader reader)
        {
            FleetActionRunTarget target = new FleetActionRunTarget();
            target.Id = reader["id"].ToString()!;
            target.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            target.RunId = SqlServerCommandHelper.ReadString(reader["run_id"]);
            target.VesselId = SqlServerCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            target.VesselName = SqlServerCommandHelper.ReadString(reader["vessel_name"]) ?? String.Empty;
            target.Status = SqlServerCommandHelper.ReadEnum(reader["status"], FleetActionTargetStatusEnum.Pending);
            target.SkipReason = SqlServerCommandHelper.ReadString(reader["skip_reason"]);
            target.FailureReason = SqlServerCommandHelper.ReadString(reader["failure_reason"]);
            target.RenderedText = SqlServerCommandHelper.ReadString(reader["rendered_text"]);
            target.ExitCode = SqlServerCommandHelper.ReadNullableInt(reader["exit_code"]);
            target.OutputText = SqlServerCommandHelper.ReadString(reader["output_text"]);
            target.ErrorText = SqlServerCommandHelper.ReadString(reader["error_text"]);
            target.OutputTruncated = SqlServerCommandHelper.ReadBool(reader["output_truncated"], false);
            target.VoyageId = SqlServerCommandHelper.ReadString(reader["voyage_id"]);
            target.StartedUtc = SqlServerCommandHelper.ReadNullableDate(reader["started_utc"]);
            target.CompletedUtc = SqlServerCommandHelper.ReadNullableDate(reader["completed_utc"]);
            target.DurationMs = SqlServerCommandHelper.ReadNullableLong(reader["duration_ms"]);
            target.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            target.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return target;
        }

        #endregion
    }
}

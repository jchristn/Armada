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
    /// SQL Server implementation of vessel import batch persistence.
    /// </summary>
    public class VesselImportBatchMethods : IVesselImportBatchMethods
    {
        #region Private-Members

        private static readonly string _Columns = "id, tenant_id, user_id, status, harbor_id, fleet_id, job_id, requested_path_count, candidate_count, created_count, skipped_count, failed_count, created_utc, last_update_utc, completed_utc, discovery_job_id, truncated, error_message, categorization_status, categorization_captain_id, categorization_job_id, categorization_prompt, categorization_apply_automatically, categorization_error, categorization_started_utc, categorization_completed_utc";
        private static readonly string _Values = "@id, @tenant_id, @user_id, @status, @harbor_id, @fleet_id, @job_id, @requested_path_count, @candidate_count, @created_count, @skipped_count, @failed_count, @created_utc, @last_update_utc, @completed_utc, @discovery_job_id, @truncated, @error_message, @categorization_status, @categorization_captain_id, @categorization_job_id, @categorization_prompt, @categorization_apply_automatically, @categorization_error, @categorization_started_utc, @categorization_completed_utc";

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
        public VesselImportBatchMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<VesselImportBatch> CreateAsync(VesselImportBatch batch, CancellationToken token = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            if (String.IsNullOrEmpty(batch.Id)) batch.Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselImportBatchIdPrefix, 24);
            batch.LastUpdateUtc = DateTime.UtcNow;

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "INSERT INTO vessel_import_batches (" + _Columns + ") VALUES (" + _Values + ");", cmd => Bind(cmd, batch), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return batch;
        }

        /// <inheritdoc />
        public async Task<VesselImportBatch?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportBatch> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_batches WHERE id = @id;",
                cmd => SqlServerCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<VesselImportBatch?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportBatch> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_batches WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@id", id);
                },
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<VesselImportBatch> UpdateAsync(VesselImportBatch batch, CancellationToken token = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            if (String.IsNullOrEmpty(batch.Id)) throw new ArgumentException("Batch identifier is required.", nameof(batch));
            batch.LastUpdateUtc = DateTime.UtcNow;

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_import_batches SET
                    tenant_id = @tenant_id, user_id = @user_id, status = @status, harbor_id = @harbor_id, fleet_id = @fleet_id,
                    job_id = @job_id, requested_path_count = @requested_path_count, candidate_count = @candidate_count,
                    created_count = @created_count, skipped_count = @skipped_count, failed_count = @failed_count,
                    created_utc = @created_utc, last_update_utc = @last_update_utc, completed_utc = @completed_utc,
                    discovery_job_id = @discovery_job_id, truncated = @truncated, error_message = @error_message,
                    categorization_status = @categorization_status, categorization_captain_id = @categorization_captain_id,
                    categorization_job_id = @categorization_job_id, categorization_prompt = @categorization_prompt,
                    categorization_apply_automatically = @categorization_apply_automatically, categorization_error = @categorization_error,
                    categorization_started_utc = @categorization_started_utc, categorization_completed_utc = @categorization_completed_utc
                    WHERE id = @id;", cmd => Bind(cmd, batch), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return batch;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendation_vessels WHERE batch_id = @id;", cmd => SqlServerCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendations WHERE batch_id = @id;", cmd => SqlServerCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_items WHERE batch_id = @id;", cmd => SqlServerCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_batches WHERE id = @id;", cmd => SqlServerCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                Action<SqlCommand> bind = cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@id", id);
                };
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendation_vessels WHERE tenant_id = @tenant_id AND batch_id = @id;", bind, token).ConfigureAwait(false);
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendations WHERE tenant_id = @tenant_id AND batch_id = @id;", bind, token).ConfigureAwait(false);
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_items WHERE tenant_id = @tenant_id AND batch_id = @id;", bind, token).ConfigureAwait(false);
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_batches WHERE tenant_id = @tenant_id AND id = @id;", bind, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<VesselImportBatch>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) query = new EnumerationQuery();

            List<string> conditions = new List<string> { "tenant_id = @tenant_id" };
            if (query.CreatedAfter.HasValue) conditions.Add("created_utc > @created_after");
            if (query.CreatedBefore.HasValue) conditions.Add("created_utc < @created_before");
            if (!String.IsNullOrEmpty(query.Status)) conditions.Add("status = @status");

            Action<SqlCommand> bind = cmd =>
            {
                SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                if (query.CreatedAfter.HasValue) SqlServerCommandHelper.AddDate(cmd, "@created_after", query.CreatedAfter.Value);
                if (query.CreatedBefore.HasValue) SqlServerCommandHelper.AddDate(cmd, "@created_before", query.CreatedBefore.Value);
                if (!String.IsNullOrEmpty(query.Status)) SqlServerCommandHelper.Add(cmd, "@status", NormalizeStatus(query.Status!));
            };

            string where = " WHERE " + String.Join(" AND ", conditions);
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM vessel_import_batches" + where + ";", bind, token).ConfigureAwait(false);
                List<VesselImportBatch> rows = await SqlServerCommandHelper.QueryAsync(conn,
                    "SELECT * FROM vessel_import_batches" + where + " ORDER BY created_utc " + direction + ", id " + direction + SqlServerCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);
                return EnumerationResult<VesselImportBatch>.Create(query, rows, total);
            }
        }

        /// <inheritdoc />
        public async Task<List<VesselImportBatch>> EnumerateInProgressAsync(CancellationToken token = default)
        {
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_batches WHERE status IN ('Discovering', 'Importing') OR categorization_status IN ('Pending', 'Running') ORDER BY created_utc ASC;",
                null, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string NormalizeStatus(string status)
        {
            return Enum.TryParse<VesselImportBatchStatusEnum>(status, true, out VesselImportBatchStatusEnum parsed) ? parsed.ToString() : status;
        }

        private static void Bind(SqlCommand cmd, VesselImportBatch batch)
        {
            SqlServerCommandHelper.Add(cmd, "@id", batch.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", batch.TenantId);
            SqlServerCommandHelper.Add(cmd, "@user_id", batch.UserId);
            SqlServerCommandHelper.Add(cmd, "@status", batch.Status.ToString());
            SqlServerCommandHelper.Add(cmd, "@harbor_id", batch.HarborId);
            SqlServerCommandHelper.Add(cmd, "@fleet_id", batch.FleetId);
            SqlServerCommandHelper.Add(cmd, "@job_id", batch.JobId);
            SqlServerCommandHelper.Add(cmd, "@requested_path_count", batch.RequestedPathCount);
            SqlServerCommandHelper.Add(cmd, "@candidate_count", batch.CandidateCount);
            SqlServerCommandHelper.Add(cmd, "@created_count", batch.CreatedCount);
            SqlServerCommandHelper.Add(cmd, "@skipped_count", batch.SkippedCount);
            SqlServerCommandHelper.Add(cmd, "@failed_count", batch.FailedCount);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", batch.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", batch.LastUpdateUtc);
            SqlServerCommandHelper.AddDate(cmd, "@completed_utc", batch.CompletedUtc);
            SqlServerCommandHelper.Add(cmd, "@discovery_job_id", batch.DiscoveryJobId);
            SqlServerCommandHelper.Add(cmd, "@truncated", batch.Truncated);
            SqlServerCommandHelper.Add(cmd, "@error_message", batch.ErrorMessage);
            SqlServerCommandHelper.Add(cmd, "@categorization_status", batch.CategorizationStatus.ToString());
            SqlServerCommandHelper.Add(cmd, "@categorization_captain_id", batch.CategorizationCaptainId);
            SqlServerCommandHelper.Add(cmd, "@categorization_job_id", batch.CategorizationJobId);
            SqlServerCommandHelper.Add(cmd, "@categorization_prompt", batch.CategorizationPrompt);
            SqlServerCommandHelper.Add(cmd, "@categorization_apply_automatically", batch.CategorizationApplyAutomatically);
            SqlServerCommandHelper.Add(cmd, "@categorization_error", batch.CategorizationError);
            SqlServerCommandHelper.AddDate(cmd, "@categorization_started_utc", batch.CategorizationStartedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@categorization_completed_utc", batch.CategorizationCompletedUtc);
        }

        private static VesselImportBatch FromReader(SqlDataReader reader)
        {
            VesselImportBatch batch = new VesselImportBatch();
            batch.Id = reader["id"].ToString()!;
            batch.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            batch.UserId = SqlServerCommandHelper.ReadString(reader["user_id"]);
            batch.Status = SqlServerCommandHelper.ReadEnum(reader["status"], VesselImportBatchStatusEnum.Discovered);
            batch.HarborId = SqlServerCommandHelper.ReadString(reader["harbor_id"]);
            batch.FleetId = SqlServerCommandHelper.ReadString(reader["fleet_id"]);
            batch.JobId = SqlServerCommandHelper.ReadString(reader["job_id"]);
            batch.RequestedPathCount = SqlServerCommandHelper.ReadInt(reader["requested_path_count"], 0);
            batch.CandidateCount = SqlServerCommandHelper.ReadInt(reader["candidate_count"], 0);
            batch.CreatedCount = SqlServerCommandHelper.ReadInt(reader["created_count"], 0);
            batch.SkippedCount = SqlServerCommandHelper.ReadInt(reader["skipped_count"], 0);
            batch.FailedCount = SqlServerCommandHelper.ReadInt(reader["failed_count"], 0);
            batch.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            batch.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            batch.CompletedUtc = SqlServerCommandHelper.ReadNullableDate(reader["completed_utc"]);
            batch.DiscoveryJobId = SqlServerCommandHelper.ReadString(reader["discovery_job_id"]);
            batch.Truncated = SqlServerCommandHelper.ReadBool(reader["truncated"], false);
            batch.ErrorMessage = SqlServerCommandHelper.ReadString(reader["error_message"]);
            batch.CategorizationStatus = SqlServerCommandHelper.ReadEnum(reader["categorization_status"], VesselImportCategorizationStatusEnum.None);
            batch.CategorizationCaptainId = SqlServerCommandHelper.ReadString(reader["categorization_captain_id"]);
            batch.CategorizationJobId = SqlServerCommandHelper.ReadString(reader["categorization_job_id"]);
            batch.CategorizationPrompt = SqlServerCommandHelper.ReadString(reader["categorization_prompt"]);
            batch.CategorizationApplyAutomatically = SqlServerCommandHelper.ReadBool(reader["categorization_apply_automatically"], false);
            batch.CategorizationError = SqlServerCommandHelper.ReadString(reader["categorization_error"]);
            batch.CategorizationStartedUtc = SqlServerCommandHelper.ReadNullableDate(reader["categorization_started_utc"]);
            batch.CategorizationCompletedUtc = SqlServerCommandHelper.ReadNullableDate(reader["categorization_completed_utc"]);
            return batch;
        }

        #endregion
    }
}

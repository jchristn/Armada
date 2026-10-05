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
    /// SQLite implementation of vessel import batch persistence.
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
        /// <param name="driver">SQLite database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public VesselImportBatchMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<VesselImportBatch> CreateAsync(VesselImportBatch batch, CancellationToken token = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            if (String.IsNullOrEmpty(batch.Id)) batch.Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselImportBatchIdPrefix, 24);
            batch.LastUpdateUtc = DateTime.UtcNow;

            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "INSERT INTO vessel_import_batches (" + _Columns + ") VALUES (" + _Values + ");", cmd => Bind(cmd, batch), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return batch;
        }

        /// <inheritdoc />
        public async Task<VesselImportBatch?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportBatch> results = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_batches WHERE id = @id;",
                cmd => SqliteCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<VesselImportBatch?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportBatch> results = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_batches WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@id", id);
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

            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_import_batches SET
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
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendation_vessels WHERE batch_id = @id;", cmd => SqliteCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendations WHERE batch_id = @id;", cmd => SqliteCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_items WHERE batch_id = @id;", cmd => SqliteCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_batches WHERE id = @id;", cmd => SqliteCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                Action<SqliteCommand> bind = cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@id", id);
                };
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendation_vessels WHERE tenant_id = @tenant_id AND batch_id = @id;", bind, token).ConfigureAwait(false);
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendations WHERE tenant_id = @tenant_id AND batch_id = @id;", bind, token).ConfigureAwait(false);
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_items WHERE tenant_id = @tenant_id AND batch_id = @id;", bind, token).ConfigureAwait(false);
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_batches WHERE tenant_id = @tenant_id AND id = @id;", bind, token).ConfigureAwait(false);
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

            Action<SqliteCommand> bind = cmd =>
            {
                SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                if (query.CreatedAfter.HasValue) SqliteCommandHelper.AddDate(cmd, "@created_after", query.CreatedAfter.Value);
                if (query.CreatedBefore.HasValue) SqliteCommandHelper.AddDate(cmd, "@created_before", query.CreatedBefore.Value);
                if (!String.IsNullOrEmpty(query.Status)) SqliteCommandHelper.Add(cmd, "@status", NormalizeStatus(query.Status!));
            };

            string where = " WHERE " + String.Join(" AND ", conditions);
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqliteCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM vessel_import_batches" + where + ";", bind, token).ConfigureAwait(false);
                List<VesselImportBatch> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM vessel_import_batches" + where + " ORDER BY created_utc " + direction + ", id " + direction + SqliteCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);
                return EnumerationResult<VesselImportBatch>.Create(query, rows, total);
            }
        }

        /// <inheritdoc />
        public async Task<List<VesselImportBatch>> EnumerateInProgressAsync(CancellationToken token = default)
        {
            return await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_batches WHERE status IN ('Discovering', 'Importing') OR categorization_status IN ('Pending', 'Running') ORDER BY created_utc ASC;",
                null, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string NormalizeStatus(string status)
        {
            return Enum.TryParse<VesselImportBatchStatusEnum>(status, true, out VesselImportBatchStatusEnum parsed) ? parsed.ToString() : status;
        }

        private static void Bind(SqliteCommand cmd, VesselImportBatch batch)
        {
            SqliteCommandHelper.Add(cmd, "@id", batch.Id);
            SqliteCommandHelper.Add(cmd, "@tenant_id", batch.TenantId);
            SqliteCommandHelper.Add(cmd, "@user_id", batch.UserId);
            SqliteCommandHelper.Add(cmd, "@status", batch.Status.ToString());
            SqliteCommandHelper.Add(cmd, "@harbor_id", batch.HarborId);
            SqliteCommandHelper.Add(cmd, "@fleet_id", batch.FleetId);
            SqliteCommandHelper.Add(cmd, "@job_id", batch.JobId);
            SqliteCommandHelper.Add(cmd, "@requested_path_count", batch.RequestedPathCount);
            SqliteCommandHelper.Add(cmd, "@candidate_count", batch.CandidateCount);
            SqliteCommandHelper.Add(cmd, "@created_count", batch.CreatedCount);
            SqliteCommandHelper.Add(cmd, "@skipped_count", batch.SkippedCount);
            SqliteCommandHelper.Add(cmd, "@failed_count", batch.FailedCount);
            SqliteCommandHelper.AddDate(cmd, "@created_utc", batch.CreatedUtc);
            SqliteCommandHelper.AddDate(cmd, "@last_update_utc", batch.LastUpdateUtc);
            SqliteCommandHelper.AddDate(cmd, "@completed_utc", batch.CompletedUtc);
            SqliteCommandHelper.Add(cmd, "@discovery_job_id", batch.DiscoveryJobId);
            SqliteCommandHelper.Add(cmd, "@truncated", batch.Truncated);
            SqliteCommandHelper.Add(cmd, "@error_message", batch.ErrorMessage);
            SqliteCommandHelper.Add(cmd, "@categorization_status", batch.CategorizationStatus.ToString());
            SqliteCommandHelper.Add(cmd, "@categorization_captain_id", batch.CategorizationCaptainId);
            SqliteCommandHelper.Add(cmd, "@categorization_job_id", batch.CategorizationJobId);
            SqliteCommandHelper.Add(cmd, "@categorization_prompt", batch.CategorizationPrompt);
            SqliteCommandHelper.Add(cmd, "@categorization_apply_automatically", batch.CategorizationApplyAutomatically);
            SqliteCommandHelper.Add(cmd, "@categorization_error", batch.CategorizationError);
            SqliteCommandHelper.AddDate(cmd, "@categorization_started_utc", batch.CategorizationStartedUtc);
            SqliteCommandHelper.AddDate(cmd, "@categorization_completed_utc", batch.CategorizationCompletedUtc);
        }

        private static VesselImportBatch FromReader(SqliteDataReader reader)
        {
            VesselImportBatch batch = new VesselImportBatch();
            batch.Id = reader["id"].ToString()!;
            batch.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            batch.UserId = SqliteCommandHelper.ReadString(reader["user_id"]);
            batch.Status = SqliteCommandHelper.ReadEnum(reader["status"], VesselImportBatchStatusEnum.Discovered);
            batch.HarborId = SqliteCommandHelper.ReadString(reader["harbor_id"]);
            batch.FleetId = SqliteCommandHelper.ReadString(reader["fleet_id"]);
            batch.JobId = SqliteCommandHelper.ReadString(reader["job_id"]);
            batch.RequestedPathCount = SqliteCommandHelper.ReadInt(reader["requested_path_count"], 0);
            batch.CandidateCount = SqliteCommandHelper.ReadInt(reader["candidate_count"], 0);
            batch.CreatedCount = SqliteCommandHelper.ReadInt(reader["created_count"], 0);
            batch.SkippedCount = SqliteCommandHelper.ReadInt(reader["skipped_count"], 0);
            batch.FailedCount = SqliteCommandHelper.ReadInt(reader["failed_count"], 0);
            batch.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            batch.LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"]);
            batch.CompletedUtc = SqliteCommandHelper.ReadNullableDate(reader["completed_utc"]);
            batch.DiscoveryJobId = SqliteCommandHelper.ReadString(reader["discovery_job_id"]);
            batch.Truncated = SqliteCommandHelper.ReadBool(reader["truncated"], false);
            batch.ErrorMessage = SqliteCommandHelper.ReadString(reader["error_message"]);
            batch.CategorizationStatus = SqliteCommandHelper.ReadEnum(reader["categorization_status"], VesselImportCategorizationStatusEnum.None);
            batch.CategorizationCaptainId = SqliteCommandHelper.ReadString(reader["categorization_captain_id"]);
            batch.CategorizationJobId = SqliteCommandHelper.ReadString(reader["categorization_job_id"]);
            batch.CategorizationPrompt = SqliteCommandHelper.ReadString(reader["categorization_prompt"]);
            batch.CategorizationApplyAutomatically = SqliteCommandHelper.ReadBool(reader["categorization_apply_automatically"], false);
            batch.CategorizationError = SqliteCommandHelper.ReadString(reader["categorization_error"]);
            batch.CategorizationStartedUtc = SqliteCommandHelper.ReadNullableDate(reader["categorization_started_utc"]);
            batch.CategorizationCompletedUtc = SqliteCommandHelper.ReadNullableDate(reader["categorization_completed_utc"]);
            return batch;
        }

        #endregion
    }
}

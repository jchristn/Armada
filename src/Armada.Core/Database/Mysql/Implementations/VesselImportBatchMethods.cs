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
    /// MySQL implementation of vessel import batch persistence.
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
        /// <param name="connectionString">MySQL connection string.</param>
        public VesselImportBatchMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
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

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "INSERT INTO vessel_import_batches (" + _Columns + ") VALUES (" + _Values + ");", cmd => Bind(cmd, batch), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return batch;
        }

        /// <inheritdoc />
        public async Task<VesselImportBatch?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportBatch> results = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_batches WHERE id = @id;",
                cmd => MysqlCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<VesselImportBatch?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportBatch> results = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_batches WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@id", id);
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

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_import_batches SET
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
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendation_vessels WHERE batch_id = @id;", cmd => MysqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendations WHERE batch_id = @id;", cmd => MysqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_items WHERE batch_id = @id;", cmd => MysqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_batches WHERE id = @id;", cmd => MysqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                Action<MySqlCommand> bind = cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@id", id);
                };
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendation_vessels WHERE tenant_id = @tenant_id AND batch_id = @id;", bind, token).ConfigureAwait(false);
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendations WHERE tenant_id = @tenant_id AND batch_id = @id;", bind, token).ConfigureAwait(false);
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_items WHERE tenant_id = @tenant_id AND batch_id = @id;", bind, token).ConfigureAwait(false);
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_batches WHERE tenant_id = @tenant_id AND id = @id;", bind, token).ConfigureAwait(false);
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

            Action<MySqlCommand> bind = cmd =>
            {
                MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                if (query.CreatedAfter.HasValue) MysqlCommandHelper.AddDate(cmd, "@created_after", query.CreatedAfter.Value);
                if (query.CreatedBefore.HasValue) MysqlCommandHelper.AddDate(cmd, "@created_before", query.CreatedBefore.Value);
                if (!String.IsNullOrEmpty(query.Status)) MysqlCommandHelper.Add(cmd, "@status", NormalizeStatus(query.Status!));
            };

            string where = " WHERE " + String.Join(" AND ", conditions);
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM vessel_import_batches" + where + ";", bind, token).ConfigureAwait(false);
                List<VesselImportBatch> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM vessel_import_batches" + where + " ORDER BY created_utc " + direction + ", id " + direction + MysqlCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);
                return EnumerationResult<VesselImportBatch>.Create(query, rows, total);
            }
        }

        /// <inheritdoc />
        public async Task<List<VesselImportBatch>> EnumerateInProgressAsync(CancellationToken token = default)
        {
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_batches WHERE status = 'Discovering' OR categorization_status IN ('Pending', 'Running') ORDER BY created_utc ASC;",
                null, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string NormalizeStatus(string status)
        {
            return Enum.TryParse<VesselImportBatchStatusEnum>(status, true, out VesselImportBatchStatusEnum parsed) ? parsed.ToString() : status;
        }

        private static void Bind(MySqlCommand cmd, VesselImportBatch batch)
        {
            MysqlCommandHelper.Add(cmd, "@id", batch.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", batch.TenantId);
            MysqlCommandHelper.Add(cmd, "@user_id", batch.UserId);
            MysqlCommandHelper.Add(cmd, "@status", batch.Status.ToString());
            MysqlCommandHelper.Add(cmd, "@harbor_id", batch.HarborId);
            MysqlCommandHelper.Add(cmd, "@fleet_id", batch.FleetId);
            MysqlCommandHelper.Add(cmd, "@job_id", batch.JobId);
            MysqlCommandHelper.Add(cmd, "@requested_path_count", batch.RequestedPathCount);
            MysqlCommandHelper.Add(cmd, "@candidate_count", batch.CandidateCount);
            MysqlCommandHelper.Add(cmd, "@created_count", batch.CreatedCount);
            MysqlCommandHelper.Add(cmd, "@skipped_count", batch.SkippedCount);
            MysqlCommandHelper.Add(cmd, "@failed_count", batch.FailedCount);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", batch.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", batch.LastUpdateUtc);
            MysqlCommandHelper.AddDate(cmd, "@completed_utc", batch.CompletedUtc);
            MysqlCommandHelper.Add(cmd, "@discovery_job_id", batch.DiscoveryJobId);
            MysqlCommandHelper.Add(cmd, "@truncated", batch.Truncated);
            MysqlCommandHelper.Add(cmd, "@error_message", batch.ErrorMessage);
            MysqlCommandHelper.Add(cmd, "@categorization_status", batch.CategorizationStatus.ToString());
            MysqlCommandHelper.Add(cmd, "@categorization_captain_id", batch.CategorizationCaptainId);
            MysqlCommandHelper.Add(cmd, "@categorization_job_id", batch.CategorizationJobId);
            MysqlCommandHelper.Add(cmd, "@categorization_prompt", batch.CategorizationPrompt);
            MysqlCommandHelper.Add(cmd, "@categorization_apply_automatically", batch.CategorizationApplyAutomatically);
            MysqlCommandHelper.Add(cmd, "@categorization_error", batch.CategorizationError);
            MysqlCommandHelper.AddDate(cmd, "@categorization_started_utc", batch.CategorizationStartedUtc);
            MysqlCommandHelper.AddDate(cmd, "@categorization_completed_utc", batch.CategorizationCompletedUtc);
        }

        private static VesselImportBatch FromReader(MySqlDataReader reader)
        {
            VesselImportBatch batch = new VesselImportBatch();
            batch.Id = reader["id"].ToString()!;
            batch.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            batch.UserId = MysqlCommandHelper.ReadString(reader["user_id"]);
            batch.Status = MysqlCommandHelper.ReadEnum(reader["status"], VesselImportBatchStatusEnum.Discovered);
            batch.HarborId = MysqlCommandHelper.ReadString(reader["harbor_id"]);
            batch.FleetId = MysqlCommandHelper.ReadString(reader["fleet_id"]);
            batch.JobId = MysqlCommandHelper.ReadString(reader["job_id"]);
            batch.RequestedPathCount = MysqlCommandHelper.ReadInt(reader["requested_path_count"], 0);
            batch.CandidateCount = MysqlCommandHelper.ReadInt(reader["candidate_count"], 0);
            batch.CreatedCount = MysqlCommandHelper.ReadInt(reader["created_count"], 0);
            batch.SkippedCount = MysqlCommandHelper.ReadInt(reader["skipped_count"], 0);
            batch.FailedCount = MysqlCommandHelper.ReadInt(reader["failed_count"], 0);
            batch.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            batch.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            batch.CompletedUtc = MysqlCommandHelper.ReadNullableDate(reader["completed_utc"]);
            batch.DiscoveryJobId = MysqlCommandHelper.ReadString(reader["discovery_job_id"]);
            batch.Truncated = MysqlCommandHelper.ReadBool(reader["truncated"], false);
            batch.ErrorMessage = MysqlCommandHelper.ReadString(reader["error_message"]);
            batch.CategorizationStatus = MysqlCommandHelper.ReadEnum(reader["categorization_status"], VesselImportCategorizationStatusEnum.None);
            batch.CategorizationCaptainId = MysqlCommandHelper.ReadString(reader["categorization_captain_id"]);
            batch.CategorizationJobId = MysqlCommandHelper.ReadString(reader["categorization_job_id"]);
            batch.CategorizationPrompt = MysqlCommandHelper.ReadString(reader["categorization_prompt"]);
            batch.CategorizationApplyAutomatically = MysqlCommandHelper.ReadBool(reader["categorization_apply_automatically"], false);
            batch.CategorizationError = MysqlCommandHelper.ReadString(reader["categorization_error"]);
            batch.CategorizationStartedUtc = MysqlCommandHelper.ReadNullableDate(reader["categorization_started_utc"]);
            batch.CategorizationCompletedUtc = MysqlCommandHelper.ReadNullableDate(reader["categorization_completed_utc"]);
            return batch;
        }

        #endregion
    }
}

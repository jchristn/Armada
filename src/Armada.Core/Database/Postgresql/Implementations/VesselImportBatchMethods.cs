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
    /// PostgreSQL implementation of vessel import batch persistence.
    /// </summary>
    public class VesselImportBatchMethods : IVesselImportBatchMethods
    {
        #region Private-Members

        private static readonly string _Columns = "id, tenant_id, user_id, status, harbor_id, fleet_id, job_id, requested_path_count, candidate_count, created_count, skipped_count, failed_count, created_utc, last_update_utc, completed_utc";
        private static readonly string _Values = "@id, @tenant_id, @user_id, @status, @harbor_id, @fleet_id, @job_id, @requested_path_count, @candidate_count, @created_count, @skipped_count, @failed_count, @created_utc, @last_update_utc, @completed_utc";

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
        public VesselImportBatchMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "INSERT INTO vessel_import_batches (" + _Columns + ") VALUES (" + _Values + ");", cmd => Bind(cmd, batch), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return batch;
        }

        /// <inheritdoc />
        public async Task<VesselImportBatch?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportBatch> results = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_batches WHERE id = @id;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<VesselImportBatch?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportBatch> results = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_batches WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@id", id);
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

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_import_batches SET
                    tenant_id = @tenant_id, user_id = @user_id, status = @status, harbor_id = @harbor_id, fleet_id = @fleet_id,
                    job_id = @job_id, requested_path_count = @requested_path_count, candidate_count = @candidate_count,
                    created_count = @created_count, skipped_count = @skipped_count, failed_count = @failed_count,
                    created_utc = @created_utc, last_update_utc = @last_update_utc, completed_utc = @completed_utc
                    WHERE id = @id;", cmd => Bind(cmd, batch), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return batch;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_items WHERE batch_id = @id;", cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_batches WHERE id = @id;", cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                Action<NpgsqlCommand> bind = cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@id", id);
                };
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_items WHERE tenant_id = @tenant_id AND batch_id = @id;", bind, token).ConfigureAwait(false);
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_batches WHERE tenant_id = @tenant_id AND id = @id;", bind, token).ConfigureAwait(false);
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

            Action<NpgsqlCommand> bind = cmd =>
            {
                PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                if (query.CreatedAfter.HasValue) PostgresqlCommandHelper.AddDate(cmd, "@created_after", query.CreatedAfter.Value);
                if (query.CreatedBefore.HasValue) PostgresqlCommandHelper.AddDate(cmd, "@created_before", query.CreatedBefore.Value);
                if (!String.IsNullOrEmpty(query.Status)) PostgresqlCommandHelper.Add(cmd, "@status", NormalizeStatus(query.Status!));
            };

            string where = " WHERE " + String.Join(" AND ", conditions);
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM vessel_import_batches" + where + ";", bind, token).ConfigureAwait(false);
                List<VesselImportBatch> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM vessel_import_batches" + where + " ORDER BY created_utc " + direction + ", id " + direction + PostgresqlCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);
                return EnumerationResult<VesselImportBatch>.Create(query, rows, total);
            }
        }

        #endregion

        #region Private-Methods

        private static string NormalizeStatus(string status)
        {
            return Enum.TryParse<VesselImportBatchStatusEnum>(status, true, out VesselImportBatchStatusEnum parsed) ? parsed.ToString() : status;
        }

        private static void Bind(NpgsqlCommand cmd, VesselImportBatch batch)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", batch.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", batch.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@user_id", batch.UserId);
            PostgresqlCommandHelper.Add(cmd, "@status", batch.Status.ToString());
            PostgresqlCommandHelper.Add(cmd, "@harbor_id", batch.HarborId);
            PostgresqlCommandHelper.Add(cmd, "@fleet_id", batch.FleetId);
            PostgresqlCommandHelper.Add(cmd, "@job_id", batch.JobId);
            PostgresqlCommandHelper.Add(cmd, "@requested_path_count", batch.RequestedPathCount);
            PostgresqlCommandHelper.Add(cmd, "@candidate_count", batch.CandidateCount);
            PostgresqlCommandHelper.Add(cmd, "@created_count", batch.CreatedCount);
            PostgresqlCommandHelper.Add(cmd, "@skipped_count", batch.SkippedCount);
            PostgresqlCommandHelper.Add(cmd, "@failed_count", batch.FailedCount);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", batch.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", batch.LastUpdateUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@completed_utc", batch.CompletedUtc);
        }

        private static VesselImportBatch FromReader(NpgsqlDataReader reader)
        {
            VesselImportBatch batch = new VesselImportBatch();
            batch.Id = reader["id"].ToString()!;
            batch.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            batch.UserId = PostgresqlCommandHelper.ReadString(reader["user_id"]);
            batch.Status = PostgresqlCommandHelper.ReadEnum(reader["status"], VesselImportBatchStatusEnum.Discovered);
            batch.HarborId = PostgresqlCommandHelper.ReadString(reader["harbor_id"]);
            batch.FleetId = PostgresqlCommandHelper.ReadString(reader["fleet_id"]);
            batch.JobId = PostgresqlCommandHelper.ReadString(reader["job_id"]);
            batch.RequestedPathCount = PostgresqlCommandHelper.ReadInt(reader["requested_path_count"], 0);
            batch.CandidateCount = PostgresqlCommandHelper.ReadInt(reader["candidate_count"], 0);
            batch.CreatedCount = PostgresqlCommandHelper.ReadInt(reader["created_count"], 0);
            batch.SkippedCount = PostgresqlCommandHelper.ReadInt(reader["skipped_count"], 0);
            batch.FailedCount = PostgresqlCommandHelper.ReadInt(reader["failed_count"], 0);
            batch.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            batch.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            batch.CompletedUtc = PostgresqlCommandHelper.ReadNullableDate(reader["completed_utc"]);
            return batch;
        }

        #endregion
    }
}

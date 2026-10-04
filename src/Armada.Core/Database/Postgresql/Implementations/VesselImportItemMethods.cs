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
    /// PostgreSQL implementation of vessel import item persistence.
    /// </summary>
    public class VesselImportItemMethods : IVesselImportItemMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO vessel_import_items
            (id, tenant_id, batch_id, path, proposed_name, remote_url, default_branch, candidate_status, existing_vessel_id, outcome, outcome_reason, outcome_message, vessel_id, selected, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @batch_id, @path, @proposed_name, @remote_url, @default_branch, @candidate_status, @existing_vessel_id, @outcome, @outcome_reason, @outcome_message, @vessel_id, @selected, @created_utc, @last_update_utc);";

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
        public VesselImportItemMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<VesselImportItem> CreateAsync(VesselImportItem item, CancellationToken token = default)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            Prepare(item);

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, item), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return item;
        }

        /// <inheritdoc />
        public async Task<List<VesselImportItem>> CreateManyAsync(List<VesselImportItem> items, CancellationToken token = default)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (items.Count < 1) return items;
            foreach (VesselImportItem item in items)
            {
                if (item == null) throw new ArgumentException("Items must not contain null entries.", nameof(items));
                Prepare(item);
            }

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                foreach (VesselImportItem item in items)
                {
                    token.ThrowIfCancellationRequested();
                    await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, item), token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);

            return items;
        }

        /// <inheritdoc />
        public async Task<VesselImportItem?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportItem> results = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_items WHERE id = @id;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<VesselImportItem?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportItem> results = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_items WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@id", id);
                },
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<VesselImportItem> UpdateAsync(VesselImportItem item, CancellationToken token = default)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (String.IsNullOrEmpty(item.Id)) throw new ArgumentException("Item identifier is required.", nameof(item));
            item.LastUpdateUtc = DateTime.UtcNow;

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_import_items SET
                    tenant_id = @tenant_id, batch_id = @batch_id, path = @path, proposed_name = @proposed_name,
                    remote_url = @remote_url, default_branch = @default_branch, candidate_status = @candidate_status,
                    existing_vessel_id = @existing_vessel_id, outcome = @outcome, outcome_reason = @outcome_reason,
                    outcome_message = @outcome_message, vessel_id = @vessel_id, selected = @selected, created_utc = @created_utc,
                    last_update_utc = @last_update_utc
                    WHERE id = @id;", cmd => Bind(cmd, item), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return item;
        }

        /// <inheritdoc />
        public async Task<List<VesselImportItem>> EnumerateByBatchAsync(string tenantId, string batchId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(batchId)) throw new ArgumentNullException(nameof(batchId));
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_items WHERE tenant_id = @tenant_id AND batch_id = @batch_id ORDER BY path ASC, id ASC;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@batch_id", batchId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByBatchAsync(string tenantId, string batchId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(batchId)) throw new ArgumentNullException(nameof(batchId));
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_items WHERE tenant_id = @tenant_id AND batch_id = @batch_id;", cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@batch_id", batchId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Prepare(VesselImportItem item)
        {
            if (String.IsNullOrEmpty(item.Id)) item.Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselImportItemIdPrefix, 24);
            item.LastUpdateUtc = DateTime.UtcNow;
        }

        private static void Bind(NpgsqlCommand cmd, VesselImportItem item)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", item.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", item.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@batch_id", item.BatchId);
            PostgresqlCommandHelper.Add(cmd, "@path", item.Path);
            PostgresqlCommandHelper.Add(cmd, "@proposed_name", item.ProposedName);
            PostgresqlCommandHelper.Add(cmd, "@remote_url", item.RemoteUrl);
            PostgresqlCommandHelper.Add(cmd, "@default_branch", item.DefaultBranch);
            PostgresqlCommandHelper.Add(cmd, "@candidate_status", item.CandidateStatus.ToString());
            PostgresqlCommandHelper.Add(cmd, "@existing_vessel_id", item.ExistingVesselId);
            PostgresqlCommandHelper.Add(cmd, "@outcome", item.Outcome.ToString());
            PostgresqlCommandHelper.Add(cmd, "@outcome_reason", item.OutcomeReason);
            PostgresqlCommandHelper.Add(cmd, "@outcome_message", item.OutcomeMessage);
            PostgresqlCommandHelper.Add(cmd, "@vessel_id", item.VesselId);
            PostgresqlCommandHelper.Add(cmd, "@selected", item.Selected);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", item.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", item.LastUpdateUtc);
        }

        private static VesselImportItem FromReader(NpgsqlDataReader reader)
        {
            VesselImportItem item = new VesselImportItem();
            item.Id = reader["id"].ToString()!;
            item.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            item.BatchId = PostgresqlCommandHelper.ReadString(reader["batch_id"]);
            item.Path = PostgresqlCommandHelper.ReadString(reader["path"]) ?? String.Empty;
            item.ProposedName = PostgresqlCommandHelper.ReadString(reader["proposed_name"]) ?? String.Empty;
            item.RemoteUrl = PostgresqlCommandHelper.ReadString(reader["remote_url"]);
            item.DefaultBranch = PostgresqlCommandHelper.ReadString(reader["default_branch"]);
            item.CandidateStatus = PostgresqlCommandHelper.ReadEnum(reader["candidate_status"], VesselImportCandidateStatusEnum.New);
            item.ExistingVesselId = PostgresqlCommandHelper.ReadString(reader["existing_vessel_id"]);
            item.Outcome = PostgresqlCommandHelper.ReadEnum(reader["outcome"], VesselImportOutcomeEnum.Pending);
            item.OutcomeReason = PostgresqlCommandHelper.ReadString(reader["outcome_reason"]);
            item.OutcomeMessage = PostgresqlCommandHelper.ReadString(reader["outcome_message"]);
            item.VesselId = PostgresqlCommandHelper.ReadString(reader["vessel_id"]);
            item.Selected = PostgresqlCommandHelper.ReadBool(reader["selected"], false);
            item.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            item.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return item;
        }

        #endregion
    }
}

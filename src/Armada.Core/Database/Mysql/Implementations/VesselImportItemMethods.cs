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
    /// MySQL implementation of vessel import item persistence.
    /// </summary>
    public class VesselImportItemMethods : IVesselImportItemMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO vessel_import_items
            (id, tenant_id, batch_id, path, proposed_name, remote_url, default_branch, candidate_status, existing_vessel_id, outcome, outcome_reason, outcome_message, vessel_id, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @batch_id, @path, @proposed_name, @remote_url, @default_branch, @candidate_status, @existing_vessel_id, @outcome, @outcome_reason, @outcome_message, @vessel_id, @created_utc, @last_update_utc);";

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        public VesselImportItemMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }
        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<VesselImportItem> CreateAsync(VesselImportItem item, CancellationToken token = default)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            Prepare(item);

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, item), token).ConfigureAwait(false);
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

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                foreach (VesselImportItem item in items)
                {
                    token.ThrowIfCancellationRequested();
                    await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, item), token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);

            return items;
        }

        /// <inheritdoc />
        public async Task<VesselImportItem?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportItem> results = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_items WHERE id = @id;",
                cmd => MysqlCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<VesselImportItem?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportItem> results = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_items WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@id", id);
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

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_import_items SET
                    tenant_id = @tenant_id, batch_id = @batch_id, path = @path, proposed_name = @proposed_name,
                    remote_url = @remote_url, default_branch = @default_branch, candidate_status = @candidate_status,
                    existing_vessel_id = @existing_vessel_id, outcome = @outcome, outcome_reason = @outcome_reason,
                    outcome_message = @outcome_message, vessel_id = @vessel_id, created_utc = @created_utc,
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
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_items WHERE tenant_id = @tenant_id AND batch_id = @batch_id ORDER BY path ASC, id ASC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@batch_id", batchId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByBatchAsync(string tenantId, string batchId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(batchId)) throw new ArgumentNullException(nameof(batchId));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_items WHERE tenant_id = @tenant_id AND batch_id = @batch_id;", cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@batch_id", batchId);
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

        private static void Bind(MySqlCommand cmd, VesselImportItem item)
        {
            MysqlCommandHelper.Add(cmd, "@id", item.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", item.TenantId);
            MysqlCommandHelper.Add(cmd, "@batch_id", item.BatchId);
            MysqlCommandHelper.Add(cmd, "@path", item.Path);
            MysqlCommandHelper.Add(cmd, "@proposed_name", item.ProposedName);
            MysqlCommandHelper.Add(cmd, "@remote_url", item.RemoteUrl);
            MysqlCommandHelper.Add(cmd, "@default_branch", item.DefaultBranch);
            MysqlCommandHelper.Add(cmd, "@candidate_status", item.CandidateStatus.ToString());
            MysqlCommandHelper.Add(cmd, "@existing_vessel_id", item.ExistingVesselId);
            MysqlCommandHelper.Add(cmd, "@outcome", item.Outcome.ToString());
            MysqlCommandHelper.Add(cmd, "@outcome_reason", item.OutcomeReason);
            MysqlCommandHelper.Add(cmd, "@outcome_message", item.OutcomeMessage);
            MysqlCommandHelper.Add(cmd, "@vessel_id", item.VesselId);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", item.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", item.LastUpdateUtc);
        }

        private static VesselImportItem FromReader(MySqlDataReader reader)
        {
            VesselImportItem item = new VesselImportItem();
            item.Id = reader["id"].ToString()!;
            item.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            item.BatchId = MysqlCommandHelper.ReadString(reader["batch_id"]);
            item.Path = MysqlCommandHelper.ReadString(reader["path"]) ?? String.Empty;
            item.ProposedName = MysqlCommandHelper.ReadString(reader["proposed_name"]) ?? String.Empty;
            item.RemoteUrl = MysqlCommandHelper.ReadString(reader["remote_url"]);
            item.DefaultBranch = MysqlCommandHelper.ReadString(reader["default_branch"]);
            item.CandidateStatus = MysqlCommandHelper.ReadEnum(reader["candidate_status"], VesselImportCandidateStatusEnum.New);
            item.ExistingVesselId = MysqlCommandHelper.ReadString(reader["existing_vessel_id"]);
            item.Outcome = MysqlCommandHelper.ReadEnum(reader["outcome"], VesselImportOutcomeEnum.Pending);
            item.OutcomeReason = MysqlCommandHelper.ReadString(reader["outcome_reason"]);
            item.OutcomeMessage = MysqlCommandHelper.ReadString(reader["outcome_message"]);
            item.VesselId = MysqlCommandHelper.ReadString(reader["vessel_id"]);
            item.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            item.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return item;
        }

        #endregion
    }
}

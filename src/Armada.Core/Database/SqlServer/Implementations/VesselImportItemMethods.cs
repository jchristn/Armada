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
    /// SQL Server implementation of vessel import item persistence.
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public VesselImportItemMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, item), token).ConfigureAwait(false);
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

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                foreach (VesselImportItem item in items)
                {
                    token.ThrowIfCancellationRequested();
                    await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, item), token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);

            return items;
        }

        /// <inheritdoc />
        public async Task<VesselImportItem?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportItem> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_items WHERE id = @id;",
                cmd => SqlServerCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<VesselImportItem?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<VesselImportItem> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_items WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@id", id);
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

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_import_items SET
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
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_items WHERE tenant_id = @tenant_id AND batch_id = @batch_id ORDER BY path ASC, id ASC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@batch_id", batchId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByBatchAsync(string tenantId, string batchId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(batchId)) throw new ArgumentNullException(nameof(batchId));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_items WHERE tenant_id = @tenant_id AND batch_id = @batch_id;", cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@batch_id", batchId);
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

        private static void Bind(SqlCommand cmd, VesselImportItem item)
        {
            SqlServerCommandHelper.Add(cmd, "@id", item.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", item.TenantId);
            SqlServerCommandHelper.Add(cmd, "@batch_id", item.BatchId);
            SqlServerCommandHelper.Add(cmd, "@path", item.Path);
            SqlServerCommandHelper.Add(cmd, "@proposed_name", item.ProposedName);
            SqlServerCommandHelper.Add(cmd, "@remote_url", item.RemoteUrl);
            SqlServerCommandHelper.Add(cmd, "@default_branch", item.DefaultBranch);
            SqlServerCommandHelper.Add(cmd, "@candidate_status", item.CandidateStatus.ToString());
            SqlServerCommandHelper.Add(cmd, "@existing_vessel_id", item.ExistingVesselId);
            SqlServerCommandHelper.Add(cmd, "@outcome", item.Outcome.ToString());
            SqlServerCommandHelper.Add(cmd, "@outcome_reason", item.OutcomeReason);
            SqlServerCommandHelper.Add(cmd, "@outcome_message", item.OutcomeMessage);
            SqlServerCommandHelper.Add(cmd, "@vessel_id", item.VesselId);
            SqlServerCommandHelper.Add(cmd, "@selected", item.Selected);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", item.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", item.LastUpdateUtc);
        }

        private static VesselImportItem FromReader(SqlDataReader reader)
        {
            VesselImportItem item = new VesselImportItem();
            item.Id = reader["id"].ToString()!;
            item.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            item.BatchId = SqlServerCommandHelper.ReadString(reader["batch_id"]);
            item.Path = SqlServerCommandHelper.ReadString(reader["path"]) ?? String.Empty;
            item.ProposedName = SqlServerCommandHelper.ReadString(reader["proposed_name"]) ?? String.Empty;
            item.RemoteUrl = SqlServerCommandHelper.ReadString(reader["remote_url"]);
            item.DefaultBranch = SqlServerCommandHelper.ReadString(reader["default_branch"]);
            item.CandidateStatus = SqlServerCommandHelper.ReadEnum(reader["candidate_status"], VesselImportCandidateStatusEnum.New);
            item.ExistingVesselId = SqlServerCommandHelper.ReadString(reader["existing_vessel_id"]);
            item.Outcome = SqlServerCommandHelper.ReadEnum(reader["outcome"], VesselImportOutcomeEnum.Pending);
            item.OutcomeReason = SqlServerCommandHelper.ReadString(reader["outcome_reason"]);
            item.OutcomeMessage = SqlServerCommandHelper.ReadString(reader["outcome_message"]);
            item.VesselId = SqlServerCommandHelper.ReadString(reader["vessel_id"]);
            item.Selected = SqlServerCommandHelper.ReadBool(reader["selected"], false);
            item.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            item.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return item;
        }

        #endregion
    }
}

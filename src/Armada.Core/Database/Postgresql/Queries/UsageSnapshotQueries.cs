namespace Armada.Core.Database.Postgresql.Queries
{
    /// <summary>
    /// Handwritten PostgreSQL statements for usage capture storage: snapshots, snapshot-captain links,
    /// per-account latest state, account memberships and hourly rollups. Kept next to the migration that
    /// creates the tables; the Implementations files bind these statements.
    /// </summary>
    internal static class UsageSnapshotQueries
    {
        internal const string SnapshotInsert = @"INSERT INTO usage_snapshots
            (id, tenant_id, captain_id, harbor_id, host_id, runtime, provider, account_key,
             captured_utc, received_utc, status, source, windows_json, raw_excerpt, error_code)
            VALUES
            (@id, @tenant_id, @captain_id, @harbor_id, @host_id, @runtime, @provider, @account_key,
             @captured_utc, @received_utc, @status, @source, @windows_json, @raw_excerpt, @error_code);";

        internal const string SnapshotSelectById = "SELECT * FROM usage_snapshots WHERE id = @id AND tenant_id = @tenant_id;";

        internal const string SnapshotCaptainsInsert = @"INSERT INTO usage_snapshot_captains
            (id, tenant_id, snapshot_id, captain_id, captured_utc)
            VALUES
            (@id, @tenant_id, @snapshot_id, @captain_id, @captured_utc);";

        internal const string SnapshotCaptainsSelect = "SELECT captain_id FROM usage_snapshot_captains WHERE snapshot_id = @snapshot_id AND tenant_id = @tenant_id ORDER BY captain_id;";

        internal const string SnapshotDeleteOlderThan = "DELETE FROM usage_snapshots WHERE id IN (SELECT id FROM usage_snapshots WHERE captured_utc < @captured_before ORDER BY captured_utc, id LIMIT 500);";

        internal const string SnapshotCaptainsDeleteOlderThan = "DELETE FROM usage_snapshot_captains WHERE snapshot_id IN (SELECT id FROM usage_snapshots WHERE captured_utc < @captured_before ORDER BY captured_utc, id LIMIT 500);";

        internal const string SnapshotDeleteByTenant = "DELETE FROM usage_snapshots WHERE tenant_id = @tenant_id;";

        internal const string SnapshotCaptainsDeleteByTenant = "DELETE FROM usage_snapshot_captains WHERE tenant_id = @tenant_id;";

        internal const string LatestStateUpsert = @"INSERT INTO usage_latest_state
            (id, tenant_id, host_id, runtime, provider, account_key,
             last_good_snapshot_id, last_good_captured_utc, last_good_received_utc, last_good_status, last_good_windows_json,
             last_error_snapshot_id, last_error_captured_utc, last_error_status, last_error_code,
             expired_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @host_id, @runtime, @provider, @account_key,
             @last_good_snapshot_id, @last_good_captured_utc, @last_good_received_utc, @last_good_status, @last_good_windows_json,
             @last_error_snapshot_id, @last_error_captured_utc, @last_error_status, @last_error_code,
             @expired_utc, @last_update_utc)
            ON CONFLICT(tenant_id, host_id, runtime, account_key) DO UPDATE SET
                provider = excluded.provider,
                last_good_snapshot_id = CASE WHEN excluded.last_good_snapshot_id IS NULL THEN usage_latest_state.last_good_snapshot_id ELSE excluded.last_good_snapshot_id END,
                last_good_captured_utc = CASE WHEN excluded.last_good_snapshot_id IS NULL THEN usage_latest_state.last_good_captured_utc ELSE excluded.last_good_captured_utc END,
                last_good_received_utc = CASE WHEN excluded.last_good_snapshot_id IS NULL THEN usage_latest_state.last_good_received_utc ELSE excluded.last_good_received_utc END,
                last_good_status = CASE WHEN excluded.last_good_snapshot_id IS NULL THEN usage_latest_state.last_good_status ELSE excluded.last_good_status END,
                last_good_windows_json = CASE WHEN excluded.last_good_snapshot_id IS NULL THEN usage_latest_state.last_good_windows_json ELSE excluded.last_good_windows_json END,
                last_error_snapshot_id = CASE WHEN excluded.last_error_snapshot_id IS NULL THEN usage_latest_state.last_error_snapshot_id ELSE excluded.last_error_snapshot_id END,
                last_error_captured_utc = CASE WHEN excluded.last_error_snapshot_id IS NULL THEN usage_latest_state.last_error_captured_utc ELSE excluded.last_error_captured_utc END,
                last_error_status = CASE WHEN excluded.last_error_snapshot_id IS NULL THEN usage_latest_state.last_error_status ELSE excluded.last_error_status END,
                last_error_code = CASE WHEN excluded.last_error_snapshot_id IS NULL THEN usage_latest_state.last_error_code ELSE excluded.last_error_code END,
                expired_utc = excluded.expired_utc,
                last_update_utc = excluded.last_update_utc;";

        internal const string LatestStateSelectByKey = "SELECT * FROM usage_latest_state WHERE tenant_id = @tenant_id AND host_id = @host_id AND runtime = @runtime AND account_key = @account_key;";

        internal const string LatestStateDeleteByTenant = "DELETE FROM usage_latest_state WHERE tenant_id = @tenant_id;";

        internal const string MembershipInsert = @"INSERT INTO usage_account_memberships
            (id, tenant_id, captain_id, host_id, runtime, provider, account_key, configuration_revision,
             effective_from_utc, effective_to_utc, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @captain_id, @host_id, @runtime, @provider, @account_key, @configuration_revision,
             @effective_from_utc, @effective_to_utc, @created_utc, @last_update_utc);";

        internal const string MembershipSelectById = "SELECT * FROM usage_account_memberships WHERE id = @id AND tenant_id = @tenant_id;";

        internal const string MembershipCloseOpen = "UPDATE usage_account_memberships SET effective_to_utc = @effective_to_utc, last_update_utc = @last_update_utc WHERE tenant_id = @tenant_id AND captain_id = @captain_id AND effective_to_utc IS NULL;";

        internal const string MembershipDeleteByTenant = "DELETE FROM usage_account_memberships WHERE tenant_id = @tenant_id;";

        internal const string RollupUpsert = @"INSERT INTO usage_hourly_rollups
            (id, tenant_id, host_id, runtime, provider, account_key, window_name, model, hour_utc,
             sample_count, percent_sample_count, last_status, raw_pruned, last_percent, min_percent, max_percent, mean_percent, last_used, last_limit, last_reset_utc,
             first_observed_utc, last_observed_utc, ok_count, unsupported_count, auth_missing_count, failed_count,
             estimated_count, coverage_seconds, last_update_utc)
            VALUES
            (@id, @tenant_id, @host_id, @runtime, @provider, @account_key, @window_name, @model, @hour_utc,
             @sample_count, @percent_sample_count, @last_status, @raw_pruned, @last_percent, @min_percent, @max_percent, @mean_percent, @last_used, @last_limit, @last_reset_utc,
             @first_observed_utc, @last_observed_utc, @ok_count, @unsupported_count, @auth_missing_count, @failed_count,
             @estimated_count, @coverage_seconds, @last_update_utc)
            ON CONFLICT(tenant_id, host_id, runtime, provider, account_key, window_name, model, hour_utc) DO UPDATE SET
                sample_count = excluded.sample_count,
                percent_sample_count = excluded.percent_sample_count,
                last_status = excluded.last_status,
                raw_pruned = excluded.raw_pruned,
                last_percent = excluded.last_percent,
                min_percent = excluded.min_percent,
                max_percent = excluded.max_percent,
                mean_percent = excluded.mean_percent,
                last_used = excluded.last_used,
                last_limit = excluded.last_limit,
                last_reset_utc = excluded.last_reset_utc,
                first_observed_utc = excluded.first_observed_utc,
                last_observed_utc = excluded.last_observed_utc,
                ok_count = excluded.ok_count,
                unsupported_count = excluded.unsupported_count,
                auth_missing_count = excluded.auth_missing_count,
                failed_count = excluded.failed_count,
                estimated_count = excluded.estimated_count,
                coverage_seconds = excluded.coverage_seconds,
                last_update_utc = excluded.last_update_utc;";

        internal const string RollupSelectById = "SELECT * FROM usage_hourly_rollups WHERE id = @id AND tenant_id = @tenant_id;";

        internal const string RollupDeleteOlderThan = "DELETE FROM usage_hourly_rollups WHERE hour_utc < @hour_before;";

        internal const string RollupDeleteByTenant = "DELETE FROM usage_hourly_rollups WHERE tenant_id = @tenant_id;";
    }
}
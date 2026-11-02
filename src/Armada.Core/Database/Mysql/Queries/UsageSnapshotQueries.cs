namespace Armada.Core.Database.Mysql.Queries
{
    /// <summary>
    /// Handwritten MySQL statements for usage capture storage: snapshots, snapshot-captain links,
    /// per-account latest state, account memberships and hourly rollups. Upserts use
    /// ON DUPLICATE KEY UPDATE against the unique indexes the migration creates, so an interrupted
    /// rollup job retried never accumulates a second copy of an hour.
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

        internal const string SnapshotDeleteOlderThan = "DELETE FROM usage_snapshots WHERE id IN (SELECT id FROM (SELECT id FROM usage_snapshots WHERE captured_utc < @captured_before ORDER BY captured_utc, id LIMIT 500) expired_usage_batch);";

        internal const string SnapshotCaptainsDeleteOlderThan = "DELETE FROM usage_snapshot_captains WHERE snapshot_id IN (SELECT id FROM (SELECT id FROM usage_snapshots WHERE captured_utc < @captured_before ORDER BY captured_utc, id LIMIT 500) expired_usage_batch);";

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
            ON DUPLICATE KEY UPDATE
                provider = VALUES(provider),
                last_good_snapshot_id = IF(VALUES(last_good_snapshot_id) IS NULL, last_good_snapshot_id, VALUES(last_good_snapshot_id)),
                last_good_captured_utc = IF(VALUES(last_good_snapshot_id) IS NULL, last_good_captured_utc, VALUES(last_good_captured_utc)),
                last_good_received_utc = IF(VALUES(last_good_snapshot_id) IS NULL, last_good_received_utc, VALUES(last_good_received_utc)),
                last_good_status = IF(VALUES(last_good_snapshot_id) IS NULL, last_good_status, VALUES(last_good_status)),
                last_good_windows_json = IF(VALUES(last_good_snapshot_id) IS NULL, last_good_windows_json, VALUES(last_good_windows_json)),
                last_error_snapshot_id = IF(VALUES(last_error_snapshot_id) IS NULL, last_error_snapshot_id, VALUES(last_error_snapshot_id)),
                last_error_captured_utc = IF(VALUES(last_error_snapshot_id) IS NULL, last_error_captured_utc, VALUES(last_error_captured_utc)),
                last_error_status = IF(VALUES(last_error_snapshot_id) IS NULL, last_error_status, VALUES(last_error_status)),
                last_error_code = IF(VALUES(last_error_snapshot_id) IS NULL, last_error_code, VALUES(last_error_code)),
                expired_utc = VALUES(expired_utc),
                last_update_utc = VALUES(last_update_utc);";

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
            ON DUPLICATE KEY UPDATE
                sample_count = VALUES(sample_count),
                percent_sample_count = VALUES(percent_sample_count),
                last_status = VALUES(last_status),
                raw_pruned = VALUES(raw_pruned),
                last_percent = VALUES(last_percent),
                min_percent = VALUES(min_percent),
                max_percent = VALUES(max_percent),
                mean_percent = VALUES(mean_percent),
                last_used = VALUES(last_used),
                last_limit = VALUES(last_limit),
                last_reset_utc = VALUES(last_reset_utc),
                first_observed_utc = VALUES(first_observed_utc),
                last_observed_utc = VALUES(last_observed_utc),
                ok_count = VALUES(ok_count),
                unsupported_count = VALUES(unsupported_count),
                auth_missing_count = VALUES(auth_missing_count),
                failed_count = VALUES(failed_count),
                estimated_count = VALUES(estimated_count),
                coverage_seconds = VALUES(coverage_seconds),
                last_update_utc = VALUES(last_update_utc);";

        internal const string RollupSelectById = "SELECT * FROM usage_hourly_rollups WHERE id = @id AND tenant_id = @tenant_id;";

        internal const string RollupDeleteOlderThan = "DELETE FROM usage_hourly_rollups WHERE hour_utc < @hour_before;";

        internal const string RollupDeleteByTenant = "DELETE FROM usage_hourly_rollups WHERE tenant_id = @tenant_id;";
    }
}
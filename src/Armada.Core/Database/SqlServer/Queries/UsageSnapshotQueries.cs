namespace Armada.Core.Database.SqlServer.Queries
{
    /// <summary>
    /// Handwritten SQL Server statements for usage capture storage: snapshots, snapshot-captain links,
    /// per-account latest state, account memberships and hourly rollups. Upserts use MERGE against the
    /// unique indexes the migration creates, so an interrupted rollup job retried never accumulates a
    /// second copy of an hour.
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

        internal const string SnapshotDeleteOlderThan = "DELETE FROM usage_snapshots WHERE id IN (SELECT TOP (500) id FROM usage_snapshots WHERE captured_utc < @captured_before ORDER BY captured_utc, id);";

        internal const string SnapshotCaptainsDeleteOlderThan = "DELETE FROM usage_snapshot_captains WHERE snapshot_id IN (SELECT TOP (500) id FROM usage_snapshots WHERE captured_utc < @captured_before ORDER BY captured_utc, id);";

        internal const string SnapshotDeleteByTenant = "DELETE FROM usage_snapshots WHERE tenant_id = @tenant_id;";

        internal const string SnapshotCaptainsDeleteByTenant = "DELETE FROM usage_snapshot_captains WHERE tenant_id = @tenant_id;";

        internal const string LatestStateUpsert = @"MERGE usage_latest_state WITH (HOLDLOCK) AS target
            USING (SELECT @tenant_id AS tenant_id, @host_id AS host_id, @runtime AS runtime, @account_key AS account_key) AS source
            ON target.tenant_id = source.tenant_id AND target.host_id = source.host_id AND target.runtime = source.runtime AND target.account_key = source.account_key
            WHEN MATCHED THEN UPDATE SET
                provider = @provider,
                last_good_snapshot_id = CASE WHEN @last_good_snapshot_id IS NULL THEN target.last_good_snapshot_id ELSE @last_good_snapshot_id END,
                last_good_captured_utc = CASE WHEN @last_good_snapshot_id IS NULL THEN target.last_good_captured_utc ELSE @last_good_captured_utc END,
                last_good_received_utc = CASE WHEN @last_good_snapshot_id IS NULL THEN target.last_good_received_utc ELSE @last_good_received_utc END,
                last_good_status = CASE WHEN @last_good_snapshot_id IS NULL THEN target.last_good_status ELSE @last_good_status END,
                last_good_windows_json = CASE WHEN @last_good_snapshot_id IS NULL THEN target.last_good_windows_json ELSE @last_good_windows_json END,
                last_error_snapshot_id = CASE WHEN @last_error_snapshot_id IS NULL THEN target.last_error_snapshot_id ELSE @last_error_snapshot_id END,
                last_error_captured_utc = CASE WHEN @last_error_snapshot_id IS NULL THEN target.last_error_captured_utc ELSE @last_error_captured_utc END,
                last_error_status = CASE WHEN @last_error_snapshot_id IS NULL THEN target.last_error_status ELSE @last_error_status END,
                last_error_code = CASE WHEN @last_error_snapshot_id IS NULL THEN target.last_error_code ELSE @last_error_code END,
                expired_utc = @expired_utc,
                last_update_utc = @last_update_utc
            WHEN NOT MATCHED THEN INSERT
                (id, tenant_id, host_id, runtime, provider, account_key,
                 last_good_snapshot_id, last_good_captured_utc, last_good_received_utc, last_good_status, last_good_windows_json,
                 last_error_snapshot_id, last_error_captured_utc, last_error_status, last_error_code,
                 expired_utc, last_update_utc)
            VALUES
                (@id, @tenant_id, @host_id, @runtime, @provider, @account_key,
                 @last_good_snapshot_id, @last_good_captured_utc, @last_good_received_utc, @last_good_status, @last_good_windows_json,
                 @last_error_snapshot_id, @last_error_captured_utc, @last_error_status, @last_error_code,
                 @expired_utc, @last_update_utc);";

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

        internal const string RollupUpsert = @"MERGE usage_hourly_rollups WITH (HOLDLOCK) AS target
            USING (SELECT @tenant_id AS tenant_id, @host_id AS host_id, @runtime AS runtime, @provider AS provider, @account_key AS account_key,
                          @window_name AS window_name, @model AS model, @hour_utc AS hour_utc) AS source
            ON target.tenant_id = source.tenant_id AND target.host_id = source.host_id AND target.runtime = source.runtime
               AND target.provider = source.provider AND target.account_key = source.account_key AND target.window_name = source.window_name
               AND target.model = source.model AND target.hour_utc = source.hour_utc
            WHEN MATCHED THEN UPDATE SET
                sample_count = @sample_count,
                last_percent = @last_percent,
                min_percent = @min_percent,
                max_percent = @max_percent,
                mean_percent = @mean_percent,
                last_used = @last_used,
                last_limit = @last_limit,
                last_reset_utc = @last_reset_utc,
                first_observed_utc = @first_observed_utc,
                last_observed_utc = @last_observed_utc,
                ok_count = @ok_count,
                unsupported_count = @unsupported_count,
                auth_missing_count = @auth_missing_count,
                failed_count = @failed_count,
                estimated_count = @estimated_count,
                coverage_seconds = @coverage_seconds,
                last_update_utc = @last_update_utc
            WHEN NOT MATCHED THEN INSERT
                (id, tenant_id, host_id, runtime, provider, account_key, window_name, model, hour_utc,
                 sample_count, last_percent, min_percent, max_percent, mean_percent, last_used, last_limit, last_reset_utc,
                 first_observed_utc, last_observed_utc, ok_count, unsupported_count, auth_missing_count, failed_count,
                 estimated_count, coverage_seconds, last_update_utc)
            VALUES
                (@id, @tenant_id, @host_id, @runtime, @provider, @account_key, @window_name, @model, @hour_utc,
                 @sample_count, @last_percent, @min_percent, @max_percent, @mean_percent, @last_used, @last_limit, @last_reset_utc,
                 @first_observed_utc, @last_observed_utc, @ok_count, @unsupported_count, @auth_missing_count, @failed_count,
                 @estimated_count, @coverage_seconds, @last_update_utc);";

        internal const string RollupSelectById = "SELECT * FROM usage_hourly_rollups WHERE id = @id AND tenant_id = @tenant_id;";

        internal const string RollupDeleteOlderThan = "DELETE FROM usage_hourly_rollups WHERE hour_utc < @hour_before;";

        internal const string RollupDeleteByTenant = "DELETE FROM usage_hourly_rollups WHERE tenant_id = @tenant_id;";
    }
}
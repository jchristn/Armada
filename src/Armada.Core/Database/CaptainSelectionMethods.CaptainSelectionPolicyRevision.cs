namespace Armada.Core.Database
{
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    internal sealed partial class CaptainSelectionMethods
    {
        private static async Task InsertCaptainSelectionPolicyRevisionAsync(DbConnection connection, DbTransaction transaction, CaptainSelectionPolicyRevision row, CancellationToken token)
        {
            using DbCommand command = Command(connection, transaction, "INSERT INTO captain_selection_policy_revisions(id,tenant_id,user_id,created_utc,last_update_utc,active,fingerprint,algorithm_version,mode,strategy,unknown_usage_behavior,reserve_percent,comfortable_headroom_percent,per_attempt_reservation_percent,maximum_freshness_seconds,unknown_retry_seconds,provider_cooldown_seconds,settlement_lag_seconds,maximum_unsettled_seconds,decision_retention_days,maximum_candidates) VALUES(@id,@tenant_id,@user_id,@created_utc,@last_update_utc,@active,@fingerprint,@algorithm_version,@mode,@strategy,@unknown_usage_behavior,@reserve_percent,@comfortable_headroom_percent,@per_attempt_reservation_percent,@maximum_freshness_seconds,@unknown_retry_seconds,@provider_cooldown_seconds,@settlement_lag_seconds,@maximum_unsettled_seconds,@decision_retention_days,@maximum_candidates);");
            Add(command, "@id", row.Id);
            Add(command, "@tenant_id", row.TenantId);
            Add(command, "@user_id", row.UserId);
            Add(command, "@created_utc", Utc(row.CreatedUtc));
            Add(command, "@last_update_utc", Utc(row.LastUpdateUtc));
            Add(command, "@active", row.Active ? 1 : 0);
            Add(command, "@fingerprint", row.Fingerprint);
            Add(command, "@algorithm_version", row.AlgorithmVersion);
            Add(command, "@mode", row.Mode.ToString());
            Add(command, "@strategy", row.Strategy.ToString());
            Add(command, "@unknown_usage_behavior", row.UnknownUsageBehavior.ToString());
            Add(command, "@reserve_percent", row.ReservePercent);
            Add(command, "@comfortable_headroom_percent", row.ComfortableHeadroomPercent);
            Add(command, "@per_attempt_reservation_percent", row.PerAttemptReservationPercent);
            Add(command, "@maximum_freshness_seconds", row.MaximumFreshnessSeconds);
            Add(command, "@unknown_retry_seconds", row.UnknownRetrySeconds);
            Add(command, "@provider_cooldown_seconds", row.ProviderCooldownSeconds);
            Add(command, "@settlement_lag_seconds", row.SettlementLagSeconds);
            Add(command, "@maximum_unsettled_seconds", row.MaximumUnsettledSeconds);
            Add(command, "@decision_retention_days", row.DecisionRetentionDays);
            Add(command, "@maximum_candidates", row.MaximumCandidates);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        private static CaptainSelectionPolicyRevision MapCaptainSelectionPolicyRevision(DbDataReader reader)
        {
            return new CaptainSelectionPolicyRevision
            {
                Id = Convert.ToString(reader["id"], System.Globalization.CultureInfo.InvariantCulture)!,
                TenantId = Convert.ToString(reader["tenant_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                UserId = reader["user_id"] == DBNull.Value ? null : Convert.ToString(reader["user_id"], System.Globalization.CultureInfo.InvariantCulture)!,
                CreatedUtc = ParseUtc(reader["created_utc"]),
                LastUpdateUtc = ParseUtc(reader["last_update_utc"]),
                Active = Convert.ToInt32(reader["active"], System.Globalization.CultureInfo.InvariantCulture) != 0,
                Fingerprint = Convert.ToString(reader["fingerprint"], System.Globalization.CultureInfo.InvariantCulture)!,
                AlgorithmVersion = Convert.ToString(reader["algorithm_version"], System.Globalization.CultureInfo.InvariantCulture)!,
                Mode = Enum.Parse<UsageSelectionModeEnum>(Convert.ToString(reader["mode"], System.Globalization.CultureInfo.InvariantCulture)!, false),
                Strategy = Enum.Parse<CaptainSelectionStrategyEnum>(Convert.ToString(reader["strategy"], System.Globalization.CultureInfo.InvariantCulture)!, false),
                UnknownUsageBehavior = Enum.Parse<UsageUnknownBehaviorEnum>(Convert.ToString(reader["unknown_usage_behavior"], System.Globalization.CultureInfo.InvariantCulture)!, false),
                ReservePercent = Convert.ToDouble(reader["reserve_percent"], System.Globalization.CultureInfo.InvariantCulture),
                ComfortableHeadroomPercent = Convert.ToDouble(reader["comfortable_headroom_percent"], System.Globalization.CultureInfo.InvariantCulture),
                PerAttemptReservationPercent = Convert.ToDouble(reader["per_attempt_reservation_percent"], System.Globalization.CultureInfo.InvariantCulture),
                MaximumFreshnessSeconds = Convert.ToInt32(reader["maximum_freshness_seconds"], System.Globalization.CultureInfo.InvariantCulture),
                UnknownRetrySeconds = Convert.ToInt32(reader["unknown_retry_seconds"], System.Globalization.CultureInfo.InvariantCulture),
                ProviderCooldownSeconds = Convert.ToInt32(reader["provider_cooldown_seconds"], System.Globalization.CultureInfo.InvariantCulture),
                SettlementLagSeconds = Convert.ToInt32(reader["settlement_lag_seconds"], System.Globalization.CultureInfo.InvariantCulture),
                MaximumUnsettledSeconds = Convert.ToInt32(reader["maximum_unsettled_seconds"], System.Globalization.CultureInfo.InvariantCulture),
                DecisionRetentionDays = Convert.ToInt32(reader["decision_retention_days"], System.Globalization.CultureInfo.InvariantCulture),
                MaximumCandidates = Convert.ToInt32(reader["maximum_candidates"], System.Globalization.CultureInfo.InvariantCulture),
            };
        }
    }
}
